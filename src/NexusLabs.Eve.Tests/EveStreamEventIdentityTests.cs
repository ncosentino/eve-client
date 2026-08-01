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
            new EveSendTurnRequest
            {
                Message = EveMessageContent.FromText("Reconnect"),
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
