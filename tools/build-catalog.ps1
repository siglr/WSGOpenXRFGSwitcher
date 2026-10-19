param([Parameter(Mandatory=$true)][string]$PrivateKeyPath,
      [string]$ReviewedJson = (Join-Path (Split-Path $PSScriptRoot -Parent) 'assets/catalog.reviewed.json'))
$ErrorActionPreference = 'Stop'
$taskPayload = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path $ReviewedJson)))
. (Join-Path $PSScriptRoot 'signing-key.ps1')
$taskRsa = Import-WsgSigningKey $PrivateKeyPath
$taskBytes = [Text.Encoding]::UTF8.GetBytes("WSG-CATALOG-v1" + [char]10 + $taskPayload)
$taskSignature = [Convert]::ToBase64String($taskRsa.SignData($taskBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1))
$taskEnvelope = @{ Payload=$taskPayload; Signature=$taskSignature } | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path (Split-Path $PSScriptRoot -Parent) 'assets/catalog.signed.json'), $taskEnvelope)
$taskRsa.Dispose()
