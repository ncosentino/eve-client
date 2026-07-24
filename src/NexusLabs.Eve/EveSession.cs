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
        MarkAcceptedIfCurrent(initialState, acceptedTurn.SessionId);

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
    /// <param name="options">Optional cursor and reconnect overrides.</param>
    /// <param name="cancellationToken">Stops local stream consumption.</param>
    /// <returns>The durable session event stream.</returns>
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
        return StreamAndAdvanceAsync(
            initialState,
            startIndex,
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
                cancellationToken);
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
                request.Headers,
                request.StreamReconnectPolicy,
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
                null,
                reconnectPolicy,
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

    private void MarkAcceptedIfCurrent(EveSessionState expected, string sessionId)
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
