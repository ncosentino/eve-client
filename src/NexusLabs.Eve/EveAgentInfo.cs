using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Exposes validated identity fields and the complete raw payload from <c>/eve/v1/info</c>.
/// </summary>
public sealed record EveAgentInfo
{
    internal EveAgentInfo(
        string agentName,
        string? modelId,
        EveAgentModelRouting modelRouting,
        string? rawModelRouting,
        string mode,
        int version,
        bool developmentRoutesAvailable,
        string? description,
        JsonElement raw)
    {
        AgentName = agentName;
        ModelId = modelId;
        ModelRouting = modelRouting;
        RawModelRouting = rawModelRouting;
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
    /// Gets the configured model identifier, or <see langword="null"/> when the agent selects
    /// its model dynamically.
    /// </summary>
    /// <remarks>
    /// eve <c>0.33.0</c> reports a dynamic model as
    /// <see cref="EveAgentModelRouting.Dynamic"/> with no identifier, rather than through a
    /// placeholder. This value is <see langword="null"/> exactly when
    /// <see cref="ModelRouting"/> is <see cref="EveAgentModelRouting.Dynamic"/>.
    /// </remarks>
    public string? ModelId { get; }

    /// <summary>
    /// Gets how the agent resolves the model it calls.
    /// </summary>
    public EveAgentModelRouting ModelRouting { get; }

    /// <summary>
    /// Gets the routing kind exactly as the agent reported it, or <see langword="null"/> when
    /// the agent reported no routing.
    /// </summary>
    public string? RawModelRouting { get; }

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
