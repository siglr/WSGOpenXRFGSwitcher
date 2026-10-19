param([switch]$SkipTests, [switch]$OfficialRelease)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
Push-Location $taskRoot
try {
    $taskDotnet = Join-Path $taskRoot '.tools/dotnet/dotnet.exe'
    if (-not (Test-Path $taskDotnet)) { $taskDotnet = (Get-Command dotnet -ErrorAction Stop).Source }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    foreach ($taskProject in @('src/Switcher.csproj','helper/LayerHelper.csproj','updater/Updater.csproj')) {
        & $taskDotnet build $taskProject -c Release --nologo -v:q "-p:OfficialRelease=$($OfficialRelease.IsPresent)"
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $taskProject" }
    }
    if (-not $SkipTests) {
        & $taskDotnet run --project tests/Tests.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
        & $taskDotnet publish updater-tests/UpdaterTests.csproj -c Release -o build/update-tests --nologo -v:q
        if ($LASTEXITCODE -ne 0) { throw 'Updater test build failed.' }
        & build/update-tests/Wsg.OpenXR.UpdateTests.exe
        if ($LASTEXITCODE -ne 0) { throw 'Updater tests failed.' }
    }
} finally { Pop-Location }
