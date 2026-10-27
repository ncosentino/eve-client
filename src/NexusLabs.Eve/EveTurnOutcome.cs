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
        IReadOnlyList<EveInputRequest> pendingInputRequests,
        IReadOnlyList<EveInputResolution> inputResolutions,
        string sessionId,
        EveTurnStatus status)
    {
        Data = data?.Clone();
        Message = message;
        Events = events;
        InputRequests = inputRequests;
        PendingInputRequests = pendingInputRequests;
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
    /// <remarks>
    /// The latest completed answer is retained across task and input waits. Messages completed
    /// with the <c>tool-calls</c> finish reason do not replace it.
    /// Version 27 instead joins text parts named by the matched turn's reply, excluding narration.
    /// </remarks>
    public string? Message { get; }

    /// <summary>
    /// Gets all events consumed for the turn.
    /// </summary>
    public IReadOnlyList<EveStreamEvent> Events { get; }

    /// <summary>
    /// Gets all human-input requests emitted during the turn.
    /// </summary>
    /// <remarks>
    /// Version 27 projects the first question, approval, or budget descriptor per interaction ID.
    /// Use <see cref="Interactions"/> for sign-ins, unknown kinds, and complete raw descriptors.
    /// </remarks>
    public IReadOnlyList<EveInputRequest> InputRequests { get; }

    /// <summary>
    /// Gets requests observed in this response that remain unresolved at its boundary.
    /// </summary>
    /// <remarks>
    /// Authoritative <c>input.resolved</c> and <c>approval.settled</c> events remove matching
    /// requests. The first descriptor for each case-sensitive request identity remains
    /// authoritative, and repeated requests do not reopen settled identities within this
    /// response. <see cref="InputRequests"/> retains the complete observed request history.
    /// </remarks>
    public IReadOnlyList<EveInputRequest> PendingInputRequests { get; }

    /// <summary>
    /// Gets all authoritative human-input resolutions emitted while this response was consumed.
    /// </summary>
    public IReadOnlyList<EveInputResolution> InputResolutions { get; }

    /// <summary>
    /// Gets first-observed version-27 interaction descriptors, including sign-ins and unknown kinds.
    /// Legacy streams leave this collection empty.
    /// </summary>
    public IReadOnlyList<EveInteraction> Interactions { get; internal init; } = [];

    /// <summary>
    /// Gets version-27 interactions without an observed settlement.
    /// </summary>
    public IReadOnlyList<EveInteraction> PendingInteractions { get; internal init; } = [];

    /// <summary>
    /// Gets version-27 interaction settlements without inventing legacy batch coordinates.
    /// Version-27 resolutions do not populate <see cref="InputResolutions"/>.
    /// </summary>
    public IReadOnlyList<EveInteractionResolution> InteractionResolutions { get; internal init; } = [];

    /// <summary>
    /// Gets the matching version-27 delivery settlement, including unknown raw fields.
    /// </summary>
    public JsonElement? DeliverySettlement { get; internal init; }

    /// <summary>
    /// Gets the version-27 delivery outcome exactly as received, or null for a legacy response.
    /// </summary>
    public string? DeliveryOutcome => DeliverySettlement is JsonElement settlement
        ? EveV27ResponseSummary.String(settlement, "outcome")
        : null;

    /// <summary>
    /// Gets the correlated failure payload, including a synthesized delivery failure when required.
    /// </summary>
    public JsonElement? Failure { get; internal init; }

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
