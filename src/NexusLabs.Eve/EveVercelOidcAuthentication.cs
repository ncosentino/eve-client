using System.Collections.ObjectModel;

namespace NexusLabs.Eve;

/// <summary>
/// Supplies one Vercel OIDC token as both bearer and trusted-identity headers.
/// </summary>
public sealed class EveVercelOidcAuthentication : IEveAuthentication
{
    private readonly Func<CancellationToken, ValueTask<string>> _tokenProvider;

    /// <summary>
    /// Initializes Vercel OIDC authentication with a static token.
    /// </summary>
    /// <param name="token">The Vercel OIDC token.</param>
    public EveVercelOidcAuthentication(string token)
        : this(_ => ValueTask.FromResult(token))
    {
        ArgumentNullException.ThrowIfNull(token);
    }

    /// <summary>
    /// Initializes Vercel OIDC authentication with a per-request token provider.
    /// </summary>
    /// <param name="tokenProvider">The provider invoked before each request.</param>
    public EveVercelOidcAuthentication(Func<CancellationToken, ValueTask<string>> tokenProvider)
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
            [EveProtocol.VercelTrustedOidcTokenHeaderName] = token,
        };
    }
}
