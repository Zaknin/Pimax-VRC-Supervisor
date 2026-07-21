[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$KeyId,
    [Parameter(Mandatory)][string]$PrivateKeyPath,
    [Parameter(Mandatory)][string]$PublicKeyPath,
    [Parameter(Mandatory)][string]$RecordPath,
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string]$DeploymentRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PimaxVrcSupervisor-TestDeployments')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ProductionUpdateKeyCeremony.psm1') -Force

Invoke-ProductionUpdateKeyCeremony `
    -KeyId $KeyId `
    -PrivateKeyPath $PrivateKeyPath `
    -PublicKeyPath $PublicKeyPath `
    -RecordPath $RecordPath `
    -RepositoryRoot $RepositoryRoot `
    -DeploymentRoot $DeploymentRoot | Out-Null
