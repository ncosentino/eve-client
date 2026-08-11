---
description: Move an existing deployment from eve 0.29.x or 0.30.x to eve 0.31.x without breaking a running conversation.
---

# Migrating to eve 0.31.x

`NexusLabs.Eve` `0.1.0-alpha.4` requires eve `0.31.0` or newer. `0.1.0-alpha.3` is the
final release for eve `0.29.x` and `0.30.x`.

**The boundary is hard in both directions.** A mismatched client and server complete the
first turn and fail the second. Upgrading either side alone breaks the conversation, so
the client and the agent must move together.

## Why a single smoke test will not catch this

The client sends no protocol version. The only headers it sets are `authorization`,
`content-type`, and the Vercel OIDC token header; the only query parameters are
`startIndex`, `includeTailIndex`, `token`, and `bypass`. Nothing negotiates a version, so
a mismatch cannot be detected at connect time.

The first turn carries no continuation token in either direction, so it succeeds against
either server. The failure appears on the **second** turn. A health check, an info call,
or a one-message test will all pass against a server the client cannot actually talk to.

Always verify a **multi-turn** conversation.

## Route matrix

Taken from each release's shipped `dist/src/protocol/routes.js`.

| Operation | eve 0.29.x | eve 0.30.x | eve 0.31.x |
|---|---|---|---|
| create / continue / stream / cancel | unchanged | unchanged | unchanged |
| reset | `POST /eve/v1/session/reset` | `POST /eve/v1/session/reset` | `POST /eve/v1/session/{id}/reset` |
| clear | **not available** | `POST /eve/v1/session/clear` | `POST /eve/v1/session/{id}/clear` |
| compact | **not available** | `POST /eve/v1/session/compact` | `POST /eve/v1/session/{id}/compact` |

Session creation, follow-up turns, streaming, and cancellation use the same paths on every
release. Only the control operations moved.

!!! note "Clear and compact require eve 0.30.0"
    `0.1.0-alpha.3` exposes `ClearAsync` and `CompactAsync` even though it names eve
    `0.29.4` as its reference. Those two routes were introduced in eve `0.30.0`. Against an
    eve `0.29.x` agent they never worked, and they fail with HTTP 400 rather than 404
    because `/eve/v1/session/clear` matches the continue route with a session identifier of
    `clear`.

## Observed behavior across the boundary

Each row was executed against a real server of the named version.

| Client | Server | First turn | Second turn | Control operations |
|---|---|---|---|---|
| `alpha.4` | `0.31.x` | 202 accepted | 202 accepted | 202 / 200 |
| `alpha.4` | `0.29.x` | 202 accepted | **400** `Missing or empty 'continuationToken' field.` | **404** no route matching |
| `alpha.3` | `0.31.x` | 202 accepted | **400** `Session-ID routes do not accept 'continuationToken'.` | **400**, misrouted |
| `alpha.3` | `0.30.x` | supported | supported | supported |

### Adopting the new client while the agent stays on 0.29.x or 0.30.x

This breaks. `alpha.4` never sends a continuation token, and an eve `0.29.x` or `0.30.x`
server requires one to continue a session, so the second turn is rejected with HTTP 400.
The three identifier-addressed control routes also return HTTP 404 because they do not
exist before eve `0.31.0`.

### Upgrading the agent to 0.31.x while the application stays on the old client

This also breaks, and it is the more deceptive case.

1. The first turn is posted with no token and is accepted.
2. eve `0.31.x` still emits a `continuationToken` inside the `session.waiting` stream
   event, where it is now a channel-local value.
3. `alpha.3` harvests that value and stores it as session state.
4. The next turn includes it, and the server rejects the request with HTTP 400
   `Session-ID routes do not accept 'continuationToken'.`

The old fixed control routes do not return 404 on eve `0.31.x`. `/eve/v1/session/clear`
matches the continue route with a session identifier of `clear`, so the request is
misrouted and fails with a message about missing content rather than a missing route.

## Running several agent deployments on different versions

Mixed agent versions are fine. Serving them from one application is not.

- A .NET project can reference only one version of `NexusLabs.Eve`, so a single process
  carries a single client version.
- The client has no per-instance protocol switch. `EveClient` selects the host; the
  protocol is fixed when the package is compiled.

To promote one agent deployment while another stays behind, give each one its own
deployable pinned to the matching client version. Promote an instance only when every
application that talks to it is cut over at the same time.

## Order of operations

Because both directions break, this is a coordinated cutover for each deployable rather
than a rolling upgrade of one side.

1. **Inventory.** Record which applications talk to which agent deployments, and whether
   they call `ClearAsync`, `CompactAsync`, or `ResetAsync`.
2. **Pin explicitly.** Set `0.1.0-alpha.3` as an exact version so nothing floats forward
   before the agent is ready.
3. **Migrate the code on a branch.** This is compile-time work and is independent of
   deployment. See the table below.
4. **Stand up a new agent deployment on eve `0.31.x` beside the existing one.** Do not
   upgrade in place; the old client cannot talk to it.
5. **Deploy the `alpha.4` build against the new deployment only.**
6. **Verify a multi-turn conversation**, plus every control operation the application
   uses. A single message proves nothing.
7. **Move traffic, then retire the old deployment.**
8. **Remove the `alpha.3` pin** once no deployment runs eve `0.30.x` or earlier.

## Code changes required by 0.1.0-alpha.4

| Before | After |
|---|---|
| `SendAsync(new EveSendTurnRequest { Message = m })` | `SendAsync(m, options, cancellationToken)` |
| `SendAsync` carrying `InputResponses` | `RespondAsync(inputResponses, options, cancellationToken)` |
| `EveSendTurnRequest` for shared settings | `EveTurnOptions` |
| `EveClient.CreateSession(continuationToken)` | `EveClient.AttachSession(sessionId, streamIndex)` |
| `EveSessionState.ContinuationToken` | removed; sessions are addressed by identifier |
| `EveMessageResponse.ContinuationToken` | removed |
| `EveClientOptions.PreserveCompletedSessions` | removed; a completed session stays streamable |
| `EveCancellationOutcome.SessionId` non-null | nullable; a `no_active_turn` result names no session |
| `ResetAsync` clearing local state | the handle keeps its identifier; call `CreateSession` for a new conversation |

A turn now carries either a message or input responses and never both. eve `0.31.0`
rejects a combined body with HTTP 400, so the payload is a required argument and the
combination can no longer be expressed in code.

Reusing a retired session identifier returns HTTP 409 with the error code
`session_not_active`, available through `EveClientException.ErrorCode`.

## How these results were produced

The route matrix comes from the published `dist/src/protocol/routes.js` of eve `0.29.4`,
`0.30.0`, `0.31.0`, and `0.31.3`.

The behavior table comes from requests issued against real servers: the pinned eve
`0.31.3` fixture in `test/fixtures/eve-agent`, and an eve `0.29.4` agent built from the
same agent sources. Each cell records the status code and error body that server returned.

The eve `0.30.x` row is the supported baseline for `0.1.0-alpha.3` and is stated from the
route matrix rather than from an executed request.
