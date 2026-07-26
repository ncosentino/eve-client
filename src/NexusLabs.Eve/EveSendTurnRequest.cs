using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Describes one user turn sent to an eve session.
/// </summary>
public sealed record EveSendTurnRequest
{
    /// <summary>
    /// Gets the optional user message.
    /// </summary>
    public EveMessageContent? Message { get; init; }

    /// <summary>
    /// Gets responses that resolve pending approvals or questions.
    /// </summary>
    public IReadOnlyList<EveInputResponse>? InputResponses { get; init; }

    /// <summary>
    /// Gets ephemeral context used only for this model call.
    /// </summary>
    public EveClientContext? ClientContext { get; init; }

    /// <summary>
    /// Gets the optional JSON Schema that the turn's structured result must satisfy.
    /// </summary>
    public JsonElement? OutputSchema { get; init; }

    /// <summary>
    /// Gets headers that apply to this POST and its stream reconnects.
    /// These values override every client-level header, including headers produced by the
    /// configured <see cref="IEveAuthentication"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Gets the reconnect policy for this turn's event stream.
    /// </summary>
    public EveStreamReconnectPolicy? StreamReconnectPolicy { get; init; }
}
