using System.Net;

namespace NexusLabs.Eve;

/// <summary>
/// Configures automatic reconnection for a durable eve event stream.
/// </summary>
public sealed record EveStreamReconnectPolicy
{
    /// <summary>
    /// Gets a policy that opens exactly one stream connection.
    /// </summary>
    public static EveStreamReconnectPolicy Disabled { get; } = new()
    {
        Reconnect = false,
    };

    /// <summary>
    /// Gets whether automatic reconnection is enabled.
    /// </summary>
    public bool Reconnect { get; init; } = true;

    /// <summary>
    /// Gets retry overrides for opening a stream connection.
    /// </summary>
    public EveRetryPolicy? StreamOpenRetry { get; init; }

    /// <summary>
    /// Gets retry overrides for clean or disconnected streams that make no progress.
    /// </summary>
    public EveRetryPolicy? StreamIdleRetry { get; init; }

    /// <summary>
    /// Gets the HTTP statuses that may be retried while opening a stream.
    /// </summary>
    public IReadOnlyCollection<HttpStatusCode>? RetryableStatusCodes { get; init; }
}
