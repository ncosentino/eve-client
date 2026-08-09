---
description: Request JSON Schema output and deserialize result.completed with source-generated metadata.
---

# Structured Output

Pass a JSON Schema object on the turn:

```csharp
EveMessageResponse response = await session.SendAsync(
    EveMessageContent.FromText("Return a structured summary."),
    new EveTurnOptions { OutputSchema = outputSchema },
    cancellationToken);
```

The eve server remains authoritative for validation. The client exposes the
most recent `result.completed` value through `EveTurnOutcome.Data`.

Use source-generated JSON metadata to deserialize it:

```csharp
Summary? summary = outcome.DeserializeData(AppJsonContext.Default.Summary);
```

Structured-output schemas apply only to the turn that sends them.
