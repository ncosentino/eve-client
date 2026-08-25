using System.Text.Json;

namespace NexusLabs.Eve;

internal static class EveAgentInfoV3Validator
{
    private const long MaximumSafeInteger = 9_007_199_254_740_991;

    private static readonly string[] AgentAllowedProperties =
        ["agentRoot", "appRoot", "config", "description", "model", "name", "nodeId"];
    private static readonly string[] AgentRequiredProperties =
        ["agentRoot", "appRoot", "config", "model", "name", "nodeId"];
    private static readonly string[] BindingProperties = ["backing", "logicalPath", "owner"];
    private static readonly string[] CapabilitiesProperties = ["devRoutes"];
    private static readonly string[] ChannelRouteAllowedProperties =
        ["adapterKind", "method", "name", "urlPath"];
    private static readonly string[] ChannelRouteRequiredProperties =
        ["method", "name", "urlPath"];
    private static readonly string[] ChannelsProperties = ["routes", "shadowed"];
    private static readonly string[] ChannelShadowedProperties =
        ["method", "source", "urlPath", "winnerSourceId"];
    private static readonly string[] ChannelShadowedSourceProperties =
        ["backing", "layer", "logicalPath", "owner", "sourceId"];
    private static readonly string[] CollectionProperties = ["dynamic", "static"];
    private static readonly string[] CompositionAllowedProperties =
        ["kind", "logicalPath", "owner", "sourceId", "winnerSourceId"];
    private static readonly string[] CompositionProperties = ["disabled", "shadowed"];
    private static readonly string[] CompositionRequiredProperties =
        ["kind", "logicalPath", "owner", "sourceId"];
    private static readonly string[] ConnectionAllowedProperties =
    [
        "connectionName",
        "description",
        "hasApproval",
        "hasAuthorization",
        "hasHeaders",
        "protocol",
        "toolFilter",
        "url",
    ];
    private static readonly string[] ConnectionRequiredProperties =
    [
        "connectionName",
        "description",
        "hasApproval",
        "hasAuthorization",
        "hasHeaders",
        "protocol",
        "url",
    ];
    private static readonly string[] DiagnosticsProperties =
        ["discoveryErrors", "discoveryWarnings"];
    private static readonly string[] DynamicResolverProperties = ["eventNames", "slug"];
    private static readonly string[] ExtensionScopeProperties = ["namespace", "sourceRoot"];
    private static readonly string[] FilesystemBackingAllowedProperties =
        ["extensionScope", "externalDependencies", "kind", "sourcePath"];
    private static readonly string[] FilesystemBackingRequiredProperties =
        ["externalDependencies", "kind", "sourcePath"];
    private static readonly string[] InstructionProperties =
        ["content", "name", "role"];
    private static readonly string[] KernelEffectAllowedProperties =
        ["action", "audience", "kind", "sourceId"];
    private static readonly string[] KernelEffectRequiredProperties =
        ["audience", "kind", "sourceId"];
    private static readonly string[] ModelAllowedProperties =
    [
        "contextWindowTokens",
        "endpoint",
        "id",
        "providerOptions",
        "reasoning",
        "routing",
        "source",
    ];
    private static readonly string[] ModelRequiredProperties = ["routing"];
    private static readonly string[] RemoteAgentAllowedProperties =
        ["description", "name", "nodeId", "parentNodeId", "url"];
    private static readonly string[] RemoteAgentRequiredProperties =
        ["description", "name", "nodeId", "parentNodeId"];
    private static readonly string[] RemoteAgentsProperties = ["entries", "total"];
    private static readonly string[] ResourceBackingProperties = ["kind", "sourcePath"];
    private static readonly string[] RootAllowedProperties =
    [
        "agent",
        "capabilities",
        "channels",
        "composition",
        "connections",
        "diagnostics",
        "hooks",
        "instrumentation",
        "instructions",
        "kernelEffects",
        "kind",
        "mode",
        "remoteAgents",
        "sandbox",
        "schedules",
        "skills",
        "subagents",
        "tools",
        "version",
        "workflow",
        "workspace",
    ];
    private static readonly string[] RootRequiredProperties =
    [
        "agent",
        "capabilities",
        "channels",
        "composition",
        "connections",
        "diagnostics",
        "hooks",
        "instructions",
        "kernelEffects",
        "kind",
        "mode",
        "remoteAgents",
        "sandbox",
        "schedules",
        "skills",
        "subagents",
        "tools",
        "version",
        "workflow",
        "workspace",
    ];
    private static readonly string[] GatewayRoutingAllowedProperties =
        ["byok", "kind", "target"];
    private static readonly string[] GatewayRoutingRequiredProperties = ["kind", "target"];
    private static readonly string[] ExternalRoutingProperties = ["kind", "provider"];
    private static readonly string[] DynamicRoutingProperties = ["kind", "resolver"];
    private static readonly string[] ProgrammaticBackingAllowedProperties =
        ["kind", "moduleId", "registryId", "revision", "semanticRevision"];
    private static readonly string[] ProgrammaticBackingRequiredProperties =
        ["kind", "moduleId", "registryId", "revision"];
    private static readonly string[] SandboxAllowedProperties =
    [
        "backendKind",
        "description",
        "hasBootstrap",
        "hasOnSession",
        "revalidationKey",
        "sourceHash",
    ];
    private static readonly string[] SandboxRequiredProperties = ["hasBootstrap", "hasOnSession"];
    private static readonly string[] ScheduleAllowedProperties =
        ["cron", "hasRun", "markdown", "name"];
    private static readonly string[] ScheduleRequiredProperties = ["cron", "hasRun", "name"];
    private static readonly string[] SkillAllowedProperties =
        ["description", "license", "markdown", "metadata", "name"];
    private static readonly string[] SkillRequiredProperties =
        ["description", "markdown", "name"];
    private static readonly string[] SourceAllowedProperties =
        ["binding", "exportName", "logicalPath", "owner", "sourceId", "sourceKind"];
    private static readonly string[] SourceRequiredProperties =
        ["logicalPath", "owner", "sourceId", "sourceKind"];
    private static readonly string[] SubagentAllowedProperties =
    [
        "configResolver",
        "description",
        "entryPath",
        "name",
        "nodeId",
        "parentNodeId",
        "rootPath",
        "summary",
    ];
    private static readonly string[] SubagentRequiredProperties =
        ["entryPath", "name", "nodeId", "parentNodeId", "rootPath", "summary"];
    private static readonly string[] SubagentSummaryProperties =
        ["channels", "connections", "hooks", "instructions", "schedules", "skills", "tools"];
    private static readonly string[] SubagentsProperties = ["local", "total"];
    private static readonly string[] ToolAllowedProperties =
    [
        "description",
        "hasAuth",
        "hasExecute",
        "hasModelOutputProjection",
        "hasOutputSchema",
        "inputSchema",
        "name",
        "outputSchema",
        "requiresApproval",
    ];
    private static readonly string[] ToolRequiredProperties =
    [
        "description",
        "hasAuth",
        "hasExecute",
        "hasModelOutputProjection",
        "hasOutputSchema",
        "inputSchema",
        "name",
        "requiresApproval",
    ];
    private static readonly string[] WorkflowAllowedProperties =
        ["enabled", "source", "toolName"];
    private static readonly string[] WorkflowRequiredProperties = ["enabled", "toolName"];
    private static readonly string[] WorkspaceProperties = ["resourceRoot", "rootEntries"];

    public static void Validate(JsonElement root)
    {
        ValidateExactObject(root, "$", RootRequiredProperties, RootAllowedProperties);
        ValidateAgent(RequireObject(root, "agent", "$"));
        ValidateCapabilities(RequireObject(root, "capabilities", "$"));
        ValidateChannels(RequireObject(root, "channels", "$"));
        ValidateComposition(RequireObject(root, "composition", "$"));
        ValidateConnections(RequireArray(root, "connections", "$"));
        ValidateDiagnostics(RequireObject(root, "diagnostics", "$"));
        ValidateHooks(RequireArray(root, "hooks", "$"));
        ValidateInstructions(RequireObject(root, "instructions", "$"));
        ValidateKernelEffects(RequireArray(root, "kernelEffects", "$"));
        ValidateRemoteAgents(RequireObject(root, "remoteAgents", "$"));
        ValidateSandbox(RequireObject(root, "sandbox", "$"));
        ValidateSchedules(RequireArray(root, "schedules", "$"));
        ValidateSkills(RequireObject(root, "skills", "$"));
        ValidateSubagents(RequireObject(root, "subagents", "$"));
        ValidateTools(RequireObject(root, "tools", "$"));
        ValidateWorkflow(RequireObject(root, "workflow", "$"));
        ValidateWorkspace(RequireObject(root, "workspace", "$"));
        if (root.TryGetProperty("instrumentation", out JsonElement instrumentation))
        {
            ValidateSource(instrumentation, "$.instrumentation");
        }
    }

    private static void ValidateAgent(JsonElement agent)
    {
        const string path = "$.agent";
        ValidateExactObject(
            agent,
            path,
            AgentRequiredProperties,
            AgentAllowedProperties);
        RequireString(agent, "agentRoot", path);
        RequireString(agent, "appRoot", path);
        ValidateSource(RequireObject(agent, "config", path), $"{path}.config");
        ValidateModel(RequireObject(agent, "model", path));
        RequireString(agent, "name", path);
        RequireString(agent, "nodeId", path);
        ValidateOptionalString(agent, "description", path);
    }

    private static void ValidateModel(JsonElement model)
    {
        const string path = "$.agent.model";
        ValidateExactObject(model, path, ModelRequiredProperties, ModelAllowedProperties);
        JsonElement routing = RequireObject(model, "routing", path);
        string routingPath = $"{path}.routing";
        string routingKind = RequireString(routing, "kind", routingPath);
        if (routingKind == "gateway")
        {
            ValidateExactObject(
                routing,
                routingPath,
                GatewayRoutingRequiredProperties,
                GatewayRoutingAllowedProperties);
            RequireString(routing, "target", routingPath);
            ValidateOptionalString(routing, "byok", routingPath);
            RequireString(model, "id", path);
        }
        else if (routingKind == "external")
        {
            ValidateExactObject(
                routing,
                routingPath,
                ExternalRoutingProperties,
                ExternalRoutingProperties);
            RequireString(routing, "provider", routingPath);
            RequireString(model, "id", path);
        }
        else if (routingKind == "dynamic")
        {
            ValidateExactObject(
                routing,
                routingPath,
                DynamicRoutingProperties,
                DynamicRoutingProperties);
            ValidateDynamicResolver(RequireObject(routing, "resolver", routingPath), $"{routingPath}.resolver");
        }
        else
        {
            ThrowInvalid($"{routingPath}.kind has unsupported value '{routingKind}'.");
        }

        if (model.TryGetProperty("endpoint", out JsonElement endpoint)
            && endpoint.ValueKind != JsonValueKind.Object)
        {
            ThrowInvalid($"{path}.endpoint must be an object.");
        }

        if (model.TryGetProperty("source", out JsonElement source))
        {
            ValidateSource(source, $"{path}.source");
        }
    }

    private static void ValidateCapabilities(JsonElement capabilities)
    {
        const string path = "$.capabilities";
        ValidateExactObject(
            capabilities,
            path,
            CapabilitiesProperties,
            CapabilitiesProperties);
        RequireBoolean(capabilities, "devRoutes", path);
    }

    private static void ValidateChannels(JsonElement channels)
    {
        const string path = "$.channels";
        ValidateExactObject(channels, path, ChannelsProperties, ChannelsProperties);
        JsonElement routes = RequireArray(channels, "routes", path);
        HashSet<string> routeIdentities = CreateIdentitySet();
        for (int routeIndex = 0; routeIndex < routes.GetArrayLength(); routeIndex++)
        {
            JsonElement route = routes[routeIndex];
            string routePath = $"{path}.routes[{routeIndex}]";
            ValidateSource(
                route,
                routePath,
                ChannelRouteRequiredProperties,
                ChannelRouteAllowedProperties);
            RequireString(route, "name", routePath);
            string method = RequireString(route, "method", routePath);
            string urlPath = RequireString(route, "urlPath", routePath);
            ValidateOptionalString(route, "adapterKind", routePath);
            string identity = $"{method} {NormalizeRoutePattern(urlPath)}";
            if (!routeIdentities.Add(identity))
            {
                ThrowInvalid($"{path}.routes contains duplicate normalized route '{identity}'.");
            }
        }

        JsonElement shadowed = RequireArray(channels, "shadowed", path);
        for (int shadowedIndex = 0;
             shadowedIndex < shadowed.GetArrayLength();
             shadowedIndex++)
        {
            JsonElement entry = shadowed[shadowedIndex];
            string entryPath = $"{path}.shadowed[{shadowedIndex}]";
            ValidateExactObject(
                entry,
                entryPath,
                ChannelShadowedProperties,
                ChannelShadowedProperties);
            RequireString(entry, "method", entryPath);
            RequireString(entry, "urlPath", entryPath);
            RequireString(entry, "winnerSourceId", entryPath);
            JsonElement source = RequireObject(entry, "source", entryPath);
            ValidateExactObject(
                source,
                $"{entryPath}.source",
                ChannelShadowedSourceProperties,
                ChannelShadowedSourceProperties);
            ValidateSourceBacking(
                RequireObject(source, "backing", $"{entryPath}.source"),
                $"{entryPath}.source.backing");
            RequireString(source, "layer", $"{entryPath}.source");
            RequireString(source, "logicalPath", $"{entryPath}.source");
            ValidateOwner(RequireObject(source, "owner", $"{entryPath}.source"));
            RequireString(source, "sourceId", $"{entryPath}.source");
        }
    }

    private static void ValidateComposition(JsonElement composition)
    {
        const string path = "$.composition";
        ValidateExactObject(composition, path, CompositionProperties, CompositionProperties);
        ValidateCompositionEntries(RequireArray(composition, "disabled", path), $"{path}.disabled");
        ValidateCompositionEntries(RequireArray(composition, "shadowed", path), $"{path}.shadowed");
    }

    private static void ValidateCompositionEntries(JsonElement entries, string path)
    {
        for (int index = 0; index < entries.GetArrayLength(); index++)
        {
            JsonElement entry = entries[index];
            string entryPath = $"{path}[{index}]";
            ValidateExactObject(
                entry,
                entryPath,
                CompositionRequiredProperties,
                CompositionAllowedProperties);
            RequireString(entry, "kind", entryPath);
            RequireString(entry, "logicalPath", entryPath);
            ValidateOwner(RequireObject(entry, "owner", entryPath));
            RequireString(entry, "sourceId", entryPath);
            ValidateOptionalString(entry, "winnerSourceId", entryPath);
        }
    }

    private static void ValidateConnections(JsonElement connections)
    {
        HashSet<string> identities = CreateIdentitySet();
        for (int index = 0; index < connections.GetArrayLength(); index++)
        {
            JsonElement connection = connections[index];
            string path = $"$.connections[{index}]";
            ValidateSource(
                connection,
                path,
                ConnectionRequiredProperties,
                ConnectionAllowedProperties);
            string identity = RequireString(connection, "connectionName", path);
            RequireString(connection, "description", path);
            RequireBoolean(connection, "hasApproval", path);
            RequireBoolean(connection, "hasAuthorization", path);
            RequireBoolean(connection, "hasHeaders", path);
            RequireString(connection, "protocol", path);
            RequireString(connection, "url", path);
            AddIdentity(identities, identity, "$.connections", "connectionName");
        }
    }

    private static void ValidateDiagnostics(JsonElement diagnostics)
    {
        const string path = "$.diagnostics";
        ValidateExactObject(diagnostics, path, DiagnosticsProperties, DiagnosticsProperties);
        RequireNonnegativeSafeInteger(diagnostics, "discoveryErrors", path);
        RequireNonnegativeSafeInteger(diagnostics, "discoveryWarnings", path);
    }

    private static void ValidateHooks(JsonElement hooks)
    {
        HashSet<string> identities = CreateIdentitySet();
        for (int index = 0; index < hooks.GetArrayLength(); index++)
        {
            JsonElement hook = hooks[index];
            string path = $"$.hooks[{index}]";
            ValidateDynamicResolver(hook, path);
            AddIdentity(
                identities,
                RequireString(hook, "slug", path),
                "$.hooks",
                "slug");
        }
    }

    private static void ValidateInstructions(JsonElement instructions)
    {
        const string path = "$.instructions";
        ValidateExactObject(instructions, path, CollectionProperties, CollectionProperties);
        JsonElement dynamicEntries = RequireArray(instructions, "dynamic", path);
        ValidateDynamicResolvers(dynamicEntries, $"{path}.dynamic");
        EnsureUnique(dynamicEntries, "slug", $"{path}.dynamic");

        JsonElement staticEntries = RequireArray(instructions, "static", path);
        for (int index = 0; index < staticEntries.GetArrayLength(); index++)
        {
            JsonElement instruction = staticEntries[index];
            string entryPath = $"{path}.static[{index}]";
            ValidateSource(
                instruction,
                entryPath,
                InstructionProperties,
                InstructionProperties);
            RequireString(instruction, "name", entryPath);
            RequireString(instruction, "content", entryPath);
            string role = RequireString(instruction, "role", entryPath);
            if (role is not "system" and not "user")
            {
                ThrowInvalid($"{entryPath}.role has unsupported value '{role}'.");
            }
        }

        EnsureUnique(staticEntries, "name", $"{path}.static");
    }

    private static void ValidateKernelEffects(JsonElement kernelEffects)
    {
        for (int index = 0; index < kernelEffects.GetArrayLength(); index++)
        {
            JsonElement effect = kernelEffects[index];
            string path = $"$.kernelEffects[{index}]";
            ValidateExactObject(
                effect,
                path,
                KernelEffectRequiredProperties,
                KernelEffectAllowedProperties);
            RequireString(effect, "sourceId", path);
            JsonElement audience = RequireArray(effect, "audience", path);
            ValidateEnumArray(
                audience,
                $"{path}.audience",
                [
                    "below-subagent-depth",
                    "delegated-task-child",
                    "requires-loadable-skill",
                    "requires-request-input",
                    "root-session",
                ]);
            string kind = RequireString(effect, "kind", path);
            if (kind is not "dispatch" and not "provider-tool" and not "request-input")
            {
                ThrowInvalid($"{path}.kind has unsupported value '{kind}'.");
            }

            if (effect.TryGetProperty("action", out JsonElement action))
            {
                if (action.ValueKind != JsonValueKind.String
                    || action.GetString() is not "subagent-call"
                        and not "task-cancel"
                        and not "task-update")
                {
                    ThrowInvalid($"{path}.action has an unsupported value.");
                }
            }
        }
    }

    private static void ValidateRemoteAgents(JsonElement remoteAgents)
    {
        const string path = "$.remoteAgents";
        ValidateExactObject(remoteAgents, path, RemoteAgentsProperties, RemoteAgentsProperties);
        JsonElement entries = RequireArray(remoteAgents, "entries", path);
        for (int index = 0; index < entries.GetArrayLength(); index++)
        {
            JsonElement entry = entries[index];
            string entryPath = $"{path}.entries[{index}]";
            ValidateSource(
                entry,
                entryPath,
                RemoteAgentRequiredProperties,
                RemoteAgentAllowedProperties);
            RequireString(entry, "name", entryPath);
            RequireString(entry, "description", entryPath);
            RequireString(entry, "nodeId", entryPath);
            RequireString(entry, "parentNodeId", entryPath);
            ValidateOptionalString(entry, "url", entryPath);
        }

        EnsureUnique(entries, "nodeId", $"{path}.entries");
        ValidateTotal(remoteAgents, "total", entries.GetArrayLength(), path);
    }

    private static void ValidateSandbox(JsonElement sandbox)
    {
        const string path = "$.sandbox";
        ValidateSource(
            sandbox,
            path,
            SandboxRequiredProperties,
            SandboxAllowedProperties);
        RequireBoolean(sandbox, "hasBootstrap", path);
        RequireBoolean(sandbox, "hasOnSession", path);
        ValidateOptionalString(sandbox, "backendKind", path);
        ValidateOptionalString(sandbox, "description", path);
        ValidateOptionalString(sandbox, "revalidationKey", path);
        ValidateOptionalString(sandbox, "sourceHash", path);
    }

    private static void ValidateSchedules(JsonElement schedules)
    {
        for (int index = 0; index < schedules.GetArrayLength(); index++)
        {
            JsonElement schedule = schedules[index];
            string path = $"$.schedules[{index}]";
            ValidateSource(
                schedule,
                path,
                ScheduleRequiredProperties,
                ScheduleAllowedProperties);
            RequireString(schedule, "name", path);
            RequireString(schedule, "cron", path);
            RequireBoolean(schedule, "hasRun", path);
            ValidateOptionalString(schedule, "markdown", path);
        }

        EnsureUnique(schedules, "name", "$.schedules");
    }

    private static void ValidateSkills(JsonElement skills)
    {
        const string path = "$.skills";
        ValidateExactObject(skills, path, CollectionProperties, CollectionProperties);
        JsonElement dynamicEntries = RequireArray(skills, "dynamic", path);
        ValidateDynamicResolvers(dynamicEntries, $"{path}.dynamic");
        EnsureUnique(dynamicEntries, "slug", $"{path}.dynamic");

        JsonElement staticEntries = RequireArray(skills, "static", path);
        for (int index = 0; index < staticEntries.GetArrayLength(); index++)
        {
            JsonElement skill = staticEntries[index];
            string entryPath = $"{path}.static[{index}]";
            ValidateSource(
                skill,
                entryPath,
                SkillRequiredProperties,
                SkillAllowedProperties);
            RequireString(skill, "name", entryPath);
            RequireString(skill, "description", entryPath);
            RequireString(skill, "markdown", entryPath);
            ValidateOptionalString(skill, "license", entryPath);
        }

        EnsureUnique(staticEntries, "name", $"{path}.static");
    }

    private static void ValidateSubagents(JsonElement subagents)
    {
        const string path = "$.subagents";
        ValidateExactObject(subagents, path, SubagentsProperties, SubagentsProperties);
        JsonElement local = RequireArray(subagents, "local", path);
        for (int index = 0; index < local.GetArrayLength(); index++)
        {
            JsonElement subagent = local[index];
            string entryPath = $"{path}.local[{index}]";
            ValidateSource(
                subagent,
                entryPath,
                SubagentRequiredProperties,
                SubagentAllowedProperties);
            RequireString(subagent, "name", entryPath);
            RequireString(subagent, "entryPath", entryPath);
            RequireString(subagent, "nodeId", entryPath);
            RequireString(subagent, "parentNodeId", entryPath);
            RequireString(subagent, "rootPath", entryPath);
            ValidateOptionalString(subagent, "description", entryPath);
            JsonElement summary = RequireObject(subagent, "summary", entryPath);
            ValidateExactObject(
                summary,
                $"{entryPath}.summary",
                SubagentSummaryProperties,
                SubagentSummaryProperties);
            foreach (string propertyName in SubagentSummaryProperties)
            {
                RequireNumber(summary, propertyName, $"{entryPath}.summary");
            }

            if (subagent.TryGetProperty("configResolver", out JsonElement resolver)
                )
            {
                ValidateDynamicResolver(resolver, $"{entryPath}.configResolver");
            }
        }

        EnsureUnique(local, "nodeId", $"{path}.local");
        ValidateTotal(subagents, "total", local.GetArrayLength(), path);
    }

    private static void ValidateTools(JsonElement tools)
    {
        const string path = "$.tools";
        ValidateExactObject(tools, path, CollectionProperties, CollectionProperties);
        JsonElement dynamicEntries = RequireArray(tools, "dynamic", path);
        ValidateDynamicResolvers(dynamicEntries, $"{path}.dynamic");
        EnsureUnique(dynamicEntries, "slug", $"{path}.dynamic");

        JsonElement staticEntries = RequireArray(tools, "static", path);
        for (int index = 0; index < staticEntries.GetArrayLength(); index++)
        {
            JsonElement tool = staticEntries[index];
            string entryPath = $"{path}.static[{index}]";
            ValidateSource(
                tool,
                entryPath,
                ToolRequiredProperties,
                ToolAllowedProperties);
            RequireString(tool, "name", entryPath);
            RequireString(tool, "description", entryPath);
            RequireBoolean(tool, "hasAuth", entryPath);
            RequireBoolean(tool, "hasExecute", entryPath);
            RequireBoolean(tool, "hasModelOutputProjection", entryPath);
            RequireBoolean(tool, "hasOutputSchema", entryPath);
            RequireObject(tool, "inputSchema", entryPath);
            RequireBoolean(tool, "requiresApproval", entryPath);
        }

        EnsureUnique(staticEntries, "name", $"{path}.static");
    }

    private static void ValidateWorkflow(JsonElement workflow)
    {
        const string path = "$.workflow";
        ValidateExactObject(
            workflow,
            path,
            WorkflowRequiredProperties,
            WorkflowAllowedProperties);
        bool enabled = RequireBoolean(workflow, "enabled", path);
        RequireString(workflow, "toolName", path);
        bool hasSource = workflow.TryGetProperty("source", out JsonElement source);
        if (enabled)
        {
            if (!hasSource)
            {
                ThrowInvalid($"{path}.source is required when the workflow is enabled.");
            }

            ValidateSource(source, $"{path}.source");
        }
        else if (hasSource)
        {
            ThrowInvalid($"{path}.source is not allowed when the workflow is disabled.");
        }
    }

    private static void ValidateWorkspace(JsonElement workspace)
    {
        const string path = "$.workspace";
        ValidateExactObject(workspace, path, WorkspaceProperties, WorkspaceProperties);
        RequireProperty(workspace, "resourceRoot", path);
        ValidateStringArray(RequireArray(workspace, "rootEntries", path), $"{path}.rootEntries");
    }

    private static void ValidateDynamicResolvers(JsonElement resolvers, string path)
    {
        for (int index = 0; index < resolvers.GetArrayLength(); index++)
        {
            JsonElement resolver = resolvers[index];
            ValidateDynamicResolver(resolver, $"{path}[{index}]");
        }
    }

    private static void ValidateDynamicResolver(JsonElement resolver, string path)
    {
        ValidateSource(
            resolver,
            path,
            DynamicResolverProperties,
            DynamicResolverProperties);
        ValidateStringArray(RequireArray(resolver, "eventNames", path), $"{path}.eventNames");
        RequireString(resolver, "slug", path);
    }

    private static void ValidateSource(
        JsonElement source,
        string path,
        ReadOnlySpan<string> requiredAdditionalProperties = default,
        ReadOnlySpan<string> allowedAdditionalProperties = default)
    {
        if (source.ValueKind != JsonValueKind.Object)
        {
            ThrowInvalid($"{path} must be an object.");
        }

        RequireProperties(source, path, SourceRequiredProperties);
        RequireProperties(source, path, requiredAdditionalProperties);
        using JsonElement.ObjectEnumerator properties = source.EnumerateObject();
        while (properties.MoveNext())
        {
            JsonProperty property = properties.Current;
            if (!Contains(SourceAllowedProperties, property.Name)
                && !Contains(allowedAdditionalProperties, property.Name))
            {
                ThrowInvalid($"{path} contains unknown property '{property.Name}'.");
            }
        }

        string logicalPath = RequireString(source, "logicalPath", path);
        ValidateOwner(RequireObject(source, "owner", path));
        RequireString(source, "sourceId", path);
        string sourceKind = RequireString(source, "sourceKind", path);
        if (sourceKind is not "markdown" and not "module" and not "skill-package")
        {
            ThrowInvalid($"{path}.sourceKind has unsupported value '{sourceKind}'.");
        }

        bool hasBinding = source.TryGetProperty("binding", out JsonElement binding);
        if (sourceKind == "module")
        {
            if (!hasBinding)
            {
                ThrowInvalid($"{path}.binding is required for a module source.");
            }

            ValidateBinding(binding, $"{path}.binding", logicalPath, source.GetProperty("owner"));
        }
        else if (hasBinding)
        {
            ThrowInvalid($"{path}.binding is only allowed for a module source.");
        }

        ValidateOptionalString(source, "exportName", path);
    }

    private static void ValidateBinding(
        JsonElement binding,
        string path,
        string logicalPath,
        JsonElement owner)
    {
        ValidateExactObject(binding, path, BindingProperties, BindingProperties);
        ValidateModuleBacking(RequireObject(binding, "backing", path), $"{path}.backing");
        string bindingLogicalPath = RequireString(binding, "logicalPath", path);
        if (!string.Equals(bindingLogicalPath, logicalPath, StringComparison.Ordinal))
        {
            ThrowInvalid($"{path}.logicalPath must match its source logicalPath.");
        }

        JsonElement bindingOwner = RequireObject(binding, "owner", path);
        ValidateOwner(bindingOwner);
        if (!string.Equals(
                GetOwnerIdentity(bindingOwner),
                GetOwnerIdentity(owner),
                StringComparison.Ordinal))
        {
            ThrowInvalid($"{path}.owner must match its source owner.");
        }
    }

    private static void ValidateModuleBacking(JsonElement backing, string path)
    {
        string kind = RequireString(backing, "kind", path);
        if (kind == "filesystem")
        {
            ValidateFilesystemBacking(backing, path);
        }
        else if (kind == "programmatic")
        {
            ValidateExactObject(
                backing,
                path,
                ProgrammaticBackingRequiredProperties,
                ProgrammaticBackingAllowedProperties);
            RequireString(backing, "moduleId", path);
            RequireString(backing, "registryId", path);
            RequireString(backing, "revision", path);
            ValidateOptionalString(backing, "semanticRevision", path);
        }
        else
        {
            ThrowInvalid($"{path}.kind has unsupported value '{kind}'.");
        }
    }

    private static void ValidateSourceBacking(JsonElement backing, string path)
    {
        string kind = RequireString(backing, "kind", path);
        if (kind == "resource")
        {
            ValidateExactObject(
                backing,
                path,
                ResourceBackingProperties,
                ResourceBackingProperties);
            RequireString(backing, "sourcePath", path);
            return;
        }

        ValidateModuleBacking(backing, path);
    }

    private static void ValidateFilesystemBacking(JsonElement backing, string path)
    {
        ValidateExactObject(
            backing,
            path,
            FilesystemBackingRequiredProperties,
            FilesystemBackingAllowedProperties);
        ValidateStringArray(
            RequireArray(backing, "externalDependencies", path),
            $"{path}.externalDependencies");
        RequireString(backing, "sourcePath", path);
        if (backing.TryGetProperty("extensionScope", out JsonElement extensionScope))
        {
            ValidateExactObject(
                extensionScope,
                $"{path}.extensionScope",
                ExtensionScopeProperties,
                ExtensionScopeProperties);
            RequireString(extensionScope, "namespace", $"{path}.extensionScope");
            RequireString(extensionScope, "sourceRoot", $"{path}.extensionScope");
        }
    }

    private static void ValidateOwner(JsonElement owner)
    {
        const string path = "owner";
        if (owner.ValueKind != JsonValueKind.Object)
        {
            ThrowInvalid($"{path} must be an object.");
        }

        string kind = RequireString(owner, "kind", path);
        switch (kind)
        {
            case "application":
                ValidateExactObject(owner, path, ["kind"], ["kind"]);
                break;
            case "framework":
                ValidateExactObject(owner, path, ["feature", "kind"], ["feature", "kind"]);
                RequireString(owner, "feature", path);
                break;
            case "extension":
                ValidateExactObject(
                    owner,
                    path,
                    ["kind", "namespace", "packageName"],
                    ["kind", "namespace", "packageName"]);
                RequireString(owner, "namespace", path);
                RequireString(owner, "packageName", path);
                break;
            default:
                ThrowInvalid($"{path}.kind has unsupported value '{kind}'.");
                break;
        }
    }

    private static string GetOwnerIdentity(JsonElement owner)
    {
        string kind = owner.GetProperty("kind").GetString()!;
        return kind switch
        {
            "application" => kind,
            "framework" => $"{kind}:{owner.GetProperty("feature").GetString()}",
            "extension" =>
                $"{kind}:{owner.GetProperty("namespace").GetString()}:" +
                owner.GetProperty("packageName").GetString(),
            _ => kind,
        };
    }

    private static void ValidateTotal(
        JsonElement parent,
        string propertyName,
        int expected,
        string path)
    {
        long total = RequireNonnegativeSafeInteger(parent, propertyName, path);
        if (total != expected)
        {
            ThrowInvalid($"{path}.{propertyName} must equal the number of entries.");
        }
    }

    private static void EnsureUnique(
        JsonElement entries,
        string propertyName,
        string path)
    {
        HashSet<string> identities = CreateIdentitySet();
        for (int index = 0; index < entries.GetArrayLength(); index++)
        {
            JsonElement entry = entries[index];
            string identity = RequireString(entry, propertyName, $"{path}[{index}]");
            AddIdentity(identities, identity, path, propertyName);
        }
    }

    private static HashSet<string> CreateIdentitySet()
    {
#pragma warning disable NLF0016 // Upstream JavaScript Set identity is deliberately case-sensitive.
        return new HashSet<string>(StringComparer.Ordinal);
#pragma warning restore NLF0016
    }

    private static void AddIdentity(
        HashSet<string> identities,
        string identity,
        string path,
        string propertyName)
    {
        if (!identities.Add(identity))
        {
            ThrowInvalid($"{path} contains duplicate {propertyName} '{identity}'.");
        }
    }

    private static string NormalizeRoutePattern(string path)
    {
        string trimmed = path.Trim('/');
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        string[] segments = trimmed.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            if (segment.StartsWith(':')
                || segment.Length >= 2
                && segment[0] == '['
                && segment[^1] == ']')
            {
                segments[index] = ":";
            }
        }

        return string.Join('/', segments);
    }

    private static void ValidateExactObject(
        JsonElement value,
        string path,
        ReadOnlySpan<string> requiredProperties,
        ReadOnlySpan<string> allowedProperties)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            ThrowInvalid($"{path} must be an object.");
        }

        RequireProperties(value, path, requiredProperties);
        using JsonElement.ObjectEnumerator properties = value.EnumerateObject();
        while (properties.MoveNext())
        {
            JsonProperty property = properties.Current;
            if (!Contains(allowedProperties, property.Name))
            {
                ThrowInvalid($"{path} contains unknown property '{property.Name}'.");
            }
        }
    }

    private static void RequireProperties(
        JsonElement value,
        string path,
        ReadOnlySpan<string> propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            RequireProperty(value, propertyName, path);
        }
    }

    private static JsonElement RequireProperty(
        JsonElement parent,
        string propertyName,
        string path)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
        {
            ThrowInvalid($"{path} is missing property '{propertyName}'.");
        }

        return value;
    }

    private static JsonElement RequireObject(
        JsonElement parent,
        string propertyName,
        string path)
    {
        JsonElement value = RequireProperty(parent, propertyName, path);
        if (value.ValueKind != JsonValueKind.Object)
        {
            ThrowInvalid($"{path}.{propertyName} must be an object.");
        }

        return value;
    }

    private static JsonElement RequireArray(
        JsonElement parent,
        string propertyName,
        string path)
    {
        JsonElement value = RequireProperty(parent, propertyName, path);
        if (value.ValueKind != JsonValueKind.Array)
        {
            ThrowInvalid($"{path}.{propertyName} must be an array.");
        }

        return value;
    }

    private static string RequireString(
        JsonElement parent,
        string propertyName,
        string path)
    {
        JsonElement value = RequireProperty(parent, propertyName, path);
        if (value.ValueKind != JsonValueKind.String)
        {
            ThrowInvalid($"{path}.{propertyName} must be a string.");
        }

        return value.GetString()!;
    }

    private static bool RequireBoolean(
        JsonElement parent,
        string propertyName,
        string path)
    {
        JsonElement value = RequireProperty(parent, propertyName, path);
        if (value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            ThrowInvalid($"{path}.{propertyName} must be a Boolean.");
        }

        return value.GetBoolean();
    }

    private static double RequireNumber(
        JsonElement parent,
        string propertyName,
        string path)
    {
        JsonElement value = RequireProperty(parent, propertyName, path);
        if (!value.TryGetDouble(out double result))
        {
            ThrowInvalid($"{path}.{propertyName} must be a number.");
        }

        return result;
    }

    private static long RequireNonnegativeSafeInteger(
        JsonElement parent,
        string propertyName,
        string path)
    {
        JsonElement value = RequireProperty(parent, propertyName, path);
        if (!value.TryGetInt64(out long result)
            || result < 0
            || result > MaximumSafeInteger)
        {
            ThrowInvalid($"{path}.{propertyName} must be a nonnegative safe integer.");
        }

        return result;
    }

    private static void ValidateOptionalString(
        JsonElement parent,
        string propertyName,
        string path)
    {
        if (parent.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind != JsonValueKind.String)
        {
            ThrowInvalid($"{path}.{propertyName} must be a string.");
        }
    }

    private static void ValidateStringArray(JsonElement values, string path)
    {
        for (int index = 0; index < values.GetArrayLength(); index++)
        {
            JsonElement value = values[index];
            if (value.ValueKind != JsonValueKind.String)
            {
                ThrowInvalid($"{path}[{index}] must be a string.");
            }
        }
    }

    private static void ValidateEnumArray(
        JsonElement values,
        string path,
        ReadOnlySpan<string> allowedValues)
    {
        for (int index = 0; index < values.GetArrayLength(); index++)
        {
            JsonElement value = values[index];
            if (value.ValueKind != JsonValueKind.String
                || !Contains(allowedValues, value.GetString()!))
            {
                ThrowInvalid($"{path}[{index}] has an unsupported value.");
            }
        }
    }

    private static bool Contains(ReadOnlySpan<string> values, string candidate)
    {
        foreach (string value in values)
        {
            if (string.Equals(value, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void ThrowInvalid(string detail) =>
        throw new EveProtocolException(
            $"The eve info route returned an invalid agent-info v3 payload: {detail}");
}
