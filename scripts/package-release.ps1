param(
    [string]$Version = "v1.3.0-test",
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$ReleaseRoot = ".\release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $repoRoot

$releaseRootPath = Join-Path $repoRoot $ReleaseRoot
$withRuntimeName = "PimaxVrcSupervisor-$Version-$Runtime-with-dotnet9"
$withoutRuntimeName = "PimaxVrcSupervisor-$Version-$Runtime-no-dotnet9"
$withRuntimeDir = Join-Path $releaseRootPath $withRuntimeName
$withoutRuntimeDir = Join-Path $releaseRootPath $withoutRuntimeName
$withRuntimeZip = "$withRuntimeDir.zip"
$withoutRuntimeZip = "$withoutRuntimeDir.zip"

$projects = @(
    ".\PimaxVrcSupervisor\PimaxVrcSupervisor.csproj",
    ".\PimaxVrcSupervisor.ConfigEditor\PimaxVrcSupervisor.ConfigEditor.csproj",
    ".\PimaxVrcSupervisor.SteamVrHost\PimaxVrcSupervisor.SteamVrHost.csproj"
)

$cultureFolders = @(
    "cs", "de", "es", "fr", "it", "ja", "ko", "pl", "pt-BR", "ru", "tr", "zh-Hans", "zh-Hant"
)

$expectedFiles = @(
    "PimaxVrcSupervisor.exe",
    "PimaxVrcSupervisorConfigurator.exe",
    "PimaxVrcSupervisorSteamVrHost.exe",
    "PimaxVrcSupervisorTui.exe",
    "PimaxVrcSupervisorStartupHelper.exe",
    "PimaxVrcSupervisorWatcher.exe",
    "supervisor.config.json",
    "README.md",
    "RELEASE_NOTES.md",
    "Assets\vr-overlay-icon.png"
)

function Reset-Path {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Write-Host "Removing existing $Path"
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Publish-Variant {
    param(
        [string]$OutputDirectory,
        [bool]$SelfContained
    )

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $selfContainedValue = $SelfContained.ToString().ToLowerInvariant()

    foreach ($project in $projects) {
        Write-Host "Publishing $project to $OutputDirectory (self-contained=$SelfContained)"
        dotnet publish $project -c $Configuration -r $Runtime --self-contained $selfContainedValue -o $OutputDirectory
    }
}

function Copy-TerminalUi {
    param([string]$OutputDirectory)

    $tuiPath = Join-Path $repoRoot "PimaxVrcSupervisor.Tui\target\release\PimaxVrcSupervisorTui.exe"
    if (-not (Test-Path -LiteralPath $tuiPath)) {
        throw "Rust Terminal UI executable not found: $tuiPath"
    }

    Copy-Item -LiteralPath $tuiPath -Destination (Join-Path $OutputDirectory "PimaxVrcSupervisorTui.exe") -Force
}

function Copy-HelperExecutables {
    param([string]$OutputDirectory)

    $supervisorExe = Join-Path $OutputDirectory "PimaxVrcSupervisor.exe"
    if (-not (Test-Path -LiteralPath $supervisorExe)) {
        throw "Supervisor executable not found: $supervisorExe"
    }

    Copy-Item -LiteralPath $supervisorExe -Destination (Join-Path $OutputDirectory "PimaxVrcSupervisorStartupHelper.exe") -Force
    Copy-Item -LiteralPath $supervisorExe -Destination (Join-Path $OutputDirectory "PimaxVrcSupervisorWatcher.exe") -Force
}

function Remove-GeneratedClutter {
    param([string]$OutputDirectory)

    $filePatterns = @(
        "*.pdb",
        "*.bak",
        "supervisor.active-config.txt",
        "supervisor_moved.config*",
        "tui-diagnostics*.json"
    )

    foreach ($pattern in $filePatterns) {
        Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File -Filter $pattern -ErrorAction SilentlyContinue |
            ForEach-Object {
                Write-Host "Removing generated file $($_.FullName)"
                Remove-Item -LiteralPath $_.FullName -Force
            }
    }

    Get-ChildItem -LiteralPath $OutputDirectory -Directory -Recurse |
        Where-Object { $cultureFolders -contains $_.Name } |
        Sort-Object FullName -Descending |
        ForEach-Object {
            Write-Host "Removing satellite language folder $($_.FullName)"
            Remove-Item -LiteralPath $_.FullName -Recurse -Force
        }

    Get-ChildItem -LiteralPath (Join-Path $OutputDirectory "Assets") -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in @("app.ico", "config-editor.ico") } |
        ForEach-Object {
            Write-Host "Removing embedded icon source $($_.FullName)"
            Remove-Item -LiteralPath $_.FullName -Force
        }
}

function Test-ExpectedFiles {
    param([string]$OutputDirectory)

    $missing = foreach ($file in $expectedFiles) {
        $path = Join-Path $OutputDirectory $file
        if (-not (Test-Path -LiteralPath $path)) {
            $file
        }
    }

    if ($missing) {
        throw "Missing expected files in $OutputDirectory`: $($missing -join ', ')"
    }
}

function New-Zip {
    param(
        [string]$SourceDirectory,
        [string]$ZipPath
    )

    if (Test-Path -LiteralPath $ZipPath) {
        Write-Host "Removing existing $ZipPath"
        Remove-Item -LiteralPath $ZipPath -Force
    }

    Compress-Archive -Path (Join-Path $SourceDirectory "*") -DestinationPath $ZipPath -CompressionLevel Optimal
}

function Show-ZipSummary {
    param([string]$ZipPath)

    Write-Host ""
    Write-Host "--- $(Split-Path -Leaf $ZipPath) ---"
    $entries = @(tar -tf $ZipPath)
    $entries | ForEach-Object { $_ }
    Write-Host "Entries: $($entries.Count)"
}

New-Item -ItemType Directory -Force -Path $releaseRootPath | Out-Null
Reset-Path $withRuntimeDir
Reset-Path $withoutRuntimeDir

Publish-Variant -OutputDirectory $withRuntimeDir -SelfContained $true
Publish-Variant -OutputDirectory $withoutRuntimeDir -SelfContained $false

Copy-TerminalUi -OutputDirectory $withRuntimeDir
Copy-TerminalUi -OutputDirectory $withoutRuntimeDir

Copy-HelperExecutables -OutputDirectory $withRuntimeDir
Copy-HelperExecutables -OutputDirectory $withoutRuntimeDir

Remove-GeneratedClutter -OutputDirectory $withRuntimeDir
Remove-GeneratedClutter -OutputDirectory $withoutRuntimeDir

Test-ExpectedFiles -OutputDirectory $withRuntimeDir
Test-ExpectedFiles -OutputDirectory $withoutRuntimeDir

New-Zip -SourceDirectory $withRuntimeDir -ZipPath $withRuntimeZip
New-Zip -SourceDirectory $withoutRuntimeDir -ZipPath $withoutRuntimeZip

Show-ZipSummary -ZipPath $withRuntimeZip
Show-ZipSummary -ZipPath $withoutRuntimeZip

Write-Host ""
Write-Host "Created:"
Get-Item -LiteralPath $withRuntimeZip, $withoutRuntimeZip |
    Select-Object FullName, Length, LastWriteTime |
    Format-Table -AutoSize
