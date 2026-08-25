namespace NexusLabs.Eve;

/// <summary>
/// Describes one violation in a successful eve health response.
/// </summary>
public sealed record EveHealthValidationIssue
{
    internal EveHealthValidationIssue(string path, string message)
    {
        Path = path;
        Message = message;
    }

    /// <summary>
    /// Gets the dot-separated response path, or an empty string for a root-level violation.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the validation message.
    /// </summary>
    public string Message { get; }

    /// <inheritdoc />
    public override string ToString() =>
        Path.Length == 0
            ? Message
            : $"{Path}: {Message}";
}
