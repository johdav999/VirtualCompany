using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        var sections = BuildSections(context, memorySnippets, policyInstructions, toolSchemas);
        var systemPrompt = ComposeSystemPrompt(sections);
        var messages = BuildMessages(context, systemPrompt);
        var sourceReferenceIds = ResolveSourceReferenceIds(context, memorySnippets);
        var payload = BuildPayload(context, sections, messages, memorySnippets, policyInstructions, toolSchemas);
        var promptId = CreateDeterministicPromptId(context, sections, toolSchemas);

        return new PromptBuildResult(
            promptId,
            context.CorrelationId,
            messages,
            payload,
            systemPrompt,
            sections,
            toolSchemas,
            sourceReferenceIds,
            ResolveBuiltAtUtc(context));
    }

    private static IReadOnlyList<PromptSectionDto> BuildSections(
        SingleAgentRuntimeContext context,
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
                PromptSectionIds.CompanyContext,
                "Company context",
                2,
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
                3,
                BuildMemoryContext(memorySnippets),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["memoryCount"] = JsonValue.Create(memorySnippets.Count)
                }),
            new(
                PromptSectionIds.Policies,
                "Policies",
                4,
                BuildPolicyContext(policyInstructions),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["policyInstructionCount"] = JsonValue.Create(policyInstructions.Count)
                }),
            new(
                PromptSectionIds.ToolSchemas,
                "Tool schemas",
                5,
                BuildToolSchemaContext(toolSchemas),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["toolCount"] = JsonValue.Create(toolSchemas.Count)
                })
        ];

    private static IReadOnlyList<PromptMessageDto> BuildMessages(SingleAgentRuntimeContext context, string systemPrompt) =>
        [
            new(PromptRoles.System, "prompt_package", systemPrompt, new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["companyId"] = JsonValue.Create(context.Company.CompanyId.ToString("N")),
                ["agentId"] = JsonValue.Create(context.Agent.Id.ToString("N")),
                ["sectionOrder"] = JsonSerializer.SerializeToNode(PromptSectionIds.Ordered, JsonOptions)
            }),
            new(PromptRoles.User, "task_input", BuildTaskInput(context), new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["taskId"] = JsonValue.Create(context.Task.Id.ToString("N")),
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

    private static string BuildPolicyContext(IReadOnlyList<PolicyInstruction> policyInstructions)
    {
        var instructions = policyInstructions.Count == 0
            ? [new PolicyInstruction("default_deny", DefaultPolicyInstruction, "prompt_builder_default", 0)]
            : policyInstructions;

        var builder = new StringBuilder();
        foreach (var policy in instructions.OrderBy(static x => x.Priority).ThenBy(static x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append("- ");
            builder.Append(policy.Id);
            builder.Append(" (");
            builder.Append(policy.Source);
            builder.Append("): ");
            builder.Append(policy.Content);
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
            builder.Append(": actions=");
            builder.Append(JoinOrNone(tool.SupportedActions));
            builder.Append("; scopes=");
            builder.Append(JoinOrNone(tool.Scopes));
            builder.Append("; schema=");
            builder.Append(JsonSerializer.Serialize(tool.Schema, JsonOptions));
        }

        return builder.ToString();
    }

    private static string BuildTaskInput(SingleAgentRuntimeContext context)
    {
        var builder = new StringBuilder();
        builder.Append("Execute the task using the prompt package context.");
        builder.AppendLine();
        builder.Append("Task: ");
        builder.AppendLine(context.Task.Title);
        builder.Append("Input payload: ");
        builder.Append(JsonSerializer.Serialize(context.Task.InputPayload, JsonOptions));
        return builder.ToString();
    }

    private static IEnumerable<MemorySnippet> ResolveMemorySnippets(PromptBuildRequest request)
    {
        if (request.MemorySnippets is { Count: > 0 })
        {
            return request.MemorySnippets;
        }

        return request.RuntimeContext.GroundedContext?.Context.Memory.Items.Select(static item =>
            new MemorySnippet(
                item.MemoryId,
                item.Title,
                item.Summary,
                item.MemoryType,
                item.Scope,
                item.RelevanceScore)) ?? [];
    }

    private static IEnumerable<PolicyInstruction> ResolvePolicyInstructions(PromptBuildRequest request)
    {
        if (request.PolicyInstructions is { Count: > 0 })
        {
            return request.PolicyInstructions;
        }

        var instructions = new List<PolicyInstruction>
        {
            new("default_deny", DefaultPolicyInstruction, "prompt_builder_default", 0),
            new("tenant_scope", "Use only context scoped to the request CompanyId and selected AgentId.", "orchestration_context", 10),
            new("tool_permissions", $"Tool permissions snapshot: {JsonSerializer.Serialize(request.RuntimeContext.Agent.ToolPermissions, JsonOptions)}", "agent_runtime_profile", 20),
            new("data_scopes", $"Data scopes snapshot: {JsonSerializer.Serialize(request.RuntimeContext.Agent.DataScopes, JsonOptions)}", "agent_runtime_profile", 30),
            new("approval_thresholds", $"Approval thresholds snapshot: {JsonSerializer.Serialize(request.RuntimeContext.Agent.ApprovalThresholds, JsonOptions)}", "agent_runtime_profile", 40),
            new("escalation_rules", $"Escalation rules snapshot: {JsonSerializer.Serialize(request.RuntimeContext.Agent.EscalationRules, JsonOptions)}", "agent_runtime_profile", 50)
        };

        if (IsManagerWorkerSubtask(request.RuntimeContext))
        {
            instructions.Add(new PolicyInstruction(
                "manager_worker_topology",
                "Manager-worker collaboration topology: this worker must complete only the assigned subtask, must not message or coordinate with other workers, must not create subtasks or request further delegation, and must return concise structured findings to the manager/coordinator for consolidation.",
                "manager_worker_collaboration",
                60));
        }

        return instructions;
    }

    private static bool IsManagerWorkerSubtask(SingleAgentRuntimeContext context) =>
        string.Equals(context.Task.Type, MultiAgentCollaborationTaskTypes.WorkerSubtask, StringComparison.OrdinalIgnoreCase) ||
        TryGetString(context.Task.InputPayload, "collaborationRole", out var role) &&
        string.Equals(role, "worker_subtask", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<ToolSchemaDefinition> ResolveToolSchemas(PromptBuildRequest request)
    {
        if (request.ToolSchemas is { Count: > 0 })
        {
            return request.ToolSchemas;
        }

        return request.RuntimeContext.AvailableTools.Select(static tool =>
            new ToolSchemaDefinition(
                tool.Name,
                tool.SupportedActions,
                tool.Scopes,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["policyMetadata"] = JsonSerializer.SerializeToNode(tool.PolicyMetadata, JsonOptions)
                }));
    }

    private static IReadOnlyList<string> ResolveSourceReferenceIds(SingleAgentRuntimeContext context, IReadOnlyList<MemorySnippet> memorySnippets)
    {
        var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var memory in memorySnippets)
        {
            if (!string.IsNullOrWhiteSpace(memory.SourceId))
            {
                sourceIds.Add(memory.SourceId);
            }
        }

        foreach (var sourceId in context.GroundedContext?.Context.SourceReferences.Select(static x => x.SourceId) ?? [])
        {
            if (!string.IsNullOrWhiteSpace(sourceId))
            {
                sourceIds.Add(sourceId);
            }
        }

        return sourceIds.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, JsonNode?> BuildPayload(
        SingleAgentRuntimeContext context,
        IReadOnlyList<PromptSectionDto> sections,
        IReadOnlyList<PromptMessageDto> messages,
        IReadOnlyList<MemorySnippet> memorySnippets,
        IReadOnlyList<PolicyInstruction> policyInstructions,
        IReadOnlyList<ToolSchemaDefinition> toolSchemas)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create("2026-04-13"),
            ["companyId"] = JsonValue.Create(context.Company.CompanyId.ToString("N")),
            ["agentId"] = JsonValue.Create(context.Agent.Id.ToString("N")),
            ["taskId"] = JsonValue.Create(context.Task.Id.ToString("N")),
            ["correlationId"] = JsonValue.Create(context.CorrelationId),
            ["sectionOrder"] = JsonSerializer.SerializeToNode(PromptSectionIds.Ordered, JsonOptions),
            ["sections"] = JsonSerializer.SerializeToNode(sections, JsonOptions),
            ["systemPrompt"] = JsonValue.Create(ComposeSystemPrompt(sections)),
            ["messages"] = JsonSerializer.SerializeToNode(messages, JsonOptions),
            ["memory"] = JsonSerializer.SerializeToNode(memorySnippets, JsonOptions),
            ["policies"] = JsonSerializer.SerializeToNode(policyInstructions, JsonOptions),
            ["tools"] = JsonSerializer.SerializeToNode(toolSchemas, JsonOptions),
            ["outputContract"] = JsonSerializer.SerializeToNode(new
            {
                userFacingOutput = "Required concise external answer.",
                taskArtifact = "Structured task output payload for persistence.",
                auditArtifacts = "Audit-ready action, outcome, metadata, data source, and correlation records.",
                sourceReferences = "Human-usable retrieval or context references when available.",
                toolExecutionReferences = "Structured tool execution references when available.",
                rationaleSummary = "Concise explanation summary only; do not expose hidden reasoning."
            }, JsonOptions)
        };

        if (IsManagerWorkerSubtask(context))
        {
            payload["collaborationContract"] = JsonSerializer.SerializeToNode(new
            {
                topology = "manager_worker",
                allowedCommunicationPath = "manager_to_worker_to_manager",
                workerToWorkerMessagingAllowed = false,
                furtherDelegationAllowed = false
            }, JsonOptions);
        }

        return payload;
    }

    private static Guid CreateDeterministicPromptId(
        SingleAgentRuntimeContext context,
        IReadOnlyList<PromptSectionDto> sections,
        IReadOnlyList<ToolSchemaDefinition> toolSchemas)
    {
        var material = JsonSerializer.Serialize(new
        {
            context.CorrelationId,
            CompanyId = context.Company.CompanyId,
            AgentId = context.Agent.Id,
            TaskId = context.Task.Id,
            context.Intent,
            Sections = sections.Select(static x => new { x.Id, x.Order, x.Content }),
            Tools = toolSchemas.Select(static x => new { x.Name, x.SupportedActions, x.Scopes, x.Schema })
        }, JsonOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return new Guid(hash.Take(16).ToArray());
    }

    private static DateTime ResolveBuiltAtUtc(SingleAgentRuntimeContext context) =>
        context.GroundedContext?.GeneratedAtUtc ?? context.Task.UpdatedAt;

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
    public const string CompanyContext = "company_context";
    public const string Memory = "memory";
    public const string Policies = "policies";
    public const string ToolSchemas = "tool_schemas";

    public static readonly IReadOnlyList<string> Ordered =
    [
        RoleInstructions,
        CompanyContext,
        Memory,
        Policies,
        ToolSchemas
    ];
}
