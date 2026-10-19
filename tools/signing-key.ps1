# Build-time only. Private key material never enters the app or release package.
function Import-WsgSigningKey([string]$Path) {
    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $rsa = [Security.Cryptography.RSA]::Create()
    try {
        if ([IO.Path]::GetExtension($resolved) -eq '.dpapi') {
            $bytes = [Security.Cryptography.ProtectedData]::Unprotect(
                [IO.File]::ReadAllBytes($resolved), $null,
                [Security.Cryptography.DataProtectionScope]::CurrentUser)
            try { $rsa.ImportPkcs8PrivateKey($bytes, [ref]$null) }
            finally { [Array]::Clear($bytes, 0, $bytes.Length) }
        } else {
            $rsa.FromXmlString([IO.File]::ReadAllText($resolved))
        }
        if ($rsa.KeySize -lt 3072) { throw 'A signing key of at least 3072 bits is required.' }
        return $rsa
    } catch { $rsa.Dispose(); throw }
}
