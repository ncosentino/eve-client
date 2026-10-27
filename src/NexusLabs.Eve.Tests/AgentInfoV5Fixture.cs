using System.Text.Json.Nodes;

namespace NexusLabs.Eve.Tests;

internal static class AgentInfoV5Fixture
{
    public static string Create(bool historical, Action<JsonObject> mutation) =>
        AgentInfoV4Fixture.VersionFiveWithSubagent(root =>
        {
            AddResolvers(root, historical);
            if (!historical)
            {
                root["memories"]![0]!.AsObject().Remove("visibility");
            }

            mutation(root);
        });

    public static string Legacy(int version, Action<JsonObject> mutation)
    {
        JsonObject root = JsonNode.Parse(
            version == 3
                ? AgentInfoV3Fixture.WithKernelEffectAndSubagent()
                : AgentInfoV4Fixture.WithSubagentSummary())!.AsObject();
        AddResolvers(root, true);
        mutation(root);
        return root.ToJsonString();
    }

    public static JsonObject GetObject(JsonObject root, string path)
    {
        JsonNode node = root;
        foreach (string segment in path.Split('.'))
        {
            node = node is JsonArray array
                ? array[int.Parse(segment, System.Globalization.CultureInfo.InvariantCulture)]!
                : node[segment]!;
        }

        return node.AsObject();
    }

    private static void AddResolvers(JsonObject root, bool historical)
    {
        JsonObject resolver = root["agent"]!["config"]!.DeepClone().AsObject();
        resolver["slug"] = "dynamic";
        if (historical)
        {
            resolver["eventNames"] = new JsonArray("onMessage");
        }

        root["agent"]!["model"] = new JsonObject
        {
            ["routing"] = new JsonObject
            {
                ["kind"] = "dynamic",
                ["resolver"] = resolver.DeepClone(),
            },
        };
        root["instructions"]!["dynamic"] = new JsonArray(resolver.DeepClone());
        root["skills"]!["dynamic"] = new JsonArray(resolver.DeepClone());
        root["tools"]!["dynamic"] = new JsonArray(resolver.DeepClone());
        root["subagents"]!["local"]![0]!["configResolver"] = resolver.DeepClone();
        JsonObject hook = resolver.DeepClone().AsObject();
        hook["eventNames"] = new JsonArray("onMessage");
        root["hooks"] = new JsonArray(hook);
    }
}
