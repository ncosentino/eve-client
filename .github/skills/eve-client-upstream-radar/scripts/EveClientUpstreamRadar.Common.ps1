Set-StrictMode -Version Latest

function Resolve-EveRadarTargetRepoPath {
    <#
    .SYNOPSIS
    Resolves an explicit target checkout or the repository containing this repo-local skill.
    #>
    [CmdletBinding()]
    param(
        [string] $TargetRepoPath,
        [Parameter(Mandatory)]
        [string] $ScriptRoot
    )

    $candidate = if ([string]::IsNullOrWhiteSpace($TargetRepoPath)) {
        Join-Path $ScriptRoot '..\..\..\..'
    }
    else {
        $TargetRepoPath
    }

    return [System.IO.Path]::GetFullPath($candidate)
}

function Get-EveRadarReferenceVersion {
    <#
    .SYNOPSIS
    Reads the declared upstream eve version from EveProtocol.cs.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ProtocolSource
    )

    $match = [regex]::Match(
        $ProtocolSource,
        'ReferenceEveVersion\s*=\s*"(?<version>[^"]+)"')
    if (-not $match.Success) {
        throw 'Could not find EveProtocol.ReferenceEveVersion.'
    }

    return $match.Groups['version'].Value
}

function Get-EveRadarJsonProperty {
    <#
    .SYNOPSIS
    Reads an optional property from deserialized JSON without tripping strict mode.
    #>
    [CmdletBinding()]
    param(
        $InputObject,
        [Parameter(Mandatory)]
        [string] $Name
    )

    if ($null -eq $InputObject) {
        return $null
    }

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-EveRadarTimestampText {
    <#
    .SYNOPSIS
    Normalizes a timestamp to an invariant UTC round-trip string.
    #>
    [CmdletBinding()]
    param(
        $Value
    )

    if ($Value -is [DateTimeOffset]) {
        return $Value.ToUniversalTime().ToString('o')
    }

    if ($Value -is [DateTime]) {
        return ([DateTimeOffset] $Value).ToUniversalTime().ToString('o')
    }

    $text = [string] $Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    $parsed = [DateTimeOffset]::MinValue
    $parsedSuccessfully = [DateTimeOffset]::TryParse(
        $text,
        [cultureinfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind,
        [ref] $parsed)
    if ($parsedSuccessfully) {
        return $parsed.ToUniversalTime().ToString('o')
    }

    return $text
}

function Read-EveRadarState {
    <#
    .SYNOPSIS
    Reads radar-owned durable state, returning empty state when none is recorded yet.

    .DESCRIPTION
    Schema 1 recorded only the baseline. Schema 2 adds dismissal records. Schema 3 adds
    split manifests so preflight can prove that every topic-qualified child of one upstream
    source has been resolved. Older files are upgraded in memory rather than rejected because
    the state they carry is still valid.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $StatePath
    )

    if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) {
        return [pscustomobject]@{
            schemaVersion = 3
            baseline = $null
            dismissals = @()
            splitManifests = @()
        }
    }

    $state = [System.IO.File]::ReadAllText($StatePath) | ConvertFrom-Json
    $schemaVersion = Get-EveRadarJsonProperty -InputObject $state -Name 'schemaVersion'
    if ($schemaVersion -notin 1, 2, 3) {
        throw (
            "Unsupported radar state schema version '$schemaVersion' in " +
            "'$StatePath'.")
    }

    # ConvertFrom-Json coerces an ISO timestamp into a local DateTime, so re-serializing a
    # record that was only read would rewrite the same instant with this machine's offset and
    # churn the file on every run. Normalize on read instead.
    $baseline = Get-EveRadarJsonProperty -InputObject $state -Name 'baseline'
    if ($null -ne $baseline) {
        $baseline.recordedAt = Get-EveRadarTimestampText -Value (
            Get-EveRadarJsonProperty -InputObject $baseline -Name 'recordedAt')
    }

    $dismissals = @(
        (Get-EveRadarJsonProperty -InputObject $state -Name 'dismissals') |
            Where-Object { $null -ne $_ }
    )
    foreach ($dismissal in $dismissals) {
        $dismissal.recordedAt = Get-EveRadarTimestampText -Value (
            Get-EveRadarJsonProperty -InputObject $dismissal -Name 'recordedAt')
    }

    $splitManifests = @(
        (Get-EveRadarJsonProperty -InputObject $state -Name 'splitManifests') |
            Where-Object { $null -ne $_ }
    )
    foreach ($manifest in $splitManifests) {
        $manifest.recordedAt = Get-EveRadarTimestampText -Value (
            Get-EveRadarJsonProperty -InputObject $manifest -Name 'recordedAt')
    }

    return [pscustomobject]@{
        schemaVersion = 3
        baseline = $baseline
        dismissals = $dismissals
        splitManifests = $splitManifests
    }
}

function Save-EveRadarState {
    <#
    .SYNOPSIS
    Writes radar-owned durable state so an interrupted run cannot leave it partial.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $StatePath,
        [Parameter(Mandatory)]
        $State
    )

    $directory = Split-Path -Parent $StatePath
    if (-not [string]::IsNullOrWhiteSpace($directory) -and
        -not (Test-Path -LiteralPath $directory -PathType Container)) {
        $null = New-Item -ItemType Directory -Path $directory -Force
    }

    $temporaryPath = "$StatePath.tmp"
    [System.IO.File]::WriteAllText(
        $temporaryPath,
        ($State | ConvertTo-Json -Depth 8))
    [System.IO.File]::Move($temporaryPath, $StatePath, $true)
}

function Get-EveRadarDismissal {
    <#
    .SYNOPSIS
    Returns the recorded dismissal for one immutable source identity, or null.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $State,
        [Parameter(Mandatory)]
        [string] $SourceIdentity
    )

    $dismissals = Get-EveRadarJsonProperty -InputObject $State -Name 'dismissals'
    return @(
        $dismissals |
            Where-Object {
                $null -ne $_ -and
                (Get-EveRadarJsonProperty -InputObject $_ -Name 'sourceIdentity') -ceq
                    $SourceIdentity
            }
    ) | Select-Object -First 1
}

function Add-EveRadarDismissal {
    <#
    .SYNOPSIS
    Records that one immutable upstream source identity was analyzed and dismissed.

    .DESCRIPTION
    An out-of-scope decision is a property of the immutable upstream commit alone. An
    already-present decision is that commit judged against eve-client at one target commit,
    so the target commit is recorded to keep the decision auditable and recheckable.
    Re-recording the same source identity replaces the earlier record rather than appending.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $State,
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
        [DateTimeOffset] $RecordedAt = [DateTimeOffset]::UtcNow
    )

    $existing = @(
        (Get-EveRadarJsonProperty -InputObject $State -Name 'dismissals') |
            Where-Object {
                $null -ne $_ -and
                (Get-EveRadarJsonProperty -InputObject $_ -Name 'sourceIdentity') -cne
                    $SourceIdentity
            }
    )
    $record = [pscustomobject]@{
        sourceIdentity = $SourceIdentity
        fingerprint = "eve-fp:$(Get-EveRadarFingerprint -SourceIdentity $SourceIdentity)"
        decision = $Decision
        targetCommit = $TargetCommit.Trim().ToLowerInvariant()
        upstreamHead = $UpstreamHead.Trim().ToLowerInvariant()
        reason = $Reason
        recordedAt = Get-EveRadarTimestampText -Value $RecordedAt
    }

    return [pscustomobject]@{
        schemaVersion = 3
        baseline = Get-EveRadarJsonProperty -InputObject $State -Name 'baseline'
        dismissals = @($existing + $record)
        splitManifests = @(
            (Get-EveRadarJsonProperty -InputObject $State -Name 'splitManifests') |
                Where-Object { $null -ne $_ }
        )
    }
}

function Get-EveRadarSplitManifest {
    <#
    .SYNOPSIS
    Returns the complete child-identity manifest for one split upstream source, or null.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $State,
        [Parameter(Mandatory)]
        [string] $SourceIdentity
    )

    $manifests = Get-EveRadarJsonProperty -InputObject $State -Name 'splitManifests'
    return @(
        $manifests |
            Where-Object {
                $null -ne $_ -and
                (Get-EveRadarJsonProperty -InputObject $_ -Name 'sourceIdentity') -ceq
                    $SourceIdentity
            }
    ) | Select-Object -First 1
}

function Add-EveRadarSplitManifest {
    <#
    .SYNOPSIS
    Records the complete topic-qualified child identities for one split upstream source.

    .DESCRIPTION
    A manifest does not resolve the parent by itself. Preflight resolves the parent only when
    every listed child has either a fingerprinted target issue or a recorded dismissal.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $State,
        [Parameter(Mandatory)]
        [string] $SourceIdentity,
        [Parameter(Mandatory)]
        [string[]] $ChildSourceIdentity,
        [Parameter(Mandatory)]
        [string] $TargetCommit,
        [Parameter(Mandatory)]
        [string] $UpstreamHead,
        [DateTimeOffset] $RecordedAt = [DateTimeOffset]::UtcNow
    )

    if ($SourceIdentity -notmatch
        '^eve-client-upstream:(?:pr:\d+|commit:[0-9a-fA-F]{40})$') {
        throw "Split manifest source identity '$SourceIdentity' is not an unsplit identity."
    }

    $children = @($ChildSourceIdentity | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_)
    })
    if ($children.Count -lt 2) {
        throw 'A split manifest must contain at least two child source identities.'
    }

    $prefix = "${SourceIdentity}:"
    $seen = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::Ordinal)
    $normalizedChildren = [System.Collections.Generic.List[string]]::new()
    foreach ($child in $children) {
        $normalizedChild = $child.Trim()
        if (-not $normalizedChild.StartsWith($prefix, [StringComparison]::Ordinal)) {
            throw (
                "Split child source identity '$normalizedChild' does not belong to " +
                "'$SourceIdentity'.")
        }

        $slug = $normalizedChild.Substring($prefix.Length)
        if ($slug -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$') {
            throw "Split child source identity '$normalizedChild' has an invalid topic slug."
        }

        if (-not $seen.Add($normalizedChild)) {
            throw "Split manifest contains duplicate child identity '$normalizedChild'."
        }

        $normalizedChildren.Add($normalizedChild)
    }

    $existing = @(
        (Get-EveRadarJsonProperty -InputObject $State -Name 'splitManifests') |
            Where-Object {
                $null -ne $_ -and
                (Get-EveRadarJsonProperty -InputObject $_ -Name 'sourceIdentity') -cne
                    $SourceIdentity
            }
    )
    $record = [pscustomobject]@{
        sourceIdentity = $SourceIdentity
        childSourceIdentities = @($normalizedChildren)
        targetCommit = $TargetCommit.Trim().ToLowerInvariant()
        upstreamHead = $UpstreamHead.Trim().ToLowerInvariant()
        recordedAt = Get-EveRadarTimestampText -Value $RecordedAt
    }

    return [pscustomobject]@{
        schemaVersion = 3
        baseline = Get-EveRadarJsonProperty -InputObject $State -Name 'baseline'
        dismissals = @(
            (Get-EveRadarJsonProperty -InputObject $State -Name 'dismissals') |
                Where-Object { $null -ne $_ }
        )
        splitManifests = @($existing + $record)
    }
}

function Get-EveRadarIssueByFingerprint {
    <#
    .SYNOPSIS
    Returns the first target issue carrying one immutable radar fingerprint.
    #>
    [CmdletBinding()]
    param(
        [psobject[]] $Issue,
        [Parameter(Mandatory)]
        [string] $Fingerprint
    )

    return @(
        $Issue |
            Where-Object {
                @(
                    (Get-EveRadarJsonProperty -InputObject $_ -Name 'labels') |
                        ForEach-Object {
                            Get-EveRadarJsonProperty -InputObject $_ -Name 'name'
                        }
                ) -contains $Fingerprint
            }
    ) | Select-Object -First 1
}

function Test-EveRadarIssueImplemented {
    <#
    .SYNOPSIS
    Returns whether a fingerprinted issue represents completed implementation work.
    #>
    [CmdletBinding()]
    param(
        $Issue
    )

    if ($null -eq $Issue) {
        return $false
    }

    return (
        (Get-EveRadarJsonProperty -InputObject $Issue -Name 'state') -eq 'CLOSED' -and
        (Get-EveRadarJsonProperty -InputObject $Issue -Name 'stateReason') -eq 'COMPLETED')
}

function Get-EveRadarSplitResolution {
    <#
    .SYNOPSIS
    Resolves every child in a split manifest against target issues and durable dismissals.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $State,
        [Parameter(Mandatory)]
        [string] $SourceIdentity,
        [psobject[]] $Issue,
        [switch] $IgnoreDismissals
    )

    $manifest = Get-EveRadarSplitManifest `
        -State $State `
        -SourceIdentity $SourceIdentity
    if ($null -eq $manifest) {
        return $null
    }

    $children = @(
        foreach ($childIdentity in @(
            Get-EveRadarJsonProperty `
                -InputObject $manifest `
                -Name 'childSourceIdentities'
        )) {
            $fingerprint = "eve-fp:$(Get-EveRadarFingerprint -SourceIdentity $childIdentity)"
            $matchedIssue = Get-EveRadarIssueByFingerprint `
                -Issue $Issue `
                -Fingerprint $fingerprint
            $issueImplemented = Test-EveRadarIssueImplemented -Issue $matchedIssue
            $dismissal = if ($IgnoreDismissals) {
                $null
            }
            else {
                Get-EveRadarDismissal `
                    -State $State `
                    -SourceIdentity $childIdentity
            }

            [pscustomobject]@{
                SourceIdentity = $childIdentity
                Fingerprint = $fingerprint
                Resolved = $null -ne $matchedIssue -or $null -ne $dismissal
                Implemented = $issueImplemented -or $null -ne $dismissal
                Resolution = if ($null -ne $matchedIssue) {
                    'issue'
                }
                elseif ($null -ne $dismissal) {
                    Get-EveRadarJsonProperty -InputObject $dismissal -Name 'decision'
                }
                else {
                    'unresolved'
                }
                IssueNumber = Get-EveRadarJsonProperty `
                    -InputObject $matchedIssue `
                    -Name 'number'
                IssueUrl = Get-EveRadarJsonProperty `
                    -InputObject $matchedIssue `
                    -Name 'url'
                Reason = Get-EveRadarJsonProperty `
                    -InputObject $dismissal `
                    -Name 'reason'
            }
        }
    )

    return [pscustomobject]@{
        SourceIdentity = $SourceIdentity
        TargetCommit = Get-EveRadarJsonProperty -InputObject $manifest -Name 'targetCommit'
        UpstreamHead = Get-EveRadarJsonProperty -InputObject $manifest -Name 'upstreamHead'
        RecordedAt = Get-EveRadarJsonProperty -InputObject $manifest -Name 'recordedAt'
        Resolved = @($children | Where-Object { -not $_.Resolved }).Count -eq 0
        Implemented = @($children | Where-Object { -not $_.Implemented }).Count -eq 0
        Children = $children
    }
}

function Update-EveRadarBaseline {
    <#
    .SYNOPSIS
    Validates the upstream release commit against durable state and advances it.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        $State,
        [Parameter(Mandatory)]
        [string] $Version,
        [Parameter(Mandatory)]
        [string] $Commit,
        [DateTimeOffset] $RecordedAt = [DateTimeOffset]::UtcNow
    )

    $normalizedCommit = $Commit.Trim().ToLowerInvariant()
    $baseline = Get-EveRadarJsonProperty -InputObject $State -Name 'baseline'
    $recordedVersion = Get-EveRadarJsonProperty `
        -InputObject $baseline `
        -Name 'eveVersion'
    $recordedCommit = Get-EveRadarJsonProperty `
        -InputObject $baseline `
        -Name 'eveCommit'

    $status = if ([string]::IsNullOrWhiteSpace($recordedVersion)) {
        'Bootstrapped'
    }
    elseif ($recordedVersion -ne $Version) {
        'Advanced'
    }
    elseif ($recordedCommit -ne $normalizedCommit) {
        throw (
            "Upstream tag eve@$Version now resolves to commit " +
            "'$normalizedCommit' but the radar recorded '$recordedCommit'. " +
            'The upstream release tag moved, so re-verify parity before ' +
            'trusting a delta.')
    }
    else {
        'Unchanged'
    }

    $baselineRecordedAt = if ($status -eq 'Unchanged') {
        Get-EveRadarTimestampText -Value (
            Get-EveRadarJsonProperty -InputObject $baseline -Name 'recordedAt')
    }
    else {
        $null
    }

    if ([string]::IsNullOrWhiteSpace($baselineRecordedAt)) {
        $baselineRecordedAt = Get-EveRadarTimestampText -Value $RecordedAt
    }

    # A baseline advance never invalidates a recorded dismissal: both are keyed to immutable
    # upstream identities. Carry them forward so advancing does not silently reset them.
    return [pscustomobject]@{
        Status = $status
        Commit = $normalizedCommit
        State = [pscustomobject]@{
            schemaVersion = 3
            baseline = [pscustomobject]@{
                eveVersion = $Version
                eveCommit = $normalizedCommit
                recordedAt = $baselineRecordedAt
            }
            dismissals = @(
                (Get-EveRadarJsonProperty -InputObject $State -Name 'dismissals') |
                    Where-Object { $null -ne $_ }
            )
            splitManifests = @(
                (Get-EveRadarJsonProperty -InputObject $State -Name 'splitManifests') |
                    Where-Object { $null -ne $_ }
            )
        }
    }
}

function Get-EveRadarPullRequestNumber {
    <#
    .SYNOPSIS
    Extracts a squash-merged GitHub pull request number from a commit subject.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Subject
    )

    $match = [regex]::Match($Subject, '\(#(?<number>\d+)\)\s*$')
    if (-not $match.Success) {
        return $null
    }

    return [int] $match.Groups['number'].Value
}

function Get-EveRadarOrderedReleaseTags {
    <#
    .SYNOPSIS
    Orders `eve@<semver>` release tags from oldest to newest published version.

    .DESCRIPTION
    Parses each tag into its numeric version components plus an optional
    prerelease identifier, then sorts ascending so the first release containing
    a commit can be resolved by walking the result in order. Tags that do not
    match the `eve@<major>.<minor>.<patch>` shape are ignored rather than
    guessed at.
    #>
    [CmdletBinding()]
    param(
        [string[]] $TagName
    )

    if ($null -eq $TagName) {
        return @()
    }

    $parsed = foreach ($tag in $TagName) {
        if ([string]::IsNullOrWhiteSpace($tag)) {
            continue
        }

        $trimmed = $tag.Trim()
        $match = [regex]::Match(
            $trimmed,
            '^eve@(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<prerelease>[0-9A-Za-z.-]+))?$')
        if (-not $match.Success) {
            continue
        }

        $prerelease = if ($match.Groups['prerelease'].Success) {
            $match.Groups['prerelease'].Value
        }
        else {
            $null
        }

        [pscustomobject]@{
            Tag = $trimmed
            Version = $trimmed.Substring('eve@'.Length)
            Major = [int] $match.Groups['major'].Value
            Minor = [int] $match.Groups['minor'].Value
            Patch = [int] $match.Groups['patch'].Value
            Prerelease = $prerelease
        }
    }

    return @(
        $parsed |
            Sort-Object `
                Major, `
                Minor, `
                Patch, `
                @{ Expression = { $null -ne $_.Prerelease }; Descending = $true }, `
                Prerelease)
}

function ConvertTo-EveRadarSemanticVersion {
    <#
    .SYNOPSIS
    Parses a bare eve version into comparable parts, or returns null.
    #>
    [CmdletBinding()]
    param(
        [string] $Version
    )

    if ([string]::IsNullOrWhiteSpace($Version)) {
        return $null
    }

    $match = [regex]::Match(
        $Version.Trim(),
        '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<prerelease>[0-9A-Za-z.-]+))?$')
    if (-not $match.Success) {
        return $null
    }

    return [pscustomobject]@{
        Version = $Version.Trim()
        Major = [int] $match.Groups['major'].Value
        Minor = [int] $match.Groups['minor'].Value
        Patch = [int] $match.Groups['patch'].Value
        Prerelease = if ($match.Groups['prerelease'].Success) {
            $match.Groups['prerelease'].Value
        }
        else {
            $null
        }
    }
}

function Compare-EveRadarVersion {
    <#
    .SYNOPSIS
    Orders two bare eve versions, returning -1, 0, or 1. A prerelease sorts before its release.
    #>
    [CmdletBinding()]
    param(
        [string] $Left,
        [string] $Right
    )

    $leftVersion = ConvertTo-EveRadarSemanticVersion -Version $Left
    $rightVersion = ConvertTo-EveRadarSemanticVersion -Version $Right
    if ($null -eq $leftVersion -or $null -eq $rightVersion) {
        throw "Cannot compare eve versions '$Left' and '$Right'."
    }

    foreach ($part in 'Major', 'Minor', 'Patch') {
        if ($leftVersion.$part -ne $rightVersion.$part) {
            return [Math]::Sign($leftVersion.$part - $rightVersion.$part)
        }
    }

    if ($null -eq $leftVersion.Prerelease -and $null -eq $rightVersion.Prerelease) {
        return 0
    }

    if ($null -eq $leftVersion.Prerelease) {
        return 1
    }

    if ($null -eq $rightVersion.Prerelease) {
        return -1
    }

    return [Math]::Sign(
        [string]::CompareOrdinal($leftVersion.Prerelease, $rightVersion.Prerelease))
}

function Get-EveRadarImplementedThroughVersion {
    <#
    .SYNOPSIS
    Returns the highest published eve release with no unresolved candidate at or below it.

    .DESCRIPTION
    Answers the question a release has to ask: how far has parity actually been carried?
    A release is only fully covered when every candidate belonging to it and to every earlier
    release is resolved, so the highest resolved version alone is not the answer. One open
    candidate for an earlier release holds the whole line back.

    Each candidate record needs a Version and a Resolved flag. PublishedVersion supplies every
    release in the delta, including versions with no client candidate. FloorVersion is the
    already-declared compatibility baseline and remains the answer when the first later release
    still has unresolved work.
    #>
    [CmdletBinding()]
    param(
        [psobject[]] $Candidate,
        [string[]] $PublishedVersion,
        [string] $FloorVersion
    )

    $released = @(
        $Candidate |
            Where-Object { $null -ne $_ -and -not [string]::IsNullOrWhiteSpace($_.Version) }
    )

    $versions = @(
        @($PublishedVersion) +
            @($released | ForEach-Object { $_.Version }) |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object -Unique
    )
    $byVersion = @(
        $versions |
            ForEach-Object {
                $version = $_
                $versionCandidates = @(
                    $released |
                        Where-Object Version -EQ $version
                )
                [pscustomobject]@{
                    Version = $version
                    Resolved = @(
                        $versionCandidates |
                            Where-Object { -not $_.Resolved }
                    ).Count -eq 0
                    Parsed = ConvertTo-EveRadarSemanticVersion -Version $version
                }
            } |
            Where-Object { $null -ne $_.Parsed } |
            Sort-Object `
                @{ Expression = { $_.Parsed.Major } }, `
                @{ Expression = { $_.Parsed.Minor } }, `
                @{ Expression = { $_.Parsed.Patch } }, `
                @{ Expression = { $null -ne $_.Parsed.Prerelease }; Descending = $true }, `
                @{ Expression = { $_.Parsed.Prerelease } }
    )

    $implemented = if ([string]::IsNullOrWhiteSpace($FloorVersion)) {
        $null
    }
    else {
        if ($null -eq (ConvertTo-EveRadarSemanticVersion -Version $FloorVersion)) {
            throw "Cannot use eve version '$FloorVersion' as the parity floor."
        }

        $FloorVersion
    }

    foreach ($release in $byVersion) {
        if ($null -ne $implemented -and
            (Compare-EveRadarVersion -Left $release.Version -Right $implemented) -le 0) {
            continue
        }

        if (-not $release.Resolved) {
            break
        }

        $implemented = $release.Version
    }

    return $implemented
}

function Get-EveRadarFingerprint {
    <#
    .SYNOPSIS
    Computes the stable twelve-character SHA-256 fingerprint for one immutable source identity.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $SourceIdentity
    )

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($SourceIdentity)
        $hash = $sha256.ComputeHash($bytes)
        return (
            [System.BitConverter]::ToString($hash) -replace '-', ''
        ).ToLowerInvariant().Substring(0, 12)
    }
    finally {
        $sha256.Dispose()
    }
}

function Test-EveRadarGitHubRemoteUrl {
    <#
    .SYNOPSIS
    Validates that a git remote is an HTTPS or SSH URL for one exact GitHub repository.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Url,
        [Parameter(Mandatory)]
        [string] $Repository
    )

    $escapedRepository = [regex]::Escape($Repository)
    return $Url -match (
        '^(?:' +
        'https://(?:[^/@]+@)?github\.com/' +
        '|git@github\.com:' +
        '|ssh://git@github\.com/' +
        ')' +
        $escapedRepository +
        '(?:\.git)?/?$')
}

function Test-EveRadarExplicitlyExcludedPath {
    <#
    .SYNOPSIS
    Identifies TypeScript-only client state and UI-reducer files excluded from the .NET port.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $normalized = $Path.Replace('\', '/')
    return $normalized -match (
        '^packages/eve/src/client/' +
        '(eve-agent-store|message-reducer(?:-types)?|reducer)(?:\.test)?\.ts$')
}

function Test-EveRadarBehavioralEvidencePath {
    <#
    .SYNOPSIS
    Identifies an excluded client file whose tests still encode a framework-neutral contract.

    .DESCRIPTION
    The reducer and store implementations are TypeScript-only UI helpers this package never
    ports, so they stay out of Test-EveRadarTrackedPath. Their test files are different: they
    assert the order and interleaving of durable stream events, which is protocol behavior the
    .NET client must interpret correctly. Such a path never makes a commit a port candidate on
    its own, but it must never be silently dropped either.

    Missing this distinction is why upstream PR 1868 was discarded with no candidate paths even
    though it changed the contract that an input request stays answerable across a later turn.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $normalized = $Path.Replace('\', '/')
    if (-not (Test-EveRadarExplicitlyExcludedPath -Path $normalized)) {
        return $false
    }

    return $normalized -match '\.test\.ts$'
}

function Get-EveRadarBehavioralEvidencePaths {
    <#
    .SYNOPSIS
    Returns every behavioral-evidence path represented by one git file record.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [psobject] $File
    )

    return @(
        @($File.PreviousPath, $File.Path) |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                (Test-EveRadarBehavioralEvidencePath -Path $_)
            } |
            Sort-Object -Unique
    )
}

function Test-EveRadarTrackedPath {
    <#
    .SYNOPSIS
    Determines whether an upstream path is a direct signal for the framework-neutral .NET client.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $normalized = $Path.Replace('\', '/')
    if (Test-EveRadarExplicitlyExcludedPath -Path $normalized) {
        return $false
    }

    if ($normalized -match '^packages/eve/src/client/') {
        return $true
    }

    if ($normalized -match '^packages/eve/src/protocol/') {
        return $true
    }

    return $normalized -match (
        '^packages/eve/src/runtime/input/types(?:\.test)?\.ts$') -or
        $normalized -match (
            '^packages/eve/src/channel/resolve-text(?:\.test)?\.ts$')
}

function Get-EveRadarScopeHint {
    <#
    .SYNOPSIS
    Maps an upstream file to the .NET client subsystem most likely to require parity review.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $normalized = $Path.Replace('\', '/')
    switch -Regex ($normalized) {
        '/(session|session-utils)\.(test\.)?ts$' { return 'session-state-and-turn-delivery' }
        '/(open-stream|ndjson|stream-follow)\.(integration\.)?(test\.)?ts$' {
            return 'streaming-and-reconnection'
        }
        '/(client|agent-host|url|client-error|agent-info-.+)\.(test\.)?ts$' {
            return 'client-transport-and-inspection'
        }
        '/(file-parts|authorization-message-parts|message-action-parts)\.(test\.)?ts$' {
            return 'message-content-and-input'
        }
        '/output-schema\.(test\.)?ts$' { return 'structured-output' }
        '/types\.ts$' { return 'public-contracts' }
        '/protocol/(routes|cancel-turn|reset-session)\.(test\.)?ts$' {
            return 'routes-and-session-control'
        }
        '/protocol/message\.(test\.)?ts$' { return 'stream-event-contract' }
        '/runtime/input/types(?:\.test)?\.ts$' { return 'human-input-contract' }
        '/channel/resolve-text(?:\.test)?\.ts$' { return 'human-input-resolution' }
        '/index\.ts$' { return 'public-exports' }
        default { return 'client-surface' }
    }
}

function Get-EveRadarTrackedPaths {
    <#
    .SYNOPSIS
    Returns every tracked source or destination path represented by one git file record.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [psobject] $File
    )

    return @(
        @($File.PreviousPath, $File.Path) |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                (Test-EveRadarTrackedPath -Path $_)
            } |
            Sort-Object -Unique
    )
}

function ConvertFrom-EveRadarNameStatusLine {
    <#
    .SYNOPSIS
    Parses one git diff-tree --name-status line into a stable path record.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Line
    )

    $parts = $Line -split "`t"
    if ($parts.Count -lt 2) {
        throw "Invalid git name-status line: $Line"
    }

    $status = $parts[0]
    if ($status -match '^[RC]\d+$') {
        if ($parts.Count -ne 3) {
            throw "Invalid git rename/copy line: $Line"
        }

        return [pscustomobject]@{
            Status = $status
            PreviousPath = $parts[1].Replace('\', '/')
            Path = $parts[2].Replace('\', '/')
        }
    }

    return [pscustomobject]@{
        Status = $status
        PreviousPath = $null
        Path = $parts[1].Replace('\', '/')
    }
}
