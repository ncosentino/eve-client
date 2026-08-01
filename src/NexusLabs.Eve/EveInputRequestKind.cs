namespace NexusLabs.Eve;

/// <summary>
/// Identifies the framework-owned source of an eve human-input request.
/// </summary>
/// <remarks>
/// eve stamps this discriminator on every input request so consumers can route, render, and answer
/// a request without inferring its purpose from option shapes, display hints, or tool names.
/// </remarks>
public enum EveInputRequestKind
{
    /// <summary>
    /// The server did not send a recognized discriminator.
    /// </summary>
    /// <remarks>
    /// This covers both an eve version that predates the discriminator and a future value this
    /// package does not model. Read <see cref="EveInputRequest.RawKind"/> to tell them apart:
    /// it is <see langword="null"/> when the server sent nothing and carries the wire value
    /// otherwise.
    /// </remarks>
    Unknown = 0,

    /// <summary>
    /// The agent asked the user a question.
    /// </summary>
    Question,

    /// <summary>
    /// The agent needs approval before running a tool.
    /// </summary>
    ToolApproval,

    /// <summary>
    /// The session reached a configured limit and needs a decision before continuing.
    /// </summary>
    /// <remarks>
    /// Options such as <c>continue</c> and <c>stop</c> belong to this kind. They are not an
    /// approve/deny tool prompt even when they arrive with a confirmation display hint.
    /// </remarks>
    SessionLimit,
}
