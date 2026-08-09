namespace NexusLabs.Eve;

/// <summary>
/// Configures an <see cref="EveClient"/> for one eve deployment.
/// </summary>
public sealed record EveClientOptions
{
    /// <summary>
    /// Initializes options for the specified eve host or route prefix.
    /// </summary>
    /// <param name="host">
    /// An absolute host such as <c>https://agent.example.com</c>, or a relative prefix such as
    /// <c>/api</c> when the supplied HTTP transport has a base address.
    /// </param>
    public EveClientOptions(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Length > 0 && string.IsNullOrWhiteSpace(host))
        {
            throw new ArgumentException("The eve host cannot contain only whitespace.", nameof(host));
        }

        Host = host;
    }

    /// <summary>
    /// Gets the host or route prefix on which eve routes are mounted.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// Gets the authentication provider invoked before every request and stream reconnect.
    /// Its owned headers are protected from generic per-request values.
    /// </summary>
    public IEveAuthentication? Authentication { get; init; }

    /// <summary>
    /// Gets additional header names that generic per-request values cannot replace.
    /// Use this for credentials supplied through <see cref="Headers"/>,
    /// <see cref="HeadersProvider"/>, or <see cref="RequestHeadersProvider"/>.
    /// </summary>
    public IReadOnlyCollection<string>? ProtectedHeaderNames { get; init; }

    /// <summary>
    /// Gets protected header names that may be replaced through a dedicated per-call override.
    /// A name must be listed here and supplied through
    /// <see cref="EveTurnOptions.ProtectedHeaderOverrides"/> or
    /// <see cref="EveRawRequestOptions.ProtectedHeaderOverrides"/>.
    /// </summary>
    public IReadOnlyCollection<string>? AllowedProtectedHeaderOverrides { get; init; }

    /// <summary>
    /// Gets static headers included on every request.
    /// Content-specific headers are omitted when a request has no content.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>
    /// Gets an optional dynamic header provider invoked before every request.
    /// Dynamic values override entries in <see cref="Headers"/>.
    /// </summary>
    public Func<CancellationToken, ValueTask<IReadOnlyDictionary<string, string>>>? HeadersProvider
    {
        get;
        init;
    }

    /// <summary>
    /// Gets an optional dynamic header provider that receives the request operation before
    /// every request.
    /// This provider stays client-level: values override <see cref="HeadersProvider"/> but not
    /// protected headers.
    /// </summary>
    public Func<
        EveHttpRequestContext,
        CancellationToken,
        ValueTask<IReadOnlyDictionary<string, string>>>? RequestHeadersProvider
    {
        get;
        init;
    }

    /// <summary>
    /// Gets the optional maximum UTF-8 byte count for one NDJSON stream event, excluding its
    /// line ending. A <see langword="null"/> value preserves the upstream unbounded behavior.
    /// </summary>
    public int? MaxStreamEventBytes { get; init; }

    /// <summary>
    /// Gets the maximum number of POST attempts used when delivering human-input responses.
    /// </summary>
    public int DeliveryRetryAttempts { get; init; } = 10;

    /// <summary>
    /// Gets the delay between delivery retries for a session that has not propagated yet.
    /// </summary>
    public TimeSpan DeliveryRetryDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Gets the time provider used for retry delays.
    /// </summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
