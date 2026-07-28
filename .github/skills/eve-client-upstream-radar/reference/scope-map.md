# Eve client parity scope

`NexusLabs.Eve` mirrors the framework-neutral HTTP client exported by
`vercel/eve` while deliberately excluding JavaScript UI state management and
the Eve server/runtime implementation.

## Direct upstream signals

| Upstream TypeScript area | Primary .NET parity area |
|---|---|
| `client/client.ts`, `agent-host.ts`, `url.ts`, `client-error.ts`, `agent-info-*` | `EveClient`, `EveClientOptions`, `EveUrlBuilder`, `EveClientException`, `EveAgentInfo` |
| `client/session.ts`, `session-utils.ts`, `types.ts` | `EveSession`, `EveSessionState`, request models, options, outcomes |
| `client/message-response.ts`, `open-stream.ts`, `ndjson.ts` | `EveMessageResponse`, `EveStreamFollower`, `EveNdjsonLineReader`, reconnect policy |
| `client/file-parts.ts`, `authorization-message-parts.ts`, `message-action-parts.ts` | `EveMessageContent`, `EveContentPart`, input and authorization response models |
| `client/output-schema.ts` | `EveSendTurnRequest`, request serialization, `EveTurnOutcome` |
| `protocol/routes.ts`, `cancel-turn.ts`, `reset-session.ts` | `EveRoutes`, cancellation/reset request and response contracts |
| `protocol/message.ts` | `EveStreamEvent`, `EveStreamEventKind`, metadata, turn aggregation |
| `runtime/input/types.ts`, `channel/resolve-text.ts` | `EveInputRequest`, `EveInputOption`, `EveInputResponse` |

Upstream tests in these areas are evidence of externally observable behavior.
A test-only change may still reveal a .NET contract gap, but it is not enough
by itself: confirm that the asserted behavior applies to the HTTP client and
that the .NET implementation does not already cover it.

## Explicit exclusions

- `client/eve-agent-store.ts`
- `client/message-reducer.ts`
- `client/message-reducer-types.ts`
- `client/reducer.ts`
- React, Vue, Svelte, and other browser-framework bindings
- Eve agent authoring, compiler, runtime, sandbox, channel-hosting, CLI, and
  deployment internals

A pull request touching both excluded and included files is still reviewed,
but only the framework-neutral client behavior can become an eve-client issue.

## Classification rules

1. **Network behavior wins over file names.** A change is in scope when it
   changes a public route, body, header, stream, retry, cursor, or error
   contract consumed by the TypeScript client.
2. **TypeScript ergonomics are not automatically .NET requirements.** Promise,
   hook, store, reducer, and structural-typing conveniences need a meaningful
   .NET equivalent before they qualify.
3. **Raw JSON can already provide parity.** New preview-only agent-info or event
   fields are normally a parity win when `Raw` or `Data` preserves them. File an
   issue only when identity validation, control flow, or a promised strong
   projection also needs to change.
4. **Refactors without observable behavior are dropped.** Do not file cleanup,
   rename, formatting, build, docs-only, or internal performance work unless it
   changes the client contract or materially improves client reliability.
5. **Do not advance the compatibility baseline prematurely.** An implementation
   issue may port one upstream change, but `ReferenceEveVersion`, the fixture,
   README, and compatibility docs move only when every required change through
   the chosen upstream release is complete and tested.
