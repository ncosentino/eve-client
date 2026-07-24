using System.Text;

namespace NexusLabs.Eve;

/// <summary>
/// Supplies HTTP Basic credentials for eve requests.
/// </summary>
public sealed class EveBasicAuthentication : IEveAuthentication
{
    private readonly Func<CancellationToken, ValueTask<string>> _passwordProvider;
    private readonly string _username;

    /// <summary>
    /// Initializes Basic authentication with a static password.
    /// </summary>
    /// <param name="username">The Basic authentication username.</param>
    /// <param name="password">The Basic authentication password.</param>
    public EveBasicAuthentication(string username, string password)
        : this(username, _ => ValueTask.FromResult(password))
    {
        ArgumentNullException.ThrowIfNull(password);
    }

    /// <summary>
    /// Initializes Basic authentication with a per-request password provider.
    /// </summary>
    /// <param name="username">The Basic authentication username.</param>
    /// <param name="passwordProvider">The provider invoked before each request.</param>
    public EveBasicAuthentication(
        string username,
        Func<CancellationToken, ValueTask<string>> passwordProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(passwordProvider);

        _username = username.Normalize(NormalizationForm.FormC);
        _passwordProvider = passwordProvider;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, string>> GetHeadersAsync(
        CancellationToken cancellationToken)
    {
        string password = (await _passwordProvider(cancellationToken))
            .Normalize(NormalizationForm.FormC);
        string credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_username}:{password}"));

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["authorization"] = $"Basic {credentials}",
        };
    }
}
