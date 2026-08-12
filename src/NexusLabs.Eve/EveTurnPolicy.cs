namespace NexusLabs.Eve;

/// <summary>
/// Selects how eve handles a message sent to a session that already has an active turn.
/// </summary>
/// <remarks>
/// eve <c>0.33.0</c> made <see cref="Steer"/> the server-side default. A client that sends no
/// policy therefore steers. Send <see cref="Queue"/> to keep the wait-for-completion behavior
/// that eve applied before <c>0.33.0</c>.
/// </remarks>
public enum EveTurnPolicy
{
    /// <summary>
    /// Waits for the active turn to finish before the new message runs.
    /// </summary>
    Queue,

    /// <summary>
    /// Cancels the active turn and replaces it with the new message after eve durably
    /// buffers that message.
    /// </summary>
    Steer,
}
