using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Tracks one eve conversation's continuation token, session identifier, and stream cursor.
/// </summary>
public sealed class EveSession
{
    private readonly EveClient _client;
    private readonly object _stateGate = new();
    private EveSessionState _state;

    internal EveSession(EveClient client, EveSessionState state)
    {
        _client = client;
        _state = state;
    }

    /// <summary>
    /// Gets the current serializable session cursor.
    /// Consume a response stream before persisting a fully advanced cursor.
    /// </summary>
    public EveSessionState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Sends a plain-text user turn.
    /// </summary>
    /// <param name="message">The user message.</param>
    /// <param name="cancellationToken">Cancels the POST and subsequent response stream.</param>
    /// <returns>Accepted turn metadata and its single-use event stream.</returns>
    public Task<EveMessageResponse> SendAsync(
        string message,
        CancellationToken cancellationToken) =>
        SendAsync(
            new EveSendTurnRequest
            {
                Message = EveMessageContent.FromText(message),
            },
            cancellationToken);

    /// <summary>
    /// Sends a full user turn, including attachments, input responses, context, or output schema.
    /// </summary>
    /// <param name="request">The turn payload.</param>
    /// <param name="cancellationToken">Cancels the POST and subsequent response stream.</param>
    /// <returns>Accepted turn metadata and its single-use event stream.</returns>
    public async Task<EveMessageResponse> SendAsync(
        EveSendTurnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EveSessionState initialState = State;
        byte[] body = EveRequestWriter.WriteTurn(request, initialState.ContinuationToken);
        AcceptedTurn acceptedTurn = await PostTurnAsync(
            request,
            initialState,
            body,
            cancellationToken);
        MarkAcceptedIfCurrent(
            initialState,
            acceptedTurn.SessionId,
            acceptedTurn.ContinuationToken);

        return new EveMessageResponse(
            acceptedTurn.ContinuationToken,
            acceptedTurn.SessionId,
            streamCancellationToken => CreateTurnStreamAsync(
                acceptedTurn,
                initialState,
                request,
                cancellationToken,
                streamCancellationToken));
    }

    /// <summary>
    /// Requests cooperative cancellation of the active turn.
    /// Continue consuming the turn stream to observe its terminal boundary.
    /// </summary>
    /// <param name="cancellationToken">Cancels the cancellation request.</param>
    /// <returns>The successful cancellation disposition.</returns>
    public Task<EveCancellationOutcome> CancelAsync(CancellationToken cancellationToken) =>
        CancelAsync(null, cancellationToken);

    /// <summary>
    /// Requests cooperative cancellation of the active turn with an optional turn guard.
    /// Continue consuming the turn stream to observe its terminal boundary.
    /// </summary>
    /// <param name="turnId">
    /// An optional guard that limits cancellation to the observed turn identifier.
    /// </param>
    /// <param name="cancellationToken">Cancels the cancellation request.</param>
    /// <returns>The successful cancellation disposition.</returns>
    public async Task<EveCancellationOutcome> CancelAsync(
        string? turnId,
        CancellationToken cancellationToken)
    {
        string? sessionId = State.SessionId;
        if (sessionId is null)
        {
            throw new InvalidOperationException(
                "The eve session has no session identifier. Send a message first.");
        }

        byte[] body = turnId is null ? [] : EveRequestWriter.WriteCancel(turnId);
        using ByteArrayContent content = new(body);
        content.Headers.ContentType = new("application/json");
        using HttpRequestMessage request = await _client.CreateRequestAsync(
            HttpMethod.Post,
            EveRequestKind.CancelTurn,
            EveRoutes.CancelTurn(sessionId),
            null,
            content,
            cancellationToken);
        using HttpResponseMessage response = await _client.SendTransportAsync(
            request,
            false,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await EveClient.CreateClientExceptionAsync(response, cancellationToken);
        }

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("ok", out JsonElement ok)
                || ok.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("sessionId", out JsonElement responseSessionId)
                || responseSessionId.ValueKind != JsonValueKind.String
                || !string.Equals(
                    responseSessionId.GetString(),
                    sessionId,
                    StringComparison.Ordinal)
                || !root.TryGetProperty("status", out JsonElement status)
                || status.ValueKind != JsonValueKind.String)
            {
                throw new EveProtocolException(
                    "The eve cancel route returned an invalid response.");
            }

            EveCancellationStatus cancellationStatus = status.GetString() switch
            {
                "accepted" => EveCancellationStatus.Accepted,
                "no_active_turn" => EveCancellationStatus.NoActiveTurn,
                _ => throw new EveProtocolException(
                    "The eve cancel route returned an unknown status."),
            };
            return new EveCancellationOutcome(sessionId, cancellationStatus);
        }
        catch (JsonException exception)
        {
            throw new EveProtocolException(
                "The eve cancel route returned invalid JSON.",
                exception);
        }
    }

    /// <summary>
    /// Terminally retires the durable session that owns this handle's continuation token.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="CancelAsync(CancellationToken)"/>, which only requests cancellation of
    /// the active turn, reset releases the durable session so the next send starts a fresh
    /// conversation. Resetting a session that never started is a successful local no-op that
    /// issues no HTTP request. After a successful reset the local state is empty.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the reset request.</param>
    /// <returns>The successful reset disposition.</returns>
    /// <exception cref="InvalidOperationException">
    /// The session has an identifier but no continuation token, so its outstanding event stream
    /// must be consumed before resetting.
    /// </exception>
    /// <exception cref="EveClientException">The server returned a non-successful status.</exception>
    /// <exception cref="EveProtocolException">The body was not a recognized reset payload.</exception>
    public async Task<EveResetOutcome> ResetAsync(CancellationToken cancellationToken)
    {
        EveSessionState state = State;
        string? continuationToken = RequireContinuationTokenOrNull(
            state,
            "resetting");
        if (continuationToken is null)
        {
            SetState(new EveSessionState());
            return new EveResetOutcome(EveResetStatus.NoActiveSession, null);
        }

        string responseBody = await PostContinuationTokenControlAsync(
            EveRequestKind.ResetSession,
            EveRoutes.ResetSession,
            continuationToken,
            cancellationToken);
        EveResetOutcome outcome = ParseResetOutcome(responseBody, state.SessionId);
        ResetStateIfCurrent(state);
        return outcome;
    }

    /// <summary>
    /// Queues context compaction for the durable session that owns this handle's continuation
    /// token without sending model input.
    /// </summary>
    /// <remarks>
    /// Compaction is asynchronous. Consume the durable event stream through its next session
    /// boundary before sending another turn; <c>compaction.completed</c> confirms that
    /// summarization succeeded. Compacting a session that never started is a successful local
    /// no-op that issues no HTTP request. Unlike <see cref="ResetAsync(CancellationToken)"/>,
    /// compaction preserves the local session cursor.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the compaction request.</param>
    /// <returns>The successful compaction disposition.</returns>
    /// <exception cref="InvalidOperationException">
    /// The session has an identifier but no continuation token, so its outstanding event stream
    /// must be consumed before compacting.
    /// </exception>
    /// <exception cref="EveClientException">The server returned a non-successful status.</exception>
    /// <exception cref="EveProtocolException">
    /// The body was not a recognized compaction payload.
    /// </exception>
    public async Task<EveCompactOutcome> CompactAsync(CancellationToken cancellationToken)
    {
        EveSessionState state = State;
        string? continuationToken = RequireContinuationTokenOrNull(
            state,
            "compacting");
        if (continuationToken is null)
        {
            return new EveCompactOutcome(EveCompactStatus.NoActiveSession, null);
        }

        string responseBody = await PostContinuationTokenControlAsync(
            EveRequestKind.CompactSession,
            EveRoutes.CompactSession,
            continuationToken,
            cancellationToken);
        return ParseCompactOutcome(responseBody, state.SessionId);
    }

    /// <summary>
    /// Attaches to the existing session stream from the stored or overridden cursor.
    /// Unlike a sent-turn response, this stream remains boundary-blind.
    /// </summary>
    /// <param name="cancellationToken">Stops local stream consumption.</param>
    /// <returns>The durable session event stream.</returns>
    public IAsyncEnumerable<EveStreamEvent> StreamAsync(CancellationToken cancellationToken) =>
        StreamAsync(null, cancellationToken);

    /// <summary>
    /// Attaches to the existing session stream from the stored or overridden cursor.
    /// Unlike a sent-turn response, this stream remains boundary-blind.
    /// </summary>
    /// <remarks>
    /// The stream follows live events by default. Set
    /// <see cref="EveStreamOptions.Follow"/> to <see langword="false"/> for a bounded catch-up
    /// read that completes once the cursor passes the durable tail observed when the stream
    /// opened. The stored cursor advances past every consumed event in both modes.
    /// </remarks>
    /// <param name="options">Optional cursor, bound, and reconnect overrides.</param>
    /// <param name="cancellationToken">Stops local stream consumption.</param>
    /// <returns>The durable session event stream.</returns>
    /// <exception cref="InvalidOperationException">
    /// The session has no identifier because no message has been sent yet.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A bounded read was requested with a negative effective start cursor, which cannot be
    /// bounded because its absolute position is unknown.
    /// </exception>
    /// <exception cref="EveProtocolException">
    /// A bounded read did not receive a valid durable tail index from the server.
    /// </exception>
    public IAsyncEnumerable<EveStreamEvent> StreamAsync(
        EveStreamOptions? options,
        CancellationToken cancellationToken)
    {
        EveSessionState initialState = State;
        if (initialState.SessionId is null)
        {
            throw new InvalidOperationException(
                "The eve session has no session identifier. Send a message first.");
        }

        int startIndex = options?.StartIndex ?? initialState.StreamIndex;
        bool follow = options?.Follow ?? true;
        if (!follow && startIndex < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                startIndex,
                "A bounded eve stream requires a nonnegative start cursor. " +
                "A tail-relative cursor cannot be bounded.");
        }

        return StreamAndAdvanceAsync(
            initialState,
            startIndex,
            follow,
            options?.ReconnectPolicy,
            cancellationToken);
    }

    private async Task<AcceptedTurn> PostTurnAsync(
        EveSendTurnRequest request,
        EveSessionState state,
        byte[] body,
        CancellationToken cancellationToken)
    {
        string route = state.SessionId is null
            ? EveRoutes.CreateSession
            : EveRoutes.ContinueSession(state.SessionId);
        EveRequestKind requestKind = state.SessionId is null
            ? EveRequestKind.CreateSession
            : EveRequestKind.ContinueSession;
        bool mustDeliver = request.InputResponses is { Count: > 0 };
        int attempts = mustDeliver ? _client.DeliveryRetryAttempts : 1;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            using ByteArrayContent content = new(body);
            content.Headers.ContentType = new("application/json");
            using HttpRequestMessage httpRequest = await _client.CreateRequestAsync(
                HttpMethod.Post,
                requestKind,
                route,
                request.Headers,
                content,
                cancellationToken,
                protectedHeaderOverrides: request.ProtectedHeaderOverrides);
            using HttpResponseMessage response = await _client.SendTransportAsync(
                httpRequest,
                false,
                cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ParseAcceptedTurn(response, responseBody, state.SessionId);
            }

            EveClientException exception = EveClient.CreateClientException(response, responseBody);
            bool retryable = response.StatusCode == HttpStatusCode.InternalServerError
                && responseBody.Contains(
                    "target session was not found",
                    StringComparison.OrdinalIgnoreCase);
            if (!retryable || attempt == attempts - 1)
            {
                throw exception;
            }

            await Task.Delay(
                _client.DeliveryRetryDelay,
                _client.TimeProvider,
                cancellationToken);
        }

        throw new InvalidOperationException("The eve turn delivery loop ended unexpectedly.");
    }

    private static AcceptedTurn ParseAcceptedTurn(
        HttpResponseMessage response,
        string body,
        string? currentSessionId)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            string? sessionId = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("sessionId", out JsonElement bodySessionId)
                && bodySessionId.ValueKind == JsonValueKind.String
                    ? bodySessionId.GetString()
                    : null;
            sessionId ??= response.Headers.TryGetValues(
                    EveProtocol.SessionIdHeaderName,
                    out IEnumerable<string>? sessionHeaderValues)
                ? sessionHeaderValues.FirstOrDefault()?.Trim()
                : null;
            sessionId ??= currentSessionId;

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new EveProtocolException(
                    "The eve message route did not return a session identifier.");
            }

            string? continuationToken = root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty(
                    "continuationToken",
                    out JsonElement continuationTokenValue)
                && continuationTokenValue.ValueKind == JsonValueKind.String
                    ? continuationTokenValue.GetString()
                    : null;
            return new AcceptedTurn(sessionId, continuationToken);
        }
        catch (JsonException exception)
        {
            throw new EveProtocolException(
                "The eve message route returned invalid JSON.",
                exception);
        }
    }

    private async IAsyncEnumerable<EveStreamEvent> CreateTurnStreamAsync(
        AcceptedTurn acceptedTurn,
        EveSessionState initialState,
        EveSendTurnRequest request,
        CancellationToken sendCancellationToken,
        [EnumeratorCancellation] CancellationToken streamCancellationToken)
    {
        using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            sendCancellationToken,
            streamCancellationToken);
        List<EveStreamEvent> events = [];

        try
        {
            int startIndex = string.Equals(
                initialState.SessionId,
                acceptedTurn.SessionId,
                StringComparison.Ordinal)
                ? initialState.StreamIndex
                : 0;
            await foreach (EveStreamEvent streamEvent in EveStreamFollower.FollowAsync(
                _client,
                acceptedTurn.SessionId,
                startIndex,
                true,
                request.Headers,
                request.ProtectedHeaderOverrides,
                request.StreamReconnectPolicy,
                _client.MaxStreamEventBytes,
                linkedSource.Token))
            {
                events.Add(streamEvent);
                yield return streamEvent;
                if (streamEvent.IsCurrentTurnBoundary)
                {
                    yield break;
                }
            }
        }
        finally
        {
            SetState(AdvanceState(
                initialState,
                acceptedTurn.SessionId,
                acceptedTurn.ContinuationToken,
                events,
                _client.PreserveCompletedSessions));
        }
    }

    private async IAsyncEnumerable<EveStreamEvent> StreamAndAdvanceAsync(
        EveSessionState initialState,
        int startIndex,
        bool follow,
        EveStreamReconnectPolicy? reconnectPolicy,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        List<EveStreamEvent> events = [];

        try
        {
            await foreach (EveStreamEvent streamEvent in EveStreamFollower.FollowAsync(
                _client,
                initialState.SessionId!,
                startIndex,
                follow,
                null,
                null,
                reconnectPolicy,
                _client.MaxStreamEventBytes,
                cancellationToken))
            {
                events.Add(streamEvent);
                yield return streamEvent;
            }
        }
        finally
        {
            if (startIndex >= 0)
            {
                EveSessionState cursorState = initialState with
                {
                    StreamIndex = startIndex,
                };
                SetState(AdvanceState(
                    cursorState,
                    initialState.SessionId!,
                    initialState.ContinuationToken,
                    events,
                    _client.PreserveCompletedSessions));
            }
        }
    }

    private void MarkAcceptedIfCurrent(
        EveSessionState expected,
        string sessionId,
        string? continuationToken)
    {
        lock (_stateGate)
        {
            if (!ReferenceEquals(_state, expected))
            {
                return;
            }

            _state = expected with
            {
                ContinuationToken = continuationToken ?? expected.ContinuationToken,
                SessionId = sessionId,
            };
        }
    }

    private async Task<string> PostContinuationTokenControlAsync(
            EveRequestKind kind,
            string route,
            string continuationToken,
            CancellationToken cancellationToken)
    {
        byte[] body = EveRequestWriter.WriteContinuationToken(continuationToken);
        using ByteArrayContent content = new(body);
        content.Headers.ContentType = new("application/json");
        using HttpRequestMessage request = await _client.CreateRequestAsync(
            HttpMethod.Post,
            kind,
            route,
            null,
            content,
            cancellationToken);
        using HttpResponseMessage response = await _client.SendTransportAsync(
            request,
            false,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await EveClient.CreateClientExceptionAsync(response, cancellationToken);
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string? RequireContinuationTokenOrNull(
        EveSessionState state,
        string operationGerund)
    {
        string? continuationToken = state.ContinuationToken;
        if (continuationToken is not null)
        {
            return continuationToken;
        }

        if (state.SessionId is not null)
        {
            throw new InvalidOperationException(
                "The eve session has no continuation token. " +
                $"Consume its event stream before {operationGerund}.");
        }

        return null;
    }

    private static EveResetOutcome ParseResetOutcome(string body, string? currentSessionId)
    {
        const string routeLabel = "reset";
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            string status = ReadSuccessfulControlStatus(root, routeLabel);

            switch (status)
            {
                case "no_active_session":
                    return new EveResetOutcome(EveResetStatus.NoActiveSession, null);
                case "reset":
                    string previousSessionId = ReadRequiredSessionId(
                        root,
                        "previousSessionId",
                        routeLabel);
                    EnsureSessionIdMatches(previousSessionId, currentSessionId, routeLabel);
                    return new EveResetOutcome(
                        EveResetStatus.Reset,
                        previousSessionId);
                default:
                    throw new EveProtocolException(
                        "The eve reset route returned an unknown status.");
            }
        }
        catch (JsonException exception)
        {
            throw new EveProtocolException(
                "The eve reset route returned invalid JSON.",
                exception);
        }
    }

    private static EveCompactOutcome ParseCompactOutcome(string body, string? currentSessionId)
    {
        const string routeLabel = "compact";
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            string status = ReadSuccessfulControlStatus(root, routeLabel);

            switch (status)
            {
                case "no_active_session":
                    return new EveCompactOutcome(EveCompactStatus.NoActiveSession, null);
                case "accepted":
                    string sessionId = ReadRequiredSessionId(root, "sessionId", routeLabel);
                    EnsureSessionIdMatches(sessionId, currentSessionId, routeLabel);
                    return new EveCompactOutcome(EveCompactStatus.Accepted, sessionId);
                default:
                    throw new EveProtocolException(
                        "The eve compact route returned an unknown status.");
            }
        }
        catch (JsonException exception)
        {
            throw new EveProtocolException(
                "The eve compact route returned invalid JSON.",
                exception);
        }
    }

    private static string ReadSuccessfulControlStatus(JsonElement root, string routeLabel)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("ok", out JsonElement ok)
            || ok.ValueKind != JsonValueKind.True
            || !root.TryGetProperty("status", out JsonElement statusElement)
            || statusElement.ValueKind != JsonValueKind.String
            || statusElement.GetString() is not string status)
        {
            throw new EveProtocolException(
                $"The eve {routeLabel} route returned an invalid response.");
        }

        return status;
    }

    private static string ReadRequiredSessionId(
        JsonElement root,
        string propertyName,
        string routeLabel)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(value.GetString()))
        {
            throw new EveProtocolException(
                $"The eve {routeLabel} route returned an invalid response.");
        }

        return value.GetString()!;
    }

    private static void EnsureSessionIdMatches(
        string responseSessionId,
        string? currentSessionId,
        string routeLabel)
    {
        if (currentSessionId is not null
            && !string.Equals(responseSessionId, currentSessionId, StringComparison.Ordinal))
        {
            throw new EveProtocolException(
                $"The eve {routeLabel} route returned an invalid response.");
        }
    }

    private void ResetStateIfCurrent(EveSessionState expected)
    {
        lock (_stateGate)
        {
            if (!ReferenceEquals(_state, expected))
            {
                return;
            }

            _state = new EveSessionState();
        }
    }

    private void SetState(EveSessionState state)
    {
        lock (_stateGate)
        {
            _state = state;
        }
    }

    private static EveSessionState AdvanceState(
        EveSessionState initialState,
        string sessionId,
        string? continuationToken,
        IReadOnlyList<EveStreamEvent> events,
        bool preserveCompletedSessions)
    {
        EveStreamEvent? boundary = events
            .Reverse()
            .FirstOrDefault(static streamEvent => streamEvent.IsCurrentTurnBoundary);
        int streamIndex = initialState.StreamIndex + events.Count;

        if (boundary?.Kind == EveStreamEventKind.SessionWaiting)
        {
            if (!boundary.Data.TryGetProperty(
                    "continuationToken",
                    out JsonElement waitingContinuationToken)
                || waitingContinuationToken.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(waitingContinuationToken.GetString()))
            {
                throw new EveProtocolException(
                    "A session.waiting event did not contain a continuation token.");
            }

            return new EveSessionState
            {
                ContinuationToken = waitingContinuationToken.GetString(),
                SessionId = sessionId,
                StreamIndex = streamIndex,
            };
        }

        if (boundary?.Kind == EveStreamEventKind.SessionCompleted
            && preserveCompletedSessions)
        {
            return new EveSessionState
            {
                ContinuationToken = continuationToken ?? initialState.ContinuationToken,
                SessionId = sessionId,
                StreamIndex = streamIndex,
            };
        }

        if (boundary is null)
        {
            return new EveSessionState
            {
                ContinuationToken = continuationToken ?? initialState.ContinuationToken,
                SessionId = sessionId,
                StreamIndex = streamIndex,
            };
        }

        return new EveSessionState();
    }

    private sealed record AcceptedTurn(string SessionId, string? ContinuationToken);
}
