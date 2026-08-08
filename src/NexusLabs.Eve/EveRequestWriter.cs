using System.Buffers;
using System.Text.Json;

namespace NexusLabs.Eve;

internal static class EveRequestWriter
{
    internal static byte[] WriteTurn(EveSendTurnRequest request, bool isCreate)
    {
        ValidateTurn(request, isCreate);

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();

            if (request.Message is not null)
            {
                writer.WritePropertyName("message");
                request.Message.Json.WriteTo(writer);
            }

            if (request.InputResponses is { Count: > 0 })
            {
                writer.WritePropertyName("inputResponses");
                writer.WriteStartArray();
                foreach (EveInputResponse response in request.InputResponses)
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
            }

            if (request.ClientContext is not null)
            {
                writer.WritePropertyName("clientContext");
                request.ClientContext.Json.WriteTo(writer);
            }

            if (request.OutputSchema is JsonElement outputSchema)
            {
                writer.WritePropertyName("outputSchema");
                outputSchema.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
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

    private static void ValidateTurn(EveSendTurnRequest request, bool isCreate)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool hasMessage = request.Message is not null;
        bool hasResponses = request.InputResponses is { Count: > 0 };
        if (!hasMessage && !hasResponses)
        {
            throw new ArgumentException(
                "A turn requires a non-empty message, input response, or both.",
                nameof(request));
        }

        if (isCreate && !hasMessage)
        {
            throw new ArgumentException(
                "A new eve session must start with a message.",
                nameof(request));
        }

        if (request.OutputSchema is JsonElement outputSchema
            && outputSchema.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "The eve output schema must be a JSON object.",
                nameof(request));
        }
    }
}
