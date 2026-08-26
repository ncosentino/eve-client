using System.Collections.ObjectModel;
using System.Net;
using System.Text;
using System.Text.Json;

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
    public async Task GetHealthAsync_AcceptsWhitespaceWorkflowIdentifier(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """{"ok":true,"status":"ready","workflowId":"   "}""")));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        EveHealthStatus health = await client.GetHealthAsync(cancellationToken);

        await Assert.That(health.WorkflowId)
            .IsEqualTo("   ")
            .Because("Upstream requires a nonempty string without trimming it.");
    }

    [Test]
    public async Task GetHealthAsync_ReportsInvalidJsonAsHealthResponseFailure(
        CancellationToken cancellationToken)
    {
        EveHealthResponseException exception = await GetHealthExceptionAsync(
            "not-json",
            cancellationToken);

        await Assert.That(exception.Issues.Count).IsEqualTo(0);
        await Assert.That(exception.InnerException).IsTypeOf<JsonException>();
    }

    [Test]
    public async Task GetHealthAsync_RejectsUnknownProperties(
        CancellationToken cancellationToken)
    {
        EveHealthResponseException exception = await GetHealthExceptionAsync(
            """{"ok":true,"status":"ready","workflowId":"workflow_1","extra":true}""",
            cancellationToken);

        await Assert.That(exception.Issues.Count).IsEqualTo(1);
        await Assert.That(exception.Issues[0].Path).IsEqualTo(string.Empty);
        await Assert.That(exception.Issues[0].Message).Contains("extra");
    }

    [Test]
    [Arguments("""{"status":"ready","workflowId":"workflow_1"}""", "ok")]
    [Arguments("""{"ok":false,"status":"ready","workflowId":"workflow_1"}""", "ok")]
    [Arguments("""{"ok":true,"status":"starting","workflowId":"workflow_1"}""", "status")]
    [Arguments("""{"ok":true,"status":"ready","workflowId":42}""", "workflowId")]
    [Arguments("""{"ok":true,"status":"ready","workflowId":""}""", "workflowId")]
    public async Task GetHealthAsync_ReportsStructuredSchemaIssue(
        string json,
        string expectedPath,
        CancellationToken cancellationToken)
    {
        EveHealthResponseException exception = await GetHealthExceptionAsync(
            json,
            cancellationToken);

        await Assert.That(exception.Issues.Count).IsEqualTo(1);
        await Assert.That(exception.Issues[0].Path).IsEqualTo(expectedPath);
    }

    [Test]
    public async Task GetHealthAsync_BoundsStructuredSchemaIssues(
        CancellationToken cancellationToken)
    {
        EveHealthResponseException exception = await GetHealthExceptionAsync(
            """{"ok":false,"status":"starting","workflowId":"","a":1,"b":2,"c":3}""",
            cancellationToken);

        await Assert.That(exception.Issues.Count).IsEqualTo(4);
        await Assert.That(exception.Issues[0].Path).IsEqualTo("ok");
        await Assert.That(exception.Issues[1].Path).IsEqualTo("status");
        await Assert.That(exception.Issues[2].Path).IsEqualTo("workflowId");
        await Assert.That(exception.Issues[3].Path).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task GetHealthAsync_ValidatesLastDuplicatePropertyValue(
        CancellationToken cancellationToken)
    {
        EveHealthResponseException exception = await GetHealthExceptionAsync(
            """
            {"ok":true,"status":"ready","workflowId":"workflow_1","workflowId":""}
            """,
            cancellationToken);

        await Assert.That(exception.Issues.Count).IsEqualTo(1);
        await Assert.That(exception.Issues[0].Path).IsEqualTo("workflowId");
    }

    [Test]
    public async Task GetHealthAsync_ReturnsLastDuplicatePropertyValue(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """
            {"ok":true,"status":"ready","workflowId":"","workflowId":"workflow_1"}
            """)));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        EveHealthStatus health = await client.GetHealthAsync(cancellationToken);

        await Assert.That(health.WorkflowId).IsEqualTo("workflow_1");
    }

    [Test]
    public async Task GetHealthAsync_KeepsNonSuccessResponsesAsClientErrors(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.ServiceUnavailable,
            """{"error":"not ready"}""")));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        await Assert.That(async () => await client.GetHealthAsync(cancellationToken))
            .ThrowsExactly<EveClientException>();
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
        await Assert.That(info.ModelRouting)
            .IsEqualTo(EveAgentModelRouting.Unknown)
            .Because("eve reported no routing before 0.33.0.");
        await Assert.That(info.RawModelRouting).IsNull();
        await Assert.That(info.Description).IsEqualTo("Answers weather questions.");
        await Assert.That(info.DevelopmentRoutesAvailable)
            .IsTrue()
            .Because("The agent advertises dev routes.");
        await Assert.That(info.Raw.GetProperty("extra").GetString()).IsEqualTo("preserved");
    }

    [Test]
    [Arguments("gateway", EveAgentModelRouting.Gateway)]
    [Arguments("external", EveAgentModelRouting.External)]
    [Arguments("satellite", EveAgentModelRouting.Unknown)]
    public async Task GetInfoAsync_AcceptsStaticModelRouting(
        string rawRouting,
        EveAgentModelRouting expected,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            $$"""
            {
              "kind": "eve-agent-info",
              "version": 1,
              "mode": "production",
              "agent": {
                "name": "Static Agent",
                "model": {
                  "id": "openai/gpt-5.5",
                  "routing": { "kind": "{{rawRouting}}", "target": "openai/gpt-5.5" }
                }
              },
              "capabilities": { "devRoutes": false }
            }
            """)));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        EveAgentInfo info = await client.GetInfoAsync(cancellationToken);

        await Assert.That(info.ModelId).IsEqualTo("openai/gpt-5.5");
        await Assert.That(info.ModelRouting).IsEqualTo(expected);
        await Assert.That(info.RawModelRouting).IsEqualTo(rawRouting);
    }

    [Test]
    public async Task GetInfoAsync_AcceptsDynamicModelWithoutIdentifier(
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
              "mode": "production",
              "agent": {
                "name": "Dynamic Agent",
                "model": {
                  "routing": { "kind": "dynamic" },
                  "contextWindowTokens": 200000
                }
              },
              "capabilities": { "devRoutes": false }
            }
            """)));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        EveAgentInfo info = await client.GetInfoAsync(cancellationToken);

        await Assert.That(info.AgentName).IsEqualTo("Dynamic Agent");
        await Assert.That(info.ModelId)
            .IsNull()
            .Because("eve 0.33.0 reports no identifier for a dynamic model.");
        await Assert.That(info.ModelRouting).IsEqualTo(EveAgentModelRouting.Dynamic);
        await Assert.That(info.RawModelRouting).IsEqualTo("dynamic");
        await Assert.That(
                info.Raw.GetProperty("agent").GetProperty("model")
                    .GetProperty("contextWindowTokens").GetInt32())
            .IsEqualTo(200000)
            .Because("The complete model object stays available in Raw.");
    }

    [Test]
    [Arguments(
        """{ "id": "openai/gpt-5.5", "routing": { "kind": "dynamic" } }""",
        "A dynamic model may not also report an identifier.")]
    [Arguments(
        """{ "routing": { "kind": "dynamic" }, "endpoint": { "kind": "external", "provider": "openai" } }""",
        "A dynamic model may not also report an endpoint.")]
    [Arguments(
        """{ "routing": { "kind": "gateway", "target": "openai/gpt-5.5" } }""",
        "A static model must report an identifier.")]
    [Arguments("{ }", "A model with neither an identifier nor dynamic routing is invalid.")]
    public async Task GetInfoAsync_RejectsContradictoryModelShapes(
        string modelJson,
        string reason,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            $$"""
            {
              "kind": "eve-agent-info",
              "version": 1,
              "mode": "production",
              "agent": { "name": "Hybrid Agent", "model": {{modelJson}} },
              "capabilities": { "devRoutes": false }
            }
            """)));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        await Assert.That(async () => await client.GetInfoAsync(cancellationToken))
            .Throws<EveProtocolException>()
            .Because(reason);
    }

    [Test]
    public async Task GetInfoAsync_AcceptsSchemaVersionTwoWithRoleAwareInstructions(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """
            {
              "kind": "eve-agent-info",
              "version": 2,
              "mode": "production",
              "agent": {
                "name": "Role Aware Agent",
                "model": { "id": "openai/gpt-5.5" }
              },
              "capabilities": { "devRoutes": false },
              "instructions": {
                "dynamic": [],
                "static": [
                  { "name": "base", "logicalPath": "a.md", "sourceKind": "file",
                    "content": "Be terse.", "role": "system" },
                  { "name": "extra", "logicalPath": "b.md", "sourceKind": "file",
                    "content": "Ask first.", "role": "user" }
                ]
              }
            }
            """)));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        EveAgentInfo info = await client.GetInfoAsync(cancellationToken);

        await Assert.That(info.Version)
            .IsEqualTo(2)
            .Because("eve 0.35.0 raised the agent-info schema version.");
        await Assert.That(info.AgentName).IsEqualTo("Role Aware Agent");
        JsonElement instructions = info.Raw
            .GetProperty("instructions")
            .GetProperty("static");
        await Assert.That(instructions.GetArrayLength())
            .IsEqualTo(2)
            .Because("Static instructions became a list.");
        await Assert.That(instructions[0].GetProperty("role").GetString()).IsEqualTo("system");
        await Assert.That(instructions[1].GetProperty("content").GetString())
            .IsEqualTo("Ask first.");
    }

    [Test]
    public async Task GetInfoAsync_AcceptsSchemaVersionThreeAndPreservesRawPayload(
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            AgentInfoV3Fixture.ValidJson)));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        EveAgentInfo info = await client.GetInfoAsync(cancellationToken);

        await Assert.That(info.Version).IsEqualTo(3);
        await Assert.That(info.AgentName).IsEqualTo("Test Agent");
        await Assert.That(info.Description).IsEqualTo("Exercises schema v3.");
        await Assert.That(
                info.Raw.GetProperty("agent").GetProperty("config")
                    .GetProperty("sourceId").GetString())
            .IsEqualTo("agent.ts")
            .Because("Canonical v3 source ownership remains available through Raw.");
        await Assert.That(
                info.Raw.GetProperty("workspace").GetProperty("rootEntries").GetArrayLength())
            .IsEqualTo(0);
    }

    [Test]
    public async Task GetInfoAsync_AcceptsSchemaVersionThreeExternalModel(
        CancellationToken cancellationToken)
    {
        EveAgentInfo info = await GetInfoAsync(
            AgentInfoV3Fixture.WithExternalModel(),
            cancellationToken);

        await Assert.That(info.ModelId).IsEqualTo("anthropic/claude-opus-4.8");
        await Assert.That(info.ModelRouting).IsEqualTo(EveAgentModelRouting.External);
        await Assert.That(info.RawModelRouting).IsEqualTo("external");
    }

    [Test]
    public async Task GetInfoAsync_AcceptsSchemaVersionThreeDynamicModel(
        CancellationToken cancellationToken)
    {
        EveAgentInfo info = await GetInfoAsync(
            AgentInfoV3Fixture.WithDynamicModel(),
            cancellationToken);

        await Assert.That(info.ModelId).IsNull();
        await Assert.That(info.ModelRouting).IsEqualTo(EveAgentModelRouting.Dynamic);
        await Assert.That(
                info.Raw.GetProperty("agent").GetProperty("model")
                    .GetProperty("routing").GetProperty("resolver")
                    .GetProperty("slug").GetString())
            .IsEqualTo("model");
    }

    [Test]
    public async Task GetInfoAsync_AcceptsSchemaVersionThreeKernelEffectsAndSubagents(
        CancellationToken cancellationToken)
    {
        EveAgentInfo info = await GetInfoAsync(
            AgentInfoV3Fixture.WithKernelEffectAndSubagent(),
            cancellationToken);

        await Assert.That(info.Raw.GetProperty("kernelEffects").GetArrayLength()).IsEqualTo(1);
        await Assert.That(
                info.Raw.GetProperty("subagents").GetProperty("local").GetArrayLength())
            .IsEqualTo(1);
    }

    [Test]
    public async Task GetInfoAsync_AcceptsSchemaVersionThreeBackingAndOptionalVariants(
        CancellationToken cancellationToken)
    {
        EveAgentInfo info = await GetInfoAsync(
            AgentInfoV3Fixture.WithBackingAndOptionalVariants(),
            cancellationToken);

        await Assert.That(
                info.Raw.GetProperty("agent").GetProperty("config")
                    .GetProperty("binding").GetProperty("backing")
                    .GetProperty("kind").GetString())
            .IsEqualTo("programmatic");
        await Assert.That(info.Raw.GetProperty("connections").GetArrayLength()).IsEqualTo(1);
        await Assert.That(info.Raw.GetProperty("channels").GetProperty("shadowed").GetArrayLength())
            .IsEqualTo(1);
    }

    [Test]
    public async Task GetInfoAsync_AcceptsSchemaVersionThreeOpaqueToolInputSchema(
        CancellationToken cancellationToken)
    {
        EveAgentInfo info = await GetInfoAsync(
            AgentInfoV3Fixture.WithBooleanToolInputSchema(),
            cancellationToken);

        await Assert.That(
                info.Raw.GetProperty("tools").GetProperty("static")[0]
                    .GetProperty("inputSchema").GetBoolean())
            .IsTrue()
            .Because("Upstream models tool inputSchema as opaque JSON.");
    }

    [Test]
    public async Task GetInfoAsync_AcceptsSchemaVersionFourAndPreservesMemoryMetadata(
        CancellationToken cancellationToken)
    {
        EveAgentInfo info = await GetInfoAsync(
            AgentInfoV4Fixture.ValidJson,
            cancellationToken);

        await Assert.That(info.Version).IsEqualTo(4);
        JsonElement memory = info.Raw.GetProperty("memories")[0];
        await Assert.That(memory.GetProperty("slot").GetString()).IsEqualTo("profile");
        await Assert.That(memory.GetProperty("visibility").GetString()).IsEqualTo("session");
        await Assert.That(memory.GetProperty("tools").GetBoolean())
            .IsFalse()
            .Because("Schema v4 permits only the literal false when tools is present.");
    }

    [Test]
    public async Task GetInfoAsync_AcceptsSchemaVersionFourSubagentMemorySummary(
        CancellationToken cancellationToken)
    {
        EveAgentInfo info = await GetInfoAsync(
            AgentInfoV4Fixture.WithSubagentSummary(),
            cancellationToken);

        await Assert.That(
                info.Raw.GetProperty("subagents").GetProperty("local")[0]
                    .GetProperty("summary").GetProperty("memories").GetInt32())
            .IsEqualTo(0);
    }

    [Test]
    public async Task GetInfoAsync_AcceptsSchemaVersionFourProgrammaticBackingMetadata(
        CancellationToken cancellationToken)
    {
        EveAgentInfo info = await GetInfoAsync(
            AgentInfoV4Fixture.WithProgrammaticBackingMetadata(),
            cancellationToken);

        JsonElement backing = info.Raw.GetProperty("agent").GetProperty("config")
            .GetProperty("binding").GetProperty("backing");
        await Assert.That(
                backing.GetProperty("dependencies").GetProperty("@vercel/ai").GetString())
            .IsEqualTo("7.0.58");
        await Assert.That(
                backing.GetProperty("parameters").GetProperty("limit").GetInt32())
            .IsEqualTo(25);
        await Assert.That(
                info.Raw.GetProperty("channels").GetProperty("shadowed")[0]
                    .GetProperty("source").GetProperty("form").GetString())
            .IsEqualTo("direct");
    }

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionFourMissingMemories(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV4Fixture.WithoutMemories(),
            "Schema v4 requires the memories collection.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionFourDuplicateMemorySlots(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV4Fixture.WithDuplicateMemorySlots(),
            "Schema v4 memory slots must be unique.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionFourInvalidMemoryVisibility(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV4Fixture.WithInvalidMemoryVisibility(),
            "Schema v4 memory visibility is limited to scope or session.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionFourMemoryToolsTrue(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV4Fixture.WithMemoryToolsTrue(),
            "Schema v4 permits only literal false for the optional memory tools field.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionFourMemoryBindingMismatch(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV4Fixture.WithMemoryBindingOwnerMismatch(),
            "Schema v4 memory sources use the canonical binding provenance rules.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionFourMissingSubagentMemorySummary(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV4Fixture.WithSubagentSummaryMissingMemories(),
            "Schema v4 subagent summaries require memory counts.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionFourInvalidProgrammaticDependency(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV4Fixture.WithInvalidProgrammaticDependency(),
            "Schema v4 programmatic dependencies must map strings to strings.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionFourMissingSourceDescriptorForm(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV4Fixture.WithShadowedSourceDescriptorMissingForm(),
            "Schema v4 source descriptors require a direct or derived form.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionFourInvalidSourceDescriptorForm(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV4Fixture.WithInvalidShadowedSourceDescriptorForm(),
            "Schema v4 source descriptor forms are limited to direct or derived.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_KeepsSchemaVersionThreeProgrammaticBackingStrict(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV4Fixture.VersionThreeWithProgrammaticBackingMetadata(),
            "Schema v3 must not accept fields introduced by schema v4.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_KeepsSchemaVersionThreeSourceDescriptorsStrict(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV4Fixture.VersionThreeWithSourceDescriptorForm(),
            "Schema v3 must not accept the schema-v4 source descriptor form.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsVersionTwoPayloadRelabeledAsVersionThree(
        CancellationToken cancellationToken)
    {
        const string json =
            """
            {
              "kind": "eve-agent-info",
              "version": 3,
              "mode": "production",
              "agent": {
                "name": "Relabeled Agent",
                "model": { "id": "openai/gpt-5.5" }
              },
              "capabilities": { "devRoutes": false },
              "instructions": { "dynamic": [], "static": [] }
            }
            """;

        await AssertInfoRejectedAsync(
            json,
            "Schema v3 requires the canonical source graph rather than a relabeled v2 payload.",
            cancellationToken);
    }

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionThreeMissingRequiredStructure(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV3Fixture.WithoutChannels(),
            "Schema v3 requires every canonical top-level collection.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionThreeDuplicateIdentities(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV3Fixture.WithDuplicateToolNames(),
            "Schema v3 tool names must be unique.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionThreeRouteCollisions(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV3Fixture.WithNormalizedRouteCollision(),
            "Equivalent parameterized route patterns must not collide.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionThreeMismatchedTotals(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV3Fixture.WithMismatchedRemoteAgentTotal(),
            "Schema v3 totals must equal their entry counts.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionThreeModuleWithoutBinding(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV3Fixture.WithModuleSourceMissingBinding(),
            "Every module source must identify its binding.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionThreeBindingPathMismatch(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV3Fixture.WithBindingLogicalPathMismatch(),
            "A binding logical path must match its source.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionThreeBindingOwnerMismatch(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV3Fixture.WithBindingOwnerMismatch(),
            "A binding owner must match its source.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_RejectsSchemaVersionThreeUnknownRootProperty(
        CancellationToken cancellationToken) =>
        await AssertInfoRejectedAsync(
            AgentInfoV3Fixture.WithUnknownRootProperty(),
            "Schema v3 rejects unknown root fields.",
            cancellationToken);

    [Test]
    public async Task GetInfoAsync_AcceptsSchemaVersionOne(CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue(static (_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            """
            {
              "kind": "eve-agent-info",
              "version": 1,
              "mode": "production",
              "agent": { "name": "Legacy Agent", "model": { "id": "openai/gpt-5.5" } },
              "capabilities": { "devRoutes": false },
              "instructions": { "dynamic": [], "static": { "name": "base", "markdown": "Hi." } }
            }
            """)));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        EveAgentInfo info = await client.GetInfoAsync(cancellationToken);

        await Assert.That(info.Version)
            .IsEqualTo(1)
            .Because("Agents older than eve 0.35.0 stay supported.");
        await Assert.That(
                info.Raw.GetProperty("instructions").GetProperty("static")
                    .GetProperty("markdown").GetString())
            .IsEqualTo("Hi.");
    }

    [Test]
    [Arguments(0)]
    [Arguments(5)]
    public async Task GetInfoAsync_RejectsUnsupportedSchemaVersion(
        int version,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.OK,
            $$"""
            {
              "kind": "eve-agent-info",
              "version": {{version}},
              "mode": "production",
              "agent": { "name": "Future Agent", "model": { "id": "openai/gpt-5.5" } },
              "capabilities": { "devRoutes": false }
            }
            """)));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        await Assert.That(async () => await client.GetInfoAsync(cancellationToken))
            .Throws<EveProtocolException>()
            .Because("An unknown schema version must not be parsed optimistically.");
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

    private static async Task AssertInfoRejectedAsync(
        string json,
        string reason,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, json)));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        await Assert.That(async () => await client.GetInfoAsync(cancellationToken))
            .Throws<EveProtocolException>()
            .Because(reason);
    }

    private static async Task<EveAgentInfo> GetInfoAsync(
        string json,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, json)));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        return await client.GetInfoAsync(cancellationToken);
    }

    private static async Task<EveHealthResponseException> GetHealthExceptionAsync(
        string json,
        CancellationToken cancellationToken)
    {
        using RecordingHttpMessageHandler handler = new();
        using HttpMessageInvoker transport = new(handler, false);
        handler.Enqueue((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, json)));
        EveClient client = new(transport, new EveClientOptions("https://agent.example.com"));

        return (await Assert.That(async () => await client.GetHealthAsync(cancellationToken))
            .ThrowsExactly<EveHealthResponseException>())!;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}
