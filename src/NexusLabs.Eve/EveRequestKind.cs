namespace NexusLabs.Eve;

/// <summary>
/// Identifies the eve HTTP operation for which request headers are being resolved.
/// Additional members may be added as the upstream client gains routes.
/// </summary>
public enum EveRequestKind
{
    /// <summary>
    /// A readiness request to the health route.
    /// </summary>
    Health,

    /// <summary>
    /// An agent inspection request to the info route.
    /// </summary>
    Info,

    /// <summary>
    /// A request that creates a new remote session.
    /// </summary>
    CreateSession,

    /// <summary>
    /// A request that continues an existing remote session.
    /// </summary>
    ContinueSession,

    /// <summary>
    /// A request that opens or reconnects an existing session event stream.
    /// </summary>
    StreamSession,

    /// <summary>
    /// A request that cooperatively cancels an active turn.
    /// </summary>
    CancelTurn,

    /// <summary>
    /// A caller-defined request sent through <see cref="EveClient.SendRawAsync"/>.
    /// </summary>
    Raw,
}
