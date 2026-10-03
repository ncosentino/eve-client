using System.Globalization;
using System.Net;
using System.Text.Json;

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

using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(4));
string probeScenario = "health and inspection";
using CancellationTokenRegistration timeoutDiagnostic = timeout.Token.Register(
    () => Console.Error.WriteLine($"Compatibility probe cancelled during: {probeScenario}."));
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

if (info.Version != 5
    || info.Raw.TryGetProperty("workflow", out _)
    || info.Raw.GetProperty("agent").GetProperty("config")
        .GetProperty("binding").GetProperty("backing").GetProperty("kind").GetString()
        != "filesystem")
{
    throw new InvalidOperationException(
        "The current Eve fixture did not expose schema-v5 filesystem-backed configuration.");
}

JsonElement kernelEffects = info.Raw.GetProperty("kernelEffects");
if (kernelEffects.EnumerateArray().Any(
        static effect => effect.TryGetProperty("action", out JsonElement action)
            && action.GetString() == "task-update"))
{
    throw new InvalidOperationException(
        "The Eve fixture exposed the obsolete task-update kernel effect.");
}

if (!kernelEffects.EnumerateArray().Any(
        static effect => effect.TryGetProperty("action", out JsonElement action)
            && action.GetString() == "workflow-tool-call"))
{
    throw new InvalidOperationException(
        "The Eve fixture did not expose the durable workflow tool kernel effect.");
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

using SocketsHttpHandler compactSockets = new()
{
    AllowAutoRedirect = false,
};
using CompactNamedAgentRouteHandler compactRoute = new(compactSockets);
using HttpClient compactTransport = new(compactRoute);
EveClient compactClient = new(
    compactTransport,
    new EveClientOptions(new Uri(baseUri, "/eve/support").ToString()));
EveAgentInfo compactInfo = await compactClient.GetInfoAsync(timeout.Token);
if (!string.Equals(compactInfo.AgentName, info.AgentName, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The compact named-agent route did not reach the real Eve fixture.");
}

EveSession prewarmedSession = await compactClient.PrewarmSessionAsync(timeout.Token);
if (string.IsNullOrWhiteSpace(prewarmedSession.State.SessionId)
    || prewarmedSession.State.StreamIndex != 0)
{
    throw new InvalidOperationException(
        "The message-free create request did not return a fresh remote session.");
}

EveMessageResponse prewarmedResponse = await prewarmedSession.SendAsync(
    "Return the deterministic compatibility response.",
    timeout.Token);
EveTurnOutcome prewarmedOutcome =
    await prewarmedResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(prewarmedOutcome, "prewarmed first turn");
if (compactRoute.ReadinessRefusalCount != 1)
{
    throw new InvalidOperationException(
        "The prewarmed first send did not recover from exactly one session_not_ready response.");
}

string prewarmedSessionPath =
    $"/eve/support/v1/session/{Uri.EscapeDataString(prewarmedResponse.SessionId)}";
IReadOnlyList<string> compactRequestPaths = compactRoute.RequestPaths;
if (compactRequestPaths.Count < 5
    || !string.Equals(
        compactRequestPaths[0],
        "/eve/support/v1/info",
        StringComparison.Ordinal)
    || !string.Equals(
        compactRequestPaths[1],
        "/eve/support/v1/session",
        StringComparison.Ordinal)
    || compactRequestPaths
        .Skip(2)
        .SkipLast(1)
        .Any(path => !string.Equals(path, prewarmedSessionPath, StringComparison.Ordinal))
    || !string.Equals(
        compactRequestPaths[^1],
        $"{prewarmedSessionPath}/stream",
        StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The compact named-agent route sequence was not preserved. " +
        $"Observed: {string.Join(", ", compactRequestPaths)}.");
}

EveSession textSession = client.CreateSession();
probeScenario = "text and reasoning";
EveMessageResponse textResponse = await textSession.SendAsync(
    "Return the deterministic compatibility response.",
    timeout.Token);
EveTurnOutcome textOutcome = await textResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(textOutcome, "text turn");
RequireDurableEventEnvelope(textOutcome, "text turn");
if (!textOutcome.Events.Any(static streamEvent =>
    streamEvent.Kind == EveStreamEventKind.ReasoningAppended
    && streamEvent.Data.GetProperty("reasoningDelta").GetString() == "DETERMINISTIC_REASONING"
    && !streamEvent.Data.TryGetProperty("reasoningSoFar", out _)))
{
    throw new InvalidOperationException("The fixture did not preserve streamed reasoning deltas.");
}
if (streamRecorder.StreamRequests.Count == 0
    || streamRecorder.StreamRequests.Any(static request => request.StreamVersion != "26"))
{
    throw new InvalidOperationException("The real Eve stream did not advertise protocol version 26.");
}

EveSession childParentSession = client.CreateSession();
probeScenario = "parent and child stream";
EveMessageResponse childParentResponse = await childParentSession.SendAsync(
    "REQUEST_CHILD_STREAM",
    timeout.Token);
EveTurnOutcome childParentOutcome = await childParentResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(childParentOutcome, "child parent turn");
EveStreamEvent childDescriptor = childParentOutcome.Events.Single(
    static streamEvent => streamEvent.Type == "agent.started");
string childSessionId = childDescriptor.Data.GetProperty("sessionId").GetString()
    ?? throw new InvalidOperationException("The child announcement omitted its session identifier.");
if (childSessionId == childParentResponse.SessionId
    || childDescriptor.Data.GetProperty("name").GetString() != "probe_child"
    || childDescriptor.Data.GetProperty("callId").GetString() != "call_child"
    || childDescriptor.Data.TryGetProperty("remote", out _)
    || childParentOutcome.Events.Any(static streamEvent => streamEvent.Type == "subagent.called"))
{
    throw new InvalidOperationException("The real child announcement did not use local agent.started coordinates.");
}

EveSessionState parentCursorBeforeChild = childParentSession.State;
int recordedBeforeChild = streamRecorder.StreamRequests.Count;
List<EveStreamEvent> childEvents = [];
await foreach (EveStreamEvent streamEvent in childParentSession.StreamSubagentAsync(
    childDescriptor,
    new EveStreamOptions { Follow = false, StartIndex = 0 },
    timeout.Token))
{
    childEvents.Add(streamEvent);
    if (childParentSession.State != parentCursorBeforeChild)
    {
        throw new InvalidOperationException("Consuming the child changed the parent stream cursor.");
    }
}

RecordedStreamRequest childRequest = streamRecorder.StreamRequests[recordedBeforeChild];
string expectedChildPath = $"/eve/v1/session/{Uri.EscapeDataString(childSessionId)}/stream";
if (new Uri(childRequest.Uri).AbsolutePath != expectedChildPath
    || childDescriptor.Data.GetProperty("streamPath").GetString() != expectedChildPath
    || childRequest.Uri.Contains("startIndex=", StringComparison.Ordinal)
    || childRequest.StreamVersion != "26"
    || !childEvents.Any(static streamEvent =>
        streamEvent.Kind == EveStreamEventKind.MessageCompleted
        && streamEvent.Data.GetProperty("message").GetString() == "CHILD_STREAM_OK"))
{
    throw new InvalidOperationException(
        "The direct child route did not return its independent durable stream. "
        + $"Route: {childRequest.Uri}; version: {childRequest.StreamVersion}; "
        + $"events: {string.Join(", ", childEvents.Select(static streamEvent => streamEvent.Type))}.");
}
foreach (EveStreamEvent childEvent in childEvents)
{
    if (childEvent.Metadata is not EveStreamEventMetadata childMetadata
        || string.IsNullOrWhiteSpace(childMetadata.At)
        || childMetadata.Id is not string childIdentifier
        || !IsEventIdentifier(childIdentifier))
    {
        throw new InvalidOperationException("The child stream omitted its independent durable metadata.");
    }
}

using JsonDocument outputSchema = JsonDocument.Parse(
    """{"type":"object","properties":{"status":{"type":"string"}},"required":["status"],"additionalProperties":false}""");
EveSession structuredSession = client.CreateSession();
probeScenario = "structured output";
EveMessageResponse structuredResponse = await structuredSession.SendAsync(
    EveMessageContent.FromText("STRUCTURED_RESPONSE"),
    new EveTurnOptions { OutputSchema = outputSchema.RootElement },
    timeout.Token);
EveTurnOutcome structuredOutcome = await structuredResponse.GetOutcomeAsync(timeout.Token);
if (structuredOutcome.Status != EveTurnStatus.Waiting
    || structuredOutcome.Data is not JsonElement structuredData
    || structuredData.GetProperty("status").GetString() != "STRUCTURED_OK")
{
    throw new InvalidOperationException("The real Eve fixture did not return schema-constrained output.");
}

EveSession deliveryCorrelationSession = client.CreateSession();
probeScenario = "delivery correlation";
EveMessageResponse priorDeliveryResponse = await deliveryCorrelationSession.SendAsync(
    "STALE_CURSOR_PRIOR_DELIVERY",
    timeout.Token);
EveTurnOutcome priorDeliveryOutcome =
    await priorDeliveryResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(
    priorDeliveryOutcome,
    "delivery-correlation prior turn",
    "PRIOR_DELIVERY_RESPONSE");

EveSession staleDeliveryCorrelationSession = client.CreateSession(
    new EveSessionState
    {
        SessionId = priorDeliveryResponse.SessionId,
        StreamIndex = 0,
    });
EveMessageResponse acceptedDeliveryResponse =
    await staleDeliveryCorrelationSession.SendAsync(
        "STALE_CURSOR_ACCEPTED_DELIVERY",
        timeout.Token);
if (acceptedDeliveryResponse.DeliveryId is not string acceptedDeliveryId
    || string.IsNullOrWhiteSpace(acceptedDeliveryId))
{
    throw new InvalidOperationException(
        "The existing-session accepted response did not return a delivery identifier.");
}

EveTurnOutcome acceptedDeliveryOutcome =
    await acceptedDeliveryResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(
    acceptedDeliveryOutcome,
    "delivery-correlation accepted turn",
    "ACCEPTED_DELIVERY_RESPONSE");

if (acceptedDeliveryOutcome.Events.Any(static streamEvent =>
        streamEvent.Kind == EveStreamEventKind.MessageCompleted
        && string.Equals(
            streamEvent.Data.GetProperty("message").GetString(),
            "PRIOR_DELIVERY_RESPONSE",
            StringComparison.Ordinal)))
{
    throw new InvalidOperationException(
        "The stale-cursor response yielded events from the prior durable turn.");
}

if (!acceptedDeliveryOutcome.Events.Any(streamEvent =>
        streamEvent.Metadata?.DeliveryIds?.Contains(
            acceptedDeliveryId,
            StringComparer.Ordinal) == true))
{
    throw new InvalidOperationException(
        "The accepted turn did not expose its delivery identifier in durable event metadata.");
}

if (staleDeliveryCorrelationSession.State.StreamIndex
    <= acceptedDeliveryOutcome.Events.Count)
{
    throw new InvalidOperationException(
        "The stale-cursor response did not consume prior durable events while selecting " +
        "the accepted delivery.");
}

EveSession clientContextSession = client.CreateSession();
probeScenario = "turn-scoped client context";
EveTurnOptions clientContextOptions = new()
{
    ClientContext = EveClientContext.FromText("TURN_SCOPED_CLIENT_CONTEXT_140"),
};
EveMessageResponse clientContextResponse = await clientContextSession.SendAsync(
    EveMessageContent.FromText("VERIFY_TURN_SCOPED_CLIENT_CONTEXT"),
    clientContextOptions,
    timeout.Token);
EveTurnOutcome clientContextOutcome = await clientContextResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(
    clientContextOutcome,
    "client-context tool-loop turn",
    "CLIENT_CONTEXT_PRESENT_AFTER_TOOL");

EveMessageResponse followingTurnResponse = await clientContextSession.SendAsync(
    "VERIFY_FOLLOWING_TURN_CLIENT_CONTEXT",
    timeout.Token);
EveTurnOutcome followingTurnOutcome = await followingTurnResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(
    followingTurnOutcome,
    "client-context following turn",
    "CLIENT_CONTEXT_ABSENT_ON_FOLLOWING_TURN");

EveMessageResponse resuppliedContextResponse = await clientContextSession.SendAsync(
    EveMessageContent.FromText("VERIFY_FOLLOWING_TURN_CLIENT_CONTEXT"),
    clientContextOptions,
    timeout.Token);
EveTurnOutcome resuppliedContextOutcome =
    await resuppliedContextResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(
    resuppliedContextOutcome,
    "client-context resupplied turn",
    "CLIENT_CONTEXT_PRESENT_ON_FOLLOWING_TURN");

EveClient authorizationClient = new(
    transport,
    new EveClientOptions(baseUri.ToString())
    {
        Authentication = new EveBearerAuthentication("compatibility-user"),
    });
EveSession authorizationSession = authorizationClient.CreateSession();
probeScenario = "callback authorization";
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
    else if (streamEvent.Kind == EveStreamEventKind.TurnWaiting
        && streamEvent.Data.GetProperty("on").GetString() == "input"
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

if (authorizationCallbackSent)
{
    await foreach (EveStreamEvent streamEvent in authorizationSession.StreamAsync(timeout.Token))
    {
        authorizationEvents.Add(streamEvent);
        if (streamEvent.Kind == EveStreamEventKind.SessionWaiting)
        {
            break;
        }
    }
}

int authorizationRequiredIndex = authorizationEvents.FindIndex(
    static streamEvent => streamEvent.Kind == EveStreamEventKind.AuthorizationRequired);
int interimWaitingIndex = authorizationEvents.FindIndex(
    authorizationRequiredIndex + 1,
    static streamEvent => streamEvent.Kind == EveStreamEventKind.TurnWaiting
        && streamEvent.Data.GetProperty("on").GetString() == "input");
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
        "The callback authorization did not resume across its held input boundary. " +
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
probeScenario = "attachments";
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
probeScenario = "active turn cancellation";
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
probeScenario = "bounded catch-up";
EveMessageResponse catchUpResponse = await catchUpSession.SendAsync(
    "Return the deterministic compatibility response.",
    timeout.Token);
EveTurnOutcome catchUpOutcome = await catchUpResponse.GetOutcomeAsync(timeout.Token);
RequireSuccessfulResponse(catchUpOutcome, "catch-up turn");
EveSession catchUpReader = client.AttachSession(catchUpResponse.SessionId, 0);

int recordedBeforeCatchUp = streamRecorder.StreamRequests.Count;
List<EveStreamEvent> catchUpEvents = [];
EveProtocolException? catchUpFailure = null;

try
{
    await foreach (EveStreamEvent streamEvent in catchUpReader.StreamAsync(
        new EveStreamOptions
        {
            Follow = false,
            StartIndex = 0,
        },
        timeout.Token))
    {
        catchUpEvents.Add(streamEvent);
        if (catchUpReader.State.StreamIndex != catchUpEvents.Count)
        {
            throw new InvalidOperationException(
                "The bounded catch-up read did not expose its consumed cursor before yielding " +
                $"event {catchUpEvents.Count}: {catchUpReader.State.StreamIndex}.");
        }
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

if (!catchUpRequests[0].Uri.Contains(
        $"streamControlVersion={EveProtocol.StreamControlVersion}",
        StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "The bounded catch-up read did not negotiate renewable stream control: " +
        $"{catchUpRequests[0].Uri}.");
}

foreach (RecordedStreamRequest reconnect in catchUpRequests.Skip(1))
{
    if (reconnect.Uri.Contains("includeTailIndex", StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"A bounded catch-up reconnect requested the durable tail again: {reconnect.Uri}.");
    }

    if (!reconnect.Uri.Contains(
            $"streamControlVersion={EveProtocol.StreamControlVersion}",
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"A bounded catch-up reconnect omitted renewable stream control: {reconnect.Uri}.");
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

if (catchUpReader.State.StreamIndex != catchUpEvents.Count)
{
    throw new InvalidOperationException(
        "The bounded catch-up read did not advance the session cursor: " +
        $"{catchUpReader.State.StreamIndex} of {catchUpEvents.Count} events.");
}

EveSession approvalSession = client.CreateSession();
probeScenario = "approval park and resume";
EveMessageResponse approvalResponse = await approvalSession.SendAsync(
    "REQUEST_APPROVAL",
    timeout.Token);
EveTurnOutcome approvalOutcome = await approvalResponse.GetOutcomeAsync(timeout.Token);
if (approvalOutcome.Status != EveTurnStatus.Waiting)
{
    throw new InvalidOperationException(
        $"The approval turn did not park for human input: status={approvalOutcome.Status}.");
}

int approvalInputIndex = approvalOutcome.Events
    .ToList()
    .FindIndex(static streamEvent =>
        streamEvent.Kind == EveStreamEventKind.ActionInputAppended
        && streamEvent.Data.TryGetProperty("inputTextDelta", out var delta)
        && string.Equals(
            delta.GetString(),
            """{"reason":"compatibility"}""",
            StringComparison.Ordinal));
int approvalRequestIndex = approvalOutcome.Events
    .ToList()
    .FindIndex(static streamEvent => streamEvent.Kind == EveStreamEventKind.InputRequested);
if (approvalInputIndex < 0 || approvalRequestIndex <= approvalInputIndex)
{
    throw new InvalidOperationException(
        "The approval turn did not retain streamed tool input before its request. " +
        $"Observed: {string.Join(", ", approvalOutcome.Events.Select(static value => value.Type))}.");
}

EveStreamEvent approvalInput = approvalOutcome.Events[approvalInputIndex];
if (!string.Equals(
        approvalInput.Data.GetProperty("callId").GetString(),
        "call_approval",
        StringComparison.Ordinal)
    || approvalInput.Data.TryGetProperty("inputTextOffset", out _)
    || !string.Equals(
        approvalInput.Data.GetProperty("toolName").GetString(),
        "request_approval",
        StringComparison.Ordinal)
    || string.IsNullOrWhiteSpace(approvalInput.Data.GetProperty("turnId").GetString())
    || !approvalInput.Data.GetProperty("sequence").TryGetInt32(out _)
    || !approvalInput.Data.GetProperty("stepIndex").TryGetInt32(out _))
{
    throw new InvalidOperationException(
        "The streamed approval input did not retain its protocol-v26 delta coordinates.");
}

if (approvalOutcome.InputRequests.Count != 1
    || approvalOutcome.PendingInputRequests.Count != 1
    || approvalOutcome.Events[^1].Kind != EveStreamEventKind.TurnWaiting
    || approvalOutcome.Events[^1].Data.GetProperty("on").GetString() != "input")
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
if (resumedOutcome.PendingInputRequests.Count != 0
    || resumedOutcome.Events.Any(static streamEvent => streamEvent.Kind == EveStreamEventKind.TurnCancelled))
{
    throw new InvalidOperationException("The approved tool turn did not resume without pending input.");
}
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

EveSession cancelledApprovalSession = client.CreateSession();
probeScenario = "held approval cancellation";
EveMessageResponse cancelledApprovalResponse = await cancelledApprovalSession.SendAsync(
    "REQUEST_APPROVAL",
    timeout.Token);
EveTurnOutcome cancelledApprovalHeld =
    await cancelledApprovalResponse.GetOutcomeAsync(timeout.Token);
if (cancelledApprovalHeld.Status != EveTurnStatus.Waiting
    || cancelledApprovalHeld.PendingInputRequests.Count != 1
    || cancelledApprovalHeld.Events[^1].Kind != EveStreamEventKind.TurnWaiting)
{
    throw new InvalidOperationException("The cancellation probe did not hold an approval request.");
}

EveCancellationOutcome cancelledApproval =
    await cancelledApprovalResponse.CancelAsync(timeout.Token);
List<EveStreamEvent> cancelledApprovalEvents = [];
await foreach (EveStreamEvent streamEvent in cancelledApprovalSession.StreamAsync(timeout.Token))
{
    cancelledApprovalEvents.Add(streamEvent);
    if (streamEvent.Kind == EveStreamEventKind.SessionWaiting)
    {
        break;
    }
}

if (cancelledApproval.Status != EveCancellationStatus.Accepted
    || !cancelledApprovalEvents.Any(static streamEvent =>
        streamEvent.Kind == EveStreamEventKind.TurnCancelled)
    || cancelledApprovalEvents.Any(static streamEvent =>
        streamEvent.Kind == EveStreamEventKind.MessageCompleted))
{
    throw new InvalidOperationException("Cancelling the held input turn did not settle without executing it.");
}

EveSession resetSession = client.CreateSession();
probeScenario = "reset";
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

probeScenario = "clear and compaction";
EveCompactOutcome compact = await afterResetSession.CompactAsync(timeout.Token);
if (compact.Status != EveCompactStatus.Accepted
    || compact.SessionId != afterResetResponse.SessionId)
{
    throw new InvalidOperationException("The Eve fixture did not accept compaction for the active session.");
}

EveClearOutcome clear = await afterResetSession.ClearAsync(timeout.Token);
if (clear.Status != EveClearStatus.Accepted
    || clear.SessionId != afterResetResponse.SessionId
    || afterResetSession.State.SessionId != afterResetResponse.SessionId)
{
    throw new InvalidOperationException("The Eve fixture did not clear context without retiring the session.");
}

return 0;

static void RequireSuccessfulResponse(
    EveTurnOutcome outcome,
    string operation,
    string expectedMessage = "CONNECTION_OK")
{
    if (outcome.Status != EveTurnStatus.Waiting
        || !string.Equals(outcome.Message, expectedMessage, StringComparison.Ordinal))
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
