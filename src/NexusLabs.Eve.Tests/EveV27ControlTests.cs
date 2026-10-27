using System.Net;
using System.Text;
using System.Text.Json;

namespace NexusLabs.Eve.Tests;

public sealed class EveV27ControlTests
{
    [Test]
    [Arguments("cancel")]
    [Arguments("clear")]
    [Arguments("compact")]
    [Arguments("reset")]
    public async Task Controls_ReturnAdmissionReceiptsAndObserveDurableApplication(
        string operation,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(Receipt()));
        handler.Enqueue(static (_, _) => Task.FromResult(EveTestResponses.StreamResponse(
            """
            {"at":"2026-10-27T00:00:00Z","facts":[{"type":"delivery.admitted","data":{"deliveryId":"control"}},{"type":"delivery.settled","data":{"deliveryId":"control","outcome":"applied"}}]}
            """, streamVersion: "27")));
        EveSession session = new EveClient(transport, new EveClientOptions("https://agent.example.com"))
            .AttachSession("session");
        string? deliveryId;
        switch (operation)
        {
            case "cancel":
                EveCancellationOutcome cancel = await session.CancelAsync(cancellationToken);
                await Assert.That(cancel.Status).IsEqualTo(EveCancellationStatus.Accepted);
                deliveryId = cancel.DeliveryId;
                break;
            case "clear":
                EveClearOutcome clear = await session.ClearAsync(cancellationToken);
                await Assert.That(clear.Status).IsEqualTo(EveClearStatus.Accepted);
                deliveryId = clear.DeliveryId;
                break;
            case "compact":
                EveCompactOutcome compact = await session.CompactAsync(cancellationToken);
                await Assert.That(compact.Status).IsEqualTo(EveCompactStatus.Accepted);
                deliveryId = compact.DeliveryId;
                break;
            default:
                EveResetOutcome reset = await session.ResetAsync(cancellationToken);
                await Assert.That(reset.Status).IsEqualTo(EveResetStatus.Accepted);
                await Assert.That(reset.PreviousSessionId).IsNull()
                    .Because("Admission alone must not claim that the session is already retired.");
                await Assert.That(reset.SessionId).IsEqualTo("session");
                deliveryId = reset.DeliveryId;
                break;
        }

        await Assert.That(deliveryId).IsEqualTo("control");
        await Assert.That(session.State.StreamVersion).IsEqualTo(27);
        await Assert.That(session.State.SessionId).IsEqualTo("session");
        EveTurnOutcome applied = await session.GetDeliveryOutcomeAsync(deliveryId!, cancellationToken);
        await Assert.That(applied.Status).IsEqualTo(EveTurnStatus.Completed);
        await Assert.That(applied.Message).IsNull();
        await Assert.That(applied.DeliverySettlement!.Value.GetProperty("outcome").GetString())
            .IsEqualTo("applied");
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            $"https://agent.example.com/eve/v1/session/session/{operation}");
    }

    [Test]
    public async Task RespondAsync_UsesLearnedVersion27AnswerNames(CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(Receipt()));
        EveSession session = new EveClient(transport, new EveClientOptions("https://agent.example.com"))
            .CreateSession(new EveSessionState { SessionId = "session", StreamVersion = 27 });

        await session.RespondAsync([new EveInputResponse("interaction", "approve", "yes")], cancellationToken);

        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        using JsonDocument body = JsonDocument.Parse(handler.Calls[0].Body!);
        await Assert.That(body.RootElement.TryGetProperty("inputResponses", out _)).IsFalse()
            .Because("Version 27 rejects unknown legacy top-level fields.");
        JsonElement answers = body.RootElement.GetProperty("answers");
        await Assert.That(answers.GetArrayLength()).IsEqualTo(1);
        await Assert.That(answers[0].GetProperty("interactionId").GetString()).IsEqualTo("interaction");
        await Assert.That(answers[0].GetProperty("optionId").GetString()).IsEqualTo("approve");
        await Assert.That(answers[0].GetProperty("text").GetString()).IsEqualTo("yes");
    }

    [Test]
    public async Task AnswerAsync_SelectsVersion27ExplicitlyForAnUnknownAttachedHandle(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(Receipt()));
        EveSession session = new EveClient(transport, new EveClientOptions("https://agent.example.com"))
            .AttachSession("session");

        await session.AnswerAsync([new EveInteractionAnswer("interaction", text: "answer")], cancellationToken);

        await Assert.That(session.State.StreamVersion).IsEqualTo(27);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        using JsonDocument body = JsonDocument.Parse(handler.Calls[0].Body!);
        JsonElement answers = body.RootElement.GetProperty("answers");
        await Assert.That(answers.GetArrayLength()).IsEqualTo(1);
        await Assert.That(answers[0].GetProperty("interactionId").GetString()).IsEqualTo("interaction");
    }

    [Test]
    public async Task CancelOwnedTasks_IsNotSilentlyDiscardedForVersion27(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSession session = new EveClient(transport, new EveClientOptions("https://agent.example.com"))
            .CreateSession(new EveSessionState { SessionId = "session", StreamVersion = 27 });

        await Assert.That(async () => await session.CancelAsync(
            new EveCancellationOptions { CancelOwnedTasks = true }, cancellationToken))
            .Throws<ArgumentException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("""{"ok":true,"sessionId":"foreign","deliveryId":"control"}""")]
    [Arguments("""{"ok":true,"sessionId":"session","deliveryId":""}""")]
    [Arguments("""{"ok":true,"sessionId":"session"}""")]
    [Arguments("""{"ok":false,"sessionId":"session","deliveryId":"control"}""")]
    public async Task ControlReceipts_RejectMalformedOrForeignIdentity(
        string body,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));
        EveSession session = new EveClient(transport, new EveClientOptions("https://agent.example.com"))
            .AttachSession("session");

        await Assert.That(async () => await session.ClearAsync(cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ChildReader_UsesRecordedRootAuthorityRatherThanAnEventUrl(
        CancellationToken cancellationToken)
    {
        EveV27LineDecoder decoder = new(12);
        EveStreamEvent child = decoder.Decode(
            """
            {"at":"2026-10-27T00:00:00Z","facts":[{"type":"child.opened","data":{"sessionId":"child","name":"worker","owner":{"callId":"call"},"stream":"https://untrusted.example/stream"}}]}
            """)[0];
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(EveTestResponses.StreamResponse(
            """{"$eve":"stream.ended"}""" + "\n", streamVersion: "27")));
        EveSession parent = new EveClient(transport, new EveClientOptions("https://agent.example.com")
        {
            Authentication = new EveBearerAuthentication("fixture-token"),
        }).AttachSession("root");
        EveSessionState before = parent.State;
        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent fact in parent.StreamSubagentAsync(child, cancellationToken))
        {
            events.Add(fact);
        }

        await Assert.That(events.Count).IsEqualTo(0);
        await Assert.That(parent.State).IsEqualTo(before);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        Uri request = new(handler.Calls[0].Uri);
        await Assert.That(request.Host).IsEqualTo("agent.example.com");
        await Assert.That(request.AbsolutePath).IsEqualTo("/eve/v1/session/root/descendants/stream");
        await Assert.That(handler.Calls[0].Headers["authorization"]).IsEqualTo("Bearer fixture-token");
        string encoded = request.Query.TrimStart('?').Split('&')
            .Single(static value => value.StartsWith("children=", StringComparison.Ordinal));
        using JsonDocument hops = JsonDocument.Parse(Uri.UnescapeDataString(encoded["children=".Length..]));
        await Assert.That(hops.RootElement.GetArrayLength()).IsEqualTo(1);
        await Assert.That(hops.RootElement[0].GetProperty("sessionId").GetString()).IsEqualTo("child");
        await Assert.That(hops.RootElement[0].GetProperty("position").GetInt32()).IsEqualTo(12);
    }

    private static HttpResponseMessage Receipt() => new(HttpStatusCode.Accepted)
    {
        Content = new StringContent(
            """{"ok":true,"sessionId":"session","deliveryId":"control"}""",
            Encoding.UTF8, "application/json"),
    };
}
