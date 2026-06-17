param(
    [Parameter(Mandatory = $true)]
    [string]$DllPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputDir,

    [Parameter(Mandatory = $true)]
    [string]$StopFile,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedHash,

    [int]$IntervalSeconds = 2,

    [string]$StandardOutputPath = "",

    [string]$StandardErrorPath = ""
)

$ErrorActionPreference = "Stop"

function Write-JsonLine {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [object]$Value
    )

    $Value | ConvertTo-Json -Compress -Depth 8 | Add-Content -LiteralPath $Path -Encoding UTF8
}

New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$summaryPath = Join-Path $OutputDir "manual-control-timeline-summary.jsonl"
$errorPath = Join-Path $OutputDir "manual-control-errors.jsonl"
$finishedPath = Join-Path $OutputDir "manual-control-capture-finished.json"
$started = Get-Date
$index = 0

try {
    while (-not (Test-Path -LiteralPath $StopFile)) {
        $index++
        $stamp = Get-Date -Format "yyyyMMdd-HHmmss-fff"
        $currentHash = (Get-FileHash -LiteralPath $DllPath -Algorithm SHA256).Hash
        if ($currentHash -ne $ExpectedHash) {
            Write-JsonLine -Path $errorPath -Value ([ordered]@{
                index = $index
                at = (Get-Date).ToString("o")
                error = "DLL hash mismatch."
                expected = $ExpectedHash
                actual = $currentHash
            })
            break
        }

        $samplePath = Join-Path $OutputDir ("sample-{0:D4}-{1}.json" -f $index, $stamp)
        try {
            $sampleErrorPath = Join-Path $OutputDir ("sample-{0:D4}-{1}.stderr.txt" -f $index, $stamp)
            $output = & dotnet $DllPath pimax-registration-assessment-json 2> $sampleErrorPath
            $exitCode = $LASTEXITCODE
            $output | Set-Content -LiteralPath $samplePath -Encoding UTF8
            if ($exitCode -ne 0) {
                Write-JsonLine -Path $errorPath -Value ([ordered]@{
                    index = $index
                    at = (Get-Date).ToString("o")
                    error = "Diagnostic command failed."
                    exitCode = $exitCode
                    file = (Split-Path $samplePath -Leaf)
                    stderrFile = (Split-Path $sampleErrorPath -Leaf)
                })
            }
            else {
                $json = Get-Content -LiteralPath $samplePath -Raw | ConvertFrom-Json
                Write-JsonLine -Path $summaryPath -Value ([ordered]@{
                    index = $index
                    collectedAt = (Get-Date).ToString("o")
                    file = (Split-Path $samplePath -Leaf)
                    schemaVersion = $json.schemaVersion
                    state = $json.assessment.state
                    confidence = $json.assessment.confidence
                    explanation = $json.assessment.explanation
                    sha256 = (Get-FileHash -LiteralPath $samplePath -Algorithm SHA256).Hash
                })
            }
        }
        catch {
            Write-JsonLine -Path $errorPath -Value ([ordered]@{
                index = $index
                at = (Get-Date).ToString("o")
                error = $_.Exception.Message
            })
        }

        Start-Sleep -Seconds $IntervalSeconds
    }
}
finally {
    [ordered]@{
        event = "manual_control_capture_stopped"
        startedAt = $started.ToString("o")
        stoppedAt = (Get-Date).ToString("o")
        samples = $index
        stopFileSeen = (Test-Path -LiteralPath $StopFile)
        standardOutputPath = $StandardOutputPath
        standardErrorPath = $StandardErrorPath
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $finishedPath -Encoding UTF8
}
