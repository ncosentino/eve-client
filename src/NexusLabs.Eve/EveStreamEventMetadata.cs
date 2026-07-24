namespace NexusLabs.Eve;

/// <summary>
/// Carries durable metadata stamped onto an eve stream event.
/// </summary>
public sealed record EveStreamEventMetadata
{
    internal EveStreamEventMetadata(string at)
    {
        At = at;
    }

    /// <summary>
    /// Gets the server-provided event timestamp.
    /// </summary>
    public string At { get; }
}
