using System.Net;
using System.Text;
using System.Text.Json;

namespace NexusLabs.Eve.Tests;

public sealed class EveV27StreamTests
{
    [Test]
    public async Task Decoder_PreservesCommitScopeUnknownFactsAndPositions()
    {
        EveV27LineDecoder decoder = new(7);
        IReadOnlyList<EveStreamEvent> events = decoder.Decode(Commit(
            """{"type":"session.started","data":{"runtime":{"eveVersion":"0.84.0"}}}""",
            """{"type":"future.fact","scope":{"turnId":"turn","future":true},"data":{"retained":17}}"""));

        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events[0].Metadata!.Position).IsEqualTo(new EveStreamPosition(7, 0));
        await Assert.That(events[0].Metadata!.EndOfLine!.Value).IsFalse()
            .Because("The first fact does not finish its commit.");
        await Assert.That(events[1].Metadata!.Position).IsEqualTo(new EveStreamPosition(7, 1));
        await Assert.That(events[1].Metadata!.EndOfLine!.Value).IsTrue()
            .Because("The last fact completes its source line.");
        await Assert.That(events[1].Kind).IsEqualTo(EveStreamEventKind.Unknown);
        await Assert.That(events[1].Scope!.Value.GetProperty("future").GetBoolean())
            .IsTrue().Because("Unknown owner metadata remains available to future consumers.");
        await Assert.That(events[1].Data.GetProperty("retained").GetInt32()).IsEqualTo(17);
        await Assert.That(events[1].StreamVersion).IsEqualTo(27);
        await Assert.That(decoder.NextLine).IsEqualTo(8);
    }

    [Test]
    public async Task Decoder_TransportRecordsDoNotConsumeSourcePositions()
    {
        EveV27LineDecoder decoder = new(0);
        decoder.Decode(Commit("""{"type":"future.fact","data":{}}"""));
        IReadOnlyList<EveStreamEvent> heartbeat = decoder.Decode("""{"$eve":"heartbeat"}""");
        decoder.Decode("""{"$eve":"future.transport","value":12}""");
        decoder.Decode("""{"$eve":"position","next":6}""");
        IReadOnlyList<EveStreamEvent> progress = decoder.Decode(
            """{"progress":{"type":"content.delta","scope":{"turnId":"turn"},"data":{"partId":"part","kind":"text","delta":"Hi"}}}""");
        decoder.Decode("""{"$eve":"stream.lease-ended"}""");
        decoder.Decode("""{"$eve":"stream.ended"}""");

        await Assert.That(heartbeat.Count).IsEqualTo(0);
        await Assert.That(progress.Count).IsEqualTo(1);
        await Assert.That(progress[0].Metadata!.Position).IsEqualTo(new EveStreamPosition(6, 0));
        await Assert.That(progress[0].Metadata!.At).IsEqualTo("2026-10-27T00:00:00.000Z");
        await Assert.That(decoder.NextLine).IsEqualTo(7);
        await Assert.That(decoder.LeaseEnded).IsTrue().Because("Unversioned v27 lease markers are transport records.");
        await Assert.That(decoder.StreamEnded).IsTrue().Because("An explicit stream end terminates following.");
    }

    [Test]
    [Arguments("""{"type":"session.completed"}""")]
    [Arguments("""{"at":"now","facts":[]}""")]
    [Arguments("""{"at":"now","facts":[{"data":{}}]}""")]
    [Arguments("""{"$eve":"position","next":-1}""")]
    [Arguments("""{"$eve":"position","next":"bad"}""")]
    public async Task Decoder_RejectsMalformedOrLegacyLines(string json)
    {
        EveV27LineDecoder decoder = new(0);
        await Assert.That(() => decoder.Decode(json)).Throws<EveProtocolException>();
    }

    [Test]
    public async Task PartialCommit_PersistsAndResumesWithoutSkippingOrRepeatingFacts(
        CancellationToken cancellationToken)
    {
        string body = Commit(
            """{"type":"future.first","data":{}}""",
            """{"type":"future.second","data":{}}""");
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(EveTestResponses.StreamResponse(body, 0, "27")));
        handler.Enqueue((_, _) => Task.FromResult(EveTestResponses.StreamResponse(body, 0, "27")));
        EveClient client = Client(transport);
        EveSession first = client.AttachSession("session");
        await using (IAsyncEnumerator<EveStreamEvent> reader = first.StreamAsync(
            new EveStreamOptions { Follow = false }, cancellationToken).GetAsyncEnumerator(cancellationToken))
        {
            await Assert.That(await reader.MoveNextAsync()).IsTrue().Because("The first fact is available.");
            await Assert.That(reader.Current.Type).IsEqualTo("future.first");
            await Assert.That(first.State.StreamIndex).IsEqualTo(0);
            await Assert.That(first.State.StreamEventIndex).IsEqualTo(1);
            await Assert.That(first.State.StreamVersion).IsEqualTo(27);
        }

        EveSession resumed = client.CreateSession(first.State);
        List<EveStreamEvent> remaining = [];
        await foreach (EveStreamEvent fact in resumed.StreamAsync(
            new EveStreamOptions { Follow = false }, cancellationToken))
        {
            remaining.Add(fact);
        }

        await Assert.That(remaining.Count).IsEqualTo(1);
        await Assert.That(remaining[0].Type).IsEqualTo("future.second");
        await Assert.That(resumed.State.StreamIndex).IsEqualTo(1);
        await Assert.That(resumed.State.StreamEventIndex).IsEqualTo(0);
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
    }

    [Test]
    public async Task LeaseReconnect_UsesLinesAndPreservesProgressTimestampAndFixedTail(
        CancellationToken cancellationToken)
    {
        string first = Commit("""{"type":"future.first","data":{}}""")
            + """{"$eve":"stream.lease-ended"}""" + "\n";
        string second =
            """{"progress":{"type":"content.delta","data":{"partId":"part","delta":"Hi"}}}"""
            + "\n" + Commit(
                """{"type":"session.ended","data":{"outcome":"completed"}}""",
                """{"type":"future.after-terminal","data":{}}""")
            + Commit("""{"type":"future.outside-bound","data":{}}""");
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(EveTestResponses.StreamResponse(first, 2, "27")));
        handler.Enqueue((_, _) => Task.FromResult(EveTestResponses.StreamResponse(second, 99, "27")));
        EveSession session = Client(transport).AttachSession("session");
        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent fact in session.StreamAsync(
            new EveStreamOptions { Follow = false }, cancellationToken))
        {
            events.Add(fact);
        }

        await Assert.That(events.Count).IsEqualTo(4);
        await Assert.That(events[1].Metadata!.Position).IsEqualTo(new EveStreamPosition(1, 0));
        await Assert.That(events[1].Metadata!.At).IsEqualTo("2026-10-27T00:00:00.000Z");
        await Assert.That(events[^1].Type).IsEqualTo("future.after-terminal");
        await Assert.That(session.State.StreamIndex).IsEqualTo(3);
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(handler.Calls[1].Uri).Contains("startIndex=1");
        await Assert.That(handler.Calls[1].Uri.Contains("includeTailIndex", StringComparison.Ordinal))
            .IsFalse().Because("Reconnects retain the initial bound rather than recapturing a moving tail.");
    }

    [Test]
    public async Task PositionCoverage_CompletesBoundedReadWithoutReadingPastTail(
        CancellationToken cancellationToken)
    {
        string body = """{"$eve":"position","next":6}""" + "\n"
            + Commit("""{"type":"future.outside-bound","data":{}}""");
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(EveTestResponses.StreamResponse(body, 5, "27")));
        EveSession session = Client(transport).AttachSession("session");
        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent fact in session.StreamAsync(
            new EveStreamOptions { Follow = false }, cancellationToken))
        {
            events.Add(fact);
        }

        await Assert.That(events.Count).IsEqualTo(0);
        await Assert.That(session.State.StreamIndex).IsEqualTo(6);
        await Assert.That(session.State.StreamVersion).IsEqualTo(27);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task StreamEnded_StopsBeforeAnOpenTransportBecomesIdle(
        CancellationToken cancellationToken)
    {
        using IdleAfterPrefixStream stream = new(Encoding.UTF8.GetBytes(
            """{"$eve":"heartbeat"}""" + "\n" + """{"$eve":"stream.ended"}""" + "\n"));
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Headers.TryAddWithoutValidation(EveProtocol.StreamVersionHeaderName, "27");
            return Task.FromResult(response);
        });
        EveSession session = Client(transport).AttachSession("session");
        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent fact in session.StreamAsync(cancellationToken))
        {
            events.Add(fact);
        }

        await Assert.That(events.Count).IsEqualTo(0);
        await Assert.That(stream.IdleReadStarted.IsCompleted).IsFalse()
            .Because("The explicit transport end is not an idle-timeout reconnect.");
        await Assert.That(stream.IsDisposed).IsTrue().Because("Completed readers release their transport.");
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task BoundedVersion27_UsesTheExactConsecutiveDisconnectBudget(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(EveTestResponses.StreamResponse("", 0, "27")));
        handler.Enqueue(static (_, _) => Task.FromResult(EveTestResponses.StreamResponse("", 0, "27")));
        EveSession session = Client(transport).AttachSession("session");
        EveStreamOptions options = new()
        {
            Follow = false,
            ReconnectPolicy = new EveStreamReconnectPolicy
            {
                StreamOpenRetry = new EveRetryPolicy { MaxAttempts = 2 },
                StreamIdleRetry = new EveRetryPolicy { BaseDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero },
            },
        };

        await Assert.That(async () =>
        {
            await foreach (EveStreamEvent fact in session.StreamAsync(options, cancellationToken))
            {
                throw new InvalidOperationException($"Unexpected fact {fact.Type} in an empty disconnected stream.");
            }
        }).Throws<EveProtocolException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
    }

    [Test]
    public async Task TailRelativeVersion27_ResolvesToAnAbsoluteBoundedCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(EveTestResponses.StreamResponse(
            Commit("""{"type":"future.tail","data":{}}"""), 8, "27")));
        EveSession session = Client(transport).CreateSession(new EveSessionState
        {
            SessionId = "session",
            StreamVersion = 27,
        });
        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent fact in session.StreamAsync(
            new EveStreamOptions { Follow = false, StartIndex = -1 }, cancellationToken))
        {
            events.Add(fact);
        }

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(events[0].Metadata!.Position).IsEqualTo(new EveStreamPosition(8, 0));
        await Assert.That(session.State.StreamIndex).IsEqualTo(9);
    }

    private static EveClient Client(HttpMessageInvoker transport) =>
        new(transport, new EveClientOptions("https://agent.example.com"));

    private static string Commit(params string[] facts) =>
        """{"at":"2026-10-27T00:00:00.000Z","facts":[""" + string.Join(",", facts) + "]}\n";
}
