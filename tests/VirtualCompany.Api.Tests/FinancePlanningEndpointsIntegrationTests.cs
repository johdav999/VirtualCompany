using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePlanningEndpointsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinancePlanningEndpointsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Budget_and_forecast_endpoints_apply_version_and_range_filters()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var budgetsResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/budgets?periodStartUtc=2026-04-01T00:00:00Z&periodEndUtc=2026-05-01T00:00:00Z&version=baseline");
        Assert.Equal(HttpStatusCode.OK, budgetsResponse.StatusCode);
        var budgets = await budgetsResponse.Content.ReadFromJsonAsync<List<BudgetResponse>>();
        Assert.NotNull(budgets);
        Assert.Equal(2, budgets!.Count);
        Assert.All(budgets, x => Assert.Equal("baseline", x.Version));

        var forecastsResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/forecasts?periodStartUtc=2026-04-01T00:00:00Z&periodEndUtc=2026-06-01T00:00:00Z&financeAccountId={seed.RevenueAccountId}&version=baseline");
        Assert.Equal(HttpStatusCode.OK, forecastsResponse.StatusCode);
        var forecasts = await forecastsResponse.Content.ReadFromJsonAsync<List<ForecastResponse>>();
        Assert.NotNull(forecasts);
        Assert.Equal(3, forecasts!.Count);
        Assert.All(forecasts, x =>
        {
            Assert.Equal(seed.RevenueAccountId, x.FinanceAccountId);
            Assert.Equal("baseline", x.Version);
        });
    }

    [Fact]
    public async Task Budget_create_and_update_endpoints_persist_company_scoped_rows()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var createResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/budgets",
            new
            {
                FinanceAccountId = seed.ExpenseAccountId,
                PeriodStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                Version = "working",
                Amount = 875m,
                Currency = "USD"
            });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<BudgetResponse>();
        Assert.NotNull(created);
        Assert.Equal("working", created!.Version);
        Assert.Equal(875m, created.Amount);

        var updateResponse = await client.PutAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/budgets/{created.Id}",
            new
            {
                FinanceAccountId = seed.ExpenseAccountId,
                PeriodStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                Version = "working",
                Amount = 925m,
                Currency = "USD"
            });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<BudgetResponse>();
        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated!.Id);
        Assert.Equal(925m, updated.Amount);
    }

    [Fact]
    public async Task Budget_create_endpoint_rejects_duplicate_uniqueness_key()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var createResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/budgets",
            new
            {
                FinanceAccountId = seed.RevenueAccountId,
                PeriodStartUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                Version = "baseline",
                Amount = 5500m,
                Currency = "USD"
            });

        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
    }

    [Fact]
    public async Task Budget_endpoint_supports_account_and_cost_center_filters()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/budgets?periodStartUtc=2026-06-01T00:00:00Z&periodEndUtc=2026-06-01T00:00:00Z&version=working&financeAccountId={seed.ExpenseAccountId}&costCenterId={seed.WorkingCostCenterId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var budgets = await response.Content.ReadFromJsonAsync<List<BudgetResponse>>();
        Assert.NotNull(budgets);
        Assert.Single(budgets!);
        Assert.All(budgets!, x => Assert.Equal(seed.WorkingCostCenterId, x.CostCenterId));
    }

    [Fact]
    public async Task Budget_create_endpoint_rejects_cross_tenant_account_reference()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var createResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/budgets",
            new
            {
                FinanceAccountId = seed.OtherCompanyAccountId,
                PeriodStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                Version = "working",
                Amount = 875m,
                Currency = "USD"
            });

        Assert.Equal(HttpStatusCode.BadRequest, createResponse.StatusCode);
    }

    [Fact]
    public async Task Budget_endpoints_enforce_company_membership_and_hide_cross_tenant_rows()
    {
        var seed = await SeedAsync();

        using var outsiderClient = CreateAuthenticatedClient(seed.OutsiderSubject, seed.OutsiderEmail, seed.OutsiderDisplayName);
        var forbiddenResponse = await outsiderClient.GetAsync(
            $"/internal/companies/{seed.CompanyId}/finance/budgets?periodStartUtc=2026-04-01T00:00:00Z&periodEndUtc=2026-05-01T00:00:00Z");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        using var ownerClient = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var crossTenantUpdate = await ownerClient.PutAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/budgets/{seed.OtherCompanyBudgetId}",
            new
            {
                FinanceAccountId = seed.ExpenseAccountId,
                PeriodStartUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                Version = "working",
                Amount = 925m,
                Currency = "USD"
            });

        Assert.Equal(HttpStatusCode.NotFound, crossTenantUpdate.StatusCode);
    }

    private async Task<PlanningEndpointSeed> SeedAsync()
    {
        var userId = Guid.NewGuid();
        var outsiderUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var subject = $"planning-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Planning Owner";
        var outsiderSubject = $"planning-outsider-{Guid.NewGuid():N}";
        var outsiderEmail = $"{outsiderSubject}@example.com";
        const string outsiderDisplayName = "Planning Outsider";
        var revenueAccountId = Guid.NewGuid();
        var expenseAccountId = Guid.NewGuid();
        var otherCompanyAccountId = Guid.NewGuid();
        var otherCompanyBudgetId = Guid.NewGuid();
        var workingCostCenterId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(userId, email, displayName, "dev-header", subject),
                new User(outsiderUserId, outsiderEmail, outsiderDisplayName, "dev-header", outsiderSubject));

            var company = new Company(companyId, "Planning Company");
            company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
            var otherCompany = new Company(otherCompanyId, "Other Planning Company");
            otherCompany.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
            dbContext.Companies.AddRange(company, otherCompany);
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));

            dbContext.FinanceAccounts.AddRange(
                new FinanceAccount(
                    revenueAccountId,
                    companyId,
                    "4000",
                    "Sales",
                    "revenue",
                    "USD",
                    0m,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new FinanceAccount(
                    expenseAccountId,
                    companyId,
                    "5000",
                    "Payroll",
                    "expense",
                    "USD",
                    0m,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new FinanceAccount(
                    otherCompanyAccountId,
                    otherCompanyId,
                    "4000",
                    "External Sales",
                    "revenue",
                    "USD",
                    0m,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

            dbContext.Budgets.AddRange(
                new Budget(Guid.NewGuid(), companyId, revenueAccountId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), "baseline", 5000m, "USD"),
                new Budget(Guid.NewGuid(), companyId, expenseAccountId, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), "baseline", 1800m, "USD"),
                new Budget(Guid.NewGuid(), companyId, expenseAccountId, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), "working", 1900m, "USD"),
                new Budget(Guid.NewGuid(), companyId, expenseAccountId, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), "working", 2200m, "USD", workingCostCenterId),
                new Budget(otherCompanyBudgetId, otherCompanyId, otherCompanyAccountId, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), "baseline", 6400m, "USD"));

            dbContext.Forecasts.AddRange(
                new Forecast(Guid.NewGuid(), companyId, revenueAccountId, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), "baseline", 5100m, "USD"),
                new Forecast(Guid.NewGuid(), companyId, revenueAccountId, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), "baseline", 5200m, "USD"),
                new Forecast(Guid.NewGuid(), companyId, revenueAccountId, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), "baseline", 5300m, "USD"),
                new Forecast(Guid.NewGuid(), companyId, expenseAccountId, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), "working", 2100m, "USD"));

            return Task.CompletedTask;
        });

        return new PlanningEndpointSeed(
            companyId,
            subject,
            email,
            displayName,
            outsiderSubject,
            outsiderEmail,
            outsiderDisplayName,
            revenueAccountId,
            expenseAccountId,
            otherCompanyAccountId,
            otherCompanyBudgetId,
            workingCostCenterId);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName) {        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record PlanningEndpointSeed(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName,
        string OutsiderSubject,
        string OutsiderEmail,
        string OutsiderDisplayName,
        Guid RevenueAccountId,
        Guid ExpenseAccountId,
        Guid OtherCompanyAccountId,
        Guid OtherCompanyBudgetId,
        Guid WorkingCostCenterId);

    private sealed class BudgetResponse
    {
        public Guid Id { get; set; }
        public Guid FinanceAccountId { get; set; }
        public string Version { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public Guid? CostCenterId { get; set; }
    }

    private sealed class ForecastResponse
    {
        public Guid Id { get; set; }
        public Guid FinanceAccountId { get; set; }
        public string Version { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }
}