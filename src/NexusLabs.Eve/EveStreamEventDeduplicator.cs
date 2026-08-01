namespace NexusLabs.Eve;

/// <summary>
/// Tracks which durable eve stream events have already been consumed.
/// </summary>
/// <remarks>
/// Deduplication is keyed on <see cref="EveStreamEventMetadata.Id"/>. It absorbs a reconnect that
/// overlaps already-handled events, a rewind to an earlier start index, and a persisted log merged
/// with the prefix a live stream replays. A retried step is not a duplicate: it is emitted again
/// under a new identifier.
/// <para>
/// The remembered set is unbounded because a bounded one cannot survive a rewind past its capacity.
/// Callers that retain nothing per event should bound their reads with the session stream cursor
/// instead of using this type.
/// </para>
/// <para>
/// Instances are not thread-safe and are intended to be consumed by a single stream reader.
/// </para>
/// </remarks>
public sealed class EveStreamEventDeduplicator
{
    // Event identifiers are opaque server-minted tokens; two ids that differ only by case are
    // different ids, so this set must compare them exactly.
#pragma warning disable NLF0016
    private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
#pragma warning restore NLF0016

    /// <summary>
    /// Gets the number of event identifiers currently remembered.
    /// </summary>
    public int Count => _seen.Count;

    /// <summary>
    /// Records <paramref name="streamEvent"/> and reports whether it should be processed.
    /// </summary>
    /// <param name="streamEvent">The stream event to admit.</param>
    /// <returns>
    /// <see langword="true"/> when the event has not been admitted before, or carries no durable
    /// identifier; <see langword="false"/> when its identifier was already admitted and the caller
    /// should drop it.
    /// </returns>
    /// <remarks>
    /// Events persisted before stream protocol version 20 carry no identifier and are always
    /// admitted because there is nothing to deduplicate on.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="streamEvent"/> is <see langword="null"/>.</exception>
    public bool Admit(EveStreamEvent streamEvent)
    {
        ArgumentNullException.ThrowIfNull(streamEvent);

        return streamEvent.Metadata?.Id is not string id || _seen.Add(id);
    }

    /// <summary>
    /// Forgets every remembered identifier so a new session can reuse this instance.
    /// </summary>
    public void Clear() => _seen.Clear();
}
