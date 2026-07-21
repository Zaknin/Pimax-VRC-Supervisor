Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:MaximumPrivateKeyBytes = 16KB
$script:MaximumPublicKeyBytes = 4KB
$script:MaximumCeremonyRecordBytes = 16KB

function Resolve-CeremonyOutputPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedExtension,
        [Parameter(Mandatory)][string[]]$ForbiddenRoots
    )

    if (-not [IO.Path]::IsPathFullyQualified($Path)) {
        throw "Ceremony output paths must be absolute: $Path"
    }
    if ([IO.Path]::GetExtension($Path) -cne $ExpectedExtension) {
        throw "Ceremony output path must use the $ExpectedExtension extension: $Path"
    }
    $leaf = [IO.Path]::GetFileName($Path)
    if ([string]::IsNullOrWhiteSpace($leaf) -or
        $leaf.Length -gt 255 -or
        $leaf -match '[/\\]' -or
        $leaf.Contains('..', [StringComparison]::Ordinal) -or
        @($leaf.ToCharArray() | Where-Object { [char]::IsControl($_) }).Count -gt 0) {
        throw "Ceremony output file name is unsafe: $leaf"
    }

    $parentPath = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Path))
    if ([string]::IsNullOrWhiteSpace($parentPath) -or -not (Test-Path -LiteralPath $parentPath -PathType Container)) {
        throw "Ceremony output parent directory must already exist: $parentPath"
    }
    $parent = (Resolve-Path -LiteralPath $parentPath).Path.TrimEnd('\', '/')
    $resolved = Join-Path $parent $leaf
    foreach ($root in $ForbiddenRoots) {
        $boundary = if (Test-Path -LiteralPath $root) {
            (Resolve-Path -LiteralPath $root).Path.TrimEnd('\', '/')
        } else {
            [IO.Path]::GetFullPath($root).TrimEnd('\', '/')
        }
        if ($resolved.Equals($boundary, [StringComparison]::OrdinalIgnoreCase) -or
            $resolved.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Ceremony output must remain outside $boundary."
        }
    }
    if (Test-Path -LiteralPath $resolved) {
        throw "Ceremony output already exists and will not be overwritten: $resolved"
    }
    $resolved
}

function Write-NewFileAndFlush {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][byte[]]$Bytes
    )

    $stream = [IO.FileStream]::new(
        $Path,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None,
        4096,
        [IO.FileOptions]::WriteThrough)
    try {
        $stream.Write($Bytes, 0, $Bytes.Length)
        $stream.Flush($true)
    } finally {
        $stream.Dispose()
    }
}

function Protect-PrivateKeyFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not $IsWindows) {
        throw 'Production update key ceremony is supported only on Windows.'
    }
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $arguments = @($Path, '/inheritance:r', '/grant:r', "*$sid`:(F)")
    $output = & icacls.exe @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to restrict the private-key file ACL. icacls exit code: $LASTEXITCODE"
    }
    if ($output -match 'Failed processing [1-9]') {
        throw 'Unable to restrict the private-key file ACL.'
    }
}

function Invoke-ProductionUpdateKeyCeremony {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$KeyId,
        [Parameter(Mandatory)][string]$PrivateKeyPath,
        [Parameter(Mandatory)][string]$PublicKeyPath,
        [Parameter(Mandatory)][string]$RecordPath,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$DeploymentRoot
    )

    if ($KeyId -cnotmatch '^[A-Za-z0-9_.-]{1,128}$' -or $KeyId.StartsWith('test-', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Production keyId must be a stable safe identifier and must not use a test-only prefix.'
    }
    $repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $deployment = if (Test-Path -LiteralPath $DeploymentRoot) {
        (Resolve-Path -LiteralPath $DeploymentRoot).Path
    } else {
        [IO.Path]::GetFullPath($DeploymentRoot)
    }
    $forbidden = @($repository, $deployment)
    $privatePath = Resolve-CeremonyOutputPath -Path $PrivateKeyPath -ExpectedExtension '.pk8' -ForbiddenRoots $forbidden
    $publicPath = Resolve-CeremonyOutputPath -Path $PublicKeyPath -ExpectedExtension '.der' -ForbiddenRoots $forbidden
    $ceremonyRecordPath = Resolve-CeremonyOutputPath -Path $RecordPath -ExpectedExtension '.json' -ForbiddenRoots $forbidden
    $distinctPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in @($privatePath, $publicPath, $ceremonyRecordPath)) {
        if (-not $distinctPaths.Add($path)) {
            throw 'Private key, public key, and ceremony record paths must be distinct.'
        }
    }

    $suffix = [guid]::NewGuid().ToString('N')
    $privateTemporaryPath = Join-Path ([IO.Path]::GetDirectoryName($privatePath)) (".$([IO.Path]::GetFileName($privatePath)).$suffix.tmp")
    $publicTemporaryPath = Join-Path ([IO.Path]::GetDirectoryName($publicPath)) (".$([IO.Path]::GetFileName($publicPath)).$suffix.tmp")
    $recordTemporaryPath = Join-Path ([IO.Path]::GetDirectoryName($ceremonyRecordPath)) (".$([IO.Path]::GetFileName($ceremonyRecordPath)).$suffix.tmp")
    $temporaryPaths = @($privateTemporaryPath, $publicTemporaryPath, $recordTemporaryPath)
    $createdFinalPaths = [Collections.Generic.List[string]]::new()
    [byte[]]$privateKeyBytes = $null
    [byte[]]$publicKeyBytes = $null
    try {
        $key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
        try {
            $privateKeyBytes = $key.ExportPkcs8PrivateKey()
            $publicKeyBytes = $key.ExportSubjectPublicKeyInfo()
        } finally {
            $key.Dispose()
        }
        if ($privateKeyBytes.Length -le 0 -or $privateKeyBytes.Length -gt $script:MaximumPrivateKeyBytes) {
            throw 'Generated PKCS#8 private key is outside the allowed size bound.'
        }
        if ($publicKeyBytes.Length -le 0 -or $publicKeyBytes.Length -gt $script:MaximumPublicKeyBytes) {
            throw 'Generated SubjectPublicKeyInfo is outside the allowed size bound.'
        }

        $verifier = [Security.Cryptography.ECDsa]::Create()
        try {
            $bytesRead = 0
            $verifier.ImportSubjectPublicKeyInfo($publicKeyBytes, [ref]$bytesRead)
            if ($bytesRead -ne $publicKeyBytes.Length -or $verifier.KeySize -ne 256) {
                throw 'Generated public key is not exact DER SubjectPublicKeyInfo for ECDSA P-256.'
            }
        } finally {
            $verifier.Dispose()
        }
        $fingerprint = [Convert]::ToHexString(
            [Security.Cryptography.SHA256]::HashData($publicKeyBytes)).ToLowerInvariant()

        Write-NewFileAndFlush -Path $privateTemporaryPath -Bytes $privateKeyBytes
        Protect-PrivateKeyFile -Path $privateTemporaryPath
        Write-NewFileAndFlush -Path $publicTemporaryPath -Bytes $publicKeyBytes
        $record = [ordered]@{
            schemaVersion = 1
            status = 'generated-pending-operator-approval'
            keyId = $KeyId
            algorithm = 'ecdsa-p256-sha256-der'
            publicKeyFormat = 'DER SubjectPublicKeyInfo'
            publicKeyFile = [IO.Path]::GetFileName($publicPath)
            publicKeySizeBytes = $publicKeyBytes.Length
            publicKeySha256 = $fingerprint
            privateKeyFormat = 'PKCS#8 DER'
            privateKeyRetainedAtOperatorPath = $true
            privateKeyPathRecorded = $false
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
            approvalStatus = 'pending'
        }
        $recordJson = $record | ConvertTo-Json -Depth 10
        $recordBytes = [Text.UTF8Encoding]::new($false).GetBytes($recordJson)
        if ($recordBytes.Length -le 0 -or $recordBytes.Length -gt $script:MaximumCeremonyRecordBytes) {
            throw 'Key-ceremony record is outside the 16 KiB size bound.'
        }
        Write-NewFileAndFlush -Path $recordTemporaryPath -Bytes $recordBytes

        [IO.File]::Move($privateTemporaryPath, $privatePath, $false)
        $createdFinalPaths.Add($privatePath)
        [IO.File]::Move($publicTemporaryPath, $publicPath, $false)
        $createdFinalPaths.Add($publicPath)
        [IO.File]::Move($recordTemporaryPath, $ceremonyRecordPath, $false)
        $createdFinalPaths.Add($ceremonyRecordPath)

        if ((Get-FileHash -LiteralPath $publicPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $fingerprint) {
            throw 'Final public-key fingerprint differs from the generated ceremony evidence.'
        }
        Write-Host "Production update trust key generated pending approval. keyId=$KeyId publicKeySha256=$fingerprint"
        Write-Host "publicDer=$publicPath"
        Write-Host "privateKeyRetainedAt=$privatePath"
        Write-Host "ceremonyRecord=$ceremonyRecordPath"
        [pscustomobject]@{
            KeyId = $KeyId
            PublicKeySha256 = $fingerprint
            PublicKeyPath = $publicPath
            PrivateKeyPath = $privatePath
            RecordPath = $ceremonyRecordPath
        }
    } catch {
        foreach ($path in @($temporaryPaths + @($createdFinalPaths))) {
            if (Test-Path -LiteralPath $path -PathType Leaf) {
                Remove-Item -LiteralPath $path -Force
            }
        }
        throw
    } finally {
        if ($null -ne $privateKeyBytes) {
            [Security.Cryptography.CryptographicOperations]::ZeroMemory($privateKeyBytes)
        }
    }
}

Export-ModuleMember -Function 'Invoke-ProductionUpdateKeyCeremony'
