using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ExecutiveCockpitFinanceIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ExecutiveCockpitFinanceIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dashboard_surfaces_finance_widgets_actions_and_low_cash_alert_for_finance_authorized_users()
    {
        var seed = await SeedFinanceCockpitCompanyAsync();

        using var client = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, seed.OwnerDisplayName);
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/executive-cockpit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<FinanceCockpitDashboardResponse>();

        Assert.NotNull(dashboard);
        Assert.NotNull(dashboard!.Finance);
        Assert.Equal("critical", dashboard.Finance!.Runway.Status, ignoreCase: true);
        Assert.Equal("Critical", dashboard.Finance.Runway.StatusLabel);
        Assert.False(string.IsNullOrWhiteSpace(dashboard.Finance.Runway.DisplayValue));
        Assert.Equal("USD 400.00", dashboard.Finance.CashPosition.DisplayValue);
        Assert.False(string.IsNullOrWhiteSpace(dashboard.Finance.CashPosition.TrendDisplay));
        Assert.NotEqual(default, dashboard.Finance.CashPosition.LastRefreshedUtc);
        Assert.NotNull(dashboard.Finance.LowCashAlert);
        Assert.False(string.IsNullOrWhiteSpace(dashboard.Finance.LowCashAlert!.Summary));
        Assert.NotEmpty(dashboard.Finance.LowCashAlert!.ContributingFactors);
        Assert.Contains(dashboard.Finance.DeepLinks, x => x.Key == "finance_workspace" && x.Route.Contains("/finance?companyId=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dashboard.Finance.DeepLinks, x => x.Key == "anomaly_workbench" && x.Route.Contains("/finance/anomalies", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dashboard.Finance.DeepLinks, x => x.Key == "finance_summary" && x.Route.Contains("/finance/monthly-summary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dashboard.Finance.DeepLinks, x => x.Key == "cash_detail" && x.Route.Contains("/finance/cash-position", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dashboard.Finance.AvailableActions, x => x.Key == "review_invoice" && x.OrchestrationEndpoint!.Contains("/review-workflow", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dashboard.Finance.AvailableActions, x => x.Key == "inspect_anomaly" && x.OrchestrationEndpoint!.Contains("/anomaly-evaluation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dashboard.Finance.AvailableActions, x => x.Key == "view_cash_position" && x.OrchestrationEndpoint!.Contains("/cash-position/evaluation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dashboard.Finance.AvailableActions, x => x.Key == "open_finance_summary");

        var alertDetailResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/executive-cockpit/finance-alerts/{dashboard.Finance.LowCashAlert.AlertId}");
        Assert.Equal(HttpStatusCode.OK, alertDetailResponse.StatusCode);
        var alertDetail = await alertDetailResponse.Content.ReadFromJsonAsync<FinanceAlertDetailResponse>();
        Assert.NotNull(alertDetail);
        Assert.Equal(dashboard.Finance.LowCashAlert.AlertId, alertDetail!.AlertId);
        Assert.NotEmpty(alertDetail.ContributingFactors);
        Assert.NotEmpty(alertDetail.AvailableActions);
        Assert.Contains(alertDetail.AvailableActions, x => x.Key == "open_finance_summary");
        Assert.Contains(alertDetail.Links, x => x.Key == "finance_summary" && x.Route.Contains("/finance/monthly-summary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(alertDetail.Links, x => x.Route.Contains("/finance/cash-position", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(dashboard.Alerts, x => x.Route is not null && x.Route.Contains("/finance/alerts/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Dashboard_hides_finance_widgets_and_actions_for_users_without_finance_policy_access()
    {
        var seed = await SeedFinanceCockpitCompanyAsync();

        using var client = CreateAuthenticatedClient(seed.EmployeeSubject, seed.EmployeeEmail, seed.EmployeeDisplayName);
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/executive-cockpit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dashboard = await response.Content.ReadFromJsonAsync<FinanceCockpitDashboardResponse>();

        Assert.NotNull(dashboard);
        Assert.Null(dashboard!.Finance);
        Assert.Null(dashboard.CashPosition);
    }

    [Fact]
    public async Task Finance_alert_detail_endpoint_denies_company_members_without_finance_policy_access()
    {
        var seed = await SeedFinanceCockpitCompanyAsync();

        using var ownerClient = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, seed.OwnerDisplayName);
        var ownerDashboardResponse = await ownerClient.GetAsync($"/api/companies/{seed.CompanyId}/executive-cockpit");
        var ownerDashboard = await ownerDashboardResponse.Content.ReadFromJsonAsync<FinanceCockpitDashboardResponse>();
        Assert.NotNull(ownerDashboard?.Finance?.LowCashAlert);

        using var employeeClient = CreateAuthenticatedClient(seed.EmployeeSubject, seed.EmployeeEmail, seed.EmployeeDisplayName);
        var response = await employeeClient.GetAsync(
            $"/api/companies/{seed.CompanyId}/executive-cockpit/finance-alerts/{ownerDashboard!.Finance!.LowCashAlert!.AlertId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Finance_action_orchestration_endpoints_allow_authorized_owner_and_reject_unauthorized_employee()
    {
        var seed = await SeedFinanceCockpitCompanyAsync();

        using var ownerClient = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, seed.OwnerDisplayName);
        var dashboardResponse = await ownerClient.GetAsync($"/api/companies/{seed.CompanyId}/executive-cockpit");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);

        var dashboard = await dashboardResponse.Content.ReadFromJsonAsync<FinanceCockpitDashboardResponse>();
        Assert.NotNull(dashboard?.Finance);

        var postActions = dashboard!.Finance!.AvailableActions
            .Where(action => !string.Equals(action.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Contains(postActions, action => action.Key == "review_invoice");
        Assert.Contains(postActions, action => action.Key == "inspect_anomaly");
        Assert.Contains(postActions, action => action.Key == "view_cash_position");
        Assert.All(postActions, action => Assert.False(string.IsNullOrWhiteSpace(action.OrchestrationEndpoint)));

        foreach (var action in postActions)
        {
            Assert.Equal(HttpStatusCode.OK, (await ownerClient.PostAsJsonAsync(action.OrchestrationEndpoint!, new { })).StatusCode);
        }

        using var employeeClient = CreateAuthenticatedClient(seed.EmployeeSubject, seed.EmployeeEmail, seed.EmployeeDisplayName);
        foreach (var action in postActions)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await employeeClient.PostAsJsonAsync(action.OrchestrationEndpoint!, new { })).StatusCode);
        }
    }

    [Fact]
    public void Runway_classifier_maps_thresholds_to_expected_statuses()
    {
        Assert.Equal(FinanceRunwayHealthStatus.Healthy, Application.Cockpit.ExecutiveCockpitFinanceRunwayStatusClassifier.Classify(120, 90, 30));
        Assert.Equal(FinanceRunwayHealthStatus.Warning, Application.Cockpit.ExecutiveCockpitFinanceRunwayStatusClassifier.Classify(60, 90, 30));
        Assert.Equal(FinanceRunwayHealthStatus.Critical, Application.Cockpit.ExecutiveCockpitFinanceRunwayStatusClassifier.Classify(20, 90, 30));
    }

    [Fact]
    public void Finance_widget_key_is_registered_for_widget_refresh_requests()
    {
        Assert.Contains(Application.Cockpit.ExecutiveCockpitWidgetKeys.Finance, Application.Cockpit.ExecutiveCockpitWidgetKeys.All);
    }

    private async Task<FinanceCockpitSeed> SeedFinanceCockpitCompanyAsync()
    {
        var ownerUserId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var ownerSubject = $"cockpit-finance-owner-{Guid.NewGuid():N}";
        var ownerEmail = $"{ownerSubject}@example.com";
        const string ownerDisplayName = "Cockpit Finance Owner";
        var employeeSubject = $"cockpit-finance-employee-{Guid.NewGuid():N}";
        var employeeEmail = $"{employeeSubject}@example.com";
        const string employeeDisplayName = "Cockpit Finance Employee";

        await _factory.SeedAsync(dbContext =>
        {
            var accountId = Guid.NewGuid();
            var counterpartyId = Guid.NewGuid();
            var invoiceId = Guid.NewGuid();
            var transactionId = Guid.NewGuid();

            dbContext.Users.AddRange(
                new User(ownerUserId, ownerEmail, ownerDisplayName, "dev-header", ownerSubject),
                new User(employeeUserId, employeeEmail, employeeDisplayName, "dev-header", employeeSubject));
            dbContext.Companies.Add(new Company(companyId, "Cockpit Finance Company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeUserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));

            dbContext.FinanceAccounts.Add(new FinanceAccount(
                accountId,
                companyId,
                "1000",
                "Operating Cash",
                "asset",
                "USD",
                0m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            dbContext.FinanceCounterparties.Add(new FinanceCounterparty(counterpartyId, companyId, "Vendor", "vendor", "vendor@example.com"));
            dbContext.FinanceBalances.Add(new FinanceBalance(
                Guid.NewGuid(),
                companyId,
                accountId,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                400m,
                "USD"));
            dbContext.FinanceTransactions.AddRange(
                new FinanceTransaction(
                    transactionId,
                    companyId,
                    accountId,
                    counterpartyId,
                    null,
                    null,
                    new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc),
                    "software",
                    -1200m,
                    "USD",
                    "Critical subscription renewal",
                    "COCKPIT-TX-001"),
                new FinanceTransaction(
                    Guid.NewGuid(),
                    companyId,
                    accountId,
                    counterpartyId,
                    null,
                    null,
                    new DateTime(2026, 3, 12, 0, 0, 0, DateTimeKind.Utc),
                    "rent",
                    -600m,
                    "USD",
                    "Office rent",
                    "COCKPIT-TX-002"));
            dbContext.FinanceInvoices.Add(new FinanceInvoice(
                invoiceId,
                companyId,
                counterpartyId,
                "INV-COCKPIT-001",
                new DateTime(2026, 3, 18, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
                950m,
                "USD",
                "open"));
            dbContext.FinanceSeedAnomalies.Add(new FinanceSeedAnomaly(
                Guid.NewGuid(),
                companyId,
                "missing_receipt",
                "cockpit",
                [transactionId],
                """{"expectedDetector":"receipt_completeness"}"""));

            return Task.CompletedTask;
        });

        return new FinanceCockpitSeed(companyId, ownerSubject, ownerEmail, ownerDisplayName, employeeSubject, employeeEmail, employeeDisplayName);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record FinanceCockpitSeed(
        Guid CompanyId,
        string OwnerSubject,
        string OwnerEmail,
        string OwnerDisplayName,
        string EmployeeSubject,
        string EmployeeEmail,
        string EmployeeDisplayName);

    private sealed class FinanceCockpitDashboardResponse
    {
        public Guid CompanyId { get; set; }
        public object? CashPosition { get; set; }
        public List<ExecutiveAlertResponse> Alerts { get; set; } = [];
        public FinanceCockpitSectionResponse? Finance { get; set; }
    }

    private sealed class ExecutiveAlertResponse
    {
        public string Title { get; set; } = string.Empty;
        public string? Route { get; set; }
    }

    private sealed class FinanceCockpitSectionResponse
    {
        public FinanceCockpitCashResponse CashPosition { get; set; } = new();
        public FinanceCockpitRunwayResponse Runway { get; set; } = new();
        public FinanceAlertDetailResponse? LowCashAlert { get; set; }
        public List<FinanceCockpitActionResponse> AvailableActions { get; set; } = [];
        public List<FinanceCockpitLinkResponse> DeepLinks { get; set; } = [];
    }

    private sealed class FinanceCockpitCashResponse
    {
        public string DisplayValue { get; set; } = string.Empty;
        public string TrendDisplay { get; set; } = string.Empty;
        public DateTime LastRefreshedUtc { get; set; }
    }

    private sealed class FinanceCockpitRunwayResponse
    {
        public string Status { get; set; } = string.Empty;
        public string DisplayValue { get; set; } = string.Empty;
        public string StatusLabel { get; set; } = string.Empty;
    }

    private sealed class FinanceAlertDetailResponse
    {
        public Guid AlertId { get; set; }
        public string Summary { get; set; } = string.Empty;
        public List<string> ContributingFactors { get; set; } = [];
        public List<FinanceCockpitActionResponse> AvailableActions { get; set; } = [];
        public List<FinanceCockpitLinkResponse> Links { get; set; } = [];
    }

    private sealed class FinanceCockpitActionResponse
    {
        public string Key { get; set; } = string.Empty;
        public string HttpMethod { get; set; } = string.Empty;
        public string? OrchestrationEndpoint { get; set; }
    }

    private sealed class FinanceCockpitLinkResponse
    {
        public string Key { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
    }
}