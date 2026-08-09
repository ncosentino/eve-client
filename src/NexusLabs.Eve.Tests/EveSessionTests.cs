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
        EveTurnOptions options = new()
        {
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

        EveMessageResponse response = await session.SendAsync(
            EveMessageContent.FromText("Run the check."),
            options,
            cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(response.SessionId).IsEqualTo("session_1");
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
    public async Task SendAsync_ContinuesWithSessionIdWithoutContinuationToken(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"wait":"next-user-message"}}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("session_1")));
        EveSession session = CreateClient(transport, 1024).CreateSession();

        EveMessageResponse first = await session.SendAsync("First", cancellationToken);
        await first.GetOutcomeAsync(cancellationToken);
        await session.SendAsync("Second", cancellationToken);

        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        await Assert.That(handler.Calls[2].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1");
        using JsonDocument body = JsonDocument.Parse(handler.Calls[2].Body!);
        await Assert.That(body.RootElement.TryGetProperty("continuationToken", out _))
            .IsFalse()
            .Because("Continuing a session is addressed by URL session id.");
        await Assert.That(body.RootElement.GetProperty("message").GetString()).IsEqualTo("Second");
    }

    [Test]
    public async Task AttachSession_AddressesSessionIdAndStreamsFromCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("session_attached")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.completed"}""")));
        EveSession session = CreateClient(transport).AttachSession("session_attached", 7);

        EveMessageResponse response = await session.SendAsync("Continue", cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(response.SessionId).IsEqualTo("session_attached");
        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Completed);
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_attached");
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_attached/stream?startIndex=7");
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_attached",
            StreamIndex = 8,
        });
    }

    [Test]
    public async Task AttachSession_RejectsInvalidArguments()
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveClient client = CreateClient(transport);

        await Assert.That(() => client.AttachSession(null!)).Throws<ArgumentException>();
        await Assert.That(() => client.AttachSession(string.Empty)).Throws<ArgumentException>();
        await Assert.That(() => client.AttachSession("   ")).Throws<ArgumentException>();
        await Assert.That(() => client.AttachSession("session_1", -1))
            .Throws<ArgumentOutOfRangeException>();
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
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("session_1")));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
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
            EveMessageContent.FromText("First"),
            new EveTurnOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["x-turn"] = "first",
                },
                StreamReconnectPolicy = ZeroDelayReconnectPolicy(),
            },
            cancellationToken);
        await firstResponse.GetOutcomeAsync(cancellationToken);
        await session.SendAsync("Second", cancellationToken);
        await session.CompactAsync(cancellationToken);
        await session.CancelAsync(cancellationToken);
        await session.ClearAsync(cancellationToken);
        await session.ResetAsync(cancellationToken);

        EveRequestKind[] expectedKinds =
        [
            EveRequestKind.Health,
                    EveRequestKind.Info,
                    EveRequestKind.CreateSession,
                    EveRequestKind.StreamSession,
                    EveRequestKind.StreamSession,
                    EveRequestKind.ContinueSession,
                    EveRequestKind.CompactSession,
                    EveRequestKind.CancelTurn,
                    EveRequestKind.ClearSession,
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
    public async Task PerTurnAuthorization_IsProtectedByDefaultOnPostAndStreamReconnect(
        CancellationToken cancellationToken)
    {
        EveBearerAuthentication authentication = new("deployment-token");
        IReadOnlyDictionary<string, string> expectedHeaders =
            await authentication.GetHeadersAsync(cancellationToken);
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
                Authentication = authentication,
            });

        EveMessageResponse response = await client.CreateSession().SendAsync(
            EveMessageContent.FromText("Forward the caller identity"),
            new EveTurnOptions
            {
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
            await Assert.That(authorizationValues[0])
                .IsEqualTo(expectedHeaders["authorization"]);
        }
    }

    [Test]
    public async Task PerTurnAuthorization_OverridesAuthenticationWhenExplicitlyAllowed(
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
                Authentication = new EveBearerAuthentication("deployment-token"),
                AllowedProtectedHeaderOverrides = ["authorization"],
            });

        EveMessageResponse response = await client.CreateSession().SendAsync(
            EveMessageContent.FromText("Forward the caller identity"),
            new EveTurnOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["authorization"] = "Token generic-value",
                },
                ProtectedHeaderOverrides = new Dictionary<string, string>
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
    public async Task VercelOidc_KeepsTrustedIdpTokenWhenProtectedAuthorizationOverrideWins(
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
                AllowedProtectedHeaderOverrides = ["authorization"],
            });

        EveMessageResponse response = await client.CreateSession().SendAsync(
            EveMessageContent.FromText("Forward the caller identity"),
            new EveTurnOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["authorization"] = "Token generic-value",
                },
                ProtectedHeaderOverrides = new Dictionary<string, string>
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
    public async Task BasicAuthentication_IsProtectedFromPerTurnHeadersByDefault(
        CancellationToken cancellationToken)
    {
        EveBasicAuthentication authentication = new("agent-client", "password");
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

        await client.CreateSession().SendAsync(
            EveMessageContent.FromText("Authenticate"),
            new EveTurnOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["authorization"] = PerTurnAuthorization,
                },
            },
            cancellationToken);

        await Assert.That(handler.Calls[0].RequestHeaderValues["authorization"].Count)
            .IsEqualTo(1);
        await Assert.That(handler.Calls[0].Headers["authorization"])
            .IsEqualTo(expectedHeaders["authorization"]);
    }

    [Test]
    public async Task VercelOidc_ProtectsDeclaredHeadersWhenTokenIsEmpty(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Authentication = new EveVercelOidcAuthentication(
                    static _ => ValueTask.FromResult(string.Empty)),
            });

        await client.CreateSession().SendAsync(
            EveMessageContent.FromText("Authenticate"),
            new EveTurnOptions
            {
                Headers = new Dictionary<string, string>
                {
                    ["authorization"] = PerTurnAuthorization,
                    [EveProtocol.VercelTrustedOidcTokenHeaderName] = "generic-token",
                },
            },
            cancellationToken);

        await Assert.That(handler.Calls[0].Headers.ContainsKey("authorization")).IsFalse();
        await Assert.That(handler.Calls[0].Headers.ContainsKey(
            EveProtocol.VercelTrustedOidcTokenHeaderName)).IsFalse();
    }

    [Test]
    public async Task VercelOidc_RejectsTrustedHeaderOverrideWithoutExplicitPolicy(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Authentication = new EveVercelOidcAuthentication("oidc-token"),
                AllowedProtectedHeaderOverrides = ["authorization"],
            });

        await Assert.That(async () => await client.CreateSession().SendAsync(
            EveMessageContent.FromText("Authenticate"),
            new EveTurnOptions
            {
                ProtectedHeaderOverrides = new Dictionary<string, string>
                {
                    ["authorization"] = PerTurnAuthorization,
                    [EveProtocol.VercelTrustedOidcTokenHeaderName] = "override-token",
                },
            },
            cancellationToken)).Throws<InvalidOperationException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ProtectedHeaderOverride_DoesNotLeakAcrossTurns(
        CancellationToken cancellationToken)
    {
        EveBearerAuthentication authentication = new("deployment-token");
        IReadOnlyDictionary<string, string> expectedHeaders =
            await authentication.GetHeadersAsync(cancellationToken);
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"}}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("session_1")));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Authentication = authentication,
                AllowedProtectedHeaderOverrides = ["authorization"],
            });
        EveSession session = client.CreateSession();

        EveMessageResponse firstResponse = await session.SendAsync(
            EveMessageContent.FromText("First"),
            new EveTurnOptions
            {
                ProtectedHeaderOverrides = new Dictionary<string, string>
                {
                    ["authorization"] = PerTurnAuthorization,
                },
            },
            cancellationToken);
        await firstResponse.GetOutcomeAsync(cancellationToken);
        await session.SendAsync("Second", cancellationToken);

        await Assert.That(handler.Calls[0].Headers["authorization"])
            .IsEqualTo(PerTurnAuthorization);
        await Assert.That(handler.Calls[1].Headers["authorization"])
            .IsEqualTo(PerTurnAuthorization);
        await Assert.That(handler.Calls[2].Headers["authorization"])
            .IsEqualTo(expectedHeaders["authorization"]);
    }

    [Test]
    public async Task ProtectedHeaderOverride_DoesNotApplyToCancelAsync(
        CancellationToken cancellationToken)
    {
        EveBearerAuthentication authentication = new("deployment-token");
        IReadOnlyDictionary<string, string> expectedHeaders =
            await authentication.GetHeadersAsync(cancellationToken);
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Authentication = authentication,
                AllowedProtectedHeaderOverrides = ["authorization"],
            });
        EveSession session = client.CreateSession();

        await session.SendAsync(
            EveMessageContent.FromText("Start"),
            new EveTurnOptions
            {
                ProtectedHeaderOverrides = new Dictionary<string, string>
                {
                    ["authorization"] = PerTurnAuthorization,
                },
            },
            cancellationToken);
        await session.CancelAsync(cancellationToken);

        await Assert.That(handler.Calls[0].Headers["authorization"])
            .IsEqualTo(PerTurnAuthorization);
        await Assert.That(handler.Calls[1].Headers["authorization"])
            .IsEqualTo(expectedHeaders["authorization"]);
    }

    [Test]
    public async Task ProtectedHeaderOverride_DoesNotApplyToManualStreamAttachment(
        CancellationToken cancellationToken)
    {
        EveBearerAuthentication authentication = new("deployment-token");
        IReadOnlyDictionary<string, string> expectedHeaders =
            await authentication.GetHeadersAsync(cancellationToken);
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:next","wait":"next-user-message"}}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.completed"}""")));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Authentication = authentication,
                AllowedProtectedHeaderOverrides = ["authorization"],
            });
        EveSession session = client.CreateSession();

        EveMessageResponse response = await session.SendAsync(
            EveMessageContent.FromText("Start"),
            new EveTurnOptions
            {
                ProtectedHeaderOverrides = new Dictionary<string, string>
                {
                    ["authorization"] = PerTurnAuthorization,
                },
            },
            cancellationToken);
        await response.GetOutcomeAsync(cancellationToken);
        await foreach (EveStreamEvent _ in session.StreamAsync(
            new EveStreamOptions
            {
                ReconnectPolicy = EveStreamReconnectPolicy.Disabled,
            },
            cancellationToken))
        {
        }

        await Assert.That(handler.Calls[0].Headers["authorization"])
            .IsEqualTo(PerTurnAuthorization);
        await Assert.That(handler.Calls[1].Headers["authorization"])
            .IsEqualTo(PerTurnAuthorization);
        await Assert.That(handler.Calls[2].Headers["authorization"])
            .IsEqualTo(expectedHeaders["authorization"]);
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
    public async Task CompletedSession_RetainsSessionIdAndCursor(CancellationToken cancellationToken)
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
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 1,
        });
    }

    [Test]
    public async Task FailedSession_RetainsSessionIdAndCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.failed"}""")));
        EveSession session = CreateClient(transport).CreateSession();

        EveMessageResponse response = await session.SendAsync("Keep it", cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Failed);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
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
        EveTurnOptions options = new()
        {
            StreamReconnectPolicy = ZeroDelayReconnectPolicy(),
        };

        EveMessageResponse response = await session.SendAsync(
            EveMessageContent.FromText("Reconnect"),
            options,
            cancellationToken);
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
            EveMessageContent.FromText("Bound this event"),
            new EveTurnOptions
            {
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
        EveTurnOptions options = new()
        {
            StreamReconnectPolicy = EveStreamReconnectPolicy.Disabled,
        };

        EveMessageResponse response = await session.SendAsync(
            EveMessageContent.FromText("One connection"),
            options,
            cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(outcome.Events.Count).IsEqualTo(1);
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
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
    public async Task BoundedStream_StopsAtDurableTailAndAdvancesCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(BoundedStreamResponse(
            "1",
            """{"type":"message.completed","data":{"finishReason":"stop","message":"First.","sequence":1,"stepIndex":0,"turnId":"turn_1"}}""",
            """{"type":"message.completed","data":{"finishReason":"stop","message":"Second.","sequence":2,"stepIndex":0,"turnId":"turn_1"}}""",
            """{"type":"message.completed","data":{"finishReason":"stop","message":"Beyond.","sequence":3,"stepIndex":0,"turnId":"turn_1"}}""")));
        EveSessionState initialState = new()
        {
            SessionId = "session_1",
        };
        EveSession session = CreateClient(transport).CreateSession(initialState);
        List<EveStreamEvent> events = [];

        await foreach (EveStreamEvent streamEvent in session.StreamAsync(
            new EveStreamOptions
            {
                Follow = false,
            },
            cancellationToken))
        {
            events.Add(streamEvent);
        }

        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream?includeTailIndex=1");
        await Assert.That(session.State).IsEqualTo(initialState with
        {
            StreamIndex = 2,
        });
    }

    [Test]
    public async Task BoundedStream_KeepsFirstTailAcrossReconnects(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(BoundedStreamResponse(
            "2",
            """{"type":"message.completed","data":{"finishReason":"stop","message":"First.","sequence":1,"stepIndex":0,"turnId":"turn_1"}}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"message.completed","data":{"finishReason":"stop","message":"Second.","sequence":2,"stepIndex":0,"turnId":"turn_1"}}""",
            """{"type":"message.completed","data":{"finishReason":"stop","message":"Third.","sequence":3,"stepIndex":0,"turnId":"turn_1"}}""")));
        EveSessionState initialState = new()
        {
            SessionId = "session_1",
        };
        EveSession session = CreateClient(transport).CreateSession(initialState);
        List<EveStreamEvent> events = [];

        await foreach (EveStreamEvent streamEvent in session.StreamAsync(
            new EveStreamOptions
            {
                Follow = false,
                ReconnectPolicy = ZeroDelayReconnectPolicy(),
            },
            cancellationToken))
        {
            events.Add(streamEvent);
        }

        await Assert.That(events.Count).IsEqualTo(3);
        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream?includeTailIndex=1");
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream?startIndex=1");
        await Assert.That(session.State).IsEqualTo(initialState with
        {
            StreamIndex = 3,
        });
    }

    [Test]
    public async Task BoundedStream_ReturnsImmediatelyWhenCursorIsPastTail(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(BoundedStreamResponse(
            "3",
            """{"type":"message.completed","data":{"finishReason":"stop","message":"Stale.","sequence":1,"stepIndex":0,"turnId":"turn_1"}}""")));
        EveSessionState initialState = new()
        {
            SessionId = "session_1",
            StreamIndex = 5,
        };
        EveSession session = CreateClient(transport).CreateSession(initialState);
        List<EveStreamEvent> events = [];

        await foreach (EveStreamEvent streamEvent in session.StreamAsync(
            new EveStreamOptions
            {
                Follow = false,
            },
            cancellationToken))
        {
            events.Add(streamEvent);
        }

        await Assert.That(events.Count).IsEqualTo(0);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream?startIndex=5&includeTailIndex=1");
        await Assert.That(session.State).IsEqualTo(initialState);
    }

    [Test]
    public async Task BoundedStream_ReturnsImmediatelyForAnEmptyDurableStream(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(BoundedStreamResponse("-1")));
        EveSessionState initialState = new()
        {
            SessionId = "session_1",
        };
        EveSession session = CreateClient(transport).CreateSession(initialState);
        List<EveStreamEvent> events = [];

        await foreach (EveStreamEvent streamEvent in session.StreamAsync(
            new EveStreamOptions
            {
                Follow = false,
            },
            cancellationToken))
        {
            events.Add(streamEvent);
        }

        await Assert.That(events.Count).IsEqualTo(0);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(session.State).IsEqualTo(initialState);
    }

    [Test]
    public async Task BoundedStream_RejectsMissingTailIndexHeader(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:latest","wait":"next-user-message"}}""")));
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            SessionId = "session_1",
        });

        await Assert.That(async () => await CollectAsync(session.StreamAsync(
            new EveStreamOptions
            {
                Follow = false,
            },
            cancellationToken),
            cancellationToken)).Throws<EveProtocolException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments("")]
    [Arguments("latest")]
    [Arguments("1.5")]
    [Arguments(" 4")]
    [Arguments("9007199254740991")]
    public async Task BoundedStream_RejectsInvalidTailIndexHeader(
        string tailIndex,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(BoundedStreamResponse(
            tailIndex,
            """{"type":"session.waiting","data":{"continuationToken":"eve:latest","wait":"next-user-message"}}""")));
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            SessionId = "session_1",
        });

        await Assert.That(async () => await CollectAsync(session.StreamAsync(
            new EveStreamOptions
            {
                Follow = false,
            },
            cancellationToken),
            cancellationToken)).Throws<EveProtocolException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task BoundedStream_RejectsNegativeStartCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            SessionId = "session_1",
        });

        await Assert.That(() => session.StreamAsync(
            new EveStreamOptions
            {
                Follow = false,
                StartIndex = -1,
            },
            cancellationToken)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task LiveStream_DoesNotRequestTheDurableTailIndex(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:latest","wait":"next-user-message"}}""")));
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            SessionId = "session_1",
        });
        List<EveStreamEvent> events = [];

        await foreach (EveStreamEvent streamEvent in session.StreamAsync(
            new EveStreamOptions
            {
                ReconnectPolicy = EveStreamReconnectPolicy.Disabled,
            },
            cancellationToken))
        {
            events.Add(streamEvent);
        }

        await Assert.That(events.Count).IsEqualTo(1);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/stream");
    }

    [Test]
    public async Task RespondAsync_RejectsEmptyInputResponses(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSession session = CreateClient(transport).AttachSession("session_1");

        await Assert.That(async () =>
                await session.RespondAsync([], cancellationToken))
            .Throws<ArgumentException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RespondAsync_RejectedOnUnstartedSession(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSession session = CreateClient(transport).CreateSession();

        await Assert.That(async () => await session.RespondAsync(
                [new EveInputResponse("approval_1", "approve")],
                cancellationToken))
            .Throws<InvalidOperationException>()
            .Because("A session with no id has no pending input.");
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RespondAsync_SendsInputResponsesWithoutMessageProperty(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"wait":"next-user-message"}}""")));
        EveSession session = CreateClient(transport).AttachSession("session_1");

        EveMessageResponse response = await session.RespondAsync(
            [new EveInputResponse("approval_1", "approve")],
            cancellationToken);
        await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1");
        using JsonDocument sentBody = JsonDocument.Parse(handler.Calls[0].Body!);
        await Assert.That(sentBody.RootElement.TryGetProperty("message", out _))
            .IsFalse()
            .Because("eve 0.31.0 rejects a body carrying both payloads.");
        await Assert.That(sentBody.RootElement.GetProperty("inputResponses")
            .GetArrayLength())
            .IsEqualTo(1);
        await Assert.That(sentBody.RootElement.GetProperty("inputResponses")[0]
            .GetProperty("requestId")
            .GetString())
            .IsEqualTo("approval_1");
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
            SessionId = "session_1",
        });
        EveMessageResponse response = await session.RespondAsync(
            [new EveInputResponse("approval_1", "approve")],
            cancellationToken);
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
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("session_1")));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("session_1")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"continuationToken":"eve:first-waiting","wait":"next-user-message"}}""")));
        EveSession session = CreateClient(transport).CreateSession();

        EveMessageResponse first = await session.SendAsync("First", cancellationToken);
        await session.SendAsync("Second", cancellationToken);
        await first.GetOutcomeAsync(cancellationToken);

        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
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
    public async Task SendAsync_PersistsAcceptedSessionIdBeforeStreamConsumption(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        EveSession session = CreateClient(transport).CreateSession();

        await session.SendAsync("Persist", cancellationToken);

        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 0,
        });
    }

    [Test]
    public async Task SendAsync_PreservesPriorCursorWhenAcceptedResponseOmitsSessionId(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1"}""")));
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 3,
        });

        await session.SendAsync("Keep token", cancellationToken);

        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 3,
        });
    }

    [Test]
    public async Task ClearAsync_WithoutSessionId_IsNoOpWithoutHttpCall(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSession session = CreateClient(transport).CreateSession();

        EveClearOutcome outcome = await session.ClearAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveClearStatus.NoActiveSession);
        await Assert.That(outcome.SessionId).IsNull();
        await Assert.That(session.State).IsEqualTo(new EveSessionState());
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ClearAsync_WithSessionId_UsesIdAddressedRouteWithEmptyBody(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            SessionId = "session_1",
        });

        EveClearOutcome outcome = await session.ClearAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveClearStatus.Accepted);
        await Assert.That(outcome.SessionId).IsEqualTo("session_1");
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/clear");
        await Assert.That(handler.Calls[0].Body).IsEqualTo(string.Empty);
        await Assert.That(handler.Calls[0].ContentHeaders["content-type"])
            .IsEqualTo("application/json");
    }

    [Test]
    public async Task ClearAsync_QueuesClearWithoutChangingLocalState(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        EveSessionState initialState = new()
        {
            SessionId = "session_1",
            StreamIndex = 4,
        };
        EveSession session = CreateClient(transport).CreateSession(initialState);

        EveClearOutcome outcome = await session.ClearAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveClearStatus.Accepted);
        await Assert.That(outcome.SessionId).IsEqualTo("session_1");
        await Assert.That(session.State).IsEqualTo(initialState);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/clear");
        await Assert.That(handler.Calls[0].Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Calls[0].Body).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ClearAsync_AcceptsNoActiveSessionResponse(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"status":"no_active_session"}""")));
        EveSessionState initialState = new()
        {
            SessionId = "session_1",
            StreamIndex = 2,
        };
        EveSession session = CreateClient(transport).CreateSession(initialState);

        EveClearOutcome outcome = await session.ClearAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveClearStatus.NoActiveSession);
        await Assert.That(outcome.SessionId).IsNull();
        await Assert.That(session.State).IsEqualTo(initialState);
    }

    [Test]
    public async Task ClearAsync_ThrowsClientExceptionForNonSuccessResponse(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.NotFound,
            """{"error":"unknown route"}""")));
        EveSession session = CreateClient(transport).AttachSession("session_1");

        EveClientException? exception =
            await Assert.That(async () => await session.ClearAsync(cancellationToken))
                .Throws<EveClientException>();

        await Assert.That(exception!.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(exception.ResponseBody).IsEqualTo("""{"error":"unknown route"}""");
        await Assert.That(exception.ResponseHeaders.ContainsKey("content-type")).IsTrue();
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
        });
    }

    [Test]
    [Arguments("not json")]
    [Arguments("""{"ok":true,"status":"cleared"}""")]
    [Arguments("""{"ok":false,"status":"accepted","sessionId":"session_1"}""")]
    [Arguments("""{"ok":true,"status":"accepted"}""")]
    [Arguments("""{"ok":true,"status":"accepted","sessionId":""}""")]
    public async Task ClearAsync_RejectsInvalidResponses(
        string responseBody,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            responseBody)));
        EveSession session = CreateClient(transport).AttachSession("session_1");

        await Assert.That(async () => await session.ClearAsync(cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
        });
    }

    [Test]
    public async Task ClearAsync_RejectsMismatchedSessionId(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_other","status":"accepted"}""")));
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 1,
        });

        await Assert.That(async () => await session.ClearAsync(cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(session.State.SessionId).IsEqualTo("session_1");
        await Assert.That(session.State.StreamIndex).IsEqualTo(1);
    }

    [Test]
    public async Task ClearAsync_PreservesAttachedStateWhenAccepted(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        EveSessionState initialState = new()
        {
            SessionId = "session_1",
            StreamIndex = 0,
        };
        EveSession session = CreateClient(transport).CreateSession(initialState);

        EveClearOutcome outcome = await session.ClearAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveClearStatus.Accepted);
        await Assert.That(outcome.SessionId).IsEqualTo("session_1");
        await Assert.That(session.State).IsEqualTo(initialState);
    }

    [Test]
    public async Task ClearAsync_PropagatesCancellation(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        using CancellationTokenSource clearCancellation = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken);
        handler.Enqueue(async (_, requestCancellationToken) =>
        {
            await clearCancellation.CancelAsync();
            requestCancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(Timeout.InfiniteTimeSpan, requestCancellationToken);
            return JsonResponse(
                HttpStatusCode.Accepted,
                """{"ok":true,"sessionId":"session_1","status":"accepted"}""");
        });
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            SessionId = "session_1",
        });

        await Assert.That(async () => await session.ClearAsync(clearCancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
        });
    }

    [Test]
    public async Task StreamAsync_ProjectsContextClearedAndAdvancesWaitingCursor(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"context.cleared","data":{"sequence":5,"sessionId":"session_1","turnId":"turn_clear"},"meta":{"at":"2026-08-02T12:00:00.000Z","id":"evt_clear_1"}}""",
            """{"type":"session.waiting","data":{"continuationToken":"eve:after-clear","wait":"next-user-message"}}""")));
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 4,
        });

        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent streamEvent in session
            .StreamAsync(cancellationToken)
            .WithCancellation(cancellationToken))
        {
            events.Add(streamEvent);
            if (streamEvent.IsCurrentTurnBoundary)
            {
                break;
            }
        }

        await Assert.That(events.Count).IsEqualTo(2);
        await Assert.That(events[0].Kind).IsEqualTo(EveStreamEventKind.ContextCleared);
        await Assert.That(events[0].Type).IsEqualTo("context.cleared");
        await Assert.That(events[0].Data.GetProperty("turnId").GetString())
            .IsEqualTo("turn_clear");
        await Assert.That(events[0].Metadata!.Id).IsEqualTo("evt_clear_1");
        await Assert.That(events[1].Kind).IsEqualTo(EveStreamEventKind.SessionWaiting);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 6,
        });
    }

    [Test]
    public async Task ResetAsync_WithoutSessionId_IsNoOpWithoutHttpCall(
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
    public async Task ResetAsync_WithSessionId_UsesIdAddressedRouteWithEmptyBody(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"status":"reset","previousSessionId":"session_1"}""")));
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            SessionId = "session_1",
        });

        EveResetOutcome outcome = await session.ResetAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveResetStatus.Reset);
        await Assert.That(outcome.PreviousSessionId).IsEqualTo("session_1");
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/reset");
        await Assert.That(handler.Calls[0].Body).IsEqualTo(string.Empty);
        await Assert.That(handler.Calls[0].ContentHeaders["content-type"])
            .IsEqualTo("application/json");
    }

    [Test]
    public async Task ResetAsync_RetainsSessionState(
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
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
        });
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/reset");
        await Assert.That(handler.Calls[1].Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Calls[1].Body).IsEqualTo(string.Empty);
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
        EveSession session = CreateClient(transport).AttachSession("session_1");

        EveResetOutcome outcome = await session.ResetAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveResetStatus.NoActiveSession);
        await Assert.That(outcome.PreviousSessionId).IsNull();
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
        });
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
        EveSession session = CreateClient(transport).AttachSession("session_1");

        EveClientException? exception =
            await Assert.That(async () => await session.ResetAsync(cancellationToken))
                .Throws<EveClientException>();

        await Assert.That(exception!.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(exception.ResponseBody).IsEqualTo("""{"error":"unknown route"}""");
        await Assert.That(exception.ResponseHeaders.ContainsKey("content-type")).IsTrue();
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
        });
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
        EveSession session = CreateClient(transport).AttachSession("session_1");

        await Assert.That(async () => await session.ResetAsync(cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
        });
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
        EveSession session = CreateClient(transport).AttachSession("session_1");
        handler.Enqueue(async (_, _) =>
        {
            await session.SendAsync("Concurrent", cancellationToken);
            return JsonResponse(
                HttpStatusCode.OK,
                """{"ok":true,"status":"no_active_session"}""");
        });
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse("session_2")));

        EveResetOutcome outcome = await session.ResetAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveResetStatus.NoActiveSession);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_2",
            StreamIndex = 0,
        });
    }

    [Test]
    public async Task CompactAsync_WithoutSessionId_IsNoOpWithoutHttpCall(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSession session = CreateClient(transport).CreateSession();

        EveCompactOutcome outcome = await session.CompactAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveCompactStatus.NoActiveSession);
        await Assert.That(outcome.SessionId).IsNull();
        await Assert.That(session.State).IsEqualTo(new EveSessionState());
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CompactAsync_WithSessionId_UsesIdAddressedRouteWithEmptyBody(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        EveSession session = CreateClient(transport).CreateSession(new EveSessionState
        {
            SessionId = "session_1",
        });

        EveCompactOutcome outcome = await session.CompactAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveCompactStatus.Accepted);
        await Assert.That(outcome.SessionId).IsEqualTo("session_1");
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/compact");
        await Assert.That(handler.Calls[0].Body).IsEqualTo(string.Empty);
        await Assert.That(handler.Calls[0].ContentHeaders["content-type"])
            .IsEqualTo("application/json");
    }

    [Test]
    public async Task CompactAsync_QueuesCompactionWithoutClearingState(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        EveSession session = CreateClient(transport).CreateSession();
        EveSessionState expectedState = new()
        {
            SessionId = "session_1",
            StreamIndex = 0,
        };

        await session.SendAsync("Compact", cancellationToken);
        EveCompactOutcome outcome = await session.CompactAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveCompactStatus.Accepted);
        await Assert.That(outcome.SessionId).IsEqualTo("session_1");
        await Assert.That(session.State).IsEqualTo(expectedState);
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1/compact");
        await Assert.That(handler.Calls[1].Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Calls[1].Body).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task CompactAsync_AcceptsNoActiveSessionResponse(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSessionState initialState = new()
        {
            SessionId = "session_1",
            StreamIndex = 4,
        };
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"status":"no_active_session"}""")));
        EveSession session = CreateClient(transport).CreateSession(initialState);

        EveCompactOutcome outcome = await session.CompactAsync(cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveCompactStatus.NoActiveSession);
        await Assert.That(outcome.SessionId).IsNull();
        await Assert.That(session.State).IsEqualTo(initialState);
    }

    [Test]
    public async Task CompactAsync_ThrowsClientExceptionForNonSuccessResponse(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSessionState initialState = new()
        {
            SessionId = "session_1",
            StreamIndex = 2,
        };
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.NotFound,
            """{"error":"unknown route"}""")));
        EveSession session = CreateClient(transport).CreateSession(initialState);

        EveClientException? exception =
            await Assert.That(async () => await session.CompactAsync(cancellationToken))
                .Throws<EveClientException>();

        await Assert.That(exception!.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(exception.ResponseBody).IsEqualTo("""{"error":"unknown route"}""");
        await Assert.That(exception.ResponseHeaders.ContainsKey("content-type")).IsTrue();
        await Assert.That(session.State).IsEqualTo(initialState);
    }

    [Test]
    [Arguments("not json")]
    [Arguments("""{"ok":true,"status":"retired"}""")]
    [Arguments("""{"ok":false,"status":"accepted","sessionId":"session_1"}""")]
    [Arguments("""{"ok":true,"status":"accepted"}""")]
    [Arguments("""{"ok":true,"status":"accepted","sessionId":""}""")]
    public async Task CompactAsync_RejectsInvalidResponses(
        string responseBody,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveSessionState initialState = new()
        {
            SessionId = "session_1",
        };
        handler.Enqueue((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            responseBody)));
        EveSession session = CreateClient(transport).CreateSession(initialState);

        await Assert.That(async () => await session.CompactAsync(cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(session.State).IsEqualTo(initialState);
    }

    [Test]
    public async Task CompactAsync_RejectsMismatchedSessionId(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_other","status":"accepted"}""")));
        EveSession session = CreateClient(transport).CreateSession();

        await session.SendAsync("Compact", cancellationToken);

        await Assert.That(async () => await session.CompactAsync(cancellationToken))
            .Throws<EveProtocolException>();
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 0,
        });
    }

    [Test]
    public async Task CompactAsync_PropagatesCancellation(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        handler.Enqueue(async (_, requestCancellationToken) =>
        {
            await cts.CancelAsync();
            requestCancellationToken.ThrowIfCancellationRequested();
            return JsonResponse(
                HttpStatusCode.Accepted,
                """{"ok":true,"sessionId":"session_1","status":"accepted"}""");
        });
        EveSessionState initialState = new()
        {
            SessionId = "session_1",
            StreamIndex = 7,
        };
        EveSession session = CreateClient(transport).CreateSession(initialState);

        await Assert.That(async () => await session.CompactAsync(cts.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(session.State).IsEqualTo(initialState);
    }

    [Test]
    public async Task CompactAsync_AllowsConsumingCompactionStreamEvents(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1","status":"accepted"}""")));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"compaction.requested","data":{"sequence":1}}""",
            """{"type":"compaction.completed","data":{"sequence":2}}""",
            """{"type":"session.waiting","data":{"continuationToken":"eve:after-compact","wait":"next-user-message"}}""")));
        EveSession session = CreateClient(transport).CreateSession();

        await session.SendAsync("Compact stream", cancellationToken);
        EveCompactOutcome outcome = await session.CompactAsync(cancellationToken);
        IReadOnlyList<EveStreamEvent> events = await CollectAsync(
                    session.StreamAsync(
                        new EveStreamOptions
                        {
                            ReconnectPolicy = EveStreamReconnectPolicy.Disabled,
                        },
                        cancellationToken),
                    cancellationToken);

        await Assert.That(outcome.Status).IsEqualTo(EveCompactStatus.Accepted);
        await Assert.That(events.Count).IsEqualTo(3);
        await Assert.That(events[0].Kind).IsEqualTo(EveStreamEventKind.CompactionRequested);
        await Assert.That(events[1].Kind).IsEqualTo(EveStreamEventKind.CompactionCompleted);
        await Assert.That(events[2].Kind).IsEqualTo(EveStreamEventKind.SessionWaiting);
        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 3,
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
        string sessionId = "session_1") =>
        JsonResponse(
            HttpStatusCode.Accepted,
            $$"""{"ok":true,"sessionId":"{{sessionId}}"}""");

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

    private static HttpResponseMessage BoundedStreamResponse(
        string tailIndex,
        params string[] events)
    {
        HttpResponseMessage response = StreamResponse(events);
        response.Headers.TryAddWithoutValidation("x-eve-stream-tail-index", tailIndex);
        return response;
    }

    private static async Task<IReadOnlyList<EveStreamEvent>> CollectAsync(
        IAsyncEnumerable<EveStreamEvent> stream,
        CancellationToken cancellationToken)
    {
        List<EveStreamEvent> events = [];
        await foreach (EveStreamEvent streamEvent in stream.WithCancellation(cancellationToken))
        {
            events.Add(streamEvent);
        }

        return events;
    }
}
