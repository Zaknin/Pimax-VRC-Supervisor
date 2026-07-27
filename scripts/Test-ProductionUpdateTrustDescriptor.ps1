[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourcePath,
    [Parameter(Mandatory)][string]$ExpectedKeyId,
    [Parameter(Mandatory)][string]$ExpectedAlgorithm,
    [Parameter(Mandatory)][int]$ExpectedSize,
    [Parameter(Mandatory)][string]$ExpectedFingerprint,
    [Parameter(Mandatory)][ValidateRange(1, 2)][int]$ExpectedRootCount
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sourceFile = Get-Item -LiteralPath $SourcePath
if ($sourceFile.Length -le 0 -or $sourceFile.Length -gt 64KB) {
    throw 'Production trust-root source is outside the 64 KiB validation bound.'
}
$source = [IO.File]::ReadAllText($sourceFile.FullName)

function Read-UniqueStringConstant {
    param([string]$Name)
    $matches = [regex]::Matches($source, "internal const string $Name = `"([^`"]+)`";")
    if ($matches.Count -ne 1) { throw "Expected exactly one $Name descriptor constant." }
    $matches[0].Groups[1].Value
}

function Read-UniqueIntegerConstant {
    param([string]$Name)
    $matches = [regex]::Matches($source, "internal const int $Name = ([0-9]+);")
    if ($matches.Count -ne 1) { throw "Expected exactly one $Name descriptor constant." }
    [int]$matches[0].Groups[1].Value
}

$keyId = Read-UniqueStringConstant -Name 'CurrentKeyId'
$algorithm = Read-UniqueStringConstant -Name 'CurrentAlgorithm'
$size = Read-UniqueIntegerConstant -Name 'CurrentPublicKeySize'
$fingerprint = Read-UniqueStringConstant -Name 'CurrentPublicKeySha256'
$base64 = Read-UniqueStringConstant -Name 'CurrentPublicKeyBase64'
$descriptorCount = [regex]::Matches($source, '(?m)^\s+ValidateDescriptor\(\r?$').Count

if ($keyId -cne $ExpectedKeyId -or
    $algorithm -cne $ExpectedAlgorithm -or
    $size -ne $ExpectedSize -or
    $fingerprint -cne $ExpectedFingerprint -or
    $descriptorCount -ne $ExpectedRootCount) {
    throw 'Production trust-root source descriptor differs from the approved build descriptor.'
}
if ($fingerprint -cnotmatch '^[0-9a-f]{64}$') {
    throw 'Production trust-root fingerprint must be exactly 64 lowercase hexadecimal characters.'
}
try { $publicKey = [Convert]::FromBase64String($base64) } catch { throw 'Embedded production public key is not valid Base64.' }
if ([Convert]::ToBase64String($publicKey) -cne $base64 -or $publicKey.Length -ne $ExpectedSize) {
    throw 'Embedded production public-key encoding or size differs from the approved descriptor.'
}
$actualFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($publicKey)).ToLowerInvariant()
if ($actualFingerprint -cne $ExpectedFingerprint) {
    throw 'Embedded production public-key fingerprint differs from the approved descriptor.'
}
$ecdsa = [Security.Cryptography.ECDsa]::Create()
try {
    $bytesRead = 0
    $ecdsa.ImportSubjectPublicKeyInfo($publicKey, [ref]$bytesRead)
    $parameters = $ecdsa.ExportParameters($false)
    if ($bytesRead -ne $publicKey.Length -or
        $ecdsa.KeySize -ne 256 -or
        $parameters.Curve.Oid.Value -cne '1.2.840.10045.3.1.7') {
        throw 'Embedded production public key is not exact DER SubjectPublicKeyInfo for ECDSA P-256.'
    }
} finally {
    $ecdsa.Dispose()
}

Write-Host "Production update trust descriptor validated. roots=$descriptorCount keyId=$keyId publicKeySha256=$actualFingerprint size=$($publicKey.Length) algorithm=$algorithm"
