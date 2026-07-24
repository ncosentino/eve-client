namespace NexusLabs.Eve;

/// <summary>
/// Describes one HTTP request before dynamic eve headers are resolved.
/// </summary>
public readonly record struct EveHttpRequestContext
{
    internal EveHttpRequestContext(EveRequestKind kind)
    {
        Kind = kind;
    }

    /// <summary>
    /// Gets the logical eve operation being requested.
    /// </summary>
    public EveRequestKind Kind { get; }
}
