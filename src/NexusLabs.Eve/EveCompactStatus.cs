namespace NexusLabs.Eve;

/// <summary>
/// Describes the successful outcome of an eve session-compaction request.
/// </summary>
public enum EveCompactStatus
{
    /// <summary>
    /// Context compaction was queued for the durable session that owns the
    /// continuation token.
    /// </summary>
    Accepted,

    /// <summary>
    /// No durable session owned the continuation token, so nothing was compacted.
    /// </summary>
    NoActiveSession,
}
