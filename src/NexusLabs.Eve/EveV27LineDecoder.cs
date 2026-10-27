using System.Text.Json;

namespace NexusLabs.Eve;

internal sealed class EveV27LineDecoder(int startIndex, string initialAt = "1970-01-01T00:00:00.000Z")
{
    internal string LastCommitAt { get; private set; } = initialAt;

    internal int NextLine { get; private set; } = startIndex;

    internal bool StreamEnded { get; private set; }

    internal bool LeaseEnded { get; private set; }

    internal IReadOnlyList<EveStreamEvent> Decode(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new EveProtocolException("A version-27 stream line must be an object.");
            }

            if (root.TryGetProperty("$eve", out JsonElement control))
            {
                ReadControl(root, control);
                return [];
            }

            int position = NextLine;
            NextLine = checked(position + 1);
            if (root.TryGetProperty("facts", out JsonElement facts))
            {
                if (facts.ValueKind != JsonValueKind.Array || facts.GetArrayLength() == 0
                    || !root.TryGetProperty("at", out JsonElement at)
                    || at.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(at.GetString()))
                {
                    throw new EveProtocolException(
                        "A version-27 commit requires a timestamp and nonempty facts array.");
                }

                LastCommitAt = at.GetString()!;
                EveStreamEvent[] events = new EveStreamEvent[facts.GetArrayLength()];
                for (int index = 0; index < events.Length; index++)
                {
                    events[index] = ReadEvent(facts[index], position, index, index == events.Length - 1);
                }

                return events;
            }

            if (root.TryGetProperty("progress", out JsonElement progress))
            {
                return [ReadEvent(progress, position, 0, true)];
            }

            throw new EveProtocolException(root.TryGetProperty("type", out _)
                ? "A version-27 stream contained a legacy event line. Start a new session."
                : "A version-27 source line must contain facts or progress.");
        }
        catch (JsonException exception)
        {
            throw new EveProtocolException("The version-27 stream contained invalid JSON.", exception);
        }
        catch (OverflowException exception)
        {
            throw new EveProtocolException("The version-27 source position exceeded the supported range.", exception);
        }
    }

    private void ReadControl(JsonElement root, JsonElement control)
    {
        if (control.ValueKind != JsonValueKind.String)
        {
            throw new EveProtocolException("A version-27 transport record requires a string $eve.");
        }

        switch (control.GetString())
        {
            case "position":
                if (!root.TryGetProperty("next", out JsonElement next)
                    || next.ValueKind != JsonValueKind.Number
                    || !next.TryGetInt32(out int position) || position < 0)
                {
                    throw new EveProtocolException("A position marker requires a nonnegative next position.");
                }

                NextLine = Math.Max(NextLine, position);
                break;
            case "stream.lease-ended":
                LeaseEnded = true;
                break;
            case "stream.ended":
                StreamEnded = true;
                break;
        }
    }

    private EveStreamEvent ReadEvent(JsonElement record, int line, int index, bool endOfLine)
    {
        JsonElement raw = EveJsonElementFactory.CreateObject(writer =>
        {
            writer.WriteString("at", LastCommitAt);
            writer.WriteBoolean("endOfLine", endOfLine);
            writer.WritePropertyName("position");
            writer.WriteStartObject();
            writer.WriteNumber("line", line);
            writer.WriteNumber("index", index);
            writer.WriteEndObject();
        });
        EveStreamEventMetadata metadata = new(
            LastCommitAt, null, null, raw, new EveStreamPosition(line, index), endOfLine);
        return EveStreamEvent.ParseVersion27(record, metadata);
    }
}
