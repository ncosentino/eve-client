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
