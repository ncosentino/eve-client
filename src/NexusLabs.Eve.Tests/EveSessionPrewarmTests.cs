using System.Net;
using System.Text;
using System.Text.Json;

namespace NexusLabs.Eve.Tests;

public sealed class EveSessionPrewarmTests
{
    [Test]
    public async Task PrewarmSessionAsync_FirstSendUsesExistingSession(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedPrewarmResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedMessageResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(StreamResponse(
            """{"type":"session.waiting","data":{"wait":"next-user-message"},"meta":{"at":"2026-09-19T12:00:00.000Z","deliveryIds":["delivery_1"]}}""")));
        EveClient client = new(
            transport,
            new EveClientOptions("https://agent.example.com")
            {
                RequestHeadersProvider = static (context, _) =>
                    ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                        new Dictionary<string, string>
                        {
                            ["x-request-kind"] = context.Kind.ToString(),
                        }),
            });

        EveSession session = await client.PrewarmSessionAsync(cancellationToken);

        await Assert.That(session.State).IsEqualTo(new EveSessionState
        {
            SessionId = "session_1",
            StreamIndex = 0,
        });
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session");
        await Assert.That(handler.Calls[0].Body).IsNull();
        await Assert.That(handler.Calls[0].Headers["x-request-kind"])
            .IsEqualTo(nameof(EveRequestKind.CreateSession));

        EveMessageResponse response = await session.SendAsync(
            "Begin the conversation.",
            cancellationToken);
        EveTurnOutcome outcome = await response.GetOutcomeAsync(cancellationToken);

        await Assert.That(response.SessionId).IsEqualTo("session_1");
        await Assert.That(response.DeliveryId).IsEqualTo("delivery_1");
        await Assert.That(outcome.Status).IsEqualTo(EveTurnStatus.Waiting);
        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        await Assert.That(handler.Calls[1].Uri).IsEqualTo(
            "https://agent.example.com/eve/v1/session/session_1");
        using JsonDocument sentBody = JsonDocument.Parse(handler.Calls[1].Body!);
        await Assert.That(sentBody.RootElement.GetProperty("message").GetString())
            .IsEqualTo("Begin the conversation.");
        await Assert.That(session.State.StreamIndex).IsEqualTo(1);
    }

    [Test]
    public async Task PrewarmSessionAsync_PropagatesCancellation(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, requestCancellationToken) =>
        {
            requestCancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(AcceptedPrewarmResponse());
        });
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));
        using CancellationTokenSource requestCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await requestCancellation.CancelAsync();

        await Assert.That(async () =>
                await client.PrewarmSessionAsync(requestCancellation.Token))
            .Throws<OperationCanceledException>();
    }

    [Test]
    public async Task PrewarmSessionAsync_RejectsMissingSessionIdentifier(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                """{"ok":true,"status":"accepted"}""",
                Encoding.UTF8,
                "application/json"),
        }));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        await Assert.That(async () => await client.PrewarmSessionAsync(cancellationToken))
            .Throws<EveProtocolException>();
    }

    [Test]
    public async Task PrewarmSessionAsync_PropagatesServerFailure(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(new HttpResponseMessage(
            HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"ok":false,"error":"Message-free creation is unavailable."}""",
                Encoding.UTF8,
                "application/json"),
        }));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        await Assert.That(async () => await client.PrewarmSessionAsync(cancellationToken))
            .Throws<EveClientException>();
    }

    private static HttpResponseMessage AcceptedPrewarmResponse() =>
        new(HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                """{"ok":true,"sessionId":"session_1","status":"accepted"}""",
                Encoding.UTF8,
                "application/json"),
        };

    private static HttpResponseMessage AcceptedMessageResponse() =>
        new(HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                """{"ok":true,"sessionId":"session_1","deliveryId":"delivery_1"}""",
                Encoding.UTF8,
                "application/json"),
        };

    private static HttpResponseMessage StreamResponse(params string[] events)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $"{string.Join('\n', events)}\n",
                Encoding.UTF8,
                "application/x-ndjson"),
        };
        response.Headers.TryAddWithoutValidation(
            EveProtocol.StreamVersionHeaderName,
            EveProtocol.MessageStreamVersion);
        return response;
    }
}
