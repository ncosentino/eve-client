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

        $state.schemaVersion | Should -Be 1
        $state.baseline | Should -BeNullOrEmpty
    }

    It 'rejects an unsupported state schema version' {
        $statePath = Join-Path $TestDrive 'unsupported-state.json'
        Set-Content -LiteralPath $statePath -Value '{"schemaVersion":99}'

        { Read-EveRadarState -StatePath $statePath } |
            Should -Throw '*Unsupported radar state schema version*'
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
