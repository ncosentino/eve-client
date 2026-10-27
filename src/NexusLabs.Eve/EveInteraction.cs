using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Preserves a version-27 interaction's first descriptor. Identities are case-sensitive.
/// </summary>
/// <remarks>
/// <see cref="Raw"/> is the complete interaction.opened data, retaining subject, origin,
/// audience, future request kinds and unknown fields. Sign-ins are interactions, not legacy
/// authorization waits; callback completion is delivered independently.
/// </remarks>
public sealed record EveInteraction
{
    internal EveInteraction(JsonElement data)
    {
        Raw = data.Clone();
        InteractionId = data.GetProperty("interactionId").GetString()!;
        Request = data.GetProperty("request").Clone();
    }

    /// <summary>Gets the server-issued interaction identity.</summary>
    public string InteractionId { get; }

    /// <summary>Gets the complete request descriptor, including unknown fields.</summary>
    public JsonElement Request { get; }

    /// <summary>Gets the open-ended request kind.</summary>
    public string? Kind => EveV27ResponseSummary.String(Request, "kind");

    /// <summary>Gets the prompt supplied by the server.</summary>
    public string? Prompt => EveV27ResponseSummary.String(Request, "prompt");

    /// <summary>Gets the sign-in callback URL, when this interaction supplies one.</summary>
    public string? CallbackUrl => Request.TryGetProperty("signIn", out JsonElement signIn)
        ? EveV27ResponseSummary.String(signIn, "callbackUrl")
        : null;

    /// <summary>Gets the complete interaction.opened data.</summary>
    public JsonElement Raw { get; }
}
