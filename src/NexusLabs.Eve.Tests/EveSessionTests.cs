using System.Net;
using System.Text;
using System.Text.Json;

namespace NexusLabs.Eve.Tests;

public sealed class EveSessionTests
{
    private const string PerTurnAuthorization = "Token end-user";

    [Test]
    public async Task SendAsync_AggregatesTurnAndAdvancesWaitingSession(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"message.completed","data":{"finishReason":"stop","message":"Done.","sequence":1,"stepIndex":0,"turnId":"turn_1"}}""",
            """{"type":"result.completed","data":{"result":{"count":2},"sequence":2,"stepIndex":0,"turnId":"turn_1"}}""",
            """{"type":"input.requested","data":{"requests":[{"requestId":"approval_1","prompt":"Approve?","display":"confirmation","options":[{"id":"approve","label":"Approve"}],"action":{"kind":"tool-call"}}],"sequence":3,"stepIndex":0,"turnId":"turn_1"}}""",
            """{"type":"session.waiting","data":{"continuationToken":"eve:rekeyed","wait":"next-user-message"}}""")));
        EveClient client = CreateClient(transport);
        EveSession session = client.CreateSession();
        EveSendTurnRequest request = new()
        {
            Message = EveMessageContent.FromText("Run the check."),
            ClientContext = EveClientContext.FromJson(EveJsonElementFactory.CreateObject(writer =>
            {
                writer.WriteString("route", "/billing");
            })),
            OutputSchema = EveJsonElementFactory.CreateObject(writer =>
            {
                writer.WriteString("type", "object");
            }),
            Headers = new Dictionary<string, string>
            {
                ["x-request-id"] = "request_1",
            },
        };

        EveMessageResponse response = await session.SendAsync(request, cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(response.SessionId).IsEqualTo("session_1");
        await Assert.That(response.ContinuationToken).IsEqualTo("eve:accepted");
        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Waiting);
        await Assert.That(outcome.Message).IsEqualTo("Done.");
        await Assert.That(outcome.Events.Count).IsEqualTo(4);
        await Assert.That(outcome.InputRequests.Count).IsEqualTo(1);
        await Assert.That(outcome.InputRequests[0].RequestId).IsEqualTo("approval_1");
        await Assert.That(outcome.InputRequests[0].Options.Count).IsEqualTo(1);
        await Assert.That(outcome.Data.HasValue)
            .IsTrue()
            .Because("The stream emitted result.completed.");
        await Assert.That(outcome.Data.GetValueOrDefault().GetProperty("count").GetInt32())
            .IsEqualTo(2);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            ContinuationToken = "eve:rekeyed",
            SessionId = "session_1",
            StreamIndex = 4,
        });
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session");
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream");
        await Assert.That(handler.Calls[0].Headers["x-request-id"]).IsEqualTo("request_1");
        await Assert.That(handler.Calls[1].Headers["x-request-id"]).IsEqualTo("request_1");

        using JsonDocument sentBody = JsonDocument.Parse(handler.Calls[0].Body!);
        await Assert.That(sentBody.RootElement.GetProperty("message").GetString())
            .IsEqualTo("Run the check.");
        await Assert.That(sentBody.RootElement.GetProperty("clientContext")
            .GetProperty("route")
            .GetString())
            .IsEqualTo("/billing");
        await Assert.That(sentBody.RootElement.GetProperty("outputSchema")
            .GetProperty("type")
            .GetString())
            .IsEqualTo("object");
    }

    [Test]
    public Task BearerAuthentication_AppliesAuthorizationToRequestHeaders(
        CancellationToken cancellationToken) =>
        AssertAuthenticationHeaderPlacementAsync(
            new EveBearerAuthentication("service-token"),
            cancellationToken);

    [Test]
    public Task BasicAuthentication_AppliesAuthorizationToRequestHeaders(
        CancellationToken cancellationToken) =>
        AssertAuthenticationHeaderPlacementAsync(
            new EveBasicAuthentication("agent-client", "password"),
            cancellationToken);

    [Test]
    public async Task SendAsync_AppliesHeadersToCompatibleCollections(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"status":"ready","workflowId":"workflow_1"}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.completed"}""")));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Headers = new Dictionary<string, string>
                {
                    ["content-language"] = "en-US",
                    ["x-client"] = "extension",
                },
            });

        await client.GetHealthAsync(cancellationToken);
        EveMessageResponse response = await client
            .CreateSession()
            .SendAsync("Place headers", cancellationToken);
        await response.GetOutcomeAsync(cancellationToken);

        RecordedHttpCall healthCall = handler.Calls[0];
        RecordedHttpCall call = handler.Calls[1];
        RecordedHttpCall streamCall = handler.Calls[2];
        await Assert.That(healthCall.Headers.ContainsKey("content-language")).IsFalse();
        await Assert.That(call.ContentHeaders["content-language"]).IsEqualTo("en-US");
        await Assert.That(call.RequestHeaders["x-client"]).IsEqualTo("extension");
        await Assert.That(call.ContentHeaders.ContainsKey("x-client")).IsFalse();
        await Assert.That(streamCall.Headers.ContainsKey("content-language")).IsFalse();
    }

    [Test]
    public async Task SendAsync_ContinuesWithWaitingContinuationToken(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"}}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse(
            "session_1",
            "eve:second")));
        EveSession session = CreateClient(transport, 1024).CreateSession();

        EveMessageResponse first = await session.SendAsync("First", cancellationToken);
        await first.GetOutcomeAsync(cancellationToken);
        await session.SendAsync("Second", cancellationToken);

        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        await Assert.That(handler.Calls[2].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1");
        using JsonDocument body = JsonDocument.Parse(handler.Calls[2].Body!);
        await Assert.That(body.RootElement.GetProperty("continuationToken").GetString())
            .IsEqualTo("eve:next");
        await Assert.That(body.RootElement.GetProperty("message").GetString()).IsEqualTo("Second");
    }

    [Test]
    public async Task RequestHeadersProvider_ScopesBootstrapHeaderByRequestKind(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"status":"ready","workflowId":"workflow_1"}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """
            {
              "kind": "eve-agent-info",
              "version": 1,
              "mode": "production",
              "agent": { "name": "Agent", "model": { "id": "model_1" } },
              "capabilities": { "devRoutes": false }
            }
            """)));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"turn.started","data":{"sequence":1,"turnId":"turn_1"}}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"}}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse(
            "session_1",
            "eve:continued")));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"status":"reset","previousSessionId":"session_1"}""")));
        List<EveRequestKind> requestKinds = [];
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                HeadersProvider = _ =>
                    ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                        new Dictionary<string, string>
                        {
                            ["x-infrastructure"] = "present",
                        }),
                RequestHeadersProvider = (context, _) =>
                {
                    requestKinds.Add(context.Kind);
                    Dictionary<string, string> headers = new()
                    {
                        ["x-request-kind"] = context.Kind.ToString(),
                    };
                    if (context.Kind == EveRequestKind.CreateSession)
                    {
                        headers["x-session-bootstrap"] = "bootstrap";
                    }

                    return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(headers);
                },
            });

        await client.GetHealthAsync(cancellationToken);
        await client.GetInfoAsync(cancellationToken);
        EveSession session = client.CreateSession();
        EveMessageResponse firstResponse = await session.SendAsync(
            new EveSendTurnRequest
            {
                Message = EveMessageContent.FromText("First"),
                Headers = new Dictionary<string, string>
                {
                    ["x-turn"] = "first",
                },
                StreamReconnectPolicy = ZeroDelayReconnectPolicy(),
            },
            cancellationToken);
        await firstResponse.GetOutcomeAsync(cancellationToken);
        await session.SendAsync("Second", cancellationToken);
        await session.CancelAsync(cancellationToken);
        await session.ResetAsync(cancellationToken);

        EveRequestKind[] expectedKinds =
        [
            EveRequestKind.Health,
            EveRequestKind.Info,
            EveRequestKind.CreateSession,
            EveRequestKind.StreamSession,
            EveRequestKind.StreamSession,
            EveRequestKind.ContinueSession,
            EveRequestKind.CancelTurn,
            EveRequestKind.ResetSession,
        ];
        await Assert.That(requestKinds.Count).IsEqualTo(expectedKinds.Length);
        await Assert.That(handler.Calls.Count).IsEqualTo(expectedKinds.Length);
        for (int index = 0; index < expectedKinds.Length; index++)
        {
            await Assert.That(requestKinds[index]).IsEqualTo(expectedKinds[index]);
            await Assert.That(handler.Calls[index].Headers["x-infrastructure"])
                .IsEqualTo("present");
            await Assert.That(handler.Calls[index].Headers["x-request-kind"])
                .IsEqualTo(expectedKinds[index].ToString());
            await Assert.That(handler.Calls[index].Headers.ContainsKey("x-session-bootstrap"))
                .IsEqualTo(index == 2);
        }

        await Assert.That(handler.Calls[2].Headers["x-turn"]).IsEqualTo("first");
        await Assert.That(handler.Calls[3].Headers["x-turn"]).IsEqualTo("first");
        await Assert.That(handler.Calls[4].Headers["x-turn"]).IsEqualTo("first");
        await Assert.That(handler.Calls[5].Headers.ContainsKey("x-turn")).IsFalse();
    }

    [Test]
    public async Task PerTurnAuthorization_OverridesAuthenticationOnPostAndStreamReconnect(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"turn.started","data":{"sequence":1,"turnId":"turn_1"}}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"}}""")));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Headers = new Dictionary<string, string>
                {
                    ["authorization"] = "client-static",
                },
                Authentication = new EveBearerAuthentication("deployment-token"),
            });

        EveMessageResponse response = await client.CreateSession().SendAsync(
            new EveSendTurnRequest
            {
                Message = EveMessageContent.FromText("Forward the caller identity"),
                Headers = new Dictionary<string, string>
                {
                    ["Authorization"] = PerTurnAuthorization,
                },
                StreamReconnectPolicy = ZeroDelayReconnectPolicy(),
            },
            cancellationToken);
        await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        foreach (RecordedHttpCall call in handler.Calls)
        {
            IReadOnlyList<string> authorizationValues = call.RequestHeaderValues["authorization"];
            await Assert.That(authorizationValues.Count).IsEqualTo(1);
            await Assert.That(authorizationValues[0]).IsEqualTo(PerTurnAuthorization);
        }
    }

    [Test]
    public async Task VercelOidc_KeepsTrustedIdpTokenWhenPerTurnAuthorizationWins(
        CancellationToken cancellationToken)
    {
        const string oidcToken = "oidc-token";
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"}}""")));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Authentication = new EveVercelOidcAuthentication(oidcToken),
            });

        EveMessageResponse response = await client.CreateSession().SendAsync(
            new EveSendTurnRequest
            {
                Message = EveMessageContent.FromText("Forward the caller identity"),
                Headers = new Dictionary<string, string>
                {
                    ["authorization"] = PerTurnAuthorization,
                },
            },
            cancellationToken);
        await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        foreach (RecordedHttpCall call in handler.Calls)
        {
            IReadOnlyList<string> authorizationValues = call.RequestHeaderValues["authorization"];
            await Assert.That(authorizationValues.Count).IsEqualTo(1);
            await Assert.That(authorizationValues[0]).IsEqualTo(PerTurnAuthorization);
            await Assert.That(call.Headers[EveProtocol.VercelTrustedOidcTokenHeaderName])
                .IsEqualTo(oidcToken);
        }
    }

    [Test]
    public async Task ClientLevelHeaderLayers_RemainBelowAuthentication(
        CancellationToken cancellationToken)
    {
        EveBearerAuthentication authentication = new("deployment-token");
        IReadOnlyDictionary<string, string> expectedHeaders =
            await authentication.GetHeadersAsync(cancellationToken);
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Headers = new Dictionary<string, string>
                {
                    ["authorization"] = "client-static",
                },
                HeadersProvider = static _ =>
                    ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                        new Dictionary<string, string>
                        {
                            ["authorization"] = "client-dynamic",
                        }),
                RequestHeadersProvider = static (_, _) =>
                    ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                        new Dictionary<string, string>
                        {
                            ["authorization"] = "request-aware",
                        }),
                Authentication = authentication,
            });

        await client.CreateSession().SendAsync("Authenticate", cancellationToken);

        RecordedHttpCall call = handler.Calls[0];
        IReadOnlyList<string> authorizationValues = call.RequestHeaderValues["authorization"];
        await Assert.That(authorizationValues.Count).IsEqualTo(1);
        await Assert.That(authorizationValues[0]).IsEqualTo(expectedHeaders["authorization"]);
    }


    [Test]
    public async Task CompletedSession_ResetsByDefault(CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.completed"}""")));
        EveSession session = CreateClient(transport).CreateSession();

        EveMessageResponse response = await session.SendAsync("One shot", cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Completed);
        await Assert.That(session.State).IsEqualTo(new EveSessionState());
    }

    [Test]
    public async Task CompletedSession_CanPreserveContinuationState(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.completed"}""")));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                PreserveCompletedSessions = true,
            });
        EveSession session = client.CreateSession();

        EveMessageResponse response = await session.SendAsync("Keep it", cancellationToken);
        await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            ContinuationToken = "eve:accepted",
            SessionId = "session_1",
            StreamIndex = 1,
        });
    }

    [Test]
    public async Task CancelAsync_TargetsAcceptedTurnBeforeStreamConsumption(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        EveSession session = CreateClient(transport).CreateSession();

        await session.SendAsync("Wait", cancellationToken);
        EveCancellationOutcome outcome = await session.CancelAsync(
            "turn_1",
            cancellationToken);

        await Assert.That(outcome.SessionId).IsEqualTo("session_1");
        await Assert.That(outcome.Status).IsEqualTo(EveCancellationStatus.Accepted);
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/cancel");
        using JsonDocument body = JsonDocument.Parse(handler.Calls[1].Body!);
        await Assert.That(body.RootElement.GetProperty("turnId").GetString())
            .IsEqualTo("turn_1");
    }

    [Test]
    public async Task TurnStream_ReconnectsFromAdvancedCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"turn.started","data":{"sequence":1,"turnId":"turn_1"}}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"}}""")));
        EveSession session = CreateClient(transport).CreateSession();
        EveSendTurnRequest request = new()
        {
            Message = EveMessageContent.FromText("Reconnect"),
            StreamReconnectPolicy = ZeroDelayReconnectPolicy(),
        };

        EveMessageResponse response = await session.SendAsync(request, cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(2);
        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream");
        await Assert.That(handler.Calls[2].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream?startIndex=1");
    }

    [Test]
    public async Task OversizedTurnEvent_FailsWithoutReconnect(
        CancellationToken cancellationToken)
    {
        const string payloadMarker = "payload-must-not-be-echoed";
        const string oversizedEvent = "{\"type\":\"message.appended\",\"data\":{\"messageDelta\":\""
            + payloadMarker
            + "\"}}";
        int maximumEventBytes = Encoding.UTF8.GetByteCount(oversizedEvent) - 1;
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue((_, _) => Task.FromResult(StreamResponse(oversizedEvent)));
        EveSession session = CreateClient(transport, maximumEventBytes).CreateSession();
        EveMessageResponse response = await session.SendAsync(
            new EveSendTurnRequest
            {
                Message = EveMessageContent.FromText("Bound this event"),
                StreamReconnectPolicy = ZeroDelayReconnectPolicy(),
            },
            cancellationToken);
        EveProtocolException? exception = null;

        try
        {
            await response.GetOutcomeAsync(cancellationToken);
        }
        catch (EveProtocolException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
        string message = exception?.Message ?? string.Empty;
        await Assert.That(message.Contains(
            maximumEventBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(message.Contains(payloadMarker, StringComparison.Ordinal)).IsFalse();
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            ContinuationToken = "eve:accepted",
            SessionId = "session_1",
        });
    }

    [Test]
    public async Task DisabledReconnect_UsesOneStreamConnectionAndPreservesPartialCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"turn.started","data":{"sequence":1,"turnId":"turn_1"}}""")));
        EveSession session = CreateClient(transport).CreateSession();
        EveSendTurnRequest request = new()
        {
            Message = EveMessageContent.FromText("One connection"),
            StreamReconnectPolicy = EveStreamReconnectPolicy.Disabled,
        };

        EveMessageResponse response = await session.SendAsync(request, cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(1);
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            ContinuationToken = "eve:accepted",
            SessionId = "session_1",
            StreamIndex = 1,
        });
    }

    [Test]
    public async Task TailRelativeStream_DoesNotAdvanceAbsoluteCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:latest","wait":"next-user-message"}}""")));
        EveSessionState initialState = new()
        {
            ContinuationToken = "eve:current",
            SessionId = "session_1",
            StreamIndex = 7,
        };
        EveSession session = CreateClient(transport).CreateSession(initialState);
        List<EveStreamEvent> events = [];

        await foreach (EveStreamEvent streamEvent in session.StreamAsync(
            new EveStreamOptions
            {
                StartIndex = -1,
                ReconnectPolicy = EveStreamReconnectPolicy.Disabled,
            },
            cancellationToken))
        {
            events.Add(streamEvent);
        }

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream?startIndex=-1");
        await Assert.That(session.State).IsEqualTo(initialState);
    }

    [Test]
    public async Task InputResponse_RetriesSessionPropagationFailure(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.InternalServerError,
            """{"error":"Target session was not found."}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"}}""")));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                DeliveryRetryDelay = TimeSpan.Zero,
            });
        EveSession session = client.CreateSession(new EveSessionState
        {
            ContinuationToken = "eve:current",
            SessionId = "session_1",
        });
        EveSendTurnRequest request = new()
        {
            InputResponses =
            [
                new EveInputResponse("approval_1", "approve"),
            ],
        };

        EveMessageResponse response = await session.SendAsync(request, cancellationToken);
        await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        await Assert.That(handler.Calls[0].Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Calls[1].Method).IsEqualTo(HttpMethod.Post);
    }

    [Test]
    public async Task ConsumedTurn_AppliesCursorAfterAnotherTurnWasAccepted(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse(
            "session_1",
            "eve:first-accepted")));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse(
            "session_1",
            "eve:second-accepted")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:first-waiting","wait":"next-user-message"}}""")));
        EveSession session = CreateClient(transport).CreateSession();

        EveMessageResponse first = await session.SendAsync("First", cancellationToken);
        await session.SendAsync("Second", cancellationToken);
        await first.GetOutcomeAsync(cancellationToken);

        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            ContinuationToken = "eve:first-waiting",
            SessionId = "session_1",
            StreamIndex = 1,
        });
    }

    [Test]
    public async Task MessageResponse_CanOnlyBeConsumedOnce(CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.completed"}""")));
        EveSession session = CreateClient(transport).CreateSession();
        EveMessageResponse response = await session.SendAsync("Once", cancellationToken);
        await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(async () => await response.GetOutcomeAsync(cancellationToken))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SendAsync_PersistsAcceptedContinuationTokenBeforeStreamConsumption(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        EveSession session = CreateClient(transport).CreateSession();

        await session.SendAsync("Persist", cancellationToken);

        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            ContinuationToken = "eve:accepted",
            SessionId = "session_1",
            StreamIndex = 0,
        });
    }

    [Test]
    public async Task SendAsync_PreservesPriorTokenWhenAcceptedResponseOmitsIt(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1"}""")));
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            ContinuationToken = "eve:existing",
            SessionId = "session_1",
            StreamIndex = 3,
        });

        await session.SendAsync("Keep token", cancellationToken);

        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            ContinuationToken = "eve:existing",
            SessionId = "session_1",
            StreamIndex = 3,
        });
    }

    [Test]
    public async Task ResetAsync_WithoutTokenOrSession_ClearsStateWithoutHttpCall(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSession session = CreateClient(transport).CreateSession();

        EveResetOutcome outcome = await session.ResetAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveResetStatus.NoActiveSession);
        await Assert.That(outcome.PreviousSessionId).IsNull();
        await Assert.That(session.State).IsEqualTo(new EveSessionState());
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ResetAsync_WithSessionButNoToken_Throws(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            SessionId = "session_1",
        });

        await Assert.That(async () => await session.ResetAsync(cancellationToken))
            .Throws<InvalidOperationException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ResetAsync_RetiresSessionAndClearsState(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"status":"reset","previousSessionId":"session_1"}""")));
        EveSession session = CreateClient(transport).CreateSession();

        await session.SendAsync("Retire", cancellationToken);
        EveResetOutcome outcome = await session.ResetAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveResetStatus.Reset);
        await Assert.That(outcome.PreviousSessionId).IsEqualTo("session_1");
        await Assert.That(session.State).IsEqualTo(new EveSessionState());
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/reset");
        await Assert.That(handler.Calls[1].Method).IsEqualTo(HttpMethod.Post);
        using JsonDocument body = JsonDocument.Parse(handler.Calls[1].Body!);
        await Assert.That(body.RootElement.GetProperty("continuationToken").GetString())
            .IsEqualTo("eve:accepted");
    }

    [Test]
    public async Task ResetAsync_AcceptsNoActiveSessionResponse(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"status":"no_active_session"}""")));
        EveSession session = CreateClient(transport).CreateSession("eve:orphaned");

        EveResetOutcome outcome = await session.ResetAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveResetStatus.NoActiveSession);
        await Assert.That(outcome.PreviousSessionId).IsNull();
        await Assert.That(session.State).IsEqualTo(new EveSessionState());
    }

    [Test]
    public async Task ResetAsync_ThrowsClientExceptionForNonSuccessResponse(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.NotFound,
            """{"error":"unknown route"}""")));
        EveSession session = CreateClient(transport).CreateSession("eve:orphaned");

        EveClientException? exception =
            await Assert.That(async () => await session.ResetAsync(cancellationToken))
                .Throws<EveClientException>();

        await Assert.That(exception!.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(exception.ResponseBody).IsEqualTo("""{"error":"unknown route"}""");
        await Assert.That(exception.ResponseHeaders.ContainsKey("content-type")).IsTrue();
        await Assert.That(session.State.ContinuationToken).IsEqualTo("eve:orphaned");
    }

    [Test]
    [Arguments("not json")]
    [Arguments("""{"ok":true,"status":"retired"}""")]
    [Arguments("""{"ok":false,"status":"reset","previousSessionId":"session_1"}""")]
    [Arguments("""{"ok":true,"status":"reset"}""")]
    [Arguments("""{"ok":true,"status":"reset","previousSessionId":""}""")]
    public async Task ResetAsync_RejectsInvalidResponses(
        string responseBody,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            responseBody)));
        EveSession session = CreateClient(transport).CreateSession("eve:orphaned");

        await Assert.That(async () => await session.ResetAsync(cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(session.State.ContinuationToken).IsEqualTo("eve:orphaned");
    }

    [Test]
    public async Task ResetAsync_RejectsMismatchedPreviousSessionId(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"status":"reset","previousSessionId":"session_other"}""")));
        EveSession session = CreateClient(transport).CreateSession();

        await session.SendAsync("Retire", cancellationToken);

        await Assert.That(async () => await session.ResetAsync(cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(session.State.SessionId).IsEqualTo("session_1");
    }

    [Test]
    public async Task ResetAsync_DoesNotOverwriteConcurrentlyAdvancedState(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSession session = CreateClient(transport).CreateSession("eve:orphaned");
        handler.Enqueue(async (_, _) =>
        {
            await session.SendAsync("Concurrent", cancellationToken);
            return JsonResponse(
                HttpStatusCode.OK,
                """{"ok":true,"status":"no_active_session"}""");
        });
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse(
            "session_2",
            "eve:concurrent")));

        EveResetOutcome outcome = await session.ResetAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveResetStatus.NoActiveSession);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            ContinuationToken = "eve:concurrent",
            SessionId = "session_2",
            StreamIndex = 0,
        });
    }

    private static async Task AssertAuthenticationHeaderPlacementAsync(
        IEveAuthentication authentication,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> expectedHeaders =
            await authentication.GetHeadersAsync(cancellationToken);
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Authentication = authentication,
            });

        await client.CreateSession().SendAsync("Authenticate", cancellationToken);

        RecordedHttpCall call = handler.Calls[0];
        IReadOnlyList<string> authorizationValues =
            call.RequestHeaderValues["authorization"];
        await Assert.That(authorizationValues.Count).IsEqualTo(1);
        await Assert.That(authorizationValues[0]).IsEqualTo(expectedHeaders["authorization"]);
        await Assert.That(call.ContentHeaders.ContainsKey("content-type")).IsTrue();
    }

    private static EveClient CreateClient(
        HttpMessageInvoker transport,
        int? maximumEventBytes = null) =>
        new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                MaxStreamEventBytes = maximumEventBytes,
            });

    private static EveStreamReconnectPolicy ZeroDelayReconnectPolicy() =>
        new()
        {
            StreamOpenRetry = new EveRetryPolicy
            {
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
            },
            StreamIdleRetry = new EveRetryPolicy
            {
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero,
            },
        };

    private static HttpResponseMessage AcceptedResponse(
        string sessionId = "session_1",
        string continuationToken = "eve:accepted") =>
        JsonResponse(
            HttpStatusCode.Accepted,
            $$"""{"ok":true,"sessionId":"{{sessionId}}","continuationToken":"{{continuationToken}}"}""");

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage StreamResponse(params string[] events) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{string.Join('\n', events)}\n",
                Encoding.UTF8,
                EveProtocol.MessageStreamContentType),
        };
}
