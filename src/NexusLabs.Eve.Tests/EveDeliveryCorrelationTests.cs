using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NexusLabs.Eve.Tests;

public sealed class EveDeliveryCorrelationTests
{
    [Test]
    [Arguments(EveTurnPolicy.Queue)]
    [Arguments(EveTurnPolicy.Steer)]
    public async Task ExistingSend_SkipsOlderTurnsUntilAcceptedDelivery(
        EveTurnPolicy turnPolicy,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("delivery_new")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            Event("turn.started", "delivery_old", """{"turnId":"turn_old"}"""),
            Event("message.completed", "delivery_old", """{"finishReason":"stop","message":"Old."}"""),
            Event("session.waiting", "delivery_old", """{"wait":"next-user-message"}"""),
            Event("turn.started", "delivery_active", """{"turnId":"turn_active"}"""),
            Event("turn.started", "delivery_new", """{"turnId":"turn_new"}"""),
            Event("message.completed", "delivery_new", """{"finishReason":"stop","message":"New."}"""),
            Event("session.waiting", "delivery_new", """{"wait":"next-user-message"}"""))));
        EveSession session = CreateSession(transport);

        EveMessageResponse response = await session.SendAsync(
            EveMessageContent.FromText("New"),
            new EveTurnOptions
            {
                TurnPolicy = turnPolicy,
                StreamReconnectPolicy = EveStreamReconnectPolicy.Disabled,
            },
            cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(response.DeliveryId).IsEqualTo("delivery_new");
        await Assert.That(outcome.Events.Count).IsEqualTo(3);
        await Assert.That(outcome.Message).IsEqualTo("New.");
        await Assert.That(outcome.Events[0].Data.GetProperty("turnId").GetString())
            .IsEqualTo("turn_new");
        await Assert.That(session.State.StreamIndex)
            .IsEqualTo(7)
            .Because("Skipped stale events still advance the durable cursor.");
    }

    [Test]
    public async Task ExistingSteer_PreservesActiveTurnIdentityAndUsage(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("delivery_new")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            Event("turn.started", "delivery_old", """{"sequence":0,"turnId":"turn_1"}"""),
            Event(
                "message.received",
                "delivery_old",
                """{"message":"First","sequence":0,"turnId":"turn_1"}"""),
            Event(
                "message.received",
                "delivery_new",
                """{"message":"Instead","sequence":0,"turnId":"turn_1"}"""),
            Event(
                "step.completed",
                "delivery_new",
                """{"finishReason":"stop","sequence":1,"stepIndex":1,"turnId":"turn_1","usage":{"cacheReadTokens":3,"cacheWriteTokens":0,"costUsd":0.01,"inputTokens":10,"outputTokens":2}}"""),
            Event(
                "message.completed",
                "delivery_new",
                """{"finishReason":"stop","message":"Updated reply.","sequence":2,"stepIndex":1,"turnId":"turn_1"}"""),
            Event(
                "session.waiting",
                "delivery_new",
                """{"wait":"next-user-message"}"""))));
        EveSession session = CreateSession(transport);

        EveTurnOutcome outcome = await (await session.SendAsync(
            EveMessageContent.FromText("Instead"),
            new EveTurnOptions
            {
                TurnPolicy = EveTurnPolicy.Steer,
            },
            cancellationToken)).GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(4);
        await Assert.That(outcome.Events.Any(static streamEvent =>
            streamEvent.Kind == EveStreamEventKind.TurnStarted))
            .IsFalse()
            .Because("In-flight steering continues the active turn without another turn.started.");
        await Assert.That(outcome.Events
            .Where(static streamEvent => streamEvent.Data.TryGetProperty("turnId", out _))
            .Select(static streamEvent => streamEvent.Data.GetProperty("turnId").GetString()!))
            .IsEquivalentTo(["turn_1", "turn_1", "turn_1"]);
        EveStreamEvent completedStep = outcome.Events.Single(static streamEvent =>
            streamEvent.Kind == EveStreamEventKind.StepCompleted);
        await Assert.That(completedStep.Data.GetProperty("usage")
            .GetProperty("inputTokens").GetInt32())
            .IsEqualTo(10);
        await Assert.That(outcome.Message).IsEqualTo("Updated reply.");
        await Assert.That(session.State.StreamIndex).IsEqualTo(6);
    }

    [Test]
    public async Task ExistingSteer_AfterSettlementCorrelatesFollowUpTurn(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("delivery_new")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            Event(
                "message.received",
                "delivery_old",
                """{"message":"First","sequence":0,"turnId":"turn_1"}"""),
            Event("turn.started", "delivery_old", """{"sequence":1,"turnId":"turn_1"}"""),
            Event(
                "message.completed",
                "delivery_old",
                """{"finishReason":"stop","message":"First reply.","sequence":2,"stepIndex":0,"turnId":"turn_1"}"""),
            Event(
                "session.waiting",
                "delivery_old",
                """{"wait":"next-user-message"}"""),
            Event(
                "message.received",
                "delivery_new",
                """{"message":"Instead","sequence":0,"turnId":"turn_2"}"""),
            Event("turn.started", "delivery_new", """{"sequence":1,"turnId":"turn_2"}"""),
            Event(
                "message.completed",
                "delivery_new",
                """{"finishReason":"stop","message":"Follow-up reply.","sequence":2,"stepIndex":0,"turnId":"turn_2"}"""),
            Event(
                "session.waiting",
                "delivery_new",
                """{"wait":"next-user-message"}"""))));
        EveSession session = CreateSession(transport);

        EveTurnOutcome outcome = await (await session.SendAsync(
            EveMessageContent.FromText("Instead"),
            new EveTurnOptions
            {
                TurnPolicy = EveTurnPolicy.Steer,
            },
            cancellationToken)).GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(4);
        await Assert.That(outcome.Events[0].Kind).IsEqualTo(EveStreamEventKind.MessageReceived);
        await Assert.That(outcome.Events[1].Kind).IsEqualTo(EveStreamEventKind.TurnStarted);
        await Assert.That(outcome.Events
            .Where(static streamEvent => streamEvent.Data.TryGetProperty("turnId", out _))
            .Select(static streamEvent => streamEvent.Data.GetProperty("turnId").GetString()!))
            .IsEquivalentTo(["turn_2", "turn_2", "turn_2"]);
        await Assert.That(outcome.Message).IsEqualTo("Follow-up reply.");
        await Assert.That(session.State.StreamIndex).IsEqualTo(8);
    }

    [Test]
    public async Task ExistingSend_AcceptsCoalescedDeliveryIdentifier(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("delivery_new")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            EventWithDeliveryIds(
                "turn.started",
                """["delivery_other","delivery_new"]""",
                """{"turnId":"turn_new"}"""),
            EventWithDeliveryIds(
                "session.waiting",
                """["delivery_other","delivery_new"]""",
                """{"wait":"next-user-message"}"""))));
        EveSession session = CreateSession(transport);

        EveTurnOutcome outcome = await (await session.SendAsync(
            "Coalesce",
            cancellationToken)).GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(2);
        await Assert.That(outcome.Events[0].Metadata!.DeliveryIds)
            .IsEquivalentTo(["delivery_other", "delivery_new"]);
    }

    [Test]
    public async Task ExistingSend_SuppressesUnrelatedTaggedReplayAfterCorrelation(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("delivery_new")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            Event("turn.started", "delivery_new", """{"turnId":"turn_new"}"""),
            Event("message.completed", "delivery_old", """{"finishReason":"stop","message":"Old replay."}"""),
            """{"type":"message.completed","data":{"finishReason":"stop","message":"Global."}}""",
            Event("session.waiting", "delivery_new", """{"wait":"next-user-message"}"""))));
        EveSession session = CreateSession(transport);

        EveTurnOutcome outcome = await (await session.SendAsync(
            "Filter replay",
            cancellationToken)).GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(3);
        await Assert.That(outcome.Events.Any(streamEvent =>
            streamEvent.Data.TryGetProperty("message", out JsonElement message)
            && message.GetString() == "Old replay."))
            .IsFalse()
            .Because("Events tagged only to another delivery must not reach the response.");
        await Assert.That(outcome.Message).IsEqualTo("Global.");
        await Assert.That(session.State.StreamIndex).IsEqualTo(4);
    }

    [Test]
    public async Task ExistingSend_ReconnectsAfterAllConsumedStaleEvents(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("delivery_new")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            Event("turn.started", "delivery_old", """{"turnId":"turn_old"}"""),
            Event("message.completed", "delivery_old", """{"finishReason":"stop","message":"Old."}"""),
            Event("turn.completed", "delivery_old", """{"turnId":"turn_old"}"""),
            Event("session.waiting", "delivery_old", """{"wait":"next-user-message"}"""))));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            Event("turn.started", "delivery_new", """{"turnId":"turn_new"}"""),
            Event("session.waiting", "delivery_new", """{"wait":"next-user-message"}"""))));
        EveSession session = CreateSession(transport);

        EveTurnOutcome outcome = await (await session.SendAsync(
            EveMessageContent.FromText("Reconnect"),
            new EveTurnOptions
            {
                StreamReconnectPolicy = ZeroDelayReconnectPolicy(),
            },
            cancellationToken)).GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(2);
        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        await Assert.That(handler.Calls[2].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream?startIndex=4");
        await Assert.That(session.State.StreamIndex).IsEqualTo(6);
    }

    [Test]
    [Arguments("""{"ok":true,"sessionId":"session_1"}""")]
    [Arguments("""{"ok":true,"sessionId":"session_1","deliveryId":""}""")]
    public async Task ExistingSend_RejectsMissingOrEmptyAcceptedDeliveryIdentifier(
        string acceptedJson,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            acceptedJson)));
        EveSession session = CreateSession(transport);

        await Assert.That(async () => await session.SendAsync("Require ID", cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(session.State.StreamIndex).IsEqualTo(0);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExistingSend_FailsWhenSessionCompletesBeforeAcceptedDelivery(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("delivery_new")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            Event("turn.started", "delivery_old", """{"turnId":"turn_old"}"""),
            Event("session.completed", "delivery_old", "{}"))));
        EveSession session = CreateSession(transport);
        EveMessageResponse response = await session.SendAsync("Too late", cancellationToken);

        await Assert.That(async () => await response.GetOutcomeAsync(cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(session.State.StreamIndex).IsEqualTo(2);
    }

    [Test]
    public async Task ExistingSend_DisabledReconnectFailsBeforeAcceptedBoundary(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("delivery_new")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            Event("turn.started", "delivery_new", """{"turnId":"turn_new"}"""))));
        EveSession session = CreateSession(transport);
        EveMessageResponse response = await session.SendAsync(
            EveMessageContent.FromText("Incomplete"),
            new EveTurnOptions
            {
                StreamReconnectPolicy = EveStreamReconnectPolicy.Disabled,
            },
            cancellationToken);

        await Assert.That(async () => await response.GetOutcomeAsync(cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(session.State.StreamIndex).IsEqualTo(1);
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ExistingSend_SurfacesSessionFailureAfterCorrelation(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("delivery_new")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            Event("turn.started", "delivery_new", """{"turnId":"turn_new"}"""),
            Event("session.failed", "delivery_other", """{"error":"Session failed."}"""))));
        EveSession session = CreateSession(transport);

        EveTurnOutcome outcome = await (await session.SendAsync(
            "Observe failure",
            cancellationToken)).GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(2);
        await Assert.That(outcome.Events[^1].Kind).IsEqualTo(EveStreamEventKind.SessionFailed);
        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Failed);
    }

    [Test]
    public async Task ExistingSend_CancellationTargetsAcceptedTurnInsteadOfStaleTurn(
        CancellationToken cancellationToken)
    {
        TaskCompletionSource settle =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> cancelledTurnIds = [];
        EveMessageResponse response = new(
            "session_1",
            "delivery_new",
            true,
            streamCancellationToken => CorrelatedTurnStreamAsync(
                settle.Task,
                streamCancellationToken),
            (turnId, requestCancellationToken) =>
            {
                requestCancellationToken.ThrowIfCancellationRequested();
                cancelledTurnIds.Add(turnId);
                return Task.FromResult(new EveCancellationOutcome(
                    "session_1",
                    EveCancellationStatus.Accepted));
            },
            cancellationToken);
        Task<IReadOnlyList<EveStreamEvent>> consumption =
            CollectAsync(response, cancellationToken);

        EveCancellationOutcome cancellation =
            await response.CancelAsync(cancellationToken);

        await Assert.That(cancellation.Status).IsEqualTo(EveCancellationStatus.Accepted);
        await Assert.That(cancelledTurnIds).IsEquivalentTo(["turn_new"]);

        settle.SetResult();
        IReadOnlyList<EveStreamEvent> events = await consumption.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken);
        await Assert.That(events.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ExistingSend_RemainsAttachedAcrossCallbackAuthorizationParking(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("delivery_new")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            Event("turn.started", "delivery_new", """{"turnId":"turn_new"}"""),
            Event(
                "authorization.required",
                "delivery_new",
                """{"name":"github","webhookUrl":"https://agent.example.com/auth/github"}"""),
            Event("session.waiting", "delivery_new", """{"wait":"next-user-message"}"""),
            Event(
                "authorization.completed",
                "delivery_new",
                """{"name":"github","outcome":"authorized"}"""),
            Event("session.waiting", "delivery_new", """{"wait":"next-user-message"}"""))));
        EveSession session = CreateSession(transport);

        EveTurnOutcome outcome = await (await session.SendAsync(
            "Authorize",
            cancellationToken)).GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(5);
        await Assert.That(outcome.Events.Count(streamEvent =>
            streamEvent.Kind == EveStreamEventKind.SessionWaiting)).IsEqualTo(2);
        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Waiting);
    }

    [Test]
    public async Task InitialCreate_DoesNotRequireOrUseDeliveryCorrelation(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1"}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            Event("turn.started", "delivery_create", """{"turnId":"turn_1"}"""),
            Event(
                "session.waiting",
                "delivery_create",
                """{"wait":"next-user-message"}"""))));
        EveSession session = new EveClient(
            transport,
            new EveClientOptions("https://agent.example.com")).CreateSession();

        EveMessageResponse response = await session.SendAsync("Create", cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(response.DeliveryId).IsNull();
        await Assert.That(outcome.Events.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RespondAsync_DoesNotRequireOrUseDeliveryCorrelation(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1"}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            Event(
                "input.resolved",
                "delivery_prior",
                """{"resolutions":[],"sequence":1,"stepIndex":0,"turnId":"turn_1"}"""),
            Event(
                "session.waiting",
                "delivery_prior",
                """{"wait":"next-user-message"}"""))));
        EveSession session = CreateSession(transport);

        EveMessageResponse response = await session.RespondAsync(
            [new EveInputResponse("approval_1", "approve")],
            cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(response.DeliveryId).IsNull();
        await Assert.That(outcome.Events.Count).IsEqualTo(2);
    }

    private static EveSession CreateSession(HttpMessageInvoker transport) =>
        new EveClient(
            transport,
            new EveClientOptions("https://agent.example.com"))
        .AttachSession("session_1");

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

    private static HttpResponseMessage AcceptedResponse(string deliveryId) =>
        JsonResponse(
            HttpStatusCode.Accepted,
            $$"""{"ok":true,"sessionId":"session_1","deliveryId":"{{deliveryId}}"}""");

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage StreamResponse(params string[] events)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{string.Join('\n', events)}\n",
                Encoding.UTF8,
                EveProtocol.MessageStreamContentType),
        };
        response.Headers.TryAddWithoutValidation(
            EveProtocol.StreamVersionHeaderName,
            EveProtocol.MessageStreamVersion);
        return response;
    }

    private static async IAsyncEnumerable<EveStreamEvent> CorrelatedTurnStreamAsync(
        Task settle,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return EveStreamEvent.Parse(
            Event("turn.started", "delivery_old", """{"turnId":"turn_old"}"""));
        yield return EveStreamEvent.Parse(
            Event("turn.started", "delivery_new", """{"turnId":"turn_new"}"""));
        await settle.WaitAsync(cancellationToken);
        yield return EveStreamEvent.Parse(
            Event("session.waiting", "delivery_new", """{"wait":"next-user-message"}"""));
    }

    private static async Task<IReadOnlyList<EveStreamEvent>> CollectAsync(
        IAsyncEnumerable<EveStreamEvent> stream,
        CancellationToken cancellationToken)
    {
        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent streamEvent in
            stream.WithCancellation(cancellationToken))
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private static string Event(string type, string deliveryId, string data) =>
        EventWithDeliveryIds(type, $$"""["{{deliveryId}}"]""", data);

    private static string EventWithDeliveryIds(
        string type,
        string deliveryIds,
        string data) =>
        "{\"type\":\"" + type + "\",\"data\":" + data
        + ",\"meta\":{\"at\":\"2026-09-08T12:00:00.000Z\",\"deliveryIds\":"
        + deliveryIds + "}}";
}
