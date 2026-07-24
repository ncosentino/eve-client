using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Wraps ephemeral context supplied to one eve model call.
/// </summary>
public sealed record EveClientContext
{
    private EveClientContext(JsonElement json)
    {
        Json = json.Clone();
    }

    /// <summary>
    /// Gets the JSON value sent as client context.
    /// </summary>
    public JsonElement Json { get; }

    /// <summary>
    /// Creates one text context message.
    /// </summary>
    /// <param name="text">The ephemeral context text.</param>
    /// <returns>A text client context.</returns>
    public static EveClientContext FromText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new EveClientContext(EveJsonElementFactory.CreateString(text));
    }

    /// <summary>
    /// Creates multiple text context messages.
    /// </summary>
    /// <param name="messages">The ordered context messages.</param>
    /// <returns>An array client context.</returns>
    public static EveClientContext FromMessages(params string[] messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        return new EveClientContext(EveJsonElementFactory.CreateArray(writer =>
        {
            foreach (string message in messages)
            {
                ArgumentNullException.ThrowIfNull(message);
                writer.WriteStringValue(message);
            }
        }));
    }

    /// <summary>
    /// Wraps a JSON string, string array, or object as client context.
    /// </summary>
    /// <param name="json">The client-context JSON.</param>
    /// <returns>A client context that preserves the supplied JSON.</returns>
    public static EveClientContext FromJson(JsonElement json)
    {
        bool valid = json.ValueKind is JsonValueKind.String or JsonValueKind.Object;
        if (json.ValueKind == JsonValueKind.Array)
        {
            valid = true;
            for (int index = 0; index < json.GetArrayLength(); index++)
            {
                if (json[index].ValueKind != JsonValueKind.String)
                {
                    valid = false;
                    break;
                }
            }
        }

        if (!valid)
        {
            throw new ArgumentException(
                "Eve client context must be a string, string array, or JSON object.",
                nameof(json));
        }

        return new EveClientContext(json);
    }
}
