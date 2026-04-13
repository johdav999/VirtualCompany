using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Context;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class PromptBuilderTests
{
    [Fact]
    public void Build_composes_required_sections_in_stable_order()
    {
        var builder = new StructuredPromptBuilder();
        var context = CreateRuntimeContext();

        var result = builder.Build(new PromptBuildRequest(context));

        Assert.Equal(
            [PromptSectionIds.RoleInstructions, PromptSectionIds.CompanyContext, PromptSectionIds.Memory, PromptSectionIds.Policies, PromptSectionIds.ToolSchemas],
            result.Sections.Select(x => x.Id).ToArray());
        Assert.Equal([1, 2, 3, 4, 5], result.Sections.Select(x => x.Order).ToArray());
        Assert.Contains("You are Nora Ledger", result.SystemPrompt);
        Assert.Contains("CompanyId: " + context.Company.CompanyId.ToString("N"), result.SystemPrompt);
        Assert.Contains("Revenue recognition decision", result.SystemPrompt);
        Assert.Contains("Default-deny", result.SystemPrompt);
        Assert.Contains("erp", result.SystemPrompt);
    }

    [Fact]
    public void Build_handles_missing_memory_with_explicit_fallback()
    {
        var builder = new StructuredPromptBuilder();
        var context = CreateRuntimeContext(groundedContext: null);

        var result = builder.Build(new PromptBuildRequest(context));

        var memory = Assert.Single(result.Sections, x => x.Id == PromptSectionIds.Memory);
        Assert.Contains("No retrieved memory was provided", memory.Content);
        Assert.Empty(result.SourceReferenceIds);
    }

    [Fact]
    public void Build_handles_missing_tool_schemas_with_explicit_fallback()
    {
        var builder = new StructuredPromptBuilder();
        var context = CreateRuntimeContext(availableTools: []);

        var result = builder.Build(new PromptBuildRequest(context));

        var tools = Assert.Single(result.Sections, x => x.Id == PromptSectionIds.ToolSchemas);
        Assert.Contains("No tools are currently available", tools.Content);
        Assert.Empty(result.ToolSchemas);
    }

    [Fact]
    public void Build_includes_policy_instructions()
    {
        var builder = new StructuredPromptBuilder();
        var context = CreateRuntimeContext();

        var result = builder.Build(new PromptBuildRequest(
            context,
            PolicyInstructions:
            [
                new PolicyInstruction("approval_required", "Escalate payments over the configured threshold.", "test", 1)
            ]));

        var policies = Assert.Single(result.Sections, x => x.Id == PromptSectionIds.Policies);
        Assert.Contains("approval_required", policies.Content);
        Assert.Contains("Escalate payments over the configured threshold.", policies.Content);
    }

    [Fact]
    public void Build_preserves_tenant_and_agent_scope()
    {
        var builder = new StructuredPromptBuilder();
        var context = CreateRuntimeContext();

        var result = builder.Build(new PromptBuildRequest(context));

        Assert.Equal(context.Company.CompanyId.ToString("N"), result.Payload["companyId"]!.GetValue<string>());
        Assert.Equal(context.Agent.Id.ToString("N"), result.Payload["agentId"]!.GetValue<string>());
        Assert.Contains("CompanyId: " + context.Company.CompanyId.ToString("N"), result.SystemPrompt);
        Assert.Contains("agent", result.Sections[0].Metadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_includes_manager_worker_topology_for_worker_subtasks()
    {
        var builder = new StructuredPromptBuilder();
        var context = CreateRuntimeContext(
            taskType: MultiAgentCollaborationTaskTypes.WorkerSubtask,
            inputPayload: Payload(
                ("collaborationRole", JsonValue.Create("worker_subtask")),
                ("allowFurtherDelegation", JsonValue.Create(false))));

        var result = builder.Build(new PromptBuildRequest(context));

        var policies = Assert.Single(result.Sections, x => x.Id == PromptSectionIds.Policies);
        Assert.Contains("manager_worker_topology", policies.Content);
        Assert.Contains("must not message or coordinate with other workers", policies.Content);
        Assert.Contains("must not create subtasks or request further delegation", policies.Content);
        Assert.NotNull(result.Payload["collaborationContract"]);
        Assert.Equal("manager_worker", result.Payload["collaborationContract"]!["topology"]!.GetValue<string>());
        Assert.Equal("manager_to_worker_to_manager", result.Payload["collaborationContract"]!["allowedCommunicationPath"]!.GetValue<string>());
        Assert.False(result.Payload["collaborationContract"]!["workerToWorkerMessagingAllowed"]!.GetValue<bool>());
    }

    [Fact]
    public void Build_is_deterministic_for_same_input()
    {
        var builder = new StructuredPromptBuilder();
        var request = new PromptBuildRequest(CreateRuntimeContext());

        var first = builder.Build(request);
        var second = builder.Build(request);

        Assert.Equal(first.PromptId, second.PromptId);
        Assert.Equal(first.BuiltAtUtc, second.BuiltAtUtc);
        Assert.Equal(first.SystemPrompt, second.SystemPrompt);
        Assert.Equal(Serialize(first.Payload), Serialize(second.Payload));
    }

    private static SingleAgentRuntimeContext CreateRuntimeContext(
        GroundedPromptContextDto? groundedContext = null,
        IReadOnlyList<ToolMetadataDto>? availableTools = null,
        string taskType = "finance_execution",
        Dictionary<string, JsonNode?>? inputPayload = null)
    {
        var companyId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var agentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var taskId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var timestamp = new DateTime(2026, 4, 13, 10, 0, 0, DateTimeKind.Utc);

        return new SingleAgentRuntimeContext(
            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
            "prompt-builder-test",
            new TaskDetailDto(
                taskId,
                companyId,
                taskType,
                "Pay approved invoice",
                "Run the approved payment action with tenant-scoped context.",
                "normal",
                "open",
                null,
                agentId,
                null,
                null,
                "user",
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                inputPayload ?? Payload(("invoiceId", JsonValue.Create("inv-100"))),
                [],
                null,
                null,
                timestamp,
                timestamp,
                null,
                new TaskAgentSummaryDto(agentId, "Nora Ledger", "active"),
                null),
            new AgentRuntimeProfileDto(
                agentId,
                companyId,
                "finance",
                "Nora Ledger",
                "Finance Manager",
                "Finance",
                "Senior",
                "active",
                "Execute finance operations through approved tools.",
                Payload(("style", JsonValue.Create("precise"))),
                Payload(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
                Payload(("targets", new JsonArray(JsonValue.Create("forecast_accuracy")))),
                Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
                Payload(("execute", new JsonArray(JsonValue.Create("payments")))),
                Payload(("approval", new JsonObject { ["expenseUsd"] = 1000 })),
                Payload(("escalateTo", JsonValue.Create("founder"))),
                [],
                [],
                true,
                timestamp,
                "level_2"),
            new CompanyRuntimeContext(
                companyId,
                "Company A",
                "Software",
                "B2B",
                "Europe/Stockholm",
                "SEK",
                "en",
                "EU"),
            groundedContext ?? CreateGroundedContext(companyId, agentId, timestamp),
            availableTools ??
            [
                new ToolMetadataDto(
                    "erp",
                    ["execute", "read"],
                    ["payments"],
                    Payload(("allowed", new JsonArray(JsonValue.Create("erp")))))
            ],
            OrchestrationIntentValues.ExecuteTask);
    }

    private static GroundedPromptContextDto CreateGroundedContext(Guid companyId, Guid agentId, DateTime timestamp)
    {
        var source = new GroundedContextItemSourceDto(
            "memory_item",
            "mem-001",
            "Revenue recognition decision",
            1,
            1,
            0.91,
            timestamp,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["scope"] = "payments"
            });

        var memory = new MemoryContextItemDto(
            "mem-001",
            "Revenue recognition decision",
            "Use the latest revenue recognition policy before booking payment adjustments.",
            "company_memory",
            "payments",
            0.93,
            timestamp,
            timestamp,
            null,
            0.91,
            source);

        return new GroundedPromptContextDto(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            timestamp,
            new CompanyContextSectionDto(
                companyId,
                agentId,
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                "Nora Ledger",
                "Finance Manager",
                "Execute finance operations through approved tools.",
                ["payments"],
                "single_agent_task_execution"),
            new NormalizedGroundedContextDto(
                [new GroundedContextSectionDescriptorDto("memory", "Memory", 1, 1)],
                new DocumentContextSectionDto("knowledge", "Knowledge", []),
                new MemoryContextSectionDto("memory", "Memory", [memory]),
                new RecentTaskContextSectionDto("recent_tasks", "Recent Task History", []),
                new RelevantRecordContextSectionDto("relevant_records", "Relevant Records", []),
                [
                    new RetrievalSourceReferenceDto(
                        "memory_item",
                        "mem-001",
                        "Revenue recognition decision",
                        null,
                        null,
                        null,
                        "memory",
                        "Memory",
                        1,
                        null,
                        1,
                        0.91,
                        "Use the latest revenue recognition policy before booking payment adjustments.",
                        timestamp,
                        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase))
                ],
                new GroundedContextSectionCountsDto(0, 1, 0, 0, 1)),
            new RetrievalAppliedFiltersDto(["payments"], true, true, true, true));
    }

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] properties)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in properties)
        {
            payload[key] = value?.DeepClone();
        }

        return payload;
    }

    private static string Serialize(Dictionary<string, JsonNode?> payload) =>
        JsonSerializer.Serialize(payload);
}
