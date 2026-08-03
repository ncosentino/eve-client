namespace NexusLabs.Eve;

/// <summary>
/// Carries the successful response from an eve session-context clear request.
/// </summary>
public sealed record EveClearOutcome
{
    internal EveClearOutcome(EveClearStatus status, string? sessionId)
    {
        Status = status;
        SessionId = sessionId;
    }

    /// <summary>
    /// Gets the clear disposition.
    /// </summary>
    public EveClearStatus Status { get; }

    /// <summary>
    /// Gets the session that accepted the clear, or <see langword="null"/> when
    /// <see cref="Status"/> is <see cref="EveClearStatus.NoActiveSession"/>.
    /// </summary>
    public string? SessionId { get; }
}
