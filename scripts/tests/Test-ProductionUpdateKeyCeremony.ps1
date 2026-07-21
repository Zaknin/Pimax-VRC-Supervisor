[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
Import-Module (Join-Path $repositoryRoot 'scripts\ProductionUpdateKeyCeremony.psm1') -Force
$script:AssertionCount = 0

function Assert-True {
    param([Parameter(Mandatory)][bool]$Condition, [Parameter(Mandatory)][string]$Message)
    $script:AssertionCount++
    if (-not $Condition) { throw "ASSERTION FAILED: $Message" }
}

function Assert-Throws {
    param([Parameter(Mandatory)][scriptblock]$Action, [Parameter(Mandatory)][string]$MessagePattern)
    $script:AssertionCount++
    try { & $Action } catch {
        if ($_.Exception.Message -notmatch $MessagePattern) {
            throw "ASSERTION FAILED: expected /$MessagePattern/ but received: $($_.Exception.Message)"
        }
        return
    }
    throw "ASSERTION FAILED: expected action to throw /$MessagePattern/."
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("pimax-key-ceremony-tests-" + [guid]::NewGuid().ToString('N'))
$outputRoot = Join-Path $testRoot 'operator-output'
$deploymentRoot = Join-Path $testRoot 'deployments'
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
[IO.Directory]::CreateDirectory($deploymentRoot) | Out-Null
try {
    $privatePath = Join-Path $outputRoot 'ephemeral.private.pk8'
    $publicPath = Join-Path $outputRoot 'ephemeral.public.spki.der'
    $recordPath = Join-Path $outputRoot 'ephemeral.ceremony.json'
    $result = Invoke-ProductionUpdateKeyCeremony `
        -KeyId 'pimax-update-ceremony-fixture' `
        -PrivateKeyPath $privatePath `
        -PublicKeyPath $publicPath `
        -RecordPath $recordPath `
        -RepositoryRoot $repositoryRoot `
        -DeploymentRoot $deploymentRoot

    Assert-True -Condition (Test-Path -LiteralPath $privatePath -PathType Leaf) -Message 'explicit private PKCS#8 path is created'
    Assert-True -Condition (Test-Path -LiteralPath $publicPath -PathType Leaf) -Message 'public DER path is created'
    Assert-True -Condition (Test-Path -LiteralPath $recordPath -PathType Leaf) -Message 'bounded ceremony record is created'
    Assert-True -Condition ((Get-Item -LiteralPath $recordPath).Length -lt 16KB) -Message 'ceremony record is bounded'
    Assert-True -Condition ((Get-FileHash -LiteralPath $publicPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $result.PublicKeySha256) -Message 'reported fingerprint matches exact public DER bytes'

    $recordText = Get-Content -LiteralPath $recordPath -Raw
    $record = $recordText | ConvertFrom-Json
    Assert-True -Condition ($record.keyId -ceq 'pimax-update-ceremony-fixture') -Message 'record contains exact keyId'
    Assert-True -Condition ($record.publicKeySha256 -ceq $result.PublicKeySha256) -Message 'record contains exact fingerprint'
    Assert-True -Condition ($record.privateKeyPathRecorded -eq $false) -Message 'record contains no private-key path'
    Assert-True -Condition (-not $recordText.Contains($privatePath, [StringComparison]::OrdinalIgnoreCase)) -Message 'record does not leak private-key path'

    $privateKey = [Security.Cryptography.ECDsa]::Create()
    $publicKey = [Security.Cryptography.ECDsa]::Create()
    try {
        $privateRead = 0
        $publicRead = 0
        $privateKey.ImportPkcs8PrivateKey([IO.File]::ReadAllBytes($privatePath), [ref]$privateRead)
        $publicKey.ImportSubjectPublicKeyInfo([IO.File]::ReadAllBytes($publicPath), [ref]$publicRead)
        $bytes = [Text.Encoding]::UTF8.GetBytes('ephemeral ceremony verification only')
        $signature = $privateKey.SignData($bytes, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)
        Assert-True -Condition ($publicKey.VerifyData($bytes, $signature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)) -Message 'exported private/public pair signs and verifies exact bytes'
    } finally {
        $privateKey.Dispose()
        $publicKey.Dispose()
    }

    Assert-Throws -Action {
        Invoke-ProductionUpdateKeyCeremony -KeyId 'pimax-update-ceremony-fixture' -PrivateKeyPath $privatePath -PublicKeyPath (Join-Path $outputRoot 'second.der') -RecordPath (Join-Path $outputRoot 'second.json') -RepositoryRoot $repositoryRoot -DeploymentRoot $deploymentRoot
    } -MessagePattern 'already exists'
    Assert-Throws -Action {
        Invoke-ProductionUpdateKeyCeremony -KeyId 'test-production-key' -PrivateKeyPath (Join-Path $outputRoot 'test.private.pk8') -PublicKeyPath (Join-Path $outputRoot 'test.der') -RecordPath (Join-Path $outputRoot 'test.json') -RepositoryRoot $repositoryRoot -DeploymentRoot $deploymentRoot
    } -MessagePattern 'test-only prefix'
    Assert-Throws -Action {
        Invoke-ProductionUpdateKeyCeremony -KeyId 'pimax-update-unsafe-repo' -PrivateKeyPath (Join-Path $repositoryRoot 'unsafe.private.pk8') -PublicKeyPath (Join-Path $outputRoot 'unsafe.der') -RecordPath (Join-Path $outputRoot 'unsafe.json') -RepositoryRoot $repositoryRoot -DeploymentRoot $deploymentRoot
    } -MessagePattern 'outside'
    Assert-Throws -Action {
        Invoke-ProductionUpdateKeyCeremony -KeyId 'pimax-update-unsafe-deployment' -PrivateKeyPath (Join-Path $deploymentRoot 'unsafe.private.pk8') -PublicKeyPath (Join-Path $outputRoot 'unsafe-deploy.der') -RecordPath (Join-Path $outputRoot 'unsafe-deploy.json') -RepositoryRoot $repositoryRoot -DeploymentRoot $deploymentRoot
    } -MessagePattern 'outside'
    Assert-Throws -Action {
        Invoke-ProductionUpdateKeyCeremony -KeyId 'pimax-update-relative' -PrivateKeyPath '.\relative.pk8' -PublicKeyPath (Join-Path $outputRoot 'relative.der') -RecordPath (Join-Path $outputRoot 'relative.json') -RepositoryRoot $repositoryRoot -DeploymentRoot $deploymentRoot
    } -MessagePattern 'absolute'

    Write-Host "Production update key-ceremony tests passed: $script:AssertionCount assertions."
} finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
