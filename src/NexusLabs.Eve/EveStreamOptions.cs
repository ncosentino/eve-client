namespace NexusLabs.Eve;

/// <summary>
/// Configures attachment to an existing eve session stream.
/// </summary>
public sealed record EveStreamOptions
{
    /// <summary>
    /// Gets an optional absolute event index, or a negative index relative to the current tail.
    /// </summary>
    public int? StartIndex { get; init; }

    /// <summary>
    /// Gets a value indicating whether the stream keeps following live events (the default).
    /// </summary>
    /// <remarks>
    /// Set this to <see langword="false"/> for a bounded catch-up read. A bounded stream reads
    /// every event recorded through the durable tail observed when the stream opens, reconnects
    /// through interruptions without moving that bound, and then completes instead of waiting for
    /// future events. Bounded reads require a nonnegative effective start cursor and a server that
    /// reports the durable tail index.
    /// </remarks>
    public bool Follow { get; init; } = true;

    /// <summary>
    /// Gets the stream reconnect policy.
    /// </summary>
    public EveStreamReconnectPolicy? ReconnectPolicy { get; init; }
}
