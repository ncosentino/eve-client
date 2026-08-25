using System.Diagnostics.CodeAnalysis;

namespace NexusLabs.Eve;

/// <summary>
/// Represents a successful HTTP response that does not satisfy the eve protocol contract.
/// </summary>
[SuppressMessage(
    "Roslynator",
    "RCS1194",
    Justification = "Binary exception serialization is obsolete in modern .NET.")]
public class EveProtocolException : IOException
{
    /// <summary>
    /// Initializes a protocol exception.
    /// </summary>
    public EveProtocolException()
    {
    }

    /// <summary>
    /// Initializes a protocol exception with an error message.
    /// </summary>
    /// <param name="message">The protocol error message.</param>
    public EveProtocolException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a protocol exception with an error message and underlying exception.
    /// </summary>
    /// <param name="message">The protocol error message.</param>
    /// <param name="innerException">The underlying parsing or transport exception.</param>
    public EveProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
