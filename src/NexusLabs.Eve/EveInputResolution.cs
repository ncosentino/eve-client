using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Represents one authoritative terminal resolution from an eve human-input batch.
/// </summary>
public sealed record EveInputResolution
{
    internal EveInputResolution(
        string requestId,
        EveInputRequestKind kind,
        string rawKind,
        EveInputResolutionOutcome outcome,
        string rawOutcome,
        EveInputResponse? response,
        string turnId,
        int stepIndex,
        int sequence,
        JsonElement raw)
    {
        RequestId = requestId;
        Kind = kind;
        RawKind = rawKind;
        Outcome = outcome;
        RawOutcome = rawOutcome;
        Response = response;
        TurnId = turnId;
        StepIndex = stepIndex;
        Sequence = sequence;
        Raw = raw.Clone();
    }

    /// <summary>
    /// Gets the stable identifier of the resolved request.
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// Gets the framework-owned request source.
    /// </summary>
    public EveInputRequestKind Kind { get; }

    /// <summary>
    /// Gets the request source exactly as the server sent it.
    /// </summary>
    public string RawKind { get; }

    /// <summary>
    /// Gets the authoritative terminal outcome.
    /// </summary>
    public EveInputResolutionOutcome Outcome { get; }

    /// <summary>
    /// Gets the terminal outcome exactly as the server sent it.
    /// </summary>
    public string RawOutcome { get; }

    /// <summary>
    /// Gets the accepted response, or <see langword="null"/> when the request resolved without one.
    /// </summary>
    public EveInputResponse? Response { get; }

    /// <summary>
    /// Gets the turn identifier from the original pending-input batch.
    /// </summary>
    public string TurnId { get; }

    /// <summary>
    /// Gets the step index from the original pending-input batch.
    /// </summary>
    public int StepIndex { get; }

    /// <summary>
    /// Gets the sequence number from the original pending-input batch.
    /// </summary>
    public int Sequence { get; }

    /// <summary>
    /// Gets the complete raw resolution object.
    /// </summary>
    /// <remarks>
    /// Batch coordinates remain on the owning event's <see cref="EveStreamEvent.Data"/> and are
    /// projected separately by <see cref="TurnId"/>, <see cref="StepIndex"/>, and
    /// <see cref="Sequence"/>.
    /// </remarks>
    public JsonElement Raw { get; }
}
