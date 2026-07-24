# NexusLabs.Eve

<p align="center">
  <img
    src="https://raw.githubusercontent.com/ncosentino/eve-client/main/docs/assets/eve-brand.png"
    alt="eve.NET logo"
    width="320" />
</p>

[![CI](https://github.com/ncosentino/eve-client/actions/workflows/ci.yml/badge.svg)](https://github.com/ncosentino/eve-client/actions/workflows/ci.yml)
[![Documentation](https://github.com/ncosentino/eve-client/actions/workflows/docs.yml/badge.svg)](https://www.devleader.ca/projects/eve-client)
[![NuGet](https://img.shields.io/nuget/vpre/NexusLabs.Eve.svg)](https://www.nuget.org/packages/NexusLabs.Eve)

<!-- genesis:description:start -->
A C# client for the Vercel eve HTTP API.
<!-- genesis:description:end -->

`NexusLabs.Eve` ports the framework-neutral `eve/client` protocol surface to .NET:
health and agent inspection, authentication, durable sessions, human-input responses,
cooperative cancellation, NDJSON streaming, reconnect-by-index, attachments, and
structured output.

The initial compatibility target is Vercel `eve` **0.27.3** at commit
`5d961f350ff1d96ca71251f8cf638c8274962d00`, whose message stream protocol is version
**19**. eve is still a preview, so pin and test compatible versions before upgrading.

## Prerequisites

<!-- genesis:prerequisites:start -->
- .NET 10 SDK
<!-- genesis:prerequisites:end -->

## Install

```shell
dotnet add package NexusLabs.Eve
```

The package has no runtime dependencies outside .NET. Supply a caller-managed
`HttpMessageInvoker`; an `HttpClient` created by `IHttpClientFactory` can be passed
directly because it derives from `HttpMessageInvoker`.

Full documentation is published at
[www.devleader.ca/projects/eve-client](https://www.devleader.ca/projects/eve-client).

## Quick start

```csharp
using NexusLabs.Eve;

using HttpClient transport = httpClientFactory.CreateClient("eve");
EveClient client = new(
    transport,
    new EveClientOptions("https://agent.example.com")
    {
        Authentication = new EveBearerAuthentication(
            cancellationToken => GetAccessTokenAsync(cancellationToken)),
    });

EveHealthStatus health = await client.GetHealthAsync(cancellationToken);
EveSession session = client.CreateSession();
EveMessageResponse response = await session.SendAsync(
    "What is the weather in Brooklyn?",
    cancellationToken);
EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

Console.WriteLine($"{health.Status}: {outcome.Message}");
```

Keep the transport alive until every response stream has finished. For
credential-bearing clients, configure the transport not to follow redirects across
origins:

```csharp
services
    .AddHttpClient("eve")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
    });
```

## Authentication

Credentials and dynamic headers are resolved before every HTTP request, including
stream reconnects:

```csharp
EveClientOptions options = new("https://agent.example.com")
{
    Authentication = new EveVercelOidcAuthentication(
        cancellationToken => GetVercelOidcTokenAsync(cancellationToken)),
    HeadersProvider = async cancellationToken => new Dictionary<string, string>
    {
        ["x-vercel-protection-bypass"] =
            await GetProtectionBypassAsync(cancellationToken),
    },
};
```

Built-in providers cover bearer, Basic, and Vercel OIDC authentication. Authentication
headers take precedence over client-wide and per-turn headers.

Use `RequestHeadersProvider` to select dynamic headers by `EveRequestKind`, such as
limiting a bootstrap credential to `CreateSession` while continuing to resolve normal
authentication and infrastructure headers for stream reconnects.

## Streaming

`EveMessageResponse` is single-use. Aggregate it with `GetOutcomeAsync`, or consume
events as they arrive:

```csharp
EveMessageResponse response = await session.SendAsync(
    "Draft a plan and show your work.",
    cancellationToken);

await foreach (EveStreamEvent streamEvent in response.WithCancellation(cancellationToken))
{
    if (streamEvent.Kind == EveStreamEventKind.MessageAppended)
    {
        Console.Write(streamEvent.Data.GetProperty("messageDelta").GetString());
    }
}
```

Known wire types map to `EveStreamEventKind`; the original `Type` and `Data` JSON are
always preserved so newer eve events remain consumable before this package adds a
stronger projection.

The default reconnect policy mirrors the TypeScript client. A relay that owns cursor
recovery can disable it:

```csharp
EveMessageResponse response = await session.SendAsync(
    new EveSendTurnRequest
    {
        Message = EveMessageContent.FromText("Run the long operation."),
        StreamReconnectPolicy = EveStreamReconnectPolicy.Disabled,
    },
    cancellationToken);
```

## Continuations and cancellation

Persist `session.State` after consuming a stream, then resume it later:

```csharp
EveSession resumed = client.CreateSession(savedState);
EveMessageResponse response = await resumed.SendAsync(
    "Continue where we left off.",
    cancellationToken);
```

`SessionId` attaches to a durable stream; `ContinuationToken` sends the next turn;
`StreamIndex` prevents replaying consumed events.

Once a turn is accepted, cancellation can be requested before its stream settles:

```csharp
EveMessageResponse response = await session.SendAsync(
    "Run the long operation.",
    cancellationToken);
EveCancellationOutcome cancellation = await session.CancelAsync(cancellationToken);
EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);
```

Continue consuming the stream after cancellation to observe `turn.cancelled` followed
by `session.waiting` and to advance the cursor.

## Attachments and human input

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
EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

if (outcome.InputRequests.Count > 0)
{
    EveMessageResponse resumed = await session.SendAsync(
        new EveSendTurnRequest
        {
            InputResponses =
            [
                new EveInputResponse(
                    outcome.InputRequests[0].RequestId,
                    optionId: "approve"),
            ],
        },
        cancellationToken);
}
```

## Structured output

Pass raw JSON Schema in `EveSendTurnRequest.OutputSchema`. The server remains
authoritative for validation. Deserialize the final `result.completed` value with
source-generated metadata:

```csharp
Summary? summary = outcome.DeserializeData(AppJsonContext.Default.Summary);
```

## Scope

This package covers the transport-neutral TypeScript `Client`, `ClientSession`,
`MessageResponse`, session state, protocol events, and file-part helpers. The
TypeScript-only React/Vue/Svelte hooks, `EveAgentStore`, and UI message reducer are not
ported because they depend on JavaScript UI and AI SDK types rather than the HTTP
protocol.

`GetInfoAsync` validates the agent-info identity contract and exposes the complete JSON
through `EveAgentInfo.Raw`. This avoids breaking consumers whenever preview-only
inspection fields change.

## Development

<!-- genesis:build-test:start -->
```shell
dotnet tool restore
dotnet build
dotnet test
npm ci --prefix test/fixtures/eve-agent
npm run test:client --prefix test/fixtures/eve-agent
dotnet pack --no-build
pwsh scripts/validate-packages.ps1
```
<!-- genesis:build-test:end -->

## Architecture

- Caller-owned `HttpMessageInvoker` transport; no DI framework dependency.
- Immutable session cursors suitable for persistence.
- `System.Text.Json` DOM values at preview protocol boundaries for forward compatibility.
- Single-use async response streams with automatic absolute-cursor reconnection.
- TUnit contract tests modeled on Vercel's TypeScript client behavior.

## Project Structure

<!-- genesis:structure:start -->
```
src/
  NexusLabs.Eve/                 # Library source
  NexusLabs.Eve.Tests/           # Unit tests
```
<!-- genesis:structure:end -->

## Contributing

See `.github/instructions/` for coding conventions enforced by Copilot.
See [RELEASING.md](./RELEASING.md) for versioning, trusted publishing, and
release gates.

## License

MIT. See [LICENSE](./LICENSE).
