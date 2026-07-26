using NexusLabs.Eve;

if (args.Length != 1 || !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? baseUri))
{
    throw new ArgumentException("Expected one absolute Eve base URL argument.", nameof(args));
}

using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
using SocketsHttpHandler handler = new()
{
    AllowAutoRedirect = false,
};
using HttpClient transport = new(handler);
EveClient client = new(transport, new EveClientOptions(baseUri.ToString()));

EveHealthStatus health = await client.GetHealthAsync(timeout.Token);
if (!health.Ok || !string.Equals(health.Status, "ready", StringComparison.Ordinal))
{
    throw new InvalidOperationException("The Eve fixture did not report ready health.");
}

EveAgentInfo info = await client.GetInfoAsync(timeout.Token);
if (string.IsNullOrWhiteSpace(info.AgentName)
    || string.IsNullOrWhiteSpace(info.ModelId)
    || info.Version != 1)
{
    throw new InvalidOperationException("The Eve fixture returned invalid agent information.");
}

EveSession textSession = client.CreateSession();
EveMessageResponse textResponse = await textSession.SendAsync(
    "Return the deterministic compatibility response.",
    timeout.Token);
EveTurnOutcome textOutcome = await textResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(textOutcome, "text turn");

EveSession attachmentSession = client.CreateSession();
EveMessageResponse attachmentResponse = await attachmentSession.SendAsync(
    new EveSendTurnRequest
    {
        Message = EveMessageContent.FromParts(
            EveContentPart.CreateText("Read the attached fixture."),
            EveContentPart.CreateFile(
                "fixture"u8,
                "text/plain",
                "fixture.txt")),
    },
    timeout.Token);
EveTurnOutcome attachmentOutcome = await attachmentResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(attachmentOutcome, "attachment turn");

EveSession cancellationSession = client.CreateSession();
EveMessageResponse cancellationResponse = await cancellationSession.SendAsync(
    "WAIT_FOR_CANCEL",
    timeout.Token);
List<EveStreamEventKind> cancellationEvents = [];
EveCancellationOutcome? cancellation = null;

await foreach (EveStreamEvent streamEvent in cancellationResponse.WithCancellation(timeout.Token))
{
    cancellationEvents.Add(streamEvent.Kind);

    if (cancellation is null && streamEvent.Kind == EveStreamEventKind.TurnStarted)
    {
        string turnId = streamEvent.Data.GetProperty("turnId").GetString()
            ?? throw new InvalidOperationException("turn.started did not contain a turn ID.");
        cancellation = await cancellationSession.CancelAsync(turnId, timeout.Token);
    }
}

if (cancellation?.Status != EveCancellationStatus.Accepted)
{
    throw new InvalidOperationException("The Eve fixture did not accept turn cancellation.");
}

if (!cancellationEvents.Contains(EveStreamEventKind.TurnCancelled)
    || !cancellationEvents.Contains(EveStreamEventKind.SessionWaiting))
{
    throw new InvalidOperationException(
        "The cancelled turn did not settle with turn.cancelled and session.waiting. " +
        $"Observed: {string.Join(", ", cancellationEvents)}.");
}

EveSession resetSession = client.CreateSession();
EveMessageResponse resetResponse = await resetSession.SendAsync(
    "Return the deterministic compatibility response.",
    timeout.Token);
string resetSessionId = resetResponse.SessionId;
if (resetSession.State.ContinuationToken is null)
{
    throw new InvalidOperationException(
        "The accepted turn did not record a continuation token before stream consumption.");
}

EveTurnOutcome resetOutcome = await resetResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(resetOutcome, "reset turn");

EveResetOutcome reset = await resetSession.ResetAsync(timeout.Token);
if (reset.Status != EveResetStatus.Reset
    || !string.Equals(reset.PreviousSessionId, resetSessionId, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        $"The Eve fixture did not retire the session: status={reset.Status}, " +
        $"previousSessionId={reset.PreviousSessionId}, expected={resetSessionId}.");
}

if (resetSession.State != new EveSessionState())
{
    throw new InvalidOperationException("Reset did not clear the local session state.");
}

EveMessageResponse afterResetResponse = await resetSession.SendAsync(
    "Return the deterministic compatibility response.",
    timeout.Token);
if (string.Equals(afterResetResponse.SessionId, resetSessionId, StringComparison.Ordinal))
{
    throw new InvalidOperationException("Reset did not create a new remote session.");
}

EveTurnOutcome afterResetOutcome = await afterResetResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(afterResetOutcome, "post-reset turn");

return 0;

static void RequireSuccessfulResponse(EveTurnOutcome outcome, string operation)
{
    if (outcome.Status != EveTurnStatus.Waiting
        || !string.Equals(outcome.Message, "CONNECTION_OK", StringComparison.Ordinal))
    {
        EveStreamEvent? failure = outcome.Events.LastOrDefault(static streamEvent =>
            streamEvent.IsFailure);
        string failureDetails = failure is null
            ? "none"
            : $"{failure.Type}: {failure.Data.GetRawText()}";
        throw new InvalidOperationException(
            $"The {operation} failed: status={outcome.Status}, message={outcome.Message}, " +
            $"failure={failureDetails}.");
    }

    if (outcome.Events.Count == 0)
    {
        throw new InvalidOperationException($"The {operation} did not stream events.");
    }
}
