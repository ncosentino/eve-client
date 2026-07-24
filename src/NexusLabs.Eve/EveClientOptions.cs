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
    /// </summary>
    public IEveAuthentication? Authentication { get; init; }

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
    /// Values override <see cref="HeadersProvider"/> but not per-request or authentication headers.
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
    /// Gets whether a normally completed session retains its continuation state for another turn.
    /// Failed sessions still reset.
    /// </summary>
    public bool PreserveCompletedSessions { get; init; }

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
