using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;
using VirtualCompany.Application.Cockpit;

namespace VirtualCompany.Api.Tests;

public sealed class DepartmentDashboardConfigurationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DepartmentDashboardConfigurationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Department_sections_are_returned_in_deterministic_order_with_ordered_widgets()
    {
        var companyId = await SeedCompanyAsync("dashboard-owner-order", "dashboard-owner-order@example.com", CompanyMembershipRole.Owner);

        using var client = CreateAuthenticatedClient("dashboard-owner-order", "dashboard-owner-order@example.com");
        var response = await client.GetAsync($"/api/companies/{companyId}/executive-cockpit/composition");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<DepartmentDashboardConfigurationResponse>();

        Assert.NotNull(dashboard);
        Assert.Equal(["finance", "sales", "support", "operations"], dashboard!.Sections.Select(x => x.DepartmentKey).ToArray());
        Assert.All(dashboard.Sections, section =>
        {
            Assert.NotEmpty(section.Widgets);
            Assert.True(section.IsVisible);
            Assert.Equal(section.Widgets.OrderBy(x => x.DisplayOrder).ThenBy(x => x.WidgetKey).Select(x => x.WidgetKey), section.Widgets.Select(x => x.WidgetKey));
            Assert.NotEmpty(section.Navigation.Route);
            Assert.NotNull(section.EmptyState);
            Assert.All(section.Widgets, widget => Assert.True(widget.IsVisible));
        });
    }

    [Fact]
    public async Task Department_sections_filter_by_membership_role()
    {
        var ownerCompanyId = await SeedCompanyAsync("dashboard-owner-role", "dashboard-owner-role@example.com", CompanyMembershipRole.Owner);
        var employeeCompanyId = await SeedCompanyAsync("dashboard-employee-role", "dashboard-employee-role@example.com", CompanyMembershipRole.Employee);
        var financeCompanyId = await SeedCompanyAsync("dashboard-finance-role", "dashboard-finance-role@example.com", CompanyMembershipRole.FinanceApprover);

        using var ownerClient = CreateAuthenticatedClient("dashboard-owner-role", "dashboard-owner-role@example.com");
        var ownerResponse = await ownerClient.GetAsync($"/api/companies/{ownerCompanyId}/executive-cockpit/department-sections");
        var ownerDashboard = await ownerResponse.Content.ReadFromJsonAsync<DepartmentDashboardConfigurationResponse>();

        using var employeeClient = CreateAuthenticatedClient("dashboard-employee-role", "dashboard-employee-role@example.com");
        var employeeResponse = await employeeClient.GetAsync($"/api/companies/{employeeCompanyId}/executive-cockpit/department-sections");
        var employeeDashboard = await employeeResponse.Content.ReadFromJsonAsync<DepartmentDashboardConfigurationResponse>();

        using var financeClient = CreateAuthenticatedClient("dashboard-finance-role", "dashboard-finance-role@example.com");
        var financeResponse = await financeClient.GetAsync($"/api/companies/{financeCompanyId}/executive-cockpit/department-sections");
        var financeDashboard = await financeResponse.Content.ReadFromJsonAsync<DepartmentDashboardConfigurationResponse>();

        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, employeeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, financeResponse.StatusCode);
        Assert.Equal(["finance", "sales", "support", "operations"], ownerDashboard!.Sections.Select(x => x.DepartmentKey).ToArray());
        Assert.Empty(employeeDashboard!.Sections);
        Assert.Equal(["finance"], financeDashboard!.Sections.Select(x => x.DepartmentKey).ToArray());
    }

    [Fact]
    public async Task Widget_visibility_is_filtered_inside_visible_sections()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var departmentConfigId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "dashboard-widget-owner@example.com", "Widget Owner", "dev-header", "dashboard-widget-owner"));
            dbContext.Companies.Add(new Company(companyId, "Widget Visibility Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            var section = new DashboardDepartmentConfig(
                departmentConfigId,
                companyId,
                "finance",
                "Finance",
                99,
                true,
                "cash-stack",
                Nodes(("label", JsonValue.Create("Open Finance")), ("route", JsonValue.Create($"/dashboard?companyId={companyId}&department=finance"))),
                Roles("owner"),
                Nodes(("title", JsonValue.Create("Finance is ready")), ("message", JsonValue.Create("Finance has no activity yet."))));

            section.AddWidget(new DashboardWidgetConfig(
                Guid.NewGuid(),
                companyId,
                departmentConfigId,
                "finance_owner_widget",
                "Owner widget",
                "summary_count",
                20,
                true,
                DepartmentDashboardSummaryBindings.OpenTasks,
                Nodes(("label", JsonValue.Create("Open owner widget")), ("route", JsonValue.Create($"/dashboard?companyId={companyId}&department=finance&widget=owner"))),
                Roles("owner"),
                Nodes(("title", JsonValue.Create("Owner widget")), ("message", JsonValue.Create("Owner data is empty.")))));
            section.AddWidget(new DashboardWidgetConfig(
                Guid.NewGuid(),
                companyId,
                departmentConfigId,
                "finance_admin_widget",
                "Admin-only widget",
                "summary_count",
                10,
                true,
                DepartmentDashboardSummaryBindings.PendingApprovals,
                Nodes(("label", JsonValue.Create("Open admin widget")), ("route", JsonValue.Create($"/dashboard?companyId={companyId}&department=finance&widget=admin"))),
                Roles("admin"),
                Nodes(("title", JsonValue.Create("Admin widget")), ("message", JsonValue.Create("Admin data is empty.")))));
            dbContext.DashboardDepartmentConfigs.Add(section);
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient("dashboard-widget-owner", "dashboard-widget-owner@example.com");
        var response = await client.GetAsync($"/api/companies/{companyId}/executive-cockpit/department-sections");
        var dashboard = await response.Content.ReadFromJsonAsync<DepartmentDashboardConfigurationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var finance = Assert.Single(dashboard!.Sections, x => x.DepartmentKey == "finance");
        Assert.Equal(10, finance.DisplayOrder);
        var widget = Assert.Single(finance.Widgets);
        Assert.Equal("finance_owner_widget", widget.WidgetKey);
        Assert.DoesNotContain(finance.Widgets, x => x.WidgetKey == "finance_admin_widget");
    }

    [Fact]
    public async Task Department_sections_are_tenant_scoped()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var agentA = Guid.NewGuid();
        var agentB = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(userA, "dashboard-tenant-a@example.com", "Tenant A", "dev-header", "dashboard-tenant-a"),
                new User(userB, "dashboard-tenant-b@example.com", "Tenant B", "dev-header", "dashboard-tenant-b"));
            dbContext.Companies.AddRange(
                new Company(companyA, "Tenant A Dashboard Company"),
                new Company(companyB, "Tenant B Dashboard Company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyA, userA, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyB, userB, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.AddRange(
                new Agent(agentA, companyA, "ops", "Ops A", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active),
                new Agent(agentB, companyB, "ops", "Ops B", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));
            dbContext.WorkTasks.Add(new WorkTask(Guid.NewGuid(), companyB, "ops", "Other company task", null, WorkTaskPriority.Normal, agentB, null, "user", userB));
            return Task.CompletedTask;
        });

        using var clientA = CreateAuthenticatedClient("dashboard-tenant-a", "dashboard-tenant-a@example.com");
        var responseA = await clientA.GetAsync($"/api/companies/{companyA}/executive-cockpit/department-sections");
        var dashboardA = await responseA.Content.ReadFromJsonAsync<DepartmentDashboardConfigurationResponse>();

        var forbidden = await clientA.GetAsync($"/api/companies/{companyB}/executive-cockpit/department-sections");

        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var operationsA = Assert.Single(dashboardA!.Sections, x => x.DepartmentKey == "operations");
        Assert.Equal(1, operationsA.SummaryCounts["active_agents"]);
        Assert.Equal(0, operationsA.SummaryCounts["open_tasks"]);
    }

    [Fact]
    public async Task Empty_departments_return_fallback_metadata_and_zero_counts()
    {
        var companyId = await SeedCompanyAsync("dashboard-empty-owner", "dashboard-empty-owner@example.com", CompanyMembershipRole.Owner);

        using var client = CreateAuthenticatedClient("dashboard-empty-owner", "dashboard-empty-owner@example.com");
        var response = await client.GetAsync($"/api/companies/{companyId}/executive-cockpit/department-sections");
        var dashboard = await response.Content.ReadFromJsonAsync<DepartmentDashboardConfigurationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var finance = Assert.Single(dashboard!.Sections, x => x.DepartmentKey == "finance");
        Assert.False(finance.HasData);
        Assert.True(finance.IsEmpty);
        Assert.All(finance.SummaryCounts.Values, value => Assert.Equal(0, value));
        Assert.NotEmpty(finance.EmptyState.Title);
        Assert.NotEmpty(finance.EmptyState.Message);
        Assert.NotEmpty(finance.Navigation.Route);
        Assert.All(finance.Widgets, widget =>
        {
            Assert.Equal(0, widget.SummaryValue);
            Assert.False(widget.HasData);
            Assert.True(widget.IsEmpty);
            Assert.NotEmpty(widget.EmptyState.Message);
        });
    }

    [Fact]
    public void Visibility_evaluator_denies_missing_roles_and_cross_tenant_context()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var membership = new VirtualCompany.Application.Auth.ResolvedCompanyMembershipContext(
            Guid.NewGuid(),
            companyId,
            Guid.NewGuid(),
            "Policy Company",
            CompanyMembershipRole.Owner,
            CompanyMembershipStatus.Active);

        Assert.False(DepartmentDashboardVisibility.IsVisibleToMembership(new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase), membership, companyId));
        Assert.False(DepartmentDashboardVisibility.IsVisibleToMembership(Roles("owner"), membership, otherCompanyId));
        Assert.True(DepartmentDashboardVisibility.IsVisibleToMembership(Roles("owner"), membership, companyId));
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, subject);
        return client;
    }

    private async Task<Guid> SeedCompanyAsync(string subject, string email, CompanyMembershipRole role)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, subject, "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, $"{subject} Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, role, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return companyId;
    }

    private static Dictionary<string, JsonNode?> Roles(params string[] roles) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["roles"] = new JsonArray(roles.Select(role => JsonValue.Create(role)).ToArray<JsonNode?>())
        };

    private static Dictionary<string, JsonNode?> Nodes(params (string Key, JsonNode? Value)[] properties) =>
        properties.ToDictionary(
            pair => pair.Key,
            pair => pair.Value?.DeepClone(),
            StringComparer.OrdinalIgnoreCase);

    private sealed class DepartmentDashboardConfigurationResponse
    {
        public Guid CompanyId { get; set; }
        public List<DepartmentDashboardSectionResponse> Sections { get; set; } = [];
    }

    private sealed class DepartmentDashboardSectionResponse
    {
        public Guid Id { get; set; }
        public string DepartmentKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool IsVisible { get; set; }
        public string? Icon { get; set; }
        public bool HasData { get; set; }
        public Dictionary<string, int> SummaryCounts { get; set; } = [];
        public bool IsEmpty { get; set; }
        public DepartmentDashboardNavigationResponse Navigation { get; set; } = new();
        public DepartmentDashboardEmptyStateResponse EmptyState { get; set; } = new();
        public List<DepartmentDashboardWidgetResponse> Widgets { get; set; } = [];
    }

    private sealed class DepartmentDashboardWidgetResponse
    {
        public Guid Id { get; set; }
        public string WidgetKey { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string WidgetType { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool IsVisible { get; set; }
        public string SummaryBinding { get; set; } = string.Empty;
        public int SummaryValue { get; set; }
        public bool HasData { get; set; }
        public bool IsEmpty { get; set; }
        public DepartmentDashboardNavigationResponse Navigation { get; set; } = new();
        public DepartmentDashboardEmptyStateResponse EmptyState { get; set; } = new();
    }

    private sealed class DepartmentDashboardNavigationResponse
    {
        public string Label { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
    }

    private sealed class DepartmentDashboardEmptyStateResponse
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? ActionLabel { get; set; }
        public string? ActionRoute { get; set; }
    }
}