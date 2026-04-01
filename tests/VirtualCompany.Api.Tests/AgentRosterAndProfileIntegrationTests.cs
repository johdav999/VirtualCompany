using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AgentRosterAndProfileIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AgentRosterAndProfileIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Agent_roster_view_returns_all_company_agents_and_tenant_scoped_filter_options_when_unfiltered()
    {
        var seed = await SeedManagerDirectoryAsync();

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/roster");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentRosterResponse>();
        Assert.NotNull(payload);

        Assert.Equal(3, payload!.Items.Count);
        Assert.Equal(new[] { seed.FinanceAgentId, seed.OperationsAgentId, seed.SupportAgentId }, payload.Items.Select(x => x.Id));
        Assert.DoesNotContain(payload.Items, x => x.Id == seed.OtherCompanyAgentId);

        Assert.Equal(new[] { "Finance", "Operations", "Support" }, payload.Departments);
        Assert.DoesNotContain("Legal", payload.Departments);

        Assert.Equal(new[] { "active", "paused", "restricted", "archived" }, payload.Statuses);
    }

    [Fact]
    public async Task Agent_roster_view_filters_by_department_and_status_and_returns_workload_summary()
    {
        var seed = await SeedManagerDirectoryAsync();

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/roster?department=Finance&status=active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentRosterResponse>();
        Assert.NotNull(payload);

        var item = Assert.Single(payload!.Items);
        Assert.Equal(seed.FinanceAgentId, item.Id);
        Assert.Equal("Nora Ledger", item.DisplayName);
        Assert.Equal("Finance Manager", item.RoleName);
        Assert.Equal("Finance", item.Department);
        Assert.Equal("active", item.Status);
        Assert.Equal("level_2", item.AutonomyLevel);
        Assert.Equal($"/agents/{seed.FinanceAgentId}?companyId={seed.CompanyId}", item.ProfileRoute);
        Assert.Equal(1, item.WorkloadSummary.OpenItemsCount);
        Assert.Equal(1, item.WorkloadSummary.AwaitingApprovalCount);
        Assert.Equal("blocked", item.WorkloadSummary.HealthStatus);
        Assert.Equal("blocked", item.WorkloadSummary.HealthSummary.Status);
        Assert.Equal("Blocked", item.WorkloadSummary.HealthSummary.Label);
        Assert.Equal("1 task is waiting on approval or unblock.", item.WorkloadSummary.HealthSummary.Reason);
        Assert.Equal("Blocked - 1 task is waiting on approval or unblock.", item.WorkloadSummary.Summary);
        Assert.Contains("Finance", payload.Departments);
        Assert.Equal(new[] { "active", "paused", "restricted", "archived" }, payload.Statuses);
    }

    [Fact]
    public async Task Agent_roster_view_uses_inactive_fallback_when_no_task_activity_exists()
    {
        var seed = await SeedManagerDirectoryAsync();

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/roster?department=Support");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentRosterResponse>();
        Assert.NotNull(payload);

        var item = Assert.Single(payload!.Items);
        Assert.Equal(seed.SupportAgentId, item.Id);
        Assert.Equal("Support", item.Department);
        Assert.Equal("paused", item.Status);
    }

    [Fact]
    public async Task Agent_roster_view_filters_by_status_only()
    {
        var seed = await SeedManagerDirectoryAsync();

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/roster?status=active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentRosterResponse>();
        Assert.NotNull(payload);
        Assert.Equal(new[] { seed.FinanceAgentId, seed.OperationsAgentId }, payload!.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Agent_roster_view_uses_unknown_fallback_when_no_task_activity_exists()
    {
        var seed = await SeedManagerDirectoryAsync();

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/roster?department=Operations&status=active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentRosterResponse>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(seed.OperationsAgentId, item.Id);
        Assert.Equal("inactive", item.WorkloadSummary.HealthStatus);
        Assert.Equal("inactive", item.WorkloadSummary.HealthSummary.Status);
        Assert.Equal("Inactive", item.WorkloadSummary.HealthSummary.Label);
        Assert.Equal("No recent task activity has been recorded yet.", item.WorkloadSummary.HealthSummary.Reason);
        Assert.Equal("Inactive - No recent task activity has been recorded yet.", item.WorkloadSummary.Summary);
    }

    [Fact]
    public async Task Agent_profile_view_is_available_to_manager_but_hides_restricted_configuration()
    {
        var seed = await SeedManagerDirectoryAsync();

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/{seed.FinanceAgentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentProfileResponse>();
        Assert.NotNull(payload);
        Assert.False(payload!.Visibility.CanViewPermissions);
        Assert.False(payload.Visibility.CanViewThresholds);
        Assert.False(payload.Visibility.CanEditAgent);
        Assert.NotEmpty(payload.Objectives);
        Assert.NotEmpty(payload.WorkingHours);
        Assert.Empty(payload.ToolPermissions);
        Assert.Empty(payload.ApprovalThresholds);
        Assert.NotEmpty(payload.RecentActivity);
    }

    [Fact]
    public async Task Agent_profile_view_returns_same_derived_health_summary_as_roster()
    {
        var seed = await SeedManagerDirectoryAsync();

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");

        var rosterResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/roster?department=Finance&status=active");
        var rosterPayload = await rosterResponse.Content.ReadFromJsonAsync<AgentRosterResponse>();
        var rosterItem = Assert.Single(rosterPayload!.Items);

        var profileResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/{seed.FinanceAgentId}");
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);

        var profilePayload = await profileResponse.Content.ReadFromJsonAsync<AgentProfileResponse>();
        Assert.NotNull(profilePayload);
        Assert.Equal(rosterItem.WorkloadSummary.HealthSummary.Status, profilePayload!.WorkloadSummary.HealthSummary.Status);
        Assert.Equal(rosterItem.WorkloadSummary.HealthSummary.Label, profilePayload.WorkloadSummary.HealthSummary.Label);
        Assert.Equal(rosterItem.WorkloadSummary.HealthSummary.Reason, profilePayload.WorkloadSummary.HealthSummary.Reason);
        Assert.Equal("blocked", profilePayload.WorkloadSummary.HealthStatus);
        Assert.Equal("Blocked - 1 task is waiting on approval or unblock.", profilePayload.WorkloadSummary.Summary);
    }

    [Fact]
    public async Task Agent_profile_view_hides_sensitive_configuration_for_specialized_non_admin_roles()
    {
        var seed = await SeedManagerDirectoryAsync();

        using var client = CreateAuthenticatedClient("finance-approver", "finance-approver@example.com", "Finance Approver");
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/{seed.FinanceAgentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentProfileResponse>();
        Assert.NotNull(payload);
        Assert.False(payload!.Visibility.CanViewPermissions);
        Assert.False(payload.Visibility.CanViewThresholds);
        Assert.False(payload.Visibility.CanViewWorkingHours);
        Assert.False(payload.Visibility.CanEditAgent);
        Assert.NotEmpty(payload.Objectives);
        Assert.Empty(payload.ToolPermissions);
        Assert.Empty(payload.WorkingHours);
    }

    [Fact]
    public async Task Agent_profile_view_allows_owner_and_stays_company_scoped()
    {
        var seed = await SeedManagerDirectoryAsync();

        using var ownerClient = CreateAuthenticatedClient("owner", "owner@example.com", "Owner");
        var ownerResponse = await ownerClient.GetAsync($"/api/companies/{seed.CompanyId}/agents/{seed.FinanceAgentId}");
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);

        var ownerPayload = await ownerResponse.Content.ReadFromJsonAsync<AgentProfileResponse>();
        Assert.NotNull(ownerPayload);
        Assert.True(ownerPayload!.Visibility.CanViewPermissions);
        Assert.True(ownerPayload.Visibility.CanViewThresholds);
        Assert.True(ownerPayload.Visibility.CanEditAgent);
        Assert.NotEmpty(ownerPayload.ToolPermissions);
        Assert.NotEmpty(ownerPayload.ApprovalThresholds);

        var crossTenantResponse = await ownerClient.GetAsync($"/api/companies/{seed.CompanyId}/agents/{seed.OtherCompanyAgentId}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);

        using var employeeClient = CreateAuthenticatedClient("employee", "employee@example.com", "Employee");
        var employeeResponse = await employeeClient.GetAsync($"/api/companies/{seed.CompanyId}/agents/{seed.FinanceAgentId}");
        Assert.Equal(HttpStatusCode.OK, employeeResponse.StatusCode);

        var employeePayload = await employeeResponse.Content.ReadFromJsonAsync<AgentProfileResponse>();
        Assert.NotNull(employeePayload);
        Assert.False(employeePayload!.Visibility.CanViewPermissions);
        Assert.False(employeePayload.Visibility.CanViewThresholds);
        Assert.False(employeePayload.Visibility.CanEditAgent);
        Assert.NotEmpty(employeePayload.Objectives);
        Assert.NotEmpty(employeePayload.RecentActivity);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private async Task<DirectorySeed> SeedManagerDirectoryAsync()
    {
        var ownerUserId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        var financeApproverUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var financeAgentId = Guid.NewGuid();
        var supportAgentId = Guid.NewGuid();
        var operationsAgentId = Guid.NewGuid();
        var otherCompanyAgentId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(ownerUserId, "owner@example.com", "Owner", "dev-header", "owner"),
                new User(managerUserId, "manager@example.com", "Manager", "dev-header", "manager"),
                new User(employeeUserId, "employee@example.com", "Employee", "dev-header", "employee"),
                new User(financeApproverUserId, "finance-approver@example.com", "Finance Approver", "dev-header", "finance-approver"));

            dbContext.Companies.AddRange(
                new Company(companyId, "Company A"),
                new Company(otherCompanyId, "Company B"));

            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, managerUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeUserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, financeApproverUserId, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active));

            dbContext.Agents.AddRange(
                new Agent(
                    financeAgentId,
                    companyId,
                    "finance",
                    "Nora Ledger",
                    "Finance Manager",
                    "Finance",
                    null,
                    AgentSeniority.Senior,
                    AgentStatus.Active,
                    autonomyLevel: AgentAutonomyLevel.Level2,
                    objectives: Payload(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
                    kpis: Payload(("targets", new JsonArray(JsonValue.Create("forecast_accuracy")))),
                    tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
                    scopes: Payload(("read", new JsonArray(JsonValue.Create("finance")))),
                    thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 5000 })),
                    escalationRules: Payload(("critical", new JsonArray(JsonValue.Create("cash_runway_under_90_days"))), ("escalateTo", JsonValue.Create("owner"))),
                    roleBrief: "Finance operating profile.",
                    workingHours: Payload(("timezone", JsonValue.Create("UTC"))),
                    triggerLogic: Payload(("enabled", JsonValue.Create(true)))),
                new Agent(
                    supportAgentId,
                    companyId,
                    "support",
                    "Casey Support",
                    "Support Lead",
                    "Support",
                    null,
                    AgentSeniority.Lead,
                    AgentStatus.Paused,
                    objectives: Payload(("primary", new JsonArray(JsonValue.Create("Reduce response time"))))),
                new Agent(
                    operationsAgentId,
                    companyId,
                    "operations",
                    "Avery Ops",
                    "Operations Coordinator",
                    "Operations",
                    null,
                    AgentSeniority.Mid,
                    AgentStatus.Active,
                    objectives: Payload(("primary", new JsonArray(JsonValue.Create("Stabilize daily operations"))))),
                new Agent(
                    otherCompanyAgentId,
                    otherCompanyId,
                    "support",
                    "Other Company Legal",
                    "Legal Lead",
                    "Legal",
                    null,
                    AgentSeniority.Lead,
                    AgentStatus.Restricted));

            var execution = new ToolExecutionAttempt(
                executionId,
                companyId,
                financeAgentId,
                "erp",
                ToolActionType.Execute,
                "finance.approval",
                Payload(("amount", JsonValue.Create(1200))));

            var approval = new ApprovalRequest(
                approvalId,
                companyId,
                financeAgentId,
                executionId,
                ownerUserId,
                "erp",
                ToolActionType.Execute,
                "expense",
                Payload(("expenseUsd", JsonValue.Create(1200))));

            execution.MarkAwaitingApproval(approvalId, Payload(("outcome", JsonValue.Create("require_approval"))));

            dbContext.ToolExecutionAttempts.Add(execution);
            dbContext.ApprovalRequests.Add(approval);
            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompany.Infrastructure.Persistence.VirtualCompanyDbContext>();
        dbContext.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            companyId,
            "user",
            ownerUserId,
            "agent.operating_profile.updated",
            "agent",
            financeAgentId.ToString("N"),
            "succeeded",
            "Updated the finance operating profile.",
            new[] { "agent_management" },
            new Dictionary<string, string?>()));
        await dbContext.SaveChangesAsync();

        return new DirectorySeed(companyId, financeAgentId, supportAgentId, operationsAgentId, otherCompanyAgentId);
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

    private sealed record DirectorySeed(Guid CompanyId, Guid FinanceAgentId, Guid SupportAgentId, Guid OperationsAgentId, Guid OtherCompanyAgentId);

    private sealed class AgentRosterResponse
    {
        public List<AgentRosterItem> Items { get; set; } = [];
        public List<string> Departments { get; set; } = [];
        public List<string> Statuses { get; set; } = [];
    }

    private sealed class AgentRosterItem
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string AutonomyLevel { get; set; } = string.Empty;
        public string? ProfileRoute { get; set; }
        public AgentWorkloadSummary WorkloadSummary { get; set; } = new();
    }

    private sealed class AgentProfileResponse
    {
        public Guid Id { get; set; }
        public Dictionary<string, JsonElement> Objectives { get; set; } = [];
        public Dictionary<string, JsonElement> ToolPermissions { get; set; } = [];
        public Dictionary<string, JsonElement> ApprovalThresholds { get; set; } = [];
        public Dictionary<string, JsonElement> WorkingHours { get; set; } = [];
        public List<AgentRecentActivity> RecentActivity { get; set; } = [];
        public AgentWorkloadSummary WorkloadSummary { get; set; } = new();
        public AgentProfileVisibility Visibility { get; set; } = new();
    }

    private sealed class AgentRecentActivity
    {
        public DateTime OccurredUtc { get; set; }
        public string ActivityType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Detail { get; set; }
    }

    private sealed class AgentProfileVisibility
    {
        public bool CanViewPermissions { get; set; }
        public bool CanViewThresholds { get; set; }
        public bool CanViewWorkingHours { get; set; }
        public bool CanEditAgent { get; set; }
        public bool CanPauseOrRestrictAgent { get; set; }
    }

    private sealed class AgentHealthSummary
    {
        public string Status { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    private sealed class AgentWorkloadSummary
    {
        public int OpenItemsCount { get; set; }
        public int AwaitingApprovalCount { get; set; }
        public int ExecutedCount { get; set; }
        public int FailedCount { get; set; }
        public DateTime? LastActivityUtc { get; set; }
        public AgentHealthSummary HealthSummary { get; set; } = new();
        public string HealthStatus { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
    }
}