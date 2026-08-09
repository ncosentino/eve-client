<#
.SYNOPSIS
Collects the exact upstream eve commit delta beyond the .NET client's declared baseline.

.DESCRIPTION
Reads the committed ncosentino/eve-client main ref without inspecting working-tree files,
verifies that its protocol constant and npm fixture agree, updates a
radar-owned partial clone of vercel/eve, and writes a deterministic JSON inventory.

.PARAMETER TargetRepoPath
Optional path to the ncosentino/eve-client clone whose remote main ref defines current
parity. Defaults to the repository containing this repo-local skill.

.PARAMETER TargetRepository
Expected GitHub owner/repository identifier for the target clone.

.PARAMETER TargetRemote
Target git remote whose branch defines committed parity.

.PARAMETER TargetBranch
Target remote branch to fetch and inspect.

.PARAMETER UpstreamRepository
GitHub owner/repository identifier for the official Eve implementation.

.PARAMETER UpstreamBranch
Official Eve branch compared with the declared compatibility baseline.

.PARAMETER CachePath
Radar-owned partial clone used for immutable upstream git objects.

.PARAMETER StatePath
Radar-owned durable state file recording the verified upstream release baseline.

.PARAMETER OutputPath
Absolute JSON path for the generated inventory.

.PARAMETER SkipTargetFetch
Uses the currently available target remote ref without fetching. Intended for supervised tests.

.PARAMETER SkipUpstreamFetch
Uses the currently available radar-owned upstream cache without fetching. Intended for tests.
#>
[CmdletBinding()]
param(
    [string] $TargetRepoPath,
    [string] $TargetRepository = 'ncosentino/eve-client',
    [string] $TargetRemote = 'origin',
    [string] $TargetBranch = 'main',
    [string] $UpstreamRepository = 'vercel/eve',
    [string] $UpstreamBranch = 'main',
    [string] $CachePath = $(
        Join-Path `
            -Path ([Environment]::GetFolderPath(
                [Environment+SpecialFolder]::LocalApplicationData)) `
            -ChildPath 'eve-client-upstream-radar\repositories\vercel-eve'),
    [string] $StatePath = $(
        Join-Path `
            -Path ([Environment]::GetFolderPath(
                [Environment+SpecialFolder]::LocalApplicationData)) `
            -ChildPath 'eve-client-upstream-radar\state.json'),
    [Parameter(Mandatory)]
    [string] $OutputPath,
    [switch] $SkipTargetFetch,
    [switch] $SkipUpstreamFetch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'EveClientUpstreamRadar.Common.ps1')

$TargetRepoPath = Resolve-EveRadarTargetRepoPath `
    -TargetRepoPath $TargetRepoPath `
    -ScriptRoot $PSScriptRoot

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = @(& git @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed:`n$($output -join "`n")"
    }

    return $output
}

function Get-GitTextAtRef {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryPath,
        [Parameter(Mandatory)]
        [string] $Ref,
        [Parameter(Mandatory)]
        [string] $Path
    )

    return (
        Invoke-Git -Arguments @(
            '-C',
            $RepositoryPath,
            'show',
            "${Ref}:$Path"
        )) -join "`n"
}

if (-not (Test-Path -LiteralPath $TargetRepoPath -PathType Container)) {
    throw "Target repository was not found at '$TargetRepoPath'."
}

$targetGitDirectory = Join-Path $TargetRepoPath '.git'
if (-not (Test-Path -LiteralPath $targetGitDirectory)) {
    throw "Target path is not a git working tree: '$TargetRepoPath'."
}

$targetRemoteUrl = (
    Invoke-Git -Arguments @(
        '-C',
        $TargetRepoPath,
        'remote',
        'get-url',
        $TargetRemote
    ) | Select-Object -First 1).Trim()
if (-not (Test-EveRadarGitHubRemoteUrl `
        -Url $targetRemoteUrl `
        -Repository $TargetRepository)) {
    throw (
        "Remote '$TargetRemote' points to '$targetRemoteUrl', not " +
        "'$TargetRepository'.")
}

$targetRef = "refs/remotes/$TargetRemote/$TargetBranch"
if (-not $SkipTargetFetch) {
    $null = Invoke-Git -Arguments @(
        '-C',
        $TargetRepoPath,
        'fetch',
        '--quiet',
        $TargetRemote,
        "+refs/heads/${TargetBranch}:$targetRef"
    )
}

$targetCommit = (
    Invoke-Git -Arguments @(
        '-C',
        $TargetRepoPath,
        'rev-parse',
        "${targetRef}^{commit}"
    ) | Select-Object -First 1).Trim().ToLowerInvariant()

$protocolSource = Get-GitTextAtRef `
    -RepositoryPath $TargetRepoPath `
    -Ref $targetCommit `
    -Path 'src/NexusLabs.Eve/EveProtocol.cs'
$fixtureJson = Get-GitTextAtRef `
    -RepositoryPath $TargetRepoPath `
    -Ref $targetCommit `
    -Path 'test/fixtures/eve-agent/package.json'

$referenceVersion = Get-EveRadarReferenceVersion -ProtocolSource $protocolSource
$fixtureVersion = ($fixtureJson | ConvertFrom-Json).dependencies.eve

if ($fixtureVersion -ne $referenceVersion) {
    throw (
        "Fixture eve version '$fixtureVersion' does not match " +
        "EveProtocol.ReferenceEveVersion '$referenceVersion'.")
}

$cacheParent = Split-Path -Parent $CachePath
if (-not (Test-Path -LiteralPath $cacheParent)) {
    $null = New-Item -ItemType Directory -Path $cacheParent -Force
}

$upstreamUrl = "https://github.com/$UpstreamRepository.git"
if (-not (Test-Path -LiteralPath $CachePath -PathType Container)) {
    $null = Invoke-Git -Arguments @(
        'clone',
        '--filter=blob:none',
        '--no-checkout',
        '--quiet',
        $upstreamUrl,
        $CachePath
    )
}

if (-not (Test-Path -LiteralPath (Join-Path $CachePath '.git'))) {
    throw "Upstream cache is not a git working tree: '$CachePath'."
}

$cachedRemoteUrl = (
    Invoke-Git -Arguments @(
        '-C',
        $CachePath,
        'remote',
        'get-url',
        'origin'
    ) | Select-Object -First 1).Trim()
if (-not (Test-EveRadarGitHubRemoteUrl `
        -Url $cachedRemoteUrl `
        -Repository $UpstreamRepository)) {
    throw (
        "Upstream cache points to '$cachedRemoteUrl', not " +
        "'$UpstreamRepository'.")
}

$upstreamRef = "refs/remotes/origin/$UpstreamBranch"
$tagRef = "refs/tags/eve@$referenceVersion"
if (-not $SkipUpstreamFetch) {
    $null = Invoke-Git -Arguments @(
        '-C',
        $CachePath,
        'fetch',
        '--quiet',
        'origin',
        "+refs/heads/${UpstreamBranch}:$upstreamRef",
        "+refs/tags/eve@${referenceVersion}:$tagRef",
        '+refs/tags/eve@*:refs/tags/eve@*'
    )
}

$baselineCommit = (
    Invoke-Git -Arguments @(
        '-C',
        $CachePath,
        'rev-parse',
        "${tagRef}^{commit}"
    ) | Select-Object -First 1).Trim().ToLowerInvariant()
$upstreamHeadCommit = (
    Invoke-Git -Arguments @(
        '-C',
        $CachePath,
        'rev-parse',
        "${upstreamRef}^{commit}"
    ) | Select-Object -First 1).Trim().ToLowerInvariant()

$baselineUpdate = Update-EveRadarBaseline `
    -State (Read-EveRadarState -StatePath $StatePath) `
    -Version $referenceVersion `
    -Commit $baselineCommit

$null = & git -C $CachePath merge-base --is-ancestor $baselineCommit $upstreamHeadCommit
if ($LASTEXITCODE -ne 0) {
    throw (
        "The declared baseline '$baselineCommit' is not an ancestor of " +
        "upstream '$UpstreamBranch' at '$upstreamHeadCommit'.")
}

Save-EveRadarState -StatePath $StatePath -State $baselineUpdate.State

$commitShas = @(
    Invoke-Git -Arguments @(
        '-C',
        $CachePath,
        'rev-list',
        '--reverse',
        "$baselineCommit..$upstreamHeadCommit"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)

# Only releases that already contain the baseline can contain a delta commit.
# Walking them from oldest to newest and keeping the first claim gives each
# commit the lowest published version a consumer can install to get it.
$candidateReleaseTags = @(
    Invoke-Git -Arguments @(
        '-C',
        $CachePath,
        'tag',
        '--contains',
        $baselineCommit,
        '--list',
        'eve@*'
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
)
$releaseVersionByCommit = @{}
foreach ($release in Get-EveRadarOrderedReleaseTags -TagName $candidateReleaseTags) {
    $releasedShas = @(
        Invoke-Git -Arguments @(
            '-C',
            $CachePath,
            'rev-list',
            "$baselineCommit..refs/tags/$($release.Tag)"
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )

    foreach ($releasedSha in $releasedShas) {
        $key = $releasedSha.Trim().ToLowerInvariant()
        if (-not $releaseVersionByCommit.ContainsKey($key)) {
            $releaseVersionByCommit[$key] = $release.Version
        }
    }
}

$commits = foreach ($sha in $commitShas) {
    $subject = (
        Invoke-Git -Arguments @(
            '-C',
            $CachePath,
            'show',
            '-s',
            '--format=%s',
            $sha
        ) | Select-Object -First 1).Trim()
    $authoredAt = (
        Invoke-Git -Arguments @(
            '-C',
            $CachePath,
            'show',
            '-s',
            '--format=%aI',
            $sha
        ) | Select-Object -First 1).Trim()
    $nameStatusLines = @(
        Invoke-Git -Arguments @(
            '-C',
            $CachePath,
            'diff-tree',
            '--root',
            '--first-parent',
            '--no-commit-id',
            '--name-status',
            '-r',
            '--find-renames',
            $sha
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    $files = @(
        $nameStatusLines |
            ForEach-Object { ConvertFrom-EveRadarNameStatusLine -Line $_ }
    )
    $relevantFiles = @(
        $files |
            Where-Object { @(Get-EveRadarTrackedPaths -File $_).Count -gt 0 }
    )
    $relevantPaths = @(
        $relevantFiles |
            ForEach-Object { Get-EveRadarTrackedPaths -File $_ } |
            Sort-Object -Unique
    )
    $scopeHints = @(
        $relevantPaths |
            ForEach-Object { Get-EveRadarScopeHint -Path $_ } |
            Sort-Object -Unique
    )
    $pullRequestNumber = Get-EveRadarPullRequestNumber -Subject $subject
    $shaKey = $sha.Trim().ToLowerInvariant()
    $releasedInVersion = if ($releaseVersionByCommit.ContainsKey($shaKey)) {
        $releaseVersionByCommit[$shaKey]
    }
    else {
        $null
    }

    [pscustomobject]@{
        Sha = $shaKey
        Subject = $subject
        AuthoredAt = $authoredAt
        PullRequestNumber = $pullRequestNumber
        PullRequestUrl = if ($null -eq $pullRequestNumber) {
            $null
        }
        else {
            "https://github.com/$UpstreamRepository/pull/$pullRequestNumber"
        }
        CommitUrl = "https://github.com/$UpstreamRepository/commit/$sha"
        ReleasedInVersion = $releasedInVersion
        CandidateByPath = @($relevantFiles).Count -gt 0
        ScopeHints = $scopeHints
        RelevantPaths = $relevantPaths
        Files = $files
    }
}

$result = [pscustomobject]@{
    SchemaVersion = 3
    GeneratedAt = [DateTimeOffset]::UtcNow.ToString('o')
    Target = [pscustomobject]@{
        Repository = $TargetRepository
        RepositoryPath = [System.IO.Path]::GetFullPath($TargetRepoPath)
        Ref = $targetRef
        Commit = $targetCommit
        ReferenceEveVersion = $referenceVersion
        ReferenceEveCommit = $baselineCommit
        BaselineStatus = $baselineUpdate.Status
        FixtureEveVersion = $fixtureVersion
    }
    Upstream = [pscustomobject]@{
        Repository = $UpstreamRepository
        Ref = $upstreamRef
        HeadCommit = $upstreamHeadCommit
        CachePath = [System.IO.Path]::GetFullPath($CachePath)
    }
    Delta = [pscustomobject]@{
        CommitCount = @($commits).Count
        PathCandidateCount = @($commits | Where-Object CandidateByPath).Count
        UnreleasedPathCandidateCount = @(
            $commits |
                Where-Object { $_.CandidateByPath -and $null -eq $_.ReleasedInVersion }).Count
    }
    Commits = @($commits)
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    $null = New-Item -ItemType Directory -Path $outputDirectory -Force
}

$json = $result | ConvertTo-Json -Depth 10
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($OutputPath),
    $json,
    [System.Text.UTF8Encoding]::new($false))

return $result
