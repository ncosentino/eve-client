namespace NexusLabs.Eve;

/// <summary>
/// Identifies the eve protocol revision implemented by this package.
/// </summary>
public static class EveProtocol
{
    /// <summary>
    /// Gets the upstream TypeScript package version used as the compatibility reference.
    /// </summary>
    public const string ReferenceEveVersion = "0.27.6";

    /// <summary>
    /// Gets the durable message-stream protocol version used by the reference client.
    /// </summary>
    public const string MessageStreamVersion = "19";

    /// <summary>
    /// Gets the media type returned by eve session streams.
    /// </summary>
    public const string MessageStreamContentType = "application/x-ndjson";

    /// <summary>
    /// Gets the response header that carries the assigned eve session identifier.
    /// </summary>
    public const string SessionIdHeaderName = "x-eve-session-id";

    /// <summary>
    /// Gets the Vercel header used to present a trusted OIDC identity-provider token.
    /// </summary>
    public const string VercelTrustedOidcTokenHeaderName = "x-vercel-trusted-oidc-idp-token";
}
