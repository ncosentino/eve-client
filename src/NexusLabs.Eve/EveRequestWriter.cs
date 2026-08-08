using System.Buffers;
using System.Text.Json;

namespace NexusLabs.Eve;

internal static class EveRequestWriter
{
    internal static byte[] WriteTurn(
        EveSendTurnRequest request,
        EveTurnPayloadKind payloadKind,
        bool isCreate)
    {
        ValidateTurn(request, payloadKind, isCreate);

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();

            if (payloadKind == EveTurnPayloadKind.Message)
            {
                writer.WritePropertyName("message");
                request.Message!.Json.WriteTo(writer);
            }
            else
            {
                writer.WritePropertyName("inputResponses");
                writer.WriteStartArray();
                foreach (EveInputResponse response in request.InputResponses!)
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

    // eve 0.31.0 answers a body carrying both fields with HTTP 400
    // ('message' and 'inputResponses' are mutually exclusive), so the conflict is rejected
    // before any network call rather than surfacing as a server error.
    private static void ValidateTurn(
        EveSendTurnRequest request,
        EveTurnPayloadKind payloadKind,
        bool isCreate)
    {
        ArgumentNullException.ThrowIfNull(request);

        bool hasMessage = request.Message is not null;
        bool hasResponses = request.InputResponses is { Count: > 0 };

        if (request.OutputSchema is JsonElement outputSchema
            && outputSchema.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                "The eve output schema must be a JSON object.",
                nameof(request));
        }

        if (payloadKind == EveTurnPayloadKind.Message)
        {
            if (!hasMessage)
            {
                throw new ArgumentException(
                    "A sent turn requires a message.",
                    nameof(request));
            }

            if (hasResponses)
            {
                throw new ArgumentException(
                    "A turn cannot carry both a message and input responses. " +
                    "Use RespondAsync to resolve pending input.",
                    nameof(request));
            }

            return;
        }

        if (!hasResponses)
        {
            throw new ArgumentException(
                "A response turn requires at least one input response.",
                nameof(request));
        }

        if (hasMessage)
        {
            throw new ArgumentException(
                "A turn cannot carry both a message and input responses. " +
                "Use SendAsync to send a message.",
                nameof(request));
        }

        if (isCreate)
        {
            throw new ArgumentException(
                "A new eve session must start with a message.",
                nameof(request));
        }
    }
}
