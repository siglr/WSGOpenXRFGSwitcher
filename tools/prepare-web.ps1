param([string]$SharedWeb = 'C:\WSG Stream Feeder\web')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$web = Join-Path $project 'web'
$destination = Join-Path $project 'dist\website'
$package = [IO.File]::ReadAllText((Join-Path $project 'dist\latest-package.txt')).Trim()
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackageDirectory $package

# The Stream Feeder project owns the shared tools index and stylesheet.
# Keep its source and staged HTML asset tags in sync for future uploads.
foreach ($name in @('index.html', 'WSG-Stream-Feeder.html')) {
    $path = Join-Path $SharedWeb $name
    $html = [IO.File]::ReadAllText($path)
    $html = [regex]::Replace($html, '(?<prefix>(?:src|href)=")(?<asset>tools\.css|feeder\.js|assets/[^"?]+\.png)(?:\?v=[a-f0-9]+)?"', [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        $asset = $match.Groups['asset'].Value
        $hash = (Get-FileHash -LiteralPath (Join-Path $SharedWeb $asset) -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0,16)
        return $match.Groups['prefix'].Value + $asset + '?v=' + $hash + '"'
    })
    [IO.File]::WriteAllText($path, $html)
}
New-Item -ItemType Directory -Path (Join-Path $destination 'assets') -Force | Out-Null
foreach ($name in @('index.html', 'tools.css')) {
    Copy-Item -LiteralPath (Join-Path $SharedWeb $name) -Destination $destination -Force
}
foreach ($name in @('wsg-logo.png', 'glass-cockpit-refresh.png', 'wsg-stream-feeder.png')) {
    Copy-Item -LiteralPath (Join-Path $SharedWeb ('assets\' + $name)) -Destination (Join-Path $destination 'assets') -Force
}
foreach ($name in @('openxr.css', 'openxr.js', 'UPLOAD-INSTRUCTIONS.txt')) {
    Copy-Item -LiteralPath (Join-Path $web $name) -Destination $destination -Force
}
Copy-Item -LiteralPath (Join-Path $web 'assets\wsg-openxr-fg-switcher.png') -Destination (Join-Path $destination 'assets') -Force
$path = Join-Path $web 'WSG-OpenXR-FG-Switcher.html'
$html = [IO.File]::ReadAllText($path)
$html = [regex]::Replace($html, '(?<prefix>(?:src|href)=")(?<asset>tools\.css|openxr\.(?:css|js)|assets/[^"?]+\.png)(?:\?v=[a-f0-9]+)?"', [Text.RegularExpressions.MatchEvaluator]{
    param($match)
    $asset = $match.Groups['asset'].Value
    $hash = (Get-FileHash -LiteralPath (Join-Path $destination $asset) -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0,16)
    return $match.Groups['prefix'].Value + $asset + '?v=' + $hash + '"'
})
[IO.File]::WriteAllText($path, $html)
[IO.File]::WriteAllText((Join-Path $destination 'WSG-OpenXR-FG-Switcher.html'), $html)
# Fixed website filenames, with the exact verified ZIP and signed metadata.
# Re-running replaces the same files rather than accumulating dated copies.
Copy-Item -LiteralPath ($package + '.zip') -Destination (Join-Path $destination 'WSG-OpenXR-FG-Switcher.zip') -Force
Copy-Item -LiteralPath ($package + '.version.json') -Destination (Join-Path $destination 'WSG-OpenXR-FG-Switcher.version.json') -Force
Copy-Item -LiteralPath (Join-Path $project 'assets/catalog.legacy.signed.json') -Destination (Join-Path $destination 'WSG-OpenXR-FG-Switcher.providers.json') -Force
Copy-Item -LiteralPath (Join-Path $project 'assets/catalog.signed.json') -Destination (Join-Path $destination 'WSG-OpenXR-FG-Switcher.providers-v2.json') -Force
[IO.File]::WriteAllText((Join-Path $destination 'WSG-OpenXR-FG-Switcher.zip.sha256'), (Get-FileHash -LiteralPath ($package + '.zip')).Hash.ToLowerInvariant() + '  WSG-OpenXR-FG-Switcher.zip' + [char]10)
Write-Output "Website upload files prepared: $destination"
