using System.Text.Json.Nodes;

namespace NexusLabs.Eve.Tests;

internal static class AgentInfoV3Fixture
{
    public const string ValidJson =
        """
        {
          "agent": {
            "agentRoot": "/tmp/test-agent/agent",
            "appRoot": "/tmp/test-agent",
            "config": {
              "binding": {
                "backing": {
                  "externalDependencies": [],
                  "kind": "filesystem",
                  "sourcePath": "/tmp/test-agent/agent/agent.ts"
                },
                "logicalPath": "agent.ts",
                "owner": { "kind": "application" }
              },
              "logicalPath": "agent.ts",
              "owner": { "kind": "application" },
              "sourceId": "agent.ts",
              "sourceKind": "module"
            },
            "description": "Exercises schema v3.",
            "model": {
              "id": "openai/gpt-5.5",
              "routing": { "kind": "gateway", "target": "openai" }
            },
            "name": "Test Agent",
            "nodeId": "__root__"
          },
          "capabilities": { "devRoutes": true },
          "channels": { "routes": [], "shadowed": [] },
          "composition": { "disabled": [], "shadowed": [] },
          "connections": [],
          "diagnostics": { "discoveryErrors": 0, "discoveryWarnings": 0 },
          "hooks": [],
          "instructions": { "dynamic": [], "static": [] },
          "kernelEffects": [],
          "kind": "eve-agent-info",
          "mode": "development",
          "remoteAgents": { "entries": [], "total": 0 },
          "sandbox": {
            "binding": {
              "backing": {
                "externalDependencies": [],
                "kind": "filesystem",
                "sourcePath": "/tmp/test-agent/agent/sandbox.ts"
              },
              "logicalPath": "sandbox.ts",
              "owner": { "kind": "application" }
            },
            "hasBootstrap": false,
            "hasOnSession": false,
            "logicalPath": "sandbox.ts",
            "owner": { "kind": "application" },
            "sourceId": "sandbox.ts",
            "sourceKind": "module"
          },
          "schedules": [],
          "skills": { "dynamic": [], "static": [] },
          "subagents": { "local": [], "total": 0 },
          "tools": { "dynamic": [], "static": [] },
          "version": 3,
          "workflow": { "enabled": false, "toolName": "Workflow" },
          "workspace": { "resourceRoot": null, "rootEntries": [] }
        }
        """;

    public static string WithDuplicateToolNames() =>
        Mutate(static root =>
        {
            JsonArray tools = root["tools"]!["static"]!.AsArray();
            tools.Add(CreateTool("duplicate", "tools/first.ts"));
            tools.Add(CreateTool("duplicate", "tools/second.ts"));
        });

    public static string WithExternalModel() =>
        Mutate(static root =>
            root["agent"]!["model"] = new JsonObject
            {
                ["id"] = "anthropic/claude-opus-4.8",
                ["routing"] = new JsonObject
                {
                    ["kind"] = "external",
                    ["provider"] = "anthropic",
                },
            });

    public static string WithDynamicModel() =>
        Mutate(static root =>
            root["agent"]!["model"] = new JsonObject
            {
                ["routing"] = new JsonObject
                {
                    ["kind"] = "dynamic",
                    ["resolver"] = CreateDynamicResolver("model"),
                },
            });

    public static string WithKernelEffectAndSubagent() =>
        Mutate(static root =>
        {
            root["kernelEffects"]!.AsArray().Add(new JsonObject
            {
                ["action"] = "subagent-call",
                ["audience"] = new JsonArray("root-session", "delegated-task-child"),
                ["kind"] = "dispatch",
                ["sourceId"] = "tools/agent.ts",
            });
            root["subagents"]!["local"]!.AsArray().Add(new JsonObject
            {
                ["binding"] = CreateBinding("subagents/researcher.ts"),
                ["configResolver"] = CreateDynamicResolver("researcher-config"),
                ["description"] = "Researches a bounded topic.",
                ["entryPath"] = "subagents/researcher.ts",
                ["logicalPath"] = "subagents/researcher.ts",
                ["name"] = "researcher",
                ["nodeId"] = "researcher",
                ["owner"] = new JsonObject { ["kind"] = "application" },
                ["parentNodeId"] = "__root__",
                ["rootPath"] = "subagents/researcher",
                ["sourceId"] = "subagents/researcher.ts",
                ["sourceKind"] = "module",
                ["summary"] = new JsonObject
                {
                    ["channels"] = 0,
                    ["connections"] = 0,
                    ["hooks"] = 0,
                    ["instructions"] = 1,
                    ["schedules"] = 0,
                    ["skills"] = 0,
                    ["tools"] = 0,
                },
            });
            root["subagents"]!["total"] = 1;
        });

    public static string WithBackingAndOptionalVariants() =>
        Mutate(static root =>
        {
            root["agent"]!["config"]!["binding"]!["backing"] = new JsonObject
            {
                ["kind"] = "programmatic",
                ["moduleId"] = "agent",
                ["registryId"] = "test-registry",
                ["revision"] = "1",
                ["semanticRevision"] = "1.0.0",
            };
            root["agent"]!["model"]!["routing"]!["byok"] = "required";
            root["sandbox"]!["backendKind"] = "local";
            root["sandbox"]!["description"] = "Local sandbox.";
            root["sandbox"]!["revalidationKey"] = "sandbox-v1";
            root["sandbox"]!["sourceHash"] = "sha256:test";
            root["sandbox"]!["binding"]!["backing"]!["extensionScope"] = new JsonObject
            {
                ["namespace"] = "test",
                ["sourceRoot"] = "/tmp/test-agent",
            };
            root["instrumentation"] = new JsonObject
            {
                ["logicalPath"] = "instrumentation.md",
                ["owner"] = new JsonObject { ["kind"] = "application" },
                ["sourceId"] = "instrumentation.md",
                ["sourceKind"] = "markdown",
            };
            root["connections"]!.AsArray().Add(new JsonObject
            {
                ["binding"] = CreateBinding("connections/search.ts"),
                ["connectionName"] = "search",
                ["description"] = "Search connection.",
                ["hasApproval"] = false,
                ["hasAuthorization"] = true,
                ["hasHeaders"] = false,
                ["logicalPath"] = "connections/search.ts",
                ["owner"] = new JsonObject { ["kind"] = "application" },
                ["protocol"] = "openapi",
                ["sourceId"] = "connections/search.ts",
                ["sourceKind"] = "module",
                ["toolFilter"] = new JsonObject
                {
                    ["tools"] = new JsonObject
                    {
                        ["allow"] = new JsonArray("search"),
                    },
                },
                ["url"] = "https://example.com/openapi.json",
            });
            root["channels"]!["shadowed"]!.AsArray().Add(new JsonObject
            {
                ["method"] = "GET",
                ["source"] = new JsonObject
                {
                    ["backing"] = new JsonObject
                    {
                        ["kind"] = "resource",
                        ["sourcePath"] = "channels/shadowed.ts",
                    },
                    ["layer"] = "application",
                    ["logicalPath"] = "channels/shadowed.ts",
                    ["owner"] = new JsonObject { ["kind"] = "application" },
                    ["sourceId"] = "channels/shadowed.ts",
                },
                ["urlPath"] = "/shadowed",
                ["winnerSourceId"] = "channels/winner.ts",
            });
        });

    public static string WithNormalizedRouteCollision() =>
        Mutate(static root =>
        {
            JsonArray routes = root["channels"]!["routes"]!.AsArray();
            routes.Add(CreateRoute("channels/first.ts", "/users/:id"));
            routes.Add(CreateRoute("channels/second.ts", "/users/[userId]"));
        });

    public static string WithMismatchedRemoteAgentTotal() =>
        Mutate(static root => root["remoteAgents"]!["total"] = 1);

    public static string WithBooleanToolInputSchema() =>
        Mutate(static root =>
        {
            JsonObject tool = CreateTool("boolean-schema", "tools/boolean-schema.ts");
            tool["inputSchema"] = true;
            root["tools"]!["static"]!.AsArray().Add(tool);
        });

    public static string WithModuleSourceMissingBinding() =>
        Mutate(static root =>
        {
            root["schedules"]!.AsArray().Add(new JsonObject
            {
                ["cron"] = "* * * * *",
                ["hasRun"] = true,
                ["logicalPath"] = "schedules/nightly.ts",
                ["name"] = "nightly",
                ["owner"] = new JsonObject { ["kind"] = "application" },
                ["sourceId"] = "schedules/nightly.ts",
                ["sourceKind"] = "module",
            });
        });

    public static string WithBindingLogicalPathMismatch() =>
        Mutate(static root =>
            root["agent"]!["config"]!["binding"]!["logicalPath"] = "other.ts");

    public static string WithBindingOwnerMismatch() =>
        Mutate(static root =>
            root["agent"]!["config"]!["binding"]!["owner"] = new JsonObject
            {
                ["feature"] = "default-tools",
                ["kind"] = "framework",
            });

    public static string WithUnknownRootProperty() =>
        Mutate(static root => root["unexpected"] = true);

    public static string WithoutChannels() =>
        Mutate(static root => root.Remove("channels"));

    private static string Mutate(Action<JsonObject> mutation)
    {
        JsonObject root = JsonNode.Parse(ValidJson)!.AsObject();
        mutation(root);
        return root.ToJsonString();
    }

    private static JsonObject CreateTool(string name, string logicalPath) =>
        new()
        {
            ["description"] = "A test tool.",
            ["hasAuth"] = false,
            ["hasExecute"] = true,
            ["hasModelOutputProjection"] = false,
            ["hasOutputSchema"] = false,
            ["inputSchema"] = new JsonObject(),
            ["logicalPath"] = logicalPath,
            ["name"] = name,
            ["owner"] = new JsonObject { ["kind"] = "application" },
            ["requiresApproval"] = false,
            ["sourceId"] = logicalPath,
            ["sourceKind"] = "module",
            ["binding"] = CreateBinding(logicalPath),
        };

    private static JsonObject CreateRoute(string logicalPath, string urlPath) =>
        new()
        {
            ["binding"] = CreateBinding(logicalPath),
            ["logicalPath"] = logicalPath,
            ["method"] = "GET",
            ["name"] = logicalPath,
            ["owner"] = new JsonObject { ["kind"] = "application" },
            ["sourceId"] = logicalPath,
            ["sourceKind"] = "module",
            ["urlPath"] = urlPath,
        };

    private static JsonObject CreateDynamicResolver(string slug) =>
        new()
        {
            ["binding"] = CreateBinding($"dynamic/{slug}.ts"),
            ["eventNames"] = new JsonArray("session.started"),
            ["logicalPath"] = $"dynamic/{slug}.ts",
            ["owner"] = new JsonObject { ["kind"] = "application" },
            ["slug"] = slug,
            ["sourceId"] = $"dynamic/{slug}.ts",
            ["sourceKind"] = "module",
        };

    private static JsonObject CreateBinding(string logicalPath) =>
        new()
        {
            ["backing"] = new JsonObject
            {
                ["externalDependencies"] = new JsonArray(),
                ["kind"] = "filesystem",
                ["sourcePath"] = $"/tmp/test-agent/agent/{logicalPath}",
            },
            ["logicalPath"] = logicalPath,
            ["owner"] = new JsonObject { ["kind"] = "application" },
        };
}
