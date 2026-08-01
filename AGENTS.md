# Agent Instructions

## Behavior

- Be unbiased. Do not optimize for agreement.
- When weighing options, always do a pros/cons analysis.
- Always compare the main plausible paths and explain tradeoffs.
- Do not blindly agree with the user; compare and contrast alternatives fairly.
- State uncertainty explicitly.
- Distinguish verified facts from assumptions.

### Coding Behavior

- Do NOT rely on your training data for latest language and tech stack versions. Research with web searches.
- Back important claims with concrete evidence from code, tests, outputs, docs, or measurements.

### Research Behavior

- Run multiple parallel sub agents to collect data.
- Analyze the results to form a consensus to present to the user.
- Back up any claims with concrete evidence and citations.

## Project Overview

NexusLabs.Eve is a dependency-free .NET 10 client for the stable HTTP protocol exposed
by Vercel eve agents. It serves backend services, jobs, tests, and custom .NET user
interfaces that need durable session, streaming, continuation, cancellation, attachment,
and structured-output support without using the TypeScript SDK.

## Architecture

- `EveClient` owns host, auth, header, and delivery policy but not the supplied
  `HttpMessageInvoker`.
- `EveSession` owns an immutable `EveSessionState` cursor and advances it only after
  consumed stream events.
- `EveStreamFollower` mirrors upstream absolute-index reconnect and idle-budget behavior.
- Preview event and inspection payloads retain raw `JsonElement` values so unknown
  upstream fields and event types remain available.
- The compatibility baseline is Vercel eve 0.29.4, stream protocol version 20.
- `test/fixtures/eve-agent` is a pinned, deterministic real Eve server used by the
  compatibility probe under `tests/NexusLabs.Eve.CompatibilityProbe`.
- `version.json` is the only release-version source; package versions come from NBGV.
- MkDocs publishes development, stable, and immutable versioned API documentation.

## Conventions

- Keep the package free of runtime dependencies unless a protocol feature cannot be
  implemented safely with the .NET base class library.
- Require explicit cancellation tokens on I/O APIs.
- Preserve TypeScript client semantics for routes, session state, header precedence,
  human-input delivery retries, turn boundaries, and stream reconnection.
- Add a contract test for every externally observable protocol change.
- Never hard-code a package version in a project file or publish with a long-lived
  NuGet API key.

## Validation

```shell
dotnet tool restore
dotnet restore
dotnet format --no-restore --verify-no-changes
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
npm ci --prefix test/fixtures/eve-agent
npm run test:client --prefix test/fixtures/eve-agent
pwsh scripts/generate-api-docs.ps1 -OutputDirectory docs/api/dev
python -m mkdocs build --strict
dotnet pack src/NexusLabs.Eve/NexusLabs.Eve.csproj --configuration Release --output artifacts/packages
pwsh scripts/validate-packages.ps1
```

Stack-specific conventions and the exact build/test/lint commands are provided as
path-scoped instructions under `.github/instructions/` and load automatically for the
files they match (for example, C# error-handling rules on `*.cs`, the npm quality gate on
`package.json` / `*.ts`). Consult them when working in a given stack.

## Pull Request Delivery

- Local commits are unrestricted checkpoints. The initial default branch may be created only
  when bootstrapping a truly empty remote; after that, push only feature branches and deliver
  through pull requests.
- Run targeted checks while iterating. This repository currently uses GitHub-hosted runners;
  any future self-hosted route must force public fork pull requests onto an allow-listed hosted
  runner before repository variables, matrices, or job outputs can influence runner selection.
- Branch-dispatched validation is appropriate before a pull request only when pushing the branch
  itself is safe. Do not expose private or internal details through a public branch.
- "Open a PR" and "publish a PR" mean ready for review. "Open a draft PR" and "open a PR so I can
  review" mean draft. Agent-initiated pull requests default to draft unless the user explicitly
  requests ready delivery.
- Genesis drafts run the `preflight` subset and publish `Draft CI`. Moving a pull request to ready
  starts fresh full validation and publishes the required `CI` check.
- When `GENESIS_REVIEW_POLICY=copilot-one-approval`, a ready Copilot-authored pull request requires
  one OWNER, MEMBER, or COLLABORATOR approval on its current SHA. A later Copilot push invalidates
  the prior approval.
- In public repositories, external fork workflows require explicit maintainer approval before
  execution. Treat approval as permission to run the complete proposed workflow, including any
  runner selection made by the pull request.
- Native merges use GitHub's branch auto-delete setting. The inactive private workflow-run fallback
  may be installed only for a private repository whose plan cannot enforce branch protection.

Before opening a ready pull request, publishing a draft, or pushing more commits to an already-ready
pull request:

1. Confirm the pull request title follows conventional commit semantics; squash merging uses it as
   the default-branch commit subject.
2. Record validation evidence and assess HIGH: omitted behavior, implementation gaps, and
   failing/missing tests; MEDIUM: technical debt, missing coverage, and weak assertions; LOW:
   assumptions.
3. Fix every HIGH issue or keep/return the pull request to draft. Disclose MEDIUM and LOW findings
   in the pull request body.
4. When protected native auto-merge is configured, arm it only for a ready pull request after this
   assessment.

## Out of Scope

- Reimplementing the eve agent runtime, filesystem authoring framework, or CLI.
- Porting React, Vue, Svelte, or JavaScript AI SDK state-management helpers.
- Hiding preview protocol changes behind silent fallbacks.
