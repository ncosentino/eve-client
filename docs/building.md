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
