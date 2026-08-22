---
description: Consume live eve events and configure automatic cursor-based reconnection.
---

# Streaming

Iterate an `EveMessageResponse` to process events as they arrive:

```csharp
await foreach (EveStreamEvent streamEvent in
    response.WithCancellation(cancellationToken))
{
    if (streamEvent.Kind == EveStreamEventKind.MessageAppended)
    {
        string delta = streamEvent.Data.GetProperty("messageDelta").GetString() ?? "";
        Console.Write(delta);
    }
}
```

Known event names map to `EveStreamEventKind`. The original wire-level `Type`
and `Data` remain available for forward compatibility.

## Preliminary tool output

A tool implemented as an async generator streams provisional snapshots. Each
non-terminal snapshot arrives as `action.partial`
(`EveStreamEventKind.ActionPartial`); the final one arrives as `action.result`
and is the only value exposed to the model.

```csharp
await foreach (EveStreamEvent streamEvent in
    response.WithCancellation(cancellationToken))
{
    switch (streamEvent.Kind)
    {
        case EveStreamEventKind.ActionPartial:
            RenderProvisional(streamEvent.Data.GetProperty("result"));
            break;
        case EveStreamEventKind.ActionResult:
            CommitResult(streamEvent.Data.GetProperty("result"));
            break;
    }
}
```

Treat a partial snapshot as provisional display state. Never persist one as a
final tool result, and expect it to be superseded.

## Approval lifecycle

eve `0.34.0` publishes the durable lifecycle of a human approval request. Each
responder attempt arrives as `approval.candidate`
(`EveStreamEventKind.ApprovalCandidate`) with a stable `candidateId` and an
`outcome` of `pending`, `rejected`, `failed`, `timed-out`, or `stale`. A terminal
candidate outcome may carry a `reason`. The request's terminal result arrives once
as `approval.settled` (`EveStreamEventKind.ApprovalSettled`) with an `outcome` of
`approved` or `cancelled`.

```csharp
await foreach (EveStreamEvent streamEvent in
    response.WithCancellation(cancellationToken))
{
    switch (streamEvent.Kind)
    {
        case EveStreamEventKind.ApprovalCandidate:
            TrackCandidate(
                streamEvent.Data.GetProperty("candidateId").GetString(),
                streamEvent.Data.GetProperty("outcome").GetString());
            break;
        case EveStreamEventKind.ApprovalSettled:
            SettleRequest(
                streamEvent.Data.GetProperty("requestId").GetString(),
                streamEvent.Data.GetProperty("outcome").GetString());
            break;
    }
}
```

Candidate events precede settlement for the same `requestId`. Neither event ends a
turn, and neither changes how `EveTurnOutcome` aggregates messages, results, or
input requests. Outcome values are read from raw data rather than projected to an
enum, so a future outcome added upstream is still readable.

An agent older than eve `0.34.0` never emits these events. Because the client maps
by wire type, they simply do not appear.

## Callback authorization parking

eve `0.41.0` may emit `authorization.required` with a `webhookUrl`, followed by an
interim `session.waiting`, while a framework-owned callback is pending. Keep enumerating
the active `EveMessageResponse`: it remains attached until the matching
`authorization.completed` event arrives and the resumed turn reaches its next session
boundary.

Pending authorizations are correlated by `Data["name"]`, so multiple callbacks can settle
independently. An `authorization.required` event without `webhookUrl` is non-blocking and
the following `session.waiting` ends the response normally.

`EveStreamEvent.IsCurrentTurnBoundary` identifies session-level boundary event types. Its
value is intentionally context-free, so it remains `true` for an interim waiting event.
Do not add a manual `break` for that property while iterating an active response; allow
`EveMessageResponse` to apply the pending-authorization context.

## Resolved human input

eve `0.39.1` emits `input.resolved` after accepting pending human input and before
the resumed `step.started`. Aggregate a response to read the authoritative outcomes:

```csharp
EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

foreach (EveInputResolution resolution in outcome.InputResolutions)
{
    Console.WriteLine(
        $"{resolution.RequestId}: {resolution.Outcome} at " +
        $"{resolution.TurnId}/{resolution.StepIndex}/{resolution.Sequence}");
}
```

`Response` contains the accepted option or text when one exists. It is `null` for
authoritative response-less outcomes such as `Ignored`. Unknown request kinds and
outcomes project as `Unknown` while `RawKind`, `RawOutcome`, and `Raw` preserve the
wire values.

## Durable event identity

Stream protocol version 20 stamps every persisted event with a stable
`evt_`-prefixed identifier before it is written, exposed as
`EveStreamEvent.Metadata.Id`. Replaying that event — through a reconnect, a
rewind, or a re-read of a finished session — yields the same identifier. A
retried step is not a replay: it is emitted again under a new identifier.

Events persisted before protocol version 20 carry no identifier and report
`null`, so they cannot be deduplicated. eve `0.27.6` emits protocol version 19
and never stamps one.
`EveStreamEventDeduplicator` encodes that contract, so a caller that resumes a
stream can drop events it already processed:

```csharp
EveStreamEventDeduplicator seen = new();

await foreach (EveStreamEvent streamEvent in
    session.StreamAsync(cancellationToken))
{
    if (!seen.Admit(streamEvent))
    {
        continue;
    }

    Handle(streamEvent);
}
```

The remembered set is unbounded because a bounded window cannot survive a rewind
past its capacity. Callers that retain nothing per event should bound their reads
with the session cursor instead.

## Reconnection

The default policy follows a durable stream from the next absolute event index.
`SendAsync` and `RespondAsync` responses keep reconnecting until the current turn
reaches a session boundary or the caller cancels consumption. Manually attached
`StreamAsync` reads retain a finite idle reconnect budget.

Each underlying stream read also has a fixed 15-second idle deadline. If an open
connection stops producing bytes without closing, the client disposes that response and
reconnects from the cursor after every fully consumed event. This deadline is separate
from the retry delays and attempt budget. Explicit caller cancellation remains terminal.

Set `EveStreamReconnectPolicy.StreamIdleRetry.MaxAttempts` to give an active
response an explicit finite budget. Progress resets any finite idle budget.

Disable reconnection when a proxy owns cursor recovery:

```csharp
StreamReconnectPolicy = EveStreamReconnectPolicy.Disabled;
```

## Cancel the exact response turn

Start consuming a response before requesting cancellation. `CancelAsync` waits
for that response's `turn.started`, sends its turn identifier as a guard, and
keeps stream consumption attached through the durable boundary:

```csharp
EveMessageResponse response = await session.SendAsync(
    "Run the long operation.",
    cancellationToken);
Task<EveTurnOutcome> outcomeTask = response.GetOutcomeAsync(cancellationToken);

EveCancellationOutcome cancellation =
    await response.CancelAsync(cancellationToken);
EveTurnOutcome outcome = await outcomeTask;
```

Concurrent calls share one in-flight cancellation request. The first call's
token controls that request; later callers can cancel only their own wait. A
failed request can be retried while the turn remains active, and a settled
response returns `NoActiveTurn` without another HTTP request. Use
`session.CancelAsync(turnId, cancellationToken)` when only an attached session
and an observed turn identifier are available.

## Attach to an existing stream

```csharp
await foreach (EveStreamEvent streamEvent in
    session.StreamAsync(cancellationToken))
{
    // Process historical and future events.
}
```

Negative start indexes are relative to the current tail and intentionally do
not advance the stored absolute cursor.

## Bounded catch-up reads

Set `Follow = false` to read everything recorded through the durable tail
observed when the stream opens, then stop instead of waiting for future events:

```csharp
await foreach (EveStreamEvent streamEvent in session.StreamAsync(
    new EveStreamOptions
    {
        Follow = false,
    },
    cancellationToken))
{
    // Process only the backlog, then regain control.
}
```

The first bounded request sends `includeTailIndex=1` and the server answers with
the `x-eve-stream-tail-index` response header. That first tail is an immutable
upper bound: reconnects resume from the advancing cursor without requesting or
rebasing the tail, and the stream completes as soon as the cursor passes the
bound, including immediately when the stored cursor is already past it. The
stored cursor advances past every consumed event.

Bounded reads require a nonnegative effective start cursor, so combining
`Follow = false` with a tail-relative `StartIndex` throws
`ArgumentOutOfRangeException`. A server that omits the tail header, or reports a
malformed or out-of-range value, throws `EveProtocolException`; eve `0.27.6`
never reported the header, so bounded reads against that release fail.

## Bound individual events

The upstream TypeScript client does not limit NDJSON event size. Preserve that
behavior by default, or opt into a client-wide UTF-8 byte limit for defense in depth:

```csharp
EveClientOptions options = new("https://agent.example.com")
{
    MaxStreamEventBytes = 1_048_576,
};
```

The limit applies to one raw NDJSON line before trimming, excluding its line ending.
An oversized event throws `EveProtocolException` without including the rejected
payload, and the deterministic protocol failure is not retried.
