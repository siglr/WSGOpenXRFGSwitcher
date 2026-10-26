param([Parameter(Mandatory=$true)][string]$SigningKey,
      [Parameter(Mandatory=$true)][long]$Revision)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$reviewed = Join-Path $root 'assets/catalog.reviewed.json'
$document = Get-Content -LiteralPath $reviewed -Raw | ConvertFrom-Json
if ($Revision -le 0 -or ($document.Revision -and $Revision -lt $document.Revision)) { throw 'Use a positive, increasing catalog revision.' }
$destination = Join-Path $root 'dist/provider-catalog'
$target = Join-Path $destination 'WSG-OpenXR-FG-Switcher.providers-v2.json'
foreach ($previousTarget in @($target, (Join-Path $destination 'WSG-OpenXR-FG-Switcher.providers.json'))) {
if (Test-Path -LiteralPath $previousTarget) {
    $previous = Get-Content -LiteralPath $previousTarget -Raw | ConvertFrom-Json
    $previousDocument = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($previous.Payload)) | ConvertFrom-Json
    if ($Revision -le $previousDocument.Revision) { throw 'Increment the revision before publishing another provider catalog.' }
}
}
$document | Add-Member NoteProperty Revision $Revision -Force
[IO.File]::WriteAllText($reviewed, ($document | ConvertTo-Json -Depth 12) + [Environment]::NewLine)
& (Join-Path $PSScriptRoot 'build-catalog.ps1') -PrivateKeyPath $SigningKey
[IO.Directory]::CreateDirectory($destination) | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'assets/catalog.signed.json') -Destination $target -Force
Copy-Item -LiteralPath (Join-Path $root 'assets/catalog.legacy.signed.json') -Destination (Join-Path $destination 'WSG-OpenXR-FG-Switcher.providers.json') -Force
Write-Output ('Signed provider catalog ready: ' + $target)
Write-Output 'Upload this file to /otherdata/tools/ after its upstream packages and compatibility have been reviewed. No WSG rebuild is required.'
