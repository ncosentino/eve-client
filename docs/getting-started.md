---
description: Install NexusLabs.Eve, create a client, and send the first turn to an eve agent.
---

# Getting Started

## Install the package

```shell
dotnet add package NexusLabs.Eve
```

The package targets .NET 10 and has no runtime package dependencies.

## Create the transport

Use a caller-managed `HttpClient` or `HttpMessageInvoker`. Applications using
dependency injection should obtain `HttpClient` from `IHttpClientFactory`.

```csharp
using NexusLabs.Eve;

using HttpClient transport = httpClientFactory.CreateClient("eve");
EveClient client = new(
    transport,
    new EveClientOptions("https://agent.example.com"));
```

Keep the transport alive until every active response stream has completed.

## Check the agent

```csharp
EveHealthStatus health = await client.GetHealthAsync(cancellationToken);
EveAgentInfo info = await client.GetInfoAsync(cancellationToken);
```

## Send a turn

```csharp
EveSession session = client.CreateSession();
EveMessageResponse response = await session.SendAsync(
    "Summarize the release status.",
    cancellationToken);
EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

Console.WriteLine(outcome.Message);
```

`EveMessageResponse` is single-use. Aggregate it with `GetOutcomeAsync`, or
iterate it directly to process events live.
