using System.Net;
using System.Text;
using System.Text.Json;

namespace NexusLabs.Eve.Tests;

public sealed class EveSessionTests
{
    [Test]
    public async Task SendAsync_AggregatesTurnAndAdvancesWaitingSession(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"message.completed","data":{"finishReason":"stop","message":"Done.","sequence":1,"stepIndex":0,"turnId":"turn_1"}}""",
            """{"type":"result.completed","data":{"result":{"count":2},"sequence":2,"stepIndex":0,"turnId":"turn_1"}}""",
            """{"type":"input.requested","data":{"requests":[{"requestId":"approval_1","prompt":"Approve?","display":"confirmation","options":[{"id":"approve","label":"Approve"}],"action":{"kind":"tool-call"}}],"sequence":3,"stepIndex":0,"turnId":"turn_1"}}""",
            """{"type":"session.waiting","data":{"continuationToken":"eve:rekeyed","wait":"next-user-message"}}""")));
        EveClient client = CreateClient(transport);
        EveSession session = client.CreateSession();
        EveSendTurnRequest request = new()
        {
            Message = EveMessageContent.FromText("Run the check."),
            ClientContext = EveClientContext.FromJson(EveJsonElementFactory.CreateObject(writer =>
            {
                writer.WriteString("route", "/billing");
            })),
            OutputSchema = EveJsonElementFactory.CreateObject(writer =>
            {
                writer.WriteString("type", "object");
            }),
            Headers = new Dictionary<string, string>
            {
                ["x-request-id"] = "request_1",
            },
        };

        EveMessageResponse response = await session.SendAsync(request, cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(response.SessionId).IsEqualTo("session_1");
        await Assert.That(response.ContinuationToken).IsEqualTo("eve:accepted");
        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Waiting);
        await Assert.That(outcome.Message).IsEqualTo("Done.");
        await Assert.That(outcome.Events.Count).IsEqualTo(4);
        await Assert.That(outcome.InputRequests.Count).IsEqualTo(1);
        await Assert.That(outcome.InputRequests[0].RequestId).IsEqualTo("approval_1");
        await Assert.That(outcome.InputRequests[0].Options.Count).IsEqualTo(1);
        await Assert.That(outcome.Data.HasValue)
            .IsTrue()
            .Because("The stream emitted result.completed.");
        await Assert.That(outcome.Data.GetValueOrDefault().GetProperty("count").GetInt32())
            .IsEqualTo(2);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            ContinuationToken = "eve:rekeyed",
            SessionId = "session_1",
            StreamIndex = 4,
        });
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session");
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream");
        await Assert.That(handler.Calls[0].Headers["x-request-id"]).IsEqualTo("request_1");
        await Assert.That(handler.Calls[1].Headers["x-request-id"]).IsEqualTo("request_1");

        using JsonDocument sentBody = JsonDocument.Parse(handler.Calls[0].Body!);
        await Assert.That(sentBody.RootElement.GetProperty("message").GetString())
            .IsEqualTo("Run the check.");
        await Assert.That(sentBody.RootElement.GetProperty("clientContext")
            .GetProperty("route")
            .GetString())
            .IsEqualTo("/billing");
        await Assert.That(sentBody.RootElement.GetProperty("outputSchema")
            .GetProperty("type")
            .GetString())
            .IsEqualTo("object");
    }

    [Test]
    public async Task SendAsync_ContinuesWithWaitingContinuationToken(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"}}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse(
            "session_1",
            "eve:second")));
        EveSession session = CreateClient(transport).CreateSession();

        EveMessageResponse first = await session.SendAsync("First", cancellationToken);
        await first.GetOutcomeAsync(cancellationToken);
        await session.SendAsync("Second", cancellationToken);

        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        await Assert.That(handler.Calls[2].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1");
        using JsonDocument body = JsonDocument.Parse(handler.Calls[2].Body!);
        await Assert.That(body.RootElement.GetProperty("continuationToken").GetString())
            .IsEqualTo("eve:next");
        await Assert.That(body.RootElement.GetProperty("message").GetString()).IsEqualTo("Second");
    }

    [Test]
    public async Task CompletedSession_ResetsByDefault(CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.completed"}""")));
        EveSession session = CreateClient(transport).CreateSession();

        EveMessageResponse response = await session.SendAsync("One shot", cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Completed);
        await Assert.That(session.State).IsEqualTo(new EveSessionState());
    }

    [Test]
    public async Task CompletedSession_CanPreserveContinuationState(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.completed"}""")));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                PreserveCompletedSessions = true,
            });
        EveSession session = client.CreateSession();

        EveMessageResponse response = await session.SendAsync("Keep it", cancellationToken);
        await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            ContinuationToken = "eve:accepted",
            SessionId = "session_1",
            StreamIndex = 1,
        });
    }

    [Test]
    public async Task CancelAsync_TargetsAcceptedTurnBeforeStreamConsumption(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        EveSession session = CreateClient(transport).CreateSession();

        await session.SendAsync("Wait", cancellationToken);
        EveCancellationOutcome outcome = await session.CancelAsync(
            "turn_1",
            cancellationToken);

        await Assert.That(outcome.SessionId).IsEqualTo("session_1");
        await Assert.That(outcome.Status).IsEqualTo(EveCancellationStatus.Accepted);
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/cancel");
        using JsonDocument body = JsonDocument.Parse(handler.Calls[1].Body!);
        await Assert.That(body.RootElement.GetProperty("turnId").GetString())
            .IsEqualTo("turn_1");
    }

    [Test]
    public async Task TurnStream_ReconnectsFromAdvancedCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"turn.started","data":{"sequence":1,"turnId":"turn_1"}}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"}}""")));
        EveSession session = CreateClient(transport).CreateSession();
        EveSendTurnRequest request = new()
        {
            Message = EveMessageContent.FromText("Reconnect"),
            StreamReconnectPolicy = ZeroDelayReconnectPolicy(),
        };

        EveMessageResponse response = await session.SendAsync(request, cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(2);
        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream");
        await Assert.That(handler.Calls[2].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream?startIndex=1");
    }

    [Test]
    public async Task DisabledReconnect_UsesOneStreamConnectionAndPreservesPartialCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"turn.started","data":{"sequence":1,"turnId":"turn_1"}}""")));
        EveSession session = CreateClient(transport).CreateSession();
        EveSendTurnRequest request = new()
        {
            Message = EveMessageContent.FromText("One connection"),
            StreamReconnectPolicy = EveStreamReconnectPolicy.Disabled,
        };

        EveMessageResponse response = await session.SendAsync(request, cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(1);
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            ContinuationToken = "eve:accepted",
            SessionId = "session_1",
            StreamIndex = 1,
        });
    }

    [Test]
    public async Task TailRelativeStream_DoesNotAdvanceAbsoluteCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:latest","wait":"next-user-message"}}""")));
        EveSessionState initialState = new()
        {
            ContinuationToken = "eve:current",
            SessionId = "session_1",
            StreamIndex = 7,
        };
        EveSession session = CreateClient(transport).CreateSession(initialState);
        List<EveStreamEvent> events = [];

        await foreach (EveStreamEvent streamEvent in session.StreamAsync(
            new EveStreamOptions
            {
                StartIndex = -1,
                ReconnectPolicy = EveStreamReconnectPolicy.Disabled,
            },
            cancellationToken))
        {
            events.Add(streamEvent);
        }

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream?startIndex=-1");
        await Assert.That(session.State).IsEqualTo(initialState);
    }

    [Test]
    public async Task InputResponse_RetriesSessionPropagationFailure(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.InternalServerError,
            """{"error":"Target session was not found."}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"}}""")));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                DeliveryRetryDelay = TimeSpan.Zero,
            });
        EveSession session = client.CreateSession(new EveSessionState
        {
            ContinuationToken = "eve:current",
            SessionId = "session_1",
        });
        EveSendTurnRequest request = new()
        {
            InputResponses =
            [
                new EveInputResponse("approval_1", "approve"),
            ],
        };

        EveMessageResponse response = await session.SendAsync(request, cancellationToken);
        await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        await Assert.That(handler.Calls[0].Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Calls[1].Method).IsEqualTo(HttpMethod.Post);
    }

    [Test]
    public async Task ConsumedTurn_AppliesCursorAfterAnotherTurnWasAccepted(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse(
            "session_1",
            "eve:first-accepted")));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse(
            "session_1",
            "eve:second-accepted")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:first-waiting","wait":"next-user-message"}}""")));
        EveSession session = CreateClient(transport).CreateSession();

        EveMessageResponse first = await session.SendAsync("First", cancellationToken);
        await session.SendAsync("Second", cancellationToken);
        await first.GetOutcomeAsync(cancellationToken);

        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            ContinuationToken = "eve:first-waiting",
            SessionId = "session_1",
            StreamIndex = 1,
        });
    }

    [Test]
    public async Task MessageResponse_CanOnlyBeConsumedOnce(CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.completed"}""")));
        EveSession session = CreateClient(transport).CreateSession();
        EveMessageResponse response = await session.SendAsync("Once", cancellationToken);
        await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(async () => await response.GetOutcomeAsync(cancellationToken))
            .Throws<InvalidOperationException>();
    }

    private static EveClient CreateClient(HttpMessageInvoker transport) =>
        new(
            transport,
            new EveClientOptions("https://agent.example.com"));

    private static EveStreamReconnectPolicy ZeroDelayReconnectPolicy() =>
        new()
        {
            StreamOpenRetry = new EveRetryPolicy
            {
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
            },
            StreamIdleRetry = new EveRetryPolicy
            {
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
            },
        };

    private static HttpResponseMessage AcceptedResponse(
        string sessionId = "session_1",
        string continuationToken = "eve:accepted") =>
        JsonResponse(
            HttpStatusCode.Accepted,
            $$"""{"ok":true,"sessionId":"{{sessionId}}","continuationToken":"{{continuationToken}}"}""");

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage StreamResponse(params string[] events) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{string.Join('\n', events)}\n",
                Encoding.UTF8,
                EveProtocol.MessageStreamContentType),
        };
}
