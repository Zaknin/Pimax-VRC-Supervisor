[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
Import-Module (Join-Path $repositoryRoot 'scripts\ReleasePipeline.psm1') -Force
$script:AssertionCount = 0

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    $script:AssertionCount++
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

function Assert-Throws {
    param([Parameter(Mandatory)][scriptblock]$Action, [Parameter(Mandatory)][string]$MessagePattern)
    $script:AssertionCount++
    try {
        & $Action
    } catch {
        if ($_.Exception.Message -notmatch $MessagePattern) {
            throw "ASSERTION FAILED: expected error /$MessagePattern/ but received: $($_.Exception.Message)"
        }
        return
    }
    throw "ASSERTION FAILED: expected action to throw /$MessagePattern/."
}

function Write-TestText {
    param([string]$Path, [string]$Text)
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function New-TestCandidate {
    param([string]$Directory, [string]$SourceCommit = ('a' * 40))

    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $version = '1.4.0'
    $tag = 'v1.4.0'
    $packages = @(
        "PimaxVrcSupervisor-$tag-win-x64-with-dotnet9.zip",
        "PimaxVrcSupervisor-$tag-win-x64-no-dotnet9.zip"
    )
    foreach ($package in $packages) {
        Write-TestText -Path (Join-Path $Directory $package) -Text "test package $package"
        $hash = (Get-FileHash -LiteralPath (Join-Path $Directory $package) -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-TestText -Path (Join-Path $Directory "$package.sha256") -Text "$hash  $package"
        Write-TestText -Path (Join-Path $Directory "$package.sigstore.json") -Text '{"testBundle":true}'
        Write-TestText -Path (Join-Path $Directory "$package.attestation.json") -Text '{"testAttestation":true}'
    }
    $manifestResult = New-ExactReleaseManifest `
        -CandidateDirectory $Directory `
        -Version $version `
        -Tag $tag `
        -SourceCommit $SourceCommit `
        -Channel stable `
        -GeneratedAtUtc '2026-07-21T00:00:00Z'
    Write-TestText -Path "$($manifestResult.ManifestPath).sigstore.json" -Text '{"testBundle":true}'
    Write-TestText -Path "$($manifestResult.ManifestPath).attestation.json" -Text '{"testAttestation":true}'
    Complete-ReleaseCandidate `
        -CandidateDirectory $Directory `
        -Version $version `
        -Tag $tag `
        -SourceCommit $SourceCommit `
        -Channel stable `
        -WorkflowRunId '12345' `
        -WorkflowRunAttempt '1' `
        -WorkflowRunUrl 'https://github.com/Zaknin/Pimax-VRC-Supervisor/actions/runs/12345' `
        -WorkflowRef 'Zaknin/Pimax-VRC-Supervisor/.github/workflows/release.yml@refs/heads/main'
    [pscustomobject]@{ Version = $version; Tag = $tag; SourceCommit = $SourceCommit; ManifestPath = $manifestResult.ManifestPath }
}

function New-TestKeyPair {
    param([string]$Directory, [string]$Name = 'test')

    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $privatePath = Join-Path $Directory "$Name-private.pem"
    $publicPath = Join-Path $Directory "$Name-public.pem"
    $key = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
    try {
        Write-TestText -Path $privatePath -Text $key.ExportPkcs8PrivateKeyPem()
        Write-TestText -Path $publicPath -Text $key.ExportSubjectPublicKeyInfoPem()
    } finally {
        $key.Dispose()
    }
    [pscustomobject]@{ Private = $privatePath; Public = $publicPath }
}

function Update-TestManifest {
    param(
        [Parameter(Mandatory)][string]$CandidateDirectory,
        [Parameter(Mandatory)][scriptblock]$Mutation
    )

    $manifestPath = Get-ChildItem -LiteralPath $CandidateDirectory -File -Filter '*-update-manifest-v1.json' |
        Where-Object { $_.Name -notlike '*.signatures.json' } |
        Select-Object -First 1 -ExpandProperty FullName
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 30
    & $Mutation $manifest
    Write-TestText -Path $manifestPath -Text ($manifest | ConvertTo-Json -Depth 30 -Compress)
    $manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumPath = "$manifestPath.sha256"
    Write-TestText -Path $checksumPath -Text "$manifestHash  $([IO.Path]::GetFileName($manifestPath))"
    $checksumHash = (Get-FileHash -LiteralPath $checksumPath -Algorithm SHA256).Hash.ToLowerInvariant()

    $sumsPath = Join-Path $CandidateDirectory 'SHA256SUMS.txt'
    $sums = [Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadAllLines($sumsPath)) {
        if ($line.EndsWith("  $([IO.Path]::GetFileName($manifestPath))", [StringComparison]::Ordinal)) {
            $sums.Add("$manifestHash  $([IO.Path]::GetFileName($manifestPath))")
        } elseif ($line.EndsWith("  $([IO.Path]::GetFileName($checksumPath))", [StringComparison]::Ordinal)) {
            $sums.Add("$checksumHash  $([IO.Path]::GetFileName($checksumPath))")
        } else {
            $sums.Add($line)
        }
    }
    Write-TestText -Path $sumsPath -Text ($sums -join "`n")

    $inventoryPath = Join-Path $CandidateDirectory 'release-candidate-inventory.json'
    $inventory = Get-Content -LiteralPath $inventoryPath -Raw | ConvertFrom-Json -Depth 30
    $inventory.manifestSha256 = $manifestHash
    foreach ($path in @($manifestPath, $checksumPath, $sumsPath)) {
        $file = Get-Item -LiteralPath $path
        $entry = @($inventory.files | Where-Object { $_.fileName -ceq $file.Name })[0]
        $entry.sizeBytes = $file.Length
        $entry.sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    Write-TestText -Path $inventoryPath -Text ($inventory | ConvertTo-Json -Depth 30)
}

function New-CommandResult {
    param([int]$ExitCode = 0, [string]$StdOut = '', [string]$StdErr = '')
    [pscustomobject]@{ ExitCode = $ExitCode; StdOut = $StdOut; StdErr = $StdErr }
}

function New-PublisherHarness {
    param(
        [Parameter(Mandatory)]$Fixture,
        [Parameter(Mandatory)][string]$CandidateDirectory,
        [bool]$ImmutableEnabled = $true,
        [string]$RemoteCommit = $Fixture.SourceCommit,
        [string]$AuthenticatedLogin = 'Zaknin',
        [string]$RepositoryIdentity = 'Zaknin/Pimax-VRC-Supervisor',
        [switch]$CorruptDownload,
        [switch]$FailPostVerify
    )

    $state = [pscustomobject]@{
        Release = $null
        Mutations = [Collections.Generic.List[string]]::new()
        Commands = [Collections.Generic.List[string]]::new()
        PublishCount = 0
        ImmutableEnabled = $ImmutableEnabled
        RemoteCommit = $RemoteCommit
        FailPostVerify = [bool]$FailPostVerify
        AuthenticatedLogin = $AuthenticatedLogin
        RepositoryIdentity = $RepositoryIdentity
        CorruptDownload = [bool]$CorruptDownload
    }
    $makeResult = {
        param([int]$ExitCode = 0, [string]$StdOut = '', [string]$StdErr = '')
        [pscustomobject]@{ ExitCode = $ExitCode; StdOut = $StdOut; StdErr = $StdErr }
    }.GetNewClosure()
    $gh = {
        param([string[]]$CommandArguments)
        $command = $CommandArguments -join ' '
        $state.Commands.Add($command)
        if ($command -eq 'api user --jq .login') { return & $makeResult 0 "$($state.AuthenticatedLogin)`n" '' }
        if ($command -eq 'repo view Zaknin/Pimax-VRC-Supervisor --json nameWithOwner --jq .nameWithOwner') { return & $makeResult 0 "$($state.RepositoryIdentity)`n" '' }
        if ($command -like 'api -H X-GitHub-Api-Version:*') { return & $makeResult 0 (@{ enabled = $state.ImmutableEnabled } | ConvertTo-Json -Compress) '' }
        if ($command -like 'api repos/Zaknin/Pimax-VRC-Supervisor/commits/*') { return & $makeResult 0 "$($state.RemoteCommit)`n" '' }
        if ($command -eq 'api --paginate --slurp repos/Zaknin/Pimax-VRC-Supervisor/releases?per_page=100') {
            $releases = if ($null -eq $state.Release) { @() } else { @($state.Release) }
            return & $makeResult 0 (ConvertTo-Json -InputObject @($releases) -Depth 20 -Compress) ''
        }
        if ($CommandArguments[0] -ceq 'release' -and $CommandArguments[1] -ceq 'create') {
            $state.Mutations.Add('create')
            $state.Release = [pscustomobject]@{
                tag_name = $Fixture.Tag
                name = "Pimax VRC Supervisor $($Fixture.Tag)"
                body = "Immutable release for Pimax VRC Supervisor $($Fixture.Tag). See RELEASE_NOTES.md in the package."
                target_commitish = $Fixture.SourceCommit
                html_url = "https://github.com/Zaknin/Pimax-VRC-Supervisor/releases/tag/$($Fixture.Tag)"
                draft = $true
                prerelease = $false
                immutable = $false
                assets = @()
            }
            return & $makeResult
        }
        if ($CommandArguments[0] -ceq 'release' -and $CommandArguments[1] -ceq 'upload') {
            $state.Mutations.Add('upload')
            $paths = @($CommandArguments | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
            $state.Release.assets = @($state.Release.assets) + @($paths | ForEach-Object {
                $file = Get-Item -LiteralPath $_
                [pscustomobject]@{ name = $file.Name; size = $file.Length }
            })
            return & $makeResult
        }
        if ($CommandArguments[0] -ceq 'release' -and $CommandArguments[1] -ceq 'download') {
            $name = $CommandArguments[[Array]::IndexOf($CommandArguments, '--pattern') + 1]
            $destination = $CommandArguments[[Array]::IndexOf($CommandArguments, '--dir') + 1]
            Copy-Item -LiteralPath (Join-Path $CandidateDirectory $name) -Destination (Join-Path $destination $name)
            if ($state.CorruptDownload) {
                $downloadedPath = Join-Path $destination $name
                $length = (Get-Item -LiteralPath $downloadedPath).Length
                [IO.File]::WriteAllBytes($downloadedPath, [byte[]]::new($length))
            }
            return & $makeResult
        }
        if ($CommandArguments[0] -ceq 'release' -and $CommandArguments[1] -ceq 'edit') {
            $state.Mutations.Add('publish')
            $state.PublishCount++
            $state.Release.draft = $false
            $state.Release.immutable = $true
            return & $makeResult
        }
        if ($CommandArguments[0] -ceq 'release' -and $CommandArguments[1] -ceq 'verify' -and $state.FailPostVerify) {
            return & $makeResult 1 '' 'mock post-publish verification failure'
        }
        if ($CommandArguments[0] -ceq 'release' -and $CommandArguments[1] -in @('verify', 'verify-asset')) { return & $makeResult }
        if ($CommandArguments[0] -ceq 'attestation') { return & $makeResult }
        throw "Unexpected mocked gh command: $command"
    }.GetNewClosure()
    $process = {
        param([string[]]$CommandArguments)
        if ($CommandArguments[0] -ceq 'git' -and $CommandArguments -contains 'rev-parse') { return & $makeResult 0 "$($Fixture.SourceCommit)`n" '' }
        if ($CommandArguments[0] -ceq 'git' -and $CommandArguments -contains 'status') { return & $makeResult }
        if ($CommandArguments[0] -ceq 'cosign') { return & $makeResult }
        throw "Unexpected mocked process command: $($CommandArguments -join ' ')"
    }.GetNewClosure()
    [pscustomobject]@{ State = $state; Gh = $gh; Process = $process }
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("pimax-release-tests-" + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    Assert-Throws -Action { Assert-StrictReleaseIdentity -Version '1.4.0-alpha' -Tag 'v1.4.0-alpha' -SourceCommit ('a' * 40) -Channel stable } -MessagePattern 'stable SemVer'
    Assert-Throws -Action { Assert-StrictReleaseIdentity -Version '1.4.0' -Tag 'v1.4.1' -SourceCommit ('a' * 40) -Channel stable } -MessagePattern 'exactly'
    Assert-Throws -Action { Assert-StrictReleaseIdentity -Version '1.3.1' -Tag 'v1.3.1' -SourceCommit ('a' * 40) -Channel stable } -MessagePattern 'Historical release'

    $candidate = Join-Path $testRoot 'candidate'
    $fixture = New-TestCandidate -Directory $candidate
    $context = Test-ReleaseCandidateDirectory -CandidateDirectory $candidate
    Assert-True -Condition ($context.BaseFiles.Count -eq 14) -Message 'candidate inventory has exactly fourteen CI-produced assets'
    Assert-True -Condition (@($context.Inventory.files | Where-Object packageVariant -ceq 'with-dotnet9').Count -eq 4) -Message 'with-dotnet9 variant inventory is exact'
    Assert-True -Condition (@($context.Inventory.files | Where-Object packageVariant -ceq 'no-dotnet9').Count -eq 4) -Message 'no-dotnet9 variant inventory is exact'

    $keys = New-TestKeyPair -Directory (Join-Path $testRoot 'keys')
    $signing = Invoke-OfflineManifestSigningCore -CandidateDirectory $candidate -PrivateKeyPath $keys.Private -KeyId 'release-current-test' -RepositoryRoot $repositoryRoot
    Assert-True -Condition (Test-Path -LiteralPath $signing.SignaturePath) -Message 'raw detached signature is created'
    Assert-True -Condition ((Get-Content -LiteralPath $signing.SignaturePath -Raw) -notmatch '[\r\n]') -Message 'raw detached signature is one exact Base64 value'
    $signedContext = Test-ReleaseCandidateDirectory -CandidateDirectory $candidate -AllowSigningOutputs
    $verified = Test-OfflineManifestSignature -CandidateContext $signedContext -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'release-current-test'
    Assert-True -Condition ($verified.PublicKeySha256 -ceq $signing.PublicKeySha256) -Message 'offline signature verifies with approved public key'
    Assert-Throws -Action { Invoke-OfflineManifestSigningCore -CandidateDirectory $candidate -PrivateKeyPath $keys.Private -KeyId 'release-current-test' -RepositoryRoot $repositoryRoot } -MessagePattern 'output already exists|file set mismatch'
    $overwritten = Invoke-OfflineManifestSigningCore -CandidateDirectory $candidate -PrivateKeyPath $keys.Private -KeyId 'release-current-test' -RepositoryRoot $repositoryRoot -Overwrite
    Assert-True -Condition ($overwritten.PublicKeySha256 -ceq $signing.PublicKeySha256) -Message 'explicit overwrite repeats the same reviewed key ceremony'
    Assert-Throws -Action { Test-OfflineManifestSignature -CandidateContext $signedContext -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'unknown-key' } -MessagePattern 'inconsistent'

    $wrongKeys = New-TestKeyPair -Directory (Join-Path $testRoot 'wrong-keys') -Name wrong
    Assert-Throws -Action { Test-OfflineManifestSignature -CandidateContext $signedContext -ApprovedPublicKeyPath $wrongKeys.Public -ExpectedKeyId 'release-current-test' } -MessagePattern 'fingerprint|verification failed'

    $alteredCandidate = Join-Path $testRoot 'altered'
    $alteredFixture = New-TestCandidate -Directory $alteredCandidate
    Add-Content -LiteralPath $alteredFixture.ManifestPath -Value ' ' -NoNewline
    Assert-Throws -Action { Test-ReleaseCandidateDirectory -CandidateDirectory $alteredCandidate } -MessagePattern 'size mismatch|SHA-256 mismatch|manifest hash'

    $missingVariantCandidate = Join-Path $testRoot 'missing-variant'
    New-TestCandidate -Directory $missingVariantCandidate | Out-Null
    Update-TestManifest -CandidateDirectory $missingVariantCandidate -Mutation { param($manifest) $manifest.assets = @($manifest.assets[0]) }
    Assert-Throws -Action { Test-ReleaseCandidateDirectory -CandidateDirectory $missingVariantCandidate } -MessagePattern 'each package variant exactly once'

    $duplicateVariantCandidate = Join-Path $testRoot 'duplicate-variant'
    New-TestCandidate -Directory $duplicateVariantCandidate | Out-Null
    Update-TestManifest -CandidateDirectory $duplicateVariantCandidate -Mutation { param($manifest) $manifest.assets[1].variant = 'with-dotnet9' }
    Assert-Throws -Action { Test-ReleaseCandidateDirectory -CandidateDirectory $duplicateVariantCandidate } -MessagePattern 'each package variant exactly once'

    $staleNameCandidate = Join-Path $testRoot 'stale-name'
    New-TestCandidate -Directory $staleNameCandidate | Out-Null
    Update-TestManifest -CandidateDirectory $staleNameCandidate -Mutation { param($manifest) $manifest.assets[0].fileName = 'PimaxVrcSupervisor-v1.3.0-win-x64-with-dotnet9.zip' }
    Assert-Throws -Action { Test-ReleaseCandidateDirectory -CandidateDirectory $staleNameCandidate } -MessagePattern 'package contract is invalid'

    $invalidSignatureCandidate = Join-Path $testRoot 'invalid-signature'
    Copy-Item -LiteralPath $candidate -Destination $invalidSignatureCandidate -Recurse
    Write-TestText -Path (Join-Path $invalidSignatureCandidate 'PimaxVrcSupervisor-v1.4.0-update-manifest-v1.json.sig') -Text 'AAAA'
    $invalidSignatureContext = Test-ReleaseCandidateDirectory -CandidateDirectory $invalidSignatureCandidate -AllowSigningOutputs
    Assert-Throws -Action { Test-OfflineManifestSignature -CandidateContext $invalidSignatureContext -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'release-current-test' } -MessagePattern 'inconsistent|verification failed'

    $missingSignatureCandidate = Join-Path $testRoot 'missing-signature'
    Copy-Item -LiteralPath $candidate -Destination $missingSignatureCandidate -Recurse
    Remove-Item -LiteralPath (Join-Path $missingSignatureCandidate 'PimaxVrcSupervisor-v1.4.0-update-manifest-v1.json.sig') -Force
    Assert-Throws -Action { Test-ReleaseCandidateDirectory -CandidateDirectory $missingSignatureCandidate -AllowSigningOutputs } -MessagePattern 'part of the offline signing output set'

    $boundaryCandidate = Join-Path $testRoot 'boundary'
    New-TestCandidate -Directory $boundaryCandidate | Out-Null
    Assert-Throws -Action { Invoke-OfflineManifestSigningCore -CandidateDirectory $boundaryCandidate -PrivateKeyPath (Join-Path $repositoryRoot 'scripts\ReleasePipeline.psm1') -KeyId 'release-current-test' -RepositoryRoot $repositoryRoot } -MessagePattern 'Sensitive key path'

    $reportDirectory = Join-Path $testRoot 'reports-success'
    $harness = New-PublisherHarness -Fixture $fixture -CandidateDirectory $candidate
    $publishResult = Invoke-ImmutableReleasePublishCore `
        -CandidateDirectory $candidate `
        -ApprovedPublicKeyPath $keys.Public `
        -ExpectedKeyId 'release-current-test' `
        -ReportDirectory $reportDirectory `
        -RepositoryRoot $repositoryRoot `
        -Confirm `
        -GhInvoker $harness.Gh `
        -ProcessInvoker $harness.Process `
        -ConfirmationProvider { param($Prompt) "PUBLISH $($fixture.Tag)" }
    Assert-True -Condition ($publishResult.Status -ceq 'published-and-verified') -Message 'publisher completes all post-publish verification'
    Assert-True -Condition ($harness.State.PublishCount -eq 1) -Message 'publisher publishes exactly once'
    Assert-True -Condition (($harness.State.Mutations -join ',') -ceq 'create,upload,publish') -Message 'remote mutation order is draft create, upload, publish'
    Assert-True -Condition (@($harness.State.Commands | Where-Object { $_ -match '--clobber' }).Count -eq 0) -Message 'publisher never uses clobber'
    Assert-True -Condition (@($harness.State.Commands | Where-Object { $_ -like 'release verify-asset *' }).Count -eq 18) -Message 'every final asset receives release-asset verification'

    Assert-Throws -Action {
        Invoke-ImmutableReleasePublishCore -CandidateDirectory $candidate -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'release-current-test' -ReportDirectory (Join-Path $testRoot 'reports-repeat') -RepositoryRoot $repositoryRoot -Confirm -GhInvoker $harness.Gh -ProcessInvoker $harness.Process -ConfirmationProvider { "PUBLISH $($fixture.Tag)" }
    } -MessagePattern 'already published'
    Assert-True -Condition ($harness.State.PublishCount -eq 1) -Message 'repeat invocation cannot mutate a published release'

    $immutableHarness = New-PublisherHarness -Fixture $fixture -CandidateDirectory $candidate -ImmutableEnabled $false
    Assert-Throws -Action {
        Invoke-ImmutableReleasePublishCore -CandidateDirectory $candidate -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'release-current-test' -ReportDirectory (Join-Path $testRoot 'reports-immutable') -RepositoryRoot $repositoryRoot -Confirm -GhInvoker $immutableHarness.Gh -ProcessInvoker $immutableHarness.Process -ConfirmationProvider { "PUBLISH $($fixture.Tag)" }
    } -MessagePattern 'immutability must be enabled'
    Assert-True -Condition ($immutableHarness.State.Mutations.Count -eq 0) -Message 'immutability failure occurs before remote mutation'

    $tagHarness = New-PublisherHarness -Fixture $fixture -CandidateDirectory $candidate -RemoteCommit ('b' * 40)
    Assert-Throws -Action {
        Invoke-ImmutableReleasePublishCore -CandidateDirectory $candidate -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'release-current-test' -ReportDirectory (Join-Path $testRoot 'reports-tag') -RepositoryRoot $repositoryRoot -Confirm -GhInvoker $tagHarness.Gh -ProcessInvoker $tagHarness.Process -ConfirmationProvider { "PUBLISH $($fixture.Tag)" }
    } -MessagePattern 'remote tag'
    Assert-True -Condition ($tagHarness.State.Mutations.Count -eq 0) -Message 'tag mismatch occurs before remote mutation'

    $authHarness = New-PublisherHarness -Fixture $fixture -CandidateDirectory $candidate -AuthenticatedLogin 'wrong-user'
    Assert-Throws -Action {
        Invoke-ImmutableReleasePublishCore -CandidateDirectory $candidate -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'release-current-test' -ReportDirectory (Join-Path $testRoot 'reports-auth') -RepositoryRoot $repositoryRoot -Confirm -GhInvoker $authHarness.Gh -ProcessInvoker $authHarness.Process -ConfirmationProvider { "PUBLISH $($fixture.Tag)" }
    } -MessagePattern 'authenticated as Zaknin'
    Assert-True -Condition ($authHarness.State.Mutations.Count -eq 0) -Message 'wrong authentication fails before remote mutation'

    $repoHarness = New-PublisherHarness -Fixture $fixture -CandidateDirectory $candidate -RepositoryIdentity 'Zaknin/another-repository'
    Assert-Throws -Action {
        Invoke-ImmutableReleasePublishCore -CandidateDirectory $candidate -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'release-current-test' -ReportDirectory (Join-Path $testRoot 'reports-repo') -RepositoryRoot $repositoryRoot -Confirm -GhInvoker $repoHarness.Gh -ProcessInvoker $repoHarness.Process -ConfirmationProvider { "PUBLISH $($fixture.Tag)" }
    } -MessagePattern 'repository identity'
    Assert-True -Condition ($repoHarness.State.Mutations.Count -eq 0) -Message 'wrong repository fails before remote mutation'

    $draftHarness = New-PublisherHarness -Fixture $fixture -CandidateDirectory $candidate
    $draftHarness.State.Release = [pscustomobject]@{
        tag_name = $fixture.Tag; name = 'Wrong title'; body = 'Wrong body'; target_commitish = $fixture.SourceCommit
        html_url = "https://github.com/Zaknin/Pimax-VRC-Supervisor/releases/tag/$($fixture.Tag)"
        draft = $true; prerelease = $false; immutable = $false; assets = @()
    }
    Assert-Throws -Action {
        Invoke-ImmutableReleasePublishCore -CandidateDirectory $candidate -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'release-current-test' -ReportDirectory (Join-Path $testRoot 'reports-draft') -RepositoryRoot $repositoryRoot -Confirm -GhInvoker $draftHarness.Gh -ProcessInvoker $draftHarness.Process -ConfirmationProvider { "PUBLISH $($fixture.Tag)" }
    } -MessagePattern 'exact approved metadata'
    Assert-True -Condition ($draftHarness.State.Mutations.Count -eq 0) -Message 'mismatched existing draft is not mutated'

    $unexpectedHarness = New-PublisherHarness -Fixture $fixture -CandidateDirectory $candidate
    $unexpectedHarness.State.Release = [pscustomobject]@{
        tag_name = $fixture.Tag; name = "Pimax VRC Supervisor $($fixture.Tag)"
        body = "Immutable release for Pimax VRC Supervisor $($fixture.Tag). See RELEASE_NOTES.md in the package."
        target_commitish = $fixture.SourceCommit
        html_url = "https://github.com/Zaknin/Pimax-VRC-Supervisor/releases/tag/$($fixture.Tag)"
        draft = $true; prerelease = $false; immutable = $false
        assets = @([pscustomobject]@{ name = 'unexpected.zip'; size = 1 })
    }
    Assert-Throws -Action {
        Invoke-ImmutableReleasePublishCore -CandidateDirectory $candidate -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'release-current-test' -ReportDirectory (Join-Path $testRoot 'reports-unexpected') -RepositoryRoot $repositoryRoot -Confirm -GhInvoker $unexpectedHarness.Gh -ProcessInvoker $unexpectedHarness.Process -ConfirmationProvider { "PUBLISH $($fixture.Tag)" }
    } -MessagePattern 'unexpected asset'
    Assert-True -Condition ($unexpectedHarness.State.Mutations.Count -eq 0) -Message 'unexpected draft asset prevents mutation'

    $corruptHarness = New-PublisherHarness -Fixture $fixture -CandidateDirectory $candidate -CorruptDownload
    Assert-Throws -Action {
        Invoke-ImmutableReleasePublishCore -CandidateDirectory $candidate -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'release-current-test' -ReportDirectory (Join-Path $testRoot 'reports-corrupt') -RepositoryRoot $repositoryRoot -Confirm -GhInvoker $corruptHarness.Gh -ProcessInvoker $corruptHarness.Process -ConfirmationProvider { "PUBLISH $($fixture.Tag)" }
    } -MessagePattern 'does not match the approved candidate'
    Assert-True -Condition ($corruptHarness.State.PublishCount -eq 0) -Message 'draft upload hash mismatch prevents publication'

    $confirmationHarness = New-PublisherHarness -Fixture $fixture -CandidateDirectory $candidate
    Assert-Throws -Action {
        Invoke-ImmutableReleasePublishCore -CandidateDirectory $candidate -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'release-current-test' -ReportDirectory (Join-Path $testRoot 'reports-confirm') -RepositoryRoot $repositoryRoot -Confirm -GhInvoker $confirmationHarness.Gh -ProcessInvoker $confirmationHarness.Process -ConfirmationProvider { 'NO' }
    } -MessagePattern 'confirmation did not exactly match'
    Assert-True -Condition ($confirmationHarness.State.PublishCount -eq 0) -Message 'exact operator confirmation is required'

    $incidentHarness = New-PublisherHarness -Fixture $fixture -CandidateDirectory $candidate -FailPostVerify
    Assert-Throws -Action {
        Invoke-ImmutableReleasePublishCore -CandidateDirectory $candidate -ApprovedPublicKeyPath $keys.Public -ExpectedKeyId 'release-current-test' -ReportDirectory (Join-Path $testRoot 'reports-incident') -RepositoryRoot $repositoryRoot -Confirm -GhInvoker $incidentHarness.Gh -ProcessInvoker $incidentHarness.Process -ConfirmationProvider { "PUBLISH $($fixture.Tag)" }
    } -MessagePattern 'immutable release verification|post-publication verification failed'
    Assert-True -Condition (($incidentHarness.State.Mutations -join ',') -ceq 'create,upload,publish') -Message 'no repair mutation follows post-publish failure'
    Assert-True -Condition (Test-Path -LiteralPath (Join-Path $testRoot "reports-incident\post-publish-$($fixture.Tag)-incident.json")) -Message 'post-publish failure creates a bounded incident report'

    $workflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github\workflows\release.yml') -Raw
    Assert-True -Condition ($workflow -match 'workflow_dispatch:') -Message 'release candidate workflow is manual'
    Assert-True -Condition ($workflow -notmatch '(?m)^\s*push:') -Message 'release candidate workflow has no tag-push trigger'
    Assert-True -Condition ($workflow -notmatch '\bgh\s+release\b') -Message 'release candidate workflow cannot create or mutate releases'
    Assert-True -Condition ($workflow -notmatch 'PRIVATE.KEY|privateKey|ECDSA_PRIVATE') -Message 'release candidate workflow has no offline ECDSA key input'
    Assert-True -Condition (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot '.github\workflows\sign-release-assets.yml'))) -Message 'post-publish signing workflow is retired'
    Assert-True -Condition ($workflow -match 'Prepare controlled native validation root') -Message 'release workflow prepares a runner-local controlled native validation root'
    Assert-True -Condition ($workflow -match 'GetPathRoot\(\$env:RUNNER_TEMP\)') -Message 'release workflow derives the controlled native validation volume from the runner-local temporary path'
    Assert-True -Condition ($workflow -match 'New-Item -ItemType Directory -Path \$validationRoot -Force') -Message 'release workflow creates the controlled native validation root when absent'
    Assert-True -Condition ($workflow -match 'WindowsIdentity\]::GetCurrent\(\)\.User') -Message 'release workflow resolves the protected validation ACL principal from the runner environment'
    Assert-True -Condition ($workflow -match 'PHASE33B_NATIVE_VALIDATION_ROOT=\$validationRoot') -Message 'release workflow propagates the runner-local validation root to native tests'
    Assert-True -Condition ($workflow -match 'Clean controlled native validation root') -Message 'release workflow has deterministic controlled native validation cleanup'
    Assert-True -Condition ($workflow -match 'Remove-Item -LiteralPath \$validationRoot -Force') -Message 'release workflow removes only an empty exact controlled native validation root'
    Assert-True -Condition ($workflow -notmatch 'DESKTOP-3V1929C|FucktoryVR') -Message 'release workflow contains no local machine or user principal dependency'
    Assert-True -Condition ($workflow -notmatch "C:\\\\PimaxVrcSupervisor-PrivilegedNativeValidation") -Message 'release workflow does not pin native validation to a fixed drive layout'

    $packageScript = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\package-release.ps1') -Raw
    Assert-True -Condition ($packageScript -match 'System\.IO\.Compression\.ZipFile\]::OpenRead') -Message 'package inventory uses managed ZIP inspection for native Windows paths'
    Assert-True -Condition ($packageScript -notmatch '\btar\s+-tf\b') -Message 'package inventory does not depend on an MSYS tar path conversion'

    Write-Host "Release pipeline tests passed: $script:AssertionCount assertions."
} finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
