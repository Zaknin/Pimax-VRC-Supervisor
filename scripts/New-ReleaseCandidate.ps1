[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Manifest', 'Complete')][string]$Stage,
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][string]$SourceCommit,
    [Parameter(Mandatory)][string]$Channel,
    [string]$GeneratedAtUtc,
    [string]$WorkflowRunId,
    [string]$WorkflowRunAttempt,
    [string]$WorkflowRunUrl,
    [string]$WorkflowRef
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleasePipeline.psm1') -Force

if ($Stage -ceq 'Manifest') {
    if ([string]::IsNullOrWhiteSpace($GeneratedAtUtc)) {
        throw 'GeneratedAtUtc is required for the Manifest stage.'
    }
    New-ExactReleaseManifest `
        -CandidateDirectory $CandidateDirectory `
        -Version $Version `
        -Tag $Tag `
        -SourceCommit $SourceCommit `
        -Channel $Channel `
        -GeneratedAtUtc $GeneratedAtUtc
    return
}

foreach ($requiredValue in @($WorkflowRunId, $WorkflowRunAttempt, $WorkflowRunUrl, $WorkflowRef)) {
    if ([string]::IsNullOrWhiteSpace($requiredValue)) {
        throw 'All workflow provenance fields are required for the Complete stage.'
    }
}
Complete-ReleaseCandidate `
    -CandidateDirectory $CandidateDirectory `
    -Version $Version `
    -Tag $Tag `
    -SourceCommit $SourceCommit `
    -Channel $Channel `
    -WorkflowRunId $WorkflowRunId `
    -WorkflowRunAttempt $WorkflowRunAttempt `
    -WorkflowRunUrl $WorkflowRunUrl `
    -WorkflowRef $WorkflowRef
