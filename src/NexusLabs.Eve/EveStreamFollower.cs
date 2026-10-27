using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace NexusLabs.Eve;

internal static class EveStreamFollower
{
    private const string TailIndexHeader = "x-eve-stream-tail-index";
    private const string StreamControlVersionQuery = "streamControlVersion";
    private const string LeaseEndedControlName = "stream.lease-ended";
    private static readonly TimeSpan StreamReadIdleTimeout = TimeSpan.FromSeconds(15);

    private static readonly HttpStatusCode[] DefaultRetryableStatusCodes =
    [
        HttpStatusCode.NotFound,
        HttpStatusCode.Conflict,
        (HttpStatusCode)425,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];

    internal static async IAsyncEnumerable<EveStreamEvent> FollowAsync(
        EveClient client,
        string sessionId,
        int initialStartIndex,
        bool follow,
        EveStreamFollowMode mode,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyDictionary<string, string>? protectedHeaderOverrides,
        EveStreamReconnectPolicy? configuredPolicy,
        int? maximumEventBytes,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        string? streamRoutePath = null,
        int initialEventIndex = 0,
        Action<int>? onSourcePosition = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialEventIndex);
        ResolvedReconnectPolicy policy = ResolvePolicy(configuredPolicy, mode);
        int startIndex = initialStartIndex;
        int eventIndex = initialEventIndex;
        TimeSpan reconnectDelay = policy.Idle.BaseDelay;
        int idleReconnects = 0;
        bool initialConnection = true;
        int? tailIndex = null;
        string progressAt = "1970-01-01T00:00:00.000Z";

        while (true)
        {
            bool deliveredEvent = false;
            ConnectionReadState readState = new();
            readState.LastCommitAt = progressAt;
            bool streamControlEnabled =
                startIndex >= 0 && policy.Idle.MaxAttempts > 0;
            HttpResponseMessage? response = null;
            IAsyncEnumerator<EveStreamEvent>? connection = null;
            using CancellationTokenSource connectionAbortSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                response = await OpenStreamOrNullAsync(
                    client,
                    sessionId,
                    startIndex,
                    follow,
                    (!follow || startIndex < 0) && tailIndex is null,
                    streamControlEnabled,
                    headers,
                    protectedHeaderOverrides,
                    policy,
                    streamRoutePath,
                    connectionAbortSource.Token);
                if (response is null)
                {
                    yield break;
                }

                int streamVersion = ReadStreamVersion(response);
                if (streamVersion != 27 && eventIndex != 0)
                {
                    throw new EveProtocolException("A partial-commit cursor requires stream protocol 27.");
                }
                if ((!follow || (startIndex < 0 && streamVersion == 27)) && tailIndex is null)
                {
                    tailIndex = ReadTailIndex(response);
                }

                if (startIndex < 0 && streamVersion == 27)
                {
                    startIndex = Math.Max(0, checked(tailIndex!.Value + 1 + startIndex));
                }

                if (streamVersion == 27)
                {
                    onSourcePosition?.Invoke(startIndex);
                }

                if (!follow && tailIndex is int openBound && startIndex > openBound)
                {
                    yield break;
                }

                connection = ReadConnectionAsync(
                        response,
                        maximumEventBytes,
                        client.TimeProvider,
                        readState,
                        streamControlEnabled,
                        startIndex,
                        onSourcePosition,
                        follow ? null : tailIndex,
                        connectionAbortSource.Token)
                    .GetAsyncEnumerator(connectionAbortSource.Token);
                bool sessionEnded = false;
                while (true)
                {
                    bool hasEvent;
                    try
                    {
                        hasEvent = await connection.MoveNextAsync();
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        yield break;
                    }

                    if (!hasEvent)
                    {
                        break;
                    }

                    EveStreamEvent streamEvent = connection.Current;
                    if (streamEvent.Metadata?.Position is EveStreamPosition position)
                    {
                        if (!follow && tailIndex is int sourceBound && position.Line > sourceBound)
                        {
                            yield break;
                        }

                        if (position.Line == startIndex && position.Index < eventIndex)
                        {
                            continue;
                        }

                        bool endOfLine = streamEvent.Metadata.EndOfLine == true;
                        startIndex = endOfLine ? checked(position.Line + 1) : position.Line;
                        eventIndex = endOfLine ? 0 : checked(position.Index + 1);
                    }
                    else
                    {
                        startIndex++;
                    }
                    deliveredEvent = true;
                    reconnectDelay = policy.Idle.BaseDelay;
                    idleReconnects = 0;
                    yield return streamEvent;

                    sessionEnded |= streamEvent.Kind is EveStreamEventKind.SessionFailed
                        or EveStreamEventKind.SessionCompleted
                        or EveStreamEventKind.SessionEnded;
                    if (sessionEnded && streamEvent.Metadata?.EndOfLine != false)
                    {
                        yield break;
                    }

                    if (!follow && tailIndex is int bound && startIndex > bound)
                    {
                        yield break;
                    }
                }

                if (readState.StreamVersion == 27)
                {
                    startIndex = Math.Max(startIndex, readState.NextLineIndex);
                    progressAt = readState.LastCommitAt;
                }
            }
            finally
            {
                try
                {
                    if (response is not null && !readState.ReachedEndOfStream)
                    {
                        await connectionAbortSource.CancelAsync();
                    }
                }
                finally
                {
                    try
                    {
                        if (connection is not null)
                        {
                            await connection.DisposeAsync();
                        }
                    }
                    finally
                    {
                        response?.Dispose();
                    }
                }
            }

            if (cancellationToken.IsCancellationRequested
                || (!follow && tailIndex is int closedBound && startIndex > closedBound)
                || readState.StreamEnded
                || (initialStartIndex < 0 && readState.StreamVersion != 27)
                || policy.Idle.MaxAttempts == 0)
            {
                yield break;
            }

            if (readState.LeaseEnded)
            {
                continue;
            }

            bool enforceIdleLimit = readState.StreamVersion != 27
                ? policy.EnforceIdleAttemptLimit
                : !follow || configuredPolicy?.StreamIdleRetry?.MaxAttempts is not null;
            int idleLimit = readState.StreamVersion == 27 && !follow
                ? policy.Open.MaxAttempts
                : policy.Idle.MaxAttempts;
            if (enforceIdleLimit
                && !deliveredEvent
                && !initialConnection
                && ++idleReconnects >= idleLimit)
            {
                if (readState.StreamVersion == 27 && !follow)
                {
                    throw new EveProtocolException(
                        "The version-27 bounded stream disconnected before reaching its fixed tail.");
                }

                yield break;
            }

            initialConnection = false;
            if (!await DelayAsync(client, reconnectDelay, cancellationToken))
            {
                yield break;
            }

            reconnectDelay = Min(reconnectDelay + reconnectDelay, policy.Idle.MaxDelay);
        }
    }

    private static async IAsyncEnumerable<EveStreamEvent> ReadConnectionAsync(
        HttpResponseMessage response,
        int? maximumEventBytes,
        TimeProvider timeProvider,
        ConnectionReadState readState,
        bool streamControlEnabled,
        int startIndex,
        Action<int>? onSourcePosition,
        int? tailIndex,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int streamVersion = ReadStreamVersion(response);
        EveStreamDecoder decoder = new(streamVersion);
        EveV27LineDecoder? lineDecoder = streamVersion == 27 ? new(startIndex, readState.LastCommitAt) : null;
        readState.StreamVersion = streamVersion;
        readState.NextLineIndex = startIndex;
        if (lineDecoder is not null)
        {
            onSourcePosition?.Invoke(startIndex);
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using EveNdjsonLineReader reader = new(
            stream,
            maximumEventBytes,
            timeProvider,
            streamVersion == 27 ? TimeSpan.FromSeconds(30) : StreamReadIdleTimeout);

        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }
            catch (Exception exception) when (IsStreamDisconnect(exception))
            {
                yield break;
            }

            if (line is null)
            {
                readState.ReachedEndOfStream = true;
                yield break;
            }

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (lineDecoder is not null)
            {
                IReadOnlyList<EveStreamEvent> events = lineDecoder.Decode(trimmed);
                foreach (EveStreamEvent streamEvent in events)
                {
                    yield return streamEvent;
                }

                readState.NextLineIndex = lineDecoder.NextLine;
                readState.LastCommitAt = lineDecoder.LastCommitAt;
                onSourcePosition?.Invoke(lineDecoder.NextLine);
                readState.LeaseEnded = lineDecoder.LeaseEnded;
                if (tailIndex is int bound && lineDecoder.NextLine > bound)
                {
                    yield break;
                }
                if (lineDecoder.StreamEnded)
                {
                    readState.StreamEnded = true;
                    yield break;
                }

                continue;
            }

            if (streamControlEnabled
                && trimmed.Contains("\"$eve\"", StringComparison.Ordinal)
                && IsLeaseEndedControl(trimmed))
            {
                readState.LeaseEnded = true;
                continue;
            }

            yield return decoder.Decode(trimmed);
        }
    }

    private static async Task<HttpResponseMessage?> OpenStreamOrNullAsync(
        EveClient client,
        string sessionId,
        int startIndex,
        bool follow,
        bool requestTailIndex,
        bool streamControlEnabled,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyDictionary<string, string>? protectedHeaderOverrides,
        ResolvedReconnectPolicy policy,
        string? streamRoutePath,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        try
        {
#pragma warning disable IDISP011 // Ownership transfers to the stream follower.
            return await OpenStreamAsync(
                client,
                sessionId,
                startIndex,
                follow,
                requestTailIndex,
                streamControlEnabled,
                headers,
                protectedHeaderOverrides,
                policy,
                streamRoutePath,
                cancellationToken);
#pragma warning restore IDISP011
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<HttpResponseMessage> OpenStreamAsync(
        EveClient client,
        string sessionId,
        int startIndex,
        bool follow,
        bool requestTailIndex,
        bool streamControlEnabled,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyDictionary<string, string>? protectedHeaderOverrides,
        ResolvedReconnectPolicy policy,
        string? streamRoutePath,
        CancellationToken cancellationToken)
    {
        HttpStatusCode? lastStatusCode = null;
        string? lastBody = null;
        IReadOnlyDictionary<string, IReadOnlyList<string>>? lastHeaders = null;
        TimeSpan retryDelay = policy.Open.BaseDelay;
        Dictionary<string, string> queryParameters = [];
        if (streamControlEnabled)
        {
            queryParameters[StreamControlVersionQuery] = EveProtocol.StreamControlVersion;
        }

        if (startIndex != 0)
        {
            queryParameters["startIndex"] = startIndex.ToString(CultureInfo.InvariantCulture);
        }

        if (requestTailIndex)
        {
            queryParameters["includeTailIndex"] = "1";
        }

        if (!follow)
        {
            queryParameters["follow"] = "false";
        }

        IReadOnlyDictionary<string, string>? query = queryParameters.Count == 0
            ? null
            : queryParameters;

        for (int attempt = 0; attempt < policy.Open.MaxAttempts; attempt++)
        {
            using HttpRequestMessage request = await client.CreateRequestAsync(
                HttpMethod.Get,
                EveRequestKind.StreamSession,
                streamRoutePath ?? EveRoutes.StreamSession(sessionId),
                headers,
                null,
                cancellationToken,
                query,
                protectedHeaderOverrides);

            HttpResponseMessage response;
            try
            {
                response = await client.SendTransportAsync(request, true, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                IsStreamDisconnect(exception) && attempt < policy.Open.MaxAttempts - 1)
            {
                if (!await DelayAsync(client, retryDelay, cancellationToken))
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                retryDelay = Min(retryDelay + retryDelay, policy.Open.MaxDelay);
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
#pragma warning disable IDISP011 // Ownership transfers to the stream follower.
                return response;
#pragma warning restore IDISP011
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            EveClientException clientException = EveClient.CreateClientException(response, body);
            lastStatusCode = response.StatusCode;
            lastBody = body;
            lastHeaders = clientException.ResponseHeaders;
            bool retryable = policy.RetryableStatusCodes.Contains(response.StatusCode)
                && !(response.StatusCode == HttpStatusCode.Conflict
                    && clientException.ErrorCode is "session_stranded" or "session_version_unsupported");
            response.Dispose();

            if (!retryable)
            {
                throw clientException;
            }

            if (attempt < policy.Open.MaxAttempts - 1)
            {
                if (!await DelayAsync(client, retryDelay, cancellationToken))
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                retryDelay = Min(retryDelay + retryDelay, policy.Open.MaxDelay);
            }
        }

        throw new EveClientException(
            lastStatusCode ?? 0,
            lastBody ?? "Failed to open the eve message stream.",
            lastHeaders ?? ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty);
    }

    private static int ReadStreamVersion(HttpResponseMessage response)
    {
        IEnumerable<string>? values = null;
        if (!response.Headers.TryGetValues(EveProtocol.StreamVersionHeaderName, out values)
            && (response.Content is null || !response.Content.Headers.TryGetValues(EveProtocol.StreamVersionHeaderName, out values)))
        {
            throw new EveProtocolException(
                $"A stream response requires the '{EveProtocol.StreamVersionHeaderName}' header.");
        }

        string? raw = values?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw) || !IsIntegerLiteral(raw))
        {
            throw new EveProtocolException(
                $"The '{EveProtocol.StreamVersionHeaderName}' response header was not an integer: '{raw}'.");
        }

        if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int version))
        {
            throw new EveProtocolException(
                $"The '{EveProtocol.StreamVersionHeaderName}' response header was out of range: '{raw}'.");
        }

        EveProtocol.ValidateMessageStreamVersion(version);
        return version;
    }

    private static bool IsLeaseEndedControl(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("$eve", out JsonElement name)
                && name.ValueKind == JsonValueKind.String
                && string.Equals(
                    name.GetString(),
                    LeaseEndedControlName,
                    StringComparison.Ordinal)
                && root.TryGetProperty("version", out JsonElement version)
                && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out int value)
                && value == 1;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // A negative tail index is valid upstream and reports an empty durable stream,
    // so a nonnegative cursor immediately passes the bound instead of failing.
    private static int ReadTailIndex(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(TailIndexHeader, out IEnumerable<string>? values))
        {
            throw new EveProtocolException(
                $"A bounded eve stream requires the {TailIndexHeader} response header. " +
                "The agent may be running an older eve version.");
        }

        string? raw = values.FirstOrDefault();
        if (!IsIntegerLiteral(raw))
        {
            throw new EveProtocolException(
                $"The {TailIndexHeader} response header was not an integer: '{raw}'.");
        }

        if (!int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int tailIndex))
        {
            throw new EveProtocolException(
                $"The {TailIndexHeader} response header was out of range: '{raw}'.");
        }

        return tailIndex;
    }

    private static bool IsIntegerLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        int start = value[0] == '-' ? 1 : 0;
        if (start == value.Length)
        {
            return false;
        }

        for (int index = start; index < value.Length; index++)
        {
            if (!char.IsAsciiDigit(value[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static ResolvedReconnectPolicy ResolvePolicy(
        EveStreamReconnectPolicy? configuredPolicy,
        EveStreamFollowMode mode)
    {
        if (configuredPolicy is { Reconnect: false })
        {
            return new ResolvedReconnectPolicy(
                new ResolvedRetryPolicy(TimeSpan.FromMilliseconds(250), 1, TimeSpan.FromSeconds(5)),
                new ResolvedRetryPolicy(TimeSpan.FromMilliseconds(250), 0, TimeSpan.FromSeconds(4)),
                true,
                new HashSet<HttpStatusCode>(DefaultRetryableStatusCodes));
        }

        bool enforceIdleAttemptLimit =
            mode == EveStreamFollowMode.SessionStream
            || configuredPolicy?.StreamIdleRetry?.MaxAttempts is not null;
        return new ResolvedReconnectPolicy(
            ResolveRetryPolicy(
                configuredPolicy?.StreamOpenRetry,
                TimeSpan.FromMilliseconds(250),
                12,
                TimeSpan.FromSeconds(5)),
            ResolveRetryPolicy(
                configuredPolicy?.StreamIdleRetry,
                TimeSpan.FromMilliseconds(250),
                5,
                TimeSpan.FromSeconds(4)),
            enforceIdleAttemptLimit,
            configuredPolicy?.RetryableStatusCodes is null
                ? new HashSet<HttpStatusCode>(DefaultRetryableStatusCodes)
                : new HashSet<HttpStatusCode>(configuredPolicy.RetryableStatusCodes));
    }

    private static ResolvedRetryPolicy ResolveRetryPolicy(
        EveRetryPolicy? configured,
        TimeSpan defaultBaseDelay,
        int defaultMaxAttempts,
        TimeSpan defaultMaxDelay)
    {
        TimeSpan baseDelay = configured?.BaseDelay ?? defaultBaseDelay;
        int maxAttempts = configured?.MaxAttempts ?? defaultMaxAttempts;
        TimeSpan maxDelay = configured?.MaxDelay ?? defaultMaxDelay;

        if (baseDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configured),
                baseDelay,
                "A stream retry base delay cannot be negative.");
        }

        if (maxAttempts < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configured),
                maxAttempts,
                "Stream retry attempts cannot be negative.");
        }

        if (maxDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configured),
                maxDelay,
                "A stream retry maximum delay cannot be negative.");
        }

        return new ResolvedRetryPolicy(baseDelay, maxAttempts, maxDelay);
    }

    private static bool IsStreamDisconnect(Exception exception) =>
        exception is not EveProtocolException
        && (exception is HttpRequestException or IOException or OperationCanceledException);

    private static async Task<bool> DelayAsync(
        EveClient client,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        Task delayTask = Task.Delay(delay, client.TimeProvider, CancellationToken.None);
        if (!cancellationToken.CanBeCanceled)
        {
            await delayTask;
            return true;
        }

        TaskCompletionSource cancellation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((TaskCompletionSource)state!).TrySetResult(),
            cancellation);
        Task completed = await Task.WhenAny(delayTask, cancellation.Task);
        return ReferenceEquals(completed, delayTask);
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) =>
        left <= right ? left : right;

    private sealed record ResolvedRetryPolicy(
        TimeSpan BaseDelay,
        int MaxAttempts,
        TimeSpan MaxDelay);

    private sealed record ResolvedReconnectPolicy(
        ResolvedRetryPolicy Open,
        ResolvedRetryPolicy Idle,
        bool EnforceIdleAttemptLimit,
        HashSet<HttpStatusCode> RetryableStatusCodes);

    private sealed class ConnectionReadState
    {
        internal bool LeaseEnded { get; set; }

        internal bool ReachedEndOfStream { get; set; }

        internal bool StreamEnded { get; set; }

        internal int StreamVersion { get; set; }

        internal int NextLineIndex { get; set; }

        internal string LastCommitAt { get; set; } = "1970-01-01T00:00:00.000Z";
    }
}
