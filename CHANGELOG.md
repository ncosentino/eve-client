# Changelog

All notable changes to NexusLabs.Eve will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `EveSession.ResetAsync` for terminally retiring a durable session, with
  `EveResetStatus`, `EveResetOutcome`, and `EveRequestKind.ResetSession`.

### Changed

- The compatibility reference moved to eve `0.27.6` (message-stream protocol `19`).
- Accepted session IDs and continuation tokens are persisted in `EveSession.State`
  as soon as `SendAsync` returns, before the response stream is consumed.
- **Breaking:** Per-request headers now override configured authentication headers, matching
  `eve@0.27.6`. Header precedence is now static client headers, `HeadersProvider`,
  `RequestHeadersProvider`, `Authentication`, then per-request headers. `EveSendTurnRequest.Headers`
  and raw `SendRawAsync` request and content headers can therefore replace configured
  authentication values, including `Authorization`, on the turn POST and every stream reconnect.
  `RequestHeadersProvider` remains client-level and is still overridden by authentication.
  Overriding `Authorization` does not remove `x-vercel-trusted-oidc-idp-token` from
  `EveVercelOidcAuthentication`. Audit per-turn and raw headers for names owned by the configured
  `IEveAuthentication` before upgrading; see `docs/authentication.md` for migration guidance.

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

[Unreleased]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.2...HEAD
[0.1.0-alpha.2]: https://github.com/ncosentino/eve-client/compare/v0.1.0-alpha.1...v0.1.0-alpha.2
[0.1.0-alpha.1]: https://github.com/ncosentino/eve-client/releases/tag/v0.1.0-alpha.1
