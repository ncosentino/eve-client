namespace NexusLabs.Eve;

/// <summary>
/// Identifies the eve protocol revision implemented by this package.
/// </summary>
public static class EveProtocol
{
    /// <summary>
    /// Gets the upstream TypeScript package version used as the compatibility reference.
    /// </summary>
    public const string ReferenceEveVersion = "0.54.0";

    /// <summary>
    /// Gets the oldest eve release this package can talk to.
    /// </summary>
    /// <remarks>
    /// eve <c>0.52.3</c> added the accepted-message delivery identity required to correlate an
    /// existing-session send with its durable events. Earlier public accepted responses do not
    /// expose that identity, so upgrade the eve server before adopting a client version that
    /// declares this minimum. The message-stream protocol remains version <c>25</c>.
    /// </remarks>
    public const string MinimumEveVersion = "0.52.3";

    /// <summary>
    /// Gets the durable message-stream protocol version used by the reference client.
    /// </summary>
    /// <remarks>
    /// This mirrors upstream's <c>EVE_MESSAGE_STREAM_VERSION</c> in
    /// <c>packages/eve/src/protocol/message.ts</c>. Read that constant when advancing the
    /// baseline; the value does not follow from whether the event vocabulary changed. eve
    /// <c>0.35.0</c> raised it to <c>22</c> while adding no event type and removing none. The
    /// <c>0.39.1</c> raised it to <c>23</c> for durable <c>input.resolved</c> events,
    /// <c>0.46.1</c> raised it to <c>24</c> for durable <c>action.input.appended</c> events, and
    /// <c>0.50.0</c> raised it to <c>25</c> for delta-only text streaming.
    /// </remarks>
    public const string MessageStreamVersion = "25";

    /// <summary>
    /// Gets the agent-info payload schema versions this package understands.
    /// </summary>
    /// <remarks>
    /// eve raised the schema to <c>2</c> in <c>0.35.0</c>, where static instructions became a
    /// list whose entries carry <c>content</c> and a <c>system</c> or <c>user</c> role.
    /// Eve <c>0.45.0</c> uses version <c>3</c> for canonical source ownership, bindings,
    /// composition diagnostics, node identities, and kernel effects. Eve <c>0.45.1</c> raised
    /// it to version <c>4</c> for first-class memory-provider inspection. Every supported
    /// version exposes the identity fields this package projects, and its complete payload
    /// remains available through <see cref="EveAgentInfo.Raw"/>. A version outside this set is
    /// rejected rather than parsed optimistically.
    /// </remarks>
    public static IReadOnlyList<int> SupportedAgentInfoVersions { get; } = [1, 2, 3, 4];

    /// <summary>
    /// Gets the media type returned by eve session streams.
    /// </summary>
    public const string MessageStreamContentType = "application/x-ndjson";

    /// <summary>
    /// Gets the response header that carries the assigned eve session identifier.
    /// </summary>
    public const string SessionIdHeaderName = "x-eve-session-id";

    /// <summary>
    /// Gets the response header that carries the stream protocol version.
    /// </summary>
    public const string StreamVersionHeaderName = "x-eve-stream-version";

    /// <summary>
    /// Gets the Vercel header used to present a trusted OIDC identity-provider token.
    /// </summary>
    public const string VercelTrustedOidcTokenHeaderName = "x-vercel-trusted-oidc-idp-token";
}
