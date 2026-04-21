using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePeriodReportingIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinancePeriodReportingIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Profit_and_loss_reports_include_only_posted_entries_inside_requested_fiscal_period()
    {
        var seed = await SeedOpenPeriodScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var januaryResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/reports/profit-loss?fiscalPeriodId={seed.JanuaryPeriodId:D}");
        Assert.Equal(HttpStatusCode.OK, januaryResponse.StatusCode);
        var januaryProfitAndLoss = await januaryResponse.Content.ReadFromJsonAsync<ProfitAndLossReportResponse>();

        Assert.NotNull(januaryProfitAndLoss);
        Assert.Equal(seed.CompanyId, januaryProfitAndLoss!.CompanyId);
        Assert.Equal(seed.JanuaryPeriodId, januaryProfitAndLoss.FiscalPeriodId);
        Assert.False(januaryProfitAndLoss.UsedSnapshot);
        Assert.Equal(1400m, januaryProfitAndLoss.TotalRevenue);
        Assert.Equal(300m, januaryProfitAndLoss.TotalExpenses);
        Assert.Equal(1100m, januaryProfitAndLoss.NetIncome);

        var januaryRevenueLine = Assert.Single(januaryProfitAndLoss.RevenueLines);
        Assert.Equal("4000", januaryRevenueLine.AccountCode);
        Assert.Equal("Sales Revenue", januaryRevenueLine.AccountName);
        Assert.Equal(1400m, januaryRevenueLine.Amount);

        var januaryExpenseLine = Assert.Single(januaryProfitAndLoss.ExpenseLines);
        Assert.Equal("6100", januaryExpenseLine.AccountCode);
        Assert.Equal("Operating Expense", januaryExpenseLine.AccountName);
        Assert.Equal(300m, januaryExpenseLine.Amount);

        var februaryResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/reports/profit-loss?fiscalPeriodId={seed.FebruaryPeriodId:D}");
        Assert.Equal(HttpStatusCode.OK, februaryResponse.StatusCode);
        var februaryProfitAndLoss = await februaryResponse.Content.ReadFromJsonAsync<ProfitAndLossReportResponse>();

        Assert.NotNull(februaryProfitAndLoss);
        Assert.Equal(seed.FebruaryPeriodId, februaryProfitAndLoss!.FiscalPeriodId);
        Assert.Equal(1427m, februaryProfitAndLoss.TotalRevenue);
        Assert.Equal(0m, februaryProfitAndLoss.TotalExpenses);
        Assert.Equal(1427m, februaryProfitAndLoss.NetIncome);
        Assert.Equal(1427m, Assert.Single(februaryProfitAndLoss.RevenueLines).Amount);
    }

    [Fact]
    public async Task Balance_sheet_reports_use_posted_as_of_values_and_exclude_unposted_and_future_entries()
    {
        var seed = await SeedOpenPeriodScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/reports/balance-sheet?fiscalPeriodId={seed.JanuaryPeriodId:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var balanceSheet = await response.Content.ReadFromJsonAsync<BalanceSheetReportResponse>();

        Assert.NotNull(balanceSheet);
        Assert.Equal(seed.CompanyId, balanceSheet!.CompanyId);
        Assert.Equal(seed.JanuaryPeriodId, balanceSheet.FiscalPeriodId);
        Assert.False(balanceSheet.UsedSnapshot);
        Assert.Equal(1550m, balanceSheet.TotalAssets);
        Assert.Equal(0m, balanceSheet.TotalLiabilities);
        Assert.Equal(1550m, balanceSheet.TotalEquity);
        Assert.True(balanceSheet.IsBalanced);
        Assert.Equal(balanceSheet.TotalAssets, balanceSheet.TotalLiabilities + balanceSheet.TotalEquity);

        var assetLine = Assert.Single(balanceSheet.AssetLines);
        Assert.Equal("1000", assetLine.AccountCode);
        Assert.Equal("Operating Cash", assetLine.AccountName);
        Assert.Equal(1550m, assetLine.Amount);

        var currentEarningsLine = balanceSheet.EquityLines.Single(x => x.AccountCode == "current_earnings");
        Assert.Equal(1550m, currentEarningsLine.Amount);
        Assert.Empty(balanceSheet.LiabilityLines);
    }

    [Fact]
    public async Task Report_endpoints_return_not_found_when_fiscal_period_is_out_of_scope()
    {
        var seed = await SeedOpenPeriodScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var missingFiscalPeriodId = Guid.NewGuid();

        var profitAndLossResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/reports/profit-loss?fiscalPeriodId={missingFiscalPeriodId:D}");
        Assert.Equal(HttpStatusCode.NotFound, profitAndLossResponse.StatusCode);

        var balanceSheetResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/reports/balance-sheet?fiscalPeriodId={missingFiscalPeriodId:D}");
        Assert.Equal(HttpStatusCode.NotFound, balanceSheetResponse.StatusCode);
    }

    [Fact]
    public async Task Report_endpoints_are_scoped_to_company_membership_context()
    {
        var authorizedSeed = await SeedOpenPeriodScenarioAsync();
        var outOfScopeSeed = await SeedOpenPeriodScenarioAsync();
        using var client = CreateAuthenticatedClient(authorizedSeed.Subject, authorizedSeed.Email, authorizedSeed.DisplayName);

        var profitAndLossResponse = await client.GetAsync($"/internal/companies/{outOfScopeSeed.CompanyId:D}/finance/reports/profit-loss?fiscalPeriodId={outOfScopeSeed.JanuaryPeriodId:D}");
        Assert.Equal(HttpStatusCode.Forbidden, profitAndLossResponse.StatusCode);

        var balanceSheetResponse = await client.GetAsync($"/internal/companies/{outOfScopeSeed.CompanyId:D}/finance/reports/balance-sheet?fiscalPeriodId={outOfScopeSeed.JanuaryPeriodId:D}");
        Assert.Equal(HttpStatusCode.Forbidden, balanceSheetResponse.StatusCode);
    }

    [Fact]
    public async Task Closed_period_reports_use_trial_balance_snapshots_for_stable_values()
    {
        var seed = await SeedClosedPeriodScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var profitAndLossResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/reports/profit-loss?fiscalPeriodId={seed.JanuaryPeriodId:D}");
        Assert.Equal(HttpStatusCode.OK, profitAndLossResponse.StatusCode);
        var profitAndLoss = await profitAndLossResponse.Content.ReadFromJsonAsync<ProfitAndLossReportResponse>();

        Assert.NotNull(profitAndLoss);
        Assert.True(profitAndLoss!.IsClosed);
        Assert.True(profitAndLoss.UsedSnapshot);
        Assert.Equal(1000m, profitAndLoss.TotalRevenue);
        Assert.Equal(400m, profitAndLoss.TotalExpenses);
        Assert.Equal(600m, profitAndLoss.NetIncome);

        var revenueLine = Assert.Single(profitAndLoss.RevenueLines);
        Assert.Equal("4000", revenueLine.AccountCode);
        Assert.Equal(1000m, revenueLine.Amount);

        var expenseLine = Assert.Single(profitAndLoss.ExpenseLines);
        Assert.Equal("6100", expenseLine.AccountCode);
        Assert.Equal(400m, expenseLine.Amount);

        var balanceSheetResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/reports/balance-sheet?fiscalPeriodId={seed.JanuaryPeriodId:D}");
        Assert.Equal(HttpStatusCode.OK, balanceSheetResponse.StatusCode);
        var balanceSheet = await balanceSheetResponse.Content.ReadFromJsonAsync<BalanceSheetReportResponse>();

        Assert.NotNull(balanceSheet);
        Assert.True(balanceSheet!.IsClosed);
        Assert.True(balanceSheet.UsedSnapshot);
        Assert.Equal(600m, balanceSheet.TotalAssets);
        Assert.Equal(0m, balanceSheet.TotalLiabilities);
        Assert.Equal(600m, balanceSheet.TotalEquity);
        Assert.True(balanceSheet.IsBalanced);
        Assert.Equal(balanceSheet.TotalAssets, balanceSheet.TotalLiabilities + balanceSheet.TotalEquity);

        var assetLine = Assert.Single(balanceSheet.AssetLines);
        Assert.Equal("1000", assetLine.AccountCode);
        Assert.Equal(600m, assetLine.Amount);

        var currentEarningsLine = balanceSheet.EquityLines.Single(x => x.AccountCode == "current_earnings");
        Assert.Equal(600m, currentEarningsLine.Amount);
        Assert.Empty(balanceSheet.LiabilityLines);
    }

    [Fact]
    public async Task Seeded_balance_sheet_scenarios_balance_assets_liabilities_and_equity()
    {
        var seed = await SeedBalancedScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/reports/balance-sheet?fiscalPeriodId={seed.JanuaryPeriodId:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var balanceSheet = await response.Content.ReadFromJsonAsync<BalanceSheetReportResponse>();

        Assert.NotNull(balanceSheet);
        Assert.True(balanceSheet!.IsBalanced);
        Assert.Equal(1500m, balanceSheet.TotalAssets);
        Assert.Equal(500m, balanceSheet.TotalLiabilities);
        Assert.Equal(1000m, balanceSheet.TotalEquity);
        Assert.Equal(balanceSheet.TotalAssets, balanceSheet.TotalLiabilities + balanceSheet.TotalEquity);

        var assetLine = Assert.Single(balanceSheet.AssetLines);
        Assert.Equal("1000", assetLine.AccountCode);
        Assert.Equal(1500m, assetLine.Amount);

        var liabilityLine = Assert.Single(balanceSheet.LiabilityLines);
        Assert.Equal("2000", liabilityLine.AccountCode);
        Assert.Equal(500m, liabilityLine.Amount);

        Assert.Contains(balanceSheet.EquityLines, x => x.AccountCode == "3000" && x.Amount == 100m);
        Assert.Contains(balanceSheet.EquityLines, x => x.AccountCode == "current_earnings" && x.Amount == 900m);
    }

    private async Task<ReportingSeed> SeedOpenPeriodScenarioAsync()
    {
        var seed = CreateSeedContext("Open Period Reporting Company");
        await _factory.SeedAsync(dbContext =>
        {
            var context = SeedBaseCompanyData(dbContext, seed);
            context.DbContext = dbContext;
            context.CompanyId = seed.CompanyId;
            context.DecemberPeriodId = seed.DecemberPeriodId;
            context.JanuaryPeriodId = seed.JanuaryPeriodId;
            context.FebruaryPeriodId = seed.FebruaryPeriodId;
            SeedOpenPeriodLedger(context);
            return Task.CompletedTask;
        });

        return seed;
    }

    private async Task<ReportingSeed> SeedClosedPeriodScenarioAsync()
    {
        var seed = CreateSeedContext("Closed Period Reporting Company");
        await _factory.SeedAsync(dbContext =>
        {
            var context = SeedBaseCompanyData(dbContext, seed, closeJanuary: true);
            context.DbContext = dbContext;
            context.CompanyId = seed.CompanyId;
            context.DecemberPeriodId = seed.DecemberPeriodId;
            context.JanuaryPeriodId = seed.JanuaryPeriodId;
            context.FebruaryPeriodId = seed.FebruaryPeriodId;
            SeedClosedPeriodLedger(context);
            return Task.CompletedTask;
        });

        return seed;
    }

    private async Task<ReportingSeed> SeedBalancedScenarioAsync()
    {
        var seed = CreateSeedContext("Balanced Reporting Company");
        await _factory.SeedAsync(dbContext =>
        {
            var context = SeedBaseCompanyData(dbContext, seed);
            context.DbContext = dbContext;
            context.CompanyId = seed.CompanyId;
            context.DecemberPeriodId = seed.DecemberPeriodId;
            context.JanuaryPeriodId = seed.JanuaryPeriodId;
            context.FebruaryPeriodId = seed.FebruaryPeriodId;

            var payableEntry = new LedgerEntry(
                Guid.NewGuid(),
                seed.CompanyId,
                seed.JanuaryPeriodId,
                "JE-0004",
                new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc),
                LedgerEntryStatuses.Posted,
                "Record payable-backed asset");
            dbContext.LedgerEntries.Add(payableEntry);
            dbContext.LedgerEntryLines.AddRange(
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, payableEntry.Id, context.CashAccountId, 500m, 0m, "USD", "Inventory-like asset"),
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, payableEntry.Id, context.PayablesAccountId, 0m, 500m, "USD", "Trade payable"));

            var equityEntry = new LedgerEntry(
                Guid.NewGuid(),
                seed.CompanyId,
                seed.JanuaryPeriodId,
                "JE-0005",
                new DateTime(2026, 1, 26, 0, 0, 0, DateTimeKind.Utc),
                LedgerEntryStatuses.Posted,
                "Owner capital");
            dbContext.LedgerEntries.Add(equityEntry);
            dbContext.LedgerEntryLines.AddRange(
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, equityEntry.Id, context.CashAccountId, 100m, 0m, "USD", "Cash contribution"),
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, equityEntry.Id, context.EquityAccountId, 0m, 100m, "USD", "Owner equity"));

            var saleEntry = new LedgerEntry(
                Guid.NewGuid(),
                seed.CompanyId,
                seed.JanuaryPeriodId,
                "JE-0006",
                new DateTime(2026, 1, 27, 0, 0, 0, DateTimeKind.Utc),
                LedgerEntryStatuses.Posted,
                "January sale");
            dbContext.LedgerEntries.Add(saleEntry);
            dbContext.LedgerEntryLines.AddRange(
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, saleEntry.Id, context.CashAccountId, 1200m, 0m, "USD", "Cash sale"),
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, saleEntry.Id, context.RevenueAccountId, 0m, 1200m, "USD", "Revenue"));

            var expenseEntry = new LedgerEntry(
                Guid.NewGuid(),
                seed.CompanyId,
                seed.JanuaryPeriodId,
                "JE-0007",
                new DateTime(2026, 1, 28, 0, 0, 0, DateTimeKind.Utc),
                LedgerEntryStatuses.Posted,
                "January expense");
            dbContext.LedgerEntries.Add(expenseEntry);
            dbContext.LedgerEntryLines.AddRange(
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, expenseEntry.Id, context.ExpenseAccountId, 300m, 0m, "USD", "Expense"),
                new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, expenseEntry.Id, context.CashAccountId, 0m, 300m, "USD", "Cash payment"));

            return Task.CompletedTask;
        });

        return seed;
    }

    private static ReportingSeed CreateSeedContext(string companyName)
    {
        var companyId = Guid.NewGuid();
        var subject = $"finance-reporting-{Guid.NewGuid():N}";
        return new ReportingSeed(
            companyId,
            Guid.NewGuid(),
            subject,
            $"{subject}@example.com",
            "Finance Reporting Reader",
            companyName,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
    }

    private static SeededAccounts SeedBaseCompanyData(
        VirtualCompanyDbContext dbContext,
        ReportingSeed seed,
        bool closeJanuary = false)
    {
        dbContext.Users.Add(new User(seed.UserId, seed.Email, seed.DisplayName, "dev-header", seed.Subject));
        dbContext.Companies.Add(new Company(seed.CompanyId, seed.CompanyName));
        dbContext.CompanyMemberships.Add(new CompanyMembership(
            Guid.NewGuid(),
            seed.CompanyId,
            seed.UserId,
            CompanyMembershipRole.Owner,
            CompanyMembershipStatus.Active));

        var customerId = Guid.NewGuid();
        var vendorId = Guid.NewGuid();
        dbContext.FinanceCounterparties.AddRange(
            new FinanceCounterparty(customerId, seed.CompanyId, "Customer", "customer", "customer@example.com"),
            new FinanceCounterparty(vendorId, seed.CompanyId, "Vendor", "vendor", "vendor@example.com"));

        var cashAccountId = Guid.NewGuid();
        var payablesAccountId = Guid.NewGuid();
        var equityAccountId = Guid.NewGuid();
        var revenueAccountId = Guid.NewGuid();
        var expenseAccountId = Guid.NewGuid();

        dbContext.FinanceAccounts.AddRange(
            new FinanceAccount(cashAccountId, seed.CompanyId, "1000", "Operating Cash", "asset", "USD", 0m, new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(payablesAccountId, seed.CompanyId, "2000", "Accounts Payable", "liability", "USD", 0m, new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(equityAccountId, seed.CompanyId, "3000", "Owner Equity", "equity", "USD", 0m, new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(revenueAccountId, seed.CompanyId, "4000", "Sales Revenue", "revenue", "USD", 0m, new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(expenseAccountId, seed.CompanyId, "6100", "Operating Expense", "expense", "USD", 0m, new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)));

        dbContext.FinancialStatementMappings.AddRange(
            new FinancialStatementMapping(Guid.NewGuid(), seed.CompanyId, cashAccountId, FinancialStatementType.BalanceSheet, FinancialStatementReportSection.BalanceSheetAssets, FinancialStatementLineClassification.CurrentAsset),
            new FinancialStatementMapping(Guid.NewGuid(), seed.CompanyId, payablesAccountId, FinancialStatementType.BalanceSheet, FinancialStatementReportSection.BalanceSheetLiabilities, FinancialStatementLineClassification.CurrentLiability),
            new FinancialStatementMapping(Guid.NewGuid(), seed.CompanyId, equityAccountId, FinancialStatementType.BalanceSheet, FinancialStatementReportSection.BalanceSheetEquity, FinancialStatementLineClassification.Equity),
            new FinancialStatementMapping(Guid.NewGuid(), seed.CompanyId, revenueAccountId, FinancialStatementType.ProfitAndLoss, FinancialStatementReportSection.ProfitAndLossRevenue, FinancialStatementLineClassification.Revenue),
            new FinancialStatementMapping(Guid.NewGuid(), seed.CompanyId, expenseAccountId, FinancialStatementType.ProfitAndLoss, FinancialStatementReportSection.ProfitAndLossOperatingExpenses, FinancialStatementLineClassification.OperatingExpense));

        dbContext.FinanceInvoices.Add(new FinanceInvoice(
            Guid.NewGuid(),
            seed.CompanyId,
            customerId,
            "INV-202601-001",
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            1m,
            "USD",
            "open"));
        dbContext.FinanceBills.Add(new FinanceBill(
            Guid.NewGuid(),
            seed.CompanyId,
            vendorId,
            "BILL-202601-001",
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 2, 0, 0, 0, DateTimeKind.Utc),
            1m,
            "USD",
            "open"));
        dbContext.FinanceTransactions.Add(new FinanceTransaction(
            Guid.NewGuid(),
            seed.CompanyId,
            cashAccountId,
            customerId,
            null,
            null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "bootstrap",
            0m,
            "USD",
            "Seed completeness transaction",
            $"BOOT-{seed.CompanyId:N}"));
        dbContext.FinanceBalances.Add(new FinanceBalance(
            Guid.NewGuid(),
            seed.CompanyId,
            cashAccountId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            0m,
            "USD"));
        dbContext.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(
            Guid.NewGuid(),
            seed.CompanyId,
            "USD",
            1000m,
            1000m,
            true,
            -10000m,
            10000m,
            90,
            30));

        dbContext.FiscalPeriods.AddRange(
            new FiscalPeriod(
                seed.DecemberPeriodId,
                seed.CompanyId,
                "FY2025-12",
                new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FiscalPeriod(
                seed.JanuaryPeriodId,
                seed.CompanyId,
                "FY2026-01",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                closeJanuary,
                closeJanuary ? new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) : null),
            new FiscalPeriod(
                seed.FebruaryPeriodId,
                seed.CompanyId,
                "FY2026-02",
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));

        return new SeededAccounts(cashAccountId, payablesAccountId, equityAccountId, revenueAccountId, expenseAccountId);
    }

    private static void SeedOpenPeriodLedger(SeededAccounts context)
    {
        var dbContext = context.DbContext;
        var companyId = context.CompanyId;
        var decemberPeriodId = context.DecemberPeriodId;
        var januaryPeriodId = context.JanuaryPeriodId;
        var februaryPeriodId = context.FebruaryPeriodId;

        // Boundary and adjacent-period entries prove the API filters by entry date.
        var priorPeriodRevenue = new LedgerEntry(Guid.NewGuid(), companyId, decemberPeriodId, "JE-0000", new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc), LedgerEntryStatuses.Posted, "December revenue");
        dbContext.LedgerEntries.Add(priorPeriodRevenue);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), companyId, priorPeriodRevenue.Id, context.CashAccountId, 450m, 0m, "USD", "Prior-period cash receipt"),
            new LedgerEntryLine(Guid.NewGuid(), companyId, priorPeriodRevenue.Id, context.RevenueAccountId, 0m, 450m, "USD", "Prior-period revenue"));

        var periodStartRevenue = new LedgerEntry(Guid.NewGuid(), companyId, januaryPeriodId, "JE-0001", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), LedgerEntryStatuses.Posted, "January opening-day revenue");
        dbContext.LedgerEntries.Add(periodStartRevenue);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), companyId, periodStartRevenue.Id, context.CashAccountId, 200m, 0m, "USD", "Boundary cash receipt"),
            new LedgerEntryLine(Guid.NewGuid(), companyId, periodStartRevenue.Id, context.RevenueAccountId, 0m, 200m, "USD", "Boundary revenue"));

        var postedRevenue = new LedgerEntry(Guid.NewGuid(), companyId, januaryPeriodId, "JE-0001", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), LedgerEntryStatuses.Posted, "Posted January revenue");
        dbContext.LedgerEntries.Add(postedRevenue);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), companyId, postedRevenue.Id, context.CashAccountId, 1200m, 0m, "USD", "Cash receipt"),
            new LedgerEntryLine(Guid.NewGuid(), companyId, postedRevenue.Id, context.RevenueAccountId, 0m, 1200m, "USD", "Revenue"));

        var postedExpense = new LedgerEntry(Guid.NewGuid(), companyId, januaryPeriodId, "JE-0002", new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc), LedgerEntryStatuses.Posted, "Posted January expense");
        dbContext.LedgerEntries.Add(postedExpense);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), companyId, postedExpense.Id, context.ExpenseAccountId, 300m, 0m, "USD", "Expense"),
            new LedgerEntryLine(Guid.NewGuid(), companyId, postedExpense.Id, context.CashAccountId, 0m, 300m, "USD", "Cash disbursement"));

        var draftRevenue = new LedgerEntry(Guid.NewGuid(), companyId, januaryPeriodId, "JE-0003", new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc), LedgerEntryStatuses.Draft, "Draft revenue");
        dbContext.LedgerEntries.Add(draftRevenue);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), companyId, draftRevenue.Id, context.CashAccountId, 999m, 0m, "USD"),
            new LedgerEntryLine(Guid.NewGuid(), companyId, draftRevenue.Id, context.RevenueAccountId, 0m, 999m, "USD"));

        var periodBoundaryRevenue = new LedgerEntry(Guid.NewGuid(), companyId, februaryPeriodId, "JE-0004", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), LedgerEntryStatuses.Posted, "Revenue at next period boundary");
        dbContext.LedgerEntries.Add(periodBoundaryRevenue);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), companyId, periodBoundaryRevenue.Id, context.CashAccountId, 650m, 0m, "USD"),
            new LedgerEntryLine(Guid.NewGuid(), companyId, periodBoundaryRevenue.Id, context.RevenueAccountId, 0m, 650m, "USD"));

        var outOfPeriodRevenue = new LedgerEntry(Guid.NewGuid(), companyId, februaryPeriodId, "JE-0004", new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc), LedgerEntryStatuses.Posted, "February revenue");
        dbContext.LedgerEntries.Add(outOfPeriodRevenue);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), companyId, outOfPeriodRevenue.Id, context.CashAccountId, 777m, 0m, "USD"),
            new LedgerEntryLine(Guid.NewGuid(), companyId, outOfPeriodRevenue.Id, context.RevenueAccountId, 0m, 777m, "USD"));
    }

    private static void SeedClosedPeriodLedger(SeededAccounts context)
    {
        var dbContext = context.DbContext;
        var companyId = context.CompanyId;
        var januaryPeriodId = context.JanuaryPeriodId;

        var postedRevenue = new LedgerEntry(Guid.NewGuid(), companyId, januaryPeriodId, "JE-0101", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), LedgerEntryStatuses.Posted, "January revenue");
        dbContext.LedgerEntries.Add(postedRevenue);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), companyId, postedRevenue.Id, context.CashAccountId, 1000m, 0m, "USD"),
            new LedgerEntryLine(Guid.NewGuid(), companyId, postedRevenue.Id, context.RevenueAccountId, 0m, 1000m, "USD"));

        var postedExpense = new LedgerEntry(Guid.NewGuid(), companyId, januaryPeriodId, "JE-0102", new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), LedgerEntryStatuses.Posted, "January expense");
        dbContext.LedgerEntries.Add(postedExpense);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), companyId, postedExpense.Id, context.ExpenseAccountId, 400m, 0m, "USD"),
            new LedgerEntryLine(Guid.NewGuid(), companyId, postedExpense.Id, context.CashAccountId, 0m, 400m, "USD"));

        var afterCloseMutation = new LedgerEntry(Guid.NewGuid(), companyId, januaryPeriodId, "JE-0103", new DateTime(2026, 1, 31, 12, 0, 0, DateTimeKind.Utc), LedgerEntryStatuses.Posted, "Late mutation that should be ignored by snapshots");
        dbContext.LedgerEntries.Add(afterCloseMutation);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), companyId, afterCloseMutation.Id, context.CashAccountId, 200m, 0m, "USD"),
            new LedgerEntryLine(Guid.NewGuid(), companyId, afterCloseMutation.Id, context.RevenueAccountId, 0m, 200m, "USD"));

        dbContext.TrialBalanceSnapshots.AddRange(
            new TrialBalanceSnapshot(Guid.NewGuid(), companyId, januaryPeriodId, context.CashAccountId, 600m, "USD"),
            new TrialBalanceSnapshot(Guid.NewGuid(), companyId, januaryPeriodId, context.RevenueAccountId, -1000m, "USD"),
            new TrialBalanceSnapshot(Guid.NewGuid(), companyId, januaryPeriodId, context.ExpenseAccountId, 400m, "USD"),
            new TrialBalanceSnapshot(Guid.NewGuid(), companyId, januaryPeriodId, context.PayablesAccountId, 0m, "USD"),
            new TrialBalanceSnapshot(Guid.NewGuid(), companyId, januaryPeriodId, context.EquityAccountId, 0m, "USD"));
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record ReportingSeed(
        Guid CompanyId,
        Guid UserId,
        string Subject,
        string Email,
        string DisplayName,
        string CompanyName,
        Guid DecemberPeriodId,
        Guid JanuaryPeriodId,
        Guid FebruaryPeriodId);

    private sealed class ProfitAndLossReportResponse
    {
        public Guid CompanyId { get; set; }
        public Guid FiscalPeriodId { get; set; }
        public string FiscalPeriodName { get; set; } = string.Empty;
        public bool IsClosed { get; set; }
        public bool UsedSnapshot { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal NetIncome { get; set; }
        public List<StatementLineResponse> RevenueLines { get; set; } = [];
        public List<StatementLineResponse> ExpenseLines { get; set; } = [];
    }

    private sealed class BalanceSheetReportResponse
    {
        public Guid CompanyId { get; set; }
        public Guid FiscalPeriodId { get; set; }
        public string FiscalPeriodName { get; set; } = string.Empty;
        public bool IsClosed { get; set; }
        public bool UsedSnapshot { get; set; }
        public decimal TotalAssets { get; set; }
        public decimal TotalLiabilities { get; set; }
        public decimal TotalEquity { get; set; }
        public bool IsBalanced { get; set; }
        public List<StatementLineResponse> AssetLines { get; set; } = [];
        public List<StatementLineResponse> LiabilityLines { get; set; } = [];
        public List<StatementLineResponse> EquityLines { get; set; } = [];
    }

    private sealed class StatementLineResponse
    {
        public Guid? AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string ReportSection { get; set; } = string.Empty;
        public string LineClassification { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
    }

    private sealed class SeededAccounts
    {
        public SeededAccounts(Guid cashAccountId, Guid payablesAccountId, Guid equityAccountId, Guid revenueAccountId, Guid expenseAccountId)
        {
            CashAccountId = cashAccountId;
            PayablesAccountId = payablesAccountId;
            EquityAccountId = equityAccountId;
            RevenueAccountId = revenueAccountId;
            ExpenseAccountId = expenseAccountId;
        }

        public VirtualCompanyDbContext DbContext { get; set; } = null!;
        public Guid CompanyId { get; set; }
        public Guid DecemberPeriodId { get; set; }
        public Guid JanuaryPeriodId { get; set; }
        public Guid FebruaryPeriodId { get; set; }
        public Guid CashAccountId { get; }
        public Guid PayablesAccountId { get; }
        public Guid EquityAccountId { get; }
        public Guid RevenueAccountId { get; }
        public Guid ExpenseAccountId { get; }
    }
}