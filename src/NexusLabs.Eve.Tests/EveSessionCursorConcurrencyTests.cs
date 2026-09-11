using System.Net;
using System.Text;

namespace NexusLabs.Eve.Tests;

public sealed class EveSessionCursorConcurrencyTests
{
    [Test]
    public async Task ConcurrentSendResponses_FartherThenShorterKeepsFartherCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            CreateTerminalEvents(8))));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            CreateTerminalEvents(4))));
        EveSession session = CreateSession(transport, streamIndex: 0);

        EveMessageResponse first = await session.SendAsync("First", cancellationToken);
        EveMessageResponse second = await session.SendAsync("Second", cancellationToken);

        EveTurnOutcome fartherOutcome = await second.GetOutcomeAsync(cancellationToken);
        await Assert.That(fartherOutcome.Events.Count).IsEqualTo(8);
        await Assert.That(session.State.StreamIndex).IsEqualTo(8);

        EveTurnOutcome shorterOutcome = await first.GetOutcomeAsync(cancellationToken);
        await Assert.That(shorterOutcome.Events.Count).IsEqualTo(4);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 8,
        });
    }

    [Test]
    public async Task ConcurrentSendResponses_ShorterThenFartherAdvancesToFartherCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            CreateTerminalEvents(4))));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            CreateTerminalEvents(8))));
        EveSession session = CreateSession(transport, streamIndex: 0);

        EveMessageResponse first = await session.SendAsync("First", cancellationToken);
        EveMessageResponse second = await session.SendAsync("Second", cancellationToken);

        EveTurnOutcome shorterOutcome = await first.GetOutcomeAsync(cancellationToken);
        await Assert.That(shorterOutcome.Events.Count).IsEqualTo(4);
        await Assert.That(session.State.StreamIndex).IsEqualTo(4);

        EveTurnOutcome fartherOutcome = await second.GetOutcomeAsync(cancellationToken);
        await Assert.That(fartherOutcome.Events.Count).IsEqualTo(8);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 8,
        });
    }

    [Test]
    public async Task CancelledSendResponse_DoesNotRegressFartherCursor(
        CancellationToken cancellationToken)
    {
        using IdleAfterPrefixStream staleStream = new(EncodeEvents(
            CreateNonTerminalEvents(4)));
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            CreateTerminalEvents(8))));
        handler.Enqueue((_, _) => Task.FromResult(StreamResponse(staleStream)));
        EveSession session = CreateSession(transport, streamIndex: 0);
        EveTurnOptions options = new()
        {
            StreamReconnectPolicy = EveStreamReconnectPolicy.Disabled,
        };

        EveMessageResponse stale = await session.SendAsync(
            EveMessageContent.FromText("Stale"),
            options,
            cancellationToken);
        EveMessageResponse farther = await session.SendAsync(
            EveMessageContent.FromText("Farther"),
            options,
            cancellationToken);
        await farther.GetOutcomeAsync(cancellationToken);
        using CancellationTokenSource streamCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<EveTurnOutcome> staleOutcome = stale.GetOutcomeAsync(streamCancellation.Token);
        await staleStream.IdleReadStarted.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken);
        await streamCancellation.CancelAsync();
        EveTurnOutcome outcome = await staleOutcome.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(4);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 8,
        });
    }

    [Test]
    public async Task DisposedSendResponse_DoesNotRegressFartherCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            CreateTerminalEvents(8))));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            CreateNonTerminalEvents(6))));
        EveSession session = CreateSession(transport, streamIndex: 0);

        EveMessageResponse stale = await session.SendAsync("Stale", cancellationToken);
        EveMessageResponse farther = await session.SendAsync("Farther", cancellationToken);
        await farther.GetOutcomeAsync(cancellationToken);

        await using (IAsyncEnumerator<EveStreamEvent> enumerator =
            stale.GetAsyncEnumerator(cancellationToken))
        {
            for (int eventIndex = 0; eventIndex < 4; eventIndex++)
            {
                await Assert.That(await enumerator.MoveNextAsync())
                    .IsTrue()
                    .Because("The stale response must remain open until explicit disposal.");
            }
        }

        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 8,
        });
    }

    [Test]
    public async Task FailedSendResponse_DoesNotRegressFartherCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            CreateTerminalEvents(8))));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            [.. CreateNonTerminalEvents(4), "{"])));
        EveSession session = CreateSession(transport, streamIndex: 0);

        EveMessageResponse stale = await session.SendAsync("Stale", cancellationToken);
        EveMessageResponse farther = await session.SendAsync("Farther", cancellationToken);
        await farther.GetOutcomeAsync(cancellationToken);

        await Assert.That(async () => await stale.GetOutcomeAsync(cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 8,
        });
    }

    [Test]
    public async Task StreamAsync_ExplicitEarlierStartDoesNotRegressCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            CreateNonTerminalEvents(2))));
        EveSession session = CreateSession(transport, streamIndex: 8);

        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent streamEvent in session.StreamAsync(
            new EveStreamOptions
            {
                StartIndex = 2,
                ReconnectPolicy = EveStreamReconnectPolicy.Disabled,
            },
            cancellationToken))
        {
            events.Add(streamEvent);
        }

        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream?startIndex=2");
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 8,
        });
    }

    [Test]
    public async Task StreamAsync_EarlyDisposalDoesNotRegressFartherCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            CreateNonTerminalEvents(6))));
        EveSession session = CreateSession(transport, streamIndex: 8);

        await using (IAsyncEnumerator<EveStreamEvent> enumerator = session.StreamAsync(
            new EveStreamOptions
            {
                StartIndex = 2,
                ReconnectPolicy = EveStreamReconnectPolicy.Disabled,
            },
            cancellationToken).GetAsyncEnumerator(cancellationToken))
        {
            for (int eventIndex = 0; eventIndex < 2; eventIndex++)
            {
                await Assert.That(await enumerator.MoveNextAsync())
                    .IsTrue()
                    .Because("The manual stream must advance before explicit disposal.");
            }
        }

        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream?startIndex=2");
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 8,
        });
    }

    private static EveSession CreateSession(HttpMessageInvoker transport, int streamIndex) =>
        new EveClient(
            transport,
            new EveClientOptions("https://agent.example.com"))
        .CreateSession(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = streamIndex,
        });

    private static HttpResponseMessage AcceptedResponse() =>
        new(HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                """{"ok":true,"sessionId":"session_1"}""",
                Encoding.UTF8,
                "application/json"),
        };

    private static HttpResponseMessage StreamResponse(params string[] events) =>
        StreamResponse(new MemoryStream(EncodeEvents(events), writable: false));

    private static HttpResponseMessage StreamResponse(Stream stream)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
        };
        response.Headers.TryAddWithoutValidation(
            EveProtocol.StreamVersionHeaderName,
            EveProtocol.MessageStreamVersion);
        return response;
    }

    private static string[] CreateTerminalEvents(int count) =>
        [
            .. CreateNonTerminalEvents(count - 1),
            """{"type":"session.waiting","data":{"wait":"next-user-message"}}""",
        ];

    private static string[] CreateNonTerminalEvents(int count) =>
        Enumerable.Range(1, count)
            .Select(static index => $$"""{"type":"test.event.{{index}}"}""")
            .ToArray();

    private static byte[] EncodeEvents(IReadOnlyList<string> events) =>
        Encoding.UTF8.GetBytes($"{string.Join('\n', events)}\n");
}
