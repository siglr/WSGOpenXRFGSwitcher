$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskPrivate = Join-Path $taskRoot '.tools/signing.private.xml'
New-Item -ItemType Directory -Force (Split-Path $taskPrivate) | Out-Null
$taskRsa = [System.Security.Cryptography.RSA]::Create(3072)
if (Test-Path $taskPrivate) { $taskRsa.FromXmlString([IO.File]::ReadAllText($taskPrivate)) }
else { [IO.File]::WriteAllText($taskPrivate, $taskRsa.ToXmlString($true)) }
[IO.File]::WriteAllText((Join-Path $taskRoot 'assets/trust-public.pem'), $taskRsa.ExportSubjectPublicKeyInfoPem())
$taskPublic = $taskRsa.ToXmlString($false)
$taskSource = '// Local development identity; replace for an official publishing channel.' + [Environment]::NewLine + 'internal static class ReleasePublicKey { internal const string Xml = "' + $taskPublic + '"; }'
[IO.File]::WriteAllText((Join-Path $taskRoot 'src/Updates/ReleasePublicKey.cs'), $taskSource)
$taskRsa.Dispose()
Write-Output 'Development identity ready. Private key remains under ignored .tools/.'
