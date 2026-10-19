param([string]$KeyFile = (Join-Path $env:LOCALAPPDATA 'WSG OpenXR FG Switcher\signing\release-key.dpapi'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'signing-key.ps1')
$root = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\')
$keyPath = [IO.Path]::GetFullPath($KeyFile)
$publicPath = Join-Path $root 'src/Updates/ReleasePublicKey.cs'
$existing = [IO.File]::ReadAllText($publicPath)
$established = -not $existing.StartsWith('// Local development identity;')
if ($keyPath.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetExtension($keyPath) -ne '.dpapi') { throw 'Store the protected release key outside the repository in a .dpapi file.' }
if (Test-Path -LiteralPath $keyPath) { $rsa = Import-WsgSigningKey $keyPath }
else {
    if ($established) { throw 'An established release identity exists. Restore its protected key; do not generate a replacement.' }
    $rsa = [Security.Cryptography.RSA]::Create(3072)
    $bytes = $rsa.ExportPkcs8PrivateKey()
    try {
        $protected = [Security.Cryptography.ProtectedData]::Protect($bytes, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($keyPath)) | Out-Null
        $stream = [IO.File]::Open($keyPath, [IO.FileMode]::CreateNew)
        try { $stream.Write($protected, 0, $protected.Length) } finally { $stream.Dispose() }
    } finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}
try {
    if ($established -and -not $existing.Contains($rsa.ToXmlString($false))) { throw 'This key does not match the established release identity.' }
    [IO.File]::WriteAllText((Join-Path $root 'assets/trust-public.pem'), $rsa.ExportSubjectPublicKeyInfoPem())
    $source = '// Public release identity. Keep the private key outside the repository.' + [Environment]::NewLine +
        'internal static class ReleasePublicKey { internal const string Xml = "' + $rsa.ToXmlString($false) + '"; }'
    [IO.File]::WriteAllText((Join-Path $root 'src/Updates/ReleasePublicKey.cs'), $source)
} finally { $rsa.Dispose() }
Write-Output 'Release public identity ready; private key protected for the current Windows user.'
