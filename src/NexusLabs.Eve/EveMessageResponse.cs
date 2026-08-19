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
        try
        {
            await foreach (EveStreamEvent streamEvent in
                _createStream(cancellationToken).WithCancellation(cancellationToken))
            {
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
                else if (streamEvent.IsCurrentTurnBoundary)
                {
                    lock (_stateGate)
                    {
                        _settled = true;
                    }

                    _turnIdentity.TrySetResult(default);
                }

                yield return streamEvent;
            }
        }
        finally
        {
            _turnIdentity.TrySetResult(default);
        }
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

        return new EveTurnOutcome(data, message, events, inputRequests, sessionId, status);
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
