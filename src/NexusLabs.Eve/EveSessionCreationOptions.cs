namespace NexusLabs.Eve;

/// <summary>
/// Configures initial session creation, not subsequent messages or input responses.
/// </summary>
public sealed record EveSessionCreationOptions
{
    /// <summary>
    /// Gets fixed declarative tool stubs. Null omits the field; an empty list sends an empty array.
    /// Requires eve 0.75.0 or newer and the server's <c>allowToolStubs</c> grant.
    /// The server validates JSON Schema constraints and configuration limits.
    /// </summary>
    public IReadOnlyList<EveToolStub>? Stubs { get; init; }

    /// <summary>Gets headers for idle creation only. Initial-message headers come from turn options.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Gets explicit protected-header overrides for idle creation, subject to the client allow-list.
    /// Initial-message overrides come from turn options.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ProtectedHeaderOverrides { get; init; }

    internal EveSessionCreationOptions Snapshot()
    {
        if (Stubs is null)
        {
            return this;
        }

        if (Stubs.Count > 100)
        {
            throw new ArgumentException("A session accepts at most 100 tool stub rules.", nameof(Stubs));
        }

        EveToolStub[] rules = Stubs.ToArray();
        // Rule IDs use the server's case-sensitive JavaScript identity semantics.
#pragma warning disable NLF0016
        HashSet<string> ids = new(StringComparer.Ordinal);
#pragma warning restore NLF0016
        foreach (EveToolStub rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);
            if (!ids.Add(rule.Id))
            {
                throw new ArgumentException("Tool stub identifiers must be unique.", nameof(Stubs));
            }
        }

        return this with { Stubs = Array.AsReadOnly(rules) };
    }
}
