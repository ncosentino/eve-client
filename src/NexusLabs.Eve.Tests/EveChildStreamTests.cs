using System.Net;
using System.Text;
using System.Text.Json;

using static NexusLabs.Eve.Tests.EveTestResponses;

namespace NexusLabs.Eve.Tests;

public sealed class EveChildStreamTests
{
    private const string ChildEvent = """{"type":"test.child","data":{}}""";
    private const string ProxyPath = "/eve/v1/session/parent/subagents/call/child/stream";
    private const string DirectPath = "/eve/v1/session/child/stream";

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ChildStream_UsesParentTransportAndIndependentLeaseCursor(
        bool legacy,
        bool remote,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        int resolution = 0;
        EveClient client = new(transport, new EveClientOptions("https://parent.example/eve/named?tenant=one")
        {
            Authentication = new EveBearerAuthentication(_ =>
                ValueTask.FromResult($"token-{++resolution}")),
            Headers = new Dictionary<string, string> { ["x-permission"] = "static" },
            HeadersProvider = _ => ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string> { ["x-permission"] = "dynamic" }),
            RequestHeadersProvider = (_, _) => ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>
                {
                    ["x-permission"] = "request",
                    ["authorization"] = "untrusted-generic-token",
                }),
        });
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            ChildEvent + "\n" + """{"$eve":"stream.lease-ended","version":1}""" + "\n",
            tailIndex: 3)));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(ChildEvent + "\n")));
        EveSession parent = client.AttachSession("parent", 77);
        EveStreamEvent descriptor = Descriptor(legacy, remote);

        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent streamEvent in parent.StreamSubagentAsync(
            descriptor,
            new EveStreamOptions { StartIndex = 2, Follow = false },
            cancellationToken))
        {
            events.Add(streamEvent);
        }

        string route = remote
            ? "/eve/named/v1/session/parent/subagents/call/child/stream"
            : "/eve/named/v1/session/child/stream";
        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events[0].Type).IsEqualTo("test.child");
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            $"https://parent.example{route}?tenant=one&streamControlVersion=1&startIndex=2&includeTailIndex=1");
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            $"https://parent.example{route}?tenant=one&streamControlVersion=1&startIndex=3");
        await Assert.That(handler.Calls[0].Headers["authorization"]).IsEqualTo("Bearer token-1");
        await Assert.That(handler.Calls[1].Headers["authorization"]).IsEqualTo("Bearer token-2");
        await Assert.That(handler.Calls[1].Headers["x-permission"]).IsEqualTo("request");
        await Assert.That(parent.State.StreamIndex).IsEqualTo(77);
        await Assert.That(parent.State.SessionId).IsEqualTo("parent");
    }

    [Test]
    [Arguments("https://other.example/eve/v1/session/child/stream")]
    [Arguments("//other.example/eve/v1/session/child/stream")]
    [Arguments("/eve/v1/session/child/stream?redirect=https://other.example")]
    [Arguments("/eve/v1/session/child/stream#fragment")]
    [Arguments("/eve/v1/session/other/stream")]
    [Arguments("/eve/v1/session/child/../stream")]
    [Arguments("/eve/v1/session/child\\stream")]
    public async Task ChildStream_RejectsUntrustedPathsBeforeTransport(
        string path,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSession parent = new EveClient(transport, new EveClientOptions("https://parent.example"))
            .AttachSession("parent");

        await Assert.That(() => parent.StreamSubagentAsync(
            Descriptor(false, false, path), cancellationToken)).Throws<ArgumentException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("""{"type":"subagent.called","data":{"sessionId":"foreign","childSessionId":"child","callId":"call","childStreamPath":"/eve/v1/session/child/stream"}}""")]
    [Arguments("""{"type":"agent.started","data":{"sessionId":"child","callId":"call","remote":{},"streamPath":"/eve/v1/session/foreign/subagents/call/child/stream"}}""")]
    [Arguments("""{"type":"agent.started","data":{"sessionId":"child","callId":"call"}}""")]
    [Arguments("""{"type":"agent.started","data":{"sessionId":"child","callId":"call","remote":42,"streamPath":"/eve/v1/session/parent/subagents/call/child/stream"}}""")]
    [Arguments("""{"type":"agent.started","data":{"sessionId":"..","callId":"call","streamPath":"/eve/v1/session/../stream"}}""")]
    [Arguments("""{"type":"agent.started","data":{"sessionId":"child","callId":"call","streamPath":13}}""")]
    [Arguments("""{"type":"subagent.called","data":{}}""")]
    [Arguments("""{"type":"test.other","data":{}}""")]
    public async Task ChildStream_RejectsForeignOrMalformedDescriptors(
        string json,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSession parent = new EveClient(transport, new EveClientOptions("https://parent.example"))
            .AttachSession("parent");

        await Assert.That(() => parent.StreamSubagentAsync(
            EveStreamEvent.Parse(json), cancellationToken)).Throws<ArgumentException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ChildStream_DefaultsToZeroAndDoesNotStoreChildProgress(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(ChildEvent + "\n")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(ChildEvent + "\n")));
        EveSession parent = new EveClient(transport, new EveClientOptions("https://parent.example"))
            .AttachSession("parent", 99);
        EveStreamOptions options = new() { ReconnectPolicy = EveStreamReconnectPolicy.Disabled };
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await foreach (EveStreamEvent _ in parent.StreamSubagentAsync(
                Descriptor(false, false), options, cancellationToken))
            {
            }
        }

        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo("https://parent.example" + DirectPath);
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(handler.Calls[0].Uri);
        await Assert.That(parent.State.StreamIndex).IsEqualTo(99);
        await Assert.That(() => parent.StreamSubagentAsync(
            Descriptor(false, false),
            new EveStreamOptions { Follow = false, StartIndex = -1 },
            cancellationToken)).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ChildStream_CancellationOrEarlyDisposalAbortsConnection(
        bool cancel,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        await using BoundaryResponseStream stream = new(Encoding.UTF8.GetBytes(ChildEvent + "\n"), cancellationToken);
        handler.Enqueue((_, requestToken) =>
        {
            stream.AttachRequestCancellation(requestToken);
            HttpResponseMessage response = StreamResponse(string.Empty);
            response.Content.Dispose();
            response.Content = new StreamContent(stream);
            return Task.FromResult(response);
        });
        EveSession parent = new EveClient(transport, new EveClientOptions("https://parent.example"))
            .AttachSession("parent", 88);
        using CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using (IAsyncEnumerator<EveStreamEvent> reader = parent.StreamSubagentAsync(
            Descriptor(false, false), source.Token).GetAsyncEnumerator(cancellationToken))
        {
            await Assert.That(await reader.MoveNextAsync()).IsTrue().Because("The child event is available.");
            if (cancel)
            {
                Task<bool> pending = reader.MoveNextAsync().AsTask();
                await stream.ReadBlocked.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
                await source.CancelAsync();
                await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken))
                    .IsFalse().Because("Local cancellation ends child enumeration.");
            }
        }

        await Assert.That(stream.IsDisposed).IsTrue().Because("The child transport is released.");
        await Assert.That(stream.RequestCancellation.IsCompleted).IsTrue().Because("Early disposal aborts the request.");
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(parent.State.StreamIndex).IsEqualTo(88);
    }

    [Test]
    public async Task ChildStream_EnforcesClientEventLimit(CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(ChildEvent + "\n")));
        EveSession parent = new EveClient(transport, new EveClientOptions("https://parent.example")
        {
            MaxStreamEventBytes = 8,
        }).AttachSession("parent");

        await Assert.That(async () =>
        {
            await foreach (EveStreamEvent _ in parent.StreamSubagentAsync(
                Descriptor(false, false), cancellationToken))
            {
            }
        }).Throws<EveProtocolException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ChildStream_RetriesOpenAndDisconnectWithoutChangingRoute(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{}}""" + "\n", tailIndex: 1)));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(ChildEvent + "\n")));
        EveSession parent = new EveClient(transport, new EveClientOptions("https://parent.example"))
            .AttachSession("parent", 91);
        List<string> eventTypes = [];

        await foreach (EveStreamEvent streamEvent in parent.StreamSubagentAsync(
            Descriptor(true, true),
            new EveStreamOptions
            {
                Follow = false,
                ReconnectPolicy = new EveStreamReconnectPolicy
                {
                    StreamOpenRetry = new EveRetryPolicy
                    {
                        BaseDelay = TimeSpan.Zero,
                        MaxDelay = TimeSpan.Zero,
                        MaxAttempts = 2,
                    },
                    StreamIdleRetry = new EveRetryPolicy
                    {
                        BaseDelay = TimeSpan.Zero,
                        MaxDelay = TimeSpan.Zero,
                        MaxAttempts = 2,
                    },
                },
            },
            cancellationToken))
        {
            eventTypes.Add(streamEvent.Type);
        }

        await Assert.That(string.Join(",", eventTypes)).IsEqualTo("session.waiting,test.child");
        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://parent.example" + ProxyPath + "?streamControlVersion=1&includeTailIndex=1");
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(handler.Calls[0].Uri);
        await Assert.That(handler.Calls[2].Uri).IsEqualTo(
            "https://parent.example" + ProxyPath + "?streamControlVersion=1&startIndex=1");
        await Assert.That(parent.State.StreamIndex).IsEqualTo(91);
    }

    [Test]
    public async Task ChildStream_HandlesTailRelativeAndAlreadyCancelledReads(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(ChildEvent + "\n")));
        EveSession parent = new EveClient(transport, new EveClientOptions("https://parent.example"))
            .AttachSession("parent", 12);
        int count = 0;
        await foreach (EveStreamEvent _ in parent.StreamSubagentAsync(
            Descriptor(false, false), new EveStreamOptions { StartIndex = -1 }, cancellationToken))
        {
            count++;
        }

        using CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await source.CancelAsync();
        await foreach (EveStreamEvent _ in parent.StreamSubagentAsync(
            Descriptor(false, false), source.Token))
        {
            count++;
        }

        await Assert.That(count).IsEqualTo(1);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://parent.example" + DirectPath + "?startIndex=-1");
        await Assert.That(parent.State.StreamIndex).IsEqualTo(12);
    }

    [Test]
    public async Task ChildStream_RequiresDescriptorAndEstablishedParent(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveClient client = new(transport, new EveClientOptions("https://parent.example"));
        EveSession parent = client.AttachSession("parent");

        await Assert.That(() => parent.StreamSubagentAsync(null!, cancellationToken))
            .Throws<ArgumentNullException>();
        await Assert.That(() => client.CreateSession().StreamSubagentAsync(
            Descriptor(false, false), cancellationToken)).Throws<InvalidOperationException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    private static EveStreamEvent Descriptor(bool legacy, bool remote, string? path = null)
    {
        string remoteJson = remote ? ""","remote":{"url":"https://remote.example","resolverId":"credential"}""" : string.Empty;
        string descriptorPath = path ?? (remote ? ProxyPath : DirectPath);
        string escapedPath = JsonEncodedText.Encode(descriptorPath).ToString();
        using JsonDocument data = JsonDocument.Parse(legacy
            ? $$"""{"sessionId":"parent","childSessionId":"child","callId":"call","childStreamPath":"{{escapedPath}}"{{remoteJson}}}"""
            : $$"""{"sessionId":"child","callId":"call","streamPath":"{{escapedPath}}"{{remoteJson}}}""");
        return new EveStreamEvent(
            legacy ? "subagent.called" : "agent.started",
            EveStreamEventKind.Unknown,
            data.RootElement,
            null);
    }
}
