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

## Reconnection

The default policy follows a durable stream from the next absolute event index.
Progress resets the idle retry budget, preventing a long-running active turn
from being abandoned.

Disable reconnection when a proxy owns cursor recovery:

```csharp
StreamReconnectPolicy = EveStreamReconnectPolicy.Disabled;
```

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
does not report the header yet.

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
