using System.Globalization;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSummaryUiContractIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceSummaryUiContractIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Finance_summary_service_and_presenter_match_backend_values_for_the_active_tenant()
    {
        var seed = await SeedFinanceSummaryCompaniesAsync();
        using var client = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, seed.OwnerDisplayName);
        var financeClient = new FinanceApiClient(client);

        var cashPosition = await financeClient.GetCashPositionAsync(seed.CompanyAId, seed.CompanyAReferenceUtc);
        var cashViewModel = FinanceSummaryPresenter.ToCashPositionViewModel(cashPosition);
        Assert.NotNull(cashPosition);
        Assert.NotNull(cashViewModel);
        Assert.Equal(seed.CompanyAId, cashPosition!.CompanyId);
        Assert.Equal(seed.CompanyAId, cashViewModel!.CompanyId);
        Assert.Equal($"USD {cashPosition.AvailableBalance.ToString("N2", CultureInfo.InvariantCulture)}", cashViewModel.AvailableBalance);
        Assert.Equal(FormatCurrency(cashPosition.AverageMonthlyBurn, cashPosition.Currency), cashViewModel.AverageMonthlyBurn);
        Assert.Equal(FormatCurrency(cashPosition.Thresholds.WarningCashAmount!.Value, cashPosition.Thresholds.Currency), cashViewModel.WarningThreshold);
        Assert.Equal(cashPosition.EstimatedRunwayDays is int runwayDays ? $"{runwayDays:N0} days" : "n/a", cashViewModel.EstimatedRunway);

        var balances = await financeClient.GetBalancesAsync(seed.CompanyAId, seed.CompanyAReferenceUtc);
        var balancesViewModel = FinanceSummaryPresenter.ToBalancesViewModel(seed.CompanyAId, seed.CompanyAReferenceUtc, balances);
        Assert.NotNull(balancesViewModel);
        Assert.Equal($"USD {balances.Sum(balance => balance.Amount).ToString("N2", CultureInfo.InvariantCulture)}", balancesViewModel!.TotalBalance);
        Assert.All(balancesViewModel.Accounts, account => Assert.Contains(account.AccountId, seed.CompanyAAccountIds));
        var orderedBalances = balances.OrderBy(account => account.AccountCode, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var i = 0; i < orderedBalances.Length; i++)
        {
            Assert.Equal(orderedBalances[i].AccountCode, balancesViewModel.Accounts[i].AccountCode);
            Assert.Equal(orderedBalances[i].AccountName, balancesViewModel.Accounts[i].AccountName);
            Assert.Equal(FormatCurrency(orderedBalances[i].Amount, orderedBalances[i].Currency), balancesViewModel.Accounts[i].Amount);
            Assert.Equal(FormatDate(orderedBalances[i].AsOfUtc), balancesViewModel.Accounts[i].AsOf);
        }

        var monthlySummary = await financeClient.GetMonthlySummaryAsync(seed.CompanyAId, seed.CompanyAMonthReferenceUtc);
        var monthlyViewModel = FinanceSummaryPresenter.ToMonthlySummaryViewModel(monthlySummary);
        Assert.NotNull(monthlySummary);
        Assert.NotNull(monthlyViewModel);
        Assert.Equal(seed.CompanyAId, monthlyViewModel!.CompanyId);
        Assert.Equal($"USD {monthlySummary!.ProfitAndLoss.Revenue.ToString("N2", CultureInfo.InvariantCulture)}", monthlyViewModel.Revenue);
        Assert.NotEmpty(monthlyViewModel.ExpenseCategories);
        Assert.Equal(monthlySummary.ExpenseBreakdown!.Categories.Count, monthlyViewModel.ExpenseCategories.Count);
        var orderedCategories = monthlySummary.ExpenseBreakdown.Categories.OrderByDescending(category => category.Amount).ToArray();
        for (var i = 0; i < orderedCategories.Length; i++)
        {
            var backendCategory = orderedCategories[i];
            var viewCategory = monthlyViewModel.ExpenseCategories[i];
            Assert.Equal(backendCategory.Category, viewCategory.Category);
            Assert.Equal(FormatCurrency(backendCategory.Amount, backendCategory.Currency), viewCategory.Amount);
            Assert.Equal(FormatPercentage(monthlySummary.ExpenseBreakdown.TotalExpenses == 0m ? 0m : backendCategory.Amount / monthlySummary.ExpenseBreakdown.TotalExpenses), viewCategory.Share);
        }

        var anomalies = await financeClient.GetAnomaliesAsync(seed.CompanyAId);
        var anomaliesViewModel = FinanceSummaryPresenter.ToAnomaliesViewModel(seed.CompanyAId, anomalies);
        Assert.NotNull(anomaliesViewModel);
        Assert.Equal(seed.CompanyAId, anomaliesViewModel!.CompanyId);
        Assert.Contains(anomaliesViewModel.Items, item => item.Id == seed.CompanyAAnomalyId);
        Assert.DoesNotContain(anomaliesViewModel.Items, item => item.Id == seed.CompanyBAnomalyId);
        var anomaly = Assert.Single(anomalies, item => item.Id == seed.CompanyAAnomalyId);
        var anomalyViewModel = Assert.Single(anomaliesViewModel.Items, item => item.Id == seed.CompanyAAnomalyId);
        Assert.Equal(anomaly.AffectedRecordIds.Count == 1 ? "1 record" : $"{anomaly.AffectedRecordIds.Count} records", anomalyViewModel.AffectedRecords);
        Assert.Contains("expectedDetector", anomalyViewModel.DetectorSummary, StringComparison.OrdinalIgnoreCase);

        var companyBCashPosition = await financeClient.GetCashPositionAsync(seed.CompanyBId, seed.CompanyBReferenceUtc);
        var companyBCashViewModel = FinanceSummaryPresenter.ToCashPositionViewModel(companyBCashPosition);
        Assert.NotNull(companyBCashViewModel);
        Assert.Equal(seed.CompanyBId, companyBCashViewModel!.CompanyId);
        Assert.NotEqual(cashViewModel.AvailableBalance, companyBCashViewModel.AvailableBalance);
    }

    [Fact]
    public async Task Finance_summary_presenters_surface_empty_states_when_the_backend_returns_no_tenant_data()
    {
        var seed = await SeedFinanceSummaryCompaniesAsync();
        using var client = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, seed.OwnerDisplayName);
        var financeClient = new FinanceApiClient(client);

        var balances = await financeClient.GetBalancesAsync(seed.EmptyCompanyId, seed.EmptyCompanyReferenceUtc);
        var anomalies = await financeClient.GetAnomaliesAsync(seed.EmptyCompanyId);

        Assert.Empty(balances);
        Assert.Empty(anomalies);
        Assert.Null(FinanceSummaryPresenter.ToBalancesViewModel(seed.EmptyCompanyId, seed.EmptyCompanyReferenceUtc, balances));
        Assert.Null(FinanceSummaryPresenter.ToAnomaliesViewModel(seed.EmptyCompanyId, anomalies));
    }

    [Fact]
    public async Task Finance_summary_service_produces_error_state_inputs_when_finance_access_is_forbidden()
    {
        var seed = await SeedFinanceSummaryCompaniesAsync();
        using var client = CreateAuthenticatedClient(seed.EmployeeSubject, seed.EmployeeEmail, seed.EmployeeDisplayName);
        var financeClient = new FinanceApiClient(client);

        await Assert.ThrowsAsync<FinanceApiException>(() => financeClient.GetBalancesAsync(seed.CompanyAId, seed.CompanyAReferenceUtc));
    }

    private async Task<FinanceSummarySeed> SeedFinanceSummaryCompaniesAsync()
    {
        var ownerUserId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var emptyCompanyId = Guid.NewGuid();
        var ownerSubject = $"finance-ui-owner-{Guid.NewGuid():N}";
        var ownerEmail = $"{ownerSubject}@example.com";
        const string ownerDisplayName = "Finance UI Owner";
        var employeeSubject = $"finance-ui-employee-{Guid.NewGuid():N}";
        var employeeEmail = $"{employeeSubject}@example.com";
        const string employeeDisplayName = "Finance UI Employee";
        var companyAReferenceUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var companyBReferenceUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var emptyCompanyReferenceUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var companyAMonthReferenceUtc = new DateTime(2025, 12, 15, 0, 0, 0, DateTimeKind.Utc);
        FinanceSeedResult? companyASeed = null;
        FinanceSeedResult? companyBSeed = null;
        var companyAAnomalyId = Guid.Empty;
        var companyBAnomalyId = Guid.Empty;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(ownerUserId, ownerEmail, ownerDisplayName, "dev-header", ownerSubject));
            dbContext.Users.Add(new User(employeeUserId, employeeEmail, employeeDisplayName, "dev-header", employeeSubject));

            dbContext.Companies.AddRange(
                new Company(companyAId, "Finance UI Company A"),
                new Company(companyBId, "Finance UI Company B"),
                new Company(emptyCompanyId, "Finance UI Empty Company"));

            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyAId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyBId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), emptyCompanyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyAId, employeeUserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));

            companyASeed = FinanceSeedData.AddMockFinanceData(dbContext, companyAId, companyAReferenceUtc);
            companyBSeed = FinanceSeedData.AddMockFinanceData(dbContext, companyBId, companyBReferenceUtc);

            companyAAnomalyId = Guid.NewGuid();
            dbContext.FinanceSeedAnomalies.Add(new FinanceSeedAnomaly(
                companyAAnomalyId,
                companyAId,
                "missing_receipt",
                "ui_contract",
                [companyASeed.TransactionIds[0]],
                """{"expectedDetector":"receipt_completeness","expectedSignal":"missing_supporting_document"}"""));

            companyBAnomalyId = Guid.NewGuid();
            dbContext.FinanceSeedAnomalies.Add(new FinanceSeedAnomaly(
                companyBAnomalyId,
                companyBId,
                "duplicate_transaction",
                "ui_contract",
                [companyBSeed.TransactionIds[0]],
                """{"expectedDetector":"duplicate_payment","expectedSignal":"repeated_external_reference"}"""));

            return Task.CompletedTask;
        });

        return new FinanceSummarySeed(
            companyAId,
            companyBId,
            emptyCompanyId,
            ownerSubject,
            ownerEmail,
            ownerDisplayName,
            employeeSubject,
            employeeEmail,
            employeeDisplayName,
            companyAReferenceUtc,
            companyBReferenceUtc,
            emptyCompanyReferenceUtc,
            companyAMonthReferenceUtc,
            companyASeed!.AccountIds,
            companyAAnomalyId,
            companyBAnomalyId);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record FinanceSummarySeed(
        Guid CompanyAId,
        Guid CompanyBId,
        Guid EmptyCompanyId,
        string OwnerSubject,
        string OwnerEmail,
        string OwnerDisplayName,
        string EmployeeSubject,
        string EmployeeEmail,
        string EmployeeDisplayName,
        DateTime CompanyAReferenceUtc,
        DateTime CompanyBReferenceUtc,
        DateTime EmptyCompanyReferenceUtc,
        DateTime CompanyAMonthReferenceUtc,
        IReadOnlyList<Guid> CompanyAAccountIds,
        Guid CompanyAAnomalyId,
        Guid CompanyBAnomalyId);

    private static string FormatCurrency(decimal amount, string currency) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string FormatDate(DateTime utcDateTime) =>
        utcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string FormatPercentage(decimal percentage) =>
        percentage.ToString("P0", CultureInfo.InvariantCulture);
}
