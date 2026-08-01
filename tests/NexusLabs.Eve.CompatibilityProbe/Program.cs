using System.Globalization;

using NexusLabs.Eve;
using NexusLabs.Eve.CompatibilityProbe;

if (args.Length != 1 || !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? baseUri))
{
    throw new ArgumentException("Expected one absolute Eve base URL argument.", nameof(args));
}

using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
using SocketsHttpHandler handler = new()
{
    AllowAutoRedirect = false,
};
using StreamRequestRecorder streamRecorder = new(handler);
using HttpClient transport = new(streamRecorder);
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
RequireDurableEventEnvelope(textOutcome, "text turn");

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

EveSession catchUpSession = client.CreateSession();
EveMessageResponse catchUpResponse = await catchUpSession.SendAsync(
    "Return the deterministic compatibility response.",
    timeout.Token);
EveTurnOutcome catchUpOutcome = await catchUpResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(catchUpOutcome, "catch-up turn");

int recordedBeforeCatchUp = streamRecorder.StreamRequests.Count;
List<EveStreamEvent> catchUpEvents = [];
EveProtocolException? catchUpFailure = null;

try
{
    await foreach (EveStreamEvent streamEvent in catchUpSession.StreamAsync(
        new EveStreamOptions
        {
            Follow = false,
            StartIndex = 0,
        },
        timeout.Token))
    {
        catchUpEvents.Add(streamEvent);
    }
}
catch (EveProtocolException exception)
{
    catchUpFailure = exception;
}

RecordedStreamRequest[] catchUpRequests = streamRecorder.StreamRequests
    .Skip(recordedBeforeCatchUp)
    .ToArray();
if (catchUpRequests.Length == 0)
{
    throw new InvalidOperationException("The bounded catch-up read did not open a stream.");
}

if (!catchUpRequests[0].Uri.Contains("includeTailIndex=1", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The first bounded catch-up request did not ask for the durable tail index: " +
        $"{catchUpRequests[0].Uri}.");
}

foreach (RecordedStreamRequest reconnect in catchUpRequests.Skip(1))
{
    if (reconnect.Uri.Contains("includeTailIndex", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"A bounded catch-up reconnect requested the durable tail again: {reconnect.Uri}.");
    }
}

string? observedTailIndex = catchUpRequests[0].TailIndex;
if (observedTailIndex is null)
{
    throw new InvalidOperationException(
        $"eve {EveProtocol.ReferenceEveVersion} omitted the x-eve-stream-tail-index response " +
        "header, so a bounded catch-up read cannot be verified.");
}

if (catchUpFailure is not null)
{
    throw new InvalidOperationException(
        $"The Eve fixture reported tail index '{observedTailIndex}', " +
        $"but the bounded read failed: {catchUpFailure.Message}");
}

if (!int.TryParse(
        observedTailIndex,
        NumberStyles.AllowLeadingSign,
        CultureInfo.InvariantCulture,
        out int tailIndex)
    || tailIndex < 0)
{
    throw new InvalidOperationException(
        $"The Eve fixture reported an invalid tail index: '{observedTailIndex}'.");
}

if (catchUpEvents.Count != tailIndex + 1)
{
    throw new InvalidOperationException(
        $"The bounded catch-up read returned {catchUpEvents.Count} events " +
        $"for tail index {tailIndex}.");
}

if (catchUpSession.State.StreamIndex != catchUpEvents.Count)
{
    throw new InvalidOperationException(
        "The bounded catch-up read did not advance the session cursor: " +
        $"{catchUpSession.State.StreamIndex} of {catchUpEvents.Count} events.");
}

EveSession approvalSession = client.CreateSession();
EveMessageResponse approvalResponse = await approvalSession.SendAsync(
    "REQUEST_APPROVAL",
    timeout.Token);
EveTurnOutcome approvalOutcome = await approvalResponse.GetOutcomeAsync(timeout.Token);
if (approvalOutcome.Status != EveTurnStatus.Waiting)
{
    throw new InvalidOperationException(
        $"The approval turn did not park for human input: status={approvalOutcome.Status}.");
}

if (approvalOutcome.InputRequests.Count != 1)
{
    throw new InvalidOperationException(
        $"The approval turn emitted {approvalOutcome.InputRequests.Count} input requests.");
}

EveInputRequest approvalRequest = approvalOutcome.InputRequests[0];

if (approvalRequest.RawKind != "tool-approval"
    || approvalRequest.Kind != EveInputRequestKind.ToolApproval)
{
    throw new InvalidOperationException(
        $"eve {EveProtocol.ReferenceEveVersion} reported input request kind " +
        $"'{approvalRequest.RawKind ?? "<absent>"}' projected as {approvalRequest.Kind}.");
}

if (approvalRequest.Options.Count == 0)
{
    throw new InvalidOperationException(
        "The approval request did not offer any selectable options.");
}

EveMessageResponse resumedResponse = await approvalSession.SendAsync(
    new EveSendTurnRequest
    {
        InputResponses = [new EveInputResponse(approvalRequest.RequestId, "approve")],
    },
    timeout.Token);
EveTurnOutcome resumedOutcome = await resumedResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(resumedOutcome, "approved tool turn");

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

static void RequireDurableEventEnvelope(EveTurnOutcome outcome, string operation)
{
    EveStreamEventDeduplicator deduplicator = new();
    int admitted = 0;

    foreach (EveStreamEvent streamEvent in outcome.Events)
    {
        if (streamEvent.Metadata is not EveStreamEventMetadata metadata)
        {
            throw new InvalidOperationException(
                $"The {operation} produced '{streamEvent.Type}' without durable metadata.");
        }

        if (string.IsNullOrWhiteSpace(metadata.At))
        {
            throw new InvalidOperationException(
                $"The {operation} produced '{streamEvent.Type}' without a durable timestamp.");
        }

        if (metadata.Id is not string identifier || !IsEventIdentifier(identifier))
        {
            throw new InvalidOperationException(
                $"The {operation} produced '{streamEvent.Type}' with durable identifier " +
                $"'{metadata.Id ?? "<absent>"}', which is not an eve stream protocol " +
                $"{EveProtocol.MessageStreamVersion} event id.");
        }

        if (deduplicator.Admit(streamEvent))
        {
            admitted++;
        }
    }

    if (admitted != outcome.Events.Count)
    {
        throw new InvalidOperationException(
            $"The {operation} repeated a durable identifier: admitted {admitted} of " +
            $"{outcome.Events.Count} events.");
    }

    if (deduplicator.Count != outcome.Events.Count)
    {
        throw new InvalidOperationException(
            $"The {operation} remembered {deduplicator.Count} identifiers for " +
            $"{outcome.Events.Count} events.");
    }
}

// Mirrors the upstream shape check: the 'evt_' prefix followed by a Crockford base32 ULID.
static bool IsEventIdentifier(string value)
{
    const string prefix = "evt_";
    const int ulidLength = 26;
    const string crockfordAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    if (!value.StartsWith(prefix, StringComparison.Ordinal)
        || value.Length != prefix.Length + ulidLength)
    {
        return false;
    }

    foreach (char character in value.AsSpan(prefix.Length))
    {
        if (!crockfordAlphabet.Contains(character, StringComparison.Ordinal))
        {
            return false;
        }
    }

    return true;
}
