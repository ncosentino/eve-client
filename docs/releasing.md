---
description: Prepare and publish a trusted NexusLabs.Eve release.
---

# Releasing

The complete maintainer checklist is in
[`RELEASING.md`](https://github.com/ncosentino/eve-client/blob/main/RELEASING.md).

Releases require:

- A clean `main` branch matching `origin/main`.
- Successful CI for the exact release commit.
- A matching NBGV version, semantic version tag, and changelog section.
- The protected `release` GitHub environment.
- A NuGet.org trusted publishing policy for `release.yml`.
- Cloudflare Pages secrets and `DOCS_DEPLOY_ENABLED=true` for canonical docs.

The release script validates and tags locally but never pushes `main`:

```powershell
./scripts/release.ps1 -Version 0.1.0-alpha.1 -DryRun
./scripts/release.ps1 -Version 0.1.0-alpha.1
git push origin v0.1.0-alpha.1
```
