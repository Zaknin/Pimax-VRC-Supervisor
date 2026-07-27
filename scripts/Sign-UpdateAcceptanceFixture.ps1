[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [Parameter(Mandatory)][string]$SignatureEnvelopePath,
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [Parameter(Mandatory)][string]$KeyId,
    [Parameter(Mandatory)][string]$ExpectedPublicKeySha256,
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string]$DeploymentRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PimaxVrcSupervisor-TestDeployments')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$maximumManifestBytes = 256KB
$maximumEnvelopeBytes = 64KB
$algorithm = 'ecdsa-p256-sha256-der'

function Resolve-ExistingFile {
    param([string]$Path, [int64]$MaximumBytes, [string]$Description)
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "$Description path must be absolute." }
    $file = Get-Item -LiteralPath $Path
    if ($file.PSIsContainer -or $file.Length -le 0 -or $file.Length -gt $MaximumBytes) {
        throw "$Description is outside its allowed size bound."
    }
    $file.FullName
}

function Resolve-Root {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path -PathType Container) { return (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\', '/') }
    [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Test-IsWithin {
    param([string]$Path, [string]$Root)
    $Path.Equals($Root, [StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Import-P256PrivateKey {
    param([string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    try {
        $key = [Security.Cryptography.ECDsa]::Create()
        try {
            $read = 0
            $key.ImportPkcs8PrivateKey($bytes, [ref]$read)
            if ($read -ne $bytes.Length -or $key.KeySize -ne 256) {
                throw 'The operator key is not exact PKCS#8 DER for ECDSA P-256.'
            }
            return $key
        } catch {
            $key.Dispose()
            throw
        }
    } finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes)
    }
}

function Write-NewUtf8NoBomAndFlush {
    param([string]$Path, [string]$Text)
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($Text)
    try {
        $stream = [IO.FileStream]::new($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        } finally {
            $stream.Dispose()
        }
    } finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($bytes)
    }
}

if ($KeyId -cnotmatch '^[A-Za-z0-9_.-]{1,128}$' -or $KeyId.StartsWith('test-', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The production keyId is invalid.'
}
if ($ExpectedPublicKeySha256 -cnotmatch '^[0-9a-f]{64}$') {
    throw 'ExpectedPublicKeySha256 must be exactly 64 lowercase hexadecimal characters.'
}

$repository = Resolve-Root $RepositoryRoot
$deployment = Resolve-Root $DeploymentRoot
$manifest = Resolve-ExistingFile $ManifestPath $maximumManifestBytes 'Manifest'
$privateKey = Resolve-ExistingFile $PrivateKeyPath 16KB 'Private key'
if (Test-IsWithin $privateKey $repository -or Test-IsWithin $privateKey $deployment) {
    throw 'The private key must remain outside the repository and deployment directories.'
}
if (-not (Test-IsWithin $manifest $repository)) {
    throw 'The acceptance manifest must be an explicitly reviewed repository fixture.'
}

if (-not [IO.Path]::IsPathFullyQualified($SignatureEnvelopePath)) { throw 'Signature envelope path must be absolute.' }
$envelopeParent = (Resolve-Path -LiteralPath ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($SignatureEnvelopePath)))).Path
$envelopePath = Join-Path $envelopeParent ([IO.Path]::GetFileName($SignatureEnvelopePath))
if (-not (Test-IsWithin $envelopePath $repository)) { throw 'The public signature envelope must be a repository fixture.' }
if (Test-Path -LiteralPath $envelopePath) { throw 'The signature envelope already exists and will not be overwritten.' }

$manifestBytes = [IO.File]::ReadAllBytes($manifest)
try {
    if ($manifestBytes.Length -ge 3 -and $manifestBytes[0] -eq 0xef -and $manifestBytes[1] -eq 0xbb -and $manifestBytes[2] -eq 0xbf) {
        throw 'The acceptance manifest must be UTF-8 without a byte-order mark.'
    }
    $manifestText = [Text.UTF8Encoding]::new($false, $true).GetString($manifestBytes)
    $document = [Text.Json.JsonDocument]::Parse($manifestText)
    try {
        $version = $document.RootElement.GetProperty('release').GetProperty('version').GetString()
        if ($version -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw 'The fixture version is not a normalized stable version.' }
    } finally {
        $document.Dispose()
    }
    $expectedManifestName = "PimaxVrcSupervisor-v$version-update-manifest-v1.json"
    if ([IO.Path]::GetFileName($manifest) -cne $expectedManifestName) {
        throw 'The acceptance manifest file name does not match its version.'
    }

    $key = Import-P256PrivateKey $privateKey
    try {
        $publicBytes = $key.ExportSubjectPublicKeyInfo()
        $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($publicBytes)).ToLowerInvariant()
        if ($publicBytes.Length -ne 91 -or $fingerprint -cne $ExpectedPublicKeySha256) {
            throw 'The external private key does not correspond to the approved production public-key descriptor.'
        }
        $signature = $key.SignData($manifestBytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)
        if (-not $key.VerifyData($manifestBytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)) {
            throw 'Immediate verification of the acceptance signature failed.'
        }
        $envelope = [ordered]@{
            schemaVersion = 1
            manifestFile = $expectedManifestName
            manifestSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($manifestBytes)).ToLowerInvariant()
            signatures = @([ordered]@{
                keyId = $KeyId
                algorithm = $algorithm
                signatureBase64 = [Convert]::ToBase64String($signature)
            })
        }
        $json = $envelope | ConvertTo-Json -Depth 8 -Compress
        if ([Text.UTF8Encoding]::new($false).GetByteCount($json) -gt $maximumEnvelopeBytes) {
            throw 'The signature envelope exceeds its allowed size bound.'
        }
        if (-not $PSCmdlet.ShouldProcess($envelopePath, 'Write operator-approved acceptance signature envelope')) { return }
        Write-NewUtf8NoBomAndFlush $envelopePath $json
        Write-Host "Acceptance fixture signed and verified. keyId=$KeyId publicKeySha256=$fingerprint manifestSha256=$($envelope.manifestSha256)"
    } finally {
        $key.Dispose()
    }
} finally {
    [Security.Cryptography.CryptographicOperations]::ZeroMemory($manifestBytes)
}
