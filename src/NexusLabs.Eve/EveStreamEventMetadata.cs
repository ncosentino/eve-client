using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Carries durable metadata stamped onto an eve stream event.
/// </summary>
public sealed record EveStreamEventMetadata
{
    internal EveStreamEventMetadata(
        string at,
        string? id,
        IReadOnlyList<string>? deliveryIds,
        JsonElement raw)
    {
        At = at;
        Id = id;
        DeliveryIds = deliveryIds;
        Raw = raw.Clone();
    }

    /// <summary>
    /// Gets the server-provided event timestamp.
    /// </summary>
    public string At { get; }

    /// <summary>
    /// Gets the durable event identifier, or <see langword="null"/> when the server did not stamp one.
    /// </summary>
    /// <remarks>
    /// eve stamps this identifier once, before the event is persisted, so rewinding, reconnecting,
    /// or replaying a finished session yields the same value. A retried step is not a replay: it is
    /// emitted again under a new identifier. Events persisted before stream protocol version 20
    /// carry no identifier and therefore report <see langword="null"/>; they cannot be deduplicated.
    /// </remarks>
    public string? Id { get; }

    /// <summary>
    /// Gets the ordered message-delivery identifiers associated with this event, or
    /// <see langword="null"/> when the server did not stamp delivery correlation metadata.
    /// </summary>
    /// <remarks>
    /// The order and duplicates are preserved exactly as received. A turn may contain more than
    /// one identifier when the server coalesces multiple accepted messages into one delivery.
    /// </remarks>
    public IReadOnlyList<string>? DeliveryIds { get; }

    /// <summary>
    /// Gets the complete server-provided metadata object.
    /// </summary>
    public JsonElement Raw { get; }
}
