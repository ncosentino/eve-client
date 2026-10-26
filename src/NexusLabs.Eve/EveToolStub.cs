using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Declares a creation-time tool rule with one outcome or an ordered, nonempty sequence.
/// </summary>
public sealed class EveToolStub
{
    /// <summary>
    /// Initializes a rule with one outcome.
    /// </summary>
    /// <param name="id">A nonempty identifier unique within the session's rules.</param>
    /// <param name="tool">A tool name, optionally qualified by a local subagent path.</param>
    /// <param name="outcome">The single response or thrown error.</param>
    /// <param name="match">Input property names mapped to JSON Schema objects or booleans.</param>
    /// <exception cref="ArgumentException">An identifier, target, or constraint is invalid.</exception>
    /// <exception cref="ArgumentNullException">An outcome or required argument is null.</exception>
    public EveToolStub(
        string id,
        string tool,
        EveToolStubOutcome outcome,
        IReadOnlyDictionary<string, JsonElement>? match = null)
        : this(id, tool, [outcome], match, false)
    {
    }

    /// <summary>
    /// Initializes a rule with ordered outcomes selected by the server.
    /// </summary>
    /// <param name="id">A nonempty identifier unique within the session's rules.</param>
    /// <param name="tool">A tool name, optionally qualified by a local subagent path.</param>
    /// <param name="outcomes">A nonempty ordered sequence of responses and thrown errors.</param>
    /// <param name="match">Input property names mapped to JSON Schema objects or booleans.</param>
    /// <exception cref="ArgumentException">An identifier, target, sequence, or constraint is invalid.</exception>
    /// <exception cref="ArgumentNullException">A required argument or outcome is null.</exception>
    public EveToolStub(
        string id,
        string tool,
        IReadOnlyList<EveToolStubOutcome> outcomes,
        IReadOnlyDictionary<string, JsonElement>? match = null)
        : this(id, tool, outcomes, match, true)
    {
    }

    private EveToolStub(
        string id,
        string tool,
        IReadOnlyList<EveToolStubOutcome> outcomes,
        IReadOnlyDictionary<string, JsonElement>? match,
        bool sequence)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(tool);
        ArgumentNullException.ThrowIfNull(outcomes);
        if (outcomes.Count == 0)
        {
            throw new ArgumentException("A stub sequence cannot be empty.", nameof(outcomes));
        }

        if (tool.Split('/')[^1] is "eve__search" or "eve__tool" or "eve__skill")
        {
            throw new ArgumentException("Stub the specific tool, not a catalog routing tool.", nameof(tool));
        }

        Id = id;
        Tool = tool;
        IsSequence = sequence;
        EveToolStubOutcome[] snapshot = outcomes.ToArray();
        foreach (EveToolStubOutcome outcome in snapshot)
        {
            ArgumentNullException.ThrowIfNull(outcome);
        }

        Outcomes = Array.AsReadOnly(snapshot);
        if (match is not null)
        {
            Dictionary<string, JsonElement> constraints = new(StringComparer.Ordinal);
            foreach ((string property, JsonElement schema) in match)
            {
                if (schema.ValueKind is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False))
                {
                    throw new ArgumentException("Stub match constraints must be JSON Schema objects or booleans.", nameof(match));
                }

                constraints.Add(property, schema.Clone());
            }

            Match = new System.Collections.ObjectModel.ReadOnlyDictionary<string, JsonElement>(constraints);
        }
    }

    /// <summary>Gets the rule identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the target tool name.</summary>
    public string Tool { get; }

    /// <summary>Gets optional input-property JSON Schema constraints.</summary>
    public IReadOnlyDictionary<string, JsonElement>? Match { get; }

    /// <summary>Gets the immutable ordered outcomes.</summary>
    public IReadOnlyList<EveToolStubOutcome> Outcomes { get; }

    /// <summary>Gets whether to emit <c>outcomes</c> instead of <c>outcome</c>.</summary>
    public bool IsSequence { get; }
}
