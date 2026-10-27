using System.Runtime.CompilerServices;
using System.Text.Json;

namespace NexusLabs.Eve.Tests;

public sealed class EveV27OutcomeTests
{
    [Test]
    [Arguments("applied", EveTurnStatus.Completed)]
    [Arguments("ignored", EveTurnStatus.Completed)]
    [Arguments("refused", EveTurnStatus.Failed)]
    [Arguments("failed", EveTurnStatus.Failed)]
    public async Task NonConversationalDeliveryDoesNotLeakReply(
        string deliveryOutcome,
        EveTurnStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        EveStreamEvent[] facts =
        [
            Fact("delivery.admitted", EveStreamEventKind.DeliveryAdmitted,
                """{"deliveryId":"d"}"""),
            Fact("content.completed", EveStreamEventKind.ContentCompleted,
                """{"partId":"text","kind":"text","value":"Not its reply"}"""),
            Fact("content.completed", EveStreamEventKind.ContentCompleted,
                """{"partId":"result","kind":"result","value":{"secret":true}}""", turnId: "t"),
            Fact("turn.settled", EveStreamEventKind.TurnSettled,
                """{"turnId":"t","outcome":"completed","reply":["text","result"]}"""),
            Fact("delivery.settled", EveStreamEventKind.DeliverySettled,
                $$"""{"deliveryId":"d","turnId":"t","outcome":"{{deliveryOutcome}}"}"""),
        ];
        EveTurnOutcome outcome = await ResponseAsync(facts, cancellationToken)
            .GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(expectedStatus);
        await Assert.That(outcome.DeliveryOutcome).IsEqualTo(deliveryOutcome);
        await Assert.That(outcome.Message).IsNull();
        await Assert.That(outcome.Data).IsNull();
        await Assert.That(outcome.Events.Count).IsEqualTo(5);
    }

    [Test]
    public async Task DrainsSettlementCommitAndSelectsMatchedTurnReply(
        CancellationToken cancellationToken)
    {
        EveStreamEvent[] facts =
        [
            Fact("delivery.admitted", EveStreamEventKind.DeliveryAdmitted,
                """{"deliveryId":"d"}"""),
            Fact("delivery.consumed", EveStreamEventKind.DeliveryConsumed,
                """{"deliveryId":"d","turnId":"t"}"""),
            Fact("content.completed", EveStreamEventKind.ContentCompleted,
                """{"partId":"n","kind":"text","value":"Narration"}"""),
            Fact("content.completed", EveStreamEventKind.ContentCompleted,
                """{"partId":"a","kind":"text","value":"First"}"""),
            Fact("content.completed", EveStreamEventKind.ContentCompleted,
                """{"partId":"b","kind":"text","value":"Second"}"""),
            Fact("content.completed", EveStreamEventKind.ContentCompleted,
                """{"partId":"r","kind":"result","value":{"answer":42}}""", turnId: "t"),
            Fact("content.completed", EveStreamEventKind.ContentCompleted,
                """{"partId":"other","kind":"result","value":{"answer":0}}""", turnId: "other"),
            Fact("delivery.settled", EveStreamEventKind.DeliverySettled,
                """{"deliveryId":"d","outcome":"handled"}""", endOfLine: false),
            Fact("turn.settled", EveStreamEventKind.TurnSettled,
                """{"turnId":"t","outcome":"completed","reply":["a","b"]}""", endOfLine: false),
            Fact("turn.settled", EveStreamEventKind.TurnSettled,
                """{"turnId":"other","outcome":"failed","reply":["n"]}""", endOfLine: false),
            Fact("future.fact", EveStreamEventKind.Unknown,
                """{"future":{"retained":true}}"""),
            Fact("future.after", EveStreamEventKind.Unknown, "{}"),
        ];
        EveTurnOutcome outcome = await ResponseAsync(facts, cancellationToken)
            .GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(11);
        await Assert.That(outcome.Message).IsEqualTo("First\nSecond");
        await Assert.That(outcome.Data!.Value.GetProperty("answer").GetInt32()).IsEqualTo(42);
        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Completed);
        await Assert.That(outcome.Events[^1].Type).IsEqualTo("future.fact");
        await Assert.That(outcome.Events[^1].Data.GetProperty("future")
            .GetProperty("retained").GetBoolean()).IsTrue()
            .Because("Unknown facts remain available through raw data.");
    }

    [Test]
    [Arguments("cancelled", EveTurnStatus.Cancelled)]
    [Arguments("failed", EveTurnStatus.Failed)]
    [Arguments("completed", EveTurnStatus.Completed)]
    public async Task HandledDeliveryUsesItsOwnTurnOutcome(
        string turnOutcome,
        EveTurnStatus expectedStatus,
        CancellationToken cancellationToken)
    {
        EveStreamEvent[] facts =
        [
            Fact("delivery.admitted", EveStreamEventKind.DeliveryAdmitted,
                """{"deliveryId":"d"}"""),
            Fact("turn.settled", EveStreamEventKind.TurnSettled,
                $$"""{"turnId":"t","outcome":"{{turnOutcome}}"}"""),
            Fact("turn.settled", EveStreamEventKind.TurnSettled,
                """{"turnId":"unrelated","outcome":"failed"}"""),
            Fact("delivery.settled", EveStreamEventKind.DeliverySettled,
                """{"deliveryId":"unrelated","outcome":"refused"}"""),
            Fact("delivery.settled", EveStreamEventKind.DeliverySettled,
                """{"deliveryId":"d","turnId":"t","outcome":"handled"}"""),
        ];
        EveTurnOutcome outcome = await ResponseAsync(facts, cancellationToken)
            .GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(expectedStatus);
        await Assert.That(outcome.Events.Count).IsEqualTo(5);
    }

    [Test]
    public async Task InteractionsAreFirstWinsCaseSensitiveAndSignInStopsAtDeliveryBoundary(
        CancellationToken cancellationToken)
    {
        EveStreamEvent[] facts =
        [
            Fact("delivery.admitted", EveStreamEventKind.DeliveryAdmitted,
                """{"deliveryId":"d"}"""),
            Fact("interaction.opened", EveStreamEventKind.InteractionOpened,
                """{"interactionId":"Q","subject":{"turnId":"t"},"request":{"kind":"question","prompt":"First"}}"""),
            Fact("interaction.settled", EveStreamEventKind.InteractionSettled,
                """{"interactionId":"Q","outcome":"accepted","response":{"text":"Yes"},"future":42}"""),
            Fact("interaction.opened", EveStreamEventKind.InteractionOpened,
                """{"interactionId":"Q","request":{"kind":"question","prompt":"Duplicate"}}"""),
            Fact("interaction.opened", EveStreamEventKind.InteractionOpened,
                """{"interactionId":"q","request":{"kind":"budget","prompt":"Continue?"}}"""),
            Fact("interaction.opened", EveStreamEventKind.InteractionOpened,
                """{"interactionId":"auth","request":{"kind":"sign-in","prompt":"Sign in","signIn":{"name":"tool","callbackUrl":"https://example.test/callback"}}}"""),
            Fact("interaction.opened", EveStreamEventKind.InteractionOpened,
                """{"interactionId":"future","request":{"kind":"future-kind","prompt":"New interaction","extra":{"mode":"future"}}}"""),
            Fact("delivery.settled", EveStreamEventKind.DeliverySettled,
                """{"deliveryId":"d","turnId":"t","outcome":"awaiting-input"}"""),
            Fact("interaction.settled", EveStreamEventKind.InteractionSettled,
                """{"interactionId":"auth","outcome":"accepted"}"""),
        ];
        EveTurnOutcome outcome = await ResponseAsync(facts, cancellationToken)
            .GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Waiting);
        await Assert.That(outcome.Events.Count).IsEqualTo(8);
        await Assert.That(outcome.Interactions.Count).IsEqualTo(4);
        await Assert.That(outcome.Interactions[0].Prompt).IsEqualTo("First");
        await Assert.That(outcome.PendingInteractions.Count).IsEqualTo(3);
        await Assert.That(outcome.PendingInteractions[1].CallbackUrl)
            .IsEqualTo("https://example.test/callback");
        await Assert.That(outcome.PendingInteractions[2].Kind).IsEqualTo("future-kind");
        await Assert.That(outcome.PendingInteractions[2].Request.GetProperty("extra")
            .GetProperty("mode").GetString()).IsEqualTo("future");
        await Assert.That(outcome.InputRequests.Count).IsEqualTo(3);
        await Assert.That(outcome.InputRequests[1].Prompt).IsEqualTo("Duplicate");
        await Assert.That(outcome.PendingInputRequests.Count).IsEqualTo(1);
        await Assert.That(outcome.PendingInputRequests[0].RequestId).IsEqualTo("q");
        await Assert.That(outcome.InteractionResolutions.Count).IsEqualTo(1);
        await Assert.That(outcome.InteractionResolutions[0].Raw.GetProperty("future").GetInt32())
            .IsEqualTo(42);
        await Assert.That(outcome.InputResolutions.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CancellationUsesMatchingStartedCauseWithoutWaitingForConsumedFact(
        CancellationToken cancellationToken)
    {
        string? cancelled = null;
        EveStreamEvent[] facts =
        [
            Fact("delivery.admitted", EveStreamEventKind.DeliveryAdmitted,
                """{"deliveryId":"d"}"""),
            Fact("turn.started", EveStreamEventKind.TurnStarted,
                """{"turnId":"wrong","cause":{"deliveryId":"other"}}"""),
            Fact("turn.started", EveStreamEventKind.TurnStarted,
                """{"turnId":"right","cause":{"deliveryId":"d"}}""", endOfLine: false),
            Fact("delivery.consumed", EveStreamEventKind.DeliveryConsumed,
                """{"deliveryId":"d","turnId":"right"}"""),
            Fact("delivery.settled", EveStreamEventKind.DeliverySettled,
                """{"deliveryId":"d","turnId":"right","outcome":"awaiting-input"}"""),
        ];
        EveMessageResponse response = new(
            "session", "d", true, false, token => StreamAsync(facts, token),
            (turnId, token) =>
            {
                token.ThrowIfCancellationRequested();
                cancelled = turnId;
                return Task.FromResult(new EveCancellationOutcome(
                    "session", EveCancellationStatus.Accepted));
            }, cancellationToken);

        await foreach (EveStreamEvent fact in response.WithCancellation(cancellationToken))
        {
            if (fact.Kind == EveStreamEventKind.TurnStarted
                && fact.Data.GetProperty("turnId").GetString() == "right")
            {
                await response.CancelAsync(cancellationToken).WaitAsync(
                    TimeSpan.FromSeconds(30), cancellationToken);
            }
        }

        await Assert.That(cancelled).IsEqualTo("right");
        EveCancellationOutcome afterPause = await response.CancelAsync(cancellationToken);
        await Assert.That(afterPause.Status).IsEqualTo(EveCancellationStatus.Accepted);
    }

    [Test]
    public async Task AdmissionIncludesEarlierFactsOnlyFromItsOwnCommit(
        CancellationToken cancellationToken)
    {
        EveStreamEvent[] facts =
        [
            Fact("turn.settled", EveStreamEventKind.TurnSettled,
                """{"turnId":"old","outcome":"failed"}""", line: 0),
            Fact("content.completed", EveStreamEventKind.ContentCompleted,
                """{"partId":"p","kind":"text","value":"Answer"}""",
                endOfLine: false, line: 1),
            Fact("delivery.admitted", EveStreamEventKind.DeliveryAdmitted,
                """{"deliveryId":"d"}""", line: 1),
            Fact("turn.settled", EveStreamEventKind.TurnSettled,
                """{"turnId":"t","outcome":"completed","reply":["p"]}""", line: 2),
            Fact("delivery.settled", EveStreamEventKind.DeliverySettled,
                """{"deliveryId":"d","turnId":"t","outcome":"handled"}""", line: 3),
        ];
        EveTurnOutcome outcome = await ResponseAsync(facts, cancellationToken)
            .GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(4);
        await Assert.That(outcome.Events[0].Type).IsEqualTo("content.completed");
        await Assert.That(outcome.Message).IsEqualTo("Answer");
        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Completed);
    }

    [Test]
    public async Task HeldTurnCancellationUsesConsumedIdentityAfterWaitingOutcome(
        CancellationToken cancellationToken)
    {
        string? cancelled = null;
        EveStreamEvent[] facts =
        [
            Fact("delivery.admitted", EveStreamEventKind.DeliveryAdmitted,
                """{"deliveryId":"d"}"""),
            Fact("turn.started", EveStreamEventKind.TurnStarted,
                """{"turnId":"wrong","cause":{"deliveryId":"other"}}"""),
            Fact("delivery.consumed", EveStreamEventKind.DeliveryConsumed,
                """{"deliveryId":"other","turnId":"wrong"}"""),
            Fact("delivery.consumed", EveStreamEventKind.DeliveryConsumed,
                """{"deliveryId":"d","turnId":"held"}"""),
            Fact("delivery.settled", EveStreamEventKind.DeliverySettled,
                """{"deliveryId":"d","turnId":"held","outcome":"awaiting-input"}"""),
        ];
        EveMessageResponse response = new(
            "session", "d", true, false, token => StreamAsync(facts, token),
            (turnId, token) =>
            {
                token.ThrowIfCancellationRequested();
                cancelled = turnId;
                return Task.FromResult(new EveCancellationOutcome(
                    "session", EveCancellationStatus.Accepted));
            }, cancellationToken);

        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);
        EveCancellationOutcome disposition = await response.CancelAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Waiting);
        await Assert.That(disposition.Status).IsEqualTo(EveCancellationStatus.Accepted);
        await Assert.That(cancelled).IsEqualTo("held");
    }

    [Test]
    public async Task AnswerCancellationUsesItsDecidingResponseBeforeDeliveryConsumption(
        CancellationToken cancellationToken)
    {
        string? cancelled = null;
        EveStreamEvent[] facts =
        [
            Fact("delivery.admitted", EveStreamEventKind.DeliveryAdmitted,
                """{"deliveryId":"d"}"""),
            Fact("response.submitted", EveStreamEventKind.ResponseSubmitted,
                """{"deliveryId":"other","responseId":"unrelated"}"""),
            Fact("interaction.settled", EveStreamEventKind.InteractionSettled,
                """{"interactionId":"other","outcome":"accepted","cause":{"responseId":"unrelated"}}""",
                turnId: "wrong"),
            Fact("response.submitted", EveStreamEventKind.ResponseSubmitted,
                """{"deliveryId":"d","responseId":"answer"}"""),
            Fact("interaction.settled", EveStreamEventKind.InteractionSettled,
                """{"interactionId":"other","outcome":"accepted","cause":{"responseId":"unrelated"}}""",
                turnId: "wrong"),
            Fact("interaction.settled", EveStreamEventKind.InteractionSettled,
                """{"interactionId":"approval","outcome":"accepted","cause":{"responseId":"answer"}}""",
                turnId: "held"),
            Fact("delivery.consumed", EveStreamEventKind.DeliveryConsumed,
                """{"deliveryId":"d","turnId":"held"}"""),
            Fact("delivery.settled", EveStreamEventKind.DeliverySettled,
                """{"deliveryId":"d","turnId":"held","outcome":"handled"}"""),
        ];
        EveMessageResponse response = new(
            "session", "d", true, true, token => StreamAsync(facts, token),
            (turnId, token) =>
            {
                token.ThrowIfCancellationRequested();
                cancelled = turnId;
                return Task.FromResult(new EveCancellationOutcome(
                    "session", EveCancellationStatus.Accepted));
            }, cancellationToken);

        int observed = 0;
        await foreach (EveStreamEvent fact in response.WithCancellation(cancellationToken))
        {
            observed++;
            if (fact.Kind == EveStreamEventKind.InteractionSettled
                && fact.Data.GetProperty("interactionId").GetString() == "approval")
            {
                await response.CancelAsync(cancellationToken).WaitAsync(
                    TimeSpan.FromSeconds(30), cancellationToken);
            }
        }

        await Assert.That(observed).IsEqualTo(8);
        await Assert.That(cancelled).IsEqualTo("held");
    }

    [Test]
    public async Task SessionEndDrainsItsCommitAndPreservesFailure(
        CancellationToken cancellationToken)
    {
        EveStreamEvent[] facts =
        [
            Fact("delivery.admitted", EveStreamEventKind.DeliveryAdmitted,
                """{"deliveryId":"d"}"""),
            Fact("session.ended", EveStreamEventKind.SessionEnded,
                """{"outcome":"failed","error":{"code":"BROKEN","message":"Failed","future":1}}""",
                endOfLine: false),
            Fact("delivery.settled", EveStreamEventKind.DeliverySettled,
                """{"deliveryId":"d","outcome":"failed"}""", endOfLine: false),
            Fact("future.final", EveStreamEventKind.Unknown, "{}"),
            Fact("future.after", EveStreamEventKind.Unknown, "{}"),
        ];
        EveTurnOutcome outcome = await ResponseAsync(facts, cancellationToken)
            .GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Failed);
        await Assert.That(outcome.Events.Count).IsEqualTo(4);
        await Assert.That(outcome.Failure!.Value.GetProperty("code").GetString()).IsEqualTo("BROKEN");
        await Assert.That(outcome.Failure!.Value.GetProperty("future").GetInt32()).IsEqualTo(1);
    }

    [Test]
    public async Task MissingAdmissionOrSettlementIsNotSuccessful(
        CancellationToken cancellationToken)
    {
        EveStreamEvent[] facts =
        [
            Fact("delivery.admitted", EveStreamEventKind.DeliveryAdmitted,
                """{"deliveryId":"other"}"""),
        ];
        EveMessageResponse response = ResponseAsync(facts, cancellationToken);

        await Assert.That(async () => await response.GetOutcomeAsync(cancellationToken))
            .Throws<EveProtocolException>();
    }

    [Test]
    public async Task SessionEndingWithoutDeliverySettlementCannotFabricateAnOutcome(
        CancellationToken cancellationToken)
    {
        EveStreamEvent[] facts =
        [
            Fact("delivery.admitted", EveStreamEventKind.DeliveryAdmitted,
                """{"deliveryId":"d"}"""),
            Fact("session.ended", EveStreamEventKind.SessionEnded,
                """{"outcome":"completed"}"""),
        ];
        EveMessageResponse response = ResponseAsync(facts, cancellationToken);

        await Assert.That(async () => await response.GetOutcomeAsync(cancellationToken))
            .Throws<EveProtocolException>();
    }

    private static EveMessageResponse ResponseAsync(
        IReadOnlyList<EveStreamEvent> facts,
        CancellationToken cancellationToken) => new(
            "session", "d", true, false, token => StreamAsync(facts, token),
            static (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(new EveCancellationOutcome(
                    "session", EveCancellationStatus.Accepted));
            }, cancellationToken);

    private static EveStreamEvent Fact(
        string type,
        EveStreamEventKind kind,
        string data,
        bool endOfLine = true,
        string? turnId = null,
        int line = 0)
    {
        using JsonDocument document = JsonDocument.Parse(data);
        JsonElement? scope = turnId is null ? null
            : EveJsonElementFactory.CreateObject(writer => writer.WriteString("turnId", turnId));
        return new EveStreamEvent(
            type, kind, document.RootElement.Clone(),
            new EveStreamEventMetadata(
                "", null, null, EveJsonElementFactory.EmptyObject,
                new EveStreamPosition(line, 0), endOfLine), scope, 27);
    }

    private static async IAsyncEnumerable<EveStreamEvent> StreamAsync(
        IReadOnlyList<EveStreamEvent> facts,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        foreach (EveStreamEvent fact in facts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return fact;
        }
    }
}
