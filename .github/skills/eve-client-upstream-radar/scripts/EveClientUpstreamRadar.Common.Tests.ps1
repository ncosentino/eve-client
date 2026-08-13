BeforeAll {
    . (Join-Path $PSScriptRoot 'EveClientUpstreamRadar.Common.ps1')
}

Describe 'Eve client upstream radar helpers' {
    It 'resolves the repository containing the repo-local skill by default' {
        $repoRoot = Join-Path $TestDrive 'checkout'
        $scriptRoot = Join-Path `
            $repoRoot `
            '.github\skills\eve-client-upstream-radar\scripts'

        Resolve-EveRadarTargetRepoPath `
            -ScriptRoot $scriptRoot |
            Should -Be ([System.IO.Path]::GetFullPath($repoRoot))
    }

    It 'preserves an explicit target repository path' {
        $repoRoot = Join-Path $TestDrive 'explicit-checkout'

        Resolve-EveRadarTargetRepoPath `
            -TargetRepoPath $repoRoot `
            -ScriptRoot $PSScriptRoot |
            Should -Be ([System.IO.Path]::GetFullPath($repoRoot))
    }

    It 'extracts the declared reference version' {
        $source = 'public const string ReferenceEveVersion = "0.27.6";'

        Get-EveRadarReferenceVersion -ProtocolSource $source |
            Should -Be '0.27.6'
    }

    It 'returns empty state when none has been recorded' {
        $statePath = Join-Path $TestDrive 'missing\state.json'

        $state = Read-EveRadarState -StatePath $statePath

        $state.schemaVersion | Should -Be 2
        $state.baseline | Should -BeNullOrEmpty
        @($state.dismissals).Count | Should -Be 0
    }

    It 'upgrades a schema 1 state file without losing its baseline' {
        $statePath = Join-Path $TestDrive 'schema1\state.json'
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $statePath) -Force
        Set-Content -LiteralPath $statePath -Value (
            '{"schemaVersion":1,"baseline":{"eveVersion":"0.32.0",' +
            '"eveCommit":"1013aed31ee4b21d9af2ef8f7da069a6743f8af6",' +
            '"recordedAt":"2026-08-12T12:00:29.8299605+00:00"}}')

        $state = Read-EveRadarState -StatePath $statePath

        $state.schemaVersion | Should -Be 2
        $state.baseline.eveVersion | Should -Be '0.32.0'
        @($state.dismissals).Count | Should -Be 0
    }

    It 'rejects an unsupported state schema version' {
        $statePath = Join-Path $TestDrive 'unsupported-state.json'
        Set-Content -LiteralPath $statePath -Value '{"schemaVersion":99}'

        { Read-EveRadarState -StatePath $statePath } |
            Should -Throw '*Unsupported radar state schema version*'
    }

    It 'normalizes timestamps to invariant UTC across a read and write cycle' {
        $statePath = Join-Path $TestDrive 'drift\state.json'
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $statePath) -Force
        Set-Content -LiteralPath $statePath -Value (
            '{"schemaVersion":2,"baseline":{"eveVersion":"0.32.0",' +
            '"eveCommit":"1013aed31ee4b21d9af2ef8f7da069a6743f8af6",' +
            '"recordedAt":"2026-08-12T12:00:29.8299605+00:00"},' +
            '"dismissals":[{"sourceIdentity":"eve-client-upstream:pr:1861",' +
            '"fingerprint":"eve-fp:8a3284b11636","decision":"out-of-scope",' +
            '"targetCommit":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",' +
            '"upstreamHead":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",' +
            '"reason":"server internal",' +
            '"recordedAt":"2026-08-13T13:51:34.9670236+00:00"}]}')

        Save-EveRadarState -StatePath $statePath -State (
            Read-EveRadarState -StatePath $statePath)

        $rewritten = [System.IO.File]::ReadAllText($statePath)
        $rewritten |
            Should -BeLike '*2026-08-12T12:00:29.8299605+00:00*' `
                -Because 'A read-then-write cycle must not rewrite an instant in local time.'
        $rewritten | Should -BeLike '*2026-08-13T13:51:34.9670236+00:00*'
        $rewritten |
            Should -Not -BeLike '*2026-08-12T05:00:29*' `
                -Because 'The baseline instant must not drift into this machine offset.'
    }

    It 'round-trips saved state through a created directory' {
        $statePath = Join-Path $TestDrive 'nested\radar\state.json'
        $saved = [pscustomobject]@{
            schemaVersion = 1
            baseline = [pscustomobject]@{
                eveVersion = '0.31.3'
                eveCommit = '8e0bd60cd49246706a7ebdb8f7c84c3683048970'
                recordedAt = '2026-08-09T19:00:00.0000000+00:00'
            }
        }

        Save-EveRadarState -StatePath $statePath -State $saved

        $state = Read-EveRadarState -StatePath $statePath
        $state.baseline.eveVersion | Should -Be '0.31.3'
        $state.baseline.eveCommit |
            Should -Be '8e0bd60cd49246706a7ebdb8f7c84c3683048970'
        Test-Path -LiteralPath "$statePath.tmp" | Should -BeFalse
    }

    It 'records a dismissal and finds it by source identity' {
        $state = Add-EveRadarDismissal `
            -State ([pscustomobject]@{ schemaVersion = 2; baseline = $null; dismissals = @() }) `
            -SourceIdentity 'eve-client-upstream:pr:1861' `
            -Decision 'out-of-scope' `
            -TargetCommit '2C2392E8862F921FFAD0C3FE53D4F2321E07FE66' `
            -UpstreamHead '1CD563B3538B567BE8F8DB2B21E1B779DED5274F' `
            -Reason 'Instrumentation callbacks, not NDJSON client events.'

        $dismissal = Get-EveRadarDismissal `
            -State $state `
            -SourceIdentity 'eve-client-upstream:pr:1861'

        $dismissal.decision | Should -Be 'out-of-scope'
        $dismissal.fingerprint | Should -Be 'eve-fp:8a3284b11636'
        $dismissal.targetCommit |
            Should -Be '2c2392e8862f921ffad0c3fe53d4f2321e07fe66'
        $dismissal.upstreamHead |
            Should -Be '1cd563b3538b567be8f8db2b21e1b779ded5274f'
    }

    It 'returns null for a source identity that was never dismissed' {
        $state = [pscustomobject]@{ schemaVersion = 2; baseline = $null; dismissals = @() }

        Get-EveRadarDismissal `
            -State $state `
            -SourceIdentity 'eve-client-upstream:pr:9999' |
            Should -BeNullOrEmpty
    }

    It 'replaces an earlier dismissal for the same source identity' {
        $state = [pscustomobject]@{ schemaVersion = 2; baseline = $null; dismissals = @() }
        $state = Add-EveRadarDismissal `
            -State $state `
            -SourceIdentity 'eve-client-upstream:pr:1862' `
            -Decision 'out-of-scope' `
            -TargetCommit 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' `
            -UpstreamHead 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' `
            -Reason 'first'
        $state = Add-EveRadarDismissal `
            -State $state `
            -SourceIdentity 'eve-client-upstream:pr:1862' `
            -Decision 'already-present' `
            -TargetCommit 'cccccccccccccccccccccccccccccccccccccccc' `
            -UpstreamHead 'dddddddddddddddddddddddddddddddddddddddd' `
            -Reason 'second'

        @($state.dismissals).Count | Should -Be 1
        (Get-EveRadarDismissal `
            -State $state `
            -SourceIdentity 'eve-client-upstream:pr:1862').decision |
            Should -Be 'already-present'
    }

    It 'rejects a parity result that must not be cached as a dismissal' {
        {
            Add-EveRadarDismissal `
                -State ([pscustomobject]@{ schemaVersion = 2; baseline = $null; dismissals = @() }) `
                -SourceIdentity 'eve-client-upstream:pr:1861' `
                -Decision 'gap-confirmed' `
                -TargetCommit 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' `
                -UpstreamHead 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' `
                -Reason 'should not be cacheable'
        } | Should -Throw
    }

    It 'round-trips dismissals through saved state' {
        $statePath = Join-Path $TestDrive 'dismissals\state.json'
        $state = Add-EveRadarDismissal `
            -State ([pscustomobject]@{ schemaVersion = 2; baseline = $null; dismissals = @() }) `
            -SourceIdentity 'eve-client-upstream:pr:1980' `
            -Decision 'out-of-scope' `
            -TargetCommit 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' `
            -UpstreamHead 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' `
            -Reason 'Eval reporter callbacks are TypeScript-only.'

        Save-EveRadarState -StatePath $statePath -State $state

        $reloaded = Read-EveRadarState -StatePath $statePath
        (Get-EveRadarDismissal `
            -State $reloaded `
            -SourceIdentity 'eve-client-upstream:pr:1980').reason |
            Should -Be 'Eval reporter callbacks are TypeScript-only.'
    }

    It 'preserves dismissals when the baseline advances' {
        $state = Add-EveRadarDismissal `
            -State ([pscustomobject]@{
                schemaVersion = 2
                baseline = [pscustomobject]@{
                    eveVersion = '0.32.0'
                    eveCommit = '1013aed31ee4b21d9af2ef8f7da069a6743f8af6'
                    recordedAt = '2026-08-12T12:00:29.8299605+00:00'
                }
                dismissals = @()
            }) `
            -SourceIdentity 'eve-client-upstream:pr:1861' `
            -Decision 'out-of-scope' `
            -TargetCommit 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' `
            -UpstreamHead 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' `
            -Reason 'server internal'

        $update = Update-EveRadarBaseline `
            -State $state `
            -Version '0.34.0' `
            -Commit 'cccccccccccccccccccccccccccccccccccccccc'

        $update.Status | Should -Be 'Advanced'
        $update.State.schemaVersion | Should -Be 2
        @($update.State.dismissals).Count |
            Should -Be 1 -Because 'Advancing the baseline must not reset recorded dismissals.'
    }

    It 'bootstraps a baseline when no state exists' {
        $state = Read-EveRadarState -StatePath (Join-Path $TestDrive 'none.json')

        $update = Update-EveRadarBaseline `
            -State $state `
            -Version '0.31.3' `
            -Commit '8E0BD60CD49246706A7EBDB8F7C84C3683048970'

        $update.Status | Should -Be 'Bootstrapped'
        $update.State.baseline.eveCommit |
            Should -Be '8e0bd60cd49246706a7ebdb8f7c84c3683048970'
    }

    It 'keeps the original record time when the baseline is unchanged' {
        $recordedAt = '2026-08-01T00:00:00.0000000+00:00'
        $state = [pscustomobject]@{
            schemaVersion = 1
            baseline = [pscustomobject]@{
                eveVersion = '0.31.3'
                eveCommit = '8e0bd60cd49246706a7ebdb8f7c84c3683048970'
                recordedAt = $recordedAt
            }
        }

        $update = Update-EveRadarBaseline `
            -State $state `
            -Version '0.31.3' `
            -Commit '8e0bd60cd49246706a7ebdb8f7c84c3683048970' `
            -RecordedAt ([DateTimeOffset]::Parse('2026-08-09T00:00:00Z'))

        $update.Status | Should -Be 'Unchanged'
        $update.State.baseline.recordedAt | Should -Be $recordedAt
    }

    It 'normalizes a recorded time that JSON parsing coerced to local time' {
        $state = [pscustomobject]@{
            schemaVersion = 1
            baseline = [pscustomobject]@{
                eveVersion = '0.31.3'
                eveCommit = '8e0bd60cd49246706a7ebdb8f7c84c3683048970'
                recordedAt = [DateTime]::Parse('2026-08-09T12:21:48-07:00')
            }
        }

        $update = Update-EveRadarBaseline `
            -State $state `
            -Version '0.31.3' `
            -Commit '8e0bd60cd49246706a7ebdb8f7c84c3683048970'

        $update.State.baseline.recordedAt |
            Should -Be '2026-08-09T19:21:48.0000000+00:00'
    }

    It 'advances the baseline when the declared version changes' {
        $state = [pscustomobject]@{
            schemaVersion = 1
            baseline = [pscustomobject]@{
                eveVersion = '0.31.3'
                eveCommit = '8e0bd60cd49246706a7ebdb8f7c84c3683048970'
                recordedAt = '2026-08-01T00:00:00.0000000+00:00'
            }
        }

        $update = Update-EveRadarBaseline `
            -State $state `
            -Version '0.32.0' `
            -Commit '05f348023d4268c974c225c1189a283ace20b742' `
            -RecordedAt ([DateTimeOffset]::Parse('2026-08-09T00:00:00Z'))

        $update.Status | Should -Be 'Advanced'
        $update.State.baseline.eveVersion | Should -Be '0.32.0'
        $update.State.baseline.recordedAt |
            Should -Be ([DateTimeOffset]::Parse('2026-08-09T00:00:00Z').ToString('o'))
    }

    It 'rejects a release tag that moved to a different commit' {
        $state = [pscustomobject]@{
            schemaVersion = 1
            baseline = [pscustomobject]@{
                eveVersion = '0.31.3'
                eveCommit = '8e0bd60cd49246706a7ebdb8f7c84c3683048970'
                recordedAt = '2026-08-01T00:00:00.0000000+00:00'
            }
        }

        {
            Update-EveRadarBaseline `
                -State $state `
                -Version '0.31.3' `
                -Commit '05f348023d4268c974c225c1189a283ace20b742'
        } | Should -Throw '*release tag moved*'
    }

    It 'extracts a squash-merged pull request number' {
        Get-EveRadarPullRequestNumber `
            -Subject 'fix(eve): retry stream opens (#852)' |
            Should -Be 852
    }

    It 'returns null for a direct commit' {
        Get-EveRadarPullRequestNumber -Subject 'release package metadata' |
            Should -BeNullOrEmpty
    }

    It 'computes the stable upstream source fingerprint' {
        Get-EveRadarFingerprint `
            -SourceIdentity 'eve-client-upstream:pr:1219' |
            Should -Be 'aee12bc2b31d'
    }

    It 'accepts exact GitHub HTTPS and SSH remotes' {
        @(
            'https://github.com/ncosentino/eve-client.git',
            'git@github.com:ncosentino/eve-client.git',
            'ssh://git@github.com/ncosentino/eve-client'
        ) | ForEach-Object {
            Test-EveRadarGitHubRemoteUrl `
                -Url $_ `
                -Repository 'ncosentino/eve-client' |
                Should -BeTrue
        }
    }

    It 'rejects lookalike hosts and repository suffixes' {
        @(
            'https://evil.example/github.com/ncosentino/eve-client.git',
            'https://github.com/ncosentino/eve-client-archive.git',
            'git@example.com:ncosentino/eve-client.git'
        ) | ForEach-Object {
            Test-EveRadarGitHubRemoteUrl `
                -Url $_ `
                -Repository 'ncosentino/eve-client' |
                Should -BeFalse
        }
    }

    It 'tracks framework-neutral client and protocol paths' {
        @(
            'packages/eve/src/client/session.ts',
            'packages/eve/src/client/session.test.ts',
            'packages/eve/src/protocol/message.ts',
            'packages/eve/src/runtime/input/types.ts',
            'packages/eve/src/runtime/input/types.test.ts',
            'packages/eve/src/channel/resolve-text.ts',
            'packages/eve/src/channel/resolve-text.test.ts'
        ) | ForEach-Object {
            Test-EveRadarTrackedPath -Path $_ | Should -BeTrue
        }
    }

    It 'excludes TypeScript-only state and UI reducers' {
        @(
            'packages/eve/src/client/eve-agent-store.ts',
            'packages/eve/src/client/message-reducer.ts',
            'packages/eve/src/client/message-reducer.test.ts',
            'packages/eve/src/client/message-reducer-types.ts',
            'packages/eve/src/client/reducer.ts'
        ) | ForEach-Object {
            Test-EveRadarTrackedPath -Path $_ | Should -BeFalse
        }
    }

    It 'does not treat server implementation changes as direct client signals' {
        Test-EveRadarTrackedPath `
            -Path 'packages/eve/src/internal/nitro/host/build-application.ts' |
            Should -BeFalse
    }

    It 'treats an excluded reducer test as behavioral evidence' {
        Test-EveRadarBehavioralEvidencePath `
            -Path 'packages/eve/src/client/message-reducer.test.ts' |
            Should -BeTrue
    }

    It 'does not treat an excluded reducer implementation as behavioral evidence' {
        @(
            'packages/eve/src/client/message-reducer.ts',
            'packages/eve/src/client/eve-agent-store.ts',
            'packages/eve/src/client/message-reducer-types.ts'
        ) | ForEach-Object {
            Test-EveRadarBehavioralEvidencePath -Path $_ | Should -BeFalse
        }
    }

    It 'does not treat a tracked client test as behavioral evidence' {
        Test-EveRadarBehavioralEvidencePath `
            -Path 'packages/eve/src/client/session.test.ts' |
            Should -BeFalse
    }

    It 'returns behavioral evidence paths for a modified reducer test' {
        $file = ConvertFrom-EveRadarNameStatusLine `
            -Line "M`tpackages/eve/src/client/message-reducer.test.ts"
        @(Get-EveRadarBehavioralEvidencePaths -File $file) |
            Should -Be @('packages/eve/src/client/message-reducer.test.ts')
    }

    It 'returns no behavioral evidence paths for an unrelated file' {
        $file = ConvertFrom-EveRadarNameStatusLine `
            -Line "M`tpackages/eve/src/client/session.ts"
        @(Get-EveRadarBehavioralEvidencePaths -File $file).Count |
            Should -Be 0
    }

    It 'maps stream files to the streaming subsystem' {
        Get-EveRadarScopeHint `
            -Path 'packages/eve/src/client/open-stream.ts' |
            Should -Be 'streaming-and-reconnection'
    }

    It 'parses renamed files using the destination path' {
        $record = ConvertFrom-EveRadarNameStatusLine `
            -Line "R100`told.ts`tnew.ts"

        $record.Status | Should -Be 'R100'
        $record.PreviousPath | Should -Be 'old.ts'
        $record.Path | Should -Be 'new.ts'
    }

    It 'tracks a client file renamed out of the client surface' {
        $record = ConvertFrom-EveRadarNameStatusLine `
            -Line (
                "R100`tpackages/eve/src/client/session.ts" +
                "`tpackages/eve/src/internal/session.ts")

        @(Get-EveRadarTrackedPaths -File $record) |
            Should -Be @('packages/eve/src/client/session.ts')
    }

    It 'tracks a file renamed into the client surface' {
        $record = ConvertFrom-EveRadarNameStatusLine `
            -Line (
                "R100`tpackages/eve/src/internal/session.ts" +
                "`tpackages/eve/src/client/session.ts")

        @(Get-EveRadarTrackedPaths -File $record) |
            Should -Be @('packages/eve/src/client/session.ts')
    }

    It 'orders release tags by numeric version rather than string order' {
        $ordered = Get-EveRadarOrderedReleaseTags -TagName @(
            'eve@0.29.4',
            'eve@0.9.8',
            'eve@0.28.0',
            'eve@0.27.13',
            'eve@0.27.6')

        @($ordered | ForEach-Object { $_.Version }) |
            Should -Be @('0.9.8', '0.27.6', '0.27.13', '0.28.0', '0.29.4')
    }

    It 'orders a prerelease before its matching release' {
        $ordered = Get-EveRadarOrderedReleaseTags -TagName @(
            'eve@1.0.0',
            'eve@1.0.0-beta.2')

        @($ordered | ForEach-Object { $_.Version }) |
            Should -Be @('1.0.0-beta.2', '1.0.0')
    }

    It 'ignores tags that are not eve semver releases' {
        $ordered = Get-EveRadarOrderedReleaseTags -TagName @(
            'eve@0.28.0',
            'eve@nightly',
            'other@1.2.3',
            '',
            'v0.28.0')

        @($ordered | ForEach-Object { $_.Tag }) | Should -Be @('eve@0.28.0')
    }

    It 'returns nothing when no release tags are supplied' {
        @(Get-EveRadarOrderedReleaseTags -TagName @()).Count | Should -Be 0
        @(Get-EveRadarOrderedReleaseTags).Count | Should -Be 0
    }
}
