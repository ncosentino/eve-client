namespace NexusLabs.Eve;

/// <summary>
/// Carries the successful response from an eve turn-cancellation request.
/// </summary>
public sealed record EveCancellationOutcome
{
    internal EveCancellationOutcome(string? sessionId, EveCancellationStatus status)
    {
        SessionId = sessionId;
        Status = status;
    }

    /// <summary>
    /// Gets the session targeted by the request, or <see langword="null"/> when there was no
    /// active turn to cancel.
    /// </summary>
    /// <remarks>
    /// eve <c>0.31.0</c> returns the identifier only for
    /// <see cref="EveCancellationStatus.Accepted"/>. A
    /// <see cref="EveCancellationStatus.NoActiveTurn"/> result names no session because none
    /// was cancelled.
    /// </remarks>
    public string? SessionId { get; }

    /// <summary>
    /// Gets the cancellation disposition.
    /// </summary>
    public EveCancellationStatus Status { get; }
}
