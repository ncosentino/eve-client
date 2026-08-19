using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NexusLabs.Eve;

/// <summary>
/// Aggregates the events and terminal values observed for one eve turn.
/// </summary>
public sealed record EveTurnOutcome
{
    internal EveTurnOutcome(
        JsonElement? data,
        string? message,
        IReadOnlyList<EveStreamEvent> events,
        IReadOnlyList<EveInputRequest> inputRequests,
        IReadOnlyList<EveInputResolution> inputResolutions,
        string sessionId,
        EveTurnStatus status)
    {
        Data = data?.Clone();
        Message = message;
        Events = events;
        InputRequests = inputRequests;
        InputResolutions = inputResolutions;
        SessionId = sessionId;
        Status = status;
    }

    /// <summary>
    /// Gets the most recent structured result emitted by the turn.
    /// </summary>
    public JsonElement? Data { get; }

    /// <summary>
    /// Gets the final completed assistant message text.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets all events consumed for the turn.
    /// </summary>
    public IReadOnlyList<EveStreamEvent> Events { get; }

    /// <summary>
    /// Gets all human-input requests emitted during the turn.
    /// </summary>
    public IReadOnlyList<EveInputRequest> InputRequests { get; }

    /// <summary>
    /// Gets all authoritative human-input resolutions emitted while this response was consumed.
    /// </summary>
    public IReadOnlyList<EveInputResolution> InputResolutions { get; }

    /// <summary>
    /// Gets the runtime-owned session identifier.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Gets how the turn ended.
    /// </summary>
    public EveTurnStatus Status { get; }

    /// <summary>
    /// Deserializes <see cref="Data"/> with caller-provided source-generated metadata.
    /// </summary>
    /// <typeparam name="TData">The expected structured result type.</typeparam>
    /// <param name="jsonTypeInfo">Source-generated JSON metadata for <typeparamref name="TData"/>.</param>
    /// <returns>The structured result, or the default value when no result was emitted.</returns>
    public TData? DeserializeData<TData>(JsonTypeInfo<TData> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return Data is JsonElement data
            ? JsonSerializer.Deserialize(data, jsonTypeInfo)
            : default;
    }
}
