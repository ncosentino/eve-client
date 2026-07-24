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
    public async Task SendRawAsync_UsesRequestHeadersBeforeAuthentication(
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
                    ["x-scope"] = "client",
                    ["authorization"] = "Bearer stale",
                },
                Authentication = new EveBearerAuthentication("fresh"),
            });
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri("/custom", UriKind.Relative));
        request.Headers.TryAddWithoutValidation("x-scope", "request");

        using HttpResponseMessage response = await client.SendRawAsync(
            request,
            cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(handler.Calls[0].Headers["x-scope"]).IsEqualTo("request");
        await Assert.That(handler.Calls[0].Headers["authorization"]).IsEqualTo("Bearer fresh");
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
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}
