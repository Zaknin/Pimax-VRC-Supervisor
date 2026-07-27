[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$validator = Join-Path $repositoryRoot 'scripts\Test-ProductionUpdateTrustDescriptor.ps1'
$source = Join-Path $repositoryRoot 'PimaxVrcSupervisor\ProductionUpdateTrustRoots.cs'
$expected = @{
    ExpectedKeyId = 'pimax-update-primary-2026'
    ExpectedAlgorithm = 'ecdsa-p256-sha256-der'
    ExpectedSize = 91
    ExpectedFingerprint = '929fa8e2a3a8d46064202a415f6c62e3e731f334be3de4c1d6d7045267371933'
    ExpectedRootCount = 1
}

$assertions = 0
function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
    $script:assertions++
}

function Invoke-Validator {
    param([string]$CandidateSource, [hashtable]$Descriptor = $expected)
    & $validator `
        -SourcePath $CandidateSource `
        -ExpectedKeyId $Descriptor.ExpectedKeyId `
        -ExpectedAlgorithm $Descriptor.ExpectedAlgorithm `
        -ExpectedSize $Descriptor.ExpectedSize `
        -ExpectedFingerprint $Descriptor.ExpectedFingerprint `
        -ExpectedRootCount $Descriptor.ExpectedRootCount 6>&1
}

function Assert-Rejected {
    param([scriptblock]$Action, [string]$Message)
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Assert-True $rejected $Message
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('pimax-update-trust-descriptor-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $output = Invoke-Validator -CandidateSource $source | Out-String
    Assert-True ([bool]($output -match 'pimax-update-primary-2026')) 'The approved descriptor did not validate.'

    foreach ($mutation in @(
        @{ Name = 'keyId'; From = 'pimax-update-primary-2026'; To = 'pimax-update-primary-2027' },
        @{ Name = 'algorithm'; From = 'ecdsa-p256-sha256-der'; To = 'ecdsa-p384-sha384-der' },
        @{ Name = 'size'; From = 'CurrentPublicKeySize = 91;'; To = 'CurrentPublicKeySize = 92;' },
        @{ Name = 'fingerprint'; From = $expected.ExpectedFingerprint; To = ('0' + $expected.ExpectedFingerprint.Substring(1)) },
        @{ Name = 'bytes'; From = 'MFkwEwYH'; To = 'MFkwEwYG' }
    )) {
        $candidate = Join-Path $temporaryRoot ("ProductionUpdateTrustRoots-$($mutation.Name).cs")
        $content = [IO.File]::ReadAllText($source).Replace($mutation.From, $mutation.To)
        [IO.File]::WriteAllText($candidate, $content, [Text.UTF8Encoding]::new($false))
        Assert-Rejected { Invoke-Validator -CandidateSource $candidate } "A changed $($mutation.Name) descriptor was accepted."
    }

    $wrongBuildDescriptor = $expected.Clone()
    $wrongBuildDescriptor.ExpectedFingerprint = 'f' * 64
    Assert-Rejected { Invoke-Validator -CandidateSource $source -Descriptor $wrongBuildDescriptor } 'A mismatched build descriptor was accepted.'

    $wrongRootCount = $expected.Clone()
    $wrongRootCount.ExpectedRootCount = 2
    Assert-Rejected { Invoke-Validator -CandidateSource $source -Descriptor $wrongRootCount } 'A mismatched trust-root count was accepted.'
} finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}

Write-Host "Production update trust descriptor tests passed: $assertions assertions."
