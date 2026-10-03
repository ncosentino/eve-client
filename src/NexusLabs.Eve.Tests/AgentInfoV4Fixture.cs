using System.Text.Json.Nodes;

namespace NexusLabs.Eve.Tests;

internal static class AgentInfoV4Fixture
{
    public static string ValidJson =>
        Create(static _ => { });

    public static string VersionFiveWithSubagent() =>
        VersionFive(WithSubagentSummary(), static _ => { });

    public static string VersionFive(Action<JsonObject> mutation) =>
        VersionFive(WithProgrammaticBackingMetadata(), mutation);

    private static string VersionFive(string versionFourJson, Action<JsonObject> mutation) =>
        Mutate(
            versionFourJson,
            root =>
            {
                root["version"] = 5;
                root.Remove("workflow");
                JsonObject sandbox = root["sandbox"]!.AsObject();
                sandbox.Remove("backendKind");
                sandbox.Remove("description");
                sandbox.Remove("hasBootstrap");
                sandbox.Remove("hasOnSession");
                sandbox.Remove("revalidationKey");
                sandbox.Remove("sourceHash");
                sandbox["provider"] = "vercel";
                sandbox["environmentExportName"] = "environment";
                sandbox["revisionHash"] = "sha256:environment";
                root["agent"]!["outputSchema"] = new JsonObject { ["type"] = "object" };
                mutation(root);
            });

    public static string VersionFiveWithValue(string path, string? json) =>
        VersionFive(root =>
        {
            string[] segments = path.Split('.');
            JsonNode parent = root;
            for (int index = 0; index < segments.Length - 1; index++)
            {
                parent = parent is JsonArray array
                    ? array[int.Parse(segments[index], System.Globalization.CultureInfo.InvariantCulture)]!
                    : parent[segments[index]]!;
            }

            if (json is null)
            {
                parent.AsObject().Remove(segments[^1]);
            }
            else
            {
                parent[segments[^1]] = JsonNode.Parse(json);
            }
        });

    public static string WithoutMemories() =>
        Create(static root => root.Remove("memories"));

    public static string WithoutWorkflow() =>
        Create(static root => root.Remove("workflow"));

    public static string WithDuplicateMemorySlots() =>
        Create(static root =>
            root["memories"]!.AsArray().Add(
                CreateMemory("profile", "memory/secondary.ts")));

    public static string WithInvalidMemoryVisibility() =>
        Create(static root =>
            root["memories"]![0]!["visibility"] = "global");

    public static string WithMemoryToolsProperty() =>
        Create(static root =>
            root["memories"]![0]!["tools"] = false);

    public static string WithMemoryBindingOwnerMismatch() =>
        Create(static root =>
            root["memories"]![0]!["binding"]!["owner"] = new JsonObject
            {
                ["feature"] = "memory",
                ["kind"] = "framework",
            });

    public static string WithKernelEffectAudience(string audience) =>
        Create(
            root =>
            {
                root["kernelEffects"]!.AsArray().Add(new JsonObject
                {
                    ["audience"] = new JsonArray(audience),
                    ["kind"] = "dispatch",
                    ["sourceId"] = "tools/agent.ts",
                });
            });

    public static string WithSubagentSummary() =>
        Create(AgentInfoV3Fixture.WithKernelEffectAndSubagent(), static _ => { });

    public static string WithSubagentSummaryMissingMemories() =>
        Create(
            AgentInfoV3Fixture.WithKernelEffectAndSubagent(),
            static root =>
                root["subagents"]!["local"]![0]!["summary"]!.AsObject().Remove("memories"));

    public static string WithKernelEffectAction(string action) =>
        Create(
            AgentInfoV3Fixture.ValidJson,
            root =>
                root["kernelEffects"]!.AsArray().Add(new JsonObject
                {
                    ["action"] = action,
                    ["audience"] = new JsonArray("root-session"),
                    ["kind"] = "dispatch",
                    ["sourceId"] = "tools/durable.ts",
                }));

    public static string WithUnknownKernelEffectAction() =>
        WithKernelEffectAction("unknown-action");

    public static string WithProgrammaticBackingMetadata() =>
        Create(
            AgentInfoV3Fixture.WithBackingAndOptionalVariants(),
            static root =>
            {
                JsonObject backing = root["agent"]!["config"]!["binding"]!["backing"]!.AsObject();
                backing["dependencies"] = new JsonObject
                {
                    ["@vercel/ai"] = "7.0.58",
                };
                backing["parameters"] = new JsonObject
                {
                    ["enabled"] = true,
                    ["limit"] = 25,
                };
            });

    public static string WithInvalidProgrammaticDependency() =>
        Mutate(
            WithProgrammaticBackingMetadata(),
            static root =>
                root["agent"]!["config"]!["binding"]!["backing"]!
                    ["dependencies"]!["@vercel/ai"] = 7);

    public static string WithShadowedSourceDescriptorMissingForm() =>
        Create(
            AgentInfoV3Fixture.WithBackingAndOptionalVariants(),
            static root =>
                root["channels"]!["shadowed"]![0]!["source"]!.AsObject().Remove("form"));

    public static string WithInvalidShadowedSourceDescriptorForm() =>
        Create(
            AgentInfoV3Fixture.WithBackingAndOptionalVariants(),
            static root =>
                root["channels"]!["shadowed"]![0]!["source"]!["form"] = "inherited");

    public static string VersionThreeWithProgrammaticBackingMetadata() =>
        Mutate(
            AgentInfoV3Fixture.WithBackingAndOptionalVariants(),
            static root =>
            {
                JsonObject backing = root["agent"]!["config"]!["binding"]!["backing"]!.AsObject();
                backing["dependencies"] = new JsonObject
                {
                    ["@vercel/ai"] = "7.0.58",
                };
                backing["parameters"] = new JsonObject();
            });

    public static string VersionThreeWithSourceDescriptorForm() =>
        Mutate(
            AgentInfoV3Fixture.WithBackingAndOptionalVariants(),
            static root =>
                root["channels"]!["shadowed"]![0]!["source"]!["form"] = "direct");

    private static string Create(Action<JsonObject> mutation) =>
        Create(AgentInfoV3Fixture.ValidJson, mutation);

    private static string Create(string versionThreeJson, Action<JsonObject> mutation)
    {
        JsonObject root = JsonNode.Parse(versionThreeJson)!.AsObject();
        root["version"] = 4;
        root["memories"] = new JsonArray
        {
            CreateMemory("profile", "memory/profile.ts"),
        };
        AddSubagentMemoryCounts(root);
        AddSourceDescriptorForms(root);
        mutation(root);
        return root.ToJsonString();
    }

    private static string Mutate(string json, Action<JsonObject> mutation)
    {
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        mutation(root);
        return root.ToJsonString();
    }

    private static void AddSubagentMemoryCounts(JsonObject root)
    {
        JsonArray subagents = root["subagents"]!["local"]!.AsArray();
        for (int index = 0; index < subagents.Count; index++)
        {
            subagents[index]!["summary"]!["memories"] = 0;
        }
    }

    private static void AddSourceDescriptorForms(JsonObject root)
    {
        JsonArray shadowedRoutes = root["channels"]!["shadowed"]!.AsArray();
        for (int index = 0; index < shadowedRoutes.Count; index++)
        {
            shadowedRoutes[index]!["source"]!["form"] = "direct";
        }
    }

    private static JsonObject CreateMemory(string slot, string logicalPath) =>
        new()
        {
            ["binding"] = CreateBinding(logicalPath),
            ["description"] = "Stores durable profile details.",
            ["logicalPath"] = logicalPath,
            ["owner"] = new JsonObject { ["kind"] = "application" },
            ["slot"] = slot,
            ["sourceId"] = logicalPath,
            ["sourceKind"] = "module",
            ["visibility"] = "session",
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
