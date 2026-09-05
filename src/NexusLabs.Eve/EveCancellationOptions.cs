namespace NexusLabs.Eve;

/// <summary>
/// Carries optional guards and scopes for a session-cancellation request.
/// </summary>
public sealed record EveCancellationOptions
{
    /// <summary>
    /// Gets the optional turn identifier that limits cancellation to the observed turn.
    /// </summary>
    public string? TurnId { get; init; }

    /// <summary>
    /// Gets or sets whether eve should cooperatively cancel all background tasks owned by the
    /// session. An unset value omits the field and preserves turn-only cancellation.
    /// </summary>
    public bool? CancelOwnedTasks { get; init; }
}
