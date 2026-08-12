using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Carries the settings shared by sending a message and responding to human input.
/// </summary>
/// <remarks>
/// eve <c>0.31.0</c> requires a turn to carry either a message or input responses, never both,
/// so the payload is supplied to <see cref="EveSession.SendAsync(EveMessageContent, EveTurnOptions, System.Threading.CancellationToken)"/>
/// or <see cref="EveSession.RespondAsync(IReadOnlyList{EveInputResponse}, EveTurnOptions, System.Threading.CancellationToken)"/>
/// directly. Keeping it off this type makes the rejected combination impossible to express.
/// </remarks>
public sealed record EveTurnOptions
{
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
    /// These values override non-protected client-level headers but cannot replace
    /// authentication-owned or otherwise protected headers.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Gets explicit overrides for protected headers on this POST and its stream reconnects.
    /// Every name must also appear in
    /// <see cref="EveClientOptions.AllowedProtectedHeaderOverrides"/>.
    /// Never populate this collection directly from untrusted inbound headers.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ProtectedHeaderOverrides { get; init; }

    /// <summary>
    /// Gets the reconnect policy for this turn's event stream.
    /// </summary>
    public EveStreamReconnectPolicy? StreamReconnectPolicy { get; init; }

    /// <summary>
    /// Gets how eve handles this message when the session already has an active turn.
    /// </summary>
    /// <remarks>
    /// This applies only to a message sent to an existing session. It is omitted when the turn
    /// creates the session, because a new session has no active turn, and when the turn carries
    /// only input responses, matching the upstream client. Leaving this unset sends no policy, so
    /// eve <c>0.33.0</c> and later steer the active turn.
    /// </remarks>
    public EveTurnPolicy? TurnPolicy { get; init; }
}
