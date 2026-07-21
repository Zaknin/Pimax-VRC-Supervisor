[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$SourceCommit,
    [Parameter(Mandatory)][string]$Channel,
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
Import-Module (Join-Path $PSScriptRoot 'ReleasePipeline.psm1') -Force

$identity = Assert-StrictReleaseIdentity -Version $Version -Tag $Tag -SourceCommit $SourceCommit -Channel $Channel
Test-ReleaseComponentVersions -RepositoryRoot $root -Version $Version

$head = (& git -C $root rev-parse HEAD 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $head -cne $identity.SourceCommit) {
    throw "Checked-out HEAD must exactly match source commit $($identity.SourceCommit). Actual: $head"
}

& git -C $root cat-file -e "$($identity.SourceCommit)^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Source commit does not resolve to a local commit: $($identity.SourceCommit)"
}

$remote = (& git -C $root remote get-url origin 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $remote -cnotmatch '^(https://github\.com/|git@github\.com:|ssh://git@github\.com/)Zaknin/Pimax-VRC-Supervisor(?:\.git)?$') {
    throw "The origin remote is not the fixed release repository. Actual: $remote"
}

Write-Host "Release identity validated: repository=Zaknin/Pimax-VRC-Supervisor version=$Version tag=$Tag commit=$SourceCommit channel=$Channel"
