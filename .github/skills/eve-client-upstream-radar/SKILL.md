---
name: eve-client-upstream-radar
description: >
  Compare ncosentino/eve-client's declared compatibility baseline with merged
  framework-neutral client changes on vercel/eve main, verify each change
  against the committed C# implementation, and create deduplicated,
  agent-ready GitHub issues for confirmed parity gaps. Designed for scheduled
  unattended runs without modifying either repository.
user-invocable: true
compatibility: Requires git, PowerShell 7, GitHub CLI authentication, and execution from an ncosentino/eve-client checkout.
allowed-tools: Bash(git:*) Bash(gh:*) Bash(pwsh:*) Read Task
---

# Eve Client Upstream Radar

Track merged TypeScript client fixes and improvements that
`ncosentino/eve-client` still needs, without creating speculative or duplicate
issues.

The radar compares the exact compatibility baseline declared by the C# client
to current `vercel/eve` main. It does not depend on a lookback window, so a
failed or skipped scheduled run cannot lose changes. Until the C# baseline
advances, every unresolved upstream change remains in the next run's delta.

## Invocation

```text
eve-client-upstream-radar
  --target-repo-path <path>  (optional; defaults to the repository containing this skill)
  --inventory-path <path>    (optional pre-collected inventory from the deterministic preflight)
  --max-issues <N>           (default: 8)
  --dry-run                  (default for interactive runs)
  --auto-create              (required for unattended GitHub writes)
  --skip-fetch               (supervised diagnostics only)
```

Exactly one of `--dry-run` or `--auto-create` is effective. Interactive runs
default to `--dry-run`. A scheduled prompt must explicitly pass
`--auto-create`. Reject `--max-issues` values outside `1..8`; eight is a hard
safety ceiling, not only a default. Reject `--skip-fetch` with
`--auto-create`: stale refs may be inspected only in dry-run mode.

## Invariants

- Read `reference/scope-map.md` and `reference/issue-template.md` before
  collecting.
- Inspect the target's fetched `origin/main`, never its working-tree files.
- Use the radar-owned upstream cache returned by the collector; never add an
  upstream remote to the user's eve-client clone.
- Every proposed issue requires source-level parity evidence against committed
  eve-client main.
- Never file an "investigate whether this applies" issue. Insufficient evidence
  is a drop with a report reason.
- Never edit, commit, push, merge, release, close, or delete anything in either
  repository.
- Never advance the eve compatibility version or fixture.
- Stop GitHub writes on the first failure and report completed versus pending
  actions.

## Phase 0 - Preflight and exact delta

1. Verify `gh auth status` succeeds.
2. Verify the target path exists and its configured remote identifies
   `ncosentino/eve-client`.
3. When `--inventory-path` is supplied, read that file, verify
   `SchemaVersion=2`, and use its immutable target/upstream commits. Do not
   recollect or create a second run directory.
4. Otherwise run the deterministic preflight:

   ```powershell
   $preflight = & (Join-Path $skillDir 'scripts\Invoke-EveUpstreamRadarPreflight.ps1') `
       -TargetRepoPath <target-repo-path> `
       -SkipFetch:$skipFetch
   ```

   The preflight creates the durable run directory and inventory through the
   collector. The collector:

   - Fetches only the target's remote `main` ref; it does not inspect or alter
     working-tree files.
   - Verifies `EveProtocol.ReferenceEveVersion`, README's exact upstream commit,
     and the npm Eve fixture all agree.
   - Resolves the matching `eve@<version>` tag.
   - Updates a radar-owned partial clone of `vercel/eve`.
   - Enumerates every commit from that baseline through upstream main.
   - Resolves each commit's `ReleasedInVersion`: the lowest published
     `eve@<semver>` tag containing it, or `null` when it exists only on
     upstream main.
   - Marks direct path candidates and provides subsystem hints.

5. If preflight collection fails, stop without creating issues.
6. If `preflight.Status` is `NoCandidates` or `NoUntrackedCandidates`, print
   its inventory/report paths and counts, then finish immediately. The
   deterministic script has already written the complete report; do not read
   the full skill, fetch PR bodies, inspect diffs, or rewrite the report.
7. If `preflight.Status` is `AnalysisRequired`, continue with
   `preflight.InventoryPath`.
8. Path filtering is only the first signal for remaining work. Review every
   `CandidateByPath=true` commit; do not create issues directly from the
   collector output.

## Phase 1 - Resolve merged pull requests

For each path candidate:

1. Use `PullRequestNumber` when present and fetch authoritative metadata:

   ```powershell
   gh pr view <number> --repo vercel/eve `
     --json number,title,body,author,mergedAt,mergeCommit,files,labels,url
   ```

2. For a direct commit without a parsed PR number, query associated pull
   requests:

   ```powershell
   gh api repos/vercel/eve/commits/<sha>/pulls
   ```

   If none exists, retain the commit as its own source item.

3. Read the relevant diff from `inventory.Upstream.CachePath`:

   ```powershell
   git -C <cache-path> show --stat --format=fuller <sha>
   git -C <cache-path> show --format= -- <relevant paths>
   ```

4. Cluster by merged pull request. Split one PR only when it contains multiple
   independently assignable framework-neutral behaviors. Give every split a
   stable kebab-case topic slug.

5. Classify each cluster:

   - `bug-fix`
   - `feature`
   - `behavior-change`
   - `reliability-or-performance`
   - `type-or-schema`
   - `out-of-scope`

Drop docs-only, build-only, formatting, cleanup, server-internal, and
TypeScript-only UI/store/reducer work unless the diff proves a client-visible
contract change.

## Phase 2 - Mandatory .NET parity research

Parity-check every non-dropped cluster. This is the radar's value gate.

When there are independent subsystems, dispatch focused `explore` agents in
parallel. Batch related files into one agent rather than launching one agent
per file. Every prompt must include:

- The complete upstream PR/commit evidence and relevant diff summary.
- The upstream cache path and exact source commit.
- The target repository path and exact immutable
  `inventory.Target.Commit`.
- The likely .NET areas from `reference/scope-map.md`.
- Precise yes/no questions about whether the behavior already exists.
- A requirement for committed-ref file and line citations.

Scheduled orchestration may run on a lightweight model to keep zero-candidate
daily checks inexpensive. When a path candidate exists, use a synchronous
high-capability research agent for each independent subsystem. In GitHub
Copilot CLI, prefer a `general-purpose` agent using `gpt-5.6-sol` with high
reasoning effort when that model is available; otherwise use the strongest
available read-only research agent. The lightweight orchestrator must not
substitute its own shallow parity guess for this escalation.

Agents must inspect git objects, not the target working tree:

```powershell
git -C <target-path> grep -n <pattern> <target-commit> -- <paths>
git -C <target-path> show <target-commit>:<path>
```

For each cluster, assign exactly one parity result:

- `gap-confirmed` - externally observable behavior is missing or materially
  different. Eligible for dedup and issue creation.
- `already-present` - the C# implementation or tests already provide parity.
- `out-of-scope` - no meaningful framework-neutral .NET equivalent.
- `insufficient-evidence` - the upstream intent or target parity cannot be
  established confidently. Do not file.

Special cases:

- Raw `JsonElement` preservation usually covers preview-only agent-info and
  event-field additions. Confirm before filing a stronger projection request.
- An upstream refactor is not a .NET issue unless it fixes observable behavior
  or materially changes reliability.
- A TypeScript API convenience needs an idiomatic .NET use case; mechanical
  one-to-one API translation is not evidence.
- If an open eve-client PR already implements the change, classify it as
  tracked and do not create another issue.

Do not proceed until every candidate has a parity result.

## Phase 3 - Stable deduplication

For each `gap-confirmed` cluster, form one immutable source identity:

```text
eve-client-upstream:pr:<number>
eve-client-upstream:pr:<number>:<topic-slug>   # split PR only
eve-client-upstream:commit:<full-sha>          # direct commit only
```

Compute:

```text
fingerprint = first 12 lowercase hex characters of SHA-256(source identity)
label = eve-fp:<fingerprint>
```

Search all target issues and pull requests for:

1. The exact fingerprint label or metadata marker.
2. The exact upstream PR/commit URL. For an unsplit PR this is conclusive. For
   a split PR, the shared URL is only supporting evidence; require the
   topic-specific fingerprint or a clearly equivalent topic match.
3. A clearly equivalent title/topic when older manually filed work lacks the
   marker.

Decision:

| Match | Action |
|---|---|
| Open issue | Skip as already tracked |
| Open pull request | Skip as in progress |
| Closed issue, any reason | Skip forever for this immutable upstream change |
| Merged pull request / parity already present | Skip as implemented |
| No match | Eligible to create |

Never re-file the same upstream PR because an earlier issue was closed. A later
upstream fix has a new PR or commit identity and therefore a new fingerprint.

## Phase 4 - Rank and cap

Process every candidate through scope, parity, and dedup before applying the
cap. `--max-issues` limits only new issue creation.

Rank eligible issues:

1. Security, data loss, protocol breakage.
2. Correctness, cancellation, durable state, and stream reliability.
3. Public client capability or route support.
4. Type/schema improvements and client ergonomics.
5. Non-contract performance improvements.

If eligible issues exceed the cap, leave the lower-ranked items uncreated and
record them in the report. Because the scan is baseline-based, they remain
eligible on the next run.

## Phase 5 - Approval mode

- `--dry-run`: write the complete proposed action plan and create nothing.
- `--auto-create`: skip the human gate and create up to `--max-issues`.

Interactive invocation without `--auto-create` never writes to GitHub.

## Phase 6 - Create and verify issues

Before the first create, ensure these labels exist:

- `source:auto-radar` - create if absent.
- `eve-fp:<hash>` - create for the candidate if absent.

Always apply these existing target labels:

- `dependencies`
- `.NET`

Also apply the type label:

- `bug` for upstream bug fixes.
- `enhancement` for features and intentional behavior changes.

Do not invent priority or status labels that the repository does not use.

Build the body from `reference/issue-template.md`. It must contain:

- Upstream PR/commit links and relevant TypeScript paths.
- The published eve release containing the change, taken from the inventory's
  `ReleasedInVersion`, or an explicit statement that it is on upstream main
  only. Never guess this by reading version numbers out of changelogs.
- Original-prose before/after behavior.
- Concrete .NET parity evidence from committed main.
- Agent-ready acceptance criteria.
- At least two plausible implementation approaches with pros and cons when
  there is a meaningful design choice.
- Related eve-client issues or pull requests.
- The hidden fingerprint metadata marker.

Create with a body file:

```powershell
gh issue create --repo ncosentino/eve-client `
  --title "<title>" `
  --body-file <body-path> `
  --label "<comma-separated labels>"
```

After each create, verify the issue body and labels with `gh issue view`. Stop
the batch on the first failed create or verification.

## Phase 7 - Run report

Write `<run-directory>\eve-client-upstream-radar.md` with:

- Target main commit and declared eve baseline.
- Upstream main commit and total delta size.
- How many path candidates are not yet in a published eve release.
- Every source cluster, classification, parity result, and evidence summary.
- Dedup matches.
- Created issue URLs.
- Cap-deferred candidates.
- Dropped candidates and explicit reasons.
- Errors and partial-write status.

Count deterministic path-gated commits as `out of scope` in the summary so the
headline counts reconcile with the complete upstream delta.

Zero confirmed gaps is a healthy result. Still write the report.

Print the inventory and report paths plus counts for:

- upstream commits
- path candidates
- confirmed gaps
- already present
- out of scope
- insufficient evidence
- created
- deduplicated
- cap deferred

## Scheduled prompt

```text
From the eve-client repository root, run the repo-local
.github\skills\eve-client-upstream-radar\scripts\
Invoke-EveUpstreamRadarPreflight.ps1 script. If its status is NoCandidates or
NoUntrackedCandidates, print its artifact paths and counts, then stop without
invoking the full skill. If its status is AnalysisRequired, invoke the
repo-local eve-client-upstream-radar skill with the returned inventory path,
--auto-create, and --max-issues 8. Perform mandatory source-level parity
research against the inventory's immutable target commit, deduplicate every
confirmed gap, and stop GitHub writes on the first failure. Never inspect
working-tree files, modify repositories, commit, push, merge, release, close
issues, or advance the compatibility baseline.
```
