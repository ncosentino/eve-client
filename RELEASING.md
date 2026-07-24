# Releasing NexusLabs.Eve

Releases use Nerdbank.GitVersioning, semantic version tags, NuGet trusted
publishing, and the `release` GitHub environment.

## One-time NuGet.org setup

Configure a trusted publishing policy for the `NexusLabs.Eve` package:

1. Sign in to NuGet.org as `ncosentino`.
2. Reserve or create the `NexusLabs.Eve` package ID.
3. Open the package's **Trusted Publishing** settings.
4. Add a GitHub Actions policy with:
   - Organization or user: `ncosentino`
   - Repository: `eve-client`
   - Workflow file: `release.yml`
   - Environment: `release`
5. Do not create or store a long-lived NuGet API key.

The release workflow exchanges GitHub's OIDC identity for a short-lived NuGet
credential through `NuGet/login@v1`.

## One-time GitHub setup

1. Create a `release` environment.
2. Add required reviewers if releases should require approval.
3. Enable GitHub Pages with **GitHub Actions** as the source.
4. Require the `CI` workflow on `main` before merging.

No NuGet secret is required. `GITHUB_TOKEN` publishes to GitHub Packages and
creates the GitHub Release.

## Prepare a release

```powershell
git checkout main
git pull
dotnet tool restore
dotnet nbgv set-version 0.1.0-alpha.2
```

Move the completed entries from `Unreleased` into:

```markdown
## [0.1.0-alpha.2] - YYYY-MM-DD
```

Update the comparison links at the bottom of `CHANGELOG.md`, then commit the
version and changelog changes through the normal pull-request flow.

## Validate and tag

After the release commit is on `origin/main` and CI is green:

```powershell
./scripts/release.ps1 -Version 0.1.0-alpha.2 -DryRun
./scripts/release.ps1 -Version 0.1.0-alpha.2
git push origin v0.1.0-alpha.2
```

The script never pushes `main`. It validates the repository, creates the local
tag, and leaves the explicit tag push to the maintainer.

## Release workflow

Pushing the tag:

1. Verifies that the same commit passed the `CI` workflow on `main`.
2. Validates the tag, NBGV version, and changelog section.
3. Builds, tests, packs, and validates the package.
4. Publishes to NuGet.org and GitHub Packages.
5. Creates a GitHub Release with `.nupkg` and `.snupkg` assets.
6. Generates and deploys stable and versioned documentation.

## Verify the release

- NuGet.org lists the expected `NexusLabs.Eve` version.
- The GitHub Release contains both package artifacts.
- The documentation site exposes the new stable and versioned API reference.
- A clean sample project can restore and instantiate `NexusLabs.Eve`.
