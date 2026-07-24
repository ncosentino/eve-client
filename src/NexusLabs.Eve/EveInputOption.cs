namespace NexusLabs.Eve;

/// <summary>
/// Describes one selectable answer to an eve human-input request.
/// </summary>
public sealed record EveInputOption
{
    internal EveInputOption(string id, string label, string? description, string? style)
    {
        Id = id;
        Label = label;
        Description = description;
        Style = style;
    }

    /// <summary>
    /// Gets the stable option identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the user-facing option label.
    /// </summary>
    public string Label { get; }

    /// <summary>
    /// Gets optional supporting text.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the optional presentation style hint.
    /// </summary>
    public string? Style { get; }
}
