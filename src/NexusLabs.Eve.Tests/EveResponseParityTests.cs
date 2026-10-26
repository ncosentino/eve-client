using System.Net;
using System.Text;
using System.Text.Json;

namespace NexusLabs.Eve.Tests;

public sealed class EveResponseParityTests
{
    [Test]
    [Arguments("input")]
    [Arguments("tasks")]
    public async Task CompletedAnswer_SurvivesEveryWait(
        string waitKind,
        CancellationToken cancellationToken)
    {
        string events =
            """
            {"type":"message.completed","data":{"finishReason":"stop","message":"Answer","turnId":"turn_1"}}
            {"type":"message.completed","data":{"finishReason":"tool-calls","message":"Tool narration","turnId":"turn_1"}}
            """
            + "\n"
            + $$$"""{"type":"turn.waiting","data":{"on":"{{{waitKind}}}","turnId":"turn_1"}}"""
            + "\n"
            + """{"type":"session.completed"}""";
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        QueueTurn(handler, events);
        EveClient client = CreateClient(transport);
        EveMessageResponse response = await client.CreateSession()
            .SendAsync("Test", cancellationToken);

        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Message).IsEqualTo("Answer");
        await Assert.That(outcome.Events.Count).IsEqualTo(waitKind == "input" ? 3 : 4);
        await Assert.That(outcome.Status).IsEqualTo(
            waitKind == "input" ? EveTurnStatus.Waiting : EveTurnStatus.Completed);
    }

    [Test]
    public async Task DuplicateRequest_PreservesFirstPromptOptionsAndAction(
        CancellationToken cancellationToken)
    {
        const string events =
            """
            {"type":"input.requested","data":{"requests":[{"requestId":"Question","kind":"question","prompt":"Original","options":[{"id":"first","label":"First"}],"action":{"toolName":"original"}},{"requestId":"question","kind":"question","prompt":"Other"}]}}
            {"type":"input.requested","data":{"requests":[{"requestId":"Question","kind":"tool-approval","prompt":"Replacement","options":[{"id":"replacement","label":"Replacement"}],"action":{"toolName":"replacement"}}]}}
            {"type":"turn.waiting","data":{"on":"input","turnId":"turn_1"}}
            """;
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        QueueTurn(handler, events);
        EveMessageResponse response = await CreateClient(transport).CreateSession()
            .SendAsync("Test", cancellationToken);

        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.InputRequests.Count).IsEqualTo(3);
        await Assert.That(outcome.InputRequests[2].Prompt).IsEqualTo("Replacement");
        await Assert.That(outcome.PendingInputRequests.Count).IsEqualTo(2);
        EveInputRequest first = outcome.PendingInputRequests[0];
        await Assert.That(first.RequestId).IsEqualTo("Question");
        await Assert.That(first.Prompt).IsEqualTo("Original");
        await Assert.That(first.Kind).IsEqualTo(EveInputRequestKind.Question);
        await Assert.That(first.Options.Count).IsEqualTo(1);
        await Assert.That(first.Options[0].Id).IsEqualTo("first");
        await Assert.That(first.Action.GetProperty("toolName").GetString()).IsEqualTo("original");
        await Assert.That(outcome.PendingInputRequests[1].RequestId).IsEqualTo("question");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SettledRequest_IsNotReopenedByReplay(
        bool approval,
        CancellationToken cancellationToken)
    {
        string settlement = approval
            ? """{"type":"approval.settled","data":{"requestId":"request_1","outcome":"approved"}}"""
            : """{"type":"input.resolved","data":{"resolutions":[{"requestId":"request_1","kind":"question","outcome":"answered"}],"turnId":"turn_1","stepIndex":0,"sequence":1}}""";
        string events =
            """{"type":"input.requested","data":{"requests":[{"requestId":"request_1","kind":"question","prompt":"Original"}]}}"""
            + "\n" + settlement + "\n"
            + """
            {"type":"input.requested","data":{"requests":[{"requestId":"request_1","kind":"question","prompt":"Replay"}]}}
            {"type":"turn.waiting","data":{"on":"tasks","turnId":"turn_1"}}
            {"type":"message.completed","data":{"finishReason":"stop","message":"Finished","turnId":"turn_1"}}
            {"type":"session.completed"}
            """;
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        QueueTurn(handler, events);
        EveMessageResponse response = await CreateClient(transport).CreateSession()
            .SendAsync("Test", cancellationToken);

        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.PendingInputRequests.Count).IsEqualTo(0);
        await Assert.That(outcome.InputRequests.Count).IsEqualTo(2);
        await Assert.That(outcome.Events.Count).IsEqualTo(6);
        await Assert.That(outcome.Message).IsEqualTo("Finished");
        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Completed);
    }

    [Test]
    [Arguments("question", "answered")]
    [Arguments("tool-approval", "approved")]
    public async Task AnswerResponse_CanCancelResolvedTurnBeforeAnotherBoundary(
        string kind,
        string outcome,
        CancellationToken cancellationToken)
    {
        string events =
            $$$"""{"type":"input.resolved","data":{"resolutions":[{"requestId":"request_1","kind":"{{{kind}}}","outcome":"{{{outcome}}}"}],"turnId":"resumed_turn","stepIndex":1,"sequence":2}}"""
            + "\n"
            + """{"type":"session.waiting","data":{"wait":"next-user-message"}}""";
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        QueueTurn(handler, events);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        EveMessageResponse response = await CreateClient(transport).AttachSession("session_1")
            .RespondAsync([new EveInputResponse("request_1", "approve", "Answer")], cancellationToken);
        await using IAsyncEnumerator<EveStreamEvent> reader =
            response.GetAsyncEnumerator(cancellationToken);

        await Assert.That(await reader.MoveNextAsync()).IsTrue()
            .Because("The resolution identifies a resumed turn before any new boundary.");
        EveCancellationOutcome cancellation = await response.CancelAsync(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);

        await Assert.That(reader.Current.Kind).IsEqualTo(EveStreamEventKind.InputResolved);
        await Assert.That(cancellation.Status).IsEqualTo(EveCancellationStatus.Accepted);
        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        using JsonDocument body = JsonDocument.Parse(handler.Calls[2].Body!);
        await Assert.That(body.RootElement.GetProperty("turnId").GetString())
            .IsEqualTo("resumed_turn");
    }

    [Test]
    public async Task MessageResponse_DoesNotCancelAnEarlierResolvedTurn(
        CancellationToken cancellationToken)
    {
        const string events =
            """
            {"type":"input.resolved","data":{"resolutions":[{"requestId":"old_request","kind":"question","outcome":"ignored"}],"turnId":"old_turn","stepIndex":0,"sequence":1}}
            {"type":"turn.started","data":{"turnId":"new_turn"}}
            {"type":"session.completed"}
            """;
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        QueueTurn(handler, events);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        EveMessageResponse response = await CreateClient(transport).CreateSession()
            .SendAsync("Test", cancellationToken);
        await using IAsyncEnumerator<EveStreamEvent> reader =
            response.GetAsyncEnumerator(cancellationToken);
        await Assert.That(await reader.MoveNextAsync()).IsTrue()
            .Because("The response observes an unrelated resolution before its new turn.");

        Task<EveCancellationOutcome> cancellation = response.CancelAsync(cancellationToken);
        await Assert.That(cancellation.IsCompleted).IsFalse()
            .Because("Normal sends must wait for their own turn identity.");
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(await reader.MoveNextAsync()).IsTrue()
            .Because("The next event identifies the newly accepted turn.");
        EveCancellationOutcome result = await cancellation.WaitAsync(
            TimeSpan.FromSeconds(30), cancellationToken);

        await Assert.That(result.Status).IsEqualTo(EveCancellationStatus.Accepted);
        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        using JsonDocument body = JsonDocument.Parse(handler.Calls[2].Body!);
        await Assert.That(body.RootElement.GetProperty("turnId").GetString()).IsEqualTo("new_turn");
    }

    [Test]
    public async Task RejectedAnswer_DoesNotCancelAnUnidentifiedTurn(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        QueueTurn(handler, """{"type":"session.waiting","data":{"wait":"next-user-message"}}""");
        EveMessageResponse response = await CreateClient(transport).AttachSession("session_1")
            .RespondAsync([new EveInputResponse("request_1", text: "Answer")], cancellationToken);
        await response.GetOutcomeAsync(cancellationToken);

        EveCancellationOutcome cancellation = await response.CancelAsync(cancellationToken);

        await Assert.That(cancellation.Status).IsEqualTo(EveCancellationStatus.NoActiveTurn);
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
    }

    private static EveClient CreateClient(HttpMessageInvoker transport) =>
        new(transport, new EveClientOptions("https://agent.example.com"));

    private static void QueueTurn(RecordingHttpMessageHandler handler, string events)
    {
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"sessionId":"session_1","deliveryId":"delivery_1"}""")));
        handler.Enqueue((_, _) => Task.FromResult(EveTestResponses.StreamResponse(events + "\n")));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) =>
        new(statusCode) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
