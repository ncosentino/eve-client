using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Wraps either a plain text message or an AI SDK-compatible user-content array.
/// </summary>
public sealed record EveMessageContent
{
    private EveMessageContent(JsonElement json)
    {
        Json = json.Clone();
    }

    /// <summary>
    /// Gets the JSON string or array sent as the eve message.
    /// </summary>
    public JsonElement Json { get; }

    /// <summary>
    /// Creates a plain text message.
    /// </summary>
    /// <param name="text">The user message.</param>
    /// <returns>A text message.</returns>
    public static EveMessageContent FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new EveMessageContent(EveJsonElementFactory.CreateString(text));
    }

    /// <summary>
    /// Creates a structured message from one or more content parts.
    /// </summary>
    /// <param name="parts">The ordered content parts.</param>
    /// <returns>A structured message.</returns>
    public static EveMessageContent FromParts(params EveContentPart[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Length == 0)
        {
            throw new ArgumentException("At least one content part is required.", nameof(parts));
        }

        return new EveMessageContent(EveJsonElementFactory.CreateArray(writer =>
        {
            foreach (EveContentPart part in parts)
            {
                ArgumentNullException.ThrowIfNull(part);
                part.Json.WriteTo(writer);
            }
        }));
    }

    /// <summary>
    /// Wraps a plain JSON string or an AI SDK-compatible content array.
    /// </summary>
    /// <param name="json">The message JSON.</param>
    /// <returns>A message that preserves the supplied JSON.</returns>
    public static EveMessageContent FromJson(JsonElement json)
    {
        if (json.ValueKind is not JsonValueKind.String and not JsonValueKind.Array)
        {
            throw new ArgumentException(
                "An eve message must be a JSON string or content array.",
                nameof(json));
        }

        return new EveMessageContent(json);
    }
}
