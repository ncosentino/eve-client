[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version,

    [switch] $DryRun,

    [switch] $SkipCiCheck
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if ((git status --porcelain).Length -ne 0) {
        throw "The working tree must be clean before releasing."
    }

    $branch = (git branch --show-current).Trim()
    if ($branch -cne "main") {
        throw "Releases must be prepared from main; current branch is '$branch'."
    }

    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet tool restore failed."
    }

    $nbgvVersion = (& dotnet nbgv get-version -v SemVer2).Trim()
    $packageVersion = (& dotnet nbgv get-version -v NuGetPackageVersion).Trim()
    if ($nbgvVersion -cne $Version) {
        throw "Requested version '$Version' does not match NBGV version '$nbgvVersion'."
    }

    if (-not (Select-String `
        -Path CHANGELOG.md `
        -Pattern "## [$Version]" `
        -SimpleMatch `
        -Quiet)) {
        throw "CHANGELOG.md does not contain a '$Version' release section."
    }

    $tag = "v$Version"
    if (git tag --list $tag) {
        throw "Tag '$tag' already exists."
    }

    $head = (git rev-parse HEAD).Trim()
    if (-not $SkipCiCheck) {
        git fetch origin main --quiet
        if ($LASTEXITCODE -ne 0) {
            throw "Could not fetch origin/main."
        }

        $originMain = (git rev-parse origin/main).Trim()
        if ($head -cne $originMain) {
            throw "HEAD must match origin/main before releasing."
        }

        $repository = (gh repo view --json nameWithOwner --jq .nameWithOwner).Trim()
        $runs = gh api `
            -H "Accept: application/vnd.github+json" `
            "/repos/$repository/actions/workflows/ci.yml/runs?branch=main&per_page=100"
        if ($LASTEXITCODE -ne 0) {
            throw "Could not query the CI workflow."
        }

        $successfulRun = ($runs | ConvertFrom-Json).workflow_runs |
            Where-Object {
                $_.head_sha -ceq $head -and
                $_.event -in @("push", "workflow_dispatch") -and
                $_.status -ceq "completed" -and
                $_.conclusion -ceq "success"
            } |
            Select-Object -First 1
        if ($null -eq $successfulRun) {
            throw "No successful main CI run exists for commit '$head'."
        }
    }

    Remove-Item artifacts/packages -Recurse -Force -ErrorAction SilentlyContinue
    & dotnet restore
    if ($LASTEXITCODE -ne 0) {
        throw "Release restore failed."
    }

    & dotnet format --no-restore --verify-no-changes
    if ($LASTEXITCODE -ne 0) {
        throw "Release formatting verification failed."
    }

    & dotnet build --configuration Release --no-restore --no-incremental
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed."
    }

    & dotnet test --configuration Release --no-build --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        throw "Release tests failed."
    }

    & npm ci --prefix test/fixtures/eve-agent --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) {
        throw "Eve fixture dependency installation failed."
    }

    $previousProbeNoBuild = $env:EVE_PROBE_NO_BUILD
    try {
        $env:EVE_PROBE_NO_BUILD = "1"
        & npm run test:client --prefix test/fixtures/eve-agent
        if ($LASTEXITCODE -ne 0) {
            throw "The live Eve compatibility probe failed."
        }
    }
    finally {
        $env:EVE_PROBE_NO_BUILD = $previousProbeNoBuild
    }

    & dotnet pack `
        src/NexusLabs.Eve/NexusLabs.Eve.csproj `
        --configuration Release `
        --no-build `
        -p:PublicRelease=true `
        --output artifacts/packages
    if ($LASTEXITCODE -ne 0) {
        throw "Release pack failed."
    }

    & ./scripts/validate-packages.ps1 -ExpectedVersion $packageVersion
    & ./scripts/test-package-consumer.ps1 -ExpectedVersion $packageVersion

    & python -m pip install --quiet -r requirements-docs.txt
    if ($LASTEXITCODE -ne 0) {
        throw "Documentation dependency installation failed."
    }

    & python -m unittest discover -s scripts/tests -p "test_*.py"
    if ($LASTEXITCODE -ne 0) {
        throw "Documentation script tests failed."
    }

    & ./scripts/generate-api-docs.ps1 -OutputDirectory docs/api/dev
    & ./scripts/generate-api-docs.ps1 -OutputDirectory "docs/api/v$Version"
    & ./scripts/generate-api-docs.ps1 `
        -OutputDirectory docs/api/stable `
        -PreserveRootIndex
    & python -m mkdocs build --strict
    if ($LASTEXITCODE -ne 0) {
        throw "Documentation build failed."
    }

    if ($DryRun) {
        Write-Host "Release dry run succeeded for $tag."
        return
    }

    git tag $tag
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create tag '$tag'."
    }

    Write-Host "Created local tag '$tag'."
    Write-Host "Push it explicitly with: git push origin $tag"
}
finally {
    Pop-Location
}
