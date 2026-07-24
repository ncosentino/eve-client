using System.Collections.ObjectModel;

namespace NexusLabs.Eve;

/// <summary>
/// Supplies a bearer token for eve requests.
/// </summary>
public sealed class EveBearerAuthentication : IEveAuthentication
{
    private readonly Func<CancellationToken, ValueTask<string>> _tokenProvider;

    /// <summary>
    /// Initializes bearer authentication with a static token.
    /// </summary>
    /// <param name="token">The bearer token.</param>
    public EveBearerAuthentication(string token)
        : this(_ => ValueTask.FromResult(token))
    {
        ArgumentNullException.ThrowIfNull(token);
    }

    /// <summary>
    /// Initializes bearer authentication with a per-request token provider.
    /// </summary>
    /// <param name="tokenProvider">The provider invoked before each request.</param>
    public EveBearerAuthentication(Func<CancellationToken, ValueTask<string>> tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _tokenProvider = tokenProvider;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(
        CancellationToken cancellationToken)
    {
        string token = (await _tokenProvider(cancellationToken)).Trim();
        if (token.Length == 0)
        {
            return ReadOnlyDictionary<string, string>.Empty;
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["authorization"] = $"Bearer {token}",
        };
    }
}
