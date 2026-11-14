---
description: Build, test, package, and validate NexusLabs.Eve from source.
---

# Building

## Prerequisites

- .NET 10 SDK
- Node.js 24
- Python 3 for documentation

## Build and test

```shell
dotnet tool restore
dotnet restore
dotnet format --no-restore --verify-no-changes
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

## Run the live Eve fixture

```shell
npm ci --prefix test/fixtures/eve-agent
npm run test:client --prefix test/fixtures/eve-agent
```

## Package

```shell
dotnet pack src/NexusLabs.Eve/NexusLabs.Eve.csproj \
  --configuration Release \
  --output artifacts/packages

pwsh scripts/validate-packages.ps1
```

## Documentation

```shell
pip install -r requirements-docs.txt
pwsh scripts/generate-api-docs.ps1 -OutputDirectory docs/api/dev
python -m mkdocs build --strict
```

API browser and social titles include the qualified type or namespace and the
development, stable, or immutable version context. The metadata hook leaves
visible headings and navigation labels unchanged.

Descriptions use the same API identity followed by the XML-backed introduction,
or a guide's title and first introductory paragraph. Code, notes, tables, and
member-only prose are not summaries; pages without introductory prose use their
identity alone. Maintain ordinary XML comments and guide introductions rather
than adding bespoke descriptions. Existing nonempty descriptions take precedence,
and the homepage retains its configured description unless explicitly overridden.
Source metadata stays plain text; HTML attributes and JSON-LD are escaped only
when rendered. This policy applies to normal builds, not frozen archive backfills.

The hosted `Documentation titles` workflow renders focused fixtures and previews
every published stable/archive page without writing to the archive. It also
demonstrates the regression with the metadata hook disabled.

For already-published API pages, dispatch that workflow on `main` with
`refresh_published_titles=true`. It validates the complete archive before changing
only browser, Open Graph, and Twitter title values, retains an outside-site
receipt, commits to `gh-pages` without force pushing, and updates GitHub Pages.
Repeat dispatches are safe. Historical API content is never regenerated.

After successful maintenance, dispatch the existing `Documentation Deployment`
workflow to update the canonical Cloudflare mirror. Its existing enable flag,
environment, credentials, and 20-version retention policy remain unchanged.
All publication workflows share the non-cancelling `pages` lock.
