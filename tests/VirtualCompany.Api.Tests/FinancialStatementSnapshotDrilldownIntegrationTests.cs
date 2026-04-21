using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancialStatementSnapshotDrilldownIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinancialStatementSnapshotDrilldownIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Regenerating_unlocked_period_creates_new_statement_snapshot_versions_without_overwriting_prior_versions()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var firstResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.JanuaryPeriodId:D}/reporting/stored-statements/regenerate",
            new { runInBackground = false });
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.JanuaryPeriodId:D}/reporting/stored-statements/regenerate",
            new { runInBackground = false });
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var snapshots = await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.FinancialStatementSnapshots.IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId && x.FiscalPeriodId == seed.JanuaryPeriodId)
                .OrderBy(x => x.StatementType)
                .ThenBy(x => x.VersionNumber)
                .Select(x => new
                {
                    x.StatementType,
                    x.VersionNumber,
                    x.BalancesChecksum,
                    x.GeneratedAtUtc,
                    x.SourcePeriodStartUtc,
                    x.SourcePeriodEndUtc,
                    LineCount = x.Lines.Count
                })
                .ToListAsync());

        Assert.Equal(4, snapshots.Count);
        Assert.Equal([1, 2], snapshots.Where(x => x.StatementType == FinancialStatementType.BalanceSheet).Select(x => x.VersionNumber).ToArray());
        Assert.Equal([1, 2], snapshots.Where(x => x.StatementType == FinancialStatementType.ProfitAndLoss).Select(x => x.VersionNumber).ToArray());
        Assert.Single(
            snapshots.Where(x => x.StatementType == FinancialStatementType.BalanceSheet).Select(x => x.BalancesChecksum).Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.Single(
            snapshots.Where(x => x.StatementType == FinancialStatementType.ProfitAndLoss).Select(x => x.BalancesChecksum).Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.All(snapshots, snapshot =>
        {
            Assert.False(string.IsNullOrWhiteSpace(snapshot.BalancesChecksum));
            Assert.Equal(64, snapshot.BalancesChecksum.Length);
            Assert.Matches("^[0-9a-f]+$", snapshot.BalancesChecksum);
            Assert.True(snapshot.GeneratedAtUtc > DateTime.UtcNow.AddMinutes(-5));
            Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), snapshot.SourcePeriodStartUtc);
            Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), snapshot.SourcePeriodEndUtc);
            Assert.True(snapshot.LineCount > 0);
        });
    }

    [Fact]
    public async Task Snapshot_history_endpoints_return_versioned_snapshot_headers_and_lines()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.JanuaryPeriodId:D}/reporting/stored-statements/regenerate",
            new { runInBackground = false });
        await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.JanuaryPeriodId:D}/reporting/stored-statements/regenerate",
            new { runInBackground = false });

        var listResponse = await client.GetAsync(
            $"/api/companies/{seed.CompanyId:D}/financial-statements/snapshots?fiscalPeriodId={seed.JanuaryPeriodId:D}&statementType=profit_and_loss");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var summaries = await listResponse.Content.ReadFromJsonAsync<List<FinancialStatementSnapshotSummaryResponse>>();

        Assert.NotNull(summaries);
        Assert.Equal([2, 1], summaries!.Select(x => x.VersionNumber).ToArray());
        Assert.All(summaries, summary =>
        {
            Assert.Equal(seed.CompanyId, summary.CompanyId);
            Assert.Equal(seed.JanuaryPeriodId, summary.FiscalPeriodId);
            Assert.Equal("profit_and_loss", summary.StatementType);
            Assert.True(summary.LineCount > 0);
        });

        var detailResponse = await client.GetAsync($"/api/companies/{seed.CompanyId:D}/financial-statements/snapshots/{summaries[0].SnapshotId:D}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<FinancialStatementSnapshotDetailResponse>();

        Assert.NotNull(detail);
        Assert.Equal(summaries[0].SnapshotId, detail!.SnapshotId);
        Assert.Contains(detail.Lines, line => line.AccountCode == "4000" && line.Amount == 900m);
    }

    [Fact]
    public async Task Snapshot_drilldown_by_snapshot_id_matches_live_drilldown_for_same_period_and_reconciles_exactly()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var regenerateResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.JanuaryPeriodId:D}/reporting/stored-statements/regenerate",
            new { runInBackground = false });
        Assert.Equal(HttpStatusCode.OK, regenerateResponse.StatusCode);

        var summaries = await client.GetFromJsonAsync<List<FinancialStatementSnapshotSummaryResponse>>(
            $"/api/companies/{seed.CompanyId:D}/financial-statements/snapshots?fiscalPeriodId={seed.JanuaryPeriodId:D}&statementType=profit_and_loss");
        var snapshotSummary = Assert.Single(summaries!, x => x.VersionNumber == 1);

        var snapshotResponse = await client.GetAsync(
            $"/api/companies/{seed.CompanyId:D}/financial-statements/snapshots/{snapshotSummary.SnapshotId:D}/lines/4000/drilldown");
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);
        var snapshotPayload = await snapshotResponse.Content.ReadFromJsonAsync<FinancialStatementDrilldownResponse>();

        Assert.NotNull(snapshotPayload);
        Assert.Equal("snapshot", snapshotPayload!.SourceMode);
        Assert.Equal(snapshotSummary.SnapshotId, snapshotPayload.Snapshot!.SnapshotId);
        Assert.Equal(900m, snapshotPayload.SelectedLine.Amount);
        Assert.Equal(900m, snapshotPayload.ReconciliationTotal);
        Assert.Equal(900m, snapshotPayload.JournalLineTotal);
        Assert.Equal(0m, snapshotPayload.OpeningBalanceAdjustment);
        Assert.Equal(0m, snapshotPayload.ReconciliationDelta);
        Assert.Equal(1, snapshotPayload.Snapshot!.VersionNumber);
        Assert.Equal(900m, snapshotPayload.JournalEntries.SelectMany(x => x.Lines).Sum(x => x.ContributionAmount));

        var liveResponse = await client.GetAsync(
            $"/api/companies/{seed.CompanyId:D}/financial-statements/drilldown?fiscalPeriodId={seed.JanuaryPeriodId:D}&statementType=profit_and_loss&lineCode=4000");
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        var livePayload = await liveResponse.Content.ReadFromJsonAsync<FinancialStatementDrilldownResponse>();

        Assert.NotNull(livePayload);
        Assert.Equal("live", livePayload!.SourceMode);
        Assert.Null(livePayload.Snapshot);
        Assert.Equal(900m, livePayload.SelectedLine.Amount);
        Assert.Equal(900m, livePayload.ReconciliationTotal);
        Assert.Equal(900m, livePayload.JournalLineTotal);
        Assert.Equal(0m, livePayload.OpeningBalanceAdjustment);
        Assert.Equal(0m, livePayload.ReconciliationDelta);
        Assert.Equal(900m, livePayload.JournalEntries.SelectMany(x => x.Lines).Sum(x => x.ContributionAmount));
        Assert.All(livePayload.JournalEntries, entry =>
            Assert.True(entry.EntryUtc >= new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) &&
                        entry.EntryUtc < new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(snapshotPayload.SelectedLine.Amount, livePayload.SelectedLine.Amount);
        Assert.Equal(snapshotPayload.JournalLineTotal, livePayload.JournalLineTotal);
        Assert.Equal(snapshotPayload.ReconciliationTotal, livePayload.ReconciliationTotal);
    }

    [Fact]
    public async Task Snapshot_drilldown_returns_not_found_for_unknown_snapshot_line()
    {
        var seed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/fiscal-periods/{seed.JanuaryPeriodId:D}/reporting/stored-statements/regenerate",
            new { runInBackground = false });
        var summaries = await client.GetFromJsonAsync<List<FinancialStatementSnapshotSummaryResponse>>(
            $"/api/companies/{seed.CompanyId:D}/financial-statements/snapshots?fiscalPeriodId={seed.JanuaryPeriodId:D}&statementType=profit_and_loss");
        var snapshotSummary = Assert.Single(summaries!, x => x.VersionNumber == 1);

        var response = await client.GetAsync(
            $"/api/companies/{seed.CompanyId:D}/financial-statements/snapshots/{snapshotSummary.SnapshotId:D}/lines/unknown-line/drilldown");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Drilldown_endpoint_is_company_scoped()
    {
        var authorizedSeed = await SeedScenarioAsync();
        var outOfScopeSeed = await SeedScenarioAsync();
        using var client = CreateAuthenticatedClient(authorizedSeed.Subject, authorizedSeed.Email, authorizedSeed.DisplayName);

        var response = await client.GetAsync(
            $"/internal/companies/{outOfScopeSeed.CompanyId:D}/finance/reports/drilldown?fiscalPeriodId={outOfScopeSeed.JanuaryPeriodId:D}&statementType=profit_and_loss&lineCode=4000");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<ScenarioSeed> SeedScenarioAsync()
    {
        var seed = new ScenarioSeed(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"finance-snapshot-{Guid.NewGuid():N}",
            $"finance-snapshot-{Guid.NewGuid():N}@example.com",
            "Finance Snapshot Owner",
            "Snapshot Drilldown Co");

        await _factory.SeedAsync(dbContext =>
        {
            SeedBaseCompanyData(dbContext, seed);
            SeedLedger(dbContext, seed);
            return Task.CompletedTask;
        });

        return seed;
    }

    private static void SeedBaseCompanyData(VirtualCompanyDbContext dbContext, ScenarioSeed seed)
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
        var equityAccountId = Guid.NewGuid();
        var revenueAccountId = Guid.NewGuid();
        var expenseAccountId = Guid.NewGuid();

        dbContext.FinanceAccounts.AddRange(
            new FinanceAccount(cashAccountId, seed.CompanyId, "1000", "Operating Cash", "asset", "USD", 0m, new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(equityAccountId, seed.CompanyId, "3000", "Owner Equity", "equity", "USD", 0m, new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(revenueAccountId, seed.CompanyId, "4000", "Sales Revenue", "revenue", "USD", 0m, new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(expenseAccountId, seed.CompanyId, "6100", "Operating Expense", "expense", "USD", 0m, new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)));

        dbContext.FinancialStatementMappings.AddRange(
            new FinancialStatementMapping(Guid.NewGuid(), seed.CompanyId, cashAccountId, FinancialStatementType.BalanceSheet, FinancialStatementReportSection.BalanceSheetAssets, FinancialStatementLineClassification.CurrentAsset),
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
                seed.JanuaryPeriodId,
                seed.CompanyId,
                "FY2026-01",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                true,
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FiscalPeriod(
                seed.FebruaryPeriodId,
                seed.CompanyId,
                "FY2026-02",
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)));
    }

    private static void SeedLedger(VirtualCompanyDbContext dbContext, ScenarioSeed seed)
    {
        var cashAccountId = dbContext.FinanceAccounts.Single(x => x.CompanyId == seed.CompanyId && x.Code == "1000").Id;
        var equityAccountId = dbContext.FinanceAccounts.Single(x => x.CompanyId == seed.CompanyId && x.Code == "3000").Id;
        var revenueAccountId = dbContext.FinanceAccounts.Single(x => x.CompanyId == seed.CompanyId && x.Code == "4000").Id;
        var expenseAccountId = dbContext.FinanceAccounts.Single(x => x.CompanyId == seed.CompanyId && x.Code == "6100").Id;

        var capitalEntry = new LedgerEntry(
            Guid.NewGuid(),
            seed.CompanyId,
            seed.JanuaryPeriodId,
            "JE-1000",
            new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc),
            LedgerEntryStatuses.Posted,
            "Owner capital");
        dbContext.LedgerEntries.Add(capitalEntry);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, capitalEntry.Id, cashAccountId, 500m, 0m, "USD", "Cash contribution"),
            new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, capitalEntry.Id, equityAccountId, 0m, 500m, "USD", "Owner equity"));

        var januaryRevenue = new LedgerEntry(
            Guid.NewGuid(),
            seed.CompanyId,
            seed.JanuaryPeriodId,
            "JE-1001",
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            LedgerEntryStatuses.Posted,
            "January sale");
        dbContext.LedgerEntries.Add(januaryRevenue);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, januaryRevenue.Id, cashAccountId, 900m, 0m, "USD", "Cash sale"),
            new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, januaryRevenue.Id, revenueAccountId, 0m, 900m, "USD", "Revenue"));

        var januaryExpense = new LedgerEntry(
            Guid.NewGuid(),
            seed.CompanyId,
            seed.JanuaryPeriodId,
            "JE-1002",
            new DateTime(2026, 1, 18, 0, 0, 0, DateTimeKind.Utc),
            LedgerEntryStatuses.Posted,
            "January expense");
        dbContext.LedgerEntries.Add(januaryExpense);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, januaryExpense.Id, expenseAccountId, 200m, 0m, "USD", "Expense"),
            new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, januaryExpense.Id, cashAccountId, 0m, 200m, "USD", "Cash payment"));

        var februaryRevenue = new LedgerEntry(
            Guid.NewGuid(),
            seed.CompanyId,
            seed.FebruaryPeriodId,
            "JE-2001",
            new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc),
            LedgerEntryStatuses.Posted,
            "February sale");
        dbContext.LedgerEntries.Add(februaryRevenue);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, februaryRevenue.Id, cashAccountId, 300m, 0m, "USD", "Cash sale"),
            new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, februaryRevenue.Id, revenueAccountId, 0m, 300m, "USD", "Revenue"));

        var februaryExpense = new LedgerEntry(
            Guid.NewGuid(),
            seed.CompanyId,
            seed.FebruaryPeriodId,
            "JE-2002",
            new DateTime(2026, 2, 14, 0, 0, 0, DateTimeKind.Utc),
            LedgerEntryStatuses.Posted,
            "February expense");
        dbContext.LedgerEntries.Add(februaryExpense);
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, februaryExpense.Id, expenseAccountId, 50m, 0m, "USD", "Expense"),
            new LedgerEntryLine(Guid.NewGuid(), seed.CompanyId, februaryExpense.Id, cashAccountId, 0m, 50m, "USD", "Cash payment"));
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record ScenarioSeed(
        Guid CompanyId,
        Guid UserId,
        Guid JanuaryPeriodId,
        Guid FebruaryPeriodId,
        string Subject,
        string Email,
        string DisplayName,
        string CompanyName);

    private sealed class FinancialStatementDrilldownResponse
    {
        public Guid CompanyId { get; set; }
        public Guid FiscalPeriodId { get; set; }
        public string FiscalPeriodName { get; set; } = string.Empty;
        public string StatementType { get; set; } = string.Empty;
        public string SourceMode { get; set; } = string.Empty;
        public SnapshotMetadataResponse? Snapshot { get; set; }
        public DrilldownLineResponse SelectedLine { get; set; } = new();
        public decimal OpeningBalanceAdjustment { get; set; }
        public decimal JournalLineTotal { get; set; }
        public decimal ReconciliationTotal { get; set; }
        public decimal ReconciliationDelta { get; set; }
        public List<DrilldownEntryResponse> JournalEntries { get; set; } = [];
    }

    private sealed class FinancialStatementSnapshotSummaryResponse
    {
        public Guid SnapshotId { get; set; }
        public Guid CompanyId { get; set; }
        public Guid FiscalPeriodId { get; set; }
        public string FiscalPeriodName { get; set; } = string.Empty;
        public string StatementType { get; set; } = string.Empty;
        public int VersionNumber { get; set; }
        public string BalancesChecksum { get; set; } = string.Empty;
        public DateTime GeneratedAtUtc { get; set; }
        public DateTime SourcePeriodStartUtc { get; set; }
        public DateTime SourcePeriodEndUtc { get; set; }
        public string Currency { get; set; } = string.Empty;
        public int LineCount { get; set; }
    }

    private sealed class FinancialStatementSnapshotDetailResponse
    {
        public Guid SnapshotId { get; set; }
        public FinancialStatementSnapshotSummaryResponse Summary { get; set; } = new();
        public List<DrilldownLineResponse> Lines { get; set; } = [];
    }

    private sealed class SnapshotMetadataResponse
    {
        public Guid SnapshotId { get; set; }
        public int VersionNumber { get; set; }
        public string BalancesChecksum { get; set; } = string.Empty;
        public DateTime GeneratedAtUtc { get; set; }
        public DateTime SourcePeriodStartUtc { get; set; }
        public DateTime SourcePeriodEndUtc { get; set; }
        public string Currency { get; set; } = string.Empty;
    }

    private sealed class DrilldownLineResponse
    {
        public Guid? AccountId { get; set; }
        public string LineCode { get; set; } = string.Empty;
        public string LineName { get; set; } = string.Empty;
        public string ReportSection { get; set; } = string.Empty;
        public string LineClassification { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
    }

    private sealed class DrilldownEntryResponse
    {
        public Guid EntryId { get; set; }
        public string EntryNumber { get; set; } = string.Empty;
        public DateTime EntryUtc { get; set; }
        public string? Description { get; set; }
        public decimal TotalContributionAmount { get; set; }
        public List<DrilldownJournalLineResponse> Lines { get; set; } = [];
    }

    private sealed class DrilldownJournalLineResponse
    {
        public Guid JournalLineId { get; set; }
        public Guid FinanceAccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public decimal DebitAmount { get; set; }
        public decimal CreditAmount { get; set; }
        public decimal ContributionAmount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string? Description { get; set; }
    }
}