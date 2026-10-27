using System.Text.Json;

namespace NexusLabs.Eve;

internal static class EveV27ResponseSummary
{
    internal static EveTurnOutcome Create(
        string sessionId,
        string? deliveryId,
        IReadOnlyList<EveStreamEvent> events)
    {
        // Protocol identities use JavaScript Map's case-sensitive equality.
#pragma warning disable NLF0016
        Dictionary<string, JsonElement> parts = new(StringComparer.Ordinal);
        Dictionary<string, JsonElement> turns = new(StringComparer.Ordinal);
        Dictionary<string, JsonElement> results = new(StringComparer.Ordinal);
        Dictionary<string, string?> replies = new(StringComparer.Ordinal);
        Dictionary<string, EveInteraction> interactions = new(StringComparer.Ordinal);
        HashSet<string> settledInteractions = new(StringComparer.Ordinal);
#pragma warning restore NLF0016
        List<EveInteractionResolution> resolutions = [];
        JsonElement? settlement = null;
        JsonElement? sessionEnd = null;
        string? consumedBy = null;
        foreach (EveStreamEvent fact in events)
        {
            JsonElement data = fact.Data;
            switch (fact.Kind)
            {
                case EveStreamEventKind.DeliveryConsumed when String(data, "deliveryId") == deliveryId:
                    consumedBy = String(data, "turnId");
                    break;
                case EveStreamEventKind.DeliverySettled when String(data, "deliveryId") == deliveryId:
                    settlement = data;
                    break;
                case EveStreamEventKind.SessionEnded:
                    sessionEnd = data;
                    break;
                case EveStreamEventKind.TurnSettled:
                    turns[data.GetProperty("turnId").GetString()!] = data;
                    ObserveReply(data, parts, replies, results);
                    break;
                case EveStreamEventKind.ContentCompleted:
                    parts[data.GetProperty("partId").GetString()!] = data;
                    if (String(data, "kind") == "result"
                        && data.TryGetProperty("value", out JsonElement contentValue)
                        && fact.Scope is JsonElement scope
                        && String(scope, "turnId") is string resultTurn)
                    {
                        results[resultTurn] = contentValue;
                    }

                    break;
                case EveStreamEventKind.InteractionOpened:
                    EveInteraction interaction = new(data);
                    interactions.TryAdd(interaction.InteractionId, interaction);
                    break;
                case EveStreamEventKind.InteractionSettled:
                    EveInteractionResolution resolution = new(data);
                    resolutions.Add(resolution);
                    settledInteractions.Add(resolution.InteractionId);
                    break;
            }
        }

        if (settlement is null)
        {
            throw new EveProtocolException(
                "The stream ended before the accepted delivery settled.");
        }

        string? outcome = settlement is JsonElement delivered ? String(delivered, "outcome") : null;
        string? turnId = settlement is JsonElement terminal
            ? String(terminal, "turnId") ?? consumedBy
            : consumedBy;
        JsonElement turn = default;
        if (turnId is not null)
        {
            turns.TryGetValue(turnId, out turn);
        }

        string? turnOutcome = String(turn, "outcome");
        EveTurnStatus status = outcome switch
        {
            "awaiting-input" => EveTurnStatus.Waiting,
            "failed" or "refused" => EveTurnStatus.Failed,
            "applied" or "ignored" => EveTurnStatus.Completed,
            "handled" => turnOutcome switch
            {
                "failed" => EveTurnStatus.Failed,
                "cancelled" => EveTurnStatus.Cancelled,
                _ => EveTurnStatus.Completed,
            },
            _ => EveTurnStatus.Failed,
        };
        string? message = null;
        JsonElement? result = null;
        if (outcome is "handled" or "awaiting-input" && turnId is not null)
        {
            replies.TryGetValue(turnId, out message);
            if (results.TryGetValue(turnId, out JsonElement scopedResult))
            {
                result = scopedResult;
            }
        }

        JsonElement? failure = null;
        if (status == EveTurnStatus.Failed)
        {
            failure = Error(turn, false)
                ?? (sessionEnd is JsonElement end ? Error(end, true) : null);
            if (failure is null)
            {
                string code = outcome == "refused" ? "DELIVERY_REFUSED" : "DELIVERY_FAILED";
                string? reason = settlement is JsonElement failed ? String(failed, "reason") : null;
                string failureMessage = reason
                    ?? (outcome == "refused" ? "The delivery was refused." : "The delivery failed.");
                failure = EveJsonElementFactory.CreateObject(writer =>
                {
                    writer.WriteString("code", code);
                    writer.WriteString("message", failureMessage);
                });
            }
        }

        EveInteraction[] pending = interactions.Values
            .Where(item => !settledInteractions.Contains(item.InteractionId)).ToArray();
        EveInputRequest[] requests = interactions.Values.Select(ProjectInput)
            .OfType<EveInputRequest>().ToArray();
        EveInputRequest[] pendingRequests = pending.Select(ProjectInput)
            .OfType<EveInputRequest>().ToArray();
        return new EveTurnOutcome(
            result, message, events,
            requests, pendingRequests, [], sessionId, status)
        {
            Interactions = interactions.Values.ToArray(),
            PendingInteractions = pending,
            InteractionResolutions = resolutions,
            DeliverySettlement = settlement?.Clone(),
            Failure = failure?.Clone(),
        };
    }

    internal static string? String(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void ObserveReply(
        JsonElement turn,
        Dictionary<string, JsonElement> parts,
        Dictionary<string, string?> replies,
        Dictionary<string, JsonElement> results)
    {
        string turnId = turn.GetProperty("turnId").GetString()!;
        List<string> text = [];
        if (turn.TryGetProperty("reply", out JsonElement reply))
        {
            foreach (JsonElement partId in reply.EnumerateArray())
            {
                if (!parts.TryGetValue(partId.GetString()!, out JsonElement part)
                    || !part.TryGetProperty("value", out JsonElement value))
                {
                    continue;
                }

                if (String(part, "kind") == "text" && value.ValueKind == JsonValueKind.String)
                {
                    text.Add(value.GetString()!);
                }
                else if (String(part, "kind") == "result")
                {
                    results[turnId] = value;
                }
            }
        }

        replies[turnId] = text.Count == 0 ? null : string.Join("\n", text);
    }

    private static JsonElement? Error(JsonElement data, bool session)
    {
        if (String(data, "outcome") != "failed")
        {
            return null;
        }

        return data.TryGetProperty("error", out JsonElement error) ? error
            : EveJsonElementFactory.CreateObject(writer =>
            {
                writer.WriteString("code", session ? "SESSION_FAILED" : "TURN_FAILED");
                writer.WriteString("message", session ? "The session failed." : "The turn failed.");
            });
    }

    private static EveInputRequest? ProjectInput(EveInteraction interaction)
    {
        EveInputRequestKind kind = interaction.Kind switch
        {
            "question" => EveInputRequestKind.Question,
            "approval" => EveInputRequestKind.ToolApproval,
            "budget" => EveInputRequestKind.SessionLimit,
            _ => EveInputRequestKind.Unknown,
        };
        if (kind == EveInputRequestKind.Unknown)
        {
            return null;
        }

        JsonElement request = interaction.Request;
        List<EveInputOption> options = [];
        if (request.TryGetProperty("options", out JsonElement choices))
        {
            foreach (JsonElement choice in choices.EnumerateArray())
            {
                options.Add(new EveInputOption(
                    choice.GetProperty("id").GetString()!,
                    choice.GetProperty("label").GetString()!,
                    String(choice, "description"), String(choice, "style")));
            }
        }

        bool? freeform = request.TryGetProperty("allowFreeform", out JsonElement allow)
            ? allow.GetBoolean() : null;
        return new EveInputRequest(
            interaction.InteractionId, interaction.Prompt!, kind, interaction.Kind,
            String(request, "display"), freeform, options, interaction.Raw);
    }
}
