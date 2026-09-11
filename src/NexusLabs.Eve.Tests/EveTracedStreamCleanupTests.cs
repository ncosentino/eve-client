namespace NexusLabs.Eve.Tests;

public sealed class EveTracedStreamCleanupTests
{
    [Test]
    public async Task GetOutcomeAsync_AbortsBoundaryConnectionBeforeDisposal(
        CancellationToken cancellationToken)
    {
        using BoundaryHttpMessageHandler handler = new(
            cancellationToken,
            """{"type":"result.completed","data":{"result":{"answer":"child-result"}}}""",
            """{"type":"session.waiting","data":{"wait":"next-user-message"}}""");
        using HttpMessageInvoker transport = new(handler, false);
        EveSession session = CreateClient(transport).CreateSession();
        EveMessageResponse response = await session.SendAsync(
            "Return a structured answer.",
            cancellationToken);
        Task<EveTurnOutcome> outcomeTask = response.GetOutcomeAsync(cancellationToken);

        try
        {
            await handler.Stream.DisposeStarted.WaitAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken);
            EveTurnOutcome outcome = await outcomeTask.WaitAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken);

            await Assert.That(outcome.Data.HasValue)
                .IsTrue()
                .Because("The result.completed event must populate structured output.");
            await Assert.That(outcome.Data.GetValueOrDefault()
                .GetProperty("answer")
                .GetString())
                .IsEqualTo("child-result");
            await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Waiting);
            await Assert.That(outcome.Events.Count).IsEqualTo(2);
            await Assert.That(handler.Stream.RequestCancellation.IsCompleted)
                .IsTrue()
                .Because("Early boundary termination must abort the stream request.");
            await Assert.That(handler.Stream.DisposeCompleted.IsCompleted)
                .IsTrue()
                .Because("Stream disposal must finish after the request is aborted.");
            await Assert.That(handler.Stream.IsDisposed)
                .IsTrue()
                .Because("The primary response stream must be deterministically disposed.");
            await Assert.That(handler.StreamContentIsDisposed)
                .IsTrue()
                .Because("Disposing the response must dispose its HTTP content.");
            await Assert.That(handler.Stream.TraceIsOpen)
                .IsTrue()
                .Because("The tracing branch must remain open while the outcome settles.");
            await Assert.That(handler.RequestCount).IsEqualTo(2);
            await Assert.That(session.State).IsEqualTo(new EveSessionState
            {
                SessionId = "session_1",
                StreamIndex = 2,
            });
        }
        finally
        {
            handler.CompleteTrace();
            if (!outcomeTask.IsCompleted)
            {
                await outcomeTask.WaitAsync(
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
            }
        }
    }

    [Test]
    public async Task GetOutcomeAsync_CallerCancellationDisposesOpenConnection(
        CancellationToken cancellationToken)
    {
        using BoundaryHttpMessageHandler handler = new(
            cancellationToken,
            """{"type":"turn.started","data":{"turnId":"turn_1"}}""");
        using HttpMessageInvoker transport = new(handler, false);
        EveSession session = CreateClient(transport).CreateSession();
        EveMessageResponse response = await session.SendAsync(
            "Keep streaming.",
            cancellationToken);
        using CancellationTokenSource streamCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<EveTurnOutcome> outcomeTask =
            response.GetOutcomeAsync(streamCancellation.Token);

        try
        {
            await handler.Stream.ReadBlocked.WaitAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken);
            await streamCancellation.CancelAsync();
            EveTurnOutcome outcome = await outcomeTask.WaitAsync(
                TimeSpan.FromSeconds(30),
                cancellationToken);

            await Assert.That(outcome.Events.Count).IsEqualTo(1);
            await Assert.That(handler.Stream.RequestCancellation.IsCompleted)
                .IsTrue()
                .Because("Caller cancellation must reach the active stream request.");
            await Assert.That(handler.Stream.DisposeCompleted.IsCompleted)
                .IsTrue()
                .Because("Caller cancellation must unblock asynchronous stream disposal.");
            await Assert.That(handler.Stream.IsDisposed)
                .IsTrue()
                .Because("Caller cancellation must release the primary response stream.");
            await Assert.That(handler.StreamContentIsDisposed)
                .IsTrue()
                .Because("Caller cancellation must still dispose the response content.");
            await Assert.That(handler.Stream.TraceIsOpen)
                .IsTrue()
                .Because("Primary cleanup must not require the tracing branch to close.");
            await Assert.That(handler.RequestCount).IsEqualTo(2);
            await Assert.That(session.State).IsEqualTo(new EveSessionState
            {
                SessionId = "session_1",
                StreamIndex = 1,
            });
        }
        finally
        {
            handler.CompleteTrace();
            if (!outcomeTask.IsCompleted)
            {
                await outcomeTask.WaitAsync(
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
            }
        }
    }

    private static EveClient CreateClient(HttpMessageInvoker transport) =>
        new(transport, new EveClientOptions("https://agent.example.com"));
}
