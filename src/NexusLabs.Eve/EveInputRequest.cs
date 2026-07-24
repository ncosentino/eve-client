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
        string? display,
        bool? allowFreeform,
        IReadOnlyList<EveInputOption> options,
        JsonElement action)
    {
        RequestId = requestId;
        Prompt = prompt;
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
