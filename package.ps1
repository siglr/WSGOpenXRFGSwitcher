param([string]$SigningKey, [switch]$OfficialRelease, [switch]$SkipTests,
      [string]$AuthenticodeCertificateThumbprint, [string]$TimestampServer)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
Push-Location $taskRoot
try {
    if ($OfficialRelease -and [string]::IsNullOrWhiteSpace($SigningKey)) { throw 'Official publishing requires an explicitly supplied product signing key.' }
    if ([string]::IsNullOrWhiteSpace($SigningKey)) { $SigningKey = Join-Path $taskRoot '.tools/signing.private.xml' }
    if ($OfficialRelease -and [IO.Path]::GetExtension($SigningKey) -ne '.dpapi') { throw 'Official releases require the protected product release key.' }
    . (Join-Path $taskRoot 'tools/signing-key.ps1')
    $taskRsa = Import-WsgSigningKey $SigningKey
    if ($taskRsa.ExportSubjectPublicKeyInfoPem() -ne [IO.File]::ReadAllText((Join-Path $taskRoot 'assets/trust-public.pem'))) { throw 'The signing identity does not match this app and catalog. Update the public identity and rebuild the catalog first.' }
    $taskSourceKey = Get-Content 'src/Updates/ReleasePublicKey.cs' -Raw
    if (-not $taskSourceKey.Contains($taskRsa.ToXmlString($false))) { throw 'Updater public identity does not match.' }
    & ./build.ps1 -SkipTests:$SkipTests -OfficialRelease:$OfficialRelease
    $taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
    if (-not (Test-Path $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
    [xml]$taskProps = Get-Content Directory.Build.props
    $taskVersion = [string]$taskProps.Project.PropertyGroup.Version
    $taskName = 'WSG OpenXR FG Switcher'
    $taskPackage = Join-Path $taskRoot ('dist/package-' + $taskVersion + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    if (Test-Path $taskPackage) { throw 'Package destination already exists; preserving it.' }
    New-Item -ItemType Directory -Path $taskPackage -Force | Out-Null
    foreach ($taskProject in @('src/Switcher.csproj','helper/LayerHelper.csproj','updater/Updater.csproj')) {
        & $taskDotnet publish $taskProject -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false "-p:OfficialRelease=$($OfficialRelease.IsPresent)" -o $taskPackage --nologo -v:q
        if ($LASTEXITCODE -ne 0) { throw "Publishing failed: $taskProject" }
    }
    if (Get-ChildItem $taskPackage -Directory) { throw 'Unexpected runtime subdirectories. Inspect packaging before signing.' }
    $taskAppDlls = @('WSG OpenXR FG Switcher.dll','WSG OpenXR FG Switcher.LayerHelper.dll','WSG OpenXR FG Switcher.Updater.dll','Wsg.OpenXR.Core.dll')
    foreach ($taskDll in Get-ChildItem -LiteralPath $taskPackage -Filter '*.dll') {
        if ($taskDll.Name -notin $taskAppDlls) { throw "Unexpected DLL in app-only package: $($taskDll.Name)" }
    }
    foreach ($taskConfig in Get-ChildItem -LiteralPath $taskPackage -Filter '*.runtimeconfig.json') {
        $taskRuntime = (Get-Content -LiteralPath $taskConfig.FullName -Raw | ConvertFrom-Json).runtimeOptions
        $taskFrameworks = @($taskRuntime.framework) + @($taskRuntime.frameworks) | Where-Object { $null -ne $_ }
        if ($taskRuntime.includedFrameworks -or $taskFrameworks.Count -eq 0 -or @($taskFrameworks | Where-Object { $_.version -notmatch '^10\.0\.' }).Count) { throw 'Package must require the shared .NET 10 runtime.' }
        if ($taskConfig.Name -eq 'WSG OpenXR FG Switcher.runtimeconfig.json' -and 'Microsoft.WindowsDesktop.App' -notin $taskFrameworks.name) { throw 'Desktop runtime requirement is missing.' }
    }
    foreach ($taskFile in @('README.md','NOTICE.md','LICENSE.md')) { Copy-Item -LiteralPath $taskFile -Destination $taskPackage }
    Copy-Item -LiteralPath 'notices/UPDATER-LICENSE.md' -Destination $taskPackage
    if ($AuthenticodeCertificateThumbprint) {
        if (-not $TimestampServer) { throw 'Supply the timestamp server required by your certificate provider.' }
        $taskCertificate = Get-Item -LiteralPath ('Cert:/CurrentUser/My/' + $AuthenticodeCertificateThumbprint)
        foreach ($taskBinary in Get-ChildItem $taskPackage -File | Where-Object { $_.Extension -in @('.exe','.dll') -and ($_.Name.StartsWith('WSG OpenXR') -or $_.Name -eq 'Wsg.OpenXR.Core.dll') }) {
            $taskSignatureResult = Set-AuthenticodeSignature -LiteralPath $taskBinary.FullName -Certificate $taskCertificate -HashAlgorithm SHA256 -TimestampServer $TimestampServer
            if ($taskSignatureResult.Status -ne 'Valid') { throw 'Authenticode signing failed.' }
        }
    }
    [IO.File]::WriteAllText((Join-Path $taskPackage 'VERSION'), $taskVersion + [char]10)
    $taskInventory = [ordered]@{}
    $taskNames = [string[]](Get-ChildItem $taskPackage -File | Select-Object -ExpandProperty Name)
    [Array]::Sort($taskNames,[StringComparer]::Ordinal)
    foreach ($taskFile in $taskNames) { $taskInventory[$taskFile] = (Get-FileHash (Join-Path $taskPackage $taskFile) -Algorithm SHA256).Hash.ToLowerInvariant() }
    $taskProduct = 'WSG-OpenXR-FG-Switcher'
    $taskNl = [char]10
    $taskMessage = 'WSGOpenXrFgSwitcher inventory v1' + $taskNl + $taskProduct + $taskNl + $taskVersion + $taskNl
    foreach ($taskFile in $taskNames) { $taskMessage += $taskFile + [char]0 + $taskInventory[$taskFile] + $taskNl }
    $taskPublic = $taskRsa.ToXmlString($false)
    $taskKeyId = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($taskPublic))).ToLowerInvariant()
    $taskSignature = [Convert]::ToBase64String($taskRsa.SignData([Text.Encoding]::UTF8.GetBytes($taskMessage),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1))
    [IO.File]::WriteAllText((Join-Path $taskPackage 'app-files.json'), (@{schema=1;product=$taskProduct;version=$taskVersion;files=$taskInventory;keyId=$taskKeyId;signature=$taskSignature}|ConvertTo-Json -Depth 5))
    $taskArchive = $taskPackage + '.zip'
    $taskZip = [IO.Compression.ZipFile]::Open($taskArchive,[IO.Compression.ZipArchiveMode]::Create)
    try { foreach ($taskFile in Get-ChildItem $taskPackage -File) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($taskZip,$taskFile.FullName,$taskFile.Name,[IO.Compression.CompressionLevel]::Optimal)|Out-Null } }
    finally { $taskZip.Dispose() }
    $taskHash = (Get-FileHash $taskArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    $taskSize = (Get-Item $taskArchive).Length
    $taskMessage = 'WSGOpenXrFgSwitcher release v1' + $taskNl + $taskProduct + $taskNl + $taskVersion + $taskNl + $taskHash + $taskNl + $taskSize + $taskNl
    $taskSignature = [Convert]::ToBase64String($taskRsa.SignData([Text.Encoding]::UTF8.GetBytes($taskMessage),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1))
    [IO.File]::WriteAllText(($taskPackage + '.version.json'), (@{schema=1;product=$taskProduct;version=$taskVersion;sha256=$taskHash;size=$taskSize;keyId=$taskKeyId;signature=$taskSignature}|ConvertTo-Json))
    $taskRsa.Dispose()
    & ./tools/verify-package.ps1 -PackageDirectory $taskPackage
    [IO.File]::WriteAllText((Join-Path $taskRoot 'dist/latest-package.txt'),$taskPackage)
    Write-Output ("Package: " + $taskPackage)
    Write-Output ("Archive: " + $taskArchive)
    Write-Output ("SHA256: " + $taskHash)
    & ./tools/clean-workspace.ps1
} finally { Pop-Location }
