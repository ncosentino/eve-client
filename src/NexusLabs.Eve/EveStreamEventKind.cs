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
    /// One or more human-input requests reached authoritative terminal outcomes.
    /// </summary>
    /// <remarks>
    /// Stream protocol <c>23</c> emits this after eve accepts a pending-input batch and before
    /// the resumed <see cref="StepStarted"/> event. Resolutions may intentionally omit an
    /// accepted response.
    /// </remarks>
    InputResolved,

    /// <summary>
    /// One responder-bound approval candidate reached a lifecycle outcome.
    /// </summary>
    /// <remarks>
    /// eve <c>0.34.0</c> emits this for each responder attempt on an approval request, carrying
    /// a stable candidate identifier and an outcome of <c>pending</c>, <c>rejected</c>,
    /// <c>failed</c>, <c>timed-out</c>, or <c>stale</c>. A terminal outcome may also carry a
    /// reason. Candidate events precede <see cref="ApprovalSettled"/> for the same request.
    /// </remarks>
    ApprovalCandidate,

    /// <summary>
    /// An approval request reached its terminal settlement.
    /// </summary>
    /// <remarks>
    /// eve <c>0.34.0</c> emits this once per approval request with an outcome of
    /// <c>approved</c> or <c>cancelled</c>. It follows that request's
    /// <see cref="ApprovalCandidate"/> events.
    /// </remarks>
    ApprovalSettled,

    /// <summary>
    /// An action produced a result.
    /// </summary>
    ActionResult,

    /// <summary>
    /// An action produced a preliminary output snapshot that a later
    /// <see cref="ActionResult"/> supersedes.
    /// </summary>
    /// <remarks>
    /// Stream protocol <c>21</c> emits this for each non-terminal snapshot yielded by an
    /// async-generator tool. Only the terminal <see cref="ActionResult"/> is exposed to the
    /// model, so treat these as provisional display state.
    /// </remarks>
    ActionPartial,

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

    /// <summary>
    /// A model appended text while streaming one tool call's input.
    /// </summary>
    /// <remarks>
    /// Stream protocol <c>24</c> reports the text delta, its zero-based UTF-16 code-unit
    /// offset, and the tool-call, turn, step, and sequence coordinates. The complete payload
    /// remains available through <see cref="EveStreamEvent.Data"/>.
    /// </remarks>
    ActionInputAppended,
}
