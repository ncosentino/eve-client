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
    /// Gets whether this event settles the current turn.
    /// </summary>
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

    internal static EveStreamEvent Parse(string json)
    {
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

            JsonElement data = root.TryGetProperty("data", out JsonElement dataElement)
                ? dataElement
                : EveJsonElementFactory.EmptyObject;
            EveStreamEventMetadata? metadata = ParseMetadata(root);

            return new EveStreamEvent(type, ResolveKind(type), data, metadata);
        }
        catch (JsonException exception)
        {
            throw new EveProtocolException("The eve stream contained invalid JSON.", exception);
        }
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
            "input.requested" => EveStreamEventKind.InputRequested,
            "action.result" => EveStreamEventKind.ActionResult,
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
