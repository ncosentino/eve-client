namespace NexusLabs.Eve;

/// <summary>
/// Carries the successful response from an eve session-compaction request.
/// </summary>
public sealed record EveCompactOutcome
{
    internal EveCompactOutcome(EveCompactStatus status, string? sessionId)
    {
        Status = status;
        SessionId = sessionId;
    }

    /// <summary>
    /// Gets the compaction disposition.
    /// </summary>
    public EveCompactStatus Status { get; }

    /// <summary>
    /// Gets the session whose compaction was queued, or <see langword="null"/> when
    /// <see cref="Status"/> is <see cref="EveCompactStatus.NoActiveSession"/>.
    /// </summary>
    public string? SessionId { get; }
}
