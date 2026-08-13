using System.Text;

namespace NexusLabs.Eve.Tests;

public sealed class EveStreamEventIdentityTests
{
    private const string StampedEvent =
        """{"type":"turn.started","data":{"sequence":1,"turnId":"turn_1"},"meta":{"at":"2026-07-29T20:42:07.123Z","id":"evt_01K1AWQZ8N0000000000000000"}}""";

    [Test]
    public async Task Parse_ProjectsStampedIdentifierAndTimestamp()
    {
        EveStreamEvent streamEvent = EveStreamEvent.Parse(StampedEvent);

        await Assert.That(streamEvent.Metadata).IsNotNull();
        await Assert.That(streamEvent.Metadata!.At).IsEqualTo("2026-07-29T20:42:07.123Z");
        await Assert.That(streamEvent.Metadata.Id).IsEqualTo("evt_01K1AWQZ8N0000000000000000");
    }

    [Test]
    public async Task Parse_ReportsNoIdentifierForEventPersistedBeforeProtocol20()
    {
        EveStreamEvent streamEvent = EveStreamEvent.Parse(
            """{"type":"turn.started","data":{"turnId":"turn_1"},"meta":{"at":"2026-07-29T20:42:07.123Z"}}""");

        await Assert.That(streamEvent.Metadata).IsNotNull();
        await Assert.That(streamEvent.Metadata!.At).IsEqualTo("2026-07-29T20:42:07.123Z");
        await Assert.That(streamEvent.Metadata.Id).IsNull();
    }

    [Test]
    [Arguments("\"\"")]
    [Arguments("\"   \"")]
    [Arguments("17")]
    [Arguments("null")]
    public async Task Parse_ReportsNoIdentifierForUnusableIdentifierValue(string identifierJson)
    {
        EveStreamEvent streamEvent = EveStreamEvent.Parse(
            """{"type":"turn.started","data":{},"meta":{"at":"2026-07-29T20:42:07.123Z","id":"""
            + identifierJson
            + "}}");

        await Assert.That(streamEvent.Metadata).IsNotNull();
        await Assert.That(streamEvent.Metadata!.Id).IsNull();
    }

    [Test]
    public async Task Parse_ReportsNoMetadataForNestedSubagentEventWithoutEnvelope()
    {
        EveStreamEvent streamEvent = EveStreamEvent.Parse(
            """{"type":"subagent.event","data":{"callId":"call_1","subagentName":"reviewer","event":{"type":"turn.started","data":{}}}}""");

        await Assert.That(streamEvent.Kind).IsEqualTo(EveStreamEventKind.SubagentEvent);
        await Assert.That(streamEvent.Metadata).IsNull();
    }

    [Test]
    public async Task Parse_RecognizesActionPartialAndPreservesPayload()
    {
        EveStreamEvent streamEvent = EveStreamEvent.Parse(
            """{"type":"action.partial","data":{"result":{"output":"partial output"},"sequence":4,"stepIndex":0,"turnId":"turn_1"}}""");

        await Assert.That(streamEvent.Kind).IsEqualTo(EveStreamEventKind.ActionPartial);
        await Assert.That(streamEvent.Type).IsEqualTo("action.partial");
        await Assert.That(streamEvent.Data.GetProperty("result")
            .GetProperty("output")
            .GetString())
            .IsEqualTo("partial output");
        await Assert.That(streamEvent.Data.GetProperty("turnId").GetString())
            .IsEqualTo("turn_1");
        await Assert.That(streamEvent.IsCurrentTurnBoundary)
            .IsFalse()
            .Because("A preliminary snapshot never ends a turn.");
    }

    [Test]
    public async Task Parse_KeepsActionPartialDistinctFromTerminalActionResult()
    {
        EveStreamEvent partial = EveStreamEvent.Parse(
            """{"type":"action.partial","data":{"result":{"output":"provisional"},"sequence":1,"stepIndex":0,"turnId":"turn_1"}}""");
        EveStreamEvent terminal = EveStreamEvent.Parse(
            """{"type":"action.result","data":{"result":{"output":"final"},"sequence":2,"stepIndex":0,"turnId":"turn_1"}}""");

        await Assert.That(partial.Kind).IsEqualTo(EveStreamEventKind.ActionPartial);
        await Assert.That(terminal.Kind).IsEqualTo(EveStreamEventKind.ActionResult);
        await Assert.That(partial.Kind).IsNotEqualTo(terminal.Kind);
    }

    [Test]
    [Arguments("pending")]
    [Arguments("rejected")]
    [Arguments("failed")]
    [Arguments("timed-out")]
    [Arguments("stale")]
    public async Task Parse_RecognizesApprovalCandidateOutcomes(string outcome)
    {
        EveStreamEvent streamEvent = EveStreamEvent.Parse(
            $$$"""{"type":"approval.candidate","data":{"candidateId":"cand_1","outcome":"{{{outcome}}}","requestId":"approval_1","responderPrincipalId":"user_1","sequence":3,"stepIndex":0,"turnId":"turn_1"}}""");

        await Assert.That(streamEvent.Kind).IsEqualTo(EveStreamEventKind.ApprovalCandidate);
        await Assert.That(streamEvent.Type).IsEqualTo("approval.candidate");
        await Assert.That(streamEvent.Data.GetProperty("outcome").GetString()).IsEqualTo(outcome);
        await Assert.That(streamEvent.Data.GetProperty("candidateId").GetString())
            .IsEqualTo("cand_1");
        await Assert.That(streamEvent.IsCurrentTurnBoundary)
            .IsFalse()
            .Because("An approval candidate never ends a turn.");
    }

    [Test]
    public async Task Parse_OmitsApprovalCandidateReasonWhenAbsent()
    {
        EveStreamEvent withReason = EveStreamEvent.Parse(
            """{"type":"approval.candidate","data":{"candidateId":"cand_1","outcome":"rejected","reason":"responder declined","requestId":"approval_1","responderPrincipalId":"user_1","sequence":3,"stepIndex":0,"turnId":"turn_1"}}""");
        EveStreamEvent withoutReason = EveStreamEvent.Parse(
            """{"type":"approval.candidate","data":{"candidateId":"cand_1","outcome":"pending","requestId":"approval_1","responderPrincipalId":"user_1","sequence":3,"stepIndex":0,"turnId":"turn_1"}}""");

        await Assert.That(withReason.Data.GetProperty("reason").GetString())
            .IsEqualTo("responder declined");
        await Assert.That(withoutReason.Data.TryGetProperty("reason", out _))
            .IsFalse()
            .Because("An absent reason stays absent in the raw payload.");
    }

    [Test]
    [Arguments("approved")]
    [Arguments("cancelled")]
    public async Task Parse_RecognizesApprovalSettledOutcomes(string outcome)
    {
        EveStreamEvent streamEvent = EveStreamEvent.Parse(
            $$$"""{"type":"approval.settled","data":{"outcome":"{{{outcome}}}","requestId":"approval_1","responderPrincipalId":"user_1","sequence":4,"stepIndex":0,"turnId":"turn_1"}}""");

        await Assert.That(streamEvent.Kind).IsEqualTo(EveStreamEventKind.ApprovalSettled);
        await Assert.That(streamEvent.Type).IsEqualTo("approval.settled");
        await Assert.That(streamEvent.Data.GetProperty("outcome").GetString()).IsEqualTo(outcome);
        await Assert.That(streamEvent.Data.GetProperty("requestId").GetString())
            .IsEqualTo("approval_1");
        await Assert.That(streamEvent.IsCurrentTurnBoundary)
            .IsFalse()
            .Because("An approval settlement never ends a turn.");
    }

    [Test]
    public async Task Parse_KeepsApprovalLifecycleKindsDistinct()
    {
        EveStreamEvent candidate = EveStreamEvent.Parse(
            """{"type":"approval.candidate","data":{"candidateId":"cand_1","outcome":"pending","requestId":"approval_1","responderPrincipalId":"user_1","sequence":3,"stepIndex":0,"turnId":"turn_1"}}""");
        EveStreamEvent settled = EveStreamEvent.Parse(
            """{"type":"approval.settled","data":{"outcome":"approved","requestId":"approval_1","responderPrincipalId":"user_1","sequence":4,"stepIndex":0,"turnId":"turn_1"}}""");
        EveStreamEvent inputRequested = EveStreamEvent.Parse(
            """{"type":"input.requested","data":{"requests":[],"sequence":2,"stepIndex":0,"turnId":"turn_1"}}""");

        await Assert.That(candidate.Kind).IsNotEqualTo(settled.Kind);
        await Assert.That(candidate.Kind).IsNotEqualTo(inputRequested.Kind);
        await Assert.That(settled.Kind).IsNotEqualTo(inputRequested.Kind);
    }

    [Test]
    public async Task Parse_PreservesAuthorizationCandidateCorrelation()
    {
        EveStreamEvent correlated = EveStreamEvent.Parse(
            """{"type":"authorization.required","data":{"candidateId":"cand_1","connectionName":"github","sequence":5,"stepIndex":0,"turnId":"turn_1"}}""");
        EveStreamEvent uncorrelated = EveStreamEvent.Parse(
            """{"type":"authorization.required","data":{"connectionName":"github","sequence":5,"stepIndex":0,"turnId":"turn_1"}}""");

        await Assert.That(correlated.Kind).IsEqualTo(EveStreamEventKind.AuthorizationRequired);
        await Assert.That(correlated.Data.GetProperty("candidateId").GetString())
            .IsEqualTo("cand_1");
        await Assert.That(uncorrelated.Data.TryGetProperty("candidateId", out _))
            .IsFalse()
            .Because("An absent candidate correlation stays absent.");
    }

    [Test]
    public async Task Deduplicator_DropsReplayedEventAndKeepsRetriedEmission()
    {
        EveStreamEventDeduplicator deduplicator = new();
        EveStreamEvent original = EveStreamEvent.Parse(StampedEvent);
        EveStreamEvent replay = EveStreamEvent.Parse(StampedEvent);
        EveStreamEvent retry = EveStreamEvent.Parse(
            """{"type":"turn.started","data":{"sequence":1,"turnId":"turn_1"},"meta":{"at":"2026-07-29T20:42:09.400Z","id":"evt_01K1AWQZ8N0000000000000001"}}""");

        await Assert.That(deduplicator.Admit(original))
            .IsTrue()
            .Because("The first emission of an identifier has not been seen.");
        await Assert.That(deduplicator.Admit(replay))
            .IsFalse()
            .Because("A replayed event repeats its durable identifier.");
        await Assert.That(deduplicator.Admit(retry))
            .IsTrue()
            .Because("A retried step is emitted under a new identifier.");
        await Assert.That(deduplicator.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Deduplicator_AdmitsEveryEventThatCarriesNoIdentifier()
    {
        EveStreamEventDeduplicator deduplicator = new();
        EveStreamEvent legacy = EveStreamEvent.Parse(
            """{"type":"turn.started","data":{},"meta":{"at":"2026-07-29T20:42:07.123Z"}}""");
        EveStreamEvent unstamped = EveStreamEvent.Parse(
            """{"type":"turn.started","data":{}}""");

        await Assert.That(deduplicator.Admit(legacy))
            .IsTrue()
            .Because("An event persisted before protocol 20 has nothing to deduplicate on.");
        await Assert.That(deduplicator.Admit(legacy))
            .IsTrue()
            .Because("Repeated id-less events cannot be recognized as duplicates.");
        await Assert.That(deduplicator.Admit(unstamped)).IsTrue();
        await Assert.That(deduplicator.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Deduplicator_ClearForgetsAdmittedIdentifiers()
    {
        EveStreamEventDeduplicator deduplicator = new();
        EveStreamEvent streamEvent = EveStreamEvent.Parse(StampedEvent);
        deduplicator.Admit(streamEvent);

        deduplicator.Clear();

        await Assert.That(deduplicator.Count).IsEqualTo(0);
        await Assert.That(deduplicator.Admit(streamEvent))
            .IsTrue()
            .Because("A cleared deduplicator starts a new session with no history.");
    }

    [Test]
    public async Task Deduplicator_RejectsNullEvent()
    {
        EveStreamEventDeduplicator deduplicator = new();

        await Assert.That(() => deduplicator.Admit(null!)).Throws<ArgumentNullException>();
    }

    [Test]
    public async Task TurnStream_PreservesIdentifiersWhenAReconnectReplaysHandledEvents(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(StampedEvent)));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            StampedEvent,
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"},"meta":{"at":"2026-07-29T20:42:08.900Z","id":"evt_01K1AWQZ8N0000000000000002"}}""")));
        EveSession session = new EveClient(
            transport,
            new EveClientOptions("https://agent.example.com")).CreateSession();

        EveMessageResponse response = await session.SendAsync(
            EveMessageContent.FromText("Reconnect"),
            new EveTurnOptions
            {
                StreamReconnectPolicy = new EveStreamReconnectPolicy
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
                },
            },
            cancellationToken);

        EveStreamEventDeduplicator deduplicator = new();
        List<EveStreamEvent> admitted = [];
        List<string?> observedIdentifiers = [];
        await foreach (EveStreamEvent streamEvent in response.WithCancellation(cancellationToken))
        {
            observedIdentifiers.Add(streamEvent.Metadata?.Id);
            if (deduplicator.Admit(streamEvent))
            {
                admitted.Add(streamEvent);
            }
        }

        await Assert.That(observedIdentifiers.Count)
            .IsEqualTo(3)
            .Because("The client delivers every event the server replays.");
        await Assert.That(observedIdentifiers[0]).IsEqualTo("evt_01K1AWQZ8N0000000000000000");
        await Assert.That(observedIdentifiers[1]).IsEqualTo("evt_01K1AWQZ8N0000000000000000");
        await Assert.That(observedIdentifiers[2]).IsEqualTo("evt_01K1AWQZ8N0000000000000002");
        await Assert.That(admitted.Count).IsEqualTo(2);
        await Assert.That(admitted[0].Kind).IsEqualTo(EveStreamEventKind.TurnStarted);
        await Assert.That(admitted[1].Kind).IsEqualTo(EveStreamEventKind.SessionWaiting);
    }

    private static HttpResponseMessage AcceptedResponse() =>
        new(System.Net.HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                """{"ok":true,"sessionId":"session_1","continuationToken":"eve:accepted"}""",
                Encoding.UTF8,
                "application/json"),
        };

    private static HttpResponseMessage StreamResponse(params string[] events) =>
        new(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{string.Join('\n', events)}\n",
                Encoding.UTF8,
                EveProtocol.MessageStreamContentType),
        };
}
