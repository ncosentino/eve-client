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

EveMessageResponse response = await session.SendAsync(message, cancellationToken);
```

Files may use inline data URLs or caller-provided URLs.

## Answer a request

`input.requested` events are aggregated into `EveTurnOutcome.InputRequests`.

```csharp
EveInputRequest request = outcome.InputRequests[0];
EveMessageResponse resumed = await session.RespondAsync(
    [new EveInputResponse(request.RequestId, optionId: "approve")],
    cancellationToken);
```

eve `0.31.0` requires a turn to carry either a message or input responses, never both.
`SendAsync` sends a message and `RespondAsync` resolves pending input; passing both on one
request throws `ArgumentException` before any network call, matching the server's HTTP 400
`'message' and 'inputResponses' are mutually exclusive`.

Input responses are retried for the short propagation window where an accepted
durable session is not yet visible to the delivery route.

## Classify a request

eve stamps every input request with a framework-owned discriminator, projected as
`EveInputRequest.Kind`. Route on it instead of inferring intent from `Display`,
`Options`, or the tool name inside `Action`: a session-limit prompt can arrive
with a confirmation hint, two options, and a tool name, yet it is not an
approve/deny tool prompt.

```csharp
string answer = request.Kind switch
{
    EveInputRequestKind.ToolApproval => "approve",
    EveInputRequestKind.SessionLimit => "continue",
    EveInputRequestKind.Question => ChooseAnswer(request),
    _ => throw new NotSupportedException(
        $"Unhandled eve input request kind '{request.RawKind}'."),
};
```

`Kind` reports `EveInputRequestKind.Unknown` in two cases, and `RawKind`
distinguishes them:

| `Kind` | `RawKind` | Meaning |
|---|---|---|
| `Question`, `ToolApproval`, `SessionLimit` | matching wire value | A modelled request kind |
| `Unknown` | the wire value | A newer eve emitted a kind this package does not model |
| `Unknown` | `null` | The server predates the discriminator, such as eve `0.27.6` or earlier |

A `kind` that is present but not a string is a malformed request and throws
`EveProtocolException` rather than being reported as a legacy server.
