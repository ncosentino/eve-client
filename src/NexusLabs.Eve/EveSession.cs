using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace NexusLabs.Eve;

/// <summary>
/// Tracks one eve conversation's immutable session identifier and stream cursor.
/// </summary>
/// <remarks>
/// The handle is fixed. Once a turn assigns a session identifier, that identifier is retained
/// for every later turn, control, and stream, including after a terminal session boundary.
/// Start a separate conversation with <see cref="EveClient.CreateSession()"/> instead of
/// reusing a retired handle.
/// </remarks>
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
        SendAsync(EveMessageContent.FromText(message), null, cancellationToken);

    /// <summary>
    /// Sends a user turn carrying attachments or multi-part content.
    /// </summary>
    /// <param name="message">The user message.</param>
    /// <param name="cancellationToken">Cancels the POST and subsequent response stream.</param>
    /// <returns>Accepted turn metadata and its single-use event stream.</returns>
    public Task<EveMessageResponse> SendAsync(
        EveMessageContent message,
        CancellationToken cancellationToken) =>
        SendAsync(message, null, cancellationToken);

    /// <summary>
    /// Sends a user turn with context, headers, an output schema, or a reconnect policy.
    /// </summary>
    /// <remarks>
    /// eve <c>0.31.0</c> requires a turn to carry either a message or input responses, never
    /// both. Resolve pending human input with
    /// <see cref="RespondAsync(IReadOnlyList{EveInputResponse}, EveTurnOptions, CancellationToken)"/>.
    /// <para>
    /// When this message reaches a session that already has an active turn, eve <c>0.33.0</c> and
    /// later cancel and replace that turn unless
    /// <see cref="EveTurnOptions.TurnPolicy"/> is <see cref="EveTurnPolicy.Queue"/>. The policy is
    /// sent only for a message continuing an existing session.
    /// </para>
    /// </remarks>
    /// <param name="message">The user message.</param>
    /// <param name="options">Optional per-turn settings.</param>
    /// <param name="cancellationToken">Cancels the POST and subsequent response stream.</param>
    /// <returns>Accepted turn metadata and its single-use event stream.</returns>
    public Task<EveMessageResponse> SendAsync(
        EveMessageContent message,
        EveTurnOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        return PostTurnRequestAsync(
            state => EveRequestWriter.WriteMessageTurn(
                message,
                options,
                state.SessionId is not null),
            options,
            false,
            cancellationToken);
    }

    /// <summary>
    /// Resolves pending human-input requests without sending a user message.
    /// </summary>
    /// <param name="inputResponses">Responses to pending approvals or questions.</param>
    /// <param name="cancellationToken">Cancels the POST and subsequent response stream.</param>
    /// <returns>Accepted turn metadata and its single-use event stream.</returns>
    public Task<EveMessageResponse> RespondAsync(
        IReadOnlyList<EveInputResponse> inputResponses,
        CancellationToken cancellationToken) =>
        RespondAsync(inputResponses, null, cancellationToken);

    /// <summary>
    /// Resolves pending human-input requests with context, headers, or a reconnect policy.
    /// </summary>
    /// <remarks>
    /// eve <c>0.31.0</c> requires a turn to carry either a message or input responses, never
    /// both. Send a user message with
    /// <see cref="SendAsync(EveMessageContent, EveTurnOptions, CancellationToken)"/>.
    /// </remarks>
    /// <param name="inputResponses">Responses to pending approvals or questions.</param>
    /// <param name="options">Optional per-turn settings.</param>
    /// <param name="cancellationToken">Cancels the POST and subsequent response stream.</param>
    /// <returns>Accepted turn metadata and its single-use event stream.</returns>
    /// <exception cref="ArgumentException">No input responses were supplied.</exception>
    /// <exception cref="InvalidOperationException">
    /// The session has not started, so there is no pending input to resolve.
    /// </exception>
    public Task<EveMessageResponse> RespondAsync(
        IReadOnlyList<EveInputResponse> inputResponses,
        EveTurnOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputResponses);
        if (State.SessionId is null)
        {
            throw new InvalidOperationException(
                "A new eve session must start with a message. " +
                "There is no pending input to resolve.");
        }

        return PostTurnRequestAsync(
            _ => EveRequestWriter.WriteResponseTurn(inputResponses, options),
            options,
            true,
            cancellationToken);
    }

    private async Task<EveMessageResponse> PostTurnRequestAsync(
        Func<EveSessionState, byte[]> createBody,
        EveTurnOptions? options,
        bool mustDeliver,
        CancellationToken cancellationToken)
    {
        EveSessionState initialState = State;
        byte[] body = createBody(initialState);
        AcceptedTurn acceptedTurn = await PostTurnAsync(
            options,
            mustDeliver,
            initialState,
            body,
            cancellationToken);
        MarkAcceptedIfCurrent(initialState, acceptedTurn.SessionId);

        return new EveMessageResponse(
            acceptedTurn.SessionId,
            streamCancellationToken => CreateTurnStreamAsync(
                acceptedTurn,
                initialState,
                options,
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
            string status = ReadSuccessfulControlStatus(root, "cancel");

            switch (status)
            {
                case "no_active_turn":
                    // eve 0.31.0 validates this variant strictly, so an identifier here means
                    // the response does not match the contract rather than being extra data.
                    if (root.TryGetProperty("sessionId", out _))
                    {
                        throw new EveProtocolException(
                            "The eve cancel route returned an invalid response.");
                    }

                    return new EveCancellationOutcome(null, EveCancellationStatus.NoActiveTurn);
                case "accepted":
                    string acceptedSessionId = ReadRequiredSessionId(root, "sessionId", "cancel");
                    EnsureSessionIdMatches(acceptedSessionId, sessionId, "cancel");
                    return new EveCancellationOutcome(
                        acceptedSessionId,
                        EveCancellationStatus.Accepted);
                default:
                    throw new EveProtocolException(
                        "The eve cancel route returned an unknown status.");
            }
        }
        catch (JsonException exception)
        {
            throw new EveProtocolException(
                "The eve cancel route returned invalid JSON.",
                exception);
        }
    }

    /// <summary>
    /// Queues clearing of this session's durable model-message history while preserving the
    /// session identity, configuration, non-message state, limits, continuation token, and sandbox.
    /// </summary>
    /// <remarks>
    /// The clear is asynchronous on the durable stream. Consume events through
    /// <see cref="EveStreamEventKind.ContextCleared"/> and the following
    /// <see cref="EveStreamEventKind.SessionWaiting"/> boundary before sending another turn.
    /// Clearing a session that never started is a successful local no-op that issues no HTTP
    /// request. Unlike <see cref="ResetAsync(CancellationToken)"/>, a successful clear leaves the
    /// local cursor unchanged.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the clear request.</param>
    /// <returns>The successful clear disposition.</returns>
    /// <exception cref="EveClientException">The server returned a non-successful status.</exception>
    /// <exception cref="EveProtocolException">The body was not a recognized clear payload.</exception>
    public async Task<EveClearOutcome> ClearAsync(CancellationToken cancellationToken)
    {
        string? sessionId = State.SessionId;
        if (sessionId is null)
        {
            return new EveClearOutcome(EveClearStatus.NoActiveSession, null);
        }

        string responseBody = await PostControlAsync(
            EveRequestKind.ClearSession,
            EveRoutes.ClearSession(sessionId),
            cancellationToken);
        return ParseClearOutcome(responseBody, sessionId);
    }

    /// <summary>
    /// Terminally retires the durable session addressed by this handle.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="CancelAsync(CancellationToken)"/>, which only requests cancellation of
    /// the active turn, reset releases the durable session. The handle keeps its immutable
    /// session identifier, so it does not recycle into a new conversation; obtain a fresh handle
    /// from <see cref="EveClient.CreateSession()"/> to start one. Resetting a session that never
    /// started is a successful local no-op that issues no HTTP request.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the reset request.</param>
    /// <returns>The successful reset disposition.</returns>
    /// <exception cref="EveClientException">The server returned a non-successful status.</exception>
    /// <exception cref="EveProtocolException">The body was not a recognized reset payload.</exception>
    public async Task<EveResetOutcome> ResetAsync(CancellationToken cancellationToken)
    {
        string? sessionId = State.SessionId;
        if (sessionId is null)
        {
            return new EveResetOutcome(EveResetStatus.NoActiveSession, null);
        }

        string responseBody = await PostControlAsync(
            EveRequestKind.ResetSession,
            EveRoutes.ResetSession(sessionId),
            cancellationToken);
        return ParseResetOutcome(responseBody, sessionId);
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
    /// <exception cref="EveClientException">The server returned a non-successful status.</exception>
    /// <exception cref="EveProtocolException">
    /// The body was not a recognized compaction payload.
    /// </exception>
    public async Task<EveCompactOutcome> CompactAsync(CancellationToken cancellationToken)
    {
        string? sessionId = State.SessionId;
        if (sessionId is null)
        {
            return new EveCompactOutcome(EveCompactStatus.NoActiveSession, null);
        }

        string responseBody = await PostControlAsync(
            EveRequestKind.CompactSession,
            EveRoutes.CompactSession(sessionId),
            cancellationToken);
        return ParseCompactOutcome(responseBody, sessionId);
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
        EveTurnOptions? options,
        bool mustDeliver,
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
        int attempts = mustDeliver ? _client.DeliveryRetryAttempts : 1;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            using ByteArrayContent content = new(body);
            content.Headers.ContentType = new("application/json");
            using HttpRequestMessage httpRequest = await _client.CreateRequestAsync(
                HttpMethod.Post,
                requestKind,
                route,
                options?.Headers,
                content,
                cancellationToken,
                protectedHeaderOverrides: options?.ProtectedHeaderOverrides);
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

            return new AcceptedTurn(sessionId);
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
        EveTurnOptions? options,
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
                EveStreamFollowMode.ActiveTurnResponse,
                options?.Headers,
                options?.ProtectedHeaderOverrides,
                options?.StreamReconnectPolicy,
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
                events));
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
                EveStreamFollowMode.SessionStream,
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
                    events));
            }
        }
    }

    private void MarkAcceptedIfCurrent(
        EveSessionState expected,
        string sessionId)
    {
        lock (_stateGate)
        {
            if (!ReferenceEquals(_state, expected))
            {
                return;
            }

            _state = expected with
            {
                SessionId = sessionId,
            };
        }
    }

    private async Task<string> PostControlAsync(
            EveRequestKind kind,
            string route,
            CancellationToken cancellationToken)
    {
        using ByteArrayContent content = new([]);
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

    private static EveClearOutcome ParseClearOutcome(string body, string? currentSessionId)
    {
        const string routeLabel = "clear";
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            string status = ReadSuccessfulControlStatus(root, routeLabel);

            switch (status)
            {
                case "no_active_session":
                    return new EveClearOutcome(EveClearStatus.NoActiveSession, null);
                case "accepted":
                    string sessionId = ReadRequiredSessionId(root, "sessionId", routeLabel);
                    EnsureSessionIdMatches(sessionId, currentSessionId, routeLabel);
                    return new EveClearOutcome(EveClearStatus.Accepted, sessionId);
                default:
                    throw new EveProtocolException(
                        "The eve clear route returned an unknown status.");
            }
        }
        catch (JsonException exception)
        {
            throw new EveProtocolException(
                "The eve clear route returned invalid JSON.",
                exception);
        }
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

    private void SetState(EveSessionState state)
    {
        lock (_stateGate)
        {
            _state = state;
        }
    }

    // eve 0.31.0 made the session handle fixed: no boundary event recycles the identifier and
    // no continuation token is carried, so advancing is purely a cursor addition.
    private static EveSessionState AdvanceState(
        EveSessionState initialState,
        string sessionId,
        IReadOnlyList<EveStreamEvent> events) =>
        new()
        {
            SessionId = sessionId,
            StreamIndex = initialState.StreamIndex + events.Count,
        };

    private sealed record AcceptedTurn(string SessionId);
}
