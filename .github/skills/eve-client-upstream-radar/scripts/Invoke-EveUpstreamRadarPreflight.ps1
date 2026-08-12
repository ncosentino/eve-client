<#
.SYNOPSIS
Collects the Eve upstream delta and completes zero-candidate runs deterministically.

.DESCRIPTION
Creates one durable run directory, invokes Collect-EveUpstreamDelta.ps1, and writes
the final Markdown report immediately when no framework-neutral client path changed.
When candidates exist, it returns the inventory path for full skill analysis.

.PARAMETER TargetRepoPath
Optional path to the ncosentino/eve-client clone whose remote main ref defines current
parity. Defaults to the repository containing this repo-local skill.

.PARAMETER TargetRepository
GitHub owner/repository identifier used for remote validation and fingerprint lookup.

.PARAMETER RunRoot
Directory that owns durable radar run artifacts.

.PARAMETER StatePath
Radar-owned durable state file recording the verified upstream release baseline.

.PARAMETER SkipFetch
Uses currently available refs and is intended only for supervised dry-run diagnostics.
#>
[CmdletBinding()]
param(
    [string] $TargetRepoPath,
    [string] $TargetRepository = 'ncosentino/eve-client',
    [string] $RunRoot = $(
        Join-Path `
            -Path ([Environment]::GetFolderPath(
                [Environment+SpecialFolder]::LocalApplicationData)) `
            -ChildPath 'eve-client-upstream-radar\runs'),
    [string] $StatePath = $(
        Join-Path `
            -Path ([Environment]::GetFolderPath(
                [Environment+SpecialFolder]::LocalApplicationData)) `
            -ChildPath 'eve-client-upstream-radar\state.json'),
    [switch] $SkipFetch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'EveClientUpstreamRadar.Common.ps1')

$TargetRepoPath = Resolve-EveRadarTargetRepoPath `
    -TargetRepoPath $TargetRepoPath `
    -ScriptRoot $PSScriptRoot

$stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = Join-Path $RunRoot "$stamp-$suffix"
$null = New-Item -ItemType Directory -Path $runDirectory -Force

$inventoryPath = Join-Path $runDirectory 'inventory.json'
$inventory = & (Join-Path $PSScriptRoot 'Collect-EveUpstreamDelta.ps1') `
    -TargetRepoPath $TargetRepoPath `
    -TargetRepository $TargetRepository `
    -OutputPath $inventoryPath `
    -StatePath $StatePath `
    -SkipTargetFetch:$SkipFetch `
    -SkipUpstreamFetch:$SkipFetch

$candidateCommits = @(
    $inventory.Commits |
        Where-Object CandidateByPath
)
$candidateCount = $candidateCommits.Count
# A commit whose only client-directory footprint is an excluded implementation's test file is
# never a port candidate, but its tests still encode durable stream ordering. Surface it for
# analysis instead of discarding it with the implementation.
$evidenceCommits = @(
    $inventory.Commits |
        Where-Object { $_.BehavioralEvidenceByPath -and -not $_.CandidateByPath }
)
$evidenceCount = $evidenceCommits.Count
$sourceCandidates = @(
    @($candidateCommits) + @($evidenceCommits) |
        ForEach-Object {
            $identity = if ($null -eq $_.PullRequestNumber) {
                "eve-client-upstream:commit:$($_.Sha)"
            }
            else {
                "eve-client-upstream:pr:$($_.PullRequestNumber)"
            }
            [pscustomobject]@{
                SourceIdentity = $identity
                Fingerprint = "eve-fp:$(Get-EveRadarFingerprint -SourceIdentity $identity)"
                Commit = $_
            }
        } |
        Group-Object SourceIdentity |
        ForEach-Object { $_.Group | Select-Object -First 1 }
)

$issues = @()
if ($sourceCandidates.Count -gt 0) {
    $issueJson = @(
        & gh issue list `
            --repo $TargetRepository `
            --state all `
            --limit 1000 `
            --json number,title,state,stateReason,labels,url 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
        throw (
            "Failed to list target issues for deterministic fingerprint " +
            "deduplication:`n$($issueJson -join "`n")")
    }

    $issues = @(($issueJson -join "`n") | ConvertFrom-Json)
}

$trackedCandidates = @(
    foreach ($candidate in $sourceCandidates) {
        $match = @(
            $issues |
                Where-Object {
                    @($_.labels | ForEach-Object { $_.name }) -contains
                        $candidate.Fingerprint
                }
        ) | Select-Object -First 1
        if ($null -ne $match) {
            [pscustomobject]@{
                SourceIdentity = $candidate.SourceIdentity
                Fingerprint = $candidate.Fingerprint
                PullRequestNumber = $candidate.Commit.PullRequestNumber
                Commit = $candidate.Commit.Sha
                Subject = $candidate.Commit.Subject
                IssueNumber = $match.number
                IssueTitle = $match.title
                IssueState = $match.state
                IssueStateReason = $match.stateReason
                IssueUrl = $match.url
            }
        }
    }
)
# Behavioral evidence shares the fingerprint scheme, so an evidence commit that already has a
# tracking issue must stop the run as cheaply as a tracked path candidate. Only untracked work
# justifies the full analysis pass.
$untrackedCandidateCount = $sourceCandidates.Count - $trackedCandidates.Count
$reportPath = $null
$status = if ($candidateCount -eq 0 -and $evidenceCount -eq 0) {
    'NoCandidates'
}
elseif ($untrackedCandidateCount -eq 0) {
    'NoUntrackedCandidates'
}
else {
    'AnalysisRequired'
}

if ($status -ne 'AnalysisRequired') {
    $reportPath = Join-Path $runDirectory 'eve-client-upstream-radar.md'
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('# Eve Client Upstream Radar')
    $lines.Add('')
    $lines.Add("Generated: $($inventory.GeneratedAt)")
    $lines.Add('')
    $lines.Add('## Result')
    $lines.Add('')
    if ($status -eq 'NoCandidates') {
        $lines.Add(
            'No framework-neutral TypeScript client paths changed after the declared ' +
            'compatibility baseline. No parity analysis or GitHub writes were required.')
    }
    else {
        $lines.Add(
            'Every framework-neutral path candidate already has an immutable upstream ' +
            'fingerprint on a target issue. No repeat parity analysis or GitHub writes ' +
            'were required.')
    }
    $lines.Add('')
    $lines.Add('## Compared revisions')
    $lines.Add('')
    $lines.Add('| Source | Revision |')
    $lines.Add('|---|---|')
    $lines.Add(
        "| eve-client main | ``$($inventory.Target.Commit)`` |")
    $lines.Add(
        "| Declared eve $($inventory.Target.ReferenceEveVersion) baseline | " +
        "``$($inventory.Target.ReferenceEveCommit)`` |")
    $lines.Add(
        "| vercel/eve main | ``$($inventory.Upstream.HeadCommit)`` |")
    $lines.Add('')
    $lines.Add('## Path-gated upstream commits')
    $lines.Add('')
    $lines.Add('| Commit | Subject | Reason |')
    $lines.Add('|---|---|---|')
    foreach ($commit in @($inventory.Commits)) {
        if ($commit.CandidateByPath) {
            continue
        }
        $shortSha = $commit.Sha.Substring(0, 12)
        $subject = $commit.Subject.Replace('|', '\|')
        $reason = if ($commit.BehavioralEvidenceByPath) {
            'Behavioral evidence only; reviewed without a tracked port path'
        }
        else {
            'No tracked client or protocol path changed'
        }
        $lines.Add(
            "| [$shortSha]($($commit.CommitUrl)) | $subject | $reason |")
    }
    if ($trackedCandidates.Count -gt 0) {
        $lines.Add('')
        $lines.Add('## Already tracked candidates')
        $lines.Add('')
        $lines.Add('| Source | Fingerprint | Target issue |')
        $lines.Add('|---|---|---|')
        foreach ($tracked in $trackedCandidates) {
            $lines.Add(
                "| ``$($tracked.SourceIdentity)`` | ``$($tracked.Fingerprint)`` | " +
                "[#$($tracked.IssueNumber)]($($tracked.IssueUrl)) - " +
                "$($tracked.IssueTitle.Replace('|', '\|')) |")
        }
    }
    $lines.Add('')
    $lines.Add('## Decision counts')
    $lines.Add('')
    $lines.Add('| Decision | Count |')
    $lines.Add('|---|---:|')
    $lines.Add("| Upstream commits | $($inventory.Delta.CommitCount) |")
    $lines.Add("| Path candidates | $candidateCount |")
    $lines.Add("| Behavioral-evidence commits | $evidenceCount |")
    $lines.Add(
        '| Path candidates not yet in a published eve release | ' +
        "$($inventory.Delta.UnreleasedPathCandidateCount) |")
    $lines.Add('| Confirmed gaps | 0 |')
    $lines.Add('| Already present | 0 |')
    $lines.Add(
        "| Out of scope | $($inventory.Delta.CommitCount - $candidateCount) |")
    $lines.Add('| Insufficient evidence | 0 |')
    $lines.Add('| Created | 0 |')
    $lines.Add("| Deduplicated | $($trackedCandidates.Count) |")
    $lines.Add('| Cap deferred | 0 |')

    [System.IO.File]::WriteAllLines(
        $reportPath,
        $lines,
        [System.Text.UTF8Encoding]::new($false))
}

$preflightPath = Join-Path $runDirectory 'preflight.json'
$result = [pscustomobject]@{
    SchemaVersion = 3
    Status = $status
    RunDirectory = [System.IO.Path]::GetFullPath($runDirectory)
    InventoryPath = [System.IO.Path]::GetFullPath($inventoryPath)
    PreflightPath = [System.IO.Path]::GetFullPath($preflightPath)
    ReportPath = if ($null -eq $reportPath) {
        $null
    }
    else {
        [System.IO.Path]::GetFullPath($reportPath)
    }
    TargetCommit = $inventory.Target.Commit
    ReferenceEveVersion = $inventory.Target.ReferenceEveVersion
    ReferenceEveCommit = $inventory.Target.ReferenceEveCommit
    UpstreamHeadCommit = $inventory.Upstream.HeadCommit
    UpstreamCommitCount = [int] $inventory.Delta.CommitCount
    PathCandidateCount = $candidateCount
    BehavioralEvidenceCount = $evidenceCount
    BehavioralEvidenceCommits = @(
        $evidenceCommits |
            ForEach-Object {
                [pscustomobject]@{
                    Sha = $_.Sha
                    Subject = $_.Subject
                    PullRequestNumber = $_.PullRequestNumber
                    PullRequestUrl = $_.PullRequestUrl
                    CommitUrl = $_.CommitUrl
                    ReleasedInVersion = $_.ReleasedInVersion
                    BehavioralEvidencePaths = $_.BehavioralEvidencePaths
                }
            }
    )
    UnreleasedPathCandidateCount = [int] $inventory.Delta.UnreleasedPathCandidateCount
    SourceCandidateCount = $sourceCandidates.Count
    TrackedCandidateCount = $trackedCandidates.Count
    UntrackedCandidateCount = $untrackedCandidateCount
    TrackedCandidates = $trackedCandidates
}

[System.IO.File]::WriteAllText(
    $preflightPath,
    ($result | ConvertTo-Json -Depth 5),
    [System.Text.UTF8Encoding]::new($false))

return $result
