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

function Get-EveRadarReadmeReference {
    <#
    .SYNOPSIS
    Reads the upstream eve version and commit documented in README.md.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Readme
    )

    $match = [regex]::Match(
        $Readme,
        '(?is)compatibility target\s+is\s+Vercel\s+`eve`\s+\*\*(?<version>[^*]+)\*\*' +
        '\s+at\s+commit\s+`(?<commit>[0-9a-f]{40})`')
    if (-not $match.Success) {
        throw 'Could not find the README compatibility version and commit.'
    }

    return [pscustomobject]@{
        Version = $match.Groups['version'].Value.Trim()
        Commit = $match.Groups['commit'].Value.ToLowerInvariant()
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
