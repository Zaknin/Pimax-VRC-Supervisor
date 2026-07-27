[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$framework = 'net9.0-windows10.0.19041.0'
$helperPath = Join-Path $repoRoot "PimaxVrcSupervisor.UpdateCheckTestHelper\bin\$Configuration\$framework\PimaxVrcSupervisor.UpdateCheckTestHelper.exe"
$workerPath = Join-Path $repoRoot "PimaxVrcSupervisor.UpdateWorker\bin\$Configuration\$framework\PimaxVrcSupervisor.UpdateWorker.exe"
$supervisorPath = Join-Path $repoRoot "PimaxVrcSupervisor\bin\$Configuration\$framework\PimaxVrcSupervisor.exe"

foreach ($path in @($helperPath, $workerPath, $supervisorPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing required built executable: $path"
    }
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("PimaxVrcSupervisor-MixedIntegrity-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null

function Wait-ForFile {
    param([string]$Path, [int]$TimeoutSeconds = 12)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        if ([DateTime]::UtcNow -ge $deadline) {
            throw "Timed out waiting for $Path"
        }

        Start-Sleep -Milliseconds 50
    }
}

function Assert-AlreadyRunning {
    param([string]$Json, [int]$ExitCode, [string]$Scenario)

    $result = $Json | ConvertFrom-Json
    if ($ExitCode -ne 1 -or $result.resultCode -ne 'already_running' -or $result.status -ne $null) {
        throw "$Scenario did not fail before queued execution. exit=$ExitCode result=$Json"
    }
}

try {
    $standardReady = Join-Path $temp 'standard-ready'
    $standardRelease = Join-Path $temp 'standard-release'
    $standardHolder = Start-Process -FilePath $helperPath -ArgumentList @('hold', $standardReady, $standardRelease) -PassThru
    Wait-ForFile -Path $standardReady

    $elevatedSupervisorResult = Join-Path $temp 'elevated-supervisor-result.json'
    $elevatedSupervisorExit = Join-Path $temp 'elevated-supervisor-exit.txt'
    $command = '"' + $supervisorPath + '" --update-check-once --source configurator > "' + $elevatedSupervisorResult + '" 2>&1 & echo %ERRORLEVEL% > "' + $elevatedSupervisorExit + '"'
    Start-Process -FilePath $env:ComSpec -ArgumentList '/d', '/s', '/c', $command -Verb RunAs | Out-Null
    Wait-ForFile -Path $elevatedSupervisorExit
    Assert-AlreadyRunning -Json (Get-Content -LiteralPath $elevatedSupervisorResult -Raw) -ExitCode ([int](Get-Content -LiteralPath $elevatedSupervisorExit -Raw)) -Scenario 'Unelevated gate owner versus elevated Supervisor'

    Set-Content -LiteralPath $standardRelease -Value 'release'
    $standardHolder.WaitForExit()
    if ($standardHolder.ExitCode -ne 0) {
        throw "The standard-integrity holder did not release the gate. exit=$($standardHolder.ExitCode)"
    }

    $elevatedReady = Join-Path $temp 'elevated-ready'
    $elevatedRelease = Join-Path $temp 'elevated-release'
    $elevatedHolder = Start-Process -FilePath $helperPath -ArgumentList @('hold', $elevatedReady, $elevatedRelease) -Verb RunAs -PassThru
    Wait-ForFile -Path $elevatedReady

    $workerJson = & $workerPath --update-check-once --source configurator | Out-String
    Assert-AlreadyRunning -Json $workerJson -ExitCode $LASTEXITCODE -Scenario 'Elevated gate owner versus unelevated UpdateWorker'

    Set-Content -LiteralPath $elevatedRelease -Value 'release'
    $elevatedHolder.WaitForExit()
    if ($elevatedHolder.ExitCode -ne 0) {
        throw "The elevated holder did not release the gate. exit=$($elevatedHolder.ExitCode)"
    }

    $afterRelease = & $helperPath try | Out-String
    if ($LASTEXITCODE -ne 0 -or $afterRelease.Trim() -ne 'acquired') {
        throw "The shared gate was not available after release. exit=$LASTEXITCODE output=$afterRelease"
    }

    Write-Host 'PASS: standard and elevated processes contended on the same SID-derived Global mutex; both rejections were immediate already_running responses without an operation status; the gate became available after release.'
    Write-Host 'Run the automated UpdateCheckAdmissionSecurityTests separately to prove a malformed or unreadable descriptor fails closed as gate_unavailable without relaxed owner/DACL validation.'
}
finally {
    if (Test-Path -LiteralPath $temp) {
        Remove-Item -LiteralPath $temp -Recurse -Force
    }
}
