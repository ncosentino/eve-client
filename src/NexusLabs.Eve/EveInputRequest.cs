using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Describes a question or approval that must be answered before an eve turn can continue.
/// </summary>
public sealed record EveInputRequest
{
    internal EveInputRequest(
        string requestId,
        string prompt,
        EveInputRequestKind kind,
        string? rawKind,
        string? display,
        bool? allowFreeform,
        IReadOnlyList<EveInputOption> options,
        JsonElement action)
    {
        RequestId = requestId;
        Prompt = prompt;
        Kind = kind;
        RawKind = rawKind;
        Display = display;
        AllowFreeform = allowFreeform;
        Options = options;
        Action = action;
    }

    /// <summary>
    /// Gets the stable request identifier.
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// Gets the prompt to present to the user.
    /// </summary>
    public string Prompt { get; }

    /// <summary>
    /// Gets the framework-owned request source used to route, render, and answer this request.
    /// </summary>
    /// <remarks>
    /// Prefer this discriminator over inferring intent from <see cref="Display"/>,
    /// <see cref="Options"/>, or the tool name in <see cref="Action"/>. It reports
    /// <see cref="EveInputRequestKind.Unknown"/> when the server sends no discriminator or one this
    /// package does not model; <see cref="RawKind"/> then carries the wire value.
    /// </remarks>
    public EveInputRequestKind Kind { get; }

    /// <summary>
    /// Gets the discriminator exactly as the server sent it, or <see langword="null"/> when it sent none.
    /// </summary>
    /// <remarks>
    /// eve versions before the discriminator was introduced omit it entirely, so a
    /// <see langword="null"/> value is a legacy server rather than an unrecognized kind.
    /// </remarks>
    public string? RawKind { get; }

    /// <summary>
    /// Gets the optional rendering hint, such as <c>confirmation</c>, <c>select</c>, or <c>text</c>.
    /// </summary>
    public string? Display { get; }

    /// <summary>
    /// Gets whether a free-form answer is accepted.
    /// </summary>
    public bool? AllowFreeform { get; }

    /// <summary>
    /// Gets the selectable answer options.
    /// </summary>
    public IReadOnlyList<EveInputOption> Options { get; }

    /// <summary>
    /// Gets the raw action request associated with this input request.
    /// </summary>
    public JsonElement Action { get; }
}
