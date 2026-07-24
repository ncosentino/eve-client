namespace NexusLabs.Eve;

/// <summary>
/// Overrides retry and backoff settings for one stream reconnect phase.
/// </summary>
public sealed record EveRetryPolicy
{
    /// <summary>
    /// Gets the initial delay before retrying.
    /// </summary>
    public TimeSpan? BaseDelay { get; init; }

    /// <summary>
    /// Gets the maximum number of attempts.
    /// </summary>
    public int? MaxAttempts { get; init; }

    /// <summary>
    /// Gets the maximum delay between retries.
    /// </summary>
    public TimeSpan? MaxDelay { get; init; }
}
