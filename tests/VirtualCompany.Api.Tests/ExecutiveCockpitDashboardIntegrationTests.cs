using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ExecutiveCockpitDashboardIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ExecutiveCockpitDashboardIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dashboard_returns_empty_state_flags_for_sparse_workspace()
    {
        var companyId = await SeedSparseCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/companies/{companyId}/executive-cockpit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<ExecutiveCockpitDashboardResponse>();

        Assert.NotNull(dashboard);
        Assert.Equal(companyId, dashboard!.CompanyId);
        Assert.Contains(dashboard.SummaryKpis, x => x.Key == "pending_approvals" && x.CurrentValue == 0 && x.TrendDirection == "unknown");
        Assert.Contains(dashboard.SummaryKpis, x => x.Key == "open_tasks" && x.CurrentValue == 0 && x.TrendDirection == "unknown");
        Assert.Contains(dashboard.SummaryKpis, x => x.Key == "completed_tasks_7d" && x.CurrentValue == 0 && x.PreviousValue == 0 && x.TrendDirection == "flat");
        Assert.Contains(dashboard.SummaryKpis, x => x.Key == "active_agents" && x.CurrentValue == 0);
        Assert.True(dashboard.EmptyStateFlags.NoAgents);
        Assert.True(dashboard.EmptyStateFlags.NoWorkflows);
        Assert.True(dashboard.EmptyStateFlags.NoKnowledge);
        Assert.False(dashboard.SetupState.HasAgents);
        Assert.False(dashboard.SetupState.HasWorkflows);
        Assert.False(dashboard.SetupState.HasKnowledge);
        Assert.Equal(0, dashboard.SetupState.AgentCount);
        Assert.Equal(0, dashboard.SetupState.WorkflowCount);
        Assert.Equal(0, dashboard.SetupState.KnowledgeDocumentCount);
        Assert.True(dashboard.SetupState.IsInitialSetupEmpty);
        Assert.True(dashboard.EmptyStateFlags.NoRecentActivity);
        Assert.True(dashboard.EmptyStateFlags.NoPendingApprovals);
        Assert.True(dashboard.EmptyStateFlags.NoAlerts);
        Assert.Equal(["finance", "sales", "support", "operations"], dashboard.DepartmentSections.Select(x => x.DepartmentKey).ToArray());
        Assert.Equal([10, 20, 30, 40], dashboard.DepartmentSections.Select(x => x.DisplayOrder).ToArray());
        Assert.All(dashboard.DepartmentSections, section => Assert.All(section.Widgets, widget => Assert.True(widget.IsVisible)));
        Assert.All(dashboard.DepartmentSections, section =>
        {
            Assert.False(section.HasData);
            Assert.All(section.SummaryCounts.Values, value => Assert.Equal(0, value));
        });
    }

    [Fact]
    public async Task Dashboard_is_tenant_scoped_and_populates_operational_sections()
    {
        var seed = await SeedCockpitCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/executive-cockpit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<ExecutiveCockpitDashboardResponse>();

        Assert.NotNull(dashboard);
        Assert.Equal(seed.CompanyId, dashboard!.CompanyId);
        Assert.NotNull(dashboard.DailyBriefing);
        Assert.Contains(dashboard.SummaryKpis, x => x.Key == "pending_approvals" && x.CurrentValue == 1);
        Assert.Contains(dashboard.SummaryKpis, x => x.Key == "open_tasks" && x.CurrentValue == 1);
        Assert.Contains(dashboard.SummaryKpis, x => x.Key == "completed_tasks_7d" && x.CurrentValue == 2 && x.PreviousValue == 1 && x.TrendDirection == "up");
        Assert.Contains(dashboard.SummaryKpis, x => x.Key == "active_agents" && x.CurrentValue == 1);
        Assert.Contains(dashboard.SummaryKpis, x => x.Key == "blocked_tasks" && x.CurrentValue == 0);
        Assert.Equal(1, dashboard.PendingApprovals.TotalCount);
        Assert.Contains(dashboard.PendingApprovals.Items, x => x.Id == seed.ApprovalId);
        Assert.Contains(dashboard.PendingApprovals.Items, x => x.Id == seed.ApprovalId && x.Route.Contains($"approvalId={seed.ApprovalId}", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("status=pending", dashboard.PendingApprovals.Route, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(dashboard.Alerts, x => x.SourceId == seed.WorkflowExceptionId);
        Assert.Contains(dashboard.Alerts, x => x.SourceId == seed.WorkflowExceptionId && x.Route?.Contains($"workflowInstanceId={seed.WorkflowInstanceId}", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(dashboard.DepartmentKpis, x => x.Department == "Operations" && x.ActiveAgents == 1);
        Assert.Equal(["finance", "sales", "support", "operations"], dashboard.DepartmentSections.Select(x => x.DepartmentKey).ToArray());
        var operationsSection = Assert.Single(dashboard.DepartmentSections, x => x.DepartmentKey == "operations");
        Assert.True(operationsSection.HasData);
        Assert.Contains(operationsSection.Widgets, x => x.WidgetType == "summary_count" && !string.IsNullOrWhiteSpace(x.Navigation.Route));
        Assert.Contains(dashboard.RecentActivity, x => x.Id == seed.TaskId);
        Assert.Contains(dashboard.RecentActivity, x => x.Id == seed.TaskId && x.Route?.Contains($"taskId={seed.TaskId}", StringComparison.OrdinalIgnoreCase) == true);
        Assert.True(dashboard.SetupState.HasAgents);
        Assert.True(dashboard.SetupState.HasWorkflows);
        Assert.True(dashboard.SetupState.HasKnowledge);
        Assert.Equal(1, dashboard.SetupState.AgentCount);
        Assert.Equal(1, dashboard.SetupState.WorkflowCount);
        Assert.Equal(1, dashboard.SetupState.KnowledgeDocumentCount);
        Assert.False(dashboard.SetupState.IsInitialSetupEmpty);
        Assert.DoesNotContain(dashboard.PendingApprovals.Items, x => x.Id == seed.OtherCompanyApprovalId);

        var otherCompanyResponse = await client.GetAsync($"/api/companies/{seed.OtherCompanyId}/executive-cockpit");
        Assert.Equal(HttpStatusCode.Forbidden, otherCompanyResponse.StatusCode);
    }

    [Fact]
    public async Task Widget_endpoint_returns_scoped_payload_without_full_dashboard_fetch_contract()
    {
        var seed = await SeedCockpitCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/executive-cockpit/widgets/pending-approvals");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var widget = await response.Content.ReadFromJsonAsync<ExecutiveCockpitWidgetResponse>();

        Assert.NotNull(widget);
        Assert.Equal(seed.CompanyId, widget!.CompanyId);
        Assert.Equal("pending-approvals", widget.WidgetKey);
        Assert.NotNull(widget.Payload);
        Assert.Contains(seed.ApprovalId.ToString(), widget.Payload!.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seed.OtherCompanyApprovalId.ToString(), widget.Payload!.ToJsonString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Dashboard_cache_keys_are_tenant_scoped()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();

        Assert.Contains(companyA.ToString("N"), ExecutiveCockpitDashboardCache.BuildCacheKey(companyA));
        Assert.NotEqual(ExecutiveCockpitDashboardCache.BuildCacheKey(companyA), ExecutiveCockpitDashboardCache.BuildCacheKey(companyB));
    }

    [Theory]
    [InlineData(5, 2, "up", 3, 150)]
    [InlineData(2, 5, "down", -3, -60)]
    [InlineData(3, 3, "flat", 0, 0)]
    public void Kpi_trend_calculator_compares_current_to_previous_period(
        int current,
        int previous,
        string expectedDirection,
        int expectedDelta,
        decimal expectedPercentage)
    {
        var trend = ExecutiveCockpitKpiTrendCalculator.Calculate(current, previous);

        Assert.Equal(expectedDirection, trend.Direction);
        Assert.Equal(expectedDelta, trend.DeltaValue);
        Assert.Equal(expectedPercentage, trend.DeltaPercentage);
    }

    [Fact]
    public void Kpi_trend_calculator_avoids_divide_by_zero_and_unknown_prior_values()
    {
        Assert.Null(ExecutiveCockpitKpiTrendCalculator.Calculate(4, 0).DeltaPercentage);
        Assert.Equal("unknown", ExecutiveCockpitKpiTrendCalculator.Calculate(4, null).Direction);
        Assert.Null(ExecutiveCockpitKpiTrendCalculator.Calculate(4, null).DeltaValue);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "cockpit-founder");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "cockpit-founder@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Cockpit Founder");
        return client;
    }

    private async Task<Guid> SeedSparseCompanyAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var otherAgentId = Guid.NewGuid();
        var otherWorkflowDefinitionId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "cockpit-founder@example.com", "Cockpit Founder", "dev-header", "cockpit-founder"));
            dbContext.Companies.AddRange(new Company(companyId, "Sparse Cockpit Company"), new Company(otherCompanyId, "Other Sparse Cockpit Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(otherAgentId, otherCompanyId, "ops", "Other Ops", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(otherWorkflowDefinitionId, otherCompanyId, "other-cockpit-test", "Other cockpit test", "Operations", WorkflowTriggerType.Manual, 1, Payload(("steps", new JsonArray()))));
            dbContext.CompanyKnowledgeDocuments.Add(new CompanyKnowledgeDocument(
                Guid.NewGuid(),
                otherCompanyId,
                "Other company handbook",
                CompanyKnowledgeDocumentType.Policy,
                "knowledge/other/handbook.md",
                null,
                "handbook.md",
                "text/markdown",
                ".md",
                1024,
                accessScope: new CompanyKnowledgeDocumentAccessScope(otherCompanyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility)));
            return Task.CompletedTask;
        });

        return companyId;
    }

    private async Task<CockpitSeed> SeedCockpitCompanyAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        var workflowInstanceId = Guid.NewGuid();
        var workflowExceptionId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var completedTaskCurrentId = Guid.NewGuid();
        var completedTaskCurrentSecondId = Guid.NewGuid();
        var completedTaskPreviousId = Guid.NewGuid();
        var otherCompanyApprovalId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "cockpit-founder@example.com", "Cockpit Founder", "dev-header", "cockpit-founder"));
            dbContext.Companies.AddRange(new Company(companyId, "Cockpit Company"), new Company(otherCompanyId, "Other Cockpit Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(agentId, companyId, "ops", "Avery Ops", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));

            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(workflowDefinitionId, companyId, "cockpit-test", "Cockpit test", "Operations", WorkflowTriggerType.Manual, 1, Payload(("steps", new JsonArray()))));
            dbContext.WorkflowInstances.Add(new WorkflowInstance(workflowInstanceId, companyId, workflowDefinitionId, null));
            dbContext.WorkflowExceptions.Add(new WorkflowException(workflowExceptionId, companyId, workflowInstanceId, workflowDefinitionId, "review", WorkflowExceptionType.Blocked, "Workflow blocked", "Review the blocked workflow."));

            var task = new WorkTask(taskId, companyId, "ops", "Complete founder report", null, WorkTaskPriority.High, agentId, null, "user", userId, workflowInstanceId: workflowInstanceId);
            task.UpdateStatus(WorkTaskStatus.InProgress);
            dbContext.WorkTasks.Add(task);

            var completedTaskCurrent = new WorkTask(completedTaskCurrentId, companyId, "ops", "Publish current report", null, WorkTaskPriority.Normal, agentId, null, "user", userId);
            completedTaskCurrent.UpdateStatus(WorkTaskStatus.Completed);
            SetTaskTimestamps(completedTaskCurrent, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1));

            var completedTaskCurrentSecond = new WorkTask(completedTaskCurrentSecondId, companyId, "ops", "Close current checklist", null, WorkTaskPriority.Normal, agentId, null, "user", userId);
            completedTaskCurrentSecond.UpdateStatus(WorkTaskStatus.Completed);
            SetTaskTimestamps(completedTaskCurrentSecond, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-2));

            var completedTaskPrevious = new WorkTask(completedTaskPreviousId, companyId, "ops", "Publish previous report", null, WorkTaskPriority.Normal, agentId, null, "user", userId);
            completedTaskPrevious.UpdateStatus(WorkTaskStatus.Completed);
            SetTaskTimestamps(completedTaskPrevious, DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(-10));

            dbContext.WorkTasks.AddRange(completedTaskCurrent, completedTaskCurrentSecond, completedTaskPrevious);

            dbContext.CompanyKnowledgeDocuments.Add(new CompanyKnowledgeDocument(
                documentId,
                companyId,
                "Cockpit handbook",
                CompanyKnowledgeDocumentType.Policy,
                "knowledge/cockpit/handbook.md",
                null,
                "handbook.md",
                "text/markdown",
                ".md",
                1024,
                accessScope: new CompanyKnowledgeDocumentAccessScope(companyId, CompanyKnowledgeDocumentAccessScope.CompanyVisibility)));

            dbContext.ApprovalRequests.AddRange(
                ApprovalRequest.CreateForTarget(approvalId, companyId, ApprovalTargetEntityType.Task, taskId, "agent", agentId, "threshold", Payload(("amount", JsonValue.Create(25000))), "owner", null, []),
                ApprovalRequest.CreateForTarget(otherCompanyApprovalId, otherCompanyId, ApprovalTargetEntityType.Task, Guid.NewGuid(), "agent", Guid.NewGuid(), "threshold", Payload(("amount", JsonValue.Create(50000))), "owner", null, []));

            dbContext.CompanyBriefings.Add(new CompanyBriefing(Guid.NewGuid(), companyId, CompanyBriefingType.Daily, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, "Daily briefing", "Founder briefing summary.", [], []));
            return Task.CompletedTask;
        });

        return new CockpitSeed(companyId, otherCompanyId, approvalId, otherCompanyApprovalId, workflowExceptionId, workflowInstanceId, taskId);
    }

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] properties) =>
        properties.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static void SetTaskTimestamps(WorkTask task, DateTime createdUtc, DateTime completedUtc)
    {
        typeof(WorkTask).GetProperty(nameof(WorkTask.CreatedUtc))!.SetValue(task, createdUtc);
        typeof(WorkTask).GetProperty(nameof(WorkTask.UpdatedUtc))!.SetValue(task, completedUtc);
        typeof(WorkTask).GetProperty(nameof(WorkTask.CompletedUtc))!.SetValue(task, completedUtc);
    }

    private sealed record CockpitSeed(Guid CompanyId, Guid OtherCompanyId, Guid ApprovalId, Guid OtherCompanyApprovalId, Guid WorkflowExceptionId, Guid WorkflowInstanceId, Guid TaskId);

    private sealed class ExecutiveCockpitDashboardResponse
    {
        public Guid CompanyId { get; set; }
        public ExecutiveCockpitDailyBriefingResponse? DailyBriefing { get; set; }
        public List<ExecutiveCockpitSummaryKpiResponse> SummaryKpis { get; set; } = [];
        public ExecutiveCockpitPendingApprovalsResponse PendingApprovals { get; set; } = new();
        public List<ExecutiveCockpitAlertResponse> Alerts { get; set; } = [];
        public List<ExecutiveCockpitDepartmentKpiResponse> DepartmentKpis { get; set; } = [];
        public List<DepartmentDashboardSectionResponse> DepartmentSections { get; set; } = [];
        public List<ExecutiveCockpitActivityResponse> RecentActivity { get; set; } = [];
        public ExecutiveCockpitSetupStateResponse SetupState { get; set; } = new();
        public ExecutiveCockpitEmptyStateFlagsResponse EmptyStateFlags { get; set; } = new();
    }

    private sealed class ExecutiveCockpitDailyBriefingResponse { public Guid Id { get; set; } }
    private sealed class ExecutiveCockpitSummaryKpiResponse
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int CurrentValue { get; set; }
        public int? PreviousValue { get; set; }
        public string TrendDirection { get; set; } = string.Empty;
        public int? DeltaValue { get; set; }
        public decimal? DeltaPercentage { get; set; }
        public string DeltaText { get; set; } = string.Empty;
        public string ComparisonLabel { get; set; } = string.Empty;
        public bool IsEmpty { get; set; }
    }
    private sealed class ExecutiveCockpitPendingApprovalsResponse { public int TotalCount { get; set; } public List<ExecutiveCockpitApprovalResponse> Items { get; set; } = []; public string Route { get; set; } = string.Empty; }
    private sealed class ExecutiveCockpitApprovalResponse { public Guid Id { get; set; } public string Route { get; set; } = string.Empty; }
    private sealed class ExecutiveCockpitAlertResponse { public Guid? SourceId { get; set; } public string? Route { get; set; } }
    private sealed class ExecutiveCockpitDepartmentKpiResponse { public string Department { get; set; } = string.Empty; public int ActiveAgents { get; set; } }
    private sealed class DepartmentDashboardSectionResponse
    {
        public string DepartmentKey { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool HasData { get; set; }
        public Dictionary<string, int> SummaryCounts { get; set; } = [];
        public List<DepartmentDashboardWidgetResponse> Widgets { get; set; } = [];
    }
    private sealed class DepartmentDashboardWidgetResponse
    {
        public string WidgetType { get; set; } = string.Empty;
        public bool IsVisible { get; set; }
        public DepartmentDashboardNavigationResponse Navigation { get; set; } = new();
    }
    private sealed class DepartmentDashboardNavigationResponse { public string Route { get; set; } = string.Empty; }
    private sealed class ExecutiveCockpitActivityResponse { public Guid Id { get; set; } public string? Route { get; set; } }
    private sealed class ExecutiveCockpitSetupStateResponse
    {
        public bool HasAgents { get; set; }
        public bool HasWorkflows { get; set; }
        public bool HasKnowledge { get; set; }
        public int AgentCount { get; set; }
        public int WorkflowCount { get; set; }
        public int KnowledgeDocumentCount { get; set; }
        public bool IsInitialSetupEmpty { get; set; }
    }

    private sealed class ExecutiveCockpitEmptyStateFlagsResponse
    {
        public bool NoAgents { get; set; }
        public bool NoWorkflows { get; set; }
        public bool NoKnowledge { get; set; }
        public bool NoRecentActivity { get; set; }
        public bool NoPendingApprovals { get; set; }
        public bool NoAlerts { get; set; }
    }

    private sealed class ExecutiveCockpitWidgetResponse
    {
        public Guid CompanyId { get; set; }
        public string WidgetKey { get; set; } = string.Empty;
        public JsonNode? Payload { get; set; }
    }
}