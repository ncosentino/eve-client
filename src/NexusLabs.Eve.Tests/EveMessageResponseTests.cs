using System.Runtime.CompilerServices;

namespace NexusLabs.Eve.Tests;

public sealed class EveMessageResponseTests
{
    [Test]
    public async Task CancelAsync_WaitsForTurnAndSharesRequestUntilBoundaryAsync(
        CancellationToken cancellationToken)
    {
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource settle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> cancelledTurnIds = [];
        EveMessageResponse response = new(
            "session_1",
            streamCancellationToken => ControlledTurnStreamAsync(
                start.Task,
                settle.Task,
                streamCancellationToken),
            (turnId, requestCancellationToken) =>
            {
                requestCancellationToken.ThrowIfCancellationRequested();
                cancelledTurnIds.Add(turnId);
                return Task.FromResult(new EveCancellationOutcome(
                    "session_1",
                    EveCancellationStatus.Accepted));
            });

        Task<IReadOnlyList<EveStreamEvent>> consumption =
            CollectAsync(response, cancellationToken);
        Task<EveCancellationOutcome> cancellation =
            response.CancelAsync(cancellationToken);
        Task<EveCancellationOutcome> duplicate =
            response.CancelAsync(cancellationToken);

        await Assert.That(cancelledTurnIds.Count).IsEqualTo(0);

        start.SetResult();
        EveCancellationOutcome outcome = await cancellation.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken);
        EveCancellationOutcome duplicateOutcome = await duplicate.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveCancellationStatus.Accepted);
        await Assert.That(duplicateOutcome.Status).IsEqualTo(EveCancellationStatus.Accepted);
        await Assert.That(cancelledTurnIds).IsEquivalentTo(["turn_1"]);

        settle.SetResult();
        IReadOnlyList<EveStreamEvent> events = await consumption.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken);
        EveCancellationOutcome afterBoundary = await response.CancelAsync(cancellationToken);

        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(afterBoundary.Status).IsEqualTo(EveCancellationStatus.NoActiveTurn);
        await Assert.That(cancelledTurnIds.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CancelAsync_LaterCallerCanCancelWaitWithoutCancellingRequestAsync(
        CancellationToken cancellationToken)
    {
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource settle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        EveMessageResponse response = new(
            "session_1",
            streamCancellationToken => ControlledTurnStreamAsync(
                start.Task,
                settle.Task,
                streamCancellationToken),
            (_, requestCancellationToken) =>
            {
                requestCancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref attempts);
                return Task.FromResult(new EveCancellationOutcome(
                    "session_1",
                    EveCancellationStatus.Accepted));
            });
        Task<IReadOnlyList<EveStreamEvent>> consumption =
            CollectAsync(response, cancellationToken);
        Task<EveCancellationOutcome> cancellation =
            response.CancelAsync(cancellationToken);
        using CancellationTokenSource duplicateCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<EveCancellationOutcome> duplicate =
            response.CancelAsync(duplicateCancellation.Token);

        await duplicateCancellation.CancelAsync();

        await Assert.That(async () => await duplicate.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken))
            .Throws<OperationCanceledException>();

        start.SetResult();
        EveCancellationOutcome outcome = await cancellation.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveCancellationStatus.Accepted);
        await Assert.That(attempts).IsEqualTo(1);

        settle.SetResult();
        await consumption.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }

    [Test]
    public async Task CancelAsync_FirstCallerCancellationCanRetryAsync(
        CancellationToken cancellationToken)
    {
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource settle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        EveMessageResponse response = new(
            "session_1",
            streamCancellationToken => ControlledTurnStreamAsync(
                start.Task,
                settle.Task,
                streamCancellationToken),
            (_, requestCancellationToken) =>
            {
                requestCancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref attempts);
                return Task.FromResult(new EveCancellationOutcome(
                    "session_1",
                    EveCancellationStatus.Accepted));
            });
        Task<IReadOnlyList<EveStreamEvent>> consumption =
            CollectAsync(response, cancellationToken);
        using CancellationTokenSource firstCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<EveCancellationOutcome> cancelled =
            response.CancelAsync(firstCancellation.Token);

        await firstCancellation.CancelAsync();

        await Assert.That(async () => await cancelled.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken))
            .Throws<OperationCanceledException>();

        Task<EveCancellationOutcome> retry = response.CancelAsync(cancellationToken);
        start.SetResult();
        EveCancellationOutcome outcome = await retry.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveCancellationStatus.Accepted);
        await Assert.That(attempts).IsEqualTo(1);

        settle.SetResult();
        await consumption.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }

    [Test]
    public async Task CancelAsync_FailedRequestCanRetryWhileTurnIsActiveAsync(
        CancellationToken cancellationToken)
    {
        TaskCompletionSource settle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        EveMessageResponse response = new(
            "session_1",
            streamCancellationToken => StartedTurnStreamAsync(
                settle.Task,
                streamCancellationToken),
            (turnId, requestCancellationToken) =>
            {
                requestCancellationToken.ThrowIfCancellationRequested();
                int attempt = Interlocked.Increment(ref attempts);
                return attempt == 1
                    ? Task.FromException<EveCancellationOutcome>(
                        new HttpRequestException("Cancel unavailable."))
                    : Task.FromResult(new EveCancellationOutcome(
                        "session_1",
                        EveCancellationStatus.Accepted));
            });
        Task<IReadOnlyList<EveStreamEvent>> consumption =
            CollectAsync(response, cancellationToken);

        await Assert.That(async () => await response.CancelAsync(cancellationToken))
            .Throws<HttpRequestException>();

        EveCancellationOutcome outcome = await response.CancelAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveCancellationStatus.Accepted);
        await Assert.That(attempts).IsEqualTo(2);

        settle.SetResult();
        await consumption.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }

    [Test]
    public async Task CancelAsync_SettledResponseWithoutTurnDoesNotSendRequestAsync(
        CancellationToken cancellationToken)
    {
        int attempts = 0;
        EveMessageResponse response = new(
            "session_1",
            BoundaryOnlyStreamAsync,
            (_, _) =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromResult(new EveCancellationOutcome(
                    "session_1",
                    EveCancellationStatus.Accepted));
            });

        Task<IReadOnlyList<EveStreamEvent>> consumption =
            CollectAsync(response, cancellationToken);
        EveCancellationOutcome outcome = await response.CancelAsync(cancellationToken);
        IReadOnlyList<EveStreamEvent> events = await consumption;

        await Assert.That(outcome.Status).IsEqualTo(EveCancellationStatus.NoActiveTurn);
        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(attempts).IsEqualTo(0);
    }

    [Test]
    public async Task CancelAsync_ResponseEndsWithoutTurnDoesNotSendRequestAsync(
        CancellationToken cancellationToken)
    {
        int attempts = 0;
        EveMessageResponse response = new(
            "session_1",
            EmptyStreamAsync,
            (_, _) =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromResult(new EveCancellationOutcome(
                    "session_1",
                    EveCancellationStatus.Accepted));
            });

        Task<IReadOnlyList<EveStreamEvent>> consumption =
            CollectAsync(response, cancellationToken);
        EveCancellationOutcome outcome = await response.CancelAsync(cancellationToken);
        IReadOnlyList<EveStreamEvent> events = await consumption;

        await Assert.That(outcome.Status).IsEqualTo(EveCancellationStatus.NoActiveTurn);
        await Assert.That(events.Count).IsEqualTo(0);
        await Assert.That(attempts).IsEqualTo(0);
    }

    [Test]
    public async Task CancelAsync_MalformedTurnIdentityFailsWithoutSendingRequestAsync(
        CancellationToken cancellationToken)
    {
        int attempts = 0;
        EveMessageResponse response = new(
            "session_1",
            MalformedTurnStreamAsync,
            (_, _) =>
            {
                Interlocked.Increment(ref attempts);
                return Task.FromResult(new EveCancellationOutcome(
                    "session_1",
                    EveCancellationStatus.Accepted));
            });

        Task<IReadOnlyList<EveStreamEvent>> consumption =
            CollectAsync(response, cancellationToken);
        Task<EveCancellationOutcome> cancellation =
            response.CancelAsync(cancellationToken);

        await Assert.That(async () => await consumption.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(async () => await cancellation.WaitAsync(
            TimeSpan.FromSeconds(30),
            cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(attempts).IsEqualTo(0);
    }

    [Test]
    public async Task CancelAsync_RemainsActiveAcrossCallbackAuthorizationParkingAsync(
        CancellationToken cancellationToken)
    {
        List<string> cancelledTurnIds = [];
        EveMessageResponse response = new(
            "session_1",
            CallbackAuthorizationStreamAsync,
            (turnId, requestCancellationToken) =>
            {
                requestCancellationToken.ThrowIfCancellationRequested();
                cancelledTurnIds.Add(turnId);
                return Task.FromResult(new EveCancellationOutcome(
                    "session_1",
                    EveCancellationStatus.Accepted));
            });

        await using IAsyncEnumerator<EveStreamEvent> enumerator =
            response.GetAsyncEnumerator(cancellationToken);
        await Assert.That(await enumerator.MoveNextAsync())
            .IsTrue()
            .Because("The response should expose turn.started.");
        await Assert.That(enumerator.Current.Kind).IsEqualTo(EveStreamEventKind.TurnStarted);
        await Assert.That(await enumerator.MoveNextAsync())
            .IsTrue()
            .Because("The response should expose authorization.required.");
        await Assert.That(enumerator.Current.Kind)
            .IsEqualTo(EveStreamEventKind.AuthorizationRequired);
        await Assert.That(await enumerator.MoveNextAsync())
            .IsTrue()
            .Because("The response should expose the interim session.waiting event.");
        await Assert.That(enumerator.Current.Kind).IsEqualTo(EveStreamEventKind.SessionWaiting);

        EveCancellationOutcome cancellation = await response.CancelAsync(cancellationToken);

        await Assert.That(cancellation.Status).IsEqualTo(EveCancellationStatus.Accepted);
        await Assert.That(cancelledTurnIds).IsEquivalentTo(["turn_1"]);
        await Assert.That(await enumerator.MoveNextAsync())
            .IsTrue()
            .Because("The response should continue through authorization completion.");
        await Assert.That(enumerator.Current.Kind)
            .IsEqualTo(EveStreamEventKind.AuthorizationCompleted);
        await Assert.That(await enumerator.MoveNextAsync())
            .IsTrue()
            .Because("The response should expose the final session.waiting event.");
        await Assert.That(enumerator.Current.Kind).IsEqualTo(EveStreamEventKind.SessionWaiting);
        await Assert.That(await enumerator.MoveNextAsync())
            .IsFalse()
            .Because("The final session.waiting event should settle the response.");

        EveCancellationOutcome afterBoundary = await response.CancelAsync(cancellationToken);

        await Assert.That(afterBoundary.Status).IsEqualTo(EveCancellationStatus.NoActiveTurn);
    }

    private static async IAsyncEnumerable<EveStreamEvent> ControlledTurnStreamAsync(
        Task start,
        Task settle,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await start.WaitAsync(cancellationToken);
        yield return TurnStartedEvent();
        await settle.WaitAsync(cancellationToken);
        yield return SessionWaitingEvent();
    }

    private static async IAsyncEnumerable<EveStreamEvent> StartedTurnStreamAsync(
        Task settle,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return TurnStartedEvent();
        await settle.WaitAsync(cancellationToken);
        yield return SessionWaitingEvent();
    }

    private static async IAsyncEnumerable<EveStreamEvent> BoundaryOnlyStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        yield return SessionWaitingEvent();
    }

    private static async IAsyncEnumerable<EveStreamEvent> EmptyStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }

    private static async IAsyncEnumerable<EveStreamEvent> MalformedTurnStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        yield return EveStreamEvent.Parse(
            """{"type":"turn.started","data":{"sequence":0}}""");
    }

    private static async IAsyncEnumerable<EveStreamEvent> CallbackAuthorizationStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        yield return TurnStartedEvent();
        yield return EveStreamEvent.Parse(
            """{"type":"authorization.required","data":{"name":"linear","webhookUrl":"https://agent.example.com/auth/linear"}}""");
        yield return SessionWaitingEvent();
        yield return EveStreamEvent.Parse(
            """{"type":"authorization.completed","data":{"name":"linear","outcome":"authorized"}}""");
        yield return SessionWaitingEvent();
    }

    private static EveStreamEvent TurnStartedEvent() =>
        EveStreamEvent.Parse(
            """{"type":"turn.started","data":{"sequence":0,"turnId":"turn_1"}}""");

    private static EveStreamEvent SessionWaitingEvent() =>
        EveStreamEvent.Parse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"}}""");

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
}
