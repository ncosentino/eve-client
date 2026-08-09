using System.Collections.ObjectModel;
using System.Net;
using System.Text;

namespace NexusLabs.Eve.Tests;

public sealed class EveClientTests
{
    [Test]
    public async Task GetHealthAsync_AppliesQueryHeadersAndAuthentication(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"status":"ready","workflowId":"workflow_1"}""")));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com/proxy?bypass=secret")
            {
                Headers = new Dictionary<string, string>
                {
                    ["x-client"] = "static",
                },
                HeadersProvider = _ => ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                    new Dictionary<string, string>
                    {
                        ["x-client"] = "dynamic",
                    }),
                Authentication = new EveBearerAuthentication("access-token"),
            });

        EveHealthStatus health = await client.GetHealthAsync(cancellationToken);

        await Assert.That(health.Ok).IsTrue().Because("The health route returned ok=true.");
        await Assert.That(health.Status).IsEqualTo("ready");
        await Assert.That(health.WorkflowId).IsEqualTo("workflow_1");
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/proxy/eve/v1/health?bypass=secret");
        await Assert.That(handler.Calls[0].Headers["x-client"]).IsEqualTo("dynamic");
        await Assert.That(handler.Calls[0].Headers["authorization"])
            .IsEqualTo("Bearer access-token");
    }

    [Test]
    public async Task GetInfoAsync_ReturnsProjectedAndRawAgentInfo(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """
            {
              "kind": "eve-agent-info",
              "version": 1,
              "mode": "development",
              "agent": {
                "name": "Weather Agent",
                "description": "Answers weather questions.",
                "model": { "id": "openai/gpt-5.5" }
              },
              "capabilities": { "devRoutes": true },
              "extra": "preserved"
            }
            """)));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        EveAgentInfo info = await client.GetInfoAsync(cancellationToken);

        await Assert.That(info.AgentName).IsEqualTo("Weather Agent");
        await Assert.That(info.ModelId).IsEqualTo("openai/gpt-5.5");
        await Assert.That(info.Description).IsEqualTo("Answers weather questions.");
        await Assert.That(info.DevelopmentRoutesAvailable)
            .IsTrue()
            .Because("The agent advertises dev routes.");
        await Assert.That(info.Raw.GetProperty("extra").GetString()).IsEqualTo("preserved");
    }

    [Test]
    public async Task GetInfoAsync_RejectsNonEvePayload(CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"kind":"not-eve","version":1}""")));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        await Assert.That(async () => await client.GetInfoAsync(cancellationToken))
            .Throws<EveProtocolException>();
    }

    [Test]
    public async Task SendRawAsync_ProtectsAuthenticationFromGenericHeadersByDefault(
        CancellationToken cancellationToken)
    {
        const string rawAuthorization = "Token raw-override";
        EveBearerAuthentication authentication = new("fresh");
        IReadOnlyDictionary<string, string> expectedHeaders =
            await authentication.GetHeadersAsync(cancellationToken);
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.NoContent)));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Headers = new Dictionary<string, string>
                {
                    ["x-scope"] = "client",
                    ["authorization"] = "client-static",
                },
                RequestHeadersProvider = static (_, _) =>
                    ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                        new Dictionary<string, string>
                        {
                            ["authorization"] = "request-aware",
                        }),
                Authentication = authentication,
            });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/custom", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("x-scope", "request");
        request.Headers.TryAddWithoutValidation("Authorization", rawAuthorization);

        using HttpResponseMessage response = await client.SendRawAsync(
            request,
            cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(handler.Calls[0].Headers["x-scope"]).IsEqualTo("request");
        await Assert.That(handler.Calls[0].RequestHeaderValues["authorization"].Count)
            .IsEqualTo(1);
        await Assert.That(handler.Calls[0].Headers["authorization"])
            .IsEqualTo(expectedHeaders["authorization"]);
    }

    [Test]
    public async Task SendRawAsync_AllowsExplicitProtectedHeaderOverride(
        CancellationToken cancellationToken)
    {
        const string rawAuthorization = "Token raw-override";
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.NoContent)));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Authentication = new EveBearerAuthentication("fresh"),
                AllowedProtectedHeaderOverrides = ["authorization"],
            });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/custom", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("authorization", "Token generic-value");

        using HttpResponseMessage response = await client.SendRawAsync(
            request,
            new EveRawRequestOptions
            {
                ProtectedHeaderOverrides = new Dictionary<string, string>
                {
                    ["AUTHORIZATION"] = rawAuthorization,
                },
            },
            cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(handler.Calls[0].RequestHeaderValues["authorization"].Count)
            .IsEqualTo(1);
        await Assert.That(handler.Calls[0].Headers["authorization"]).IsEqualTo(rawAuthorization);
    }

    [Test]
    public async Task SendRawAsync_ContentHeadersCannotOverrideAuthentication(
        CancellationToken cancellationToken)
    {
        EveBearerAuthentication authentication = new("fresh");
        IReadOnlyDictionary<string, string> expectedHeaders =
            await authentication.GetHeadersAsync(cancellationToken);
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.NoContent)));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Authentication = authentication,
                AllowedProtectedHeaderOverrides = ["authorization"],
            });
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri("/custom", UriKind.Relative))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Content.Headers.TryAddWithoutValidation(
            "authorization",
            "Token content-value");

        using HttpResponseMessage response = await client.SendRawAsync(
            request,
            cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(handler.Calls[0].RequestHeaderValues["authorization"].Count)
            .IsEqualTo(1);
        await Assert.That(handler.Calls[0].Headers["authorization"])
            .IsEqualTo(expectedHeaders["authorization"]);
        await Assert.That(handler.Calls[0].ContentHeaders.ContainsKey("authorization")).IsFalse();
    }

    [Test]
    public async Task SendRawAsync_ProtectsDeclaredHeadersWhenAuthenticationEmitsNothing(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.NoContent)));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Authentication = new EveBearerAuthentication(
                    static _ => ValueTask.FromResult(string.Empty)),
            });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/custom", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("authorization", "Token generic-value");

        using HttpResponseMessage response = await client.SendRawAsync(
            request,
            cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(handler.Calls[0].Headers.ContainsKey("authorization")).IsFalse();
    }

    [Test]
    public async Task SendRawAsync_ProtectsConfiguredClientHeaderNames(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.NoContent)));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                RequestHeadersProvider = static (_, _) =>
                    ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                        new Dictionary<string, string>
                        {
                            ["x-session-bootstrap"] = "client-credential",
                        }),
                ProtectedHeaderNames = ["x-session-bootstrap"],
            });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/custom", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("x-session-bootstrap", "generic-value");

        using HttpResponseMessage response = await client.SendRawAsync(
            request,
            cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(handler.Calls[0].Headers["x-session-bootstrap"])
            .IsEqualTo("client-credential");
    }

    [Test]
    public async Task SendRawAsync_RejectsOverrideForUnprotectedHeader(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                AllowedProtectedHeaderOverrides = ["x-client"],
            });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/custom", UriKind.Relative));

        await Assert.That(async () => await client.SendRawAsync(
            request,
            new EveRawRequestOptions
            {
                ProtectedHeaderOverrides = new Dictionary<string, string>
                {
                    ["x-client"] = "override",
                },
            },
            cancellationToken)).Throws<InvalidOperationException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task SendRawAsync_KeepsAuthenticationAboveClientLevelHeaders(
        CancellationToken cancellationToken)
    {
        EveBearerAuthentication authentication = new("fresh");
        IReadOnlyDictionary<string, string> expectedHeaders =
            await authentication.GetHeadersAsync(cancellationToken);
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.NoContent)));
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
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/custom", UriKind.Relative));

        using HttpResponseMessage response = await client.SendRawAsync(
            request,
            cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(handler.Calls[0].RequestHeaderValues["authorization"].Count)
            .IsEqualTo(1);
        await Assert.That(handler.Calls[0].Headers["authorization"])
            .IsEqualTo(expectedHeaders["authorization"]);
    }

    [Test]
    public async Task RequestHeadersProvider_ReceivesRawKindAndPreservesPrecedence(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.NoContent)));
        EveHttpRequestContext? observedContext = null;
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                HeadersProvider = _ =>
                    ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                        new Dictionary<string, string>
                        {
                            ["x-layer"] = "legacy",
                            ["x-provider-layer"] = "legacy",
                        }),
                RequestHeadersProvider = (context, _) =>
                {
                    observedContext = context;
                    return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                        new Dictionary<string, string>
                        {
                            ["x-layer"] = "request-aware",
                            ["x-provider-layer"] = "request-aware",
                        });
                },
            });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/custom", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("x-layer", "request");

        using HttpResponseMessage response = await client.SendRawAsync(
            request,
            cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(handler.Calls[0].Headers["x-layer"]).IsEqualTo("request");
        await Assert.That(handler.Calls[0].Headers["x-provider-layer"])
            .IsEqualTo("request-aware");
        await Assert.That(observedContext?.Kind).IsEqualTo(EveRequestKind.Raw);
    }

    [Test]
    public async Task SendRawAsync_PreservesContentHeaderPrecedenceAndNormalizesExtensions(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.NoContent)));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                Headers = new Dictionary<string, string>
                {
                    ["content-language"] = "en-US",
                    ["x-client"] = "client",
                },
            });
        using HttpRequestMessage request = new(
            HttpMethod.Post,
            new Uri("/custom", UriKind.Relative))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Content.Headers.ContentLanguage.Add("fr-CA");
        request.Content.Headers.TryAddWithoutValidation("x-client", "raw-content");

        using HttpResponseMessage response = await client.SendRawAsync(
            request,
            cancellationToken);

        RecordedHttpCall call = handler.Calls[0];
        await Assert.That(call.ContentHeaders["content-language"]).IsEqualTo("fr-CA");
        await Assert.That(call.RequestHeaders["x-client"]).IsEqualTo("raw-content");
        await Assert.That(call.ContentHeaders.ContainsKey("x-client")).IsFalse();
    }

    [Test]
    public async Task RequestHeadersProvider_IsIndependentAcrossConcurrentClients(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler firstHandler = new();
        using RecordingHttpMessageHandler secondHandler = new();
        using HttpMessageInvoker firstTransport = new(firstHandler, false);
        using HttpMessageInvoker secondTransport = new(secondHandler, false);
        firstHandler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_1"}""")));
        secondHandler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Accepted,
            """{"ok":true,"sessionId":"session_2"}""")));
        EveClient firstClient = CreateBootstrapClient(firstTransport, "bootstrap_1");
        EveClient secondClient = CreateBootstrapClient(secondTransport, "bootstrap_2");

        Task<EveMessageResponse> firstSend = firstClient
            .CreateSession()
            .SendAsync("First", cancellationToken);
        Task<EveMessageResponse> secondSend = secondClient
            .CreateSession()
            .SendAsync("Second", cancellationToken);
        await Task.WhenAll(firstSend, secondSend);

        await Assert.That(firstHandler.Calls[0].Headers["x-session-bootstrap"])
            .IsEqualTo("bootstrap_1");
        await Assert.That(secondHandler.Calls[0].Headers["x-session-bootstrap"])
            .IsEqualTo("bootstrap_2");
    }
    [Test]
    public async Task Constructor_RejectsNonPositiveStreamEventLimit()
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);

        await Assert.That(() => new EveClient(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                MaxStreamEventBytes = 0,
            })).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task EveClientException_UsesStructuredErrorAndPreservesHeaders()
    {
        EveClientException exception = new(
            HttpStatusCode.Unauthorized,
            """{"error":"Credentials are invalid."}""",
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["www-authenticate"] = new[] { "Bearer" },
            });

        await Assert.That(exception.Message).IsEqualTo("Credentials are invalid.");
        await Assert.That(exception.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(exception.ResponseBody).IsEqualTo(
            """{"error":"Credentials are invalid."}""");
        await Assert.That(exception.ResponseHeaders["www-authenticate"][0]).IsEqualTo("Bearer");
        await Assert.That(exception.ErrorCode)
            .IsNull()
            .Because("The response carried no code property.");
    }

    [Test]
    public async Task EveClientException_ProjectsStableErrorCode()
    {
        EveClientException exception = new(
            HttpStatusCode.Conflict,
            """{"code":"session_not_active","error":"The session is not active.","ok":false}""",
            ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty);

        await Assert.That(exception.ErrorCode).IsEqualTo("session_not_active");
        await Assert.That(exception.Message).IsEqualTo("The session is not active.");
        await Assert.That(exception.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task EveClientException_PreservesUnmodelledErrorCode()
    {
        EveClientException exception = new(
            HttpStatusCode.BadRequest,
            """{"code":"some_future_code","error":"Nope."}""",
            ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty);

        await Assert.That(exception.ErrorCode)
            .IsEqualTo("some_future_code")
            .Because("An unmodelled code stays observable as its raw value.");
    }

    [Test]
    public async Task EveClientException_IgnoresNonStringErrorCode()
    {
        EveClientException exception = new(
            HttpStatusCode.BadRequest,
            """{"code":42,"error":"Nope."}""",
            ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty);

        await Assert.That(exception.ErrorCode).IsNull();
        await Assert.That(exception.ResponseBody)
            .IsEqualTo("""{"code":42,"error":"Nope."}""")
            .Because("The raw body is never discarded.");
    }

    [Test]
    public async Task EveClientException_HandlesNonJsonBody()
    {
        EveClientException exception = new(
            HttpStatusCode.BadGateway,
            "upstream unavailable",
            ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty);

        await Assert.That(exception.ErrorCode).IsNull();
        await Assert.That(exception.Message).IsEqualTo("upstream unavailable");
    }

    private static EveClient CreateBootstrapClient(
        HttpMessageInvoker transport,
        string bootstrapValue) =>
        new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                RequestHeadersProvider = (context, _) =>
                    ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                        context.Kind == EveRequestKind.CreateSession
                            ? new Dictionary<string, string>
                            {
                                ["x-session-bootstrap"] = bootstrapValue,
                            }
                            : new Dictionary<string, string>()),
            });

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}
