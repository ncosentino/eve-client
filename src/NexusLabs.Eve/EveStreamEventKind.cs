namespace NexusLabs.Eve;

/// <summary>
/// Identifies a known event in the eve durable message stream.
/// </summary>
public enum EveStreamEventKind
{
    /// <summary>
    /// The event type is newer than or otherwise unknown to this package.
    /// </summary>
    Unknown,

    /// <summary>
    /// A durable session started.
    /// </summary>
    SessionStarted,

    /// <summary>
    /// A runtime turn started.
    /// </summary>
    TurnStarted,

    /// <summary>
    /// A user message was received.
    /// </summary>
    MessageReceived,

    /// <summary>
    /// One or more actions were requested.
    /// </summary>
    ActionsRequested,

    /// <summary>
    /// Human input was requested.
    /// </summary>
    InputRequested,

    /// <summary>
    /// An action produced a result.
    /// </summary>
    ActionResult,

    /// <summary>
    /// A remote subagent was called.
    /// </summary>
    SubagentCalled,

    /// <summary>
    /// An inline subagent started.
    /// </summary>
    SubagentStarted,

    /// <summary>
    /// An inline subagent emitted a child event.
    /// </summary>
    SubagentEvent,

    /// <summary>
    /// An inline subagent completed.
    /// </summary>
    SubagentCompleted,

    /// <summary>
    /// Assistant text was appended.
    /// </summary>
    MessageAppended,

    /// <summary>
    /// Reasoning text was appended.
    /// </summary>
    ReasoningAppended,

    /// <summary>
    /// An assistant message completed.
    /// </summary>
    MessageCompleted,

    /// <summary>
    /// A reasoning block completed.
    /// </summary>
    ReasoningCompleted,

    /// <summary>
    /// A structured result completed.
    /// </summary>
    ResultCompleted,

    /// <summary>
    /// A model step started.
    /// </summary>
    StepStarted,

    /// <summary>
    /// A model step completed.
    /// </summary>
    StepCompleted,

    /// <summary>
    /// A model step failed.
    /// </summary>
    StepFailed,

    /// <summary>
    /// A turn completed.
    /// </summary>
    TurnCompleted,

    /// <summary>
    /// A turn failed.
    /// </summary>
    TurnFailed,

    /// <summary>
    /// A turn was cancelled.
    /// </summary>
    TurnCancelled,

    /// <summary>
    /// Durable model-message history was cleared while the session remained intact.
    /// </summary>
    ContextCleared,

    /// <summary>
    /// Session-history compaction was requested.
    /// </summary>
    CompactionRequested,

    /// <summary>
    /// Session-history compaction completed.
    /// </summary>
    CompactionCompleted,

    /// <summary>
    /// Connection authorization is required.
    /// </summary>
    AuthorizationRequired,

    /// <summary>
    /// Connection authorization completed.
    /// </summary>
    AuthorizationCompleted,

    /// <summary>
    /// The session is waiting for another user message.
    /// </summary>
    SessionWaiting,

    /// <summary>
    /// The session failed.
    /// </summary>
    SessionFailed,

    /// <summary>
    /// The session completed successfully.
    /// </summary>
    SessionCompleted,
}
