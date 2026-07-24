namespace NexusLabs.Eve;

/// <summary>
/// Carries the successful response from an eve turn-cancellation request.
/// </summary>
public sealed record EveCancellationOutcome
{
    internal EveCancellationOutcome(string sessionId, EveCancellationStatus status)
    {
        SessionId = sessionId;
        Status = status;
    }

    /// <summary>
    /// Gets the session targeted by the request.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// Gets the cancellation disposition.
    /// </summary>
    public EveCancellationStatus Status { get; }
}
