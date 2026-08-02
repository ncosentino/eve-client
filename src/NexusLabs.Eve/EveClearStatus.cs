namespace NexusLabs.Eve;

/// <summary>
/// Describes the successful outcome of an eve session-context clear request.
/// </summary>
public enum EveClearStatus
{
    /// <summary>
    /// The durable session accepted the context-clear request.
    /// </summary>
    Accepted,

    /// <summary>
    /// No durable session owned the continuation token, so nothing was cleared.
    /// </summary>
    NoActiveSession,
}
