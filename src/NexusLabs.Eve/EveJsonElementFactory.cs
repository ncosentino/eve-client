using System.Buffers;
using System.Text.Json;

namespace NexusLabs.Eve;

internal static class EveJsonElementFactory
{
    internal static JsonElement EmptyObject { get; } = CreateObject(static _ => { });

    internal static JsonElement CreateString(string value)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStringValue(value);
        }

        return Parse(buffer);
    }

    internal static JsonElement CreateObject(Action<Utf8JsonWriter> writeProperties)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writeProperties(writer);
            writer.WriteEndObject();
        }

        return Parse(buffer);
    }

    internal static JsonElement CreateArray(Action<Utf8JsonWriter> writeItems)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartArray();
            writeItems(writer);
            writer.WriteEndArray();
        }

        return Parse(buffer);
    }

    private static JsonElement Parse(ArrayBufferWriter<byte> buffer)
    {
        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
