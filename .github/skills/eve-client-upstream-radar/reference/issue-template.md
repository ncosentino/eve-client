# Issue shape

Use this structure for every newly filed issue.

```markdown
> **Auto-filed by `eve-client-upstream-radar`.** This issue tracks a merged
> framework-neutral client change in `vercel/eve` that is not present on
> `ncosentino/eve-client` main.

## Summary

<What changed upstream, the current .NET gap, and why users care.>

## Upstream evidence

- **Pull request:** [vercel/eve#<number>](<url>) — `<title>`
- **Merge commit:** [`<short-sha>`](<commit-url>)
- **Released in:** eve `<version>` — or `not yet released (upstream main only)`
- **Compared from:** eve `<baseline-version>` (`<baseline-short-sha>`) to
  upstream main `<head-short-sha>`
- **Relevant TypeScript files:**
  - `<path>`

## Upstream behavior change

<Explain before/after behavior in original prose. Small pseudocode is fine;
do not paste a large upstream diff. Distinguish behavior from implementation
detail.>

## Current .NET parity gap

<Cite committed `ncosentino/eve-client` main files and line numbers, or list
the exact paths and symbols searched when the capability is absent. Explain
why raw JSON or an existing abstraction does not already provide parity.>

## Verification status

<Required. Separate what was read from source from what was inferred.>

- **Verified from committed source:** <what was read, and at which revisions>
- **Not executed:** <any behavior asserted but never run, and what would prove
  it; write "None" when everything asserted was executed>

<When the change alters a default, also state the resolved default before and
after, cite the terminating constant, and say whether a .NET caller can restore
the previous behavior today.>

## Acceptance criteria

- [ ] The externally observable upstream behavior is represented by an
      idiomatic .NET API or internal behavior.
- [ ] Contract tests cover the upstream before/after behavior.
- [ ] The real Eve compatibility probe is updated when the change affects the
      HTTP route, payload, or stream contract, and the change is present in a
      published eve release. An unreleased change is covered by contract tests
      until a release containing it exists.
- [ ] Public API additions have XML documentation covering intent, parameters,
      return values, exceptions, and constraints.
- [ ] User-facing compatibility documentation is updated when applicable.
- [ ] The compatibility baseline is not advanced unless all required changes
      through that upstream release are complete.

## Suggested implementation approaches

1. **<Approach A>** — <description>
   - Pros: <tradeoffs>
   - Cons: <tradeoffs>
2. **<Approach B>** — <description>
   - Pros: <tradeoffs>
   - Cons: <tradeoffs>

## Related eve-client work

- <Issue or PR links, or "None found">

---

<!-- eve-client-upstream-radar
fingerprint: eve-fp:<hash>
source: <pr-or-commit-identity>
target-commit: <target-main-sha>
upstream-head: <upstream-main-sha>
-->
```

## Title rules

Use `[Upstream eve] <concise behavior gap>`.

Good:

- `[Upstream eve] Retry transient failures while opening a stream`
- `[Upstream eve] Preserve continuation tokens emitted by waiting events`
- `[Upstream eve] Support disabling reconnects per send`

Avoid versions, dates, commit counts, and SHA hashes in titles. Those belong in
the body metadata.
