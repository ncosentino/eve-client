namespace NexusLabs.Eve;

/// <summary>
/// Stores the serializable cursor required to resume an eve conversation and stream.
/// </summary>
/// <remarks>
/// eve <c>0.31.0</c> removed continuation tokens from the client protocol. A session is
/// identified only by its immutable <see cref="SessionId"/>, which the handle retains across
/// terminal events so a completed or failed conversation stays inspectable and streamable.
/// </remarks>
public sealed record EveSessionState
{
    /// <summary>
    /// Gets the runtime-owned identifier used for turns, controls, streaming, and inspection.
    /// A <see langword="null"/> value means no turn has been sent, so the session does not
    /// exist remotely yet.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the absolute number of stream events already consumed.
    /// </summary>
    public int StreamIndex { get; init; }
}
