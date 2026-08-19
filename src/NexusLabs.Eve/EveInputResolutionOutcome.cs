namespace NexusLabs.Eve;

/// <summary>
/// Describes the authoritative terminal outcome of an eve human-input request.
/// </summary>
public enum EveInputResolutionOutcome
{
    /// <summary>
    /// The server sent an outcome this package does not model.
    /// </summary>
    /// <remarks>
    /// Read <see cref="EveInputResolution.RawOutcome"/> for the wire value.
    /// </remarks>
    Unknown = 0,

    /// <summary>
    /// A question or session-limit prompt was answered.
    /// </summary>
    Answered,

    /// <summary>
    /// A tool approval was granted.
    /// </summary>
    Approved,

    /// <summary>
    /// A tool approval was denied.
    /// </summary>
    Denied,

    /// <summary>
    /// The request closed without an accepted response.
    /// </summary>
    Ignored,

    /// <summary>
    /// The submitted response was invalid.
    /// </summary>
    Invalid,
}
