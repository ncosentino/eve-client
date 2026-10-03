using System.Collections.ObjectModel;
using System.Net;
using System.Text;

namespace NexusLabs.Eve.Tests;

public sealed class EveClientExceptionTests
{
    [Test]
    [Arguments("text/html", "<!doctype html><title>Not Found</title>")]
    [Arguments("TeXt/HtMl; ChArSeT=UTF-8", "<html><body>Not Found</body></html>")]
    [Arguments("text/html; charset=utf-8", """{"error":""")]
    [Arguments("text/html", "")]
    public async Task Constructor_SummarizesUnparseableHtmlAndPreservesResponse(
        string contentType,
        string body)
    {
        Dictionary<string, IReadOnlyList<string>> headers = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Content-Type"] = new[] { contentType },
            ["X-Request-Id"] = new[] { "request_1", "request_2" },
        };
        EveClientException exception = new(HttpStatusCode.NotFound, body, headers);

        await Assert.That(exception.Message).IsEqualTo(
            "Server returned 404 with an HTML response. "
            + "Check the eve route and development server configuration.");
        await Assert.That(exception.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await Assert.That(exception.ResponseBody).IsEqualTo(body);
        await Assert.That(exception.ErrorCode).IsNull();
        await Assert.That(exception.ResponseHeaders.Count).IsEqualTo(2);
        await Assert.That(exception.ResponseHeaders["content-type"].Count).IsEqualTo(1);
        await Assert.That(exception.ResponseHeaders["content-type"][0]).IsEqualTo(contentType);
        await Assert.That(exception.ResponseHeaders["x-request-id"].Count).IsEqualTo(2);
        await Assert.That(exception.ResponseHeaders["x-request-id"][0]).IsEqualTo("request_1");
        await Assert.That(exception.ResponseHeaders["x-request-id"][1]).IsEqualTo("request_2");
    }

    [Test]
    [Arguments(
        """{"error":"Credentials are invalid.","code":"auth_invalid"}""",
        "Credentials are invalid.",
        "auth_invalid")]
    [Arguments("""{"error":""}""", "", null)]
    [Arguments("""{"error":42}""", """{"error":42}""", null)]
    [Arguments("""{"code":"future_code"}""", """{"code":"future_code"}""", "future_code")]
    [Arguments("""["not an error object"]""", """["not an error object"]""", null)]
    [Arguments("null", "null", null)]
    public async Task Constructor_PrioritizesValidJsonOverHtmlContentType(
        string body,
        string expectedMessage,
        string? expectedCode)
    {
        EveClientException exception = new(
            HttpStatusCode.Unauthorized,
            body,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Content-Type"] = new[] { "TeXt/HtMl; charset=utf-8" },
            });

        await Assert.That(exception.Message).IsEqualTo(expectedMessage);
        await Assert.That(exception.ErrorCode).IsEqualTo(expectedCode);
        await Assert.That(exception.ResponseBody).IsEqualTo(body);
        await Assert.That(exception.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Arguments("upstream unavailable", "upstream unavailable")]
    [Arguments("""{"error":""", """{"error":""")]
    [Arguments("<html>not identified as HTML</html>", "<html>not identified as HTML</html>")]
    [Arguments("", "The eve server returned HTTP 502.")]
    [Arguments(" ", " ")]
    public async Task Constructor_PreservesFallbackWithoutHtmlContentType(
        string body,
        string expectedMessage)
    {
        EveClientException exception = new(
            HttpStatusCode.BadGateway,
            body,
            ReadOnlyDictionary<string, IReadOnlyList<string>>.Empty);

        await Assert.That(exception.Message).IsEqualTo(expectedMessage);
        await Assert.That(exception.ResponseBody).IsEqualTo(body);
        await Assert.That(exception.ErrorCode).IsNull();
    }

    [Test]
    [Arguments("text/plain", "upstream unavailable", "upstream unavailable")]
    [Arguments("application/json", """{"error":""", """{"error":""")]
    [Arguments("text/plain", "", "The eve server returned HTTP 502.")]
    [Arguments("application/json", "", "The eve server returned HTTP 502.")]
    public async Task Constructor_PreservesUnparseableNonHtmlResponse(
        string contentType,
        string body,
        string expectedMessage)
    {
        EveClientException exception = new(
            HttpStatusCode.BadGateway,
            body,
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["content-type"] = new[] { contentType },
            });

        await Assert.That(exception.Message).IsEqualTo(expectedMessage);
        await Assert.That(exception.ResponseBody).IsEqualTo(body);
        await Assert.That(exception.ErrorCode).IsNull();
    }

    [Test]
    [Arguments(false, HttpStatusCode.Forbidden)]
    [Arguments(true, HttpStatusCode.Forbidden)]
    [Arguments(true, HttpStatusCode.NotFound)]
    public async Task HttpAndStreamFailures_ShareHtmlDiagnostics(
        bool stream,
        HttpStatusCode statusCode,
        CancellationToken cancellationToken)
    {
        const string body = "<!doctype html><title>Wrong route</title>";
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) =>
        {
            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8),
            };
            response.Content.Headers.Remove("Content-Type");
            response.Content.Headers.TryAddWithoutValidation(
                "CoNtEnT-TyPe",
                "TeXt/HtMl; charset=utf-8");
            response.Headers.TryAddWithoutValidation("X-Request-Id", "request_1");
            return Task.FromResult(response);
        });
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        EveClientException? exception;
        if (stream)
        {
            EveSession session = client.AttachSession("session_1");
            await using IAsyncEnumerator<EveStreamEvent> enumerator = session.StreamAsync(
                new EveStreamOptions
                {
                    ReconnectPolicy = new EveStreamReconnectPolicy
                    {
                        StreamOpenRetry = new EveRetryPolicy
                        {
                            MaxAttempts = 1,
                        },
                    },
                },
                cancellationToken).GetAsyncEnumerator(cancellationToken);
            exception = await Assert.That(async () => await enumerator.MoveNextAsync())
                .ThrowsExactly<EveClientException>();
            await Assert.That(session.State.StreamIndex).IsEqualTo(0);
        }
        else
        {
            exception = await Assert.That(async () => await client.GetHealthAsync(cancellationToken))
                .ThrowsExactly<EveClientException>();
        }

        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.Message).IsEqualTo(
            $"Server returned {(int)statusCode} with an HTML response. "
            + "Check the eve route and development server configuration.");
        await Assert.That(exception.ResponseBody).IsEqualTo(body);
        await Assert.That(exception.StatusCode).IsEqualTo(statusCode);
        await Assert.That(exception.ErrorCode).IsNull();
        await Assert.That(exception.ResponseHeaders["content-type"].Count).IsEqualTo(1);
        await Assert.That(exception.ResponseHeaders["content-type"][0])
            .IsEqualTo("TeXt/HtMl; charset=utf-8");
        await Assert.That(exception.ResponseHeaders["x-request-id"].Count).IsEqualTo(1);
        await Assert.That(exception.ResponseHeaders["x-request-id"][0]).IsEqualTo("request_1");
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo(stream
            ? "https://agent.example.com/eve/v1/session/session_1/stream?streamControlVersion=1"
            : "https://agent.example.com/eve/v1/health");
    }
}
