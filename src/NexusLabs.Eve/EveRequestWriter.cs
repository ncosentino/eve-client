using System.Buffers;
using System.Text.Json;

namespace NexusLabs.Eve;

internal static class EveRequestWriter
{
    internal static byte[] WriteMessageTurn(
        EveMessageContent message,
        EveTurnOptions? options)
    {
        ArgumentNullException.ThrowIfNull(message);
        ValidateOptions(options);

        return Write(
            writer =>
            {
                writer.WritePropertyName("message");
                message.Json.WriteTo(writer);
            },
            options);
    }

    internal static byte[] WriteResponseTurn(
        IReadOnlyList<EveInputResponse> inputResponses,
        EveTurnOptions? options)
    {
        ArgumentNullException.ThrowIfNull(inputResponses);
        ValidateOptions(options);
        if (inputResponses.Count == 0)
        {
            throw new ArgumentException(
                "A response turn requires at least one input response.",
                nameof(inputResponses));
        }

        return Write(
            writer =>
            {
                writer.WritePropertyName("inputResponses");
                writer.WriteStartArray();
                foreach (EveInputResponse response in inputResponses)
                {
                    writer.WriteStartObject();
                    writer.WriteString("requestId", response.RequestId);
                    if (response.OptionId is not null)
                    {
                        writer.WriteString("optionId", response.OptionId);
                    }

                    if (response.Text is not null)
                    {
                        writer.WriteString("text", response.Text);
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            },
            options);
    }

    internal static byte[] WriteCancel(string turnId)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("turnId", turnId);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    // Only the caller's chosen payload is serialized, so eve 0.31.0's rejection of a body
    // carrying both message and inputResponses cannot be triggered by this client.
    private static byte[] Write(Action<Utf8JsonWriter> writePayload, EveTurnOptions? options)
    {
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writePayload(writer);

            if (options?.ClientContext is not null)
            {
                writer.WritePropertyName("clientContext");
                options.ClientContext.Json.WriteTo(writer);
            }

            if (options?.OutputSchema is JsonElement outputSchema)
            {
                writer.WritePropertyName("outputSchema");
                outputSchema.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void ValidateOptions(EveTurnOptions? options)
    {
        if (options?.OutputSchema is JsonElement outputSchema
            && outputSchema.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "The eve output schema must be a JSON object.",
                nameof(options));
        }
    }
}
