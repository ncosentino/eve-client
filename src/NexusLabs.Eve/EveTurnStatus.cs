namespace NexusLabs.Eve;

/// <summary>
/// Describes how an eve turn ended.
/// </summary>
public enum EveTurnStatus
{
    /// <summary>
    /// The session completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The session failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The session is waiting for another user message or input response.
    /// </summary>
    Waiting,
}
