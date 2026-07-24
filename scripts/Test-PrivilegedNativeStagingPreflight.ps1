[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ProbeRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'


function Get-Win32ErrorCode {
    param([System.Exception]$Exception)

    if ($Exception -is [System.ComponentModel.Win32Exception]) {
        return $Exception.NativeErrorCode
    }

    return [Runtime.InteropServices.Marshal]::GetHRForException($Exception)
}

$result = [ordered]@{
    schemaVersion = 1
    classification = 'ENVIRONMENT_BLOCKED'
    windows = $false
    probeRoot = $ProbeRoot
    probeRootIsRepositoryOrDeploymentPath = $false
    fixedNtfs = $false
    junctionCreated = $false
    symbolicLinkCreated = $false
    errors = @()
}

$probePath = [IO.Path]::GetFullPath($ProbeRoot)
$repositoryPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$probeRoot = [IO.Path]::GetPathRoot($probePath)
if (([string]::IsNullOrWhiteSpace($probeRoot)) -or
    (-not [string]::Equals([IO.Path]::GetDirectoryName($probePath).TrimEnd('\'), $probeRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) -or
    (-not [string]::Equals([IO.Path]::GetFileName($probePath), 'PimaxVrcSupervisor-PrivilegedNativeValidation', [StringComparison]::Ordinal))) {
    $result.errors += 'Probe root must be the exact PimaxVrcSupervisor-PrivilegedNativeValidation direct child of a local volume root.'
    $result | ConvertTo-Json -Depth 4
    exit 2
}

$protectedPaths = @(
    $repositoryPath,
    [Environment]::GetFolderPath('UserProfile'),
    [Environment]::GetFolderPath('LocalApplicationData'),
    [IO.Path]::GetTempPath(),
    $env:WINDIR,
    $env:ProgramFiles,
    ${env:ProgramFiles(x86)},
    'C:\PimaxVrcSupervisor-TestDeployments'
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\') }

$intersectsProtectedPath = @($protectedPaths | Where-Object {
    $probePath.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) -or $_.StartsWith($probePath.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
}).Count -gt 0
if ($probePath.StartsWith($repositoryPath, [StringComparison]::OrdinalIgnoreCase) -or $intersectsProtectedPath) {
    $result.probeRootIsRepositoryOrDeploymentPath = $true
    $result.errors += 'Probe root must not contain, be contained by, or equal a protected path.'
    $result | ConvertTo-Json -Depth 4
    exit 2
}

if (-not $IsWindows) {
    $result.errors += 'Windows is required.'
    $result | ConvertTo-Json -Depth 4
    exit 2
}

$probe = $null
try {
    $root = [IO.Path]::GetPathRoot($probePath)
    if (-not [IO.Directory]::Exists($probePath)) {
        throw 'The authorized validation root does not exist.'
    }
    if ((Get-Item -LiteralPath $probePath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'The authorized validation root must not be a reparse point.'
    }
    $drive = [IO.DriveInfo]::new($root)
    $result.windows = $true
    $result.fixedNtfs = $drive.DriveType -eq [IO.DriveType]::Fixed -and $drive.DriveFormat -eq 'NTFS'
    if (-not $result.fixedNtfs) {
        $result.errors += "Probe root must be on a local fixed NTFS volume; found $($drive.DriveType)/$($drive.DriveFormat)."
        $result | ConvertTo-Json -Depth 4
        exit 2
    }

    $probe = Join-Path $probePath ('pimax-native-preflight-' + [Guid]::NewGuid().ToString('N'))
    $target = Join-Path $probe 'target'
    $junction = Join-Path $probe 'junction'
    $symbolicLink = Join-Path $probe 'symbolic-link'
    [IO.Directory]::CreateDirectory($target) | Out-Null

    try {
        $process = Start-Process -FilePath "$env:ComSpec" -ArgumentList '/d', '/s', '/c', "mklink /J `"$junction`" `"$target`"" -Wait -PassThru -NoNewWindow
        if ($process.ExitCode -ne 0 -or -not [IO.Directory]::Exists($junction)) {
            throw [ComponentModel.Win32Exception]::new($process.ExitCode, 'mklink /J failed')
        }
        $result.junctionCreated = $true
    }
    catch {
        $result.errors += "junction:$((Get-Win32ErrorCode $_.Exception)):$($_.Exception.Message)"
    }

    try {
        [IO.Directory]::CreateSymbolicLink($symbolicLink, $target) | Out-Null
        if (-not [IO.Directory]::Exists($symbolicLink)) {
            throw [ComponentModel.Win32Exception]::new(0, 'Directory.CreateSymbolicLink did not create the link')
        }
        $result.symbolicLinkCreated = $true
    }
    catch {
        $result.errors += "symbolicLink:$((Get-Win32ErrorCode $_.Exception)):$($_.Exception.Message)"
    }

    if ($result.junctionCreated -and $result.symbolicLinkCreated) {
        $result.classification = 'PASS'
    }
}
finally {
    if ($probe -and [IO.Directory]::Exists($probe)) {
        cmd.exe /d /s /c "rmdir `"$junction`"" 2>$null | Out-Null
        cmd.exe /d /s /c "rmdir `"$symbolicLink`"" 2>$null | Out-Null
        [IO.Directory]::Delete($probe, $true)
    }
}

$result | ConvertTo-Json -Depth 4
if ($result.classification -ne 'PASS') { exit 2 }
