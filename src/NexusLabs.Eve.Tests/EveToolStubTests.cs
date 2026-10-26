using System.Net;
using System.Text;
using System.Text.Json;

namespace NexusLabs.Eve.Tests;

public sealed class EveToolStubTests
{
    [Test]
    public async Task InitialMessage_SerializesMixedSequenceOnlyAtCreation(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));
        using JsonDocument responseDocument = JsonDocument.Parse("""{"ok":[null,true,42,"raw"]}""");
        using JsonDocument matchDocument = JsonDocument.Parse("""{"const":"issue"}""");
        EveToolStub[] rules =
        [
            new("retry", "researcher/list_tasks",
                [
                    EveToolStubOutcome.Throw("transient"),
                    EveToolStubOutcome.Throw("", "ServiceError"),
                    EveToolStubOutcome.Respond(responseDocument.RootElement),
                ],
                new Dictionary<string, JsonElement> { ["kind"] = matchDocument.RootElement }),
        ];
        EveSession session = client.CreateSession(new EveSessionCreationOptions { Stubs = rules });
        rules[0] = new EveToolStub("replacement", "other", EveToolStubOutcome.Throw("wrong"));

        await session.SendAsync(EveMessageContent.FromText("Begin"),
            new EveTurnOptions { TurnPolicy = EveTurnPolicy.Queue }, cancellationToken);
        await session.SendAsync("Continue", cancellationToken);
        await session.RespondAsync([new EveInputResponse("approval", text: "yes")], cancellationToken);

        await Assert.That(handler.Calls.Count).IsEqualTo(3);
        await Assert.That(handler.Calls[0].Uri).IsEqualTo("https://agent.example.com/eve/v1/session");
        using JsonDocument initial = JsonDocument.Parse(handler.Calls[0].Body!);
        await Assert.That(initial.RootElement.GetProperty("stubs").GetRawText()).IsEqualTo(
            """[{"id":"retry","tool":"researcher/list_tasks","match":{"kind":{"const":"issue"}},"outcomes":[{"throw":{"message":"transient"}},{"throw":{"message":"","name":"ServiceError"}},{"response":{"ok":[null,true,42,"raw"]}}]}]""");
        await Assert.That(initial.RootElement.GetProperty("message").GetString()).IsEqualTo("Begin");
        await Assert.That(initial.RootElement.TryGetProperty("turnPolicy", out _)).IsFalse()
            .Because("Initial messages have no existing turn to steer");
        for (int index = 1; index < handler.Calls.Count; index++)
        {
            using JsonDocument later = JsonDocument.Parse(handler.Calls[index].Body!);
            await Assert.That(later.RootElement.TryGetProperty("stubs", out _)).IsFalse()
                .Because("Rules are fixed at creation and cannot appear on continuation or input responses");
        }
    }

    [Test]
    public async Task Prewarm_SerializesSingleOutcomeAndCreationHeaders(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com")
        {
            Headers = new Dictionary<string, string> { ["x-test"] = "client" },
        });
        using JsonDocument json = JsonDocument.Parse("null");
        EveSession session = await client.PrewarmSessionAsync(new EveSessionCreationOptions
        {
            Stubs = [new EveToolStub("fixed", "list_tasks", EveToolStubOutcome.Respond(json.RootElement))],
            Headers = new Dictionary<string, string> { ["x-test"] = "creation" },
        }, cancellationToken);
        await session.SendAsync("Begin", cancellationToken);

        await Assert.That(handler.Calls.Count).IsEqualTo(2);
        await Assert.That(handler.Calls[0].Body).IsEqualTo(
            """{"stubs":[{"id":"fixed","tool":"list_tasks","outcome":{"response":null}}]}""");
        await Assert.That(handler.Calls[0].Headers["content-type"]).IsEqualTo("application/json");
        await Assert.That(handler.Calls[0].Headers["x-test"]).IsEqualTo("creation");
        await Assert.That(handler.Calls[1].Headers["x-test"]).IsEqualTo("client");
        using JsonDocument later = JsonDocument.Parse(handler.Calls[1].Body!);
        await Assert.That(later.RootElement.TryGetProperty("stubs", out _)).IsFalse()
            .Because("A prewarmed session already has fixed rules");
    }

    [Test]
    public async Task OmittedAndEmptyStubs_HaveDistinctCreationBodies(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        for (int index = 0; index < 4; index++)
        {
            handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        }

        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));
        await client.PrewarmSessionAsync(new EveSessionCreationOptions(), cancellationToken);
        await client.PrewarmSessionAsync(new EveSessionCreationOptions { Stubs = [] }, cancellationToken);
        await client.CreateSession().SendAsync("ordinary", cancellationToken);
        await client.CreateSession(new EveSessionCreationOptions { Stubs = [] })
            .SendAsync("empty", cancellationToken);

        await Assert.That(handler.Calls.Count).IsEqualTo(4);
        await Assert.That(handler.Calls[0].Body).IsNull();
        await Assert.That(handler.Calls[0].ContentHeaders.Count).IsEqualTo(0);
        await Assert.That(handler.Calls[1].Body).IsEqualTo("""{"stubs":[]}""");
        await Assert.That(handler.Calls[2].Body).IsEqualTo("""{"message":"ordinary"}""");
        await Assert.That(handler.Calls[3].Body).IsEqualTo("""{"message":"empty","stubs":[]}""");
    }

    [Test]
    [Arguments(true, 403)]
    [Arguments(false, 403)]
    [Arguments(true, 400)]
    [Arguments(false, 400)]
    public async Task Creation_PropagatesPermissionAndSchemaErrors(
        bool prewarm,
        int status,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent("""{"ok":false,"error":"Stub configuration rejected."}""",
                Encoding.UTF8, "application/json"),
        }));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));
        EveSessionCreationOptions options = new()
        {
            Stubs = [new EveToolStub("fixed", "list_tasks", EveToolStubOutcome.Throw("failure"))],
        };

        EveClientException? failure = null;
        await Assert.That(async () =>
        {
            try
            {
                if (prewarm)
                {
                    await client.PrewarmSessionAsync(options, cancellationToken);
                }
                else
                {
                    await client.CreateSession(options).SendAsync("Begin", cancellationToken);
                }
            }
            catch (EveClientException exception)
            {
                failure = exception;
                throw;
            }
        }).Throws<EveClientException>();
        await Assert.That(failure!.StatusCode).IsEqualTo((HttpStatusCode)status);
        await Assert.That(failure!.ResponseBody).IsEqualTo(
            """{"ok":false,"error":"Stub configuration rejected."}""");
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SendRawAsync_PreservesCallerOwnedStubBody(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));
        const string body = """{"message":"raw","stubs":[{"id":"raw","tool":"tool","outcome":{"throw":{"message":"failure"}}}]}""";
        using HttpRequestMessage request = new(HttpMethod.Post, "/eve/v1/session")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        using HttpResponseMessage response = await client.SendRawAsync(
            request, new EveRawRequestOptions(), cancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Accepted);
        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        await Assert.That(handler.Calls[0].Body).IsEqualTo(body);
    }

    [Test]
    public async Task Models_SnapshotJsonAndAcceptBooleanConstraints()
    {
        EveToolStub rule;
        using (JsonDocument document = JsonDocument.Parse("""[1,false,null]"""))
        using (JsonDocument schema = JsonDocument.Parse("true"))
        {
            rule = new EveToolStub("id", "tool", EveToolStubOutcome.Respond(document.RootElement),
                new Dictionary<string, JsonElement> { ["required"] = schema.RootElement });
        }

        await Assert.That(rule.Outcomes.Count).IsEqualTo(1);
        await Assert.That(rule.Outcomes[0].Json.GetRawText())
            .IsEqualTo("""{"response":[1,false,null]}""");
        await Assert.That(rule.Match!.Count).IsEqualTo(1);
        await Assert.That(rule.Match!["required"].ValueKind).IsEqualTo(JsonValueKind.True);
        await Assert.That(rule.IsSequence).IsFalse().Because("The single-outcome constructor emits outcome");
    }

    [Test]
    public async Task Models_RejectInvalidStructuralDeclarations()
    {
        await Assert.That(() => EveToolStubOutcome.Throw("message", "")).Throws<ArgumentException>();
        await Assert.That(() => EveToolStubOutcome.Respond(default)).Throws<ArgumentException>();
        await Assert.That(() => new EveToolStub("", "tool", EveToolStubOutcome.Throw("")))
            .Throws<ArgumentException>();
        await Assert.That(() => new EveToolStub("id", "", EveToolStubOutcome.Throw("")))
            .Throws<ArgumentException>();
        await Assert.That(() => new EveToolStub("id", "tool", Array.Empty<EveToolStubOutcome>()))
            .Throws<ArgumentException>();
        await Assert.That(() => new EveToolStub("id", "child/eve__tool", EveToolStubOutcome.Throw("")))
            .Throws<ArgumentException>();
        using JsonDocument schema = JsonDocument.Parse("42");
        await Assert.That(() => new EveToolStub("id", "tool", EveToolStubOutcome.Throw(""),
            new Dictionary<string, JsonElement> { ["value"] = schema.RootElement }))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Creation_PreservesCaseDistinctRuleIds(CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(AcceptedResponse()));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));
        EveSessionCreationOptions options = new()
        {
            Stubs =
            [
                new EveToolStub("Rule", "tool", EveToolStubOutcome.Throw("first")),
                new EveToolStub("rule", "tool", EveToolStubOutcome.Throw("second")),
            ],
        };

        await client.PrewarmSessionAsync(options, cancellationToken);

        await Assert.That(handler.Calls.Count).IsEqualTo(1);
        using JsonDocument body = JsonDocument.Parse(handler.Calls[0].Body!);
        JsonElement rules = body.RootElement.GetProperty("stubs");
        await Assert.That(rules.GetArrayLength()).IsEqualTo(2);
        await Assert.That(rules[0].GetProperty("id").GetString()).IsEqualTo("Rule");
        await Assert.That(rules[1].GetProperty("id").GetString()).IsEqualTo("rule");
    }

    [Test]
    public async Task Creation_RejectsDuplicateIdsAndExcessRulesBeforeIo()
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));
        EveToolStub rule = new("duplicate", "tool", EveToolStubOutcome.Throw(""));
        await Assert.That(() => client.CreateSession(new EveSessionCreationOptions { Stubs = [rule, rule] }))
            .Throws<ArgumentException>();
        await Assert.That(() => client.CreateSession(new EveSessionCreationOptions
        {
            Stubs = Enumerable.Range(0, 101).Select(index =>
                new EveToolStub(index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "tool", EveToolStubOutcome.Throw(""))).ToArray(),
        })).Throws<ArgumentException>();
        await Assert.That(handler.Calls.Count).IsEqualTo(0);
    }

    private static HttpResponseMessage AcceptedResponse() =>
        new(HttpStatusCode.Accepted)
        {
            Content = new StringContent(
                """{"ok":true,"sessionId":"session_1","deliveryId":"delivery_1","status":"accepted"}""",
                Encoding.UTF8, "application/json"),
        };
}
