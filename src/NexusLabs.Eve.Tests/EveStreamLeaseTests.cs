using System.Net;
using System.Text;

namespace NexusLabs.Eve.Tests;

public sealed class EveStreamLeaseTests
{
    private const string LeaseEnded =
        """{"$eve":"stream.lease-ended","version":1}""";

    [Test]
    public async Task BoundedStream_RenewsLeasesFromAdvancedCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            $"\n{Event(1)}\n\n{Event(2)}\n\n{LeaseEnded}\n",
            tailIndex: 5)));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            $"{Event(3)}\n{Event(4)}\n{LeaseEnded}\n")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            $"{Event(5)}\n{LeaseEnded}\n")));
        EveSession session = CreateSession(transport, streamIndex: 1);

        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent streamEvent in session.StreamAsync(
            new EveStreamOptions
            {
                Follow = false,
                StartIndex = 1,
            },
            cancellationToken))
        {
            events.Add(streamEvent);
        }

        await Assert.That(string.Join(
            ",",
            events.Select(static streamEvent => streamEvent.Type)))
            .IsEqualTo(
                "test.event.1,test.event.2,test.event.3,test.event.4,test.event.5");
        await Assert.That(string.Join(
            Environment.NewLine,
            handler.Calls.Select(static call => call.Uri)))
            .IsEqualTo(
                "https://agent.example.com/eve/v1/session/session_1/stream"
                + "?streamControlVersion=1&startIndex=1&includeTailIndex=1"
                + Environment.NewLine
                + "https://agent.example.com/eve/v1/session/session_1/stream"
                + "?streamControlVersion=1&startIndex=3"
                + Environment.NewLine
                + "https://agent.example.com/eve/v1/session/session_1/stream"
                + "?streamControlVersion=1&startIndex=5");
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 6,
        });
    }

    [Test]
    public async Task BoundedStream_LeaseRenewalDoesNotConsumeIdleAttempt(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            $"{LeaseEnded}\n",
            tailIndex: 0)));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse($"{LeaseEnded}\n")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse($"{Event(0)}\n")));
        EveSession session = CreateSession(transport, streamIndex: 0);

        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent streamEvent in session.StreamAsync(
            new EveStreamOptions
            {
                Follow = false,
                ReconnectPolicy = new EveStreamReconnectPolicy
                {
                    StreamIdleRetry = new EveRetryPolicy
                    {
                        BaseDelay = TimeSpan.Zero,
                        MaxAttempts = 1,
                        MaxDelay = TimeSpan.Zero,
                    },
                },
            },
            cancellationToken))
        {
            events.Add(streamEvent);
        }

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Type).IsEqualTo("test.event.0");
        await Assert.That(handler.Calls.Count).IsEqualTo(3);
    }

    [Test]
    public async Task StreamAsync_DisabledReconnectDoesNotNegotiateLease(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse($"{Event(0)}\n")));
        EveSession session = CreateSession(transport, streamIndex: 0);

        await foreach (EveStreamEvent _ in session.StreamAsync(
            new EveStreamOptions
            {
                ReconnectPolicy = EveStreamReconnectPolicy.Disabled,
            },
            cancellationToken))
        {
        }

        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream");
    }

    [Test]
    public async Task StreamAsync_TailRelativeCursorDoesNotNegotiateLease(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse($"{Event(0)}\n")));
        EveSession session = CreateSession(transport, streamIndex: 0);

        await foreach (EveStreamEvent _ in session.StreamAsync(
            new EveStreamOptions
            {
                StartIndex = -1,
            },
            cancellationToken))
        {
        }

        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream?startIndex=-1");
    }

    [Test]
    [Arguments("""{"$eve":"stream.lease-ended","version":2}""")]
    [Arguments("""{"$eve":"stream.lease-ended"}""")]
    public async Task StreamAsync_RejectsMalformedLeaseControl(
        string control,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(StreamResponse($"{control}\n")));
        EveSession session = CreateSession(transport, streamIndex: 0);

        await Assert.That(async () =>
            {
                await foreach (EveStreamEvent _ in session.StreamAsync(cancellationToken))
                {
                }
            })
            .Throws<EveProtocolException>();
    }

    [Test]
    public async Task StreamAsync_RejectsUnnegotiatedLeaseControl(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse($"{LeaseEnded}\n")));
        EveSession session = CreateSession(transport, streamIndex: 0);

        await Assert.That(async () =>
            {
                await foreach (EveStreamEvent _ in session.StreamAsync(
                    new EveStreamOptions
                    {
                        ReconnectPolicy = EveStreamReconnectPolicy.Disabled,
                    },
                    cancellationToken))
                {
                }
            })
            .Throws<EveProtocolException>();
    }

    private static EveSession CreateSession(HttpMessageInvoker transport, int streamIndex) =>
        new EveClient(
            transport,
            new EveClientOptions("https://agent.example.com"))
        .AttachSession("session_1", streamIndex);

    private static string Event(int index) =>
        "{\"type\":\"test.event."
        + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + "\",\"meta\":{\"at\":\"2026-09-20T12:00:00.000Z\"}}";

    private static HttpResponseMessage StreamResponse(string body, int? tailIndex = null)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                body,
                Encoding.UTF8,
                EveProtocol.MessageStreamContentType),
        };
        response.Headers.TryAddWithoutValidation(
            EveProtocol.StreamVersionHeaderName,
            EveProtocol.MessageStreamVersion);
        if (tailIndex is int value)
        {
            response.Headers.TryAddWithoutValidation(
                "x-eve-stream-tail-index",
                value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return response;
    }
}
