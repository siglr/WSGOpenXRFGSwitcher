param([Parameter(Mandatory=$true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskPackage = (Resolve-Path $PackageDirectory).Path
$taskPublic = [IO.File]::ReadAllText((Join-Path $taskRoot 'assets/trust-public.pem'))
$taskRsa = [Security.Cryptography.RSA]::Create()
$taskRsa.ImportFromPem($taskPublic)
$taskInventory = Get-Content (Join-Path $taskPackage 'app-files.json') -Raw | ConvertFrom-Json
$taskNl = [char]10
$taskMessage = 'WSGOpenXrFgSwitcher inventory v1' + $taskNl + $taskInventory.product + $taskNl + $taskInventory.version + $taskNl
$taskNames = [string[]]$taskInventory.files.PSObject.Properties.Name
[Array]::Sort($taskNames,[StringComparer]::Ordinal)
foreach ($taskName in $taskNames) {
    if ($taskName -match '[/\\:]' -or $taskName -in @('.','..')) { throw 'Unsafe inventory entry.' }
    $taskHash = $taskInventory.files.$taskName
    if ((Get-FileHash (Join-Path $taskPackage $taskName) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $taskHash) { throw "Changed package file: $taskName" }
    $taskMessage += $taskName + [char]0 + $taskHash + $taskNl
}
if ($taskInventory.product -ne 'WSG-OpenXR-FG-Switcher' -or -not $taskRsa.VerifyData([Text.Encoding]::UTF8.GetBytes($taskMessage),[Convert]::FromBase64String($taskInventory.signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw 'Inventory signature is invalid.' }
if (@(Get-ChildItem $taskPackage -File).Count -ne $taskNames.Length+1) { throw 'Package contains unlisted files.' }
$taskFeed = Get-Content ($taskPackage+'.version.json') -Raw | ConvertFrom-Json
$taskArchive = $taskPackage+'.zip'
if ($taskFeed.product -ne $taskInventory.product -or $taskFeed.version -ne $taskInventory.version -or (Get-FileHash $taskArchive).Hash.ToLowerInvariant() -ne $taskFeed.sha256 -or (Get-Item $taskArchive).Length -ne $taskFeed.size) { throw 'Archive metadata mismatch.' }
$taskMessage = 'WSGOpenXrFgSwitcher release v1' + $taskNl + $taskFeed.product + $taskNl + $taskFeed.version + $taskNl + $taskFeed.sha256 + $taskNl + $taskFeed.size + $taskNl
if (-not $taskRsa.VerifyData([Text.Encoding]::UTF8.GetBytes($taskMessage),[Convert]::FromBase64String($taskFeed.signature),[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)) { throw 'Release signature is invalid.' }
$taskRsa.Dispose()
Write-Output ("Verified product identity, release signature, archive hash, and "+$taskNames.Length+" managed file hashes.")
