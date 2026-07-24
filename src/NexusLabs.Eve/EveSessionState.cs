namespace NexusLabs.Eve;

/// <summary>
/// Stores the serializable cursor required to resume an eve conversation and stream.
/// </summary>
public sealed record EveSessionState
{
    /// <summary>
    /// Gets the channel-owned token used to send the next user turn.
    /// </summary>
    public string? ContinuationToken { get; init; }

    /// <summary>
    /// Gets the runtime-owned identifier used for streaming, inspection, and cancellation.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the absolute number of stream events already consumed.
    /// </summary>
    public int StreamIndex { get; init; }
}
