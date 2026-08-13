<#
.SYNOPSIS
Records that one immutable upstream source identity was analyzed and dismissed.

.DESCRIPTION
The radar only remembers a candidate that became a GitHub issue. A candidate analyzed and
dismissed leaves no trace, so it is re-analyzed on every later run until the compatibility
baseline advances past it. This script persists that decision in radar-owned durable state so
the deterministic preflight can treat it as tracked.

Call it once per dismissed candidate, after the parity decision is final.

.PARAMETER SourceIdentity
The immutable source identity, such as eve-client-upstream:pr:1861.

.PARAMETER Decision
Either out-of-scope or already-present. No other parity result may be recorded: a confirmed
gap belongs in an issue, and insufficient evidence must be revisited.

.PARAMETER TargetCommit
The eve-client commit the decision was made against.

.PARAMETER UpstreamHead
The upstream main commit the delta was collected from.

.PARAMETER Reason
A short explanation retained for audit.

.PARAMETER StatePath
Radar-owned durable state file.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SourceIdentity,
    [Parameter(Mandatory)]
    [ValidateSet('out-of-scope', 'already-present')]
    [string] $Decision,
    [Parameter(Mandatory)]
    [string] $TargetCommit,
    [Parameter(Mandatory)]
    [string] $UpstreamHead,
    [Parameter(Mandatory)]
    [string] $Reason,
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
$updated = Add-EveRadarDismissal `
    -State $state `
    -SourceIdentity $SourceIdentity `
    -Decision $Decision `
    -TargetCommit $TargetCommit `
    -UpstreamHead $UpstreamHead `
    -Reason $Reason
Save-EveRadarState -StatePath $StatePath -State $updated

$recorded = Get-EveRadarDismissal -State $updated -SourceIdentity $SourceIdentity
[pscustomobject]@{
    SourceIdentity = $SourceIdentity
    Fingerprint = Get-EveRadarJsonProperty -InputObject $recorded -Name 'fingerprint'
    Decision = $Decision
    TargetCommit = Get-EveRadarJsonProperty -InputObject $recorded -Name 'targetCommit'
    RecordedAt = Get-EveRadarJsonProperty -InputObject $recorded -Name 'recordedAt'
    StatePath = [System.IO.Path]::GetFullPath($StatePath)
}
