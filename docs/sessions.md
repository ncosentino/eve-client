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

## Clear session context

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
