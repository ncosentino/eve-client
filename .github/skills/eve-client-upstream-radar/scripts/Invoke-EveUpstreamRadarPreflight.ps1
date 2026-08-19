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

.PARAMETER RecheckDismissals
Ignores recorded dismissals so previously dismissed candidates are analyzed again.

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
    [switch] $RecheckDismissals,
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
        $match = Get-EveRadarIssueByFingerprint `
            -Issue $issues `
            -Fingerprint $candidate.Fingerprint
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
$trackedIdentities = @($trackedCandidates | ForEach-Object { $_.SourceIdentity })
$implementedIssueIdentities = @(
    $trackedCandidates |
        Where-Object {
            $_.IssueState -eq 'CLOSED' -and
            $_.IssueStateReason -eq 'COMPLETED'
        } |
        ForEach-Object { $_.SourceIdentity }
)

# A candidate analyzed and dismissed leaves no issue behind, so without a durable record it
# would be re-analyzed on every run forever. Treat a recorded dismissal as tracked, matching
# the existing rule that a closed issue is skipped for an immutable upstream change.
$radarState = Read-EveRadarState -StatePath $StatePath
$dismissedCandidates = @(
    if (-not $RecheckDismissals) {
        foreach ($candidate in $sourceCandidates) {
            if ($trackedIdentities -ccontains $candidate.SourceIdentity) {
                continue
            }

            $dismissal = Get-EveRadarDismissal `
                -State $radarState `
                -SourceIdentity $candidate.SourceIdentity
            if ($null -ne $dismissal) {
                [pscustomobject]@{
                    SourceIdentity = $candidate.SourceIdentity
                    Fingerprint = $candidate.Fingerprint
                    Commit = $candidate.Commit.Sha
                    Subject = $candidate.Commit.Subject
                    Decision = Get-EveRadarJsonProperty -InputObject $dismissal -Name 'decision'
                    TargetCommit = Get-EveRadarJsonProperty `
                        -InputObject $dismissal `
                        -Name 'targetCommit'
                    Reason = Get-EveRadarJsonProperty -InputObject $dismissal -Name 'reason'
                    RecordedAt = Get-EveRadarJsonProperty `
                        -InputObject $dismissal `
                        -Name 'recordedAt'
                }
            }
        }
    }
)
$directlyResolvedIdentities = @(
    $trackedIdentities +
        @($dismissedCandidates | ForEach-Object { $_.SourceIdentity })
)
$splitCandidateResolutions = @(
    foreach ($candidate in $sourceCandidates) {
        if ($directlyResolvedIdentities -ccontains $candidate.SourceIdentity) {
            continue
        }

        $resolution = Get-EveRadarSplitResolution `
            -State $radarState `
            -SourceIdentity $candidate.SourceIdentity `
            -Issue $issues `
            -IgnoreDismissals:$RecheckDismissals
        if ($null -ne $resolution) {
            [pscustomobject]@{
                SourceIdentity = $candidate.SourceIdentity
                Fingerprint = $candidate.Fingerprint
                PullRequestNumber = $candidate.Commit.PullRequestNumber
                Commit = $candidate.Commit.Sha
                Subject = $candidate.Commit.Subject
                ReleasedInVersion = $candidate.Commit.ReleasedInVersion
                Resolved = $resolution.Resolved
                Implemented = $resolution.Implemented
                TargetCommit = $resolution.TargetCommit
                UpstreamHead = $resolution.UpstreamHead
                RecordedAt = $resolution.RecordedAt
                Children = $resolution.Children
            }
        }
    }
)
$resolvedSplitCandidates = @(
    $splitCandidateResolutions |
        Where-Object Resolved
)
$implementedSplitCandidates = @(
    $splitCandidateResolutions |
        Where-Object Implemented
)
# The declared reference must never lag what parity work has actually covered, and the data to
# answer that already exists here: each candidate's published release plus its tracking state.
# Compute it rather than leaving it to be re-derived by hand at release time.
$resolvedIdentities = @(
    @(
        $directlyResolvedIdentities +
            @($resolvedSplitCandidates | ForEach-Object { $_.SourceIdentity })
    ) |
        Sort-Object -Unique
)
$implementedIdentities = @(
    @(
        $implementedIssueIdentities +
            @($dismissedCandidates | ForEach-Object { $_.SourceIdentity }) +
            @($implementedSplitCandidates | ForEach-Object { $_.SourceIdentity })
    ) |
        Sort-Object -Unique
)
$referenceVersion = $inventory.Target.ReferenceEveVersion
$implementedThrough = Get-EveRadarImplementedThroughVersion -Candidate @(
    $sourceCandidates |
        ForEach-Object {
            [pscustomobject]@{
                Version = $_.Commit.ReleasedInVersion
                Resolved = $implementedIdentities -ccontains $_.SourceIdentity
            }
        }
) -PublishedVersion @(
    $inventory.Commits |
        ForEach-Object { $_.ReleasedInVersion }
) -FloorVersion $referenceVersion
$baselineBehind = $null -ne $implementedThrough -and
    (Compare-EveRadarVersion -Left $implementedThrough -Right $referenceVersion) -gt 0

# Behavioral evidence shares the fingerprint scheme, so an evidence commit that already has a
# tracking issue must stop the run as cheaply as a tracked path candidate. Only untracked work
# justifies the full analysis pass.
$untrackedCandidateCount = @(
    $sourceCandidates |
        Where-Object { $resolvedIdentities -cnotcontains $_.SourceIdentity }
).Count
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
            'Every framework-neutral source candidate already has a target issue or a ' +
            'complete durable decision. No repeat parity analysis or GitHub writes were ' +
            'required.')
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
    $lines.Add('## Compatibility baseline')
    $lines.Add('')
    $lines.Add("- Declared reference: eve ``$referenceVersion``")
    $lines.Add(
        '- Parity covered through: ' + $(
            if ($null -eq $implementedThrough) {
                'no published release is fully covered'
            }
            else {
                "eve ``$implementedThrough``"
            }))
    if ($baselineBehind) {
        $lines.Add('')
        $lines.Add(
            "**The declared reference lags implemented parity.** Every candidate through eve " +
            "``$implementedThrough`` is implemented or dismissed while the package declares " +
            "``$referenceVersion``. Advance ``EveProtocol.ReferenceEveVersion`` and the pinned " +
            'fixture together before the next release.')
    }
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
    if ($resolvedSplitCandidates.Count -gt 0) {
        $lines.Add('')
        $lines.Add('## Resolved split candidates')
        $lines.Add('')
        $lines.Add('| Source | Children | Resolution |')
        $lines.Add('|---|---:|---|')
        foreach ($split in $resolvedSplitCandidates) {
            $issueCount = @(
                $split.Children |
                    Where-Object Resolution -EQ 'issue'
            ).Count
            $dismissalCount = $split.Children.Count - $issueCount
            $implementation = if ($split.Implemented) {
                'implemented'
            }
            else {
                'tracked'
            }
            $lines.Add(
                "| ``$($split.SourceIdentity)`` | $($split.Children.Count) | " +
                "$issueCount issue, $dismissalCount dismissal; $implementation |")
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
    $lines.Add("| Previously dismissed | $($dismissedCandidates.Count) |")
    $lines.Add("| Resolved split candidates | $($resolvedSplitCandidates.Count) |")
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
    SchemaVersion = 4
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
    ImplementedThroughEveVersion = $implementedThrough
    BaselineBehindImplementedParity = $baselineBehind
    ReferenceEveCommit = $inventory.Target.ReferenceEveCommit
    UpstreamHeadCommit = $inventory.Upstream.HeadCommit
    UpstreamCommitCount = [int] $inventory.Delta.CommitCount
    PathCandidateCount = $candidateCount
    BehavioralEvidenceCount = $evidenceCount
    DismissedCandidateCount = $dismissedCandidates.Count
    DismissedCandidates = $dismissedCandidates
    ResolvedSplitCandidateCount = $resolvedSplitCandidates.Count
    ResolvedSplitCandidates = $resolvedSplitCandidates
    ImplementedSplitCandidateCount = $implementedSplitCandidates.Count
    ImplementedSplitCandidates = $implementedSplitCandidates
    SplitCandidateResolutions = $splitCandidateResolutions
    RecheckedDismissals = [bool] $RecheckDismissals
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
    ($result | ConvertTo-Json -Depth 7),
    [System.Text.UTF8Encoding]::new($false))

return $result
