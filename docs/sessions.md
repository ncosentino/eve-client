---
description: Persist and resume eve continuation tokens, session IDs, and stream cursors.
---

# Sessions

An `EveSession` tracks three independent values:

- `ContinuationToken` sends the next user turn.
- `SessionId` identifies the durable runtime stream.
- `StreamIndex` counts events already consumed.

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

When only a continuation token was persisted:

```csharp
EveSession resumed = client.CreateSession(continuationToken);
```

## Reset a session

`ResetAsync` terminally retires the durable session that owns the current
continuation token, so the next send starts a fresh conversation:

```csharp
EveResetOutcome reset = await session.ResetAsync(cancellationToken);

if (reset.Status == EveResetStatus.Reset)
{
    await SaveAsync(session.State, cancellationToken);
}
```

- The accepted continuation token is recorded as soon as `SendAsync` returns,
  so reset can run before the response stream is consumed.
- A session that has an ID but no continuation token still has an outstanding
  response stream. Consume it before resetting; otherwise `ResetAsync` throws
  `InvalidOperationException`.
- A session that never started returns `EveResetStatus.NoActiveSession` and
  issues no HTTP request.
- A successful reset clears the local state. Persist the empty state and
  discard any previously cached cursor.
- The route requires eve `0.27.4` or newer. Older deployments answer with
  HTTP 404, surfaced as `EveClientException`.

`ResetAsync` is not an alias for `CancelAsync`. `CancelAsync` only requests
cooperative cancellation of the active turn and keeps the conversation
resumable; `ResetAsync` retires the conversation itself.

## Terminal behavior

`session.waiting` preserves the conversation for another turn. By default,
`session.completed` and `session.failed` reset the local cursor. Set
`PreserveCompletedSessions` when completed sessions should remain resumable.
