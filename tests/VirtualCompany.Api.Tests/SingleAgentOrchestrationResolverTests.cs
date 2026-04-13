using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Chat;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class SingleAgentOrchestrationResolverTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public SingleAgentOrchestrationResolverTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ResolveAsync_resolves_agent_from_explicit_agent_id()
    {
        var seed = await SeedScenarioAsync();

        var result = await ResolveAsync(new OrchestrationRequest(
            seed.CompanyId,
            AgentId: seed.AgentId,
            UserInput: "Summarize this account.",
            CorrelationId: "explicit-agent"));

        Assert.True(result.Succeeded);
        Assert.Equal(seed.AgentId, result.Agent!.AgentId);
        Assert.Equal(seed.CompanyId, result.Agent.CompanyId);
        Assert.Equal("explicit-agent", result.CorrelationId);
        Assert.Equal("chat", result.Intent!.Name);
        Assert.Equal(OrchestrationResolutionSources.Heuristic, result.Intent.Source);
    }

    [Fact]
    public async Task ResolveAsync_resolves_agent_from_assigned_task_when_agent_id_absent()
    {
        var seed = await SeedScenarioAsync();

        var result = await ResolveAsync(new OrchestrationRequest(
            seed.CompanyId,
            TaskId: seed.TaskId,
            CorrelationId: "task-agent"));

        Assert.True(result.Succeeded);
        Assert.Equal(seed.AgentId, result.Agent!.AgentId);
        Assert.Equal("task_execution", result.Intent!.Name);
        Assert.Equal("finance_execution", result.Intent.TaskType);
        Assert.Equal(OrchestrationResolutionSources.Task, result.Intent.Source);
    }

    [Fact]
    public async Task ResolveAsync_fails_when_task_has_no_assigned_agent()
    {
        var seed = await SeedScenarioAsync(taskAssignedAgentId: null);

        var result = await ResolveAsync(new OrchestrationRequest(
            seed.CompanyId,
            TaskId: seed.TaskId,
            CorrelationId: "unassigned-task"));

        Assert.False(result.Succeeded);
        Assert.Contains(nameof(OrchestrationRequest.AgentId), result.Errors.Keys);
        Assert.Contains(OrchestrationResolutionErrorCodes.NoResolvableTargetAgent, result.Errors[nameof(OrchestrationRequest.AgentId)][0]);
    }

    [Fact]
    public async Task ResolveAsync_fails_when_explicit_agent_belongs_to_another_company()
    {
        var seed = await SeedScenarioAsync();
        var otherCompanyId = Guid.NewGuid();
        var otherAgentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(otherCompanyId, "Other Company"));
            dbContext.Agents.Add(CreateAgent(otherAgentId, otherCompanyId, AgentStatus.Active));
            return Task.CompletedTask;
        });

        var result = await ResolveAsync(new OrchestrationRequest(
            seed.CompanyId,
            AgentId: otherAgentId,
            UserInput: "Run this in the wrong tenant.",
            CorrelationId: "cross-tenant"));

        Assert.False(result.Succeeded);
        Assert.Contains(nameof(OrchestrationRequest.AgentId), result.Errors.Keys);
        Assert.Contains(OrchestrationResolutionErrorCodes.AgentNotFound, result.Errors[nameof(OrchestrationRequest.AgentId)][0]);
    }

    [Fact]
    public async Task ResolveAsync_prefers_explicit_intent_hint_over_task_type()
    {
        var seed = await SeedScenarioAsync();

        var result = await ResolveAsync(new OrchestrationRequest(
            seed.CompanyId,
            TaskId: seed.TaskId,
            IntentHint: "Direct Chat",
            CorrelationId: "intent-hint"));

        Assert.True(result.Succeeded);
        Assert.Equal("direct_chat", result.Intent!.Name);
        Assert.Equal("finance_execution", result.Intent.TaskType);
        Assert.Equal(OrchestrationResolutionSources.Explicit, result.Intent.Source);
    }

    [Fact]
    public async Task ResolveAsync_builds_runtime_context_with_company_agent_and_task_fields()
    {
        var seed = await SeedScenarioAsync();

        var result = await ResolveAsync(new OrchestrationRequest(
            seed.CompanyId,
            TaskId: seed.TaskId,
            InitiatingActorId: seed.UserId,
            InitiatingActorType: "user",
            UserInput: "Please execute the invoice task.",
            CorrelationId: "runtime-context",
            ActorMetadata: Payload(("source", JsonValue.Create("unit-test")))));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.RuntimeContext);
        Assert.Equal(seed.CompanyId, result.RuntimeContext!.Company.CompanyId);
        Assert.Equal("Company A", result.RuntimeContext.Company.Name);
        Assert.Equal(seed.AgentId, result.RuntimeContext.Agent.AgentId);
        Assert.Equal("Nora Ledger", result.RuntimeContext.Agent.DisplayName);
        Assert.Equal(seed.TaskId, result.RuntimeContext.Task!.TaskId);
        Assert.Equal("Pay approved invoice", result.RuntimeContext.Task.Title);
        Assert.Equal("inv-100", result.RuntimeContext.Task.InputPayload["invoiceId"]!.GetValue<string>());
        Assert.Equal(seed.UserId, result.RuntimeContext.Actor.ActorId);
        Assert.Equal("runtime-context", result.RuntimeContext.Actor.CorrelationId);
        Assert.Equal("unit-test", result.RuntimeContext.Actor.Metadata["source"]!.GetValue<string>());
        Assert.Equal("level_2", result.RuntimeContext.Policy.AutonomyLevel);
        Assert.True(result.RuntimeContext.Policy.ToolPermissionSnapshot.ContainsKey("allowed"));
    }

    [Fact]
    public async Task ResolveAsync_does_not_read_ambient_correlation_when_request_omits_it()
    {
        var seed = await SeedScenarioAsync();

        using var scope = _factory.Services.CreateScope();
        var ambientCorrelation = scope.ServiceProvider.GetRequiredService<ICorrelationContextAccessor>();
        ambientCorrelation.CorrelationId = "ambient-http-correlation";
        var resolver = scope.ServiceProvider.GetRequiredService<ISingleAgentOrchestrationResolver>();

        var result = await resolver.ResolveAsync(
            new OrchestrationRequest(
                seed.CompanyId,
                AgentId: seed.AgentId,
                UserInput: "Summarize this account."),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.CorrelationId));
        Assert.NotEqual("ambient-http-correlation", result.CorrelationId);
        Assert.Equal(result.CorrelationId, result.RuntimeContext!.Actor.CorrelationId);
    }

    [Fact]
    public async Task ResolveAsync_rejects_non_executable_agent_status()
    {
        var seed = await SeedScenarioAsync(agentStatus: AgentStatus.Archived);

        var result = await ResolveAsync(new OrchestrationRequest(
            seed.CompanyId,
            AgentId: seed.AgentId,
            UserInput: "Try archived agent.",
            CorrelationId: "archived-agent"));

        Assert.False(result.Succeeded);
        Assert.Contains(OrchestrationResolutionErrorCodes.AgentStatusNotExecutable, result.Errors[nameof(OrchestrationRequest.AgentId)][0]);
    }

    [Fact]
    public async Task ResolveAsync_resolves_fallback_intent_for_direct_chat_request()
    {
        var seed = await SeedScenarioAsync(createConversation: true);

        var result = await ResolveAsync(new OrchestrationRequest(
            seed.CompanyId,
            ConversationId: seed.ConversationId,
            UserInput: "Hello",
            CorrelationId: "direct-conversation"));

        Assert.True(result.Succeeded);
        Assert.Equal(seed.AgentId, result.Agent!.AgentId);
        Assert.Equal("chat", result.Intent!.Name);
        Assert.Equal("direct_agent_chat", result.Intent.TaskType);
        Assert.Equal(OrchestrationResolutionSources.Conversation, result.Intent.Source);
        Assert.Equal(seed.ConversationId, result.RuntimeContext!.Conversation!.ConversationId);
    }

    [Fact]
    public async Task ResolveAsync_returns_deterministic_error_for_ambiguous_conversation_mapping()
    {
        var seed = await SeedScenarioAsync(createConversation: true, conversationChannelType: "team_room", conversationAgentId: null);

        var result = await ResolveAsync(new OrchestrationRequest(
            seed.CompanyId,
            ConversationId: seed.ConversationId,
            CorrelationId: "ambiguous-conversation"));

        Assert.False(result.Succeeded);
        Assert.Contains(nameof(OrchestrationRequest.ConversationId), result.Errors.Keys);
        Assert.Contains(OrchestrationResolutionErrorCodes.AmbiguousTargetAgent, result.Errors[nameof(OrchestrationRequest.ConversationId)][0]);
    }

    private async Task<OrchestrationResolutionResult> ResolveAsync(OrchestrationRequest request)
    {
        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<ISingleAgentOrchestrationResolver>();
        return await resolver.ResolveAsync(request, CancellationToken.None);
    }

    private async Task<SeededScenario> SeedScenarioAsync(
        Guid? taskAssignedAgentId = default,
        AgentStatus agentStatus = AgentStatus.Active,
        bool createConversation = false,
        string conversationChannelType = ChatChannelTypes.DirectAgent,
        Guid? conversationAgentId = default)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var conversationId = createConversation ? Guid.NewGuid() : (Guid?)null;
        var resolvedTaskAssignedAgentId = taskAssignedAgentId == default ? agentId : taskAssignedAgentId;
        var resolvedConversationAgentId = conversationAgentId == default && conversationChannelType == ChatChannelTypes.DirectAgent
            ? agentId
            : conversationAgentId;

        await _factory.SeedAsync(dbContext =>
        {
            var company = new Company(companyId, "Company A");
            company.UpdateWorkspaceProfile("Company A", "Financial services", "B2B", "Europe/Stockholm", "SEK", "sv", "EU");

            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", $"founder-{userId:N}"));
            dbContext.Companies.Add(company);
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));
            dbContext.Agents.Add(CreateAgent(agentId, companyId, agentStatus));
            dbContext.WorkTasks.Add(new WorkTask(
                taskId,
                companyId,
                "finance_execution",
                "Pay approved invoice",
                "Run the approved payment action with tenant-scoped context.",
                WorkTaskPriority.Normal,
                resolvedTaskAssignedAgentId,
                null,
                "user",
                userId,
                Payload(("invoiceId", JsonValue.Create("inv-100")))));

            if (conversationId.HasValue)
            {
                dbContext.Conversations.Add(new Conversation(
                    conversationId.Value,
                    companyId,
                    conversationChannelType,
                    "Invoice support",
                    userId,
                    resolvedConversationAgentId));
            }

            return Task.CompletedTask;
        });

        return new SeededScenario(companyId, userId, agentId, taskId, conversationId);
    }

    private static Agent CreateAgent(Guid agentId, Guid companyId, AgentStatus status) =>
        new(
            agentId,
            companyId,
            "finance",
            "Nora Ledger",
            "Finance Manager",
            "Finance",
            null,
            AgentSeniority.Senior,
            status,
            autonomyLevel: AgentAutonomyLevel.Level2,
            objectives: Payload(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
            kpis: Payload(("targets", new JsonArray(JsonValue.Create("forecast_accuracy")))),
            tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("payments")))),
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 1000 })),
            escalationRules: Payload(("escalateTo", JsonValue.Create("founder"))),
            roleBrief: "Execute finance operations through approved tools.");

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] properties)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in properties)
        {
            payload[key] = value?.DeepClone();
        }

        return payload;
    }

    private sealed record SeededScenario(Guid CompanyId, Guid UserId, Guid AgentId, Guid TaskId, Guid? ConversationId);
}
