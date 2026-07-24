using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Exposes accepted turn metadata and its single-use event stream.
/// </summary>
public sealed class EveMessageResponse : IAsyncEnumerable<EveStreamEvent>
{
    private readonly Func<CancellationToken, IAsyncEnumerable<EveStreamEvent>> _createStream;
    private int _consumed;

    internal EveMessageResponse(
        string? continuationToken,
        string sessionId,
        Func<CancellationToken, IAsyncEnumerable<EveStreamEvent>> createStream)
    {
        ContinuationToken = continuationToken;
        SessionId = sessionId;
        _createStream = createStream;
    }

    /// <summary>
    /// Gets the continuation token returned when the turn was accepted.
    /// </summary>
    public string? ContinuationToken { get; }

    /// <summary>
    /// Gets the runtime-owned session identifier.
    /// </summary>
    public string SessionId { get; }

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

        return _createStream(cancellationToken).GetAsyncEnumerator(cancellationToken);
    }

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
                display,
                allowFreeform,
                options,
                action));
        }
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
