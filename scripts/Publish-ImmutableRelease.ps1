[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$ApprovedPublicKeyPath,
    [Parameter(Mandatory)][string]$ExpectedKeyId,
    [Parameter(Mandatory)][string]$ReportDirectory,
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [switch]$Confirm
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleasePipeline.psm1') -Force

Invoke-ImmutableReleasePublishCore `
    -CandidateDirectory $CandidateDirectory `
    -ApprovedPublicKeyPath $ApprovedPublicKeyPath `
    -ExpectedKeyId $ExpectedKeyId `
    -ReportDirectory $ReportDirectory `
    -RepositoryRoot $RepositoryRoot `
    -Confirm:$Confirm
