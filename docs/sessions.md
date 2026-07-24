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

## Terminal behavior

`session.waiting` preserves the conversation for another turn. By default,
`session.completed` and `session.failed` reset the local cursor. Set
`PreserveCompletedSessions` when completed sessions should remain resumable.
