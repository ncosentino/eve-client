namespace NexusLabs.Eve;

/// <summary>
/// Configures one caller-owned request sent through
/// <see cref="EveClient.SendRawAsync(HttpRequestMessage, EveRawRequestOptions?, CancellationToken)"/>.
/// </summary>
public sealed record EveRawRequestOptions
{
    /// <summary>
    /// Gets explicit overrides for protected headers.
    /// Every name must also appear in
    /// <see cref="EveClientOptions.AllowedProtectedHeaderOverrides"/>.
    /// Never populate this collection directly from untrusted inbound headers.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ProtectedHeaderOverrides { get; init; }
}
