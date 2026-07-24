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
- The compatibility baseline is Vercel eve 0.27.3, stream protocol version 19.
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

## Commit Workflow

Before every `git commit`, complete this procedure:

1. **Build and test.** Run the build and tests for this project's stack(s) and record the exact output — pass/fail/skip and warning/error counts. The exact commands (and any stack-specific caveats) live in the path-scoped instructions under `.github/instructions/`.
2. **Self-assess.** Write an honest one-line note for each — HIGH: omitted behavior, implementation gaps, test results; MEDIUM: tech debt, missing coverage, weak assertions; LOW: assumptions.
3. **Share and gate.** Share the self-assessment with the user. Fix any HIGH issue before committing; for any MEDIUM issue, stop and get the user's acknowledgment before proceeding.
4. **Commit.** The pre-commit hook blocks the first attempt by design. Acknowledge and commit:

   ```sh
   GENESIS_PRECOMMIT_ACK=true git commit -m "type: description"
   ```

   On Windows (PowerShell): set `$env:GENESIS_PRECOMMIT_ACK = "true"`, then run `git commit`.
5. **Share evidence.** After the commit succeeds, report exact test counts, what they verified, build warning/error counts, and files changed. Do not say "all tests pass" — show the numbers.

## Out of Scope

- Reimplementing the eve agent runtime, filesystem authoring framework, or CLI.
- Porting React, Vue, Svelte, or JavaScript AI SDK state-management helpers.
- Hiding preview protocol changes behind silent fallbacks.
