namespace NexusLabs.Eve;

/// <summary>
/// Carries durable metadata stamped onto an eve stream event.
/// </summary>
public sealed record EveStreamEventMetadata
{
    internal EveStreamEventMetadata(string at, string? id)
    {
        At = at;
        Id = id;
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
}
