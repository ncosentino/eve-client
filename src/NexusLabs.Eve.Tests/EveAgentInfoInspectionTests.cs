using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace NexusLabs.Eve.Tests;

public sealed class EveAgentInfoInspectionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GetInfoAsync_AcceptsCurrentAndHistoricalVersionFiveInspection(
        bool historical,
        CancellationToken cancellationToken)
    {
        string json = AgentInfoV5Fixture.Create(historical, static root =>
        {
            root["agent"]!["outputSchema"] = JsonNode.Parse(
                """{"future":{"opaque":[null,42,{"enabled":true}]}}""");
            root["kernelEffects"]!.AsArray().Add(JsonNode.Parse(
                """{"sourceId":"future","audience":[{"unknown":true}],"kind":"future-kind","action":"future-action"}"""));
        });

        EveAgentInfo info = await GetInfoAsync(json, cancellationToken);

        await Assert.That(info.Version).IsEqualTo(5);
        await Assert.That(info.Raw.GetRawText()).IsEqualTo(json);
        await Assert.That(info.Raw.GetProperty("memories").GetArrayLength()).IsEqualTo(1);
        await Assert.That(info.Raw.GetProperty("memories")[0].TryGetProperty("visibility", out _))
            .IsEqualTo(historical);
        await Assert.That(info.Raw.GetProperty("agent").GetProperty("model")
            .GetProperty("routing").GetProperty("resolver").TryGetProperty("eventNames", out _))
            .IsEqualTo(historical);
    }

    [Test]
    [Arguments("agent.model.routing.resolver", "eventNames")]
    [Arguments("instructions.dynamic.0", "eventNames")]
    [Arguments("skills.dynamic.0", "eventNames")]
    [Arguments("tools.dynamic.0", "eventNames")]
    [Arguments("subagents.local.0.configResolver", "eventNames")]
    [Arguments("memories.0", "visibility")]
    public async Task GetInfoAsync_AcceptsIndependentVersionFiveFieldRemoval(
        string path,
        string property,
        CancellationToken cancellationToken)
    {
        string json = AgentInfoV5Fixture.Create(true, root =>
            AgentInfoV5Fixture.GetObject(root, path).Remove(property));

        EveAgentInfo info = await GetInfoAsync(json, cancellationToken);

        await Assert.That(info.Raw.GetRawText()).IsEqualTo(json);
    }

    [Test]
    [Arguments("[]")]
    [Arguments("[\"\"]")]
    [Arguments("[\"onMessage\",\"future-event\"]")]
    public async Task GetInfoAsync_AcceptsHistoricalEventNameArrays(
        string value,
        CancellationToken cancellationToken)
    {
        string json = AgentInfoV5Fixture.Create(false, root =>
            root["tools"]!["dynamic"]![0]!["eventNames"] = JsonNode.Parse(value));

        EveAgentInfo info = await GetInfoAsync(json, cancellationToken);

        await Assert.That(info.Raw.GetRawText()).IsEqualTo(json);
    }

    [Test]
    [Arguments("agent.model.routing.resolver")]
    [Arguments("instructions.dynamic.0")]
    [Arguments("skills.dynamic.0")]
    [Arguments("tools.dynamic.0")]
    [Arguments("subagents.local.0.configResolver")]
    [Arguments("hooks.0")]
    public async Task GetInfoAsync_ValidatesSuppliedResolverEventNames(
        string path,
        CancellationToken cancellationToken)
    {
        foreach (string value in new[] { "null", "false", "{}", "\"onMessage\"", "[1]", "[null]" })
        {
            string json = AgentInfoV5Fixture.Create(false, root =>
                AgentInfoV5Fixture.GetObject(root, path)["eventNames"] = JsonNode.Parse(value));

            await Assert.That(async () => await GetInfoAsync(json, cancellationToken))
                .Throws<EveProtocolException>();
        }
    }

    [Test]
    [Arguments("\"scope\"")]
    [Arguments("\"session\"")]
    public async Task GetInfoAsync_AcceptsHistoricalMemoryVisibility(
        string value,
        CancellationToken cancellationToken)
    {
        string json = AgentInfoV5Fixture.Create(false, root =>
            root["memories"]![0]!["visibility"] = JsonNode.Parse(value));

        EveAgentInfo info = await GetInfoAsync(json, cancellationToken);

        await Assert.That(info.Raw.GetRawText()).IsEqualTo(json);
    }

    [Test]
    [Arguments("null")]
    [Arguments("true")]
    [Arguments("42")]
    [Arguments("[]")]
    [Arguments("{}")]
    [Arguments("\"global\"")]
    public async Task GetInfoAsync_RejectsMalformedHistoricalMemoryVisibility(
        string value,
        CancellationToken cancellationToken)
    {
        string json = AgentInfoV5Fixture.Create(false, root =>
            root["memories"]![0]!["visibility"] = JsonNode.Parse(value));

        await Assert.That(async () => await GetInfoAsync(json, cancellationToken))
            .Throws<EveProtocolException>();
    }

    [Test]
    [Arguments(3)]
    [Arguments(4)]
    public async Task GetInfoAsync_AcceptsCompleteHistoricalResolverFixtures(
        int version,
        CancellationToken cancellationToken)
    {
        string json = AgentInfoV5Fixture.Legacy(version, static _ => { });

        EveAgentInfo info = await GetInfoAsync(json, cancellationToken);

        await Assert.That(info.Version).IsEqualTo(version);
        await Assert.That(info.Raw.GetRawText()).IsEqualTo(json);
    }

    [Test]
    [Arguments(3, "agent.model.routing.resolver")]
    [Arguments(3, "instructions.dynamic.0")]
    [Arguments(3, "skills.dynamic.0")]
    [Arguments(3, "tools.dynamic.0")]
    [Arguments(3, "subagents.local.0.configResolver")]
    [Arguments(3, "hooks.0")]
    [Arguments(4, "agent.model.routing.resolver")]
    [Arguments(4, "instructions.dynamic.0")]
    [Arguments(4, "skills.dynamic.0")]
    [Arguments(4, "tools.dynamic.0")]
    [Arguments(4, "subagents.local.0.configResolver")]
    [Arguments(4, "hooks.0")]
    public async Task GetInfoAsync_KeepsLegacyResolverEventNamesRequired(
        int version,
        string path,
        CancellationToken cancellationToken)
    {
        string json = AgentInfoV5Fixture.Legacy(version, root =>
            AgentInfoV5Fixture.GetObject(root, path).Remove("eventNames"));

        await Assert.That(async () => await GetInfoAsync(json, cancellationToken))
            .Throws<EveProtocolException>();
    }

    [Test]
    public async Task GetInfoAsync_KeepsVersionFourMemoryVisibilityRequired(
        CancellationToken cancellationToken)
    {
        JsonObject root = JsonNode.Parse(AgentInfoV4Fixture.ValidJson)!.AsObject();
        root["memories"]![0]!.AsObject().Remove("visibility");

        await Assert.That(async () => await GetInfoAsync(root.ToJsonString(), cancellationToken))
            .Throws<EveProtocolException>();
    }

    [Test]
    public async Task GetInfoAsync_KeepsVersionFiveHookEventNamesRequired(
        CancellationToken cancellationToken)
    {
        string json = AgentInfoV5Fixture.Create(false, static root =>
            root["hooks"]![0]!.AsObject().Remove("eventNames"));

        await Assert.That(async () => await GetInfoAsync(json, cancellationToken))
            .Throws<EveProtocolException>();
    }

    [Test]
    [Arguments("agent.model.routing.resolver")]
    [Arguments("instructions.dynamic.0")]
    [Arguments("skills.dynamic.0")]
    [Arguments("tools.dynamic.0")]
    [Arguments("subagents.local.0.configResolver")]
    [Arguments("memories.0")]
    public async Task GetInfoAsync_KeepsVersionFiveUnknownStructureStrict(
        string path,
        CancellationToken cancellationToken)
    {
        string json = AgentInfoV5Fixture.Create(false, root =>
            AgentInfoV5Fixture.GetObject(root, path)["extra"] = true);

        await Assert.That(async () => await GetInfoAsync(json, cancellationToken))
            .Throws<EveProtocolException>();
    }

    [Test]
    [Arguments("agent.model.routing.resolver", "slug")]
    [Arguments("memories.0", "slot")]
    [Arguments("tools.dynamic.0", "binding")]
    public async Task GetInfoAsync_KeepsVersionFiveRequiredStructureStrict(
        string path,
        string property,
        CancellationToken cancellationToken)
    {
        string json = AgentInfoV5Fixture.Create(false, root =>
            AgentInfoV5Fixture.GetObject(root, path).Remove(property));

        await Assert.That(async () => await GetInfoAsync(json, cancellationToken))
            .Throws<EveProtocolException>();
    }

    private static async Task<EveAgentInfo> GetInfoAsync(
        string json,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        return await client.GetInfoAsync(cancellationToken);
    }
}
