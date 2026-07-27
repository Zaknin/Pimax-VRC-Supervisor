Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ExpectedRepository = 'Zaknin/Pimax-VRC-Supervisor'
$script:ExpectedOwner = 'Zaknin'
$script:MaximumManifestBytes = 256KB
$script:MaximumSignatureBytes = 64KB
$script:MaximumPackageBytes = 1GB
$script:MaximumMetadataBytes = 64MB
$script:ManifestAlgorithm = 'ecdsa-p256-sha256-der'
$script:InventoryName = 'release-candidate-inventory.json'
$script:VerificationReportName = 'release-verification-report.json'
$script:SigningReportName = 'offline-signing-report.json'

function Get-ReleasePipelineConstants {
    [CmdletBinding()]
    param()

    [pscustomobject]@{
        Repository = $script:ExpectedRepository
        Owner = $script:ExpectedOwner
        MaximumManifestBytes = $script:MaximumManifestBytes
        MaximumSignatureBytes = $script:MaximumSignatureBytes
        MaximumPackageBytes = $script:MaximumPackageBytes
        MaximumMetadataBytes = $script:MaximumMetadataBytes
        InventoryName = $script:InventoryName
        VerificationReportName = $script:VerificationReportName
        SigningReportName = $script:SigningReportName
    }
}

function Assert-StrictReleaseIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$SourceCommit,
        [Parameter(Mandatory)][string]$Channel
    )

    $versionMatch = [regex]::Match($Version, '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')
    if (-not $versionMatch.Success) {
        throw "Release version must be normalized stable SemVer. Received: $Version"
    }
    if ($Tag -cne "v$Version") {
        throw "Release tag must be exactly v<version>. Expected v$Version; received $Tag."
    }
    if ($SourceCommit -cnotmatch '^[0-9a-f]{40}$') {
        throw 'Source commit must be exactly 40 lowercase hexadecimal characters.'
    }
    if ($Channel -cne 'stable') {
        throw "Only the stable channel is supported. Received: $Channel"
    }
    if ($Tag -ceq 'v1.3.1') {
        throw 'Historical release v1.3.1 is protected and must never be edited or recreated.'
    }

    [pscustomobject]@{
        Version = $Version
        Tag = $Tag
        SourceCommit = $SourceCommit
        Channel = $Channel
        Major = [long]$versionMatch.Groups[1].Value
        Minor = [long]$versionMatch.Groups[2].Value
        Patch = [long]$versionMatch.Groups[3].Value
    }
}

function Test-ReleaseComponentVersions {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Version
    )

    $root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $projectPaths = @(
        'PimaxVrcSupervisor/PimaxVrcSupervisor.csproj',
        'PimaxVrcSupervisor.UpdateWorker/PimaxVrcSupervisor.UpdateWorker.csproj',
        'PimaxVrcSupervisor.ConfigEditor/PimaxVrcSupervisor.ConfigEditor.csproj',
        'PimaxVrcSupervisor.SteamVrHost/PimaxVrcSupervisor.SteamVrHost.csproj'
    )
    foreach ($relativePath in $projectPaths) {
        $path = Join-Path $root $relativePath
        [xml]$project = Get-Content -LiteralPath $path -Raw
        $properties = $project.Project.PropertyGroup | Where-Object { $null -ne $_.Version } | Select-Object -First 1
        if ($null -eq $properties -or $properties.Version -cne $Version) {
            throw "$relativePath does not report release version $Version."
        }
        foreach ($propertyName in @('AssemblyVersion', 'FileVersion')) {
            if ($properties.$propertyName -cne "$Version.0") {
                throw "$relativePath $propertyName does not report $Version.0."
            }
        }
        if ($properties.InformationalVersion -cne $Version) {
            throw "$relativePath InformationalVersion does not report $Version."
        }
    }

    $cargoPath = Join-Path $root 'PimaxVrcSupervisor.Tui/Cargo.toml'
    $cargoVersion = Select-String -LiteralPath $cargoPath -Pattern '^version\s*=\s*"([^"]+)"\s*$' |
        Select-Object -First 1
    if ($null -eq $cargoVersion -or $cargoVersion.Matches[0].Groups[1].Value -cne $Version) {
        throw "PimaxVrcSupervisor.Tui/Cargo.toml does not report release version $Version."
    }
}

function Test-SafeFileName {
    param([Parameter(Mandatory)][string]$FileName)

    if ([string]::IsNullOrWhiteSpace($FileName) -or
        $FileName.Length -gt 255 -or
        $FileName -match '[/\\]' -or
        $FileName.Contains('..', [StringComparison]::Ordinal) -or
        @($FileName.ToCharArray() | Where-Object { [char]::IsControl($_) }).Count -gt 0 -or
        [IO.Path]::GetFileName($FileName) -cne $FileName) {
        throw "Unsafe release file name: $FileName"
    }
}

function Get-LowerSha256 {
    param([Parameter(Mandatory)][string]$Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Utf8NoBomText {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text
    )
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Write-Utf8NoBomJson {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Value,
        [switch]$Compress
    )
    $json = if ($Compress) {
        $Value | ConvertTo-Json -Depth 20 -Compress
    } else {
        $Value | ConvertTo-Json -Depth 20
    }
    Write-Utf8NoBomText -Path $Path -Text $json
}

function Assert-NoDuplicateJsonProperties {
    param([Parameter(Mandatory)][System.Text.Json.JsonElement]$Element)

    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) {
                throw "JSON contains duplicate property '$($property.Name)'."
            }
            Assert-NoDuplicateJsonProperties -Element $property.Value
        }
    } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) {
            Assert-NoDuplicateJsonProperties -Element $item
        }
    }
}

function Read-StrictJsonFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][long]$MaximumBytes
    )

    $file = Get-Item -LiteralPath $Path
    if ($file.Length -le 0 -or $file.Length -gt $MaximumBytes) {
        throw "JSON file size is outside its allowed bound: $($file.Name)"
    }
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xef -and $bytes[1] -eq 0xbb -and $bytes[2] -eq 0xbf) {
        throw "JSON file must be UTF-8 without BOM: $($file.Name)"
    }
    $text = [Text.UTF8Encoding]::new($false, $true).GetString($bytes)
    $document = [System.Text.Json.JsonDocument]::Parse($text)
    try {
        Assert-NoDuplicateJsonProperties -Element $document.RootElement
    } finally {
        $document.Dispose()
    }
    $text | ConvertFrom-Json -Depth 30
}

function Get-ReleaseNames {
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Tag
    )

    $manifest = "PimaxVrcSupervisor-$Tag-update-manifest-v1.json"
    [pscustomobject]@{
        WithPackage = "PimaxVrcSupervisor-$Tag-win-x64-with-dotnet9.zip"
        WithoutPackage = "PimaxVrcSupervisor-$Tag-win-x64-no-dotnet9.zip"
        Manifest = $manifest
        RawSignature = "$manifest.sig"
        SignatureEnvelope = "PimaxVrcSupervisor-$Tag-update-manifest-v1.signatures.json"
        ManifestChecksum = "$manifest.sha256"
        ManifestSigstore = "$manifest.sigstore.json"
        ManifestAttestation = "$manifest.attestation.json"
    }
}

function New-ExactReleaseManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$CandidateDirectory,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$SourceCommit,
        [Parameter(Mandatory)][string]$Channel,
        [Parameter(Mandatory)][string]$GeneratedAtUtc
    )

    $identity = Assert-StrictReleaseIdentity -Version $Version -Tag $Tag -SourceCommit $SourceCommit -Channel $Channel
    if ($GeneratedAtUtc -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$') {
        throw 'GeneratedAtUtc must be a normalized whole-second UTC timestamp ending in Z.'
    }
    foreach ($component in @($identity.Major, $identity.Minor, $identity.Patch)) {
        if ($component -gt 999999) {
            throw 'Release version components must be at most 999999 for deterministic releaseSequence derivation.'
        }
    }
    $releaseSequence = $identity.Major * 1000000000000L + $identity.Minor * 1000000L + $identity.Patch
    if ($releaseSequence -le 0) {
        throw 'Release sequence must be positive.'
    }

    $directory = (Resolve-Path -LiteralPath $CandidateDirectory).Path
    $names = Get-ReleaseNames -Version $Version -Tag $Tag
    $assets = @()
    foreach ($variant in @(
        [pscustomobject]@{ Name = 'with-dotnet9'; Package = $names.WithPackage; Mode = 'self-contained'; Runtime = $null },
        [pscustomobject]@{ Name = 'no-dotnet9'; Package = $names.WithoutPackage; Mode = 'framework-dependent'; Runtime = '9.0.x-windowsdesktop-x64' }
    )) {
        $packagePath = Join-Path $directory $variant.Package
        $package = Get-Item -LiteralPath $packagePath
        if ($package.Length -le 0 -or $package.Length -gt $script:MaximumPackageBytes) {
            throw "Package size is outside the supported bound: $($variant.Package)"
        }
        $checksumName = "$($variant.Package).sha256"
        $sigstoreName = "$($variant.Package).sigstore.json"
        $attestationName = "$($variant.Package).attestation.json"
        foreach ($name in @($checksumName, $sigstoreName, $attestationName)) {
            Test-SafeFileName -FileName $name
            $companion = Get-Item -LiteralPath (Join-Path $directory $name)
            if ($companion.Length -le 0 -or $companion.Length -gt $script:MaximumMetadataBytes) {
                throw "Companion size is outside the supported bound: $name"
            }
        }
        $assets += [ordered]@{
            variant = $variant.Name
            rid = 'win-x64'
            runtimeMode = $variant.Mode
            requiredWindowsDesktopRuntime = $variant.Runtime
            fileName = $variant.Package
            sizeBytes = $package.Length
            sha256 = Get-LowerSha256 -Path $package.FullName
            checksumFile = [ordered]@{ fileName = $checksumName; sha256 = Get-LowerSha256 -Path (Join-Path $directory $checksumName) }
            sigstoreBundleFile = [ordered]@{ fileName = $sigstoreName; sha256 = Get-LowerSha256 -Path (Join-Path $directory $sigstoreName) }
            attestationBundleFile = [ordered]@{ fileName = $attestationName; sha256 = Get-LowerSha256 -Path (Join-Path $directory $attestationName) }
        }
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        repository = $script:ExpectedRepository
        channel = 'stable'
        releaseSequence = $releaseSequence
        generatedAtUtc = $GeneratedAtUtc
        release = [ordered]@{
            version = $Version
            tag = $Tag
            commitSha = $SourceCommit
            releaseUrl = "https://github.com/$script:ExpectedRepository/releases/tag/$Tag"
        }
        assets = $assets
    }
    $manifestPath = Join-Path $directory $names.Manifest
    if (Test-Path -LiteralPath $manifestPath) {
        throw "Manifest already exists: $manifestPath"
    }
    Write-Utf8NoBomJson -Path $manifestPath -Value $manifest -Compress
    $manifestFile = Get-Item -LiteralPath $manifestPath
    if ($manifestFile.Length -gt $script:MaximumManifestBytes) {
        throw 'Generated update manifest exceeds the 256 KiB bound.'
    }
    $hash = Get-LowerSha256 -Path $manifestPath
    Write-Utf8NoBomText -Path (Join-Path $directory $names.ManifestChecksum) -Text "$hash  $($names.Manifest)"
    [pscustomobject]@{ ManifestPath = $manifestPath; ManifestSha256 = $hash; ReleaseSequence = $releaseSequence }
}

function Complete-ReleaseCandidate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$CandidateDirectory,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$SourceCommit,
        [Parameter(Mandatory)][string]$Channel,
        [Parameter(Mandatory)][string]$WorkflowRunId,
        [Parameter(Mandatory)][string]$WorkflowRunAttempt,
        [Parameter(Mandatory)][string]$WorkflowRunUrl,
        [Parameter(Mandatory)][string]$WorkflowRef
    )

    Assert-StrictReleaseIdentity -Version $Version -Tag $Tag -SourceCommit $SourceCommit -Channel $Channel | Out-Null
    $directory = (Resolve-Path -LiteralPath $CandidateDirectory).Path
    $names = Get-ReleaseNames -Version $Version -Tag $Tag
    $required = @(
        $names.WithPackage,
        "$($names.WithPackage).sha256",
        "$($names.WithPackage).sigstore.json",
        "$($names.WithPackage).attestation.json",
        $names.WithoutPackage,
        "$($names.WithoutPackage).sha256",
        "$($names.WithoutPackage).sigstore.json",
        "$($names.WithoutPackage).attestation.json",
        $names.Manifest,
        $names.ManifestChecksum,
        $names.ManifestSigstore,
        $names.ManifestAttestation
    )
    foreach ($name in $required) {
        Test-SafeFileName -FileName $name
        if (-not (Test-Path -LiteralPath (Join-Path $directory $name) -PathType Leaf)) {
            throw "Candidate is missing required file: $name"
        }
    }
    $unexpectedZip = @(Get-ChildItem -LiteralPath $directory -File -Filter '*.zip' | Where-Object { $required -cnotcontains $_.Name })
    if ($unexpectedZip) {
        throw "Candidate contains an unexpected ZIP: $($unexpectedZip.Name -join ', ')"
    }

    $manifestHash = Get-LowerSha256 -Path (Join-Path $directory $names.Manifest)
    $entries = foreach ($name in $required) {
        $file = Get-Item -LiteralPath (Join-Path $directory $name)
        $variant = if ($name.StartsWith($names.WithPackage, [StringComparison]::Ordinal)) {
            'with-dotnet9'
        } elseif ($name.StartsWith($names.WithoutPackage, [StringComparison]::Ordinal)) {
            'no-dotnet9'
        } else {
            $null
        }
        [ordered]@{
            fileName = $name
            sizeBytes = $file.Length
            sha256 = Get-LowerSha256 -Path $file.FullName
            packageVariant = $variant
        }
    }

    $checksumLines = $entries | Sort-Object fileName | ForEach-Object { "$($_.sha256)  $($_.fileName)" }
    Write-Utf8NoBomText -Path (Join-Path $directory 'SHA256SUMS.txt') -Text ($checksumLines -join "`n")
    $verificationReport = [ordered]@{
        schemaVersion = 1
        status = 'candidate-verified'
        repository = $script:ExpectedRepository
        version = $Version
        tag = $Tag
        sourceCommit = $SourceCommit
        packageVariants = @('with-dotnet9', 'no-dotnet9')
        manifestSha256 = $manifestHash
        offlineEcdsaSignaturePresent = $false
        releaseCreated = $false
    }
    Write-Utf8NoBomJson -Path (Join-Path $directory $script:VerificationReportName) -Value $verificationReport

    $extraFiles = @('SHA256SUMS.txt', $script:VerificationReportName)
    foreach ($name in $extraFiles) {
        $file = Get-Item -LiteralPath (Join-Path $directory $name)
        $entries += [ordered]@{
            fileName = $name
            sizeBytes = $file.Length
            sha256 = Get-LowerSha256 -Path $file.FullName
            packageVariant = $null
        }
    }
    $inventory = [ordered]@{
        schemaVersion = 1
        repository = $script:ExpectedRepository
        channel = 'stable'
        version = $Version
        tag = $Tag
        sourceCommit = $SourceCommit
        manifestFile = $names.Manifest
        manifestSha256 = $manifestHash
        workflow = [ordered]@{
            runId = $WorkflowRunId
            runAttempt = $WorkflowRunAttempt
            runUrl = $WorkflowRunUrl
            workflowRef = $WorkflowRef
        }
        files = @($entries | Sort-Object fileName)
    }
    Write-Utf8NoBomJson -Path (Join-Path $directory $script:InventoryName) -Value $inventory
    Test-ReleaseCandidateDirectory -CandidateDirectory $directory | Out-Null
}

function Test-ReleaseCandidateDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$CandidateDirectory,
        [switch]$AllowSigningOutputs
    )

    $directory = (Resolve-Path -LiteralPath $CandidateDirectory).Path
    $inventoryPath = Join-Path $directory $script:InventoryName
    $inventory = Read-StrictJsonFile -Path $inventoryPath -MaximumBytes 1MB
    $identity = Assert-StrictReleaseIdentity -Version $inventory.version -Tag $inventory.tag -SourceCommit $inventory.sourceCommit -Channel $inventory.channel
    if ($inventory.schemaVersion -ne 1 -or $inventory.repository -cne $script:ExpectedRepository) {
        throw 'Candidate inventory identity or schema is invalid.'
    }
    if ($inventory.manifestSha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw 'Candidate inventory manifest digest is invalid.'
    }
    if ($inventory.workflow.runId -cnotmatch '^[1-9][0-9]{0,19}$' -or
        $inventory.workflow.runAttempt -cnotmatch '^[1-9][0-9]{0,9}$' -or
        $inventory.workflow.runUrl -cnotmatch '^https://github\.com/Zaknin/Pimax-VRC-Supervisor/actions/runs/[1-9][0-9]{0,19}$' -or
        $inventory.workflow.workflowRef -cne 'Zaknin/Pimax-VRC-Supervisor/.github/workflows/release.yml@refs/heads/main') {
        throw 'Candidate workflow provenance is not the exact approved main-branch release workflow identity.'
    }
    $files = @($inventory.files)
    if ($files.Count -lt 14 -or $files.Count -gt 64) {
        throw "Candidate inventory file count is outside its bound: $($files.Count)"
    }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $files) {
        Test-SafeFileName -FileName $entry.fileName
        if (-not $names.Add([string]$entry.fileName)) {
            throw "Candidate inventory contains duplicate file '$($entry.fileName)'."
        }
        if ($entry.sha256 -cnotmatch '^[0-9a-f]{64}$' -or [long]$entry.sizeBytes -le 0) {
            throw "Candidate inventory hash or size is invalid for $($entry.fileName)."
        }
        $path = Join-Path $directory $entry.fileName
        $file = Get-Item -LiteralPath $path
        $maximum = if ($entry.fileName -like '*.zip') { $script:MaximumPackageBytes } else { $script:MaximumMetadataBytes }
        if ($file.Length -ne [long]$entry.sizeBytes -or $file.Length -gt $maximum) {
            throw "Candidate size mismatch for $($entry.fileName)."
        }
        if ((Get-LowerSha256 -Path $file.FullName) -cne $entry.sha256) {
            throw "Candidate SHA-256 mismatch for $($entry.fileName)."
        }
    }
    $releaseNames = Get-ReleaseNames -Version $identity.Version -Tag $identity.Tag
    $requiredInventoryNames = @(
        $releaseNames.WithPackage,
        "$($releaseNames.WithPackage).sha256",
        "$($releaseNames.WithPackage).sigstore.json",
        "$($releaseNames.WithPackage).attestation.json",
        $releaseNames.WithoutPackage,
        "$($releaseNames.WithoutPackage).sha256",
        "$($releaseNames.WithoutPackage).sigstore.json",
        "$($releaseNames.WithoutPackage).attestation.json",
        $releaseNames.Manifest,
        $releaseNames.ManifestChecksum,
        $releaseNames.ManifestSigstore,
        $releaseNames.ManifestAttestation,
        'SHA256SUMS.txt',
        $script:VerificationReportName
    )
    $inventoryNames = @($files | ForEach-Object fileName)
    $missingInventory = @($requiredInventoryNames | Where-Object { $inventoryNames -cnotcontains $_ })
    $unexpectedInventory = @($inventoryNames | Where-Object { $requiredInventoryNames -cnotcontains $_ })
    if ($inventoryNames.Count -ne $requiredInventoryNames.Count -or $missingInventory -or $unexpectedInventory) {
        throw "Candidate inventory file set is not exact. Missing=[$($missingInventory -join ', ')] Unexpected=[$($unexpectedInventory -join ', ')]"
    }
    $manifestPath = Join-Path $directory $inventory.manifestFile
    if ((Get-LowerSha256 -Path $manifestPath) -cne $inventory.manifestSha256) {
        throw 'Candidate manifest hash does not match its inventory.'
    }
    $manifest = Read-StrictJsonFile -Path $manifestPath -MaximumBytes $script:MaximumManifestBytes
    if ($manifest.schemaVersion -ne 1 -or
        $manifest.repository -cne $script:ExpectedRepository -or
        $manifest.channel -cne 'stable' -or
        $manifest.release.version -cne $identity.Version -or
        $manifest.release.tag -cne $identity.Tag -or
        $manifest.release.commitSha -cne $identity.SourceCommit) {
        throw 'Candidate manifest does not agree with the inventory.'
    }
    $variants = @($manifest.assets | ForEach-Object { $_.variant })
    if ($variants.Count -ne 2 -or
        @($variants | Where-Object { $_ -ceq 'with-dotnet9' }).Count -ne 1 -or
        @($variants | Where-Object { $_ -ceq 'no-dotnet9' }).Count -ne 1) {
        throw 'Candidate manifest must contain each package variant exactly once.'
    }
    $variantContracts = @(
        [pscustomobject]@{
            Variant = 'with-dotnet9'; FileName = $releaseNames.WithPackage
            RuntimeMode = 'self-contained'; RequiredRuntime = $null
        },
        [pscustomobject]@{
            Variant = 'no-dotnet9'; FileName = $releaseNames.WithoutPackage
            RuntimeMode = 'framework-dependent'; RequiredRuntime = '9.0.x-windowsdesktop-x64'
        }
    )
    foreach ($contract in $variantContracts) {
        $asset = @($manifest.assets | Where-Object { $_.variant -ceq $contract.Variant })[0]
        $packagePath = Join-Path $directory $contract.FileName
        if ($asset.fileName -cne $contract.FileName -or
            $asset.rid -cne 'win-x64' -or
            $asset.runtimeMode -cne $contract.RuntimeMode -or
            $asset.requiredWindowsDesktopRuntime -cne $contract.RequiredRuntime -or
            [long]$asset.sizeBytes -ne (Get-Item -LiteralPath $packagePath).Length -or
            $asset.sha256 -cne (Get-LowerSha256 -Path $packagePath) -or
            $asset.checksumFile.fileName -cne "$($contract.FileName).sha256" -or
            $asset.sigstoreBundleFile.fileName -cne "$($contract.FileName).sigstore.json" -or
            $asset.attestationBundleFile.fileName -cne "$($contract.FileName).attestation.json" -or
            $asset.checksumFile.sha256 -cne (Get-LowerSha256 -Path (Join-Path $directory $asset.checksumFile.fileName)) -or
            $asset.sigstoreBundleFile.sha256 -cne (Get-LowerSha256 -Path (Join-Path $directory $asset.sigstoreBundleFile.fileName)) -or
            $asset.attestationBundleFile.sha256 -cne (Get-LowerSha256 -Path (Join-Path $directory $asset.attestationBundleFile.fileName))) {
            throw "Candidate manifest package contract is invalid for variant $($contract.Variant)."
        }
    }

    $allowedSigning = @()
    if ($AllowSigningOutputs) {
        $releaseNames = Get-ReleaseNames -Version $identity.Version -Tag $identity.Tag
        $allowedSigning = @($releaseNames.RawSignature, $releaseNames.SignatureEnvelope, $script:SigningReportName)
    }
    $actualNames = @(Get-ChildItem -LiteralPath $directory -File | ForEach-Object Name)
    $expectedNames = @($files | ForEach-Object fileName) + @($script:InventoryName)
    $permittedNames = $expectedNames + $allowedSigning
    $unexpected = @($actualNames | Where-Object { $permittedNames -cnotcontains $_ })
    $missing = @($expectedNames | Where-Object { $actualNames -cnotcontains $_ })
    if ($AllowSigningOutputs) {
        $presentSigning = @($allowedSigning | Where-Object { $actualNames -ccontains $_ })
        if ($presentSigning.Count -ne 0 -and $presentSigning.Count -ne $allowedSigning.Count) {
            throw 'Candidate contains only part of the offline signing output set.'
        }
    }
    if ($unexpected -or $missing) {
        throw "Candidate file set mismatch. Missing=[$($missing -join ', ')] Unexpected=[$($unexpected -join ', ')]"
    }

    [pscustomobject]@{
        Directory = $directory
        Inventory = $inventory
        Identity = $identity
        ManifestPath = $manifestPath
        ManifestSha256 = $inventory.manifestSha256
        BaseFiles = $files
    }
}

function Resolve-OutsidePathBoundary {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string[]]$ForbiddenDirectories
    )

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    foreach ($directory in $ForbiddenDirectories) {
        $boundary = (Resolve-Path -LiteralPath $directory).Path.TrimEnd('\', '/')
        if ($resolved.Equals($boundary, [StringComparison]::OrdinalIgnoreCase) -or
            $resolved.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Sensitive key path must remain outside $boundary."
        }
    }
    $resolved
}

function Import-EcdsaPrivateKey {
    param([Parameter(Mandatory)][string]$Path)

    $ecdsa = [Security.Cryptography.ECDsa]::Create()
    try {
        $text = [IO.File]::ReadAllText($Path)
        if ($text.Contains('BEGIN ', [StringComparison]::Ordinal)) {
            $ecdsa.ImportFromPem($text)
        } else {
            $bytesRead = 0
            $ecdsa.ImportPkcs8PrivateKey([IO.File]::ReadAllBytes($Path), [ref]$bytesRead)
        }
        if ($ecdsa.KeySize -ne 256) {
            throw 'Offline manifest key must be ECDSA P-256.'
        }
        $ecdsa
    } catch {
        $ecdsa.Dispose()
        throw
    }
}

function Import-EcdsaPublicKey {
    param([Parameter(Mandatory)][string]$Path)

    $ecdsa = [Security.Cryptography.ECDsa]::Create()
    try {
        $text = [IO.File]::ReadAllText($Path)
        if ($text.Contains('BEGIN ', [StringComparison]::Ordinal)) {
            $ecdsa.ImportFromPem($text)
        } else {
            $bytesRead = 0
            $ecdsa.ImportSubjectPublicKeyInfo([IO.File]::ReadAllBytes($Path), [ref]$bytesRead)
        }
        if ($ecdsa.KeySize -ne 256) {
            throw 'Approved manifest public key must be ECDSA P-256.'
        }
        $ecdsa
    } catch {
        $ecdsa.Dispose()
        throw
    }
}

function Invoke-OfflineManifestSigningCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$CandidateDirectory,
        [Parameter(Mandatory)][string]$PrivateKeyPath,
        [Parameter(Mandatory)][string]$KeyId,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [switch]$Overwrite
    )

    if ($KeyId -cnotmatch '^[A-Za-z0-9_.-]{1,128}$') {
        throw 'KeyId contains unsupported characters or exceeds 128 characters.'
    }
    $context = Test-ReleaseCandidateDirectory -CandidateDirectory $CandidateDirectory -AllowSigningOutputs
    $privatePath = Resolve-OutsidePathBoundary -Path $PrivateKeyPath -ForbiddenDirectories @($RepositoryRoot, $context.Directory)
    $releaseNames = Get-ReleaseNames -Version $context.Identity.Version -Tag $context.Identity.Tag
    $rawPath = Join-Path $context.Directory $releaseNames.RawSignature
    $envelopePath = Join-Path $context.Directory $releaseNames.SignatureEnvelope
    $reportPath = Join-Path $context.Directory $script:SigningReportName
    $outputs = @($rawPath, $envelopePath, $reportPath)
    if (-not $Overwrite -and ($outputs | Where-Object { Test-Path -LiteralPath $_ })) {
        throw 'Offline signing output already exists. Use the explicit safe overwrite flag only after review.'
    }

    $manifestBytes = [IO.File]::ReadAllBytes($context.ManifestPath)
    $key = Import-EcdsaPrivateKey -Path $privatePath
    try {
        $publicKey = $key.ExportSubjectPublicKeyInfo()
        $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($publicKey)).ToLowerInvariant()
        $signature = $key.SignData(
            $manifestBytes,
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)
        if (-not $key.VerifyData(
            $manifestBytes,
            $signature,
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)) {
            throw 'Immediate verification of the generated manifest signature failed.'
        }
        $base64 = [Convert]::ToBase64String($signature)
        Write-Utf8NoBomText -Path $rawPath -Text $base64
        $envelope = [ordered]@{
            schemaVersion = 1
            manifestFile = $context.Inventory.manifestFile
            manifestSha256 = $context.ManifestSha256
            signatures = @([ordered]@{
                keyId = $KeyId
                algorithm = $script:ManifestAlgorithm
                signatureBase64 = $base64
            })
        }
        Write-Utf8NoBomJson -Path $envelopePath -Value $envelope -Compress
        $report = [ordered]@{
            schemaVersion = 1
            status = 'signed-and-verified'
            keyId = $KeyId
            publicKeySha256 = $fingerprint
            manifestFile = $context.Inventory.manifestFile
            manifestSha256 = $context.ManifestSha256
            algorithm = $script:ManifestAlgorithm
        }
        Write-Utf8NoBomJson -Path $reportPath -Value $report
        foreach ($path in $outputs) {
            $file = Get-Item -LiteralPath $path
            if ($file.Length -le 0 -or $file.Length -gt $script:MaximumSignatureBytes) {
                throw "Signing output exceeds its bound: $($file.Name)"
            }
        }
        Write-Host "Manifest signed and verified. keyId=$KeyId publicKeySha256=$fingerprint"
        [pscustomobject]@{ KeyId = $KeyId; PublicKeySha256 = $fingerprint; SignaturePath = $rawPath; EnvelopePath = $envelopePath; ReportPath = $reportPath }
    } finally {
        $key.Dispose()
    }
}

function Test-OfflineManifestSignature {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$CandidateContext,
        [Parameter(Mandatory)][string]$ApprovedPublicKeyPath,
        [Parameter(Mandatory)][string]$ExpectedKeyId
    )

    $releaseNames = Get-ReleaseNames -Version $CandidateContext.Identity.Version -Tag $CandidateContext.Identity.Tag
    $rawPath = Join-Path $CandidateContext.Directory $releaseNames.RawSignature
    $envelopePath = Join-Path $CandidateContext.Directory $releaseNames.SignatureEnvelope
    $reportPath = Join-Path $CandidateContext.Directory $script:SigningReportName
    $signatureText = [IO.File]::ReadAllText($rawPath)
    if ($signatureText.Length -eq 0 -or
        $signatureText.Length % 4 -ne 0 -or
        $signatureText -cnotmatch '^[A-Za-z0-9+/]+={0,2}$') {
        throw 'Detached manifest signature must be one exact Base64 DER value without whitespace.'
    }
    try { $signature = [Convert]::FromBase64String($signatureText) } catch { throw 'Detached manifest signature is not valid Base64 DER.' }
    $envelope = Read-StrictJsonFile -Path $envelopePath -MaximumBytes $script:MaximumSignatureBytes
    $report = Read-StrictJsonFile -Path $reportPath -MaximumBytes $script:MaximumSignatureBytes
    if ($envelope.schemaVersion -ne 1 -or
        $envelope.manifestFile -cne $CandidateContext.Inventory.manifestFile -or
        $envelope.manifestSha256 -cne $CandidateContext.ManifestSha256 -or
        @($envelope.signatures).Count -ne 1 -or
        $envelope.signatures[0].keyId -cne $ExpectedKeyId -or
        $envelope.signatures[0].algorithm -cne $script:ManifestAlgorithm -or
        $envelope.signatures[0].signatureBase64 -cne $signatureText -or
        $report.schemaVersion -ne 1 -or
        $report.status -cne 'signed-and-verified' -or
        $report.keyId -cne $ExpectedKeyId -or
        $report.manifestFile -cne $CandidateContext.Inventory.manifestFile -or
        $report.manifestSha256 -cne $CandidateContext.ManifestSha256 -or
        $report.algorithm -cne $script:ManifestAlgorithm) {
        throw 'Detached signature, envelope, signing report, or key ID is inconsistent.'
    }
    $publicKey = Import-EcdsaPublicKey -Path $ApprovedPublicKeyPath
    try {
        $spki = $publicKey.ExportSubjectPublicKeyInfo()
        $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($spki)).ToLowerInvariant()
        if ($report.publicKeySha256 -cne $fingerprint) {
            throw 'Approved public-key fingerprint does not match the signing report.'
        }
        if (-not $publicKey.VerifyData(
            [IO.File]::ReadAllBytes($CandidateContext.ManifestPath),
            $signature,
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)) {
            throw 'Detached ECDSA manifest signature verification failed.'
        }
        [pscustomobject]@{ KeyId = $ExpectedKeyId; PublicKeySha256 = $fingerprint }
    } finally {
        $publicKey.Dispose()
    }
}

function Invoke-NativeCommandResult {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$CommandArguments,
        [string]$WorkingDirectory
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $startInfo.WorkingDirectory = $WorkingDirectory
    }
    foreach ($argument in $CommandArguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Unable to start $FilePath."
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        [pscustomobject]@{
            ExitCode = $process.ExitCode
            StdOut = $stdoutTask.GetAwaiter().GetResult()
            StdErr = $stderrTask.GetAwaiter().GetResult()
        }
    } finally {
        $process.Dispose()
    }
}

function Invoke-RequiredAdapter {
    param(
        [Parameter(Mandatory)][scriptblock]$Adapter,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$Description,
        [switch]$AllowFailure
    )

    $result = & $Adapter $Arguments
    if ($null -eq $result -or
        $null -eq $result.PSObject.Properties['ExitCode'] -or
        $null -eq $result.PSObject.Properties['StdOut'] -or
        $null -eq $result.PSObject.Properties['StdErr']) {
        throw "The injected command adapter returned an invalid result for: $Description"
    }
    if ($result.StdOut.Length -gt $script:MaximumMetadataBytes -or $result.StdErr.Length -gt 1MB) {
        throw "Command output exceeded its bounded response limit for: $Description"
    }
    if (-not $AllowFailure -and [int]$result.ExitCode -ne 0) {
        throw "$Description failed with exit code $($result.ExitCode): $($result.StdErr)"
    }
    $result
}

function Get-RemoteReleaseForTag {
    param(
        [Parameter(Mandatory)][scriptblock]$GhInvoker,
        [Parameter(Mandatory)][string]$Tag
    )

    $result = Invoke-RequiredAdapter -Adapter $GhInvoker -Arguments @(
        'api', '--paginate', '--slurp', "repos/$script:ExpectedRepository/releases?per_page=100"
    ) -Description 'GitHub release inventory'
    $parsed = $result.StdOut | ConvertFrom-Json -Depth 30
    $releases = [Collections.Generic.List[object]]::new()
    if ($null -ne $parsed) {
        foreach ($page in @($parsed)) {
            foreach ($release in @($page)) {
                if ($null -ne $release) { $releases.Add($release) }
            }
        }
    }
    $matches = @($releases | Where-Object { $_.tag_name -ceq $Tag })
    if ($matches.Count -gt 1) {
        throw "GitHub returned more than one release for tag $Tag."
    }
    if ($matches.Count -eq 1) { $matches[0] } else { $null }
}

function Assert-ExactRemoteReleaseMetadata {
    param(
        [Parameter(Mandatory)]$Release,
        [Parameter(Mandatory)]$Identity,
        [Parameter(Mandatory)][string]$ExpectedTitle,
        [Parameter(Mandatory)][string]$ExpectedBody,
        [Parameter(Mandatory)][bool]$ExpectedDraft
    )

    if ($Release.tag_name -cne $Identity.Tag -or
        $Release.name -cne $ExpectedTitle -or
        $Release.body -cne $ExpectedBody -or
        $Release.target_commitish -cne $Identity.SourceCommit -or
        [bool]$Release.draft -ne $ExpectedDraft -or
        [bool]$Release.prerelease) {
        throw "Release metadata for $($Identity.Tag) is not the exact approved metadata."
    }
    $expectedUrl = "https://github.com/$script:ExpectedRepository/releases/tag/$($Identity.Tag)"
    if ($Release.html_url -cne $expectedUrl) {
        throw "Release URL for $($Identity.Tag) is not the fixed canonical URL."
    }
}

function Get-ExpectedPublishedAssets {
    param([Parameter(Mandatory)]$CandidateContext)

    $releaseNames = Get-ReleaseNames -Version $CandidateContext.Identity.Version -Tag $CandidateContext.Identity.Tag
    $names = @($CandidateContext.BaseFiles | ForEach-Object fileName) + @(
        $script:InventoryName,
        $releaseNames.RawSignature,
        $releaseNames.SignatureEnvelope,
        $script:SigningReportName
    )
    $assets = foreach ($name in $names) {
        Test-SafeFileName -FileName $name
        $file = Get-Item -LiteralPath (Join-Path $CandidateContext.Directory $name)
        if ($file.Length -le 0 -or $file.Length -gt $script:MaximumPackageBytes) {
            throw "Release asset size is outside its bound: $name"
        }
        [pscustomobject]@{
            Name = $name
            Path = $file.FullName
            Size = $file.Length
            Sha256 = Get-LowerSha256 -Path $file.FullName
        }
    }
    @($assets | Sort-Object Name)
}

function Assert-RemoteAssetInventory {
    param(
        [Parameter(Mandatory)]$Release,
        [Parameter(Mandatory)][object[]]$ExpectedAssets,
        [switch]$AllowSubset
    )

    $remoteAssets = @($Release.assets)
    $remoteNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($asset in $remoteAssets) {
        Test-SafeFileName -FileName $asset.name
        if (-not $remoteNames.Add([string]$asset.name)) {
            throw "Remote release contains duplicate asset '$($asset.name)'."
        }
        $expected = @($ExpectedAssets | Where-Object { $_.Name -ceq $asset.name })
        if ($expected.Count -ne 1) {
            throw "Remote release contains an unexpected asset: $($asset.name)"
        }
        if ([long]$asset.size -ne [long]$expected[0].Size) {
            throw "Remote release asset size differs from the approved candidate: $($asset.name)"
        }
    }
    if (-not $AllowSubset -and $remoteAssets.Count -ne $ExpectedAssets.Count) {
        throw "Remote release asset count is $($remoteAssets.Count); expected $($ExpectedAssets.Count)."
    }
    Write-Output -NoEnumerate $remoteNames
}

function Assert-DownloadedAssetHashes {
    param(
        [Parameter(Mandatory)][scriptblock]$GhInvoker,
        [Parameter(Mandatory)]$Identity,
        [Parameter(Mandatory)][object[]]$ExpectedAssets,
        [Parameter(Mandatory)][string]$DownloadDirectory
    )

    foreach ($asset in $ExpectedAssets) {
        Invoke-RequiredAdapter -Adapter $GhInvoker -Arguments @(
            'release', 'download', $Identity.Tag,
            '--repo', $script:ExpectedRepository,
            '--pattern', $asset.Name,
            '--dir', $DownloadDirectory
        ) -Description "download release asset $($asset.Name)" | Out-Null
        $downloadedPath = Join-Path $DownloadDirectory $asset.Name
        $downloaded = Get-Item -LiteralPath $downloadedPath
        if ($downloaded.Length -ne $asset.Size -or (Get-LowerSha256 -Path $downloaded.FullName) -cne $asset.Sha256) {
            throw "Downloaded release asset does not match the approved candidate: $($asset.Name)"
        }
    }
}

function Test-Sha256SumsFile {
    param([Parameter(Mandatory)]$CandidateContext)

    $path = Join-Path $CandidateContext.Directory 'SHA256SUMS.txt'
    $lines = @([IO.File]::ReadAllLines($path) | Where-Object { $_.Length -gt 0 })
    if ($lines.Count -lt 12 -or $lines.Count -gt 64) {
        throw 'SHA256SUMS.txt contains an invalid number of records.'
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($line in $lines) {
        $match = [regex]::Match($line, '^([0-9a-f]{64})  ([^/\\\x00-\x1f]+)$')
        if (-not $match.Success) {
            throw 'SHA256SUMS.txt contains a malformed record.'
        }
        $name = $match.Groups[2].Value
        Test-SafeFileName -FileName $name
        if (-not $seen.Add($name)) {
            throw "SHA256SUMS.txt contains duplicate file '$name'."
        }
        $assetPath = Join-Path $CandidateContext.Directory $name
        if ((Get-LowerSha256 -Path $assetPath) -cne $match.Groups[1].Value) {
            throw "SHA256SUMS.txt verification failed for $name."
        }
    }
}

function Write-BoundedReleaseReport {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Value
    )

    if (Test-Path -LiteralPath $Path) {
        throw "Release report already exists: $Path"
    }
    Write-Utf8NoBomJson -Path $Path -Value $Value
    if ((Get-Item -LiteralPath $Path).Length -gt 1MB) {
        throw "Release report exceeds the 1 MiB bound: $Path"
    }
}

function Invoke-ImmutableReleasePublishCore {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$CandidateDirectory,
        [Parameter(Mandatory)][string]$ApprovedPublicKeyPath,
        [Parameter(Mandatory)][string]$ExpectedKeyId,
        [Parameter(Mandatory)][string]$ReportDirectory,
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [switch]$Confirm,
        [scriptblock]$GhInvoker,
        [scriptblock]$ProcessInvoker,
        [scriptblock]$ConfirmationProvider
    )

    $root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $context = Test-ReleaseCandidateDirectory -CandidateDirectory $CandidateDirectory -AllowSigningOutputs
    $reportPath = [IO.Path]::GetFullPath($ReportDirectory)
    foreach ($forbiddenDirectory in @($root, $context.Directory)) {
        $boundary = $forbiddenDirectory.TrimEnd('\', '/')
        if ($reportPath.Equals($boundary, [StringComparison]::OrdinalIgnoreCase) -or
            $reportPath.StartsWith($boundary + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Release reports must remain outside $boundary."
        }
    }
    $reportRoot = [IO.Directory]::CreateDirectory($reportPath).FullName
    if ($context.Identity.Tag -ceq 'v1.3.1') {
        throw 'Historical release v1.3.1 is protected and must never be mutated.'
    }
    $approvedPublicKey = Resolve-OutsidePathBoundary -Path $ApprovedPublicKeyPath -ForbiddenDirectories @($root, $context.Directory)
    Test-OfflineManifestSignature -CandidateContext $context -ApprovedPublicKeyPath $approvedPublicKey -ExpectedKeyId $ExpectedKeyId | Out-Null
    Test-Sha256SumsFile -CandidateContext $context
    $expectedAssets = Get-ExpectedPublishedAssets -CandidateContext $context

    if ($null -eq $GhInvoker) {
        $GhInvoker = { param([string[]]$CommandArguments) Invoke-NativeCommandResult -FilePath 'gh' -CommandArguments $CommandArguments }
    }
    if ($null -eq $ProcessInvoker) {
        $ProcessInvoker = {
            param([string[]]$CommandArguments)
            $program = $CommandArguments[0]
            Invoke-NativeCommandResult -FilePath $program -CommandArguments @($CommandArguments | Select-Object -Skip 1)
        }
    }
    if ($null -eq $ConfirmationProvider) {
        $ConfirmationProvider = { param([string]$Prompt) Read-Host $Prompt }
    }

    $headResult = Invoke-RequiredAdapter -Adapter $ProcessInvoker -Arguments @('git', '-C', $root, 'rev-parse', 'HEAD') -Description 'local HEAD check'
    if ($headResult.StdOut.Trim() -cne $context.Identity.SourceCommit) {
        throw 'Local HEAD does not exactly match the approved source commit.'
    }
    $statusResult = Invoke-RequiredAdapter -Adapter $ProcessInvoker -Arguments @('git', '-C', $root, 'status', '--porcelain=v1', '--untracked-files=all') -Description 'local worktree check'
    if (-not [string]::IsNullOrWhiteSpace($statusResult.StdOut)) {
        throw 'The publisher requires a completely clean local worktree.'
    }

    $auth = Invoke-RequiredAdapter -Adapter $GhInvoker -Arguments @('api', 'user', '--jq', '.login') -Description 'GitHub authentication check'
    if ($auth.StdOut.Trim() -cne $script:ExpectedOwner) {
        throw "GitHub CLI must be authenticated as $script:ExpectedOwner."
    }
    $repo = Invoke-RequiredAdapter -Adapter $GhInvoker -Arguments @('repo', 'view', $script:ExpectedRepository, '--json', 'nameWithOwner', '--jq', '.nameWithOwner') -Description 'GitHub repository identity check'
    if ($repo.StdOut.Trim() -cne $script:ExpectedRepository) {
        throw 'GitHub CLI repository identity does not match the fixed release repository.'
    }
    $immutableResult = Invoke-RequiredAdapter -Adapter $GhInvoker -Arguments @(
        'api', '-H', 'X-GitHub-Api-Version: 2026-03-10', "repos/$script:ExpectedRepository/immutable-releases"
    ) -Description 'GitHub immutable release configuration check'
    $immutable = $immutableResult.StdOut | ConvertFrom-Json -Depth 10
    if ($immutable.enabled -ne $true) {
        throw 'GitHub release immutability must be enabled before draft creation.'
    }
    $tagCommit = Invoke-RequiredAdapter -Adapter $GhInvoker -Arguments @(
        'api', "repos/$script:ExpectedRepository/commits/$($context.Identity.Tag)", '--jq', '.sha'
    ) -Description 'remote tag commit check'
    if ($tagCommit.StdOut.Trim() -cne $context.Identity.SourceCommit) {
        throw 'The remote tag does not resolve to the approved source commit.'
    }

    $title = "Pimax VRC Supervisor $($context.Identity.Tag)"
    $body = "Immutable release for Pimax VRC Supervisor $($context.Identity.Tag). See RELEASE_NOTES.md in the package."
    $release = Get-RemoteReleaseForTag -GhInvoker $GhInvoker -Tag $context.Identity.Tag
    if ($null -ne $release) {
        if (-not [bool]$release.draft) {
            throw "Release $($context.Identity.Tag) is already published; the publisher will not mutate it."
        }
        Assert-ExactRemoteReleaseMetadata -Release $release -Identity $context.Identity -ExpectedTitle $title -ExpectedBody $body -ExpectedDraft $true
        Assert-RemoteAssetInventory -Release $release -ExpectedAssets $expectedAssets -AllowSubset | Out-Null
    } else {
        Invoke-RequiredAdapter -Adapter $GhInvoker -Arguments @(
            'release', 'create', $context.Identity.Tag,
            '--repo', $script:ExpectedRepository,
            '--draft',
            '--verify-tag',
            '--target', $context.Identity.SourceCommit,
            '--title', $title,
            '--notes', $body
        ) -Description 'create exact draft release' | Out-Null
        $release = Get-RemoteReleaseForTag -GhInvoker $GhInvoker -Tag $context.Identity.Tag
        if ($null -eq $release) {
            throw 'Draft release was not visible after creation.'
        }
        Assert-ExactRemoteReleaseMetadata -Release $release -Identity $context.Identity -ExpectedTitle $title -ExpectedBody $body -ExpectedDraft $true
    }

    $remoteNames = Assert-RemoteAssetInventory -Release $release -ExpectedAssets $expectedAssets -AllowSubset
    $missingAssets = @($expectedAssets | Where-Object { -not $remoteNames.Contains($_.Name) })
    if ($missingAssets.Count -gt 0) {
        $uploadArguments = @('release', 'upload', $context.Identity.Tag, '--repo', $script:ExpectedRepository)
        $uploadArguments += @($missingAssets | ForEach-Object Path)
        Invoke-RequiredAdapter -Adapter $GhInvoker -Arguments $uploadArguments -Description 'upload missing approved draft assets' | Out-Null
    }

    $release = Get-RemoteReleaseForTag -GhInvoker $GhInvoker -Tag $context.Identity.Tag
    Assert-ExactRemoteReleaseMetadata -Release $release -Identity $context.Identity -ExpectedTitle $title -ExpectedBody $body -ExpectedDraft $true
    Assert-RemoteAssetInventory -Release $release -ExpectedAssets $expectedAssets | Out-Null

    $downloadRoot = Join-Path ([IO.Path]::GetTempPath()) ("pimax-release-verify-" + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($downloadRoot) | Out-Null
    $published = $false
    try {
        Assert-DownloadedAssetHashes -GhInvoker $GhInvoker -Identity $context.Identity -ExpectedAssets $expectedAssets -DownloadDirectory $downloadRoot
        Write-Host "Approved draft inventory ($($expectedAssets.Count) assets):"
        Write-Host "  repository=$script:ExpectedRepository"
        Write-Host "  tag=$($context.Identity.Tag) version=$($context.Identity.Version) commit=$($context.Identity.SourceCommit)"
        Write-Host "  manifestKeyId=$ExpectedKeyId draft=$($release.draft) url=$($release.html_url)"
        foreach ($asset in $expectedAssets) {
            Write-Host "  $($asset.Sha256)  $($asset.Name)  $($asset.Size) bytes"
        }
        if (-not $Confirm) {
            throw 'Publication requires the explicit -Confirm switch and an exact interactive confirmation.'
        }
        $confirmation = & $ConfirmationProvider "Type PUBLISH $($context.Identity.Tag) to publish the immutable release"
        if ($confirmation -cne "PUBLISH $($context.Identity.Tag)") {
            throw 'Publication confirmation did not exactly match the required phrase.'
        }

        Invoke-RequiredAdapter -Adapter $GhInvoker -Arguments @(
            'release', 'edit', $context.Identity.Tag,
            '--repo', $script:ExpectedRepository,
            '--draft=false',
            '--latest'
        ) -Description 'publish approved draft exactly once' | Out-Null
        $published = $true

        $publishedRelease = Get-RemoteReleaseForTag -GhInvoker $GhInvoker -Tag $context.Identity.Tag
        Assert-ExactRemoteReleaseMetadata -Release $publishedRelease -Identity $context.Identity -ExpectedTitle $title -ExpectedBody $body -ExpectedDraft $false
        if ($publishedRelease.immutable -ne $true) {
            throw 'Published release metadata does not report immutable=true.'
        }
        Assert-RemoteAssetInventory -Release $publishedRelease -ExpectedAssets $expectedAssets | Out-Null
        Invoke-RequiredAdapter -Adapter $GhInvoker -Arguments @('release', 'verify', $context.Identity.Tag, '--repo', $script:ExpectedRepository) -Description 'GitHub immutable release verification' | Out-Null
        $verifiedReleaseAssets = [Collections.Generic.List[string]]::new()
        foreach ($asset in $expectedAssets) {
            Invoke-RequiredAdapter -Adapter $GhInvoker -Arguments @(
                'release', 'verify-asset', $context.Identity.Tag, $asset.Path, '--repo', $script:ExpectedRepository
            ) -Description "GitHub immutable release asset verification for $($asset.Name)" | Out-Null
            $verifiedReleaseAssets.Add($asset.Name)
        }
        $verifiedSubjects = @($context.Inventory.manifestFile) + @($context.Inventory.files | Where-Object { $_.fileName -like '*.zip' } | ForEach-Object fileName)
        foreach ($subject in $verifiedSubjects) {
            $subjectPath = Join-Path $context.Directory $subject
            $certificateIdentity = "https://github.com/$($context.Inventory.workflow.workflowRef)"
            Invoke-RequiredAdapter -Adapter $ProcessInvoker -Arguments @(
                'cosign', 'verify-blob',
                '--bundle', "$subjectPath.sigstore.json",
                '--certificate-identity', $certificateIdentity,
                '--certificate-oidc-issuer', 'https://token.actions.githubusercontent.com',
                $subjectPath
            ) -Description "Sigstore verification for $subject" | Out-Null
            Invoke-RequiredAdapter -Adapter $GhInvoker -Arguments @(
                'attestation', 'verify', $subjectPath,
                '--repo', $script:ExpectedRepository,
                '--bundle', "$subjectPath.attestation.json",
                '--signer-workflow', "$script:ExpectedRepository/.github/workflows/release.yml"
            ) -Description "GitHub artifact attestation verification for $subject" | Out-Null
        }
        Test-OfflineManifestSignature -CandidateContext $context -ApprovedPublicKeyPath $approvedPublicKey -ExpectedKeyId $ExpectedKeyId | Out-Null
        Test-Sha256SumsFile -CandidateContext $context
        $reportPath = Join-Path $reportRoot "post-publish-$($context.Identity.Tag)-verified.json"
        Write-BoundedReleaseReport -Path $reportPath -Value ([ordered]@{
            schemaVersion = 1
            status = 'published-and-verified'
            repository = $script:ExpectedRepository
            tag = $context.Identity.Tag
            sourceCommit = $context.Identity.SourceCommit
            assetCount = $expectedAssets.Count
            manifestSha256 = $context.ManifestSha256
            checks = [ordered]@{
                releaseMetadata = 'passed'
                immutableReleaseAttestation = 'passed'
                releaseAssets = @($verifiedReleaseAssets)
                sha256Sums = 'passed'
                detachedEcdsa = 'passed'
                sigstoreSubjects = @($verifiedSubjects)
                artifactAttestationSubjects = @($verifiedSubjects)
            }
            verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
        })
        [pscustomobject]@{ Status = 'published-and-verified'; Tag = $context.Identity.Tag; ReportPath = $reportPath }
    } catch {
        if ($published) {
            $incidentPath = Join-Path $reportRoot "post-publish-$($context.Identity.Tag)-incident.json"
            Write-BoundedReleaseReport -Path $incidentPath -Value ([ordered]@{
                schemaVersion = 1
                status = 'published-verification-failed'
                repository = $script:ExpectedRepository
                tag = $context.Identity.Tag
                sourceCommit = $context.Identity.SourceCommit
                error = $_.Exception.Message.Substring(0, [Math]::Min(2048, $_.Exception.Message.Length))
                recordedAtUtc = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
                remoteMutationAttemptedAfterFailure = $false
            })
            Write-Error "The release was published but post-publication verification failed. No repair mutation was attempted. Incident report: $incidentPath"
        }
        throw
    } finally {
        if (Test-Path -LiteralPath $downloadRoot) {
            Remove-Item -LiteralPath $downloadRoot -Recurse -Force
        }
    }
}

Export-ModuleMember -Function @(
    'Get-ReleasePipelineConstants',
    'Assert-StrictReleaseIdentity',
    'Test-ReleaseComponentVersions',
    'New-ExactReleaseManifest',
    'Complete-ReleaseCandidate',
    'Test-ReleaseCandidateDirectory',
    'Invoke-OfflineManifestSigningCore',
    'Test-OfflineManifestSignature',
    'Invoke-ImmutableReleasePublishCore'
)
