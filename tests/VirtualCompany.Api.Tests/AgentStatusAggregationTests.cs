using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AgentStatusAggregationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AgentStatusAggregationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Health_calculator_returns_healthy_warning_and_critical_from_explicit_thresholds()
    {
        Assert.Equal(AgentStatusHealthCalculator.Healthy, AgentStatusHealthCalculator.Calculate(new AgentStatusHealthMetrics(0, 0, 0)).Status);

        var warning = AgentStatusHealthCalculator.Calculate(new AgentStatusHealthMetrics(1, 0, 0));
        Assert.Equal(AgentStatusHealthCalculator.Warning, warning.Status);
        Assert.Contains(warning.Reasons, x => x.Contains("failed run", StringComparison.OrdinalIgnoreCase));

        var criticalFromFailures = AgentStatusHealthCalculator.Calculate(new AgentStatusHealthMetrics(3, 0, 0));
        Assert.Equal(AgentStatusHealthCalculator.Critical, criticalFromFailures.Status);

        var warningFromPolicy = AgentStatusHealthCalculator.Calculate(new AgentStatusHealthMetrics(0, 0, 1));
        Assert.Equal(AgentStatusHealthCalculator.Warning, warningFromPolicy.Status);
        Assert.Contains(warningFromPolicy.Reasons, x => x.Contains("policy violation", StringComparison.OrdinalIgnoreCase));

        var criticalFromPolicy = AgentStatusHealthCalculator.Calculate(new AgentStatusHealthMetrics(0, 0, 2));
        Assert.Equal(AgentStatusHealthCalculator.Critical, criticalFromPolicy.Status);
        Assert.Contains(criticalFromPolicy.Reasons, x => x.Contains("policy violation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Status_cards_endpoint_returns_workload_health_alerts_recent_actions_and_deep_link()
    {
        var seed = await SeedStatusScenarioAsync();

        using var client = CreateAuthenticatedClient("owner-status", "owner-status@example.com", "Owner Status");
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/status-cards");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentStatusCardsResponse>();
        Assert.NotNull(payload);

        var agent = Assert.Single(payload!.Items.Where(x => x.AgentId == seed.AgentId));
        Assert.Equal("Nora Ledger", agent.DisplayName);
        Assert.Equal("Finance Manager", agent.RoleName);
        Assert.Equal("Finance", agent.Department);
        Assert.Equal(2, agent.Workload.ActiveTaskCount);
        Assert.Equal(1, agent.Workload.BlockedTaskCount);
        Assert.Equal(2, agent.Workload.AwaitingApprovalCount);
        Assert.Equal(1, agent.Workload.ActiveWorkflowCount);
        Assert.Equal("Blocked", agent.Workload.WorkloadLevel);
        Assert.Equal(AgentStatusHealthCalculator.Critical, agent.HealthStatus);
        Assert.True(agent.ActiveAlertsCount >= 4);
        Assert.True(agent.LastUpdatedUtc <= payload.GeneratedUtc);
        Assert.Equal($"/agents/{seed.AgentId}", agent.DetailLink.Path);
        Assert.Equal("work", agent.DetailLink.ActiveTab);
        Assert.Equal(seed.CompanyId.ToString("D"), agent.DetailLink.Query["companyId"]);
        Assert.Equal("active", agent.DetailLink.Query["show"]);
        Assert.Equal("tasks,workflows,alerts", agent.DetailLink.Query["include"]);
    }

    [Fact]
    public async Task Status_detail_endpoint_resolves_deep_link_payload_for_selected_agent()
    {
        var seed = await SeedStatusScenarioAsync();

        using var client = CreateAuthenticatedClient("owner-status", "owner-status@example.com", "Owner Status");
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/status-detail");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentStatusDetailResponse>();
        Assert.NotNull(payload);
        Assert.Equal(seed.AgentId, payload!.AgentId);
        Assert.Equal(seed.CompanyId, payload.CompanyId);
        Assert.NotEmpty(payload.ActiveTasks);
        Assert.NotEmpty(payload.ActiveWorkflows);
        Assert.NotEmpty(payload.ActiveAlerts);
        Assert.Equal(AgentStatusHealthCalculator.Critical, payload.Health.Status);
        Assert.True(payload.ActiveAlertsCount >= payload.ActiveAlerts.Count);
    }

    [Fact]
    public async Task Recent_actions_are_newest_first_limited_to_five_with_deterministic_tie_breaker()
    {
        var seed = await SeedStatusScenarioAsync();

        using var client = CreateAuthenticatedClient("owner-status", "owner-status@example.com", "Owner Status");
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentStatusCardsResponse>();
        var agent = Assert.Single(payload!.Items.Where(x => x.AgentId == seed.AgentId));

        Assert.Equal(5, agent.RecentActions.Count);
        Assert.Equal(agent.RecentActions.OrderByDescending(x => x.OccurredUtc).ThenBy(x => x.ActionType, StringComparer.Ordinal).Select(x => x.RelatedEntityId), agent.RecentActions.Select(x => x.RelatedEntityId));
        Assert.Equal("task", agent.RecentActions[0].ActionType);
        Assert.Equal(seed.NewestTaskId, agent.RecentActions[0].RelatedEntityId);
    }

    [Fact]
    public async Task Status_endpoint_is_company_scoped()
    {
        var seed = await SeedStatusScenarioAsync();

        using var client = CreateAuthenticatedClient("owner-status", "owner-status@example.com", "Owner Status");
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentStatusCardsResponse>();
        Assert.NotNull(payload);
        Assert.Contains(payload!.Items, x => x.AgentId == seed.AgentId);
        Assert.DoesNotContain(payload.Items, x => x.AgentId == seed.OtherCompanyAgentId);
    }

    [Fact]
    public async Task Aggregation_reflects_current_persisted_state_without_cache()
    {
        var seed = await SeedStatusScenarioAsync();

        using var client = CreateAuthenticatedClient("owner-status", "owner-status@example.com", "Owner Status");
        var before = await client.GetFromJsonAsync<AgentStatusCardsResponse>($"/api/companies/{seed.CompanyId}/agents/status");
        var beforeCard = Assert.Single(before!.Items.Where(x => x.AgentId == seed.AgentId));

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var task = await dbContext.WorkTasks.SingleAsync(x => x.Id == seed.BlockedTaskId);
            task.UpdateStatus(WorkTaskStatus.Completed);
            await dbContext.SaveChangesAsync();
        }

        var after = await client.GetFromJsonAsync<AgentStatusCardsResponse>($"/api/companies/{seed.CompanyId}/agents/status");
        var afterCard = Assert.Single(after!.Items.Where(x => x.AgentId == seed.AgentId));

        Assert.Equal(beforeCard.Workload.BlockedTaskCount - 1, afterCard.Workload.BlockedTaskCount);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private async Task<StatusSeed> SeedStatusScenarioAsync()
    {
        var ownerUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var otherCompanyAgentId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();
        var blockedTaskId = Guid.NewGuid();
        var newestTaskId = Guid.NewGuid();
        var failedTaskId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var secondDeniedExecutionId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(ownerUserId, "owner-status@example.com", "Owner Status", "dev-header", "owner-status"));

            dbContext.Companies.AddRange(
                new Company(companyId, "Status Company"),
                new Company(otherCompanyId, "Other Status Company"));

            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            dbContext.Agents.AddRange(
                new Agent(agentId, companyId, "finance", "Nora Ledger", "Finance Manager", "Finance", null, AgentSeniority.Senior, AgentStatus.Active),
                new Agent(otherCompanyAgentId, otherCompanyId, "support", "Other Agent", "Support Lead", "Support", null, AgentSeniority.Lead, AgentStatus.Active));

            var definition = new WorkflowDefinition(
                workflowDefinitionId,
                companyId,
                "STATUS_TEST",
                "Status test",
                "Finance",
                WorkflowTriggerType.Manual,
                1,
                Payload(("steps", new JsonArray(JsonValue.Create("review")))));

            var workflow = new WorkflowInstance(
                workflowInstanceId,
                companyId,
                workflowDefinitionId,
                null,
                Payload(("source", JsonValue.Create("test"))));

            workflow.UpdateState(WorkflowInstanceStatus.Running, "review");

            var blockedTask = new WorkTask(
                blockedTaskId,
                companyId,
                "review",
                "Blocked finance review",
                null,
                WorkTaskPriority.High,
                agentId,
                null,
                "user",
                ownerUserId,
                workflowInstanceId: workflowInstanceId);
            blockedTask.UpdateStatus(WorkTaskStatus.Blocked);

            var newestTask = new WorkTask(
                newestTaskId,
                companyId,
                "close",
                "Newest finance action",
                null,
                WorkTaskPriority.Normal,
                agentId,
                null,
                "user",
                ownerUserId);
            newestTask.UpdateStatus(WorkTaskStatus.InProgress);

            var failedTask = new WorkTask(
                failedTaskId,
                companyId,
                "failed",
                "Failed finance action",
                null,
                WorkTaskPriority.Normal,
                agentId,
                null,
                "user",
                ownerUserId);
            failedTask.UpdateStatus(WorkTaskStatus.Failed);

            var execution = new ToolExecutionAttempt(
                executionId,
                companyId,
                agentId,
                "erp",
                ToolActionType.Execute,
                "finance");
            execution.MarkDenied(Payload(("reason", JsonValue.Create("policy_violation"))));

            var secondDeniedExecution = new ToolExecutionAttempt(
                secondDeniedExecutionId,
                companyId,
                agentId,
                "ledger",
                ToolActionType.Execute,
                "finance");
            secondDeniedExecution.MarkDenied(Payload(("reason", JsonValue.Create("policy_violation"))));

            var approval = new ApprovalRequest(
                approvalId,
                companyId,
                agentId,
                executionId,
                ownerUserId,
                "erp",
                ToolActionType.Execute,
                "expense",
                Payload(("expenseUsd", JsonValue.Create(9000))));

            dbContext.WorkflowDefinitions.Add(definition);
            dbContext.WorkflowInstances.Add(workflow);
            dbContext.WorkTasks.AddRange(blockedTask, newestTask, failedTask);
            dbContext.ToolExecutionAttempts.AddRange(execution, secondDeniedExecution);
            dbContext.ApprovalRequests.Add(approval);
            dbContext.Alerts.Add(new Alert(
                Guid.NewGuid(),
                companyId,
                AlertType.Risk,
                AlertSeverity.High,
                "Policy violation",
                "Denied execution requires review.",
                Payload(("executionId", JsonValue.Create(executionId))),
                Guid.NewGuid().ToString("N"),
                $"status:{executionId:N}",
                sourceAgentId: agentId));

            dbContext.AuditEvents.AddRange(
                new AuditEvent(Guid.NewGuid(), companyId, "agent", agentId, "agent.status.same_time_a", "agent", agentId.ToString("N"), "succeeded", occurredUtc: new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc)),
                new AuditEvent(Guid.NewGuid(), companyId, "agent", agentId, "agent.status.same_time_b", "agent", agentId.ToString("N"), "succeeded", occurredUtc: new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc)),
                new AuditEvent(Guid.NewGuid(), companyId, "agent", agentId, "agent.status.older", "agent", agentId.ToString("N"), "succeeded", occurredUtc: new DateTime(2026, 4, 13, 12, 0, 0, DateTimeKind.Utc)));

            return Task.CompletedTask;
        });

        return new StatusSeed(companyId, agentId, otherCompanyAgentId, blockedTaskId, newestTaskId);
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

    private sealed record StatusSeed(Guid CompanyId, Guid AgentId, Guid OtherCompanyAgentId, Guid BlockedTaskId, Guid NewestTaskId);

    private sealed class AgentStatusCardsResponse
    {
        public List<AgentStatusCard> Items { get; set; } = [];
        public DateTime GeneratedUtc { get; set; }
    }

    private sealed class AgentStatusCard
    {
        public Guid AgentId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public AgentStatusWorkload Workload { get; set; } = new();
        public string HealthStatus { get; set; } = string.Empty;
        public List<string> HealthReasons { get; set; } = [];
        public int ActiveAlertsCount { get; set; }
        public DateTime LastUpdatedUtc { get; set; }
        public List<AgentStatusRecentAction> RecentActions { get; set; } = [];
        public AgentStatusDetailLink DetailLink { get; set; } = new();
    }

    private sealed class AgentStatusWorkload
    {
        public int ActiveTaskCount { get; set; }
        public int BlockedTaskCount { get; set; }
        public int AwaitingApprovalCount { get; set; }
        public int ActiveWorkflowCount { get; set; }
        public string WorkloadLevel { get; set; } = string.Empty;
    }

    private sealed class AgentStatusRecentAction
    {
        public DateTime OccurredUtc { get; set; }
        public string ActionType { get; set; } = string.Empty;
        public Guid? RelatedEntityId { get; set; }
    }

    private sealed class AgentStatusDetailLink
    {
        public string Path { get; set; } = string.Empty;
        public string ActiveTab { get; set; } = string.Empty;
        public Dictionary<string, string> Query { get; set; } = [];
    }

    private sealed class AgentStatusDetailResponse
    {
        public Guid AgentId { get; set; }
        public Guid CompanyId { get; set; }
        public AgentStatusWorkload Workload { get; set; } = new();
        public AgentStatusHealthBreakdown Health { get; set; } = new();
        public int ActiveAlertsCount { get; set; }
        public List<AgentStatusDetailTask> ActiveTasks { get; set; } = [];
        public List<AgentStatusDetailWorkflow> ActiveWorkflows { get; set; } = [];
        public List<AgentStatusDetailAlert> ActiveAlerts { get; set; } = [];
        public List<AgentStatusRecentAction> RecentActions { get; set; } = [];
    }

    private sealed class AgentStatusHealthBreakdown
    {
        public string Status { get; set; } = string.Empty;
        public List<string> Reasons { get; set; } = [];
        public AgentStatusHealthMetricsResponse Metrics { get; set; } = new();
    }

    private sealed class AgentStatusHealthMetricsResponse
    {
        public int FailedRunCount { get; set; }
        public int StalledWorkCount { get; set; }
        public int PolicyViolationCount { get; set; }
    }

    private sealed class AgentStatusDetailTask
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    private sealed class AgentStatusDetailWorkflow
    {
        public Guid Id { get; set; }
        public string DefinitionName { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
    }

    private sealed class AgentStatusDetailAlert
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
