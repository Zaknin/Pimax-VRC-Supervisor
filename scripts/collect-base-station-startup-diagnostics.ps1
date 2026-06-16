param(
    [string]$OutputRoot = (Join-Path $env:TEMP "PimaxVrcSupervisorDiagnostics"),
    [int]$LogTailLines = 300
)

$ErrorActionPreference = "Stop"

function Redact-BluetoothAddress {
    param([string]$Text)
    if ($null -eq $Text) { return "" }
    return ($Text -replace '(?i)(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}|(?<![0-9a-f])[0-9a-f]{12}(?![0-9a-f])', '[redacted-address]')
}

function Copy-IfPresent {
    param(
        [string]$Source,
        [string]$Destination,
        [System.Collections.Generic.List[object]]$ManifestFiles,
        [switch]$SanitizeText
    )

    if (!(Test-Path -LiteralPath $Source)) {
        return
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    if ($SanitizeText) {
        Get-Content -LiteralPath $Source -Tail $LogTailLines -ErrorAction Stop |
            ForEach-Object { Redact-BluetoothAddress $_ } |
            Set-Content -LiteralPath $Destination -Encoding UTF8
    } else {
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
    }

    $item = Get-Item -LiteralPath $Destination
    $hash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
    $ManifestFiles.Add([ordered]@{
        path = $item.FullName
        name = $item.Name
        length = $item.Length
        sha256 = $hash
        sanitized = [bool]$SanitizeText
    }) | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outputDir = Join-Path $OutputRoot "BaseStationStartup-$timestamp"
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$files = [System.Collections.Generic.List[object]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
$baseStationDiagDir = Join-Path $env:LOCALAPPDATA "PimaxVrcSupervisor\Diagnostics\BaseStations"

if (Test-Path -LiteralPath $baseStationDiagDir) {
    Get-ChildItem -LiteralPath $baseStationDiagDir -File -Filter "*.jsonl*" |
        ForEach-Object {
            Copy-IfPresent -Source $_.FullName -Destination (Join-Path $outputDir ("base-station-events\" + $_.Name)) -ManifestFiles $files
        }
} else {
    $warnings.Add("Base-station diagnostics directory was not found: $baseStationDiagDir") | Out-Null
}

$candidateLogRoots = @(
    (Join-Path $env:TEMP "PimaxVrcSupervisorDiagnostics"),
    (Join-Path $env:LOCALAPPDATA "PimaxVrcSupervisor"),
    (Join-Path $env:APPDATA "PimaxVrcSupervisor")
) | Select-Object -Unique

foreach ($root in $candidateLogRoots) {
    if (!(Test-Path -LiteralPath $root)) {
        continue
    }

    Get-ChildItem -LiteralPath $root -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -match 'diagnostic|supervisor|configurator|base.?station|startup' -and
            $_.Extension -in '.log', '.txt', '.jsonl'
        } |
        Select-Object -First 20 |
        ForEach-Object {
            $relativeName = ($_.FullName.Substring($root.Length).TrimStart('\') -replace '[:\\\/]', '_')
            Copy-IfPresent -Source $_.FullName -Destination (Join-Path $outputDir ("log-tails\" + $relativeName + ".tail.txt")) -ManifestFiles $files -SanitizeText
        }
}

$processNames = @(
    "PimaxVrcSupervisor",
    "PimaxVrcSupervisorConfigurator",
    "PimaxVrcSupervisorSteamVrHost",
    "PimaxVrcSupervisorTui",
    "vrserver",
    "vrmonitor",
    "vrcompositor"
)

$processState = foreach ($name in $processNames) {
    Get-Process -Name $name -ErrorAction SilentlyContinue |
        Select-Object ProcessName, Id, StartTime, MainWindowTitle
}

$manifestPath = Join-Path $outputDir "base-station-diagnostics-manifest.json"
$notesPath = Join-Path $outputDir "base-station-diagnostics-notes.md"

$manifest = [ordered]@{
    schemaVersion = "base-station-startup-diagnostics-package-v1"
    collectedAt = (Get-Date).ToString("o")
    outputDirectory = $outputDir
    collectionMutatesSystem = $false
    diagnosticSchemaVersion = "base-station-startup-diagnostics-v1"
    collector = "scripts/collect-base-station-startup-diagnostics.ps1"
    windowsVersion = [System.Environment]::OSVersion.VersionString
    dotnetVersion = (& dotnet --version)
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    bluetoothAdapter = Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue |
        Select-Object FriendlyName, Status, Problem, InstanceId |
        ForEach-Object {
            [ordered]@{
                friendlyName = $_.FriendlyName
                status = $_.Status
                problem = $_.Problem
                instanceIdHash = if ($_.InstanceId) {
                    $bytes = [System.Text.Encoding]::UTF8.GetBytes($_.InstanceId)
                    $sha = [System.Security.Cryptography.SHA256]::Create()
                    try {
                        -join (($sha.ComputeHash($bytes) | Select-Object -First 8) | ForEach-Object { $_.ToString("x2") })
                    } finally {
                        $sha.Dispose()
                    }
                } else {
                    $null
                }
            }
        }
    processState = $processState
    files = $files
    warnings = $warnings
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

@"
# Base Station Startup Diagnostics Package

Collected: $($manifest.collectedAt)

This package is read-only. The collector copied existing base-station diagnostic events, sanitized relevant log tails, and captured process/Bluetooth adapter metadata. It did not scan for devices, start or stop processes, restart Bluetooth, power base stations on or off, or modify configuration.

## Contents

- Manifest: `base-station-diagnostics-manifest.json`
- Structured events: `base-station-events/`
- Sanitized log tails: `log-tails/`

## Warnings

$($warnings -join "`n")
"@ | Set-Content -LiteralPath $notesPath -Encoding UTF8

$files.Add([ordered]@{
    path = $manifestPath
    name = "base-station-diagnostics-manifest.json"
    length = (Get-Item -LiteralPath $manifestPath).Length
    sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    sanitized = $false
}) | Out-Null
$files.Add([ordered]@{
    path = $notesPath
    name = "base-station-diagnostics-notes.md"
    length = (Get-Item -LiteralPath $notesPath).Length
    sha256 = (Get-FileHash -LiteralPath $notesPath -Algorithm SHA256).Hash
    sanitized = $false
}) | Out-Null

$manifest.files = $files
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Output $outputDir
