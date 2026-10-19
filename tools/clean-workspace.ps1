[CmdletBinding(SupportsShouldProcess)]
param()
$ErrorActionPreference = 'Stop'
$cleanupRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$cleanupCmdlet = $PSCmdlet
$script:cleanupBytes = 0L

function Assert-WorkspacePath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($cleanupRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Cleanup target escapes workspace: $resolved" }
    $cursor = $resolved
    while ($cursor -and $cursor.Length -ge $cleanupRoot.Length) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Cleanup refuses linked paths: $cursor" }
        }
        $cursor = Split-Path $cursor -Parent
    }
    return $resolved
}

# Fail closed if the running-process inventory is unavailable. Never stop a process.
$cleanupRunning = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object ExecutablePath | Select-Object -ExpandProperty ExecutablePath)
function Test-InUse([string]$Path) {
    return @($cleanupRunning | Where-Object { $_.Equals($Path, [StringComparison]::OrdinalIgnoreCase) -or $_.StartsWith($Path.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
}
function Remove-Generated([string]$Path) {
    $target = Assert-WorkspacePath $Path
    if (-not (Test-Path -LiteralPath $target)) { return }
    if (Test-InUse $target) { Write-Warning "Keeping running build: $target"; return }
    $item = Get-Item -LiteralPath $target -Force
    $size = $item.Length
    if ($item.PSIsContainer) {
        $children = @(Get-ChildItem -LiteralPath $target -Recurse -Force)
        if (@($children | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) { throw "Cleanup refuses a directory containing links: $target" }
        $size = ($children | Where-Object { -not $_.PSIsContainer } | Measure-Object Length -Sum).Sum
    }
    if ($cleanupCmdlet.ShouldProcess($target, 'Remove generated artifact')) {
        try { Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop; $script:cleanupBytes += $size }
        catch { Write-Warning "Kept artifact that could not be removed: $target ($($_.Exception.Message))" }
    }
}

$dist = Assert-WorkspacePath (Join-Path $cleanupRoot 'dist')
$marker = Assert-WorkspacePath (Join-Path $dist 'latest-package.txt')
$latest = Assert-WorkspacePath ([IO.File]::ReadAllText($marker).Trim())
if ((Split-Path $latest -Parent) -ne $dist -or (Split-Path $latest -Leaf) -notmatch '^package-\d+\.\d+\.\d+-\d{8}-\d{6}$') { throw 'Unexpected latest-package target; cleanup cancelled.' }
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackageDirectory $latest
foreach ($package in Get-ChildItem -LiteralPath $dist -Directory) {
    if ($package.FullName -eq $latest -or $package.Name -notmatch '^package-\d+\.\d+\.\d+-\d{8}-\d{6}$') { continue }
    $target = Assert-WorkspacePath $package.FullName
    if (Test-InUse $target) { Write-Warning "Keeping running package and its ZIP: $target"; continue }
    try {
        # Preserve modified packages or folders containing user-added files.
        if (@(Get-ChildItem -LiteralPath $target -Directory).Count) { throw 'Unexpected package subdirectory.' }
        & (Join-Path $PSScriptRoot 'verify-package.ps1') -PackageDirectory $target
    } catch { Write-Warning "Keeping unverified or modified package: $target"; continue }
    Remove-Generated $target
    # If an open file prevented deletion, keep its matching distribution artifacts too.
    if ($WhatIfPreference -or -not (Test-Path -LiteralPath $target)) {
        Remove-Generated ($target + '.zip')
        Remove-Generated ($target + '.version.json')
    }
}

foreach ($project in @('src','core','helper','tests','updater','updater-tests')) {
    foreach ($output in @('bin','obj')) { Remove-Generated (Join-Path $cleanupRoot "$project/$output") }
}
Remove-Generated (Join-Path $cleanupRoot 'build/update-tests')
Remove-Generated (Join-Path $cleanupRoot '.tools/dotnet-sdk.zip')
Remove-Generated (Join-Path $cleanupRoot '.tools/redesign.py')

# Keep audit evidence, without accumulating dozens of loose logs and screenshots.
$build = Assert-WorkspacePath (Join-Path $cleanupRoot 'build')
$history = Assert-WorkspacePath (Join-Path $build 'validation-history.zip')
if (Test-Path -LiteralPath $build) {
    $evidence = @(Get-ChildItem -LiteralPath $build -Recurse -File | Where-Object { $_.Extension -in @('.log','.json','.png','.csv') })
    foreach ($file in $evidence) {
        $path = Assert-WorkspacePath $file.FullName
        if (-not $cleanupCmdlet.ShouldProcess($path, 'Archive validation evidence, then remove loose copy')) { continue }
        try {
            # Leave a log alone if a build or caller still has it open for writing.
            $evidenceInput = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
            $buffer = [IO.MemoryStream]::new()
            try { $evidenceInput.CopyTo($buffer); $bytes = $buffer.ToArray() } finally { $evidenceInput.Dispose(); $buffer.Dispose() }
            $entryName = [IO.Path]::GetRelativePath($build, $path).Replace('\','/')
            $archive = [IO.Compression.ZipFile]::Open($history, [IO.Compression.ZipArchiveMode]::Update)
            try {
                $entry = $archive.GetEntry($entryName)
                if ($entry) { $entry.Delete() }
                $entry = $archive.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
                $stream = $entry.Open()
                try { $stream.Write($bytes) } finally { $stream.Dispose() }
            } finally { $archive.Dispose() }
            # Reopen the saved archive and verify bytes before deleting any original.
            $archive = [IO.Compression.ZipFile]::OpenRead($history)
            try {
                $stream = $archive.GetEntry($entryName).Open(); $copy = [IO.MemoryStream]::new()
                try { $stream.CopyTo($copy); $saved = $copy.ToArray() } finally { $stream.Dispose(); $copy.Dispose() }
            } finally { $archive.Dispose() }
            $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
            if ($hash -ne [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($saved)) -or $hash -ne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash) { throw 'Evidence changed or archive verification failed.' }
            Remove-Generated $path
        } catch { Write-Warning "Keeping evidence: $path ($($_.Exception.Message))" }
    }
    foreach ($directory in Get-ChildItem -LiteralPath $build -Directory -Recurse | Sort-Object { $_.FullName.Length } -Descending) {
        if (-not @(Get-ChildItem -LiteralPath (Assert-WorkspacePath $directory.FullName) -Force).Count) { Remove-Generated $directory.FullName }
    }
}
Write-Output ('Cleanup removed {0:N1} MiB of generated files. Latest package kept: {1}' -f ($script:cleanupBytes / 1MB), $latest)
