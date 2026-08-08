# Changelog

All notable changes to NexusLabs.Eve will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.3...HEAD
[0.1.0-alpha.3]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.2...v0.1.0-alpha.3
[0.1.0-alpha.2]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.1...v0.1.0-alpha.2
[0.1.0-alpha.1]: https://github.com/ncosentino/eve-client/releases/tag/v0.1.0-alpha.1
