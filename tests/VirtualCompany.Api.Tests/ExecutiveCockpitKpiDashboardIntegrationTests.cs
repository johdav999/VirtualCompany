using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;

namespace VirtualCompany.Api.Tests;

public sealed class ExecutiveCockpitKpiDashboardIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ExecutiveCockpitKpiDashboardIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Kpis_filter_by_department_and_time_range_with_anomaly_context()
    {
        var seed = await SeedKpiCompanyAsync();
        var startUtc = DateTime.UtcNow.AddDays(-7);
        var endUtc = DateTime.UtcNow.AddDays(1);

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/executive-cockpit/kpis?department=Operations&startUtc={Uri.EscapeDataString(startUtc.ToString("O"))}&endUtc={Uri.EscapeDataString(endUtc.ToString("O"))}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<KpiDashboardResponse>();

        Assert.NotNull(dashboard);
        Assert.Equal(seed.CompanyId, dashboard!.CompanyId);
        Assert.Equal("Operations", dashboard.Department);
        Assert.Contains("Operations", dashboard.Departments);
        Assert.Contains("Sales", dashboard.Departments);
        Assert.All(dashboard.Kpis, tile => Assert.Equal("Operations", tile.Department));
        Assert.Contains(dashboard.Kpis, tile => tile.Key == "blocked_tasks" && tile.CurrentValue == 3 && tile.BaselineValue == 0 && tile.TrendDirection == "up");
        Assert.Contains(dashboard.Kpis, tile => tile.Key == "completed_tasks" && tile.CurrentValue == 1 && tile.BaselineValue == 1 && tile.TrendDirection == "flat");

        var anomaly = Assert.Single(dashboard.Anomalies, item => item.KpiKey == "blocked_tasks");
        Assert.Equal("critical", anomaly.Severity);
        Assert.Equal("Operations", anomaly.Department);
        Assert.Equal("threshold_breach", anomaly.Reason);
        Assert.Equal(3, anomaly.CurrentValue);
        Assert.Equal(3, anomaly.ThresholdValue);
        Assert.Contains("department=Operations", anomaly.Route, StringComparison.OrdinalIgnoreCase);

        var salesResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/executive-cockpit/kpis?department=Sales&startUtc={Uri.EscapeDataString(startUtc.ToString("O"))}&endUtc={Uri.EscapeDataString(endUtc.ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, salesResponse.StatusCode);
        var salesDashboard = await salesResponse.Content.ReadFromJsonAsync<KpiDashboardResponse>();
        Assert.NotNull(salesDashboard);
        Assert.All(salesDashboard!.Kpis, tile => Assert.Equal("Sales", tile.Department));
        Assert.Contains(salesDashboard.Kpis, tile => tile.Key == "blocked_tasks" && tile.CurrentValue == 0);
        Assert.DoesNotContain(salesDashboard.Anomalies, item => item.Department == "Operations");
    }

    [Fact]
    public async Task Kpis_filter_by_time_range()
    {
        var seed = await SeedKpiCompanyAsync();
        using var client = CreateAuthenticatedClient();

        var currentStartUtc = DateTime.UtcNow.AddDays(-7);
        var currentEndUtc = DateTime.UtcNow.AddDays(1);
        var currentResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/executive-cockpit/kpis?department=Operations&startUtc={Uri.EscapeDataString(currentStartUtc.ToString("O"))}&endUtc={Uri.EscapeDataString(currentEndUtc.ToString("O"))}");

        Assert.Equal(HttpStatusCode.OK, currentResponse.StatusCode);
        var currentDashboard = await currentResponse.Content.ReadFromJsonAsync<KpiDashboardResponse>();
        Assert.NotNull(currentDashboard);
        Assert.Contains(currentDashboard!.Kpis, tile => tile.Key == "blocked_tasks" && tile.CurrentValue == 3);

        var historicalStartUtc = DateTime.UtcNow.AddDays(-30);
        var historicalEndUtc = DateTime.UtcNow.AddDays(-20);
        var historicalResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/executive-cockpit/kpis?department=Operations&startUtc={Uri.EscapeDataString(historicalStartUtc.ToString("O"))}&endUtc={Uri.EscapeDataString(historicalEndUtc.ToString("O"))}");

        Assert.Equal(HttpStatusCode.OK, historicalResponse.StatusCode);
        var historicalDashboard = await historicalResponse.Content.ReadFromJsonAsync<KpiDashboardResponse>();
        Assert.NotNull(historicalDashboard);
        Assert.Contains(historicalDashboard!.Kpis, tile => tile.Key == "blocked_tasks" && tile.CurrentValue == 0);
        Assert.DoesNotContain(historicalDashboard.Anomalies, item => item.KpiKey == "blocked_tasks");
    }

    [Fact]
    public async Task Kpis_are_tenant_scoped()
    {
        var seed = await SeedKpiCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/companies/{seed.OtherCompanyId}/executive-cockpit/kpis");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "kpi-founder");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "kpi-founder@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "KPI Founder");
        return client;
    }

    private async Task<KpiSeed> SeedKpiCompanyAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var operationsAgentId = Guid.NewGuid();
        var salesAgentId = Guid.NewGuid();
        var workflowDefinitionId = Guid.NewGuid();
        var salesWorkflowDefinitionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "kpi-founder@example.com", "KPI Founder", "dev-header", "kpi-founder"));
            dbContext.Companies.AddRange(new Company(companyId, "KPI Company"), new Company(otherCompanyId, "Other KPI Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            dbContext.Agents.AddRange(
                new Agent(operationsAgentId, companyId, "ops", "Avery Ops", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active),
                new Agent(salesAgentId, companyId, "sales", "Sasha Sales", "Sales Lead", "Sales", null, AgentSeniority.Lead, AgentStatus.Active));

            dbContext.WorkflowDefinitions.AddRange(
                new WorkflowDefinition(workflowDefinitionId, companyId, "ops-kpi", "Operations KPI workflow", "Operations", WorkflowTriggerType.Manual, 1, Payload(("steps", new JsonArray()))),
                new WorkflowDefinition(salesWorkflowDefinitionId, companyId, "sales-kpi", "Sales KPI workflow", "Sales", WorkflowTriggerType.Manual, 1, Payload(("steps", new JsonArray()))));

            for (var index = 0; index < 3; index++)
            {
                var blockedTask = new WorkTask(Guid.NewGuid(), companyId, "ops", $"Blocked operation {index}", null, WorkTaskPriority.High, operationsAgentId, null, "user", userId);
                blockedTask.UpdateStatus(WorkTaskStatus.Blocked);
                SetTaskTimestamps(blockedTask, now.AddDays(-2), now.AddDays(-2), null);
                dbContext.WorkTasks.Add(blockedTask);
            }

            var completedCurrent = new WorkTask(Guid.NewGuid(), companyId, "ops", "Close current operation", null, WorkTaskPriority.Normal, operationsAgentId, null, "user", userId);
            completedCurrent.UpdateStatus(WorkTaskStatus.Completed);
            SetTaskTimestamps(completedCurrent, now.AddDays(-1), now.AddDays(-1), now.AddDays(-1));

            var completedBaseline = new WorkTask(Guid.NewGuid(), companyId, "ops", "Close baseline operation", null, WorkTaskPriority.Normal, operationsAgentId, null, "user", userId);
            completedBaseline.UpdateStatus(WorkTaskStatus.Completed);
            SetTaskTimestamps(completedBaseline, now.AddDays(-10), now.AddDays(-10), now.AddDays(-10));

            var salesTask = new WorkTask(Guid.NewGuid(), companyId, "sales", "Follow up lead", null, WorkTaskPriority.Normal, salesAgentId, null, "user", userId);
            salesTask.UpdateStatus(WorkTaskStatus.InProgress);
            SetTaskTimestamps(salesTask, now.AddDays(-2), now.AddDays(-2), null);

            var otherCompanyTask = new WorkTask(Guid.NewGuid(), otherCompanyId, "ops", "Other company blocked", null, WorkTaskPriority.High, Guid.NewGuid(), null, "user", userId);
            otherCompanyTask.UpdateStatus(WorkTaskStatus.Blocked);
            SetTaskTimestamps(otherCompanyTask, now.AddDays(-2), now.AddDays(-2), null);

            dbContext.WorkTasks.AddRange(completedCurrent, completedBaseline, salesTask, otherCompanyTask);
            return Task.CompletedTask;
        });

        return new KpiSeed(companyId, otherCompanyId);
    }

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] properties) =>
        properties.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static void SetTaskTimestamps(WorkTask task, DateTime createdUtc, DateTime updatedUtc, DateTime? completedUtc)
    {
        typeof(WorkTask).GetProperty(nameof(WorkTask.CreatedUtc))!.SetValue(task, createdUtc);
        typeof(WorkTask).GetProperty(nameof(WorkTask.UpdatedUtc))!.SetValue(task, updatedUtc);
        typeof(WorkTask).GetProperty(nameof(WorkTask.CompletedUtc))!.SetValue(task, completedUtc);
    }

    private sealed record KpiSeed(Guid CompanyId, Guid OtherCompanyId);

    private sealed class KpiDashboardResponse
    {
        public Guid CompanyId { get; set; }
        public DateTime GeneratedAtUtc { get; set; }
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public string? Department { get; set; }
        public List<string> Departments { get; set; } = [];
        public List<KpiTileResponse> Kpis { get; set; } = [];
        public List<KpiAnomalyResponse> Anomalies { get; set; } = [];
    }

    private sealed class KpiTileResponse
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public decimal CurrentValue { get; set; }
        public decimal? BaselineValue { get; set; }
        public decimal? DeltaValue { get; set; }
        public decimal? DeltaPercentage { get; set; }
        public string TrendDirection { get; set; } = string.Empty;
        public string ComparisonLabel { get; set; } = string.Empty;
        public DateTime AsOfUtc { get; set; }
        public string? Unit { get; set; }
        public string? Route { get; set; }
    }

    private sealed class KpiAnomalyResponse
    {
        public string KpiKey { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public DateTime OccurredUtc { get; set; }
        public string Reason { get; set; } = string.Empty;
        public decimal CurrentValue { get; set; }
        public decimal? BaselineValue { get; set; }
        public decimal? ThresholdValue { get; set; }
        public decimal? DeviationPercentage { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string? Route { get; set; }
    }
}