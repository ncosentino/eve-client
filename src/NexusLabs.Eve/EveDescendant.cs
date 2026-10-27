namespace NexusLabs.Eve;

/// <summary>
/// Identifies a recorded version-27 child hop on its parent's authorized reader.
/// </summary>
public sealed record EveDescendant
{
    /// <summary>Initializes a child hop from the received child.opened source position.</summary>
    /// <param name="sessionId">The announced child session identifier.</param>
    /// <param name="position">The parent's source-line position, not a fact or event count.</param>
    /// <exception cref="ArgumentException">The identifier is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The position is negative.</exception>
    public EveDescendant(string sessionId, int position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        SessionId = sessionId;
        Position = position;
    }

    /// <summary>Gets the child session identifier.</summary>
    public string SessionId { get; }

    /// <summary>Gets the parent's committed line carrying child.opened.</summary>
    public int Position { get; }
}
