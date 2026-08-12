namespace NexusLabs.Eve;

/// <summary>
/// Identifies the eve protocol revision implemented by this package.
/// </summary>
public static class EveProtocol
{
    /// <summary>
    /// Gets the upstream TypeScript package version used as the compatibility reference.
    /// </summary>
    public const string ReferenceEveVersion = "0.32.0";

    /// <summary>
    /// Gets the oldest eve release this package can talk to.
    /// </summary>
    /// <remarks>
    /// eve <c>0.31.0</c> moved session control operations to identifier-addressed routes and
    /// removed continuation tokens from the client protocol. Those routes do not exist on an
    /// earlier server, so this package cannot be used against one. Use <c>0.1.0-alpha.3</c> for
    /// an eve <c>0.29.x</c> or <c>0.30.x</c> agent.
    /// </remarks>
    public const string MinimumEveVersion = "0.31.0";

    /// <summary>
    /// Gets the durable message-stream protocol version used by the reference client.
    /// </summary>
    public const string MessageStreamVersion = "21";

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
