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
    Schema 1 recorded only the baseline. Schema 2 adds dismissal records so a candidate the
    radar analyzed and dismissed is remembered instead of being re-analyzed on every run.
    A schema 1 file is upgraded in memory rather than rejected, because the baseline it
    carries is still valid.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $StatePath
    )

    if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) {
        return [pscustomobject]@{
            schemaVersion = 2
            baseline = $null
            dismissals = @()
        }
    }

    $state = [System.IO.File]::ReadAllText($StatePath) | ConvertFrom-Json
    $schemaVersion = Get-EveRadarJsonProperty -InputObject $state -Name 'schemaVersion'
    if ($schemaVersion -notin 1, 2) {
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

    return [pscustomobject]@{
        schemaVersion = 2
        baseline = $baseline
        dismissals = $dismissals
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
        schemaVersion = 2
        baseline = Get-EveRadarJsonProperty -InputObject $State -Name 'baseline'
        dismissals = @($existing + $record)
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
            schemaVersion = 2
            baseline = [pscustomobject]@{
                eveVersion = $Version
                eveCommit = $normalizedCommit
                recordedAt = $baselineRecordedAt
            }
            dismissals = @(
                (Get-EveRadarJsonProperty -InputObject $State -Name 'dismissals') |
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
