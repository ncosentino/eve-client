namespace NexusLabs.Eve;

/// <summary>
/// Resolves authentication headers for an eve request.
/// </summary>
public interface IEveAuthentication
{
    /// <summary>
    /// Gets header names owned by this provider even when no value is emitted for a request.
    /// Implementations should declare every credential header so generic per-request headers
    /// cannot replace credentials when token resolution returns an empty result.
    /// </summary>
    IReadOnlyCollection<string> AuthenticationHeaderNames => Array.Empty<string>();

    /// <summary>
    /// Resolves headers immediately before an HTTP request is sent.
    /// </summary>
    /// <param name="cancellationToken">Cancels credential resolution.</param>
    /// <returns>
    /// Authentication headers whose values override client-level and generic per-request headers.
    /// A protected value can be replaced only through an explicitly allowed protected-header
    /// override.
    /// </returns>
    ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(
        CancellationToken cancellationToken);
}
