---
description: Understand supported eve versions, stream protocol compatibility, and preview-version policy.
---

# Compatibility

| NexusLabs.Eve | Reference eve | Stream protocol | Status |
|---|---:|---:|---|
| Unreleased | 0.45.1 | 23 | Development compatibility target |
| 0.1.0-alpha.9 | 0.45.0 | 23 | Current prerelease |
| 0.1.0-alpha.8 | 0.44.4 | 23 | Previous compatibility target |
| 0.1.0-alpha.7 | 0.44.0 | 23 | Previous compatibility target |
| 0.1.0-alpha.6 | 0.35.0 | 22 | Previous compatibility target |
| 0.1.0-alpha.5 | 0.34.0 | 21 | Previous compatibility target |
| 0.1.0-alpha.4 | 0.32.0 | 21 | Earlier compatibility target |
| 0.1.0-alpha.4+ | 0.31.0 | 21 | Minimum supported release |
| 0.1.0-alpha.3 | 0.29.4 | 20 | Final release for eve 0.29.x-0.30.x |
| 0.1.0-alpha.3 | 0.27.6 | 19 | Tolerated by that release, not gated by CI |

## Minimum supported eve release

**This package requires eve `0.31.0` or newer and cannot talk to an earlier server.**
`EveProtocol.MinimumEveVersion` declares the line in code.

eve `0.31.0` moved session control operations from fixed continuation-token body routes to
identifier-addressed routes:

| Operation | eve 0.30.x and earlier | eve 0.31.0 and newer |
|---|---|---|
| clear | `POST /eve/v1/session/clear` | `POST /eve/v1/session/{sessionId}/clear` |
| compact | `POST /eve/v1/session/compact` | `POST /eve/v1/session/{sessionId}/compact` |
| reset | `POST /eve/v1/session/reset` | `POST /eve/v1/session/{sessionId}/reset` |

The identifier-addressed routes return HTTP 404 on an older server, and continuation tokens are
no longer accepted or returned anywhere in the client protocol. There is no negotiation or
fallback: a protocol cutover has no half-migrated state. Pin `0.1.0-alpha.3` to target an eve
`0.29.x` or `0.30.x` agent.

Session creation, follow-up turns, streaming, and cancellation route identically on both sides
of the boundary, so only the three control operations moved. The turn body still differs: an
eve `0.30.x` or earlier server requires `continuationToken` to continue a session, and an eve
`0.31.x` server rejects that field. A mismatched pair therefore completes the first turn and
fails the second. See [Migration](migration.md) for the observed behavior in both directions
and the required order of operations.

`ClearAsync` and `CompactAsync` require eve `0.30.0` or newer even in `0.1.0-alpha.3`; those
two routes did not exist in eve `0.29.x`.

eve remains preview software. Package upgrades should therefore validate both:

1. The public HTTP route and body contracts.
2. The durable message-stream protocol version and event shapes.

The repository contains a pinned eve `0.45.1` fixture with a deterministic
model. CI builds the real server and verifies health, info, text turns,
attachment staging, streaming, bounded catch-up reads, cooperative cancellation,
approval-gated human input, callback-backed connection authorization, session context
clear, and session reset through the C# client, including the HTTP 409 refusal returned
when a retired session identifier is reused.

Event parsing stays tolerant of older stream protocols: durable event
identifiers and input-request discriminators are both projected as absent
rather than causing a failure. That tolerance is covered by contract tests, not
by the pinned fixture, and it does not extend the supported server range, which
the identifier-addressed control routes fix at eve `0.31.0` and newer.

## Preliminary tool output

Stream protocol `21` adds `action.partial`, emitted for each non-terminal snapshot
yielded by an async-generator tool. `EveStreamEventKind.ActionPartial` recognizes it, and
the terminal `action.result` continues to carry the value exposed to the model. Treat
partial snapshots as provisional display state and never as a final tool result.

## Run trace context

Stream protocol `22` adds an optional `trace` object to `session.started` and
`turn.started`, carrying the W3C `traceId`, `spanId`, and `traceFlags` for correlating a
run with an external observability backend. It added no event type and removed none.
The field is available through `EveStreamEvent.Data` rather than projected, so an
unrecognized future field on the same object stays readable. An agent older than eve
`0.35.0` omits it entirely.

## Eve 0.37.1 and 0.38.x

The framework-neutral session routes used by this package, agent-info schema version `2`,
and message-stream protocol version `22` remain unchanged through eve `0.38.3`.

eve `0.37.1` changed active response lifetime and added metadata to existing
subagent and authorization events. Active `SendAsync` and `RespondAsync` responses now
reconnect until a turn boundary or caller cancellation, while raw event data preserves
the child-stream path plus optional background-task receipt and authorization attempt ID.

eve `0.38.0` added response-scoped exact-turn cancellation to the TypeScript client.
`EveMessageResponse.CancelAsync` provides the same coordination over the existing guarded
cancel route. eve `0.38.1` through `0.38.3` add no further framework-neutral client
requirement.

## Resolved human input

Stream protocol `23`, introduced by eve `0.39.1`, adds `input.resolved` after the
server accepts a pending human-input batch and before the resumed `step.started`.
`EveStreamEventKind.InputResolved` recognizes the event, and
`EveTurnOutcome.InputResolutions` projects every request kind, terminal outcome,
optional accepted response, and original `turnId`, `stepIndex`, and `sequence`.

The known outcomes are `Answered`, `Approved`, `Denied`, `Ignored`, and `Invalid`.
Future values remain available through `RawOutcome`, while the complete resolution
object remains available through `Raw`. A resolution without a response is authoritative
and is not dropped.

## Open stream read-idle recovery

Eve `0.44.1` includes the framework-neutral stream reliability change merged in
[vercel/eve#2379](https://github.com/vercel/eve/pull/2379). Every open stream read has a
fixed 15-second idle deadline; a socket that remains connected without producing bytes
is closed and reopened from the absolute cursor after every fully consumed event.

This behavior changes no route, payload, event shape, stream protocol version, or
agent-info schema.

## Callback-backed connection authorization

eve `0.41.0` can emit an interim `session.waiting` after
`authorization.required` while a framework-owned callback is pending. Active
`SendAsync` and `RespondAsync` responses remain attached across that parking boundary,
correlate pending authorizations by `data.name`, and settle at the next session boundary
after matching `authorization.completed` events clear every pending name.

An `authorization.required` event without `webhookUrl` remains non-blocking, so the next
`session.waiting` settles normally. The stream protocol remains `23`, the agent-info
schema remains version `2`, and the core session routes are unchanged. See
[Streaming](streaming.md#callback-authorization-parking) for consumption guidance.

## Exact channel input responses

Eve `0.42.0` rejects channel input-response objects containing fields outside the exact
text, choice, confirmation, or tool-approval response contract. The sealed
`EveInputResponse` model and whitelist request writer already emit only the permitted
keys, so this upstream tightening requires no .NET request-shape change.

## Eve 0.43.0 through 0.44.4

The core session routes, stream event vocabulary, message-stream protocol `23`, and
agent-info schema version `2` remain unchanged through eve `0.44.4`. Eve `0.44.1`
contains the open-read recovery described above. Eve `0.44.4` fixes replay-to-live
continuation in the excluded JavaScript store; the .NET session cursor and stream
follower already expose the underlying bounded replay and active-follow behavior.

## Eve 0.45.0 and agent-info schema v3

Eve `0.45.0` raises agent inspection to schema version `3`. `GetInfoAsync` accepts the
canonical v3 source graph while retaining schema versions `1` and `2`, and continues to
expose every field through `EveAgentInfo.Raw`.

Version `3` is validated as a distinct contract rather than accepted by version number
alone. The client rejects relabeled v2 documents, missing canonical collections,
duplicate public identities, normalized channel-route collisions, incorrect subagent or
remote-agent totals, module sources without bindings, and bindings whose owner or logical
path disagrees with their source.

The pinned Eve `0.45.1` fixture exercises this schema through the real compatibility
probe.

## Eve 0.45.1 and agent-info schema v4

Eve `0.45.1` raises agent inspection again to schema version `4`.
`GetInfoAsync` accepts and strictly validates the required memory-provider inspection
surface while retaining schema versions `1` through `3`.

Version `4` requires a `memories` collection with unique slots, canonical source
provenance, and `scope` or `session` visibility. It also adds memory counts to local
subagent summaries, optional dependency and parameter maps to programmatic source
backings, and a required `direct` or `derived` form on source descriptors. The published
schema rejects the pre-release memory `tools` field. Every valid field remains available
through `EveAgentInfo.Raw`.

The pinned Eve `0.45.1` fixture exercises schema v4 through the real compatibility probe.

## Strict health response validation

Eve `0.45.0` strictly validates successful `GET /eve/v1/health` responses. The exact
shape is `ok: true`, `status: "ready"`, and a nonempty string `workflowId`; unknown
properties are rejected. A whitespace-only workflow identifier remains nonempty and is
therefore valid.

`GetHealthAsync` reports successful-response validation failures through
`EveHealthResponseException`. Its bounded `Issues` collection exposes at most five
path-qualified diagnostics without requiring callers to parse an exception message.
Invalid JSON preserves the parser failure as the inner exception and reports no
structured issues. Non-success HTTP responses continue to use `EveClientException`.

The pinned Eve `0.45.1` fixture exercises this strict health response through the real
compatibility probe.

Upstream eve lets generic per-request headers replace authentication.
NexusLabs.Eve requires an explicit client allowlist and dedicated per-call override
for protected headers so existing generic header bags cannot silently replace credentials.

Unknown event types remain available through `EveStreamEvent.Type` and `Data`
instead of causing deserialization failure.

## Stream event identity

Stream protocol version 20 stamps every persisted event with a stable
`evt_`-prefixed identifier. `EveStreamEvent.Metadata.Id` projects it when
present and reports `null` for events persisted under earlier protocol
versions, which cannot be deduplicated. The compatibility probe asserts that
the pinned server stamps a well-formed identifier on every event of a turn and
never repeats one.

## Upstream parity radar

The repo-local `eve-client-upstream-radar` Copilot skill under
`.github/skills/` compares the declared eve release baseline with current
`vercel/eve` main. It filters to framework-neutral client and protocol changes,
checks committed `origin/main` source for an existing equivalent, and can file
deduplicated, agent-ready issues for confirmed gaps.

The skill resolves this repository from its own location, while generated
inventories and reports live under the current user's local application-data
folder. Machine-specific checkout paths and Narnia cadence configuration are
deliberately not committed.

Bounded catch-up reads (`EveStreamOptions.Follow = false`) depend on the
`includeTailIndex=1` stream query parameter and the `x-eve-stream-tail-index`
response header. The pinned server reports the header, so the compatibility
probe verifies a real bounded read: the first request asks for the tail,
reconnects never re-request it, and the read stops exactly at the durable bound
while advancing the stored cursor. A server that omits the header, or reports a
malformed or out-of-range value, fails with `EveProtocolException` instead of
silently degrading to a live follow. eve `0.27.6` accepted the query parameter
without reporting the header, so bounded reads against that release fail.

## Input request kinds

eve stamps each human-input request with a framework-owned `kind` of `question`,
`tool-approval`, or `session-limit`. `EveInputRequest.Kind` projects it and
`EveInputRequest.RawKind` preserves the wire value, so an unmodelled future kind
stays inspectable instead of being misclassified from its option shape.

The compatibility probe drives a real approval-gated tool against the pinned
fixture, asserts the request arrives as `tool-approval`, answers it, and
verifies the turn resumes. A server that predates the discriminator reports
`EveInputRequestKind.Unknown` with a `null` raw value.
