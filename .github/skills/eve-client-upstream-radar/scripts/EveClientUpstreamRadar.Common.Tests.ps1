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

    It 'extracts a multiline README baseline' {
        $commit = '05f348023d4268c974c225c1189a283ace20b742'
        $readme = @"
The initial compatibility target is Vercel ``eve`` **0.27.6** at commit
``$commit``, whose message stream protocol is version **19**.
"@

        $reference = Get-EveRadarReadmeReference -Readme $readme

        $reference.Version | Should -Be '0.27.6'
        $reference.Commit | Should -Be $commit
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
}
