using System.Net;
using System.Text;

using static NexusLabs.Eve.Tests.EveTestResponses;

namespace NexusLabs.Eve.Tests;

public sealed class EveStrandedStreamTests
{
    private const string StrandedBody =
        """{"code":"session_stranded","error":"Reset this session to continue.","eveVersion":"0.0.1"}""";

    [Test]
    [Arguments("25", "session.failed", false)]
    [Arguments("25", "session.completed", false)]
    [Arguments("26", "session.failed", false)]
    [Arguments("26", "session.completed", false)]
    [Arguments("25", "session.failed", true)]
    [Arguments("25", "session.completed", true)]
    [Arguments("26", "session.failed", true)]
    [Arguments("26", "session.completed", true)]
    public async Task LiveStream_TerminalEventCompletesWithoutReadingOrReconnecting(
        string version,
        string terminalType,
        bool child,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        string payload = """{"type":"session.waiting","data":{}}""" + "\n"
            + "{\"type\":\"" + terminalType + "\",\"data\":{}}\n";
        await using BoundaryResponseStream stream = new(
            Encoding.UTF8.GetBytes(payload), cancellationToken);
        handler.Enqueue((_, requestToken) =>
        {
            stream.AttachRequestCancellation(requestToken);
            HttpResponseMessage response = StreamResponse(string.Empty, streamVersion: version);
            response.Content.Dispose();
            response.Content = new StreamContent(stream);
            return Task.FromResult(response);
        });
        EveSession session = CreateSession(transport);
        IAsyncEnumerable<EveStreamEvent> events = child
            ? session.StreamSubagentAsync(
                EveStreamEvent.Parse(
                    """{"type":"agent.started","data":{"sessionId":"child","callId":"call","streamPath":"/eve/v1/session/child/stream"}}"""),
                cancellationToken)
            : session.StreamAsync(cancellationToken);
        await using IAsyncEnumerator<EveStreamEvent> reader =
            events.GetAsyncEnumerator(cancellationToken);

        await Assert.That(await reader.MoveNextAsync()).IsTrue()
            .Because("A live follower continues through a nonterminal session boundary.");
        await Assert.That(reader.Current.Type).IsEqualTo("session.waiting");
        await Assert.That(await reader.MoveNextAsync()).IsTrue()
            .Because("The terminal event is yielded before completing.");
        await Assert.That(reader.Current.Type).IsEqualTo(terminalType);
        await Assert.That(await reader.MoveNextAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)).IsFalse()
            .Because("Session terminals complete even while the response body remains open.");
        await Assert.That(stream.ReadBlocked.IsCompleted).IsFalse()
            .Because("The follower must not read after a terminal event.");
        await Assert.That(stream.RequestCancellation.IsCompleted).IsTrue()
            .Because("Terminal completion aborts the live request.");
        await Assert.That(stream.IsDisposed).IsTrue()
            .Because("Terminal completion releases the response stream.");
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            child
                ? "https://agent.example.com/eve/v1/session/child/stream?streamControlVersion=1"
                : "https://agent.example.com/eve/v1/session/session_1/stream?streamControlVersion=1");
        await Assert.That(session.State.StreamIndex).IsEqualTo(child ? 0 : 2);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Stream_StrandedConflictIsFinalDespiteConfiguredRetry(
        bool follow,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) =>
        {
            HttpResponseMessage response = ErrorResponse(HttpStatusCode.Conflict, StrandedBody);
            response.Headers.Add("x-request-id", "request_1");
            return Task.FromResult(response);
        });
        EveSession session = CreateSession(transport);

        EveClientException? exception = await Assert.That(async () =>
        {
            await foreach (EveStreamEvent _ in session.StreamAsync(
                new EveStreamOptions { Follow = follow, ReconnectPolicy = ZeroDelayPolicy() },
                cancellationToken))
            {
            }
        }).Throws<EveClientException>();

        await Assert.That(exception!.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(exception.ErrorCode).IsEqualTo("session_stranded");
        await Assert.That(exception.Message).IsEqualTo("Reset this session to continue.");
        await Assert.That(exception.ResponseBody).IsEqualTo(StrandedBody);
        await Assert.That(exception.ResponseHeaders["x-request-id"].Single()).IsEqualTo("request_1");
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(session.State.StreamIndex).IsEqualTo(0);
    }

    [Test]
    [Arguments(HttpStatusCode.Conflict, """{"code":"session_not_ready"}""")]
    [Arguments(HttpStatusCode.Conflict, """{"code":"SESSION_STRANDED"}""")]
    [Arguments(HttpStatusCode.ServiceUnavailable, StrandedBody)]
    public async Task Stream_OtherConfiguredFailuresStillRetry(
        HttpStatusCode statusCode,
        string body,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(ErrorResponse(statusCode, body)));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.completed","data":{}}""" + "\n")));
        EveSession session = CreateSession(transport);
        List<string> eventTypes = [];

        await foreach (EveStreamEvent streamEvent in session.StreamAsync(
            new EveStreamOptions { ReconnectPolicy = ZeroDelayPolicy() },
            cancellationToken))
        {
            eventTypes.Add(streamEvent.Type);
        }

        await Assert.That(string.Join(",", eventTypes)).IsEqualTo("session.completed");
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(handler.Calls[0].Uri);
        await Assert.That(session.State.StreamIndex).IsEqualTo(1);
    }

    [Test]
    [Arguments("send")]
    [Arguments("respond")]
    [Arguments("clear")]
    public async Task InputAndControl_StrandedConflictPreservesErrorWithoutRetry(
        string operation,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) =>
            Task.FromResult(ErrorResponse(HttpStatusCode.Conflict, StrandedBody)));
        EveSession session = CreateSession(transport);

        EveClientException? exception = await Assert.That(async () =>
        {
            switch (operation)
            {
                case "send":
                    await session.SendAsync("Continue.", cancellationToken);
                    break;
                case "respond":
                    await session.RespondAsync(
                        [new EveInputResponse("request_1", "approve")], cancellationToken);
                    break;
                case "clear":
                    await session.ClearAsync(cancellationToken);
                    break;
            }
        }).Throws<EveClientException>();

        await Assert.That(exception!.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(exception.ErrorCode).IsEqualTo("session_stranded");
        await Assert.That(exception.ResponseBody).IsEqualTo(StrandedBody);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
    }

    private static EveSession CreateSession(HttpMessageInvoker transport) =>
        new EveClient(transport, new EveClientOptions("https://agent.example.com")
        {
            DeliveryRetryDelay = TimeSpan.Zero,
        }).AttachSession("session_1");

    private static HttpResponseMessage ErrorResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static EveStreamReconnectPolicy ZeroDelayPolicy() => new()
    {
        StreamOpenRetry = new EveRetryPolicy
        {
            BaseDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.Zero,
            MaxAttempts = 3,
        },
        StreamIdleRetry = new EveRetryPolicy
        {
            BaseDelay = TimeSpan.Zero,
            MaxDelay = TimeSpan.Zero,
            MaxAttempts = 1,
        },
    };
}
