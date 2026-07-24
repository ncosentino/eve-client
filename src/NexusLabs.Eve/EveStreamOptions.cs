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
    /// Gets the stream reconnect policy.
    /// </summary>
    public EveStreamReconnectPolicy? ReconnectPolicy { get; init; }
}
