# Changelog

All notable changes to NexusLabs.Eve will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Compatibility

- **Supported eve versions:** `0.31.0` through `0.45.x`, message-stream protocol `23`.
  The compatibility target and pinned fixture move to eve `0.45.1`; the minimum remains
  eve `0.31.0`.
- Eve `0.45.1` publishes agent-info schema version `4`; the real compatibility probe now
  verifies its memory inspection contract.

### Fixed

- Schema-v4 agent inspection rejects the obsolete memory `tools` property, matching the
  strict contract first published by Eve `0.45.1`.

## [0.1.0-alpha.9] - 2026-08-26

### Compatibility

- **Supported eve versions:** `0.31.0` through `0.45.x`, message-stream protocol `23`.
  The compatibility target and pinned fixture move to eve `0.45.0`; the minimum remains
  eve `0.31.0`.
- Eve `0.45.0` publishes agent-info schema version `3` and strict successful
  health-response validation. The real compatibility probe now verifies both contracts.
- Agent-info schema version `4` remains ahead of the published baseline because its
  upstream change has not been released.

### Added

- `GetInfoAsync` accepts and strictly validates agent-info schema version `4`, including
  first-class memory-provider entries, unique memory slots, subagent memory counts,
  versioned programmatic-backing metadata, and direct/derived source descriptors. Valid
  v4 fields remain available through `EveAgentInfo.Raw`, while schema v3 keeps its
  original strict shape.

### Fixed

- Agent inspection accepts opaque tool `inputSchema` values, including Boolean JSON
  schemas, instead of incorrectly requiring every schema to be an object.

## [0.1.0-alpha.8] - 2026-08-25

### Compatibility

- **Supported eve versions:** `0.31.0` through `0.44.x`, message-stream protocol `23`.
  The compatibility target and pinned fixture move to eve `0.44.4`; the minimum remains
  eve `0.31.0`.
- Eve `0.44.1` includes open-read idle recovery, and eve `0.44.4` fixes replay-to-live
  continuation in the JavaScript store. The .NET client already implements the
  framework-neutral behavior; core routes, stream protocol `23`, and the published
  agent-info schema version `2` remain unchanged.
- Agent-info schema version `3` and strict successful health-response validation remain
  ahead of the published baseline because their upstream change has not been released.

### Added

- `GetInfoAsync` accepts canonical agent-info schema version `3` while preserving versions
  `1` and `2`. Version `3` validates required source-graph structure, unique public
  identities, normalized route collisions, collection totals, module bindings, and
  binding owner/path provenance while retaining every inspection field through
  `EveAgentInfo.Raw`.
- `GetHealthAsync` strictly validates successful health responses and throws
  `EveHealthResponseException` with at most five structured issues for invalid JSON
  shapes, literals, missing fields, and unknown properties. Non-success responses remain
  `EveClientException`, and nonempty workflow IDs retain upstream whitespace semantics.

## [0.1.0-alpha.7] - 2026-08-21

### Compatibility

- **Supported eve versions:** `0.31.0` through `0.44.x`, message-stream protocol `23`.
  The compatibility target and pinned fixture move to eve `0.44.0`; the minimum remains
  eve `0.31.0`.
- eve `0.39.1` adds durable `input.resolved` events and raises the stream protocol from
  `22` to `23`. Core session routes and agent-info schema version `2` remain compatible.
- eve `0.41.0` keeps stream protocol `23` and agent-info schema version `2`, and active
  responses now continue across interim waiting boundaries while callback-backed
  connection authorization is pending.
- eve `0.42.0` requires exact channel input-response objects. The sealed
  `EveInputResponse` model and whitelist request writer already satisfy that contract;
  core session routes remain unchanged.
- eve `0.43.0` and `0.44.0` add no further framework-neutral client requirement.
  Message-stream protocol `23`, agent-info schema version `2`, and core session routes
  remain unchanged.

### Added

- `EveStreamEventKind.InputResolved` recognizes protocol-v23 human-input resolution
  events. `EveTurnOutcome.InputResolutions` projects every authoritative outcome,
  including response-less resolutions, while preserving raw discriminators, accepted
  responses, and original turn coordinates.
- `EveMessageResponse.CancelAsync` waits for its response stream to identify the exact
  turn, sends a guarded cancellation request, shares concurrent calls while preserving
  per-caller wait cancellation, and allows retry after a failed request while stream
  consumption continues through settlement.

### Fixed

- Active `SendAsync` and `RespondAsync` responses keep reconnecting until the current turn
  reaches a session boundary or the caller cancels consumption. Manually attached
  `StreamAsync` reads and explicitly configured idle-attempt limits retain their finite
  budgets.
- Open stream connections that stop producing bytes are closed after 15 seconds and
  reconnected from the absolute cursor after every fully consumed event. Explicit caller
  cancellation and disabled reconnection remain terminal.
- Active responses do not settle on an interim `session.waiting` while one or more
  callback-backed connection authorizations remain pending. Matching
  `authorization.completed` events clear the pending names, and the next session boundary
  settles the response normally.

## [0.1.0-alpha.6] - 2026-08-13

### Compatibility

- **Supported eve versions:** `0.31.0` through `0.35.x`, message-stream protocol `22`. The
  compatibility target moved to eve `0.35.0`; `EveProtocol.MinimumEveVersion` still
  declares `0.31.0` as the floor.
- **eve `0.35.0` breaks agent inspection on earlier releases of this package.** It returns
  agent-info schema version `2`, which `0.1.0-alpha.5` and earlier reject, so `GetInfoAsync`
  throws. Sessions, streaming, and control operations are unaffected. Use `0.1.0-alpha.6`
  or newer against a `0.35.0` agent.
- **eve `0.35.0` is otherwise additive.** Session routes are unchanged and the stream event
  vocabulary added and removed nothing; the protocol version moved to `22` for the new
  optional run trace context.

### Fixed

- Agent inspection accepts the eve `0.35.0` agent-info payload. eve raised the schema to
  version `2`, where static instructions became a list whose entries carry `content` and a
  `system` or `user` role, and this package rejected any version but `1`. `GetInfoAsync`
  therefore threw against every `0.35.0` agent before `Raw` became reachable. Sessions,
  streaming, and control operations were unaffected.

### Added

- `EveProtocol.SupportedAgentInfoVersions` lists the agent-info schema versions this
  package understands. A version outside the set is still rejected rather than parsed
  optimistically.

### Changed

- `EveProtocol.MessageStreamVersion` moves to `22`, and the compatibility reference and
  pinned fixture move to eve `0.35.0`. The value mirrors upstream's
  `EVE_MESSAGE_STREAM_VERSION`; eve raised it for the new optional run trace context on
  `session.started` and `turn.started`, which stays available through
  `EveStreamEvent.Data` rather than being projected.

## [0.1.0-alpha.5] - 2026-08-13

### Compatibility

- **Supported eve versions:** `0.31.0` through `0.34.x`, message-stream protocol `21`. The
  compatibility target moved to eve `0.34.0`; `EveProtocol.MinimumEveVersion` still
  declares `0.31.0` as the floor.
- **Nothing between eve `0.32.0` and `0.34.0` breaks this package.** Every session route is
  unchanged across that range and the stream event vocabulary only grew, adding
  `approval.candidate` and `approval.settled` in `0.34.0` and removing nothing. Verified
  against a real eve `0.34.0` agent by the compatibility probe.
- **eve `0.33.0` changes the default meaning of an overlapping send.** A message that
  reaches a session with an active turn now cancels and replaces that turn instead of
  waiting for it. The request format did not change, so nothing fails loudly. Set
  `EveTurnOptions.TurnPolicy` to `EveTurnPolicy.Queue` to restore the earlier behavior.
  An agent older than `0.33.0` ignores the field and already queues, so the setting is
  safe on every supported version.

### Added

- `EveStreamEventKind.ApprovalCandidate` and `EveStreamEventKind.ApprovalSettled` recognize
  the `approval.candidate` and `approval.settled` events published by eve `0.34.0`, which
  were previously reported as `Unknown`. Lifecycle payloads, including candidate outcomes,
  optional reasons, and the optional `candidateId` correlation on authorization events, stay
  available through raw event data so future outcome values remain readable.
- `EveTurnOptions.TurnPolicy` selects how eve handles a message sent to a session that
  already has an active turn. `EveTurnPolicy.Queue` waits for the active turn and
  `EveTurnPolicy.Steer` replaces it. The field is serialized only for a message
  continuing an existing session, and is omitted when creating a session and when a turn
  carries only input responses, matching the upstream client.
- `EveAgentInfo.ModelRouting` and `EveAgentInfo.RawModelRouting` report how an agent
  resolves the model it calls. Agents older than eve `0.33.0` report no routing and
  therefore project `EveAgentModelRouting.Unknown`.

### Changed

- **Breaking:** `EveAgentInfo.ModelId` is now nullable. It is `null` exactly when
  `ModelRouting` is `EveAgentModelRouting.Dynamic`. eve `0.33.0` reports a dynamically
  selected model through `routing.kind` with no identifier instead of a placeholder, and
  agent inspection previously failed on such a payload before `Raw` became reachable.
  Consumers that read `ModelId` into a non-nullable `string` will see a nullable warning
  and should handle the dynamic case.
- The compatibility reference moved to eve `0.34.0`. The pinned CI fixture runs that
  release; its `ai` peer dependency requirement is satisfied by the existing `7.0.58` pin.

### Fixed

- Query strings embedded in a route path are preserved as URL search parameters instead of
  being percent-encoded into the path for an absolute host, or producing a second query
  delimiter for a relative host. Explicit parameters replace same-named embedded ones,
  matching eve `0.32.0`. No route this client constructs carries an embedded query, so the
  defect was unreachable through the public API.

## [0.1.0-alpha.4] - 2026-08-09

### Compatibility

- **Supported eve versions:** `0.31.0` and newer, message-stream protocol `21`. The
  compatibility target is eve `0.31.3` and `EveProtocol.MinimumEveVersion` declares the
  floor in code.
- **Not compatible with eve `0.30.x` or earlier.** eve `0.31.0` moved session control
  operations to identifier-addressed routes and removed continuation tokens from the
  client protocol. There is no negotiation or fallback. Pin `0.1.0-alpha.3` when targeting
  an eve `0.29.x` or `0.30.x` agent.
- This release contains breaking API changes in addition to the protocol cutover. Read the
  `Removed` and `Changed` sections before upgrading.

### Removed

- `EveSessionState.ContinuationToken`, `EveMessageResponse.ContinuationToken`,
  `EveClient.CreateSession(string continuationToken)`, and
  `EveClientOptions.PreserveCompletedSessions`. eve `0.31.0` removed continuation
  tokens from the client protocol; sessions are addressed only by their immutable
  identifier.
- `EveSendTurnRequest`, replaced by `EveTurnOptions`. The old type carried both `Message`
  and `InputResponses`, so it could express the combination eve `0.31.0` rejects with
  HTTP 400. The payload is now a required argument of `SendAsync` or `RespondAsync` and
  `EveTurnOptions` carries only the shared settings, making that combination impossible
  to compile rather than caught at runtime. This mirrors upstream's
  `send(message, options)` and `respond(inputResponses, options)` split.

### Added

- `EveSession.RespondAsync` for resolving pending human-input requests as an operation
  distinct from sending a message.
- `EveTurnOptions`, carrying the settings shared by both turn operations.
- `EveStreamEventKind.ActionPartial`, recognizing the `action.partial` events that stream
  protocol `21` emits for each non-terminal snapshot yielded by an async-generator tool. The
  terminal `action.result` still carries the value exposed to the model, and the full raw
  payload remains available through `EveStreamEvent.Data`.
- `EveClientException.ErrorCode`, projecting the stable machine-readable error code eve
  `0.31.0` reports alongside the human-readable message. The raw string is preserved so an
  unmodelled future code stays observable, and it is `null` when the response carried none.
  The compatibility probe asserts a real HTTP 409 reports `session_not_active`.
- `EveClient.AttachSession(string sessionId, int streamIndex = 0)` for obtaining a fixed
  handle to a known session without replaying its stream.
- `EveProtocol.MinimumEveVersion`, declaring the oldest supported eve release (`0.31.0`)
  in code.

### Changed

- **Breaking, requires eve `0.31.0` or newer.** Session control operations moved to
  identifier-addressed routes: `POST /eve/v1/session/{sessionId}/clear`,
  `/compact`, and `/reset` replace the fixed `/eve/v1/session/clear`, `/compact`, and
  `/reset` routes. The new routes do not exist before eve `0.31.0` and return HTTP 404
  there, so this release cannot talk to an eve `0.29.x` or `0.30.x` agent. The old fixed
  routes do not return 404 on an eve `0.31.x` server; `/eve/v1/session/clear` matches the
  continue route with a session identifier of `clear`, so the request is misrouted and
  fails with HTTP 400. Pin `0.1.0-alpha.3` for those agents. `SendAsync`, `StreamAsync`,
  and `CancelAsync` route unchanged. See [Migration](docs/migration.md).
- Session message and control request bodies no longer carry `continuationToken`. Clear,
  compact, and reset send an empty body.
- A session handle is now fixed. `session.completed` and `session.failed` retain the
  session identifier and advancing cursor instead of resetting local state, so a finished
  conversation stays streamable and inspectable. This replaces the opt-in
  `PreserveCompletedSessions` behavior, which upstream removed.
- `ResetAsync` no longer clears local state. The handle keeps its identifier; obtain a new
  conversation from `EveClient.CreateSession()`.
- A `session.waiting` event is no longer required to carry a continuation token.
- **Breaking.** `EveCancellationOutcome.SessionId` is nullable. eve `0.31.0` returns the
  identifier only for an accepted cancellation; a `no_active_turn` result names no session
  because none was cancelled. The client previously required an identifier before reading
  either status, so a valid inactive response failed to parse. Both variants are now
  validated strictly, matching upstream: an inactive result carrying an identifier, and an
  accepted result missing or mismatching one, are rejected as protocol errors.
- **Breaking.** A turn carries either a message or input responses, never both. eve
  `0.31.0` answers a combined body with HTTP 400
  (`'message' and 'inputResponses' are mutually exclusive`). `SendAsync` now takes the
  message as a required argument and `RespondAsync` takes the input responses, so the
  rejected combination cannot be expressed. Callers that delivered input responses
  through `SendAsync` must move to `RespondAsync`, and callers that built an
  `EveSendTurnRequest` must pass the payload positionally with an optional
  `EveTurnOptions`.
- The compatibility reference moved to eve `0.31.3`, message-stream protocol `21`. The pinned
  CI fixture runs that release, and the compatibility probe verifies the identifier-addressed
  control routes, empty control bodies, fixed-handle reset semantics, and the HTTP 409
  `session_not_active` refusal returned when a retired session identifier is reused.

## [0.1.0-alpha.3] - 2026-08-08

### Compatibility

- **Supported eve versions:** `0.29.4` through `0.30.x`, message-stream protocol `20`.
- **Not compatible with eve `0.31.0` and later.** eve `0.31.0` moved session control
  operations to identifier-addressed routes (`/eve/v1/session/{sessionId}/clear`,
  `/eve/v1/session/{sessionId}/compact`, `/eve/v1/session/{sessionId}/reset`) and
  removed continuation tokens from the client protocol. This release still posts to the
  fixed `/eve/v1/session/clear`, `/eve/v1/session/compact`, and `/eve/v1/session/reset`
  routes, which return HTTP 404 on an eve `0.31.x` server. `SendAsync`, `StreamAsync`,
  and `CancelAsync` are unaffected because their routes did not change.
- This is the final release before the breaking cutover to eve `0.31.x`. Pin this
  version when targeting an eve `0.29.x` or `0.30.x` agent.

### Added

- `EveSession.ClearAsync` for queueing durable model-message history clearing
  while preserving the session cursor, with `EveClearStatus`, `EveClearOutcome`,
  `EveRequestKind.ClearSession`, and `EveStreamEventKind.ContextCleared`. The
  route is contract-tested against the upstream protocol shape and is not yet
  part of the pinned eve `0.29.4` compatibility baseline.
- `EveSession.CompactAsync` for queueing manual context compaction without model
  input, with `EveCompactStatus`, `EveCompactOutcome`, and
  `EveRequestKind.CompactSession`. The operation preserves the local session
  cursor and tracks unreleased upstream eve main (not part of the `0.29.4`
  compatibility baseline).
- `EveSession.ResetAsync` for terminally retiring a durable session, with
  `EveResetStatus`, `EveResetOutcome`, and `EveRequestKind.ResetSession`.
- Explicit protected-header override channels for turns and raw requests, gated by
  `EveClientOptions.AllowedProtectedHeaderOverrides`.
- `EveStreamOptions.Follow` for bounded catch-up reads that stop at the durable stream tail
  observed when the stream opens, using the `includeTailIndex=1` query parameter and the
  `x-eve-stream-tail-index` response header.
- `EveStreamEventMetadata.Id` projecting the stable `evt_`-prefixed identifier that
  message-stream protocol `20` stamps on every persisted event, plus
  `EveStreamEventDeduplicator` for dropping re-delivered events across reconnects and
  rewinds. Events persisted under protocol `19` report `null` and are always admitted.
- `EveInputRequest.Kind` and `EveInputRequest.RawKind`, projecting eve's framework-owned
  input-request discriminator through the `EveInputRequestKind` enum so `question`,
  `tool-approval`, and `session-limit` requests are routed by contract instead of by
  option shape. A server that predates the discriminator reports `Unknown` with a `null`
  raw value.

### Changed

- The compatibility reference moved to eve `0.29.4` (message-stream protocol `20`). The
  pinned CI fixture runs that release, and the compatibility probe now verifies stamped
  event identifiers, a real bounded catch-up read against the durable tail header, and an
  approval-gated human-input pause end to end.
- Accepted session IDs and continuation tokens are persisted in `EveSession.State`
  as soon as `SendAsync` returns, before the response stream is consumed.
- Non-protected per-request headers now override client-level values, matching upstream eve.
  Authentication-owned and explicitly protected headers remain authoritative by default and
  require an allowlisted, dedicated per-call override.

## [0.1.0-alpha.2] - 2026-07-24

### Added

- Request-aware dynamic headers through `RequestHeadersProvider` and `EveRequestKind`.
- An opt-in `MaxStreamEventBytes` limit for individual NDJSON events.

### Fixed

- Request-only and content-only headers are applied only to compatible .NET header collections.

## [0.1.0-alpha.1] - 2026-07-23

### Added

- A dependency-free .NET 10 client for the Vercel eve HTTP protocol.
- Health and agent-info inspection with protocol validation.
- Bearer, HTTP Basic, and Vercel OIDC authentication providers.
- Durable session creation, continuation, persistence, cancellation, and manual stream attachment.
- NDJSON event streaming with cursor-based reconnection and configurable retry policies.
- Text, file, image, client-context, human-input, and structured-output payload support.
- Forward-compatible raw JSON access for preview agent-info and stream-event extensions.
- TUnit contract coverage derived from the Vercel TypeScript client.

[Unreleased]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.9...HEAD
[0.1.0-alpha.9]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.8...v0.1.0-alpha.9
[0.1.0-alpha.8]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.7...v0.1.0-alpha.8
[0.1.0-alpha.7]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.6...v0.1.0-alpha.7
[0.1.0-alpha.6]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.5...v0.1.0-alpha.6
[0.1.0-alpha.5]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.4...v0.1.0-alpha.5
[0.1.0-alpha.4]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.3...v0.1.0-alpha.4
[0.1.0-alpha.3]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.2...v0.1.0-alpha.3
[0.1.0-alpha.2]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.1...v0.1.0-alpha.2
[0.1.0-alpha.1]: https://github.com/ncosentino/eve-client/releases/tag/v0.1.0-alpha.1
