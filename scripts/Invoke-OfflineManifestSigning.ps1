[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [Parameter(Mandatory)][string]$KeyId,
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [switch]$Overwrite
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleasePipeline.psm1') -Force

Invoke-OfflineManifestSigningCore `
    -CandidateDirectory $CandidateDirectory `
    -PrivateKeyPath $PrivateKeyPath `
    -KeyId $KeyId `
    -RepositoryRoot $RepositoryRoot `
    -Overwrite:$Overwrite | Out-Null
