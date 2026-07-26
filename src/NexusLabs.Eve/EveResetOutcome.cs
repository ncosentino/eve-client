namespace NexusLabs.Eve;

/// <summary>
/// Carries the successful response from an eve session-reset request.
/// </summary>
public sealed record EveResetOutcome
{
    internal EveResetOutcome(EveResetStatus status, string? previousSessionId)
    {
        Status = status;
        PreviousSessionId = previousSessionId;
    }

    /// <summary>
    /// Gets the reset disposition.
    /// </summary>
    public EveResetStatus Status { get; }

    /// <summary>
    /// Gets the retired session identifier, or <see langword="null"/> when
    /// <see cref="Status"/> is <see cref="EveResetStatus.NoActiveSession"/>.
    /// </summary>
    public string? PreviousSessionId { get; }
}
