using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Wraps one AI SDK-compatible user-content part.
/// </summary>
public sealed record EveContentPart
{
    private EveContentPart(JsonElement json)
    {
        Json = json.Clone();
    }

    /// <summary>
    /// Gets the JSON object sent to eve.
    /// </summary>
    public JsonElement Json { get; }

    /// <summary>
    /// Creates a text content part.
    /// </summary>
    /// <param name="text">The user text.</param>
    /// <returns>A text content part.</returns>
    public static EveContentPart CreateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new EveContentPart(EveJsonElementFactory.CreateObject(writer =>
        {
            writer.WriteString("type", "text");
            writer.WriteString("text", text);
        }));
    }

    /// <summary>
    /// Creates a file content part from a URL or data URL.
    /// </summary>
    /// <param name="data">The file URL or inline data URL.</param>
    /// <param name="mediaType">The file media type.</param>
    /// <param name="filename">The optional filename.</param>
    /// <returns>A file content part.</returns>
    public static EveContentPart CreateFile(
        string data,
        string mediaType,
        string? filename = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        return new EveContentPart(EveJsonElementFactory.CreateObject(writer =>
        {
            writer.WriteString("type", "file");
            writer.WriteString("data", data);
            writer.WriteString("mediaType", mediaType);
            if (!string.IsNullOrWhiteSpace(filename))
            {
                writer.WriteString("filename", filename);
            }
        }));
    }

    /// <summary>
    /// Creates an inline file content part from bytes.
    /// </summary>
    /// <param name="bytes">The file bytes.</param>
    /// <param name="mediaType">The file media type.</param>
    /// <param name="filename">The optional filename.</param>
    /// <returns>A file content part containing a base64 data URL.</returns>
    public static EveContentPart CreateFile(
        ReadOnlySpan<byte> bytes,
        string mediaType,
        string? filename = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        string data = $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}";
        return CreateFile(data, mediaType, filename);
    }

    /// <summary>
    /// Creates an image content part from a URL or data URL.
    /// </summary>
    /// <param name="image">The image URL or inline data URL.</param>
    /// <param name="mediaType">The optional image media type.</param>
    /// <returns>An image content part.</returns>
    public static EveContentPart CreateImage(string image, string? mediaType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        return new EveContentPart(EveJsonElementFactory.CreateObject(writer =>
        {
            writer.WriteString("type", "image");
            writer.WriteString("image", image);
            if (!string.IsNullOrWhiteSpace(mediaType))
            {
                writer.WriteString("mediaType", mediaType);
            }
        }));
    }

    /// <summary>
    /// Wraps a future or custom AI SDK content-part object.
    /// </summary>
    /// <param name="json">The content-part JSON object.</param>
    /// <returns>A content part that preserves the supplied JSON.</returns>
    public static EveContentPart FromJson(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("An eve content part must be a JSON object.", nameof(json));
        }

        return new EveContentPart(json);
    }
}
