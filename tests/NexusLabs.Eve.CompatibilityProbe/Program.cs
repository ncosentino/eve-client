using System.Globalization;
using System.Net;

using NexusLabs.Eve;
using NexusLabs.Eve.CompatibilityProbe;

if (args.Length != 2 || !Uri.TryCreate(args[0], UriKind.Absolute, out Uri? baseUri))
{
    throw new ArgumentException(
        "Expected an absolute Eve base URL and the running Eve version.",
        nameof(args));
}

// The version is read from the installed package by the fixture runner, so this compares the
// package's declared compatibility claim against the server the probe actually exercised.
// Interpolating the constant into a success message would report the claim, not verify it.
string runningEveVersion = args[1].Trim();
if (!string.Equals(runningEveVersion, EveProtocol.ReferenceEveVersion, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        $"The probe ran against eve {runningEveVersion} but " +
        $"EveProtocol.ReferenceEveVersion declares {EveProtocol.ReferenceEveVersion}. " +
        "Advance the fixture and the declared reference together.");
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
    || !EveProtocol.SupportedAgentInfoVersions.Contains(info.Version))
{
    throw new InvalidOperationException("The Eve fixture returned invalid agent information.");
}

// A dynamic model reports no identifier from eve 0.33.0 onward, so require one only when the
// agent reports concrete routing.
if (info.ModelRouting == EveAgentModelRouting.Dynamic)
{
    if (info.ModelId is not null)
    {
        throw new InvalidOperationException(
            "The Eve fixture reported a dynamic model with a model identifier.");
    }
}
else if (string.IsNullOrWhiteSpace(info.ModelId))
{
    throw new InvalidOperationException("The Eve fixture returned no model identifier.");
}

EveSession textSession = client.CreateSession();
EveMessageResponse textResponse = await textSession.SendAsync(
    "Return the deterministic compatibility response.",
    timeout.Token);
EveTurnOutcome textOutcome = await textResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(textOutcome, "text turn");
RequireDurableEventEnvelope(textOutcome, "text turn");

EveClient authorizationClient = new(
    transport,
    new EveClientOptions(baseUri.ToString())
    {
        Authentication = new EveBearerAuthentication("compatibility-user"),
    });
EveSession authorizationSession = authorizationClient.CreateSession();
EveMessageResponse authorizationResponse = await authorizationSession.SendAsync(
    "REQUEST_CALLBACK_AUTH",
    timeout.Token);
List<EveStreamEvent> authorizationEvents = [];
Uri? authorizationWebhook = null;
bool authorizationCallbackSent = false;

await foreach (EveStreamEvent streamEvent in authorizationResponse.WithCancellation(timeout.Token))
{
    authorizationEvents.Add(streamEvent);

    if (streamEvent.Kind == EveStreamEventKind.AuthorizationRequired)
    {
        string? connectionName = streamEvent.Data.TryGetProperty(
                "name",
                out var name)
            ? name.GetString()
            : null;
        string? webhookUrl = streamEvent.Data.TryGetProperty(
                "webhookUrl",
                out var webhook)
            ? webhook.GetString()
            : null;
        if (!string.Equals(connectionName, "callback-auth", StringComparison.Ordinal)
            || !Uri.TryCreate(webhookUrl, UriKind.Absolute, out Uri? reportedWebhook))
        {
            throw new InvalidOperationException(
                "The callback authorization did not expose its stable name and webhook URL.");
        }

        // The local workflow runtime mints its default localhost origin independently of the
        // fixture's selected port. The framework-owned path and token remain authoritative.
        authorizationWebhook = new Uri(baseUri, reportedWebhook.PathAndQuery);
    }
    else if (streamEvent.Kind == EveStreamEventKind.SessionWaiting
        && authorizationWebhook is not null
        && !authorizationCallbackSent)
    {
        using HttpResponseMessage callbackResponse = await transport.GetAsync(
            authorizationWebhook,
            timeout.Token);
        if (!callbackResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                "The callback authorization webhook returned " +
                $"{(int)callbackResponse.StatusCode} {callbackResponse.StatusCode}.");
        }

        authorizationCallbackSent = true;
    }
}

int authorizationRequiredIndex = authorizationEvents.FindIndex(
    static streamEvent => streamEvent.Kind == EveStreamEventKind.AuthorizationRequired);
int interimWaitingIndex = authorizationEvents.FindIndex(
    authorizationRequiredIndex + 1,
    static streamEvent => streamEvent.Kind == EveStreamEventKind.SessionWaiting);
int authorizationCompletedIndex = authorizationEvents.FindIndex(
    static streamEvent => streamEvent.Kind == EveStreamEventKind.AuthorizationCompleted);
int finalWaitingIndex = authorizationEvents.FindLastIndex(
    static streamEvent => streamEvent.Kind == EveStreamEventKind.SessionWaiting);
if (!authorizationCallbackSent
    || authorizationRequiredIndex < 0
    || interimWaitingIndex <= authorizationRequiredIndex
    || authorizationCompletedIndex <= interimWaitingIndex
    || finalWaitingIndex <= authorizationCompletedIndex)
{
    throw new InvalidOperationException(
        "The callback authorization stream did not continue across its interim waiting boundary. " +
        $"Observed: {string.Join(", ", authorizationEvents.Select(static value => value.Type))}.");
}

EveStreamEvent authorizationCompleted = authorizationEvents[authorizationCompletedIndex];
if (!string.Equals(
        authorizationCompleted.Data.GetProperty("name").GetString(),
        "callback-auth",
        StringComparison.Ordinal)
    || !string.Equals(
        authorizationCompleted.Data.GetProperty("outcome").GetString(),
        "authorized",
        StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The callback authorization did not emit its authoritative completion.");
}

EveStreamEvent? authorizationMessage = authorizationEvents.LastOrDefault(
    static streamEvent => streamEvent.Kind == EveStreamEventKind.MessageCompleted);
if (!string.Equals(
        authorizationMessage?.Data.GetProperty("message").GetString(),
        "CONNECTION_OK",
        StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The callback-authorized turn did not resume to its deterministic response.");
}

if (authorizationSession.State.StreamIndex != authorizationEvents.Count)
{
    throw new InvalidOperationException(
        "The callback authorization stream did not advance through its final boundary.");
}

EveSession attachmentSession = client.CreateSession();
EveMessageResponse attachmentResponse = await attachmentSession.SendAsync(
    EveMessageContent.FromParts(
        EveContentPart.CreateText("Read the attached fixture."),
        EveContentPart.CreateFile(
            "fixture"u8,
            "text/plain",
            "fixture.txt")),
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
        cancellation = await cancellationResponse.CancelAsync(timeout.Token);
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

EveMessageResponse resumedResponse = await approvalSession.RespondAsync(
    [new EveInputResponse(approvalRequest.RequestId, "approve")],
    timeout.Token);
EveTurnOutcome resumedOutcome = await resumedResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(resumedOutcome, "approved tool turn");
if (resumedOutcome.InputResolutions.Count != 1)
{
    throw new InvalidOperationException(
        $"The approved tool turn emitted {resumedOutcome.InputResolutions.Count} " +
        "input resolutions.");
}

EveInputResolution approvalResolution = resumedOutcome.InputResolutions[0];
EveInputResponse? acceptedApprovalResponse = approvalResolution.Response;
if (approvalResolution.RequestId != approvalRequest.RequestId
    || approvalResolution.RawKind != "tool-approval"
    || approvalResolution.Kind != EveInputRequestKind.ToolApproval
    || approvalResolution.RawOutcome != "approved"
    || approvalResolution.Outcome != EveInputResolutionOutcome.Approved
    || acceptedApprovalResponse?.RequestId != approvalRequest.RequestId
    || acceptedApprovalResponse.OptionId != "approve")
{
    throw new InvalidOperationException(
        "The approved tool turn did not project its authoritative input resolution.");
}

int resolutionIndex = -1;
int resumedStepIndex = -1;
for (int eventIndex = 0; eventIndex < resumedOutcome.Events.Count; eventIndex++)
{
    switch (resumedOutcome.Events[eventIndex].Kind)
    {
        case EveStreamEventKind.InputResolved:
            resolutionIndex = eventIndex;
            break;
        case EveStreamEventKind.StepStarted when resumedStepIndex < 0:
            resumedStepIndex = eventIndex;
            break;
    }
}

if (resolutionIndex < 0 || resumedStepIndex <= resolutionIndex)
{
    throw new InvalidOperationException(
        "The durable input resolution did not precede the resumed step.");
}

EveSession resetSession = client.CreateSession();
EveMessageResponse resetResponse = await resetSession.SendAsync(
    "Return the deterministic compatibility response.",
    timeout.Token);
string resetSessionId = resetResponse.SessionId;
if (!string.Equals(resetSession.State.SessionId, resetSessionId, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The accepted turn did not record the session id before stream consumption.");
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

if (resetSession.State != new EveSessionState
{
    SessionId = resetSessionId,
    StreamIndex = resetOutcome.Events.Count,
})
{
    throw new InvalidOperationException("Reset did not retain the local session state.");
}

// A reset handle keeps its identifier instead of recycling, so reusing it must be refused by
// the retired session rather than silently starting a new conversation.
EveClientException? retiredSendFailure = null;
try
{
    await resetSession.SendAsync(
        "Return the deterministic compatibility response.",
        timeout.Token);
}
catch (EveClientException exception)
{
    retiredSendFailure = exception;
}

if (retiredSendFailure is null)
{
    throw new InvalidOperationException(
        "Sending on a reset session identifier was accepted.");
}

if (retiredSendFailure.StatusCode != HttpStatusCode.Conflict)
{
    throw new InvalidOperationException(
        "Sending on a reset session identifier returned " +
        $"{retiredSendFailure.StatusCode}, expected 409 Conflict.");
}

if (!string.Equals(retiredSendFailure.ErrorCode, "session_not_active", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Sending on a reset session identifier reported error code " +
        $"'{retiredSendFailure.ErrorCode ?? "<absent>"}', expected 'session_not_active'. " +
        $"Body: {retiredSendFailure.ResponseBody}");
}

EveSession afterResetSession = client.CreateSession();
EveMessageResponse afterResetResponse = await afterResetSession.SendAsync(
    "Return the deterministic compatibility response.",
    timeout.Token);
if (string.Equals(afterResetResponse.SessionId, resetSessionId, StringComparison.Ordinal))
{
    throw new InvalidOperationException("A fresh session reused the retired session identifier.");
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
