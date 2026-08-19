<#
.SYNOPSIS
Records every topic-qualified child identity for one split upstream source.

.DESCRIPTION
The manifest proves which child identities comprise the complete split. It does not mark the
parent resolved by itself: preflight still requires every child to have a fingerprinted target
issue or a recorded dismissal.

Call it once after the split identities and parity results are final.

.PARAMETER SourceIdentity
The unsplit immutable source identity, such as eve-client-upstream:pr:1806.

.PARAMETER ChildSourceIdentity
Every topic-qualified child identity belonging to the source.

.PARAMETER TargetCommit
The eve-client commit the split was analyzed against.

.PARAMETER UpstreamHead
The upstream main commit the delta was collected from.

.PARAMETER StatePath
Radar-owned durable state file.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourceIdentity,
    [Parameter(Mandatory)]
    [string[]] $ChildSourceIdentity,
    [Parameter(Mandatory)]
    [string] $TargetCommit,
    [Parameter(Mandatory)]
    [string] $UpstreamHead,
    [string] $StatePath = $(
        Join-Path `
            -Path ([Environment]::GetFolderPath(
                [Environment+SpecialFolder]::LocalApplicationData)) `
            -ChildPath 'eve-client-upstream-radar\state.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'EveClientUpstreamRadar.Common.ps1')

$state = Read-EveRadarState -StatePath $StatePath
$updated = Add-EveRadarSplitManifest `
    -State $state `
    -SourceIdentity $SourceIdentity `
    -ChildSourceIdentity $ChildSourceIdentity `
    -TargetCommit $TargetCommit `
    -UpstreamHead $UpstreamHead
Save-EveRadarState -StatePath $StatePath -State $updated

$persisted = Read-EveRadarState -StatePath $StatePath
$recorded = Get-EveRadarSplitManifest `
    -State $persisted `
    -SourceIdentity $SourceIdentity
if ($null -eq $recorded) {
    throw "Split manifest '$SourceIdentity' was not present after writing '$StatePath'."
}
$expectedChildren = @($ChildSourceIdentity | ForEach-Object { $_.Trim() })
$recordedChildren = @(
    Get-EveRadarJsonProperty `
        -InputObject $recorded `
        -Name 'childSourceIdentities'
)
if (($expectedChildren -join "`n") -cne ($recordedChildren -join "`n") -or
    (Get-EveRadarJsonProperty -InputObject $recorded -Name 'targetCommit') -cne
        $TargetCommit.Trim().ToLowerInvariant()) {
    throw "Split manifest '$SourceIdentity' did not persist the requested child set."
}

[pscustomobject]@{
    SourceIdentity = $SourceIdentity
    ChildSourceIdentities = $recordedChildren
    TargetCommit = Get-EveRadarJsonProperty -InputObject $recorded -Name 'targetCommit'
    RecordedAt = Get-EveRadarJsonProperty -InputObject $recorded -Name 'recordedAt'
    StatePath = [System.IO.Path]::GetFullPath($StatePath)
}
