using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Exposes validated identity fields and the complete raw payload from <c>/eve/v1/info</c>.
/// </summary>
public sealed record EveAgentInfo
{
    internal EveAgentInfo(
        string agentName,
        string modelId,
        string mode,
        int version,
        bool developmentRoutesAvailable,
        string? description,
        JsonElement raw)
    {
        AgentName = agentName;
        ModelId = modelId;
        Mode = mode;
        Version = version;
        DevelopmentRoutesAvailable = developmentRoutesAvailable;
        Description = description;
        Raw = raw.Clone();
    }

    /// <summary>
    /// Gets the authored agent name.
    /// </summary>
    public string AgentName { get; }

    /// <summary>
    /// Gets the configured model identifier.
    /// </summary>
    public string ModelId { get; }

    /// <summary>
    /// Gets the runtime mode, either <c>development</c> or <c>production</c>.
    /// </summary>
    public string Mode { get; }

    /// <summary>
    /// Gets the agent-info payload schema version.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Gets whether the server reports dev-only routes as available.
    /// </summary>
    public bool DevelopmentRoutesAvailable { get; }

    /// <summary>
    /// Gets the optional authored agent description.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the complete agent-info JSON payload for fields not projected by this version.
    /// </summary>
    public JsonElement Raw { get; }
}
