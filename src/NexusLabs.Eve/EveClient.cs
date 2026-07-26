using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Calls the stable HTTP routes exposed by one deployed eve agent.
/// </summary>
public sealed class EveClient
{
    private readonly EveClientOptions _options;
    private readonly HttpMessageInvoker _transport;

    /// <summary>
    /// Initializes a client with a caller-owned HTTP transport.
    /// </summary>
    /// <param name="transport">
    /// The transport used for all requests. The caller retains ownership and must keep it alive
    /// while sessions or response streams are active.
    /// </param>
    /// <param name="options">The host, authentication, headers, and retry options.</param>
    public EveClient(HttpMessageInvoker transport, EveClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(options);

        if (options.DeliveryRetryAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.DeliveryRetryAttempts,
                "Delivery retry attempts must be greater than zero.");
        }

        if (options.DeliveryRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.DeliveryRetryDelay,
                "Delivery retry delay cannot be negative.");
        }

        if (options.MaxStreamEventBytes is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.MaxStreamEventBytes,
                "The maximum stream event size must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(options.TimeProvider);
        _transport = transport;
        _options = options;
    }

    /// <summary>
    /// Checks whether the eve deployment is ready.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The validated health payload.</returns>
    /// <exception cref="EveClientException">The server returned a non-successful status.</exception>
    /// <exception cref="EveProtocolException">The response did not match the health contract.</exception>
    public async Task<EveHealthStatus> GetHealthAsync(
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = await CreateRequestAsync(
            HttpMethod.Get,
            EveRequestKind.Health,
            EveRoutes.Health,
            null,
            null,
            cancellationToken);
        using HttpResponseMessage response = await SendTransportAsync(
            request,
            false,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateClientExceptionAsync(response, cancellationToken);
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("ok", out JsonElement ok)
                || ok.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("status", out JsonElement status)
                || status.ValueKind != JsonValueKind.String
                || !string.Equals(status.GetString(), "ready", StringComparison.Ordinal)
                || !root.TryGetProperty("workflowId", out JsonElement workflowId)
                || workflowId.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(workflowId.GetString()))
            {
                throw new EveProtocolException(
                    "The eve health route returned an invalid response.");
            }

            return new EveHealthStatus(true, "ready", workflowId.GetString()!);
        }
        catch (JsonException exception)
        {
            throw new EveProtocolException(
                "The eve health route returned invalid JSON.",
                exception);
        }
    }

    /// <summary>
    /// Fetches and validates the agent inspection payload.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Validated identity fields and the complete raw agent-info document.</returns>
    /// <exception cref="EveClientException">The server returned a non-successful status.</exception>
    /// <exception cref="EveProtocolException">The body was not a recognized agent-info payload.</exception>
    public async Task<EveAgentInfo> GetInfoAsync(CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = await CreateRequestAsync(
            HttpMethod.Get,
            EveRequestKind.Info,
            EveRoutes.Info,
            null,
            null,
            cancellationToken);
        using HttpResponseMessage response = await SendTransportAsync(
            request,
            false,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateClientExceptionAsync(response, cancellationToken);
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseAgentInfo(body);
    }

    /// <summary>
    /// Sends a caller-owned request against a relative path on this eve target.
    /// Client headers and authentication are applied before sending.
    /// </summary>
    /// <param name="request">
    /// The request to send. Its URI must be relative. The request remains owned by the caller.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The raw response. The caller must dispose it. Non-successful statuses are returned unchanged.
    /// </returns>
    public async Task<HttpResponseMessage> SendRawAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestUri is null || request.RequestUri.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "The raw eve request URI must be a relative route.",
                nameof(request));
        }

        string route = request.RequestUri.OriginalString;
        Dictionary<string, string> perRequestHeaders = new(StringComparer.OrdinalIgnoreCase);
        if (request.Content is not null)
        {
            AddHttpHeaders(perRequestHeaders, request.Content.Headers);
            request.Content.Headers.Clear();
        }

        AddHttpHeaders(perRequestHeaders, request.Headers);
        request.Headers.Clear();
        request.RequestUri = EveUrlBuilder.Create(_options.Host, route);
        EveHttpRequestContext requestContext = new(EveRequestKind.Raw);
        IReadOnlyDictionary<string, string> headers = await ResolveHeadersAsync(
            requestContext,
            perRequestHeaders,
            cancellationToken);
        ApplyHeaders(request, headers);
        return await SendTransportAsync(request, true, cancellationToken);
    }

    /// <summary>
    /// Creates a handle for a fresh conversation.
    /// </summary>
    /// <returns>A session whose first send creates the remote run.</returns>
    public EveSession CreateSession() => new(this, new EveSessionState());

    /// <summary>
    /// Creates a handle from a previously persisted session cursor.
    /// </summary>
    /// <param name="state">The session state to resume.</param>
    /// <returns>A session initialized from the supplied cursor.</returns>
    public EveSession CreateSession(EveSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.StreamIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state.StreamIndex,
                "A persisted stream index cannot be negative.");
        }

        return new EveSession(this, state);
    }

    /// <summary>
    /// Creates a session from a continuation token when no stream cursor was persisted.
    /// </summary>
    /// <param name="continuationToken">The channel-owned continuation token.</param>
    /// <returns>A resumable session with a zero stream cursor.</returns>
    public EveSession CreateSession(string continuationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(continuationToken);
        return new EveSession(
            this,
            new EveSessionState
            {
                ContinuationToken = continuationToken,
            });
    }

    internal bool PreserveCompletedSessions => _options.PreserveCompletedSessions;

    internal int DeliveryRetryAttempts => _options.DeliveryRetryAttempts;

    internal TimeSpan DeliveryRetryDelay => _options.DeliveryRetryDelay;

    internal int? MaxStreamEventBytes => _options.MaxStreamEventBytes;

    internal TimeProvider TimeProvider => _options.TimeProvider;

    internal string Host => _options.Host;

    internal async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        EveRequestKind requestKind,
        string route,
        IReadOnlyDictionary<string, string>? perRequestHeaders,
        HttpContent? content,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? query = null)
    {
        HttpRequestMessage request = new(method, EveUrlBuilder.Create(_options.Host, route, query))
        {
            Content = content,
        };

        try
        {
            EveHttpRequestContext requestContext = new(requestKind);
            IReadOnlyDictionary<string, string> headers = await ResolveHeadersAsync(
                requestContext,
                perRequestHeaders,
                cancellationToken);
            ApplyHeaders(request, headers);
            return request;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    internal Task<HttpResponseMessage> SendTransportAsync(
        HttpRequestMessage request,
        bool responseHeadersOnly,
        CancellationToken cancellationToken)
    {
        if (_transport is HttpClient httpClient)
        {
            HttpCompletionOption completionOption = responseHeadersOnly
                ? HttpCompletionOption.ResponseHeadersRead
                : HttpCompletionOption.ResponseContentRead;
            return httpClient.SendAsync(request, completionOption, cancellationToken);
        }

        return _transport.SendAsync(request, cancellationToken);
    }

    internal static async Task<EveClientException> CreateClientExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return CreateClientException(response, body);
    }

    internal static EveClientException CreateClientException(
        HttpResponseMessage response,
        string body)
    {
        Dictionary<string, IReadOnlyList<string>> headers = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, IEnumerable<string> values) in response.Headers)
        {
            headers[name] = values.ToArray();
        }

        foreach ((string name, IEnumerable<string> values) in response.Content.Headers)
        {
            headers[name] = values.ToArray();
        }

        return new EveClientException(response.StatusCode, body, headers);
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveHeadersAsync(
        EveHttpRequestContext requestContext,
        IReadOnlyDictionary<string, string>? perRequestHeaders,
        CancellationToken cancellationToken)
    {
        Task<IReadOnlyDictionary<string, string>> dynamicHeadersTask =
            _options.HeadersProvider is null
                ? Task.FromResult<IReadOnlyDictionary<string, string>>(
                    ReadOnlyDictionary<string, string>.Empty)
                : _options.HeadersProvider(cancellationToken).AsTask();
        Task<IReadOnlyDictionary<string, string>> requestHeadersTask =
            _options.RequestHeadersProvider is null
                ? Task.FromResult<IReadOnlyDictionary<string, string>>(
                    ReadOnlyDictionary<string, string>.Empty)
                : _options.RequestHeadersProvider(requestContext, cancellationToken).AsTask();
        Task<IReadOnlyDictionary<string, string>> authenticationTask =
            _options.Authentication is null
                ? Task.FromResult<IReadOnlyDictionary<string, string>>(
                    ReadOnlyDictionary<string, string>.Empty)
                : _options.Authentication.GetHeadersAsync(cancellationToken).AsTask();

        await Task.WhenAll(dynamicHeadersTask, requestHeadersTask, authenticationTask);

        Dictionary<string, string> resolved = new(StringComparer.OrdinalIgnoreCase);
        AddHeaders(resolved, _options.Headers);
        AddHeaders(resolved, await dynamicHeadersTask);
        AddHeaders(resolved, await requestHeadersTask);
        AddHeaders(resolved, await authenticationTask);
        AddHeaders(resolved, perRequestHeaders);
        return resolved;
    }

    private static void AddHeaders(
        IDictionary<string, string> target,
        IReadOnlyDictionary<string, string>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach ((string name, string value) in source)
        {
            target[name] = value;
        }
    }

    private static void AddHttpHeaders(
        IDictionary<string, string> target,
        HttpHeaders source)
    {
        foreach ((string name, IEnumerable<string> values) in source)
        {
            target[name] = string.Join(",", values);
        }
    }

    private static void ApplyHeaders(
        HttpRequestMessage request,
        IReadOnlyDictionary<string, string> headers)
    {
        foreach ((string name, string value) in headers)
        {
            if (ReplaceHeaderIfSupported(request.Headers, name, value))
            {
                if (request.Content is not null)
                {
                    RemoveHeaderIfSupported(request.Content.Headers, name, value);
                }

                continue;
            }

            if (request.Content is not null
                && ReplaceHeaderIfSupported(request.Content.Headers, name, value))
            {
                continue;
            }

            if (request.Content is null
                && IsSupportedByContentHeaders(name, value))
            {
                continue;
            }

            throw new InvalidOperationException($"The HTTP header '{name}' could not be applied.");
        }
    }

    private static bool IsSupportedByContentHeaders(string name, string value)
    {
        using ByteArrayContent content = new([]);
        return content.Headers.TryAddWithoutValidation(name, value);
    }

    private static void RemoveHeaderIfSupported(
        HttpHeaders headers,
        string name,
        string value)
    {
        if (headers.TryAddWithoutValidation(name, value))
        {
            headers.Remove(name);
        }
    }

    private static bool ReplaceHeaderIfSupported(
        HttpHeaders headers,
        string name,
        string value)
    {
        if (!headers.TryAddWithoutValidation(name, value))
        {
            return false;
        }

        headers.Remove(name);
        if (!headers.TryAddWithoutValidation(name, value))
        {
            throw new InvalidOperationException(
                $"The HTTP header '{name}' could not be replaced.");
        }

        return true;
    }

    private static EveAgentInfo ParseAgentInfo(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            JsonElement agent = RequireObject(root, "agent");
            JsonElement model = RequireObject(agent, "model");
            JsonElement capabilities = RequireObject(root, "capabilities");

            string kind = RequireString(root, "kind");
            int version = RequireInt32(root, "version");
            string mode = RequireString(root, "mode");
            string agentName = RequireString(agent, "name");
            string modelId = RequireString(model, "id");
            bool developmentRoutesAvailable = RequireBoolean(capabilities, "devRoutes");

            if (!string.Equals(kind, "eve-agent-info", StringComparison.Ordinal)
                || version != 1
                || mode is not "development" and not "production")
            {
                throw new EveProtocolException(
                    "The eve info route returned an unsupported agent-info payload.");
            }

            string? description = agent.TryGetProperty("description", out JsonElement descriptionValue)
                && descriptionValue.ValueKind == JsonValueKind.String
                    ? descriptionValue.GetString()
                    : null;
            return new EveAgentInfo(
                agentName,
                modelId,
                mode,
                version,
                developmentRoutesAvailable,
                description,
                root);
        }
        catch (JsonException exception)
        {
            throw new EveProtocolException(
                "The eve info route returned invalid JSON.",
                exception);
        }
    }

    private static JsonElement RequireObject(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Object)
        {
            throw new EveProtocolException(
                $"The eve info payload is missing object property '{propertyName}'.");
        }

        return value;
    }

    private static string RequireString(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new EveProtocolException(
                $"The eve info payload is missing string property '{propertyName}'.");
        }

        return value.GetString()!;
    }

    private static int RequireInt32(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || !value.TryGetInt32(out int result))
        {
            throw new EveProtocolException(
                $"The eve info payload is missing integer property '{propertyName}'.");
        }

        return result;
    }

    private static bool RequireBoolean(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            throw new EveProtocolException(
                $"The eve info payload is missing Boolean property '{propertyName}'.");
        }

        return value.GetBoolean();
    }
}
