namespace NexusLabs.Eve;

/// <summary>
/// Describes the successful outcome of a cooperative eve turn-cancellation request.
/// </summary>
public enum EveCancellationStatus
{
    /// <summary>
    /// The active turn accepted the cancellation signal.
    /// </summary>
    Accepted,

    /// <summary>
    /// The session no longer had an active turn to cancel.
    /// </summary>
    NoActiveTurn,
}
