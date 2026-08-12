namespace NexusLabs.Eve;

/// <summary>
/// Identifies how an eve agent resolves the model it calls.
/// </summary>
public enum EveAgentModelRouting
{
    /// <summary>
    /// The agent reported no routing, or a routing kind newer than this package.
    /// </summary>
    /// <remarks>
    /// eve reported no routing before <c>0.33.0</c>, where the field was optional, so this is
    /// the expected value for a concrete model on an earlier agent. Read
    /// <see cref="EveAgentInfo.RawModelRouting"/> to inspect an unrecognized kind.
    /// </remarks>
    Unknown,

    /// <summary>
    /// The model is served through the AI gateway.
    /// </summary>
    Gateway,

    /// <summary>
    /// The model is served directly by an external provider.
    /// </summary>
    External,

    /// <summary>
    /// The model is selected at runtime, so the agent reports no model identifier.
    /// </summary>
    Dynamic,
}
