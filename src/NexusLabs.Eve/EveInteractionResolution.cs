using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Preserves an authoritative version-27 interaction settlement, without legacy batch coordinates.
/// </summary>
public sealed record EveInteractionResolution
{
    internal EveInteractionResolution(JsonElement data)
    {
        Raw = data.Clone();
        InteractionId = data.GetProperty("interactionId").GetString()!;
        Outcome = data.GetProperty("outcome").GetString()!;
    }

    /// <summary>Gets the case-sensitive interaction identity.</summary>
    public string InteractionId { get; }

    /// <summary>Gets the open-ended settlement outcome exactly as received.</summary>
    public string Outcome { get; }

    /// <summary>Gets the complete settlement, retaining reason, cause, response and unknown fields.</summary>
    public JsonElement Raw { get; }
}
