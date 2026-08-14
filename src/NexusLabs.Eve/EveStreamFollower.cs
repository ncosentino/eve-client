using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;

namespace NexusLabs.Eve;

internal static class EveStreamFollower
{
    private const string TailIndexHeader = "x-eve-stream-tail-index";

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
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ResolvedReconnectPolicy policy = ResolvePolicy(configuredPolicy, mode);
        int startIndex = initialStartIndex;
        TimeSpan reconnectDelay = policy.Idle.BaseDelay;
        int idleReconnects = 0;
        bool initialConnection = true;
        int? tailIndex = null;

        while (true)
        {
            bool deliveredEvent = false;
            HttpResponseMessage? response = await OpenStreamOrNullAsync(
                client,
                sessionId,
                startIndex,
                !follow && tailIndex is null,
                headers,
                protectedHeaderOverrides,
                policy,
                cancellationToken);
            if (response is null)
            {
                yield break;
            }

            using (response)
            {
                if (!follow && tailIndex is null)
                {
                    tailIndex = ReadTailIndex(response);
                }

                if (tailIndex is int openBound && startIndex > openBound)
                {
                    yield break;
                }

                await using IAsyncEnumerator<EveStreamEvent> connection = ReadConnectionAsync(
                    response,
                    maximumEventBytes,
                    cancellationToken).GetAsyncEnumerator(cancellationToken);
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
                    startIndex++;
                    deliveredEvent = true;
                    reconnectDelay = policy.Idle.BaseDelay;
                    idleReconnects = 0;
                    yield return streamEvent;

                    if (tailIndex is int bound && startIndex > bound)
                    {
                        yield break;
                    }
                }
            }

            if (cancellationToken.IsCancellationRequested
                || initialStartIndex < 0
                || policy.Idle.MaxAttempts == 0)
            {
                yield break;
            }

            if (policy.EnforceIdleAttemptLimit
                && !deliveredEvent
                && !initialConnection
                && ++idleReconnects >= policy.Idle.MaxAttempts)
            {
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
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using EveNdjsonLineReader reader = new(stream, maximumEventBytes);

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
                yield break;
            }

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            yield return EveStreamEvent.Parse(trimmed);
        }
    }

    private static async Task<HttpResponseMessage?> OpenStreamOrNullAsync(
        EveClient client,
        string sessionId,
        int startIndex,
        bool requestTailIndex,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyDictionary<string, string>? protectedHeaderOverrides,
        ResolvedReconnectPolicy policy,
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
                requestTailIndex,
                headers,
                protectedHeaderOverrides,
                policy,
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
        bool requestTailIndex,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyDictionary<string, string>? protectedHeaderOverrides,
        ResolvedReconnectPolicy policy,
        CancellationToken cancellationToken)
    {
        HttpStatusCode? lastStatusCode = null;
        string? lastBody = null;
        IReadOnlyDictionary<string, IReadOnlyList<string>>? lastHeaders = null;
        TimeSpan retryDelay = policy.Open.BaseDelay;
        Dictionary<string, string> queryParameters = [];
        if (startIndex != 0)
        {
            queryParameters["startIndex"] = startIndex.ToString(CultureInfo.InvariantCulture);
        }

        if (requestTailIndex)
        {
            queryParameters["includeTailIndex"] = "1";
        }

        IReadOnlyDictionary<string, string>? query = queryParameters.Count == 0
            ? null
            : queryParameters;

        for (int attempt = 0; attempt < policy.Open.MaxAttempts; attempt++)
        {
            using HttpRequestMessage request = await client.CreateRequestAsync(
                HttpMethod.Get,
                EveRequestKind.StreamSession,
                EveRoutes.StreamSession(sessionId),
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
            bool retryable = policy.RetryableStatusCodes.Contains(response.StatusCode);
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
}
