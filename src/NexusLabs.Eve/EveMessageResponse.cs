using System.Runtime.CompilerServices;
using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Exposes accepted turn metadata and its single-use event stream.
/// </summary>
public sealed class EveMessageResponse : IAsyncEnumerable<EveStreamEvent>
{
    private static readonly EveCancellationOutcome NoActiveTurnOutcome =
        new(null, EveCancellationStatus.NoActiveTurn);

    private readonly Func<
        string,
        CancellationToken,
        Task<EveCancellationOutcome>> _cancelTurn;
    private readonly Func<CancellationToken, IAsyncEnumerable<EveStreamEvent>> _createStream;
    private readonly Lock _stateGate = new();
    private readonly TaskCompletionSource<TurnIdentity> _turnIdentity =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task<EveCancellationOutcome>? _cancellation;
    private int _consumed;
    private bool _settled;

    internal EveMessageResponse(
        string sessionId,
        Func<CancellationToken, IAsyncEnumerable<EveStreamEvent>> createStream,
        Func<string, CancellationToken, Task<EveCancellationOutcome>> cancelTurn)
    {
        SessionId = sessionId;
        _createStream = createStream;
        _cancelTurn = cancelTurn;
    }

    /// <summary>
    /// Gets the runtime-owned session identifier.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Requests cooperative cancellation of the exact turn represented by this response.
    /// </summary>
    /// <remarks>
    /// Start consuming this response before awaiting cancellation. The request waits for the
    /// stream to identify its turn, then sends that identifier as a guard and remains attached
    /// through the durable turn boundary. Concurrent calls share one in-flight request. The
    /// first call's token controls that request; later tokens can cancel only their caller's wait.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the queued or active cancellation request.</param>
    /// <returns>The successful cancellation disposition.</returns>
    /// <exception cref="OperationCanceledException">
    /// The cancellation token was cancelled before the request completed.
    /// </exception>
    /// <exception cref="EveClientException">The server returned a non-successful status.</exception>
    /// <exception cref="EveProtocolException">
    /// The turn-start event or cancellation response did not match the eve protocol.
    /// </exception>
    public Task<EveCancellationOutcome> CancelAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<EveCancellationOutcome> completion;
        lock (_stateGate)
        {
            if (_settled)
            {
                return Task.FromResult(NoActiveTurnOutcome);
            }

            if (_cancellation is not null)
            {
                return _cancellation.WaitAsync(cancellationToken);
            }

            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _cancellation = completion.Task;
        }

        _ = CompleteCancellationAsync(completion, cancellationToken);
        return completion.Task;
    }

    /// <summary>
    /// Consumes the event stream and aggregates its terminal values.
    /// </summary>
    /// <param name="cancellationToken">Stops local stream consumption.</param>
    /// <returns>The aggregated turn outcome.</returns>
    public async Task<EveTurnOutcome> GetOutcomeAsync(
        CancellationToken cancellationToken)
    {
        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent streamEvent in this.WithCancellation(cancellationToken))
        {
            events.Add(streamEvent);
        }

        return CreateOutcome(SessionId, events);
    }

    /// <summary>
    /// Gets the single-use event-stream enumerator.
    /// </summary>
    /// <param name="cancellationToken">Stops local stream consumption.</param>
    /// <returns>The event-stream enumerator.</returns>
    /// <exception cref="InvalidOperationException">The response was already consumed.</exception>
    public IAsyncEnumerator<EveStreamEvent> GetAsyncEnumerator(
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _consumed, 1) != 0)
        {
            throw new InvalidOperationException(
                "An eve message response can only be consumed once.");
        }

        return ObserveStreamAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

    private async Task CompleteCancellationAsync(
        TaskCompletionSource<EveCancellationOutcome> completion,
        CancellationToken cancellationToken)
    {
        TurnIdentity turnIdentity;
        try
        {
            turnIdentity = await _turnIdentity.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ResetFailedCancellation(completion.Task);
            completion.TrySetCanceled(cancellationToken);
            return;
        }
        if (turnIdentity.Error is not null)
        {
            ResetFailedCancellation(completion.Task);
            completion.TrySetException(turnIdentity.Error);
            return;
        }

        if (turnIdentity.TurnId is not string turnId)
        {
            completion.TrySetResult(NoActiveTurnOutcome);
            return;
        }

        try
        {
            EveCancellationOutcome outcome = await _cancelTurn(turnId, cancellationToken);
            completion.TrySetResult(outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ResetFailedCancellation(completion.Task);
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            ResetFailedCancellation(completion.Task);
            completion.TrySetException(exception);
        }
    }

    private async IAsyncEnumerable<EveStreamEvent> ObserveStreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Connection names are path-derived protocol identifiers, so correlation must mirror
        // JavaScript Set semantics and distinguish names that differ only by case.
#pragma warning disable NLF0016
        HashSet<string> pendingAuthorizations =
            new HashSet<string>(StringComparer.Ordinal);
#pragma warning restore NLF0016

        try
        {
            await foreach (EveStreamEvent streamEvent in
                _createStream(cancellationToken).WithCancellation(cancellationToken))
            {
                bool isCurrentTurnBoundary = IsResponseTurnBoundary(
                    streamEvent,
                    pendingAuthorizations);
                if (streamEvent.Kind == EveStreamEventKind.TurnStarted)
                {
                    try
                    {
                        _turnIdentity.TrySetResult(new TurnIdentity(
                            ReadTurnId(streamEvent),
                            null));
                    }
                    catch (EveProtocolException exception)
                    {
                        _turnIdentity.TrySetResult(new TurnIdentity(null, exception));
                        throw;
                    }
                }
                else if (isCurrentTurnBoundary)
                {
                    lock (_stateGate)
                    {
                        _settled = true;
                    }

                    _turnIdentity.TrySetResult(default);
                }

                yield return streamEvent;
                if (isCurrentTurnBoundary)
                {
                    yield break;
                }
            }
        }
        finally
        {
            _turnIdentity.TrySetResult(default);
        }
    }

    private static bool IsResponseTurnBoundary(
        EveStreamEvent streamEvent,
        HashSet<string> pendingAuthorizations)
    {
        UpdatePendingAuthorizations(streamEvent, pendingAuthorizations);

        return streamEvent.Kind switch
        {
            EveStreamEventKind.SessionWaiting => pendingAuthorizations.Count == 0,
            EveStreamEventKind.SessionFailed or EveStreamEventKind.SessionCompleted => true,
            _ => false,
        };
    }

    private static void UpdatePendingAuthorizations(
        EveStreamEvent streamEvent,
        HashSet<string> pendingAuthorizations)
    {
        if (streamEvent.Kind == EveStreamEventKind.AuthorizationRequired)
        {
            if (streamEvent.Data.ValueKind != JsonValueKind.Object)
            {
                throw new EveProtocolException(
                    "An eve authorization.required event must contain an object data value.");
            }

            if (!streamEvent.Data.TryGetProperty("webhookUrl", out JsonElement webhookUrl))
            {
                return;
            }

            if (webhookUrl.ValueKind != JsonValueKind.String)
            {
                throw new EveProtocolException(
                    "An eve authorization.required event webhookUrl must be a string.");
            }

            pendingAuthorizations.Add(RequireEventString(
                streamEvent.Data,
                "name",
                "authorization.required"));
            return;
        }

        if (streamEvent.Kind != EveStreamEventKind.AuthorizationCompleted
            || pendingAuthorizations.Count == 0)
        {
            return;
        }

        if (streamEvent.Data.ValueKind != JsonValueKind.Object)
        {
            throw new EveProtocolException(
                "An eve authorization.completed event must contain an object data value.");
        }

        pendingAuthorizations.Remove(RequireEventString(
            streamEvent.Data,
            "name",
            "authorization.completed"));
    }

    private void ResetFailedCancellation(Task<EveCancellationOutcome> cancellation)
    {
        lock (_stateGate)
        {
            if (!_settled && ReferenceEquals(_cancellation, cancellation))
            {
                _cancellation = null;
            }
        }
    }

    private static string ReadTurnId(EveStreamEvent streamEvent)
    {
        if (!streamEvent.Data.TryGetProperty("turnId", out JsonElement turnId)
            || turnId.ValueKind != JsonValueKind.String)
        {
            throw new EveProtocolException(
                "An eve turn.started event did not contain a string turnId.");
        }

        string? value = turnId.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new EveProtocolException(
                "An eve turn.started event did not contain a string turnId.");
        }

        return value;
    }

    private readonly record struct TurnIdentity(
        string? TurnId,
        EveProtocolException? Error);

    private static EveTurnOutcome CreateOutcome(
        string sessionId,
        IReadOnlyList<EveStreamEvent> events)
    {
        JsonElement? data = null;
        string? message = null;
        List<EveInputRequest> inputRequests = [];
        List<EveInputResolution> inputResolutions = [];

        foreach (EveStreamEvent streamEvent in events)
        {
            switch (streamEvent.Kind)
            {
                case EveStreamEventKind.ResultCompleted:
                    if (streamEvent.Data.TryGetProperty("result", out JsonElement result))
                    {
                        data = result.Clone();
                    }

                    break;
                case EveStreamEventKind.MessageCompleted:
                    if (streamEvent.Data.TryGetProperty("finishReason", out JsonElement finishReason)
                        && finishReason.ValueKind == JsonValueKind.String
                        && !string.Equals(
                            finishReason.GetString(),
                            "tool-calls",
                            StringComparison.Ordinal))
                    {
                        message = streamEvent.Data.TryGetProperty(
                                "message",
                                out JsonElement completedMessage)
                            && completedMessage.ValueKind == JsonValueKind.String
                                ? completedMessage.GetString()
                                : null;
                    }

                    break;
                case EveStreamEventKind.InputRequested:
                    AddInputRequests(streamEvent.Data, inputRequests);
                    break;
                case EveStreamEventKind.InputResolved:
                    AddInputResolutions(streamEvent.Data, inputResolutions);
                    break;
            }
        }

        EveTurnStatus status = events
            .Select(static streamEvent => streamEvent.Kind)
            .Reverse()
            .FirstOrDefault(static kind =>
                kind is EveStreamEventKind.SessionWaiting
                    or EveStreamEventKind.SessionFailed
                    or EveStreamEventKind.SessionCompleted) switch
        {
            EveStreamEventKind.SessionWaiting => EveTurnStatus.Waiting,
            EveStreamEventKind.SessionFailed => EveTurnStatus.Failed,
            _ => EveTurnStatus.Completed,
        };

        return new EveTurnOutcome(
            data,
            message,
            events,
            inputRequests,
            inputResolutions,
            sessionId,
            status);
    }

    private static void AddInputRequests(
        JsonElement data,
        ICollection<EveInputRequest> inputRequests)
    {
        if (!data.TryGetProperty("requests", out JsonElement requests)
            || requests.ValueKind != JsonValueKind.Array)
        {
            throw new EveProtocolException(
                "An input.requested event did not contain a requests array.");
        }

        for (int requestIndex = 0; requestIndex < requests.GetArrayLength(); requestIndex++)
        {
            JsonElement request = requests[requestIndex];
            string requestId = RequireString(request, "requestId");
            string prompt = RequireString(request, "prompt");
            string? rawKind = ReadInputRequestKind(request);
            string? display = OptionalString(request, "display");
            bool? allowFreeform = OptionalBoolean(request, "allowFreeform");
            JsonElement action = request.TryGetProperty("action", out JsonElement actionValue)
                ? actionValue.Clone()
                : EveJsonElementFactory.EmptyObject;
            List<EveInputOption> options = [];

            if (request.TryGetProperty("options", out JsonElement optionValues))
            {
                if (optionValues.ValueKind != JsonValueKind.Array)
                {
                    throw new EveProtocolException(
                        "An eve input request options value must be an array.");
                }

                for (int optionIndex = 0; optionIndex < optionValues.GetArrayLength(); optionIndex++)
                {
                    JsonElement option = optionValues[optionIndex];
                    options.Add(new EveInputOption(
                        RequireString(option, "id"),
                        RequireString(option, "label"),
                        OptionalString(option, "description"),
                        OptionalString(option, "style")));
                }
            }

            inputRequests.Add(new EveInputRequest(
                requestId,
                prompt,
                ResolveInputRequestKind(rawKind),
                rawKind,
                display,
                allowFreeform,
                options,
                action));
        }
    }

    // An absent discriminator is an eve version that predates it; a present non-string value is a
    // malformed request that must not be reported as a legacy server.
    private static string? ReadInputRequestKind(JsonElement request)
    {
        if (!request.TryGetProperty("kind", out JsonElement kind))
        {
            return null;
        }

        if (kind.ValueKind != JsonValueKind.String || kind.GetString() is not string value)
        {
            throw new EveProtocolException(
                "An eve input request kind must be a string.");
        }

        return value;
    }

    private static EveInputRequestKind ResolveInputRequestKind(string? rawKind) =>
        rawKind switch
        {
            "question" => EveInputRequestKind.Question,
            "tool-approval" => EveInputRequestKind.ToolApproval,
            "session-limit" => EveInputRequestKind.SessionLimit,
            _ => EveInputRequestKind.Unknown,
        };

    private static void AddInputResolutions(
        JsonElement data,
        ICollection<EveInputResolution> inputResolutions)
    {
        if (!data.TryGetProperty("resolutions", out JsonElement resolutions)
            || resolutions.ValueKind != JsonValueKind.Array)
        {
            throw new EveProtocolException(
                "An input.resolved event did not contain a resolutions array.");
        }

        string turnId = RequireEventString(data, "turnId", "input.resolved");
        int stepIndex = RequireEventInt32(data, "stepIndex", "input.resolved");
        int sequence = RequireEventInt32(data, "sequence", "input.resolved");
        for (int resolutionIndex = 0;
            resolutionIndex < resolutions.GetArrayLength();
            resolutionIndex++)
        {
            JsonElement resolution = resolutions[resolutionIndex];
            if (resolution.ValueKind != JsonValueKind.Object)
            {
                throw new EveProtocolException(
                    "An input.resolved resolution must be an object.");
            }

            string requestId = RequireEventString(
                resolution,
                "requestId",
                "input.resolved resolution");
            string rawKind = RequireEventString(
                resolution,
                "kind",
                "input.resolved resolution");
            string rawOutcome = RequireEventString(
                resolution,
                "outcome",
                "input.resolved resolution");
            EveInputResponse? response = ReadInputResolutionResponse(resolution);
            inputResolutions.Add(new EveInputResolution(
                requestId,
                ResolveInputRequestKind(rawKind),
                rawKind,
                ResolveInputResolutionOutcome(rawOutcome),
                rawOutcome,
                response,
                turnId,
                stepIndex,
                sequence,
                resolution));
        }
    }

    private static EveInputResponse? ReadInputResolutionResponse(JsonElement resolution)
    {
        if (!resolution.TryGetProperty("response", out JsonElement response))
        {
            return null;
        }

        if (response.ValueKind != JsonValueKind.Object)
        {
            throw new EveProtocolException(
                "An input.resolved response must be an object.");
        }

        string requestId = RequireEventString(
            response,
            "requestId",
            "input.resolved response");
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new EveProtocolException(
                "An input.resolved response requestId cannot be empty.");
        }

        return new EveInputResponse(
            requestId,
            OptionalEventString(response, "optionId", "input.resolved response"),
            OptionalEventString(response, "text", "input.resolved response"));
    }

    private static EveInputResolutionOutcome ResolveInputResolutionOutcome(string rawOutcome) =>
        rawOutcome switch
        {
            "answered" => EveInputResolutionOutcome.Answered,
            "approved" => EveInputResolutionOutcome.Approved,
            "denied" => EveInputResolutionOutcome.Denied,
            "ignored" => EveInputResolutionOutcome.Ignored,
            "invalid" => EveInputResolutionOutcome.Invalid,
            _ => EveInputResolutionOutcome.Unknown,
        };

    private static string RequireEventString(
        JsonElement parent,
        string propertyName,
        string context)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not string result)
        {
            throw new EveProtocolException(
                $"An {context} value is missing string property '{propertyName}'.");
        }

        return result;
    }

    private static int RequireEventInt32(
        JsonElement parent,
        string propertyName,
        string context)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int result))
        {
            throw new EveProtocolException(
                $"An {context} value is missing integer property '{propertyName}'.");
        }

        return result;
    }

    private static string? OptionalEventString(
        JsonElement parent,
        string propertyName,
        string context)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new EveProtocolException(
                $"An {context} property '{propertyName}' must be a string.");
        }

        return value.GetString();
    }

    private static string RequireString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || value.GetString() is not string result)
        {
            throw new EveProtocolException(
                $"An eve input request is missing string property '{propertyName}'.");
        }

        return result;
    }

    private static string? OptionalString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? OptionalBoolean(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
