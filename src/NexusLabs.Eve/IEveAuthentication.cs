namespace NexusLabs.Eve;

/// <summary>
/// Resolves authentication headers for an eve request.
/// </summary>
public interface IEveAuthentication
{
    /// <summary>
    /// Resolves headers immediately before an HTTP request is sent.
    /// </summary>
    /// <param name="cancellationToken">Cancels credential resolution.</param>
    /// <returns>
    /// Authentication headers whose values override client-level headers but are themselves
    /// overridden by explicit per-request headers.
    /// </returns>
    ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(
        CancellationToken cancellationToken);
}
