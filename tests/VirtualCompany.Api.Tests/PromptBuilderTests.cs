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
            [PromptSectionIds.RoleInstructions, PromptSectionIds.Identity, PromptSectionIds.CommunicationProfile, PromptSectionIds.CompanyContext, PromptSectionIds.Memory, PromptSectionIds.Policies, PromptSectionIds.ToolSchemas],
            result.Sections.Select(x => x.Id).ToArray());
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], result.Sections.Select(x => x.Order).ToArray());
        Assert.Contains("You are Nora Ledger", result.SystemPrompt);
        Assert.Contains("## Structured identity", result.SystemPrompt);
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
        Assert.Contains("agentId", result.Sections[0].Metadata.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_includes_communication_profile_before_model_invocation()
    {
        var builder = new StructuredPromptBuilder();
        var context = CreateRuntimeContext();

        var result = builder.Build(new PromptBuildRequest(context));

        var section = Assert.Single(result.Sections, x => x.Id == PromptSectionIds.CommunicationProfile);
        Assert.Equal(3, section.Order);
        Assert.Contains("Tone: concise executive", section.Content);
        Assert.Contains("Persona: pragmatic operator", section.Content);
        Assert.Contains("Use bullets for decisions", section.Content);
        Assert.Contains("Do not use flippant", section.Content);
        Assert.Contains("Agent identity profile", result.SystemPrompt);
        Assert.NotNull(result.Payload["agentIdentityProfile"]);
        Assert.Equal("concise executive", result.Payload["agentIdentityProfile"]!["tone"]!.GetValue<string>());
        Assert.Equal("pragmatic operator", result.Payload["agentIdentityProfile"]!["persona"]!.GetValue<string>());
        Assert.Equal("explicit", result.Payload["agentIdentityProfile"]!["profileSource"]!.GetValue<string>());
        Assert.False(result.Payload["agentIdentityProfile"]!["isFallback"]!.GetValue<bool>());
    }

    [Fact]
    public void Build_renders_same_identity_directives_for_chat_task_and_document_paths()
    {
        var builder = new StructuredPromptBuilder();
        var profile = new AgentCommunicationProfileDto(
            "calm operator",
            "risk-aware specialist",
            ["Lead with the decision"],
            ["Avoid unsupported claims"],
            ["Do not use flippant"],
            AgentCommunicationProfileSources.Explicit,
            false);

        var chat = builder.Build(new PromptBuildRequest(CreateRuntimeContext(
            intent: OrchestrationIntentValues.Chat,
            communicationProfile: profile)));
        var task = builder.Build(new PromptBuildRequest(CreateRuntimeContext(
            intent: OrchestrationIntentValues.ExecuteTask,
            communicationProfile: profile)));
        var document = builder.Build(new PromptBuildRequest(CreateRuntimeContext(
            taskType: "document_generation",
            intent: "document_generation",
            communicationProfile: profile)));

        var chatIdentity = Assert.Single(chat.Sections, x => x.Id == PromptSectionIds.CommunicationProfile).Content;
        var taskIdentity = Assert.Single(task.Sections, x => x.Id == PromptSectionIds.CommunicationProfile).Content;
        var documentIdentity = Assert.Single(document.Sections, x => x.Id == PromptSectionIds.CommunicationProfile).Content;
        Assert.Equal(chatIdentity, taskIdentity);
        Assert.Equal(taskIdentity, documentIdentity);
        Assert.Equal(PromptGenerationPathValues.Chat, chat.Payload["promptPath"]!.GetValue<string>());
        Assert.Equal(PromptGenerationPathValues.TaskOutput, task.Payload["promptPath"]!.GetValue<string>());
        Assert.Equal(PromptGenerationPathValues.DocumentGeneration, document.Payload["promptPath"]!.GetValue<string>());
        Assert.Contains("direct chat message", chat.Messages[1].Content);
        Assert.Contains("task output and summary", task.Messages[1].Content);
        Assert.Contains("document or generated artifact", document.Messages[1].Content);
        Assert.NotNull(document.Payload["documentGenerationContract"]);
        Assert.Equal("calm operator", chat.Payload["agentIdentityDirectives"]!["tone"]!.GetValue<string>());
        Assert.Equal("calm operator", task.Payload["agentIdentityDirectives"]!["tone"]!.GetValue<string>());
        Assert.Equal("calm operator", document.Payload["agentIdentityDirectives"]!["tone"]!.GetValue<string>());
        Assert.True(chat.Messages[0].Role == PromptRoles.System);
        Assert.True(task.Messages[0].Role == PromptRoles.System);
        Assert.True(document.Messages[0].Role == PromptRoles.System);
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

    [Fact]
    public void Build_includes_structured_identity_section_with_required_fields()
    {
        var builder = new StructuredPromptBuilder();

        var result = builder.Build(new PromptBuildRequest(CreateRuntimeContext()));

        var identity = Assert.Single(result.Sections, x => x.Id == PromptSectionIds.Identity);
        Assert.Contains("Role: Finance Manager", identity.Content);
        Assert.Contains("Seniority: Senior", identity.Content);
        Assert.Contains("Business responsibility:", identity.Content);
        Assert.Contains("Collaboration norms:", identity.Content);
        Assert.Contains("Personality traits:", identity.Content);
        Assert.Equal("Finance Manager", result.ResolvedIdentity.Role);
        Assert.Equal("Senior", result.ResolvedIdentity.Seniority);
    }

    [Fact]
    public void Build_produces_different_prompt_payloads_for_different_agent_identities()
    {
        var builder = new StructuredPromptBuilder();

        var finance = builder.Build(new PromptBuildRequest(CreateRuntimeContext()));
        var support = builder.Build(new PromptBuildRequest(CreateRuntimeContext(
            roleName: "Customer Support Lead",
            department: "Support",
            seniority: "Lead",
            roleBrief: "Own customer escalation triage and resolution quality.",
            communicationProfile: new AgentCommunicationProfileDto(
                "warm and precise",
                "customer advocate",
                ["Acknowledge customer impact"],
                ["Escalate urgent account risk"],
                ["Do not speculate"],
                AgentCommunicationProfileSources.Explicit,
                false))));

        Assert.NotEqual(finance.SystemPrompt, support.SystemPrompt);
        Assert.NotEqual(finance.Payload["systemPrompt"]!.GetValue<string>(), support.Payload["systemPrompt"]!.GetValue<string>());
        Assert.Contains("Finance Manager", finance.SystemPrompt);
        Assert.Contains("Customer Support Lead", support.SystemPrompt);
    }

    [Fact]
    public void Build_preserves_shared_safety_policy_across_identity_variants()
    {
        var builder = new StructuredPromptBuilder();

        var finance = builder.Build(new PromptBuildRequest(CreateRuntimeContext()));
        var support = builder.Build(new PromptBuildRequest(CreateRuntimeContext(roleName: "Support Agent", seniority: "Mid")));

        var financePolicies = Assert.Single(finance.Sections, x => x.Id == PromptSectionIds.Policies).Content;
        var supportPolicies = Assert.Single(support.Sections, x => x.Id == PromptSectionIds.Policies).Content;
        Assert.Equal(financePolicies, supportPolicies);
        Assert.Contains("Default-deny", financePolicies);
    }

    [Fact]
    public void Build_resolves_identity_precedence_without_erasing_tenant_policy()
    {
        var builder = new StructuredPromptBuilder();
        var tenantPolicy = new PromptIdentityPolicyDto(
            Role: "Tenant Generalist",
            Seniority: "Tenant Seniority",
            BusinessResponsibility: "Maintain tenant operating discipline.",
            CollaborationNorms: ["Keep legal context visible"],
            PersonalityTraits: ["risk-aware"],
            AdditionalNotes: "Tenant policy is mandatory.");
        var context = CreateRuntimeContext(
            identityPolicy: tenantPolicy,
            inputPayload: Payload(
                (PromptIdentityPayloadKeys.Role, JsonValue.Create("Task Role Attempt")),
                (PromptIdentityPayloadKeys.Seniority, JsonValue.Create("Task Seniority Attempt")),
                (PromptIdentityPayloadKeys.BusinessResponsibility, JsonValue.Create("Refine the response for invoice execution.")),
                (PromptIdentityPayloadKeys.CollaborationNorms, new JsonArray(JsonValue.Create("Coordinate with AP owner"))),
                (PromptIdentityPayloadKeys.PersonalityTraits, new JsonArray(JsonValue.Create("calm under pressure")))));

        var result = builder.Build(new PromptBuildRequest(context));

        Assert.Equal("Finance Manager", result.ResolvedIdentity.Role);
        Assert.Equal("Senior", result.ResolvedIdentity.Seniority);
        Assert.Contains("Maintain tenant operating discipline.", result.ResolvedIdentity.BusinessResponsibility);
        Assert.Contains("Execute finance operations through approved tools.", result.ResolvedIdentity.BusinessResponsibility);
        Assert.Contains("Refine the response for invoice execution.", result.ResolvedIdentity.BusinessResponsibility);
        Assert.Contains("Keep legal context visible", result.ResolvedIdentity.CollaborationNorms);
        Assert.Contains("Coordinate with AP owner", result.ResolvedIdentity.CollaborationNorms);
        Assert.Contains("risk-aware", result.ResolvedIdentity.PersonalityTraits);
        Assert.Contains("calm under pressure", result.ResolvedIdentity.PersonalityTraits);
    }

    [Fact]
    public void Build_exposes_resolved_identity_debug_payload_only_when_enabled()
    {
        var builder = new StructuredPromptBuilder();
        var production = builder.Build(new PromptBuildRequest(CreateRuntimeContext(), DebugMode: PromptDebugMode.Suppressed));
        var nonProduction = builder.Build(new PromptBuildRequest(CreateRuntimeContext(), DebugMode: PromptDebugMode.NonProduction));

        Assert.False(production.Payload.ContainsKey("resolvedIdentity"));
        Assert.True(nonProduction.Payload.ContainsKey("resolvedIdentity"));
        Assert.Equal("Finance Manager", nonProduction.Payload["resolvedIdentity"]!["role"]!.GetValue<string>());
    }

    [Fact]
    public void Build_does_not_allow_empty_task_identity_values_to_override_agent_identity()
    {
        var builder = new StructuredPromptBuilder();
        var context = CreateRuntimeContext(
            identityPolicy: new PromptIdentityPolicyDto(
                Role: "Tenant Role",
                Seniority: "Tenant Seniority",
                BusinessResponsibility: "Tenant baseline responsibility."),
            inputPayload: Payload(
                (PromptIdentityPayloadKeys.Role, JsonValue.Create(" ")),
                (PromptIdentityPayloadKeys.Seniority, JsonValue.Create("")),
                (PromptIdentityPayloadKeys.BusinessResponsibility, JsonValue.Create("Task refinement."))));

        var result = builder.Build(new PromptBuildRequest(context, DebugMode: PromptDebugMode.NonProduction));

        Assert.Equal("Finance Manager", result.ResolvedIdentity.Role);
        Assert.Equal("Senior", result.ResolvedIdentity.Seniority);
        Assert.Contains("Tenant baseline responsibility.", result.ResolvedIdentity.BusinessResponsibility);
        Assert.Contains("Execute finance operations through approved tools.", result.ResolvedIdentity.BusinessResponsibility);
        Assert.Contains("Task refinement.", result.ResolvedIdentity.BusinessResponsibility);
        Assert.Equal("agent_identity", result.Payload["resolvedIdentity"]!["sources"]!["role"]!.GetValue<string>());
        Assert.Equal("agent_identity", result.Payload["resolvedIdentity"]!["sources"]!["seniority"]!.GetValue<string>());
    }

    [Fact]
    public void Build_merges_identity_lists_in_stable_order_with_deduplication()
    {
        var builder = new StructuredPromptBuilder();
        var context = CreateRuntimeContext(
            identityPolicy: new PromptIdentityPolicyDto(
                CollaborationNorms: ["Cite assumptions", "Keep legal context visible"],
                PersonalityTraits: ["risk-aware", "precise"]),
            communicationProfile: new AgentCommunicationProfileDto(
                "precise",
                "pragmatic operator",
                ["Cite assumptions", "Use bullets for decisions"],
                ["Keep legal context visible"],
                ["Do not use flippant"],
                AgentCommunicationProfileSources.Explicit,
                false),
            inputPayload: Payload(
                (PromptIdentityPayloadKeys.CollaborationNorms, new JsonArray(JsonValue.Create("Use bullets for decisions"), JsonValue.Create("Coordinate with AP owner"))),
                (PromptIdentityPayloadKeys.PersonalityTraits, new JsonArray(JsonValue.Create("risk-aware"), JsonValue.Create("calm under pressure")))));

        var result = builder.Build(new PromptBuildRequest(context));

        Assert.Equal(["Cite assumptions", "Keep legal context visible", "Use bullets for decisions", "Coordinate with AP owner"], result.ResolvedIdentity.CollaborationNorms);
        Assert.Equal(["risk-aware", "precise", "pragmatic operator", "calm under pressure"], result.ResolvedIdentity.PersonalityTraits);
    }

    private static SingleAgentRuntimeContext CreateRuntimeContext(
        GroundedPromptContextDto? groundedContext = null,
        IReadOnlyList<ToolMetadataDto>? availableTools = null,
        string taskType = "finance_execution",
        Dictionary<string, JsonNode?>? inputPayload = null,
        string intent = OrchestrationIntentValues.ExecuteTask,
        AgentCommunicationProfileDto? communicationProfile = null,
        PromptIdentityPolicyDto? identityPolicy = null,
        string roleName = "Finance Manager",
        string department = "Finance",
        string seniority = "Senior",
        string roleBrief = "Execute finance operations through approved tools.")
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
                roleName,
                department,
                seniority,
                "active",
                roleBrief,
                Payload(("style", JsonValue.Create("precise"))),
                Payload(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
                Payload(("targets", new JsonArray(JsonValue.Create("forecast_accuracy")))),
                Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
                Payload(("execute", new JsonArray(JsonValue.Create("payments")))),
                Payload(("approval", new JsonObject { ["expenseUsd"] = 1000 })),
                Payload(("escalateTo", JsonValue.Create("founder"))),
                [],
                [],
                communicationProfile ?? new AgentCommunicationProfileDto(
                    "concise executive",
                    "pragmatic operator",
                    ["Use bullets for decisions", "Cite assumptions"],
                    ["Avoid unsupported claims"],
                    ["Do not use flippant"],
                    AgentCommunicationProfileSources.Explicit,
                    false),
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
                "EU",
                identityPolicy),
            groundedContext ?? CreateGroundedContext(companyId, agentId, timestamp),
            availableTools ??
            [
                new ToolMetadataDto(
                    "erp",
                    ["execute", "read"],
                    ["payments"],
                    Payload(("allowed", new JsonArray(JsonValue.Create("erp")))))
            ],
            intent);
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
