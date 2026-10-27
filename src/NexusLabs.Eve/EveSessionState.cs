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
    /// Gets the next absolute source position: an event index before version 27,
    /// or a source-line index in version 27.
    /// </summary>
    public int StreamIndex { get; init; }

    /// <summary>
    /// Gets the next fact within <see cref="StreamIndex"/> when a version-27 commit
    /// has been partially consumed. Zero starts at the beginning of the line.
    /// </summary>
    public int StreamEventIndex { get; init; }

    /// <summary>
    /// Gets the observed version-27 protocol or an explicitly restored legacy version.
    /// Historical legacy cursors leave this unset to preserve their value semantics.
    /// Persist this value with the cursor when resuming an existing session.
    /// </summary>
    public int? StreamVersion { get; init; }
}
