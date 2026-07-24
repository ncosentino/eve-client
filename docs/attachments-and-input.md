---
description: Send files and answer human-input requests emitted by an eve agent.
---

# Attachments and Input

## Attach a file

```csharp
EveMessageContent message = EveMessageContent.FromParts(
    EveContentPart.CreateText("Summarize this report."),
    EveContentPart.CreateFile(
        reportBytes,
        "application/pdf",
        "report.pdf"));

EveMessageResponse response = await session.SendAsync(
    new EveSendTurnRequest { Message = message },
    cancellationToken);
```

Files may use inline data URLs or caller-provided URLs.

## Answer a request

`input.requested` events are aggregated into `EveTurnOutcome.InputRequests`.

```csharp
EveInputRequest request = outcome.InputRequests[0];
EveMessageResponse resumed = await session.SendAsync(
    new EveSendTurnRequest
    {
        InputResponses =
        [
            new EveInputResponse(request.RequestId, optionId: "approve"),
        ],
    },
    cancellationToken);
```

Input responses are retried for the short propagation window where an accepted
durable session is not yet visible to the delivery route.
