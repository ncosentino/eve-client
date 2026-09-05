using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NexusLabs.Eve;

/// <summary>
/// Represents one event from an eve durable NDJSON session stream.
/// </summary>
public sealed record EveStreamEvent
{
    internal EveStreamEvent(
        string type,
        EveStreamEventKind kind,
        JsonElement data,
        EveStreamEventMetadata? metadata)
    {
        Type = type;
        Kind = kind;
        Data = data.Clone();
        Metadata = metadata;
    }

    /// <summary>
    /// Gets the wire-level event type.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Gets the known event kind, or <see cref="EveStreamEventKind.Unknown"/>.
    /// </summary>
    public EveStreamEventKind Kind { get; }

    /// <summary>
    /// Gets the raw event data object.
    /// </summary>
    public JsonElement Data { get; }

    /// <summary>
    /// Gets optional durable event metadata.
    /// </summary>
    public EveStreamEventMetadata? Metadata { get; }

    /// <summary>
    /// Gets whether this event is a session-level boundary for the current turn.
    /// </summary>
    /// <remarks>
    /// An active response can continue across an interim <see cref="EveStreamEventKind.SessionWaiting"/>
    /// while callback-backed connection authorization remains pending.
    /// </remarks>
    public bool IsCurrentTurnBoundary =>
        Kind is EveStreamEventKind.SessionWaiting
            or EveStreamEventKind.SessionFailed
            or EveStreamEventKind.SessionCompleted;

    /// <summary>
    /// Gets whether this event represents an unrecovered turn or session failure.
    /// </summary>
    public bool IsFailure =>
        Kind is EveStreamEventKind.StepFailed
            or EveStreamEventKind.TurnFailed
            or EveStreamEventKind.SessionFailed;

    /// <summary>
    /// Deserializes this event's <see cref="Data"/> with caller-provided source-generated metadata.
    /// </summary>
    /// <typeparam name="TData">The expected data type.</typeparam>
    /// <param name="jsonTypeInfo">Source-generated JSON metadata for <typeparamref name="TData"/>.</param>
    /// <returns>The deserialized event data.</returns>
    public TData? DeserializeData<TData>(JsonTypeInfo<TData> jsonTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        return JsonSerializer.Deserialize(Data, jsonTypeInfo);
    }

    internal static EveStreamEvent Parse(
        string json,
        int streamVersion = 25,
        EveStreamDecoder? decoder = null)
    {
        if (streamVersion is < 21 or > 25)
        {
            throw new EveProtocolException(
                $"Unsupported eve stream protocol version '{streamVersion}'. Supported versions are 21 through 25.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out JsonElement typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                throw new EveProtocolException("An eve stream event must contain a string type.");
            }

            string? type = typeElement.GetString();
            if (string.IsNullOrWhiteSpace(type))
            {
                throw new EveProtocolException("An eve stream event type cannot be empty.");
            }

            EveStreamEventKind kind = ResolveKind(type);
            JsonElement data = root.TryGetProperty("data", out JsonElement dataElement)
                ? dataElement
                : EveJsonElementFactory.EmptyObject;

            data = NormalizeAndValidateData(kind, data, streamVersion, decoder);
            EveStreamEventMetadata? metadata = ParseMetadata(root);

            return new EveStreamEvent(type, kind, data, metadata);
        }
        catch (JsonException exception)
        {
            throw new EveProtocolException("The eve stream contained invalid JSON.", exception);
        }
    }

    private static JsonElement NormalizeAndValidateData(
        EveStreamEventKind kind,
        JsonElement data,
        int streamVersion,
        EveStreamDecoder? decoder)
    {
        return kind switch
        {
            EveStreamEventKind.MessageAppended => ValidateAndNormalizeMessageAppended(data, streamVersion, decoder),
            EveStreamEventKind.ReasoningAppended => ValidateAndNormalizeReasoningAppended(data, streamVersion, decoder),
            EveStreamEventKind.ActionInputAppended => ValidateAndNormalizeActionInputAppended(data, streamVersion, decoder),
            _ => data,
        };
    }

    private static JsonElement ValidateAndNormalizeMessageAppended(
        JsonElement data,
        int streamVersion,
        EveStreamDecoder? decoder)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("messageDelta", out JsonElement deltaElement)
            || deltaElement.ValueKind != JsonValueKind.String)
        {
            throw new EveProtocolException("A message.appended event must contain a string 'messageDelta'.");
        }

        string delta = deltaElement.GetString()!;
        bool hasSoFar = data.TryGetProperty("messageSoFar", out JsonElement soFarElement);
        if (streamVersion == 25)
        {
            if (hasSoFar)
            {
                throw new EveProtocolException("Protocol v25 message.appended events must not contain legacy 'messageSoFar'.");
            }

            return data;
        }

        if (hasSoFar)
        {
            if (soFarElement.ValueKind != JsonValueKind.String)
            {
                throw new EveProtocolException("Legacy 'messageSoFar' must be a string.");
            }

            string soFar = soFarElement.GetString()!;
            if (!soFar.EndsWith(delta, StringComparison.Ordinal))
            {
                throw new EveProtocolException(
                    $"Legacy 'messageSoFar' snapshot '{soFar}' contradicts 'messageDelta' '{delta}'.");
            }

            string? turnId = data.TryGetProperty("turnId", out JsonElement turnIdElement)
                && turnIdElement.ValueKind == JsonValueKind.String
                    ? turnIdElement.GetString()
                    : null;

            if (decoder is not null && turnId is not null)
            {
                string? prevSoFar = decoder.GetMessageSoFar(turnId);
                if (prevSoFar is not null && soFar != prevSoFar + delta)
                {
                    throw new EveProtocolException(
                        $"Legacy 'messageSoFar' snapshot '{soFar}' contradicts accumulated text '{prevSoFar + delta}'.");
                }

                decoder.SetMessageSoFar(turnId, soFar);
            }

            return RemoveProperty(data, "messageSoFar");
        }
        else
        {
            string? turnId = data.TryGetProperty("turnId", out JsonElement turnIdElement)
                && turnIdElement.ValueKind == JsonValueKind.String
                    ? turnIdElement.GetString()
                    : null;

            if (decoder is not null && turnId is not null)
            {
                string? prevSoFar = decoder.GetMessageSoFar(turnId);
                decoder.SetMessageSoFar(turnId, (prevSoFar ?? string.Empty) + delta);
            }

            return data;
        }
    }

    private static JsonElement ValidateAndNormalizeReasoningAppended(
        JsonElement data,
        int streamVersion,
        EveStreamDecoder? decoder)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("reasoningDelta", out JsonElement deltaElement)
            || deltaElement.ValueKind != JsonValueKind.String)
        {
            throw new EveProtocolException("A reasoning.appended event must contain a string 'reasoningDelta'.");
        }

        string delta = deltaElement.GetString()!;
        bool hasSoFar = data.TryGetProperty("reasoningSoFar", out JsonElement soFarElement);
        if (streamVersion == 25)
        {
            if (hasSoFar)
            {
                throw new EveProtocolException("Protocol v25 reasoning.appended events must not contain legacy 'reasoningSoFar'.");
            }

            return data;
        }

        if (hasSoFar)
        {
            if (soFarElement.ValueKind != JsonValueKind.String)
            {
                throw new EveProtocolException("Legacy 'reasoningSoFar' must be a string.");
            }

            string soFar = soFarElement.GetString()!;
            if (!soFar.EndsWith(delta, StringComparison.Ordinal))
            {
                throw new EveProtocolException(
                    $"Legacy 'reasoningSoFar' snapshot '{soFar}' contradicts 'reasoningDelta' '{delta}'.");
            }

            string? turnId = data.TryGetProperty("turnId", out JsonElement turnIdElement)
                && turnIdElement.ValueKind == JsonValueKind.String
                    ? turnIdElement.GetString()
                    : null;

            if (decoder is not null && turnId is not null)
            {
                string? prevSoFar = decoder.GetReasoningSoFar(turnId);
                if (prevSoFar is not null && soFar != prevSoFar + delta)
                {
                    throw new EveProtocolException(
                        $"Legacy 'reasoningSoFar' snapshot '{soFar}' contradicts accumulated reasoning text '{prevSoFar + delta}'.");
                }

                decoder.SetReasoningSoFar(turnId, soFar);
            }

            return RemoveProperty(data, "reasoningSoFar");
        }
        else
        {
            string? turnId = data.TryGetProperty("turnId", out JsonElement turnIdElement)
                && turnIdElement.ValueKind == JsonValueKind.String
                    ? turnIdElement.GetString()
                    : null;

            if (decoder is not null && turnId is not null)
            {
                string? prevSoFar = decoder.GetReasoningSoFar(turnId);
                decoder.SetReasoningSoFar(turnId, (prevSoFar ?? string.Empty) + delta);
            }

            return data;
        }
    }

    private static JsonElement ValidateAndNormalizeActionInputAppended(
        JsonElement data,
        int streamVersion,
        EveStreamDecoder? decoder)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("inputTextDelta", out JsonElement deltaElement)
            || deltaElement.ValueKind != JsonValueKind.String)
        {
            throw new EveProtocolException("An action.input.appended event must contain a string 'inputTextDelta'.");
        }

        string delta = deltaElement.GetString()!;
        bool hasOffset = data.TryGetProperty("inputTextOffset", out JsonElement offsetElement);
        if (streamVersion == 25)
        {
            if (hasOffset)
            {
                throw new EveProtocolException("Protocol v25 action.input.appended events must not contain legacy 'inputTextOffset'.");
            }

            return data;
        }

        if (hasOffset)
        {
            if (offsetElement.ValueKind != JsonValueKind.Number
                || !offsetElement.TryGetInt32(out int offset)
                || offset < 0)
            {
                throw new EveProtocolException("Legacy 'inputTextOffset' must be a non-negative integer.");
            }

            string? callId = data.TryGetProperty("callId", out JsonElement callIdElement)
                && callIdElement.ValueKind == JsonValueKind.String
                    ? callIdElement.GetString()
                    : null;

            if (decoder is not null && callId is not null)
            {
                int? prevOffset = decoder.GetToolInputOffset(callId);
                if (prevOffset.HasValue && offset != prevOffset.Value)
                {
                    throw new EveProtocolException(
                        $"Legacy 'inputTextOffset' {offset} contradicts expected offset {prevOffset.Value}.");
                }

                decoder.SetToolInputOffset(callId, offset + delta.Length);
            }

            return RemoveProperty(data, "inputTextOffset");
        }
        else
        {
            string? callId = data.TryGetProperty("callId", out JsonElement callIdElement)
                && callIdElement.ValueKind == JsonValueKind.String
                    ? callIdElement.GetString()
                    : null;

            if (decoder is not null && callId is not null)
            {
                int prevOffset = decoder.GetToolInputOffset(callId) ?? 0;
                decoder.SetToolInputOffset(callId, prevOffset + delta.Length);
            }

            return data;
        }
    }

    private static JsonElement RemoveProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return element;
        }

        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            writer.WriteStartObject();
            using JsonElement.ObjectEnumerator enumerator = element.EnumerateObject();
            while (enumerator.MoveNext())
            {
                JsonProperty property = enumerator.Current;
                if (!string.Equals(property.Name, propertyName, StringComparison.Ordinal))
                {
                    property.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        using JsonDocument doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private static EveStreamEventMetadata? ParseMetadata(JsonElement root)
    {
        if (!root.TryGetProperty("meta", out JsonElement metadata)
            || metadata.ValueKind != JsonValueKind.Object
            || !metadata.TryGetProperty("at", out JsonElement at)
            || at.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string? value = at.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string? id = metadata.TryGetProperty("id", out JsonElement idElement)
            && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString()
                : null;

        return new EveStreamEventMetadata(
            value,
            string.IsNullOrWhiteSpace(id) ? null : id);
    }

    private static EveStreamEventKind ResolveKind(string type) =>
        type switch
        {
            "session.started" => EveStreamEventKind.SessionStarted,
            "turn.started" => EveStreamEventKind.TurnStarted,
            "message.received" => EveStreamEventKind.MessageReceived,
            "actions.requested" => EveStreamEventKind.ActionsRequested,
            "action.input.appended" => EveStreamEventKind.ActionInputAppended,
            "input.requested" => EveStreamEventKind.InputRequested,
            "input.resolved" => EveStreamEventKind.InputResolved,
            "approval.candidate" => EveStreamEventKind.ApprovalCandidate,
            "approval.settled" => EveStreamEventKind.ApprovalSettled,
            "action.result" => EveStreamEventKind.ActionResult,
            "action.partial" => EveStreamEventKind.ActionPartial,
            "subagent.called" => EveStreamEventKind.SubagentCalled,
            "subagent.started" => EveStreamEventKind.SubagentStarted,
            "subagent.event" => EveStreamEventKind.SubagentEvent,
            "subagent.completed" => EveStreamEventKind.SubagentCompleted,
            "message.appended" => EveStreamEventKind.MessageAppended,
            "reasoning.appended" => EveStreamEventKind.ReasoningAppended,
            "message.completed" => EveStreamEventKind.MessageCompleted,
            "reasoning.completed" => EveStreamEventKind.ReasoningCompleted,
            "result.completed" => EveStreamEventKind.ResultCompleted,
            "step.started" => EveStreamEventKind.StepStarted,
            "step.completed" => EveStreamEventKind.StepCompleted,
            "step.failed" => EveStreamEventKind.StepFailed,
            "turn.completed" => EveStreamEventKind.TurnCompleted,
            "turn.failed" => EveStreamEventKind.TurnFailed,
            "turn.cancelled" => EveStreamEventKind.TurnCancelled,
            "context.cleared" => EveStreamEventKind.ContextCleared,
            "compaction.requested" => EveStreamEventKind.CompactionRequested,
            "compaction.completed" => EveStreamEventKind.CompactionCompleted,
            "authorization.required" => EveStreamEventKind.AuthorizationRequired,
            "authorization.completed" => EveStreamEventKind.AuthorizationCompleted,
            "session.waiting" => EveStreamEventKind.SessionWaiting,
            "session.failed" => EveStreamEventKind.SessionFailed,
            "session.completed" => EveStreamEventKind.SessionCompleted,
            _ => EveStreamEventKind.Unknown,
        };
}
