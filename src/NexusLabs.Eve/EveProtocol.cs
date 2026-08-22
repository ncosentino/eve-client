namespace NexusLabs.Eve;

/// <summary>
/// Identifies the eve protocol revision implemented by this package.
/// </summary>
public static class EveProtocol
{
    /// <summary>
    /// Gets the upstream TypeScript package version used as the compatibility reference.
    /// </summary>
    public const string ReferenceEveVersion = "0.44.0";

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
    /// <remarks>
    /// This mirrors upstream's <c>EVE_MESSAGE_STREAM_VERSION</c> in
    /// <c>packages/eve/src/protocol/message.ts</c>. Read that constant when advancing the
    /// baseline; the value does not follow from whether the event vocabulary changed. eve
    /// <c>0.35.0</c> raised it to <c>22</c> while adding no event type and removing none. The
    /// <c>0.39.1</c> raised it to <c>23</c> for durable <c>input.resolved</c> events.
    /// </remarks>
    public const string MessageStreamVersion = "23";

    /// <summary>
    /// Gets the agent-info payload schema versions this package understands.
    /// </summary>
    /// <remarks>
    /// eve raised the schema to <c>2</c> in <c>0.35.0</c>, where static instructions became a
    /// list whose entries carry <c>content</c> and a <c>system</c> or <c>user</c> role. The
    /// schema remains version <c>2</c> through eve <c>0.44.0</c>. Both versions expose the
    /// identity fields this package projects, and the complete payload of either remains
    /// available through <see cref="EveAgentInfo.Raw"/>. A version outside this set is rejected
    /// rather than parsed optimistically.
    /// </remarks>
    public static IReadOnlyList<int> SupportedAgentInfoVersions { get; } = [1, 2];

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
