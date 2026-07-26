namespace NexusLabs.Eve;

/// <summary>
/// Describes the successful outcome of an eve session-reset request.
/// </summary>
public enum EveResetStatus
{
    /// <summary>
    /// The durable session that owned the continuation token was retired.
    /// </summary>
    Reset,

    /// <summary>
    /// No durable session owned the continuation token, so nothing was retired.
    /// </summary>
    NoActiveSession,
}
