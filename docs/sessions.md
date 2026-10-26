---
description: Persist and resume eve session IDs and stream cursors.
---

# Sessions

An `EveSession` is a fixed handle. It tracks two values:

- `SessionId` identifies the durable runtime session. It addresses every turn,
  control, and stream, and never changes once assigned.
- `StreamIndex` counts events already consumed.

eve `0.31.0` removed continuation tokens from the client protocol. A handle
keeps its identifier across every session boundary, so a completed or failed
conversation stays inspectable and streamable.

## Persist state

Consume the response before saving the fully advanced cursor:

```csharp
EveMessageResponse response = await session.SendAsync(
    "Create a checklist.",
    cancellationToken);
await response.GetOutcomeAsync(cancellationToken);

await SaveAsync(session.State, cancellationToken);
```

## Resume state

```csharp
EveSession resumed = client.CreateSession(savedState);
EveMessageResponse response = await resumed.SendAsync(
    "Shorten the checklist.",
    cancellationToken);
```

When only the session identifier was persisted, attach to it directly:

```csharp
EveSession resumed = client.AttachSession(sessionId);
EveSession rewound = client.AttachSession(sessionId, streamIndex: 12);
```

## Prewarm a remote session

Eve `0.59.0` and newer can create a durable conversation before its first user
message:

```csharp
EveSession session = await client.PrewarmSessionAsync(cancellationToken);

await SaveAsync(session.State, cancellationToken);

EveMessageResponse response = await session.SendAsync(
    "Begin the conversation.",
    cancellationToken);
```

Unlike `CreateSession()`, `PrewarmSessionAsync` performs remote I/O. It sends a
message-free session request, returns the accepted session identifier at cursor
zero, and makes the first later send use the existing-session route. The
message-free request does not accept turn-scoped client context, output schemas,
or delivery policies; supply those options with the first `SendAsync` call.

The remote workflow can accept the prewarm request before its session inbox is
ready. If the first later message receives `409 session_not_ready`, `SendAsync`
re-resolves dynamic headers and retries for up to 20 seconds. Backoff starts at
250 milliseconds, doubles to a two-second cap, and stops immediately on
cancellation. `RespondAsync` does not use this readiness retry.

## Declarative tool stubs

Eve `0.75.0` and newer accept fixed tool stubs at session creation when the
server grants `allowToolStubs`. This optional capability does not change the
client's compatibility baseline. Stubs are declarations sent to the server:
the client never runs tools, advances the sequence, or simulates retries.

```csharp
using JsonDocument result = JsonDocument.Parse("""{"items":[]}""");
EveSessionCreationOptions creation = new()
{
    Stubs =
    [
        new EveToolStub("tasks", "researcher/list_tasks",
        [
            EveToolStubOutcome.Throw("Service unavailable"),
            EveToolStubOutcome.Respond(result.RootElement),
        ]),
    ],
};

EveSession session = client.CreateSession(creation);
EveMessageResponse response = await session.SendAsync("List tasks.", cancellationToken);

// Alternatively create the remote idle session before sending a message.
EveSession idle = await client.PrewarmSessionAsync(creation, cancellationToken);
```

Use the single-outcome constructor for a fixed response or thrown error. The
sequence constructor requires at least one outcome and preserves order. The
server decides consumption and recovery behavior. `Throw` accepts any non-null
message, including an empty string; its optional nonempty name is omitted by
default, so the server uses `"Error"`. `Respond` preserves any defined JSON
value, including null, arrays, scalars, and objects. Outcomes and match JSON are
snapshotted, so their originating `JsonDocument` can be disposed.

The optional `match` dictionary maps required input properties to JSON Schema
objects or booleans. The server validates supported schema keywords, nesting
and size limits. Rule identifiers must be nonempty and unique (at most 100
rules). Tool names may include a local-subagent path. Target the actual tool,
not the `eve__search`, `eve__tool`, or `eve__skill` catalog routers.

Null `Stubs` omits the field, preserving the body-free idle request and ordinary
tool execution. An empty list explicitly sends `"stubs":[]`. Only the initial
`POST /eve/v1/session` carries rules; subsequent messages, human-input responses,
and attached/resumed handles cannot replace them. Creation-option headers apply
to idle creation only; initial-message headers come from `EveTurnOptions`.
Missing permission and server-side rule errors surface as `EveClientException`.
`SendRawAsync` remains a caller-owned JSON escape hatch: its header options do
not merge or rewrite the caller's body, and server route restrictions still apply.

## Overlapping sends

A message that reaches a session which already has an active turn is governed by
a delivery policy. eve `0.33.0` changed the server-side default from waiting to
steering.

!!! warning "eve 0.33.0 steers by default"
    Sending no policy means eve `0.33.0` and later steer. Set `TurnPolicy` to
    `EveTurnPolicy.Queue` to keep the earlier wait-for-completion behavior.
    Nothing fails loudly, because the request format did not change.

Eve `0.59.1` changed steering from cancel-and-replace to an in-place update at
the next committed boundary. Steering accepted while a turn is active preserves
that turn's identifier and accumulated usage; the response can therefore begin
with `message.received` or another same-turn event rather than a new
`turn.started`. Steering accepted after the answer settles starts a normal
follow-up turn with a new identifier.

```csharp
EveMessageResponse response = await session.SendAsync(
    EveMessageContent.FromText("Actually, summarize it instead."),
    new EveTurnOptions { TurnPolicy = EveTurnPolicy.Queue },
    cancellationToken);
```

The policy is sent only for a message continuing an existing session. It is
omitted when the turn creates the session, because a new session has no active
turn, and when the turn carries only input responses. Awaiting each turn before
sending the next one never overlaps, so sequential callers are unaffected.

## Clear session context

!!! note "Control operations require identifier-addressed routes"
    `ClearAsync`, `CompactAsync`, and `ResetAsync` post to
    `/eve/v1/session/{sessionId}/clear`, `/compact`, and `/reset`, which exist only in eve
    `0.31.0` and newer. They report HTTP 404 on an earlier agent. Turns, streaming, and
    cancellation are unaffected. See [Migration](migration.md).

`ClearAsync` queues removal of durable model-message history while keeping the
session identity, configuration, non-message state, limits, and sandbox:

```csharp
EveClearOutcome clear = await session.ClearAsync(cancellationToken);

if (clear.Status == EveClearStatus.Accepted)
{
    await foreach (EveStreamEvent streamEvent in session.StreamAsync(cancellationToken))
    {
        if (streamEvent.Kind == EveStreamEventKind.ContextCleared)
        {
            // History was cleared on the durable stream.
        }

        if (streamEvent.Kind == EveStreamEventKind.SessionWaiting)
        {
            break;
        }
    }
}
```

- The session identifier is recorded as soon as `SendAsync` returns, so clear
  can run before the response stream is consumed.
- A session that never started returns `EveClearStatus.NoActiveSession` and
  issues no HTTP request.
- A successful clear leaves the local cursor unchanged. Consume the durable
  stream through `context.cleared` and the following `session.waiting` boundary
  before sending another turn.
- The route is `POST /eve/v1/session/{sessionId}/clear` and requires eve
  `0.31.0` or newer.

`ClearAsync` is not an alias for `ResetAsync`. `ResetAsync` retires the
conversation; `ClearAsync` keeps the same durable session and only discards
model-message history.

## Reset a session

`ResetAsync` terminally retires the durable session addressed by this handle:

```csharp
EveResetOutcome reset = await session.ResetAsync(cancellationToken);

if (reset.Status == EveResetStatus.Reset)
{
    EveSession next = client.CreateSession();
}
```

- A session that never started returns `EveResetStatus.NoActiveSession` and
  issues no HTTP request.
- The handle keeps its session identifier after a successful reset. It does not
  recycle into a new conversation; call `CreateSession()` for that.
- The route is `POST /eve/v1/session/{sessionId}/reset` and requires eve
  `0.31.0` or newer.

`ResetAsync` is not an alias for `CancelAsync`. `CancelAsync` only requests
cooperative cancellation of the active turn and keeps the conversation
resumable; `ResetAsync` retires the conversation itself.

## Compact a session

`CompactAsync` queues context compaction for the durable session without
sending model input:

```csharp
EveCompactOutcome compact = await session.CompactAsync(cancellationToken);

if (compact.Status == EveCompactStatus.Accepted)
{
    await foreach (EveStreamEvent streamEvent in session.StreamAsync(cancellationToken))
    {
        if (streamEvent.Kind is EveStreamEventKind.SessionWaiting
            or EveStreamEventKind.SessionCompleted
            or EveStreamEventKind.SessionFailed)
        {
            break;
        }
    }
}
```

- Compaction is asynchronous. Consume the durable stream through the next
  session boundary before sending another turn. `compaction.completed` confirms
  successful summarization.
- A session that never started returns `EveCompactStatus.NoActiveSession` and
  issues no HTTP request.
- Unlike reset, compaction preserves the local session cursor.
- The route is `POST /eve/v1/session/{sessionId}/compact` and requires eve
  `0.31.0` or newer.

`CompactAsync` is not an alias for `ResetAsync`. Compaction summarizes history
in place and keeps the conversation resumable; reset retires the conversation.

## Terminal behavior

Every session boundary advances the cursor and preserves the session
identifier. `session.waiting` parks the conversation for another turn;
`session.completed` and `session.failed` end it. In all three cases the handle
remains valid for streaming and inspection, so a finished conversation can
still be replayed from index `0`.
