using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class DashboardFinanceSnapshotIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DashboardFinanceSnapshotIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Finance_snapshot_returns_cash_burn_rate_runway_and_risk_level()
    {
        var seed = await SeedFinanceCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/dashboard/finance-snapshot?companyId={seed.CompanyId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await response.Content.ReadFromJsonAsync<FinanceSnapshotResponse>();

        Assert.NotNull(snapshot);
        Assert.Equal(seed.CompanyId, snapshot!.CompanyId);
        Assert.Equal(60000m, snapshot.Cash);
        Assert.Equal(1000m, snapshot.BurnRate);
        Assert.Equal(60, snapshot.RunwayDays);
        Assert.Equal("USD", snapshot.Currency);
        Assert.Equal("warning", snapshot.RiskLevel);
        Assert.True(snapshot.HasFinanceData);
    }

    [Fact]
    public async Task Finance_snapshot_returns_missing_state_when_finance_data_is_incomplete()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "dashboard-finance-missing@example.com", "Dashboard Finance Missing", "dev-header", "dashboard-finance-missing"));
            dbContext.Companies.Add(new Company(companyId, "Dashboard Finance Missing"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/dashboard/finance-snapshot?companyId={companyId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await response.Content.ReadFromJsonAsync<FinanceSnapshotResponse>();

        Assert.NotNull(snapshot);
        Assert.False(snapshot!.HasFinanceData);
        Assert.Equal("missing", snapshot.RiskLevel);
        Assert.Equal(0m, snapshot.Cash);
        Assert.Equal(0m, snapshot.BurnRate);
        Assert.Null(snapshot.RunwayDays);
    }

    [Fact]
    public async Task Finance_snapshot_averages_last_thirty_days_of_expenses_and_keeps_company_data_isolated()
    {
        var asOfUtc = new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);
        using var factory = new TestWebApplicationFactory(new FixedTimeProvider(asOfUtc));

        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var cashAccountId = Guid.NewGuid();
        var expenseAccountId = Guid.NewGuid();
        var otherCashAccountId = Guid.NewGuid();
        var otherExpenseAccountId = Guid.NewGuid();

        await factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "dashboard-finance-window@example.com", "Dashboard Finance Window", "dev-header", "dashboard-finance-user"));
            dbContext.Companies.AddRange(
                new Company(companyId, "Windowed Finance Co"),
                new Company(otherCompanyId, "Other Finance Co"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            dbContext.FinanceAccounts.AddRange(
                new FinanceAccount(cashAccountId, companyId, "1000", "Operating Cash", "cash", "USD", 60000m, asOfUtc.AddMonths(-2).UtcDateTime),
                new FinanceAccount(expenseAccountId, companyId, "6000", "Operating Expenses", "expense", "USD", 0m, asOfUtc.AddMonths(-2).UtcDateTime),
                new FinanceAccount(otherCashAccountId, otherCompanyId, "1000", "Other Cash", "cash", "USD", 200000m, asOfUtc.AddMonths(-2).UtcDateTime),
                new FinanceAccount(otherExpenseAccountId, otherCompanyId, "6000", "Other Expenses", "expense", "USD", 0m, asOfUtc.AddMonths(-2).UtcDateTime));

            dbContext.FinanceTransactions.AddRange(
                new FinanceTransaction(Guid.NewGuid(), companyId, expenseAccountId, null, null, null, asOfUtc.AddDays(-5).UtcDateTime, "operating_expense", -12000m, "USD", "Payroll", "exp-1"),
                new FinanceTransaction(Guid.NewGuid(), companyId, expenseAccountId, null, null, null, asOfUtc.AddDays(-15).UtcDateTime, "operating_expense", -9000m, "USD", "Infrastructure", "exp-2"),
                new FinanceTransaction(Guid.NewGuid(), companyId, expenseAccountId, null, null, null, asOfUtc.AddDays(-29).UtcDateTime, "operating_expense", -9000m, "USD", "Contractors", "exp-3"),
                new FinanceTransaction(Guid.NewGuid(), companyId, expenseAccountId, null, null, null, asOfUtc.AddDays(-31).UtcDateTime, "operating_expense", -15000m, "USD", "Older expense", "exp-4"),
                new FinanceTransaction(Guid.NewGuid(), otherCompanyId, otherExpenseAccountId, null, null, null, asOfUtc.AddDays(-7).UtcDateTime, "operating_expense", -60000m, "USD", "Other payroll", "other-exp-1"));

            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(factory);
        var response = await client.GetAsync($"/api/dashboard/finance-snapshot?companyId={companyId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync();
        var snapshot = JsonSerializer.Deserialize<FinanceSnapshotResponse>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(snapshot);
        Assert.Equal(companyId, snapshot!.CompanyId);
        Assert.Equal(60000m, snapshot.Cash);
        Assert.Equal(1000m, snapshot.BurnRate);
        Assert.Equal(60, snapshot.RunwayDays);
        Assert.Equal("warning", snapshot.RiskLevel);
        Assert.True(snapshot.HasFinanceData);
        Assert.Contains("\"cash\":", payload);
        Assert.Contains("\"burnRate\":", payload);
        Assert.Contains("\"runwayDays\":", payload);
        Assert.Contains("\"riskLevel\":", payload);
    }

    private HttpClient CreateAuthenticatedClient(TestWebApplicationFactory? factory = null)
    {
        var client = (factory ?? _factory).CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "dashboard-finance-user");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "dashboard-finance@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Dashboard Finance");
        return client;
    }

    private async Task<(Guid CompanyId)> SeedFinanceCompanyAsync()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "dashboard-finance@example.com", "Dashboard Finance", "dev-header", "dashboard-finance-user"));
            dbContext.Companies.Add(new Company(companyId, "Dashboard Finance Co"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            dbContext.FinanceAccounts.Add(new FinanceAccount(
                accountId,
                companyId,
                "1000",
                "Operating Cash",
                "cash",
                "USD",
                90000m,
                nowUtc.AddMonths(-2)));

            dbContext.FinanceTransactions.AddRange(
                new FinanceTransaction(Guid.NewGuid(), companyId, accountId, null, null, null, nowUtc.AddDays(-10), "operating_expense", -15000m, "USD", "Payroll", "exp-1"),
                new FinanceTransaction(Guid.NewGuid(), companyId, accountId, null, null, null, nowUtc.AddDays(-20), "operating_expense", -15000m, "USD", "Infrastructure", "exp-2"));

            return Task.CompletedTask;
        });

        return (companyId);
    }

    private sealed class FinanceSnapshotResponse
    {
        public Guid CompanyId { get; set; }
        public decimal Cash { get; set; }
        public decimal BurnRate { get; set; }
        public int? RunwayDays { get; set; }
        public string RiskLevel { get; set; } = string.Empty;
        public bool HasFinanceData { get; set; }
        public string Currency { get; set; } = string.Empty;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}