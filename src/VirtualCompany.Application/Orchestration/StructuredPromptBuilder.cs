using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Context;

namespace VirtualCompany.Application.Orchestration;

public sealed class StructuredPromptBuilder : IPromptBuilder
{
    private const string EmptyMemoryText = "No retrieved memory was provided for this task.";
    private const string EmptyToolsText = "No tools are currently available. Do not claim to have tool access or execute external actions.";
    private const string DefaultPolicyInstruction = "Default-deny: do not execute tools, access data, or take external actions beyond the explicit tenant, agent, policy, and tool context in this prompt package.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public PromptBuildResult Build(PromptBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = request.RuntimeContext;
        var memorySnippets = ResolveMemorySnippets(request).ToList();
        var policyInstructions = ResolvePolicyInstructions(request).ToList();
        var toolSchemas = ResolveToolSchemas(request).ToList();
        var resolvedIdentity = ResolveIdentitySection(context);
        var sections = BuildSections(context, resolvedIdentity, memorySnippets, policyInstructions, toolSchemas);
        var promptPath = ResolvePromptPath(context);
        var systemPrompt = ComposeSystemPrompt(sections);
        var messages = BuildMessages(context, resolvedIdentity, systemPrompt, promptPath);
        var sourceReferenceIds = ResolveSourceReferenceIds(context, memorySnippets);
        var payload = BuildPayload(context, resolvedIdentity, sections, messages, memorySnippets, policyInstructions, toolSchemas, promptPath, request.DebugMode);
        var promptId = CreateDeterministicPromptId(context, sections, toolSchemas);

        return new PromptBuildResult(
            promptId,
            context.CorrelationId,
            messages,
            payload,
            systemPrompt,
            sections,
            resolvedIdentity,
            toolSchemas,
            sourceReferenceIds,
            ResolveBuiltAtUtc(context));
    }

    private static IReadOnlyList<PromptSectionDto> BuildSections(
        SingleAgentRuntimeContext context,
        PromptIdentitySectionDto resolvedIdentity,
        IReadOnlyList<MemorySnippet> memorySnippets,
        IReadOnlyList<PolicyInstruction> policyInstructions,
        IReadOnlyList<ToolSchemaDefinition> toolSchemas) =>
        [
            new(
                PromptSectionIds.RoleInstructions,
                "Role instructions",
                1,
                BuildRoleInstructions(context),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["companyId"] = JsonValue.Create(context.Company.CompanyId.ToString("N")),
                    ["agentId"] = JsonValue.Create(context.Agent.Id.ToString("N")),
                    ["roleName"] = JsonValue.Create(context.Agent.RoleName),
                    ["autonomyLevel"] = JsonValue.Create(context.Agent.AutonomyLevel)
                }),
            new(
                PromptSectionIds.Identity,
                "Structured identity",
                2,
                RenderIdentitySection(resolvedIdentity),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["role"] = JsonValue.Create(resolvedIdentity.Role),
                    ["seniority"] = JsonValue.Create(resolvedIdentity.Seniority),
                    ["businessResponsibility"] = JsonValue.Create(resolvedIdentity.BusinessResponsibility),
                    ["collaborationNorms"] = JsonSerializer.SerializeToNode(resolvedIdentity.CollaborationNorms, JsonOptions),
                    ["personalityTraits"] = JsonSerializer.SerializeToNode(resolvedIdentity.PersonalityTraits, JsonOptions),
                    ["sources"] = JsonSerializer.SerializeToNode(resolvedIdentity.Sources, JsonOptions)
                }),
            new(
                PromptSectionIds.CommunicationProfile,
                AgentCommunicationProfilePromptRenderer.SectionTitle,
                3,
                AgentCommunicationProfilePromptRenderer.Render(context.Agent.CommunicationProfile),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["profileSource"] = JsonValue.Create(context.Agent.CommunicationProfile.ProfileSource),
                    ["isFallback"] = JsonValue.Create(context.Agent.CommunicationProfile.IsFallback),
                    ["tone"] = JsonValue.Create(context.Agent.CommunicationProfile.Tone),
                    ["persona"] = JsonValue.Create(context.Agent.CommunicationProfile.Persona),
                    ["styleDirectives"] = JsonSerializer.SerializeToNode(context.Agent.CommunicationProfile.StyleDirectives, JsonOptions),
                    ["communicationRules"] = JsonSerializer.SerializeToNode(context.Agent.CommunicationProfile.CommunicationRules, JsonOptions),
                    ["forbiddenToneRules"] = JsonSerializer.SerializeToNode(context.Agent.CommunicationProfile.ForbiddenToneRules, JsonOptions)
                }),
            new(
                PromptSectionIds.CompanyContext,
                "Company context",
                4,
                BuildCompanyContext(context),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["companyId"] = JsonValue.Create(context.Company.CompanyId.ToString("N")),
                    ["taskId"] = JsonValue.Create(context.Task.Id.ToString("N")),
                    ["intent"] = JsonValue.Create(context.Intent)
                }),
            new(
                PromptSectionIds.Memory,
                "Memory",
                5,
                BuildMemoryContext(memorySnippets),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["memoryCount"] = JsonValue.Create(memorySnippets.Count)
                }),
            new(
                PromptSectionIds.Policies,
                "Policies",
                6,
                BuildPolicyContext(policyInstructions),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["policyInstructionCount"] = JsonValue.Create(policyInstructions.Count)
                }),
            new(
                PromptSectionIds.ToolSchemas,
                "Tool schemas",
                7,
                BuildToolSchemaContext(toolSchemas),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["toolCount"] = JsonValue.Create(toolSchemas.Count)
                })
        ];

    private static IReadOnlyList<PromptMessageDto> BuildMessages(SingleAgentRuntimeContext context, PromptIdentitySectionDto resolvedIdentity, string systemPrompt, string promptPath) =>
        [
            new(PromptRoles.System, "prompt_package", systemPrompt, new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["companyId"] = JsonValue.Create(context.Company.CompanyId.ToString("N")),
                ["agentId"] = JsonValue.Create(context.Agent.Id.ToString("N")),
                ["promptPath"] = JsonValue.Create(promptPath),
                ["resolvedIdentityRole"] = JsonValue.Create(resolvedIdentity.Role),
                ["identityProfileSource"] = JsonValue.Create(context.Agent.CommunicationProfile.ProfileSource),
                ["sectionOrder"] = JsonSerializer.SerializeToNode(PromptSectionIds.Ordered, JsonOptions)
            }),
            new(PromptRoles.User, "generation_input", BuildGenerationInput(context, promptPath), new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["taskId"] = JsonValue.Create(context.Task.Id.ToString("N")),
                ["promptPath"] = JsonValue.Create(promptPath),
                ["taskType"] = JsonValue.Create(context.Task.Type),
                ["intent"] = JsonValue.Create(context.Intent)
            })
        ];

    private static string ComposeSystemPrompt(IEnumerable<PromptSectionDto> sections)
    {
        var builder = new StringBuilder();
        foreach (var section in sections.OrderBy(static x => x.Order))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append("## ");
            builder.Append(section.Title);
            builder.AppendLine();
            builder.Append(section.Content);
        }

        return builder.ToString();
    }

    private static string BuildRoleInstructions(SingleAgentRuntimeContext context)
    {
        var builder = new StringBuilder();
        builder.Append("You are ");
        builder.Append(context.Agent.DisplayName);
        builder.Append(", ");
        builder.Append(context.Agent.RoleName);
        builder.Append(" for ");
        builder.Append(context.Company.Name);
        builder.AppendLine(".");
        builder.Append("Department: ");
        builder.AppendLine(context.Agent.Department);
        builder.Append("Seniority: ");
        builder.AppendLine(context.Agent.Seniority);
        builder.Append("Autonomy level: ");
        builder.AppendLine(context.Agent.AutonomyLevel);

        if (!string.IsNullOrWhiteSpace(context.Agent.RoleBrief))
        {
            builder.Append("Role brief: ");
            builder.AppendLine(context.Agent.RoleBrief.Trim());
        }

        builder.AppendLine("Use the task, company, memory, policy, and tool schema context exactly as provided. Do not expose hidden reasoning.");
        AppendJsonSection(builder, "Personality", context.Agent.Personality);
        AppendJsonSection(builder, "Objectives", context.Agent.Objectives);
        AppendJsonSection(builder, "KPIs", context.Agent.Kpis);
        return builder.ToString().TrimEnd();
    }

    private static string BuildCompanyContext(SingleAgentRuntimeContext context)
    {
        var builder = new StringBuilder();
        builder.Append("CompanyId: ");
        builder.AppendLine(context.Company.CompanyId.ToString("N"));
        builder.Append("Company name: ");
        builder.AppendLine(context.Company.Name);
        AppendOptionalLine(builder, "Industry", context.Company.Industry);
        AppendOptionalLine(builder, "Business type", context.Company.BusinessType);
        AppendOptionalLine(builder, "Timezone", context.Company.Timezone);
        AppendOptionalLine(builder, "Currency", context.Company.Currency);
        AppendOptionalLine(builder, "Language", context.Company.Language);
        AppendOptionalLine(builder, "Compliance region", context.Company.ComplianceRegion);

        builder.AppendLine();
        builder.Append("TaskId: ");
        builder.AppendLine(context.Task.Id.ToString("N"));
        builder.Append("Intent: ");
        builder.AppendLine(context.Intent);
        builder.Append("Task type: ");
        builder.AppendLine(context.Task.Type);
        builder.Append("Task title: ");
        builder.AppendLine(context.Task.Title);
        AppendOptionalLine(builder, "Task description", context.Task.Description);
        builder.Append("Task priority: ");
        builder.AppendLine(context.Task.Priority);
        AppendJsonSection(builder, "Task input payload", context.Task.InputPayload);

        if (context.GroundedContext is not null)
        {
            builder.AppendLine();
            builder.Append("Grounded retrievalId: ");
            builder.AppendLine(context.GroundedContext.RetrievalId.ToString("N"));
            builder.Append("Grounded retrieval intent: ");
            builder.AppendLine(context.GroundedContext.Company.RetrievalIntent);
            builder.Append("Read scopes: ");
            builder.AppendLine(JoinOrNone(context.GroundedContext.Company.ReadScopes));
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildMemoryContext(IReadOnlyList<MemorySnippet> memorySnippets)
    {
        if (memorySnippets.Count == 0)
        {
            return EmptyMemoryText;
        }

        var builder = new StringBuilder();
        foreach (var memory in memorySnippets.OrderByDescending(static x => x.RelevanceScore ?? double.MinValue).ThenBy(static x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(static x => x.SourceId, StringComparer.OrdinalIgnoreCase))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append("- ");
            builder.Append(memory.Title);
            builder.Append(" [");
            builder.Append(memory.SourceId);
            builder.Append(']');
            if (!string.IsNullOrWhiteSpace(memory.MemoryType))
            {
                builder.Append(" type=");
                builder.Append(memory.MemoryType);
            }

            if (!string.IsNullOrWhiteSpace(memory.Scope))
            {
                builder.Append(" scope=");
                builder.Append(memory.Scope);
            }

            if (memory.RelevanceScore.HasValue)
            {
                builder.Append(" score=");
                builder.Append(memory.RelevanceScore.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }

            builder.AppendLine();
            builder.Append("  ");
            builder.Append(memory.Content);
        }

        return builder.ToString().TrimEnd();
    }

    private static IReadOnlyList<MemorySnippet> ResolveMemorySnippets(PromptBuildRequest request)
    {
        if (request.MemorySnippets is not null)
        {
            return request.MemorySnippets;
        }

        var memories = request.RuntimeContext.GroundedContext?.Context.Memory.Items;
        if (memories is null || memories.Count == 0)
        {
            return [];
        }

        return memories
            .Select(static x => new MemorySnippet(
                x.MemoryId,
                x.Title,
                x.Summary,
                x.MemoryType,
                x.Scope,
                x.RelevanceScore ?? x.Salience))
            .ToList();
    }

    private static IReadOnlyList<PolicyInstruction> ResolvePolicyInstructions(PromptBuildRequest request)
    {
        var instructions = new List<PolicyInstruction>
        {
            new("system_safety_default_deny", DefaultPolicyInstruction, "system_safety", 0)
        };

        if (request.PolicyInstructions is not null)
        {
            instructions.AddRange(request.PolicyInstructions);
        }

        if (IsWorkerSubtask(request.RuntimeContext))
        {
            instructions.Add(new PolicyInstruction(
                "manager_worker_topology",
                "Manager-worker topology: this agent is executing a worker subtask. The worker must communicate results back through the parent task context, must not message or coordinate with other workers, and must not create subtasks or request further delegation unless explicitly allowed by task context.",
                "task_context",
                10));
        }

        return instructions;
    }

    private static IReadOnlyList<ToolSchemaDefinition> ResolveToolSchemas(PromptBuildRequest request)
    {
        if (request.ToolSchemas is not null)
        {
            return request.ToolSchemas;
        }

        return request.RuntimeContext.AvailableTools
            .OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(static x => new ToolSchemaDefinition(
                x.Name,
                x.SupportedActions,
                x.Scopes,
                x.PolicyMetadata))
            .ToList();
    }

    private static PromptIdentitySectionDto ResolveIdentitySection(SingleAgentRuntimeContext context)
    {
        var tenant = context.Company.IdentityPolicy;
        var task = ResolveTaskIdentityOverrides(context.Task.InputPayload);
        var agentBusinessResponsibility = FirstNonBlank(context.Agent.RoleBrief, context.GroundedContext?.Company.AgentRoleBrief);
        var agentCollaborationNorms = MergeIdentityList(
            context.Agent.CommunicationProfile.StyleDirectives,
            context.Agent.CommunicationProfile.CommunicationRules);
        var agentPersonalityTraits = MergeIdentityList(
            ExtractIdentityStrings(context.Agent.Personality),
            SplitIdentityText(context.Agent.CommunicationProfile.Persona),
            SplitIdentityText(context.Agent.CommunicationProfile.Tone));

        var role = FirstNonBlank(context.Agent.RoleName, tenant?.Role, task.Role, "Business agent");
        var seniority = FirstNonBlank(context.Agent.Seniority, tenant?.Seniority, task.Seniority, "Unspecified");
        var businessResponsibility = JoinIdentityParts(
            tenant?.BusinessResponsibility,
            agentBusinessResponsibility,
            task.BusinessResponsibility,
            $"Execute {context.Agent.Department} responsibilities for {context.Company.Name}.");
        var collaborationNorms = MergeIdentityList(
            tenant?.CollaborationNorms,
            agentCollaborationNorms,
            task.CollaborationNorms);
        var personalityTraits = MergeIdentityList(
            tenant?.PersonalityTraits,
            agentPersonalityTraits,
            task.PersonalityTraits);
        var additionalNotes = MergeIdentityList(
            SplitIdentityText(tenant?.AdditionalNotes),
            SplitIdentityText(task.AdditionalNotes));

        // Precedence is intentionally field-aware: protected system safety is outside identity,
        // agent identity wins scalar identity fields, and task context may refine composite fields.
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["role"] = ResolveScalarSource(context.Agent.RoleName, tenant?.Role, task.Role),
            ["seniority"] = ResolveScalarSource(context.Agent.Seniority, tenant?.Seniority, task.Seniority),
            ["businessResponsibility"] = BuildSourceLabel(tenant?.BusinessResponsibility, agentBusinessResponsibility, task.BusinessResponsibility),
            ["collaborationNorms"] = BuildSourceLabel(tenant?.CollaborationNorms, agentCollaborationNorms, task.CollaborationNorms),
            ["personalityTraits"] = BuildSourceLabel(tenant?.PersonalityTraits, agentPersonalityTraits, task.PersonalityTraits),
            ["additionalNotes"] = BuildSourceLabel(SplitIdentityText(tenant?.AdditionalNotes), null, SplitIdentityText(task.AdditionalNotes))
        };

        return new PromptIdentitySectionDto(
            role,
            seniority,
            businessResponsibility,
            collaborationNorms,
            personalityTraits,
            additionalNotes,
            sources);
    }

    private static string RenderIdentitySection(PromptIdentitySectionDto identity)
    {
        var builder = new StringBuilder();
        builder.Append("Role: ");
        builder.AppendLine(identity.Role);
        builder.Append("Seniority: ");
        builder.AppendLine(identity.Seniority);
        builder.Append("Business responsibility: ");
        builder.AppendLine(identity.BusinessResponsibility);
        builder.Append("Collaboration norms: ");
        builder.AppendLine(identity.CollaborationNorms.Count == 0 ? "none" : string.Join("; ", identity.CollaborationNorms));
        builder.Append("Personality traits: ");
        builder.AppendLine(identity.PersonalityTraits.Count == 0 ? "none" : string.Join("; ", identity.PersonalityTraits));

        if (identity.AdditionalNotes.Count > 0)
        {
            builder.Append("Additional notes: ");
            builder.AppendLine(string.Join("; ", identity.AdditionalNotes));
        }

        builder.AppendLine("Apply this resolved identity without changing protected system safety instructions.");
        return builder.ToString().TrimEnd();
    }

    private static string BuildPolicyContext(IReadOnlyList<PolicyInstruction> policyInstructions)
    {
        var builder = new StringBuilder();
        foreach (var instruction in policyInstructions
            .OrderBy(static x => x.Priority)
            .ThenBy(static x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append("- ");
            builder.Append(instruction.Id);
            builder.Append(" [");
            builder.Append(instruction.Source);
            builder.Append("]: ");
            builder.Append(instruction.Content.Trim());
        }

        return builder.ToString();
    }

    private static string BuildToolSchemaContext(IReadOnlyList<ToolSchemaDefinition> toolSchemas)
    {
        if (toolSchemas.Count == 0)
        {
            return EmptyToolsText;
        }

        var builder = new StringBuilder();
        foreach (var tool in toolSchemas.OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append("- ");
            builder.Append(tool.Name);
            builder.Append(" actions=");
            builder.Append(JoinOrNone(tool.SupportedActions));
            builder.Append(" scopes=");
            builder.Append(JoinOrNone(tool.Scopes));
            if (tool.Schema.Count > 0)
            {
                builder.Append(" schema=");
                builder.Append(JsonSerializer.Serialize(tool.Schema, JsonOptions));
            }
        }

        return builder.ToString();
    }

    private static string BuildGenerationInput(SingleAgentRuntimeContext context, string promptPath)
    {
        var builder = new StringBuilder();
        builder.Append("Generate ");
        builder.Append(promptPath switch
        {
            PromptGenerationPathValues.Chat => "a direct chat message",
            PromptGenerationPathValues.DocumentGeneration => "a document or generated artifact",
            _ => "task output and summary"
        });
        builder.AppendLine(" using only the prompt package context.");
        builder.Append("Task title: ");
        builder.AppendLine(context.Task.Title);
        AppendOptionalLine(builder, "Task description", context.Task.Description);
        builder.Append("Intent: ");
        builder.AppendLine(context.Intent);
        builder.AppendLine("Do not expose hidden reasoning or internal chain-of-thought.");
        return builder.ToString().TrimEnd();
    }

    private static Dictionary<string, JsonNode?> BuildPayload(
        SingleAgentRuntimeContext context,
        PromptIdentitySectionDto resolvedIdentity,
        IReadOnlyList<PromptSectionDto> sections,
        IReadOnlyList<PromptMessageDto> messages,
        IReadOnlyList<MemorySnippet> memorySnippets,
        IReadOnlyList<PolicyInstruction> policyInstructions,
        IReadOnlyList<ToolSchemaDefinition> toolSchemas,
        string promptPath,
        PromptDebugMode debugMode)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create("2026-04-14"),
            ["companyId"] = JsonValue.Create(context.Company.CompanyId.ToString("N")),
            ["agentId"] = JsonValue.Create(context.Agent.Id.ToString("N")),
            ["taskId"] = JsonValue.Create(context.Task.Id.ToString("N")),
            ["correlationId"] = JsonValue.Create(context.CorrelationId),
            ["promptPath"] = JsonValue.Create(promptPath),
            ["systemPrompt"] = JsonValue.Create(messages[0].Content),
            ["messages"] = JsonSerializer.SerializeToNode(messages, JsonOptions),
            ["sections"] = JsonSerializer.SerializeToNode(sections, JsonOptions),
            ["sectionOrder"] = JsonSerializer.SerializeToNode(PromptSectionIds.Ordered, JsonOptions),
            ["identitySection"] = JsonSerializer.SerializeToNode(resolvedIdentity, JsonOptions),
            ["renderedIdentitySection"] = JsonValue.Create(RenderIdentitySection(resolvedIdentity)),
            ["agentIdentityProfile"] = JsonSerializer.SerializeToNode(AgentCommunicationProfileJsonMapper.ToJsonDictionary(context.Agent.CommunicationProfile), JsonOptions),
            ["agentIdentityDirectives"] = JsonSerializer.SerializeToNode(AgentCommunicationProfilePromptRenderer.ToStableDirectiveMap(context.Agent.CommunicationProfile), JsonOptions),
            ["memorySnippets"] = JsonSerializer.SerializeToNode(memorySnippets, JsonOptions),
            ["policyInstructions"] = JsonSerializer.SerializeToNode(policyInstructions, JsonOptions),
            ["toolSchemas"] = JsonSerializer.SerializeToNode(toolSchemas, JsonOptions),
            ["sourceReferenceIds"] = JsonSerializer.SerializeToNode(ResolveSourceReferenceIds(context, memorySnippets), JsonOptions)
        };

        if (string.Equals(promptPath, PromptGenerationPathValues.DocumentGeneration, StringComparison.OrdinalIgnoreCase))
        {
            payload["documentGenerationContract"] = JsonSerializer.SerializeToNode(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["outputKind"] = "document_or_artifact",
                ["groundingRequirement"] = "use_prompt_package_context_only"
            }, JsonOptions);
        }

        if (IsWorkerSubtask(context))
        {
            payload["collaborationContract"] = JsonSerializer.SerializeToNode(new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["topology"] = JsonValue.Create("manager_worker"),
                ["allowedCommunicationPath"] = JsonValue.Create("manager_to_worker_to_manager"),
                ["workerToWorkerMessagingAllowed"] = JsonValue.Create(false),
                ["furtherDelegationAllowed"] = JsonValue.Create(TryGetBoolean(context.Task.InputPayload, "allowFurtherDelegation") is true)
            }, JsonOptions);
        }

        if (debugMode == PromptDebugMode.NonProduction)
        {
            payload["resolvedIdentity"] = JsonSerializer.SerializeToNode(resolvedIdentity, JsonOptions);
        }

        return payload;
    }

    private static string ResolvePromptPath(SingleAgentRuntimeContext context)
    {
        if (string.Equals(context.Intent, OrchestrationIntentValues.Chat, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context.Intent, OrchestrationIntentValues.GeneralAgentRequest, StringComparison.OrdinalIgnoreCase))
        {
            return PromptGenerationPathValues.Chat;
        }

        return string.Equals(context.Task.Type, PromptGenerationPathValues.DocumentGeneration, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(context.Intent, PromptGenerationPathValues.DocumentGeneration, StringComparison.OrdinalIgnoreCase)
            ? PromptGenerationPathValues.DocumentGeneration
            : PromptGenerationPathValues.TaskOutput;
    }

    private static IReadOnlyList<string> ResolveSourceReferenceIds(SingleAgentRuntimeContext context, IReadOnlyList<MemorySnippet> memorySnippets) =>
        context.GroundedContext?.Context.SourceReferences
            .Select(static x => x.SourceId)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? memorySnippets
            .Select(static x => x.SourceId)
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static Guid CreateDeterministicPromptId(
        SingleAgentRuntimeContext context,
        IReadOnlyList<PromptSectionDto> sections,
        IReadOnlyList<ToolSchemaDefinition> toolSchemas)
    {
        var fingerprint = JsonSerializer.Serialize(new
        {
            context.Company.CompanyId,
            AgentId = context.Agent.Id,
            TaskId = context.Task.Id,
            context.Intent,
            Sections = sections.Select(static x => new { x.Id, x.Order, x.Content }),
            Tools = toolSchemas.Select(static x => new { x.Name, x.SupportedActions, x.Scopes })
        }, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint));
        return new Guid(hash.Take(16).ToArray());
    }

    private static DateTime ResolveBuiltAtUtc(SingleAgentRuntimeContext context) =>
        context.GroundedContext?.GeneratedAtUtc ?? context.Task.UpdatedAt;

    private static PromptIdentityTaskOverrides ResolveTaskIdentityOverrides(IReadOnlyDictionary<string, JsonNode?> payload) =>
        new(
            TryGetString(payload, PromptIdentityPayloadKeys.Role, out var role) ? role : null,
            TryGetString(payload, PromptIdentityPayloadKeys.Seniority, out var seniority) ? seniority : null,
            TryGetString(payload, PromptIdentityPayloadKeys.BusinessResponsibility, out var businessResponsibility) ? businessResponsibility : null,
            TryGetStringList(payload, PromptIdentityPayloadKeys.CollaborationNorms),
            TryGetStringList(payload, PromptIdentityPayloadKeys.PersonalityTraits),
            TryGetString(payload, PromptIdentityPayloadKeys.AdditionalNotes, out var notes) ? notes : null);

    private static IReadOnlyList<string> ExtractIdentityStrings(IReadOnlyDictionary<string, JsonNode?> values)
    {
        var result = new List<string>();
        foreach (var pair in values.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                result.Add(text.Trim());
            }
            else if (pair.Value is JsonArray array)
            {
                result.AddRange(array.Select(static node => node?.GetValue<string>()).Where(static text => !string.IsNullOrWhiteSpace(text))!);
            }
        }

        return NormalizeIdentityList(result);
    }

    private static IReadOnlyList<string> MergeIdentityList(params IEnumerable<string>?[] sources) =>
        NormalizeIdentityList(sources.Where(static source => source is not null).SelectMany(static source => source!));

    private static IReadOnlyList<string> SplitIdentityText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : NormalizeIdentityList(value.Split([';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static IReadOnlyList<string> NormalizeIdentityList(IEnumerable<string> values) =>
        values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string JoinIdentityParts(string? tenant, string? agent, string? task, string fallback)
    {
        var parts = NormalizeIdentityList([tenant, agent, task]);
        return parts.Count == 0 ? fallback : string.Join(" ", parts);
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string ResolveScalarSource(string? agent, string? tenant, string? task)
    {
        if (!string.IsNullOrWhiteSpace(agent))
        {
            return "agent_identity";
        }

        if (!string.IsNullOrWhiteSpace(tenant))
        {
            return "tenant_policy";
        }

        return !string.IsNullOrWhiteSpace(task) ? "task_context" : "default";
    }

    private static string BuildSourceLabel(string? tenant, string? agent, string? task)
    {
        var sources = new List<string>();
        if (!string.IsNullOrWhiteSpace(tenant))
        {
            sources.Add("tenant_policy");
        }

        if (!string.IsNullOrWhiteSpace(agent))
        {
            sources.Add("agent_identity");
        }

        if (!string.IsNullOrWhiteSpace(task))
        {
            sources.Add("task_context");
        }

        return sources.Count == 0 ? "default" : string.Join("+", sources);
    }

    private static string BuildSourceLabel(IEnumerable<string>? tenant, IEnumerable<string>? agent, IEnumerable<string>? task)
    {
        var sources = new List<string>();
        if (tenant is not null && tenant.Any(static x => !string.IsNullOrWhiteSpace(x)))
        {
            sources.Add("tenant_policy");
        }

        if (agent is not null && agent.Any(static x => !string.IsNullOrWhiteSpace(x)))
        {
            sources.Add("agent_identity");
        }

        if (task is not null && task.Any(static x => !string.IsNullOrWhiteSpace(x)))
        {
            sources.Add("task_context");
        }

        return sources.Count == 0 ? "default" : string.Join("+", sources);
    }

    private static string BuildSourceLabel(IReadOnlyList<string>? tenant, IReadOnlyDictionary<string, JsonNode?> agent, IReadOnlyList<string>? task) =>
        BuildSourceLabel(tenant, ExtractIdentityStrings(agent), task);

    private static bool IsWorkerSubtask(SingleAgentRuntimeContext context) =>
        string.Equals(context.Task.Type, MultiAgentCollaborationTaskTypes.WorkerSubtask, StringComparison.OrdinalIgnoreCase) ||
        (TryGetString(context.Task.InputPayload, "collaborationRole", out var role) &&
         string.Equals(role, "worker_subtask", StringComparison.OrdinalIgnoreCase));

    private static void AppendOptionalLine(StringBuilder builder, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(label);
        builder.Append(": ");
        builder.AppendLine(value.Trim());
    }

    private static void AppendJsonSection(StringBuilder builder, string label, IReadOnlyDictionary<string, JsonNode?> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        builder.Append(label);
        builder.Append(": ");
        builder.AppendLine(JsonSerializer.Serialize(values, JsonOptions));
    }

    private static bool TryGetString(IReadOnlyDictionary<string, JsonNode?> payload, string key, out string value)
    {
        value = string.Empty;
        if (!payload.TryGetValue(key, out var node) || node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static IReadOnlyList<string> TryGetStringList(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is null)
        {
            return [];
        }

        if (node is JsonArray array)
        {
            return NormalizeIdentityList(array.Select(static item =>
                item is JsonValue value && value.TryGetValue<string>(out var parsed) ? parsed : null)!);
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
        {
            return SplitIdentityText(text);
        }

        return [];
    }

    private static bool? TryGetBoolean(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        return null;
    }

    private static string JoinOrNone(IEnumerable<string> values)
    {
        var normalized = values
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim())
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized.Count == 0 ? "none" : string.Join(",", normalized);
    }
}

public static class PromptSectionIds
{
    public const string RoleInstructions = "role_instructions";
    public const string Identity = "structured_identity";
    public const string CommunicationProfile = "communication_profile";
    public const string CompanyContext = "company_context";
    public const string Memory = "memory";
    public const string Policies = "policies";
    public const string ToolSchemas = "tool_schemas";

    public static readonly IReadOnlyList<string> Ordered =
    [
        RoleInstructions,
        Identity,
        CommunicationProfile,
        CompanyContext,
        Memory,
        Policies,
        ToolSchemas
    ];
}

public static class PromptGenerationPathValues
{
    public const string Chat = "chat";
    public const string TaskOutput = "task_output";
    public const string DocumentGeneration = "document_generation";
}
