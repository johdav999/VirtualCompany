using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceVarianceIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly DateTime April2026 = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime June2026 = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly TestWebApplicationFactory _factory;

    public FinanceVarianceIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Variance_endpoint_returns_actual_vs_budget_rows_grouped_by_period_account_and_cost_center()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/variance?periodStartUtc=2026-04-01T00:00:00Z&comparisonType=budget&version=baseline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceVarianceResponse>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!.CompanyId);
        Assert.Equal("budget", payload.ComparisonType);
        Assert.Equal(April2026, payload.PeriodStartUtc);
        Assert.Equal(April2026, payload.PeriodEndUtc);
        Assert.True(payload.IncludesCostCenters);
        Assert.Equal("baseline", payload.Version);
        Assert.Equal(3, payload.Rows.Count);
        Assert.Equal(2300m, payload.Rows.Sum(x => x.ActualAmount));
        Assert.Equal(2250m, payload.Rows.Sum(x => x.ComparisonAmount));

        var payrollOps = Assert.Single(payload.Rows.Where(x => x.AccountCode == "5000" && x.CostCenterId == seed.OperationsCostCenterId));
        Assert.Equal(900m, payrollOps.ActualAmount);
        Assert.Equal(1000m, payrollOps.ComparisonAmount);
        Assert.Equal("expense", payrollOps.CategoryKey);
        Assert.Equal("Expense", payrollOps.CategoryName);
        Assert.Equal(-100m, payrollOps.VarianceAmount);
        Assert.Equal(-10m, payrollOps.VariancePercentage);

        var payrollSales = Assert.Single(payload.Rows.Where(x => x.AccountCode == "5000" && x.CostCenterId == seed.SalesCostCenterId));
        Assert.Equal(1100m, payrollSales.ActualAmount);
        Assert.Equal(1000m, payrollSales.ComparisonAmount);
        Assert.Equal(100m, payrollSales.VarianceAmount);
        Assert.Equal(10m, payrollSales.VariancePercentage);

        var software = Assert.Single(payload.Rows.Where(x => x.AccountCode == "5100" && x.CostCenterId is null));
        Assert.Equal(300m, software.ActualAmount);
        Assert.Equal(250m, software.ComparisonAmount);
        Assert.Equal("expense", software.CategoryKey);
        Assert.Equal("Expense", software.CategoryName);
        Assert.Equal(50m, software.VarianceAmount);
        Assert.Equal(20m, software.VariancePercentage);
    }

    [Fact]
    public async Task Variance_endpoint_returns_actual_vs_forecast_rows_for_the_requested_company_only()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/variance?periodStartUtc=2026-04-01T00:00:00Z&comparisonType=forecast&version=baseline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceVarianceResponse>();
        Assert.NotNull(payload);
        Assert.Equal("forecast", payload!.ComparisonType);
        Assert.Equal(3, payload.Rows.Count);
        Assert.Equal(2150m, payload.Rows.Sum(x => x.ComparisonAmount));
        Assert.DoesNotContain(payload.Rows, x => x.ActualAmount == 700m && x.ComparisonAmount == 700m);

        var payrollOps = Assert.Single(payload.Rows.Where(x => x.AccountCode == "5000" && x.CostCenterId == seed.OperationsCostCenterId));
        Assert.Equal(900m, payrollOps.ActualAmount);
        Assert.Equal(750m, payrollOps.ComparisonAmount);
        Assert.Equal("expense", payrollOps.CategoryKey);
        Assert.Equal("Expense", payrollOps.CategoryName);
        Assert.Equal(150m, payrollOps.VarianceAmount);
        Assert.Equal(20m, payrollOps.VariancePercentage);

        var payrollSales = Assert.Single(payload.Rows.Where(x => x.AccountCode == "5000" && x.CostCenterId == seed.SalesCostCenterId));
        Assert.Equal(1100m, payrollSales.ActualAmount);
        Assert.Equal(1000m, payrollSales.ComparisonAmount);
        Assert.Equal(100m, payrollSales.VarianceAmount);
        Assert.Equal(10m, payrollSales.VariancePercentage);

        var software = Assert.Single(payload.Rows.Where(x => x.AccountCode == "5100" && x.CostCenterId is null));
        Assert.Equal(300m, software.ActualAmount);
        Assert.Equal(400m, software.ComparisonAmount);
        Assert.Equal(-100m, software.VarianceAmount);
        Assert.Equal(-25m, software.VariancePercentage);
    }

    [Fact]
    public async Task Variance_endpoint_groups_rows_by_period_and_account_across_requested_month_range()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/variance?periodStartUtc=2026-04-01T00:00:00Z&periodEndUtc=2026-06-01T00:00:00Z&comparisonType=actual_vs_budget&version=baseline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceVarianceResponse>();
        Assert.NotNull(payload);
        Assert.Equal("budget", payload!.ComparisonType);
        Assert.Equal(April2026, payload.PeriodStartUtc);
        Assert.Equal(June2026, payload.PeriodEndUtc);
        Assert.Equal(5, payload.Rows.Count);
        Assert.Equal(3650m, payload.Rows.Sum(x => x.ActualAmount));
        Assert.Equal(3450m, payload.Rows.Sum(x => x.ComparisonAmount));
        Assert.Equal(
            [April2026, June2026],
            payload.Rows.Select(x => x.PeriodStartUtc).Distinct().OrderBy(x => x).ToArray());
        Assert.DoesNotContain(payload.Rows, x => x.PeriodStartUtc == new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.DoesNotContain(payload.Rows, x => x.ActualAmount == 900m && x.ComparisonAmount == 900m);

        var junePayrollOps = Assert.Single(payload.Rows.Where(x => x.PeriodStartUtc == June2026 && x.AccountCode == "5000" && x.CostCenterId == seed.OperationsCostCenterId));
        Assert.Equal(1200m, junePayrollOps.ActualAmount);
        Assert.Equal(1000m, junePayrollOps.ComparisonAmount);
        Assert.Equal(200m, junePayrollOps.VarianceAmount);
        Assert.Equal(20m, junePayrollOps.VariancePercentage);

        var juneSoftware = Assert.Single(payload.Rows.Where(x => x.PeriodStartUtc == June2026 && x.AccountCode == "5100" && x.CostCenterId is null));
        Assert.Equal(150m, juneSoftware.ActualAmount);
        Assert.Equal(200m, juneSoftware.ComparisonAmount);
        Assert.Equal(-50m, juneSoftware.VarianceAmount);
        Assert.Equal(-25m, juneSoftware.VariancePercentage);
    }

    [Fact]
    public async Task Variance_endpoint_aggregates_without_cost_center_splits_when_cost_centers_are_disabled_for_the_company()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.DisabledCostCenterCompanyId}/finance/variance?periodStartUtc=2026-04-01T00:00:00Z&comparisonType=budget&version=baseline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceVarianceResponse>();
        Assert.NotNull(payload);
        Assert.Equal(seed.DisabledCostCenterCompanyId, payload!.CompanyId);
        Assert.False(payload.IncludesCostCenters);
        Assert.Equal(2, payload.Rows.Count);
        Assert.All(payload.Rows, row => Assert.Null(row.CostCenterId));

        var payroll = Assert.Single(payload.Rows.Where(x => x.AccountCode == "5000"));
        Assert.Equal("expense", payroll.CategoryKey);
        Assert.Equal("Expense", payroll.CategoryName);
        Assert.Equal(1000m, payroll.ActualAmount);
        Assert.Equal(1100m, payroll.ComparisonAmount);
        Assert.Equal(-100m, payroll.VarianceAmount);
        Assert.Equal(-9.09m, payroll.VariancePercentage);

        var software = Assert.Single(payload.Rows.Where(x => x.AccountCode == "5100"));
        Assert.Equal(200m, software.ActualAmount);
        Assert.Equal(250m, software.ComparisonAmount);
        Assert.Equal(-50m, software.VarianceAmount);
        Assert.Equal(-20m, software.VariancePercentage);
    }

    [Fact]
    public async Task Variance_endpoint_enforces_company_membership()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.OutsiderSubject, seed.OutsiderEmail, seed.OutsiderDisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/variance?periodStartUtc=2026-04-01T00:00:00Z&comparisonType=budget&version=baseline");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Variance_endpoint_returns_empty_rows_when_budget_slice_has_no_comparison_data()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/variance?periodStartUtc=2026-05-01T00:00:00Z&comparisonType=budget&version=baseline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceVarianceResponse>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Rows);
        Assert.True(payload.IncludesCostCenters);
    }

    [Fact]
    public async Task Variance_endpoint_returns_empty_rows_when_forecast_slice_has_no_comparison_data()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/variance?periodStartUtc=2026-05-01T00:00:00Z&comparisonType=forecast&version=baseline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinanceVarianceResponse>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Rows);
        Assert.True(payload.IncludesCostCenters);
    }

    private async Task<VarianceSeed> SeedAsync()
    {
        var ownerUserId = Guid.NewGuid();
        var subject = $"variance-owner-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Variance Owner";
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var disabledCostCenterCompanyId = Guid.NewGuid();

        var payrollAccountId = Guid.NewGuid();
        var softwareAccountId = Guid.NewGuid();
        var clearingAccountId = Guid.NewGuid();
        var otherPayrollAccountId = Guid.NewGuid();
        var otherClearingAccountId = Guid.NewGuid();
        var disabledPayrollAccountId = Guid.NewGuid();
        var disabledSoftwareAccountId = Guid.NewGuid();
        var disabledClearingAccountId = Guid.NewGuid();
        var operationsCostCenterId = Guid.NewGuid();
        var salesCostCenterId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();
        var juneFiscalPeriodId = Guid.NewGuid();
        var otherFiscalPeriodId = Guid.NewGuid();
        var otherJuneFiscalPeriodId = Guid.NewGuid();
        var disabledFiscalPeriodId = Guid.NewGuid();
        var outsiderUserId = Guid.NewGuid();
        var outsiderSubject = $"variance-outsider-{Guid.NewGuid():N}";
        var outsiderEmail = $"{outsiderSubject}@example.com";
        const string outsiderDisplayName = "Variance Outsider";

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(ownerUserId, email, displayName, "dev-header", subject),
                new User(outsiderUserId, outsiderEmail, outsiderDisplayName, "dev-header", outsiderSubject));

            var company = new Company(companyId, "Variance Company");
            company.UpdateBrandingAndSettings(
                null,
                new CompanySettings
                {
                    FeatureFlags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["cost_centers"] = true
                    }
                });
            company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, April2026, April2026);

            var otherCompany = new Company(otherCompanyId, "Other Variance Company");
            otherCompany.UpdateBrandingAndSettings(
                null,
                new CompanySettings
                {
                    FeatureFlags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["cost_centers"] = true
                    }
                });
            otherCompany.SetFinanceSeedStatus(FinanceSeedingState.Seeded, April2026, April2026);

            var disabledCostCenterCompany = new Company(disabledCostCenterCompanyId, "Variance Company Without Cost Centers");
            disabledCostCenterCompany.UpdateBrandingAndSettings(
                null,
                new CompanySettings
                {
                    FeatureFlags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["cost_centers"] = false
                    }
                });
            disabledCostCenterCompany.SetFinanceSeedStatus(FinanceSeedingState.Seeded, April2026, April2026);

            dbContext.Companies.AddRange(company, otherCompany, disabledCostCenterCompany);
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(
                    Guid.NewGuid(),
                    companyId,
                    ownerUserId,
                    CompanyMembershipRole.Owner,
                    CompanyMembershipStatus.Active),
                new CompanyMembership(
                    Guid.NewGuid(),
                    disabledCostCenterCompanyId,
                    ownerUserId,
                    CompanyMembershipRole.Owner,
                    CompanyMembershipStatus.Active));

            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                otherCompanyId,
                outsiderUserId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));

            dbContext.FinanceAccounts.AddRange(
                new FinanceAccount(
                    payrollAccountId,
                    companyId,
                    "5000",
                    "Payroll",
                    "expense",
                    "USD",
                    0m,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new FinanceAccount(
                    softwareAccountId,
                    companyId,
                    "5100",
                    "Software",
                    "expense",
                    "USD",
                    0m,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new FinanceAccount(
                    clearingAccountId,
                    companyId,
                    "2100",
                    "Accrued Expenses",
                    "liability",
                    "USD",
                    0m,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new FinanceAccount(
                    otherPayrollAccountId,
                    otherCompanyId,
                    "5000",
                    "Payroll",
                    "expense",
                    "USD",
                    0m,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new FinanceAccount(
                    otherClearingAccountId,
                    otherCompanyId,
                    "2100",
                    "Accrued Expenses",
                    "liability",
                    "USD",
                    0m,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new FinanceAccount(
                    disabledPayrollAccountId,
                    disabledCostCenterCompanyId,
                    "5000",
                    "Payroll",
                    "expense",
                    "USD",
                    0m,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new FinanceAccount(
                    disabledSoftwareAccountId,
                    disabledCostCenterCompanyId,
                    "5100",
                    "Software",
                    "expense",
                    "USD",
                    0m,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                new FinanceAccount(
                    disabledClearingAccountId,
                    disabledCostCenterCompanyId,
                    "2100",
                    "Accrued Expenses",
                    "liability",
                    "USD",
                    0m,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

            dbContext.FiscalPeriods.AddRange(
                new FiscalPeriod(
                    fiscalPeriodId,
                    companyId,
                    "2026-04",
                    April2026,
                    April2026.AddMonths(1)),
                new FiscalPeriod(
                    juneFiscalPeriodId,
                    companyId,
                    "2026-06",
                    June2026,
                    June2026.AddMonths(1)),
                new FiscalPeriod(
                    otherFiscalPeriodId,
                    otherCompanyId,
                    "2026-04",
                    April2026,
                    April2026.AddMonths(1)),
                new FiscalPeriod(
                    otherJuneFiscalPeriodId,
                    otherCompanyId,
                    "2026-06",
                    June2026,
                    June2026.AddMonths(1)),
                new FiscalPeriod(
                    disabledFiscalPeriodId,
                    disabledCostCenterCompanyId,
                    "2026-04",
                    April2026,
                    April2026.AddMonths(1)));

            dbContext.Budgets.AddRange(
                new Budget(Guid.NewGuid(), companyId, payrollAccountId, April2026, "baseline", 1000m, "USD", operationsCostCenterId),
                new Budget(Guid.NewGuid(), companyId, payrollAccountId, April2026, "baseline", 1000m, "USD", salesCostCenterId),
                new Budget(Guid.NewGuid(), companyId, softwareAccountId, April2026, "baseline", 250m, "USD"),
                new Budget(Guid.NewGuid(), companyId, payrollAccountId, June2026, "baseline", 1000m, "USD", operationsCostCenterId),
                new Budget(Guid.NewGuid(), companyId, softwareAccountId, June2026, "baseline", 200m, "USD"),
                new Budget(Guid.NewGuid(), otherCompanyId, otherPayrollAccountId, April2026, "baseline", 700m, "USD", operationsCostCenterId),
                new Budget(Guid.NewGuid(), otherCompanyId, otherPayrollAccountId, June2026, "baseline", 900m, "USD", operationsCostCenterId));

            dbContext.Budgets.AddRange(
                new Budget(Guid.NewGuid(), disabledCostCenterCompanyId, disabledPayrollAccountId, April2026, "baseline", 450m, "USD", operationsCostCenterId),
                new Budget(Guid.NewGuid(), disabledCostCenterCompanyId, disabledPayrollAccountId, April2026, "baseline", 650m, "USD", salesCostCenterId),
                new Budget(Guid.NewGuid(), disabledCostCenterCompanyId, disabledSoftwareAccountId, April2026, "baseline", 250m, "USD"));

            dbContext.Forecasts.AddRange(
                new Forecast(Guid.NewGuid(), companyId, payrollAccountId, April2026, "baseline", 750m, "USD", operationsCostCenterId),
                new Forecast(Guid.NewGuid(), companyId, payrollAccountId, April2026, "baseline", 1000m, "USD", salesCostCenterId),
                new Forecast(Guid.NewGuid(), companyId, softwareAccountId, April2026, "baseline", 400m, "USD"),
                new Forecast(Guid.NewGuid(), otherCompanyId, otherPayrollAccountId, April2026, "baseline", 700m, "USD", operationsCostCenterId));

            AddPostedActualEntry(
                dbContext,
                companyId,
                fiscalPeriodId,
                "LE-APR-001",
                new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
                payrollAccountId,
                clearingAccountId,
                900m,
                operationsCostCenterId);
            AddPostedActualEntry(
                dbContext,
                companyId,
                fiscalPeriodId,
                "LE-APR-002",
                new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc),
                payrollAccountId,
                clearingAccountId,
                1100m,
                salesCostCenterId);
            AddPostedActualEntry(
                dbContext,
                companyId,
                fiscalPeriodId,
                "LE-APR-003",
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                softwareAccountId,
                clearingAccountId,
                300m,
                null);

            AddPostedActualEntry(
                dbContext,
                companyId,
                juneFiscalPeriodId,
                "LE-JUN-001",
                new DateTime(2026, 6, 7, 0, 0, 0, DateTimeKind.Utc),
                payrollAccountId,
                clearingAccountId,
                1200m,
                operationsCostCenterId);
            AddPostedActualEntry(
                dbContext,
                companyId,
                juneFiscalPeriodId,
                "LE-JUN-002",
                new DateTime(2026, 6, 18, 0, 0, 0, DateTimeKind.Utc),
                softwareAccountId,
                clearingAccountId,
                150m,
                null);

            AddPostedActualEntry(
                dbContext,
                otherCompanyId,
                otherFiscalPeriodId,
                "LE-APR-900",
                new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
                otherPayrollAccountId,
                otherClearingAccountId,
                700m,
                operationsCostCenterId);

            AddPostedActualEntry(
                dbContext,
                otherCompanyId,
                otherJuneFiscalPeriodId,
                "LE-JUN-900",
                new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
                otherPayrollAccountId,
                otherClearingAccountId,
                900m,
                operationsCostCenterId);

            AddPostedActualEntry(
                dbContext,
                disabledCostCenterCompanyId,
                disabledFiscalPeriodId,
                "LE-APR-101",
                new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc),
                disabledPayrollAccountId,
                disabledClearingAccountId,
                400m,
                operationsCostCenterId);
            AddPostedActualEntry(
                dbContext,
                disabledCostCenterCompanyId,
                disabledFiscalPeriodId,
                "LE-APR-102",
                new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc),
                disabledPayrollAccountId,
                disabledClearingAccountId,
                600m,
                salesCostCenterId);
            AddPostedActualEntry(
                dbContext,
                disabledCostCenterCompanyId,
                disabledFiscalPeriodId,
                "LE-APR-103",
                new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc),
                disabledSoftwareAccountId,
                disabledClearingAccountId,
                200m,
                null);

            return Task.CompletedTask;
        });

        return new VarianceSeed(
            companyId,
            subject,
            email,
            displayName,
            disabledCostCenterCompanyId,
            operationsCostCenterId,
            salesCostCenterId,
            outsiderSubject,
            outsiderEmail,
            outsiderDisplayName);
    }

    private static void AddPostedActualEntry(
        Infrastructure.Persistence.VirtualCompanyDbContext dbContext,
        Guid companyId,
        Guid fiscalPeriodId,
        string entryNumber,
        DateTime postedAtUtc,
        Guid expenseAccountId,
        Guid offsetAccountId,
        decimal amount,
        Guid? costCenterId)
    {
        var entryId = Guid.NewGuid();
        dbContext.LedgerEntries.Add(new LedgerEntry(
            entryId,
            companyId,
            fiscalPeriodId,
            entryNumber,
            postedAtUtc,
            LedgerEntryStatuses.Posted,
            $"Actual entry {entryNumber}",
            postedAtUtc: postedAtUtc));

        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(
                Guid.NewGuid(),
                companyId,
                entryId,
                expenseAccountId,
                amount,
                0m,
                "USD",
                costCenterId,
                $"Expense line {entryNumber}",
                postedAtUtc),
            new LedgerEntryLine(
                Guid.NewGuid(),
                companyId,
                entryId,
                offsetAccountId,
                0m,
                amount,
                "USD",
                null,
                $"Offset line {entryNumber}",
                postedAtUtc));
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record VarianceSeed(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName,
        Guid DisabledCostCenterCompanyId,
        Guid OperationsCostCenterId,
        Guid SalesCostCenterId,
        string OutsiderSubject,
        string OutsiderEmail,
        string OutsiderDisplayName);

    private sealed class FinanceVarianceResponse
    {
        public Guid CompanyId { get; set; }
        public string ComparisonType { get; set; } = string.Empty;
        public DateTime PeriodStartUtc { get; set; }
        public DateTime PeriodEndUtc { get; set; }
        public string? Version { get; set; }
        public bool IncludesCostCenters { get; set; }
        public List<FinanceVarianceRowResponse> Rows { get; set; } = [];
    }

    private sealed class FinanceVarianceRowResponse
    {
        public DateTime PeriodStartUtc { get; set; }
        public Guid FinanceAccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string CategoryKey { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public Guid? CostCenterId { get; set; }
        public string? CostCenterCode { get; set; }
        public string? CostCenterName { get; set; }
        public decimal ActualAmount { get; set; }
        public decimal ComparisonAmount { get; set; }
        public decimal VarianceAmount { get; set; }
        public decimal? VariancePercentage { get; set; }
        public string Currency { get; set; } = string.Empty;
    }
}