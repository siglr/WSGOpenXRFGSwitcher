param([Parameter(Mandatory=$true)][string]$PrivateKeyPath,
      [string]$ReviewedJson = (Join-Path (Split-Path $PSScriptRoot -Parent) 'assets/catalog.reviewed.json'))
$ErrorActionPreference = 'Stop'
$taskCatalogRoot = Split-Path $PSScriptRoot -Parent
$taskDocument = Get-Content -LiteralPath $ReviewedJson -Raw | ConvertFrom-Json
. (Join-Path $PSScriptRoot 'signing-key.ps1')
$taskRsa = Import-WsgSigningKey $PrivateKeyPath
if ($taskRsa.ExportSubjectPublicKeyInfoPem() -ne [IO.File]::ReadAllText((Join-Path (Split-Path $PSScriptRoot -Parent) 'assets/trust-public.pem'))) { throw 'Catalog signing key does not match this product.' }
function Write-SignedCatalog($Document, [string]$Destination) {
    $taskPayload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($Document | ConvertTo-Json -Depth 12) + [Environment]::NewLine))
    $taskBytes = [Text.Encoding]::UTF8.GetBytes("WSG-CATALOG-v1" + [char]10 + $taskPayload)
    $taskSignature = [Convert]::ToBase64String($taskRsa.SignData($taskBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1))
    [IO.File]::WriteAllText($Destination, (@{ Payload=$taskPayload; Signature=$taskSignature } | ConvertTo-Json))
}
Write-SignedCatalog $taskDocument (Join-Path $taskCatalogRoot 'assets/catalog.signed.json')
# v0.1.2 and earlier reject unknown enum values before MinimumSwitcherVersion is read.
# Keep their feed free of new provider identities, including combination entries.
$taskLegacy = $taskDocument | ConvertTo-Json -Depth 12 | ConvertFrom-Json
$taskLegacy.Releases = @($taskLegacy.Releases | Where-Object { $_.Provider -in @('Djules','Elliott','VectorXR') })
if ($taskLegacy.BlockedCombinations) { $taskLegacy.BlockedCombinations = @($taskLegacy.BlockedCombinations | Where-Object { $_.Provider -in @('Djules','Elliott','VectorXR') }) }
Write-SignedCatalog $taskLegacy (Join-Path $taskCatalogRoot 'assets/catalog.legacy.signed.json')
$taskRsa.Dispose()
