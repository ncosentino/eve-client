using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace NexusLabs.Eve;

/// <summary>
/// Represents a successful health response that does not satisfy the eve health schema.
/// </summary>
[SuppressMessage(
    "Roslynator",
    "RCS1194",
    Justification = "Binary exception serialization is obsolete in modern .NET.")]
public sealed class EveHealthResponseException : EveProtocolException
{
    private const string DefaultMessage =
        "The server returned an unrecognized eve health response.";

    /// <summary>
    /// Initializes a health-response exception without structured issues.
    /// </summary>
    public EveHealthResponseException()
        : base(DefaultMessage)
    {
        Issues = Array.Empty<EveHealthValidationIssue>();
    }

    /// <summary>
    /// Initializes a health-response exception with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public EveHealthResponseException(string message)
        : base(message)
    {
        Issues = Array.Empty<EveHealthValidationIssue>();
    }

    /// <summary>
    /// Initializes a health-response exception with a message and underlying exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The underlying JSON parsing exception.</param>
    public EveHealthResponseException(string message, Exception innerException)
        : base(message, innerException)
    {
        Issues = Array.Empty<EveHealthValidationIssue>();
    }

    internal EveHealthResponseException(IReadOnlyList<EveHealthValidationIssue> issues)
        : base(CreateMessage(issues))
    {
        Issues = new ReadOnlyCollection<EveHealthValidationIssue>([.. issues]);
    }

    /// <summary>
    /// Gets the bounded structured validation issues. Invalid JSON reports an empty collection
    /// and preserves the parser failure through <see cref="Exception.InnerException"/>.
    /// </summary>
    public IReadOnlyList<EveHealthValidationIssue> Issues { get; }

    private static string CreateMessage(IReadOnlyList<EveHealthValidationIssue> issues) =>
        issues.Count == 0
            ? DefaultMessage
            : $"{DefaultMessage} ({string.Join("; ", issues)})";
}
