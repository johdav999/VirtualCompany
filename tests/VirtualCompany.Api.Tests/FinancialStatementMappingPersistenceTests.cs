using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancialStatementMappingPersistenceTests
{
    [Fact]
    public async Task EnsureCreated_maps_financial_statement_mappings_table_and_partial_unique_index()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var columns = await ReadColumnsAsync(connection, "financial_statement_mappings");
        Assert.Contains("company_id", columns);
        Assert.Contains("finance_account_id", columns);
        Assert.Contains("statement_type", columns);
        Assert.Contains("report_section", columns);
        Assert.Contains("line_classification", columns);
        Assert.Contains("is_active", columns);
        Assert.Contains("created_at", columns);
        Assert.Contains("updated_at", columns);

        var indexes = await ReadIndexMetadataAsync(connection, "financial_statement_mappings");
        Assert.True(indexes.TryGetValue("IX_financial_statement_mappings_finance_account_id", out var accountIndex));
        Assert.False(accountIndex.IsUnique);
        Assert.False(accountIndex.IsPartial);

        Assert.True(indexes.TryGetValue("IX_financial_statement_mappings_company_id_finance_account_id_statement_type", out var uniquenessIndex));
        Assert.True(uniquenessIndex.IsUnique);
        Assert.True(uniquenessIndex.IsPartial);
    }

    [Fact]
    public async Task SaveChanges_rejects_two_active_mappings_for_same_account_and_statement_type()
    {
        var companyId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Financial Statement Constraint Company"));
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountId,
            companyId,
            "1000",
            "Operating Cash",
            "asset",
            "USD",
            0m,
            new DateTime(2026, 4, 20, 8, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        dbContext.FinancialStatementMappings.AddRange(
            new FinancialStatementMapping(
                Guid.NewGuid(),
                companyId,
                accountId,
                FinancialStatementType.BalanceSheet,
                FinancialStatementReportSection.BalanceSheetAssets,
                FinancialStatementLineClassification.CurrentAsset),
            new FinancialStatementMapping(
                Guid.NewGuid(),
                companyId,
                accountId,
                FinancialStatementType.BalanceSheet,
                FinancialStatementReportSection.BalanceSheetAssets,
                FinancialStatementLineClassification.NonCurrentAsset));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChanges_allows_inactive_historical_mapping_for_same_account_and_statement_type()
    {
        var companyId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Historical Mapping Company"));
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountId,
            companyId,
            "2000",
            "Payables",
            "liability",
            "USD",
            0m,
            new DateTime(2026, 4, 20, 8, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        dbContext.FinancialStatementMappings.AddRange(
            new FinancialStatementMapping(
                Guid.NewGuid(),
                companyId,
                accountId,
                FinancialStatementType.BalanceSheet,
                FinancialStatementReportSection.BalanceSheetLiabilities,
                FinancialStatementLineClassification.CurrentLiability,
                isActive: true),
            new FinancialStatementMapping(
                Guid.NewGuid(),
                companyId,
                accountId,
                FinancialStatementType.BalanceSheet,
                FinancialStatementReportSection.BalanceSheetLiabilities,
                FinancialStatementLineClassification.NonCurrentLiability,
                isActive: false));

        await dbContext.SaveChangesAsync();

        var count = await dbContext.FinancialStatementMappings
            .IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == companyId && x.FinanceAccountId == accountId);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Finance_seed_bootstrap_creates_seeded_default_mappings_deterministically()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Bootstrap Mapping Company"));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceSeedBootstrapService(dbContext);
        var firstResult = await service.GenerateAsync(new FinanceSeedBootstrapCommand(companyId, 42), CancellationToken.None);

        var firstPass = await ReadSeededMappingsAsync(dbContext, companyId);
        var firstPaymentIds = await ReadSeededPaymentIdsAsync(dbContext, companyId);

        Assert.True(firstResult.PaymentCount > 0);
        Assert.Equal(
            [
                "1000|balance_sheet|balance_sheet_assets|current_asset|True",
                "1100|balance_sheet|balance_sheet_assets|current_asset|True",
                "1100|cash_flow|cash_flow_operating_activities|working_capital|True",
                "2000|balance_sheet|balance_sheet_liabilities|current_liability|True",
                "2000|cash_flow|cash_flow_operating_activities|working_capital|True",
                "6100|cash_flow|cash_flow_operating_activities|cash_disbursement|True",
                "6100|profit_and_loss|profit_and_loss_operating_expenses|operating_expense|True"
            ],
            firstPass);
        Assert.Equal(firstResult.PaymentCount, firstPaymentIds.Length);

        var reusedResult = await service.GenerateAsync(
            new FinanceSeedBootstrapCommand(companyId, 42, ReplaceExisting: false),
            CancellationToken.None);

        var reusedPass = await ReadSeededMappingsAsync(dbContext, companyId);
        var reusedPaymentIds = await ReadSeededPaymentIdsAsync(dbContext, companyId);

        Assert.Equal(firstPass, reusedPass);
        Assert.Equal(firstResult.PaymentCount, reusedResult.PaymentCount);
        Assert.Equal(firstPaymentIds, reusedPaymentIds);
        Assert.Equal(reusedPass.Length, reusedPass.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(reusedPaymentIds.Length, reusedPaymentIds.Distinct().Count());

        var regeneratedResult = await service.GenerateAsync(
            new FinanceSeedBootstrapCommand(companyId, 42, ReplaceExisting: true),
            CancellationToken.None);

        var regeneratedPass = await ReadSeededMappingsAsync(dbContext, companyId);
        var regeneratedPaymentIds = await ReadSeededPaymentIdsAsync(dbContext, companyId);

        Assert.Equal(firstPass, regeneratedPass);
        Assert.Equal(firstResult.PaymentCount, regeneratedResult.PaymentCount);
        Assert.Equal(firstPaymentIds, regeneratedPaymentIds);
        Assert.Equal(regeneratedPass.Length, regeneratedPass.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(regeneratedPaymentIds.Length, regeneratedPaymentIds.Distinct().Count());
    }

    [Fact]
    public async Task Finance_seed_bootstrap_replace_existing_removes_cash_posting_traceability_entries()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Bootstrap Cash Traceability Company"));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceSeedBootstrapService(dbContext);
        await service.GenerateAsync(new FinanceSeedBootstrapCommand(companyId, 42), CancellationToken.None);

        var bankTransaction = await dbContext.BankTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.BookingDate)
            .FirstAsync();
        var payment = await dbContext.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.PaymentDate)
            .FirstAsync();
        var fiscalPeriodId = await dbContext.FiscalPeriods
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync() ?? Guid.Empty;

        if (fiscalPeriodId == Guid.Empty)
        {
            fiscalPeriodId = Guid.NewGuid();
            dbContext.FiscalPeriods.Add(new FiscalPeriod(
                fiscalPeriodId,
                companyId,
                "FY 2026",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            await dbContext.SaveChangesAsync();
        }

        var accountIds = await dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Code)
            .Select(x => x.Id)
            .Take(2)
            .ToArrayAsync();

        var ledgerEntryId = Guid.NewGuid();
        dbContext.LedgerEntries.Add(new LedgerEntry(
            ledgerEntryId,
            companyId,
            fiscalPeriodId,
            "CSP-LEGACY-001",
            bankTransaction.BookingDate,
            LedgerEntryStatuses.Posted,
            "Legacy cash settlement",
            FinanceCashPostingSourceTypes.BankTransaction,
            bankTransaction.Id.ToString("D"),
            bankTransaction.BookingDate,
            bankTransaction.BookingDate,
            bankTransaction.BookingDate));
        dbContext.LedgerEntryLines.AddRange(
            new LedgerEntryLine(Guid.NewGuid(), companyId, ledgerEntryId, accountIds[0], 100m, 0m, bankTransaction.Currency, "Legacy cash settlement"),
            new LedgerEntryLine(Guid.NewGuid(), companyId, ledgerEntryId, accountIds[1], 0m, 100m, bankTransaction.Currency, "Legacy cash settlement"));
        dbContext.LedgerEntrySourceMappings.Add(new LedgerEntrySourceMapping(
            Guid.NewGuid(),
            companyId,
            ledgerEntryId,
            FinanceCashPostingSourceTypes.BankTransaction,
            bankTransaction.Id.ToString("D"),
            bankTransaction.BookingDate,
            bankTransaction.BookingDate));
        dbContext.BankTransactionCashLedgerLinks.Add(new BankTransactionCashLedgerLink(
            Guid.NewGuid(),
            companyId,
            bankTransaction.Id,
            ledgerEntryId,
            $"bank-transaction-ledger:{companyId:N}:{bankTransaction.Id:N}",
            bankTransaction.BookingDate));
        dbContext.PaymentCashLedgerLinks.Add(new PaymentCashLedgerLink(
            Guid.NewGuid(),
            companyId,
            payment.Id,
            ledgerEntryId,
            FinanceCashPostingSourceTypes.BankTransaction,
            bankTransaction.Id.ToString("D"),
            bankTransaction.BookingDate,
            bankTransaction.BookingDate));
        await dbContext.SaveChangesAsync();

        await service.GenerateAsync(new FinanceSeedBootstrapCommand(companyId, 42, ReplaceExisting: true), CancellationToken.None);

        Assert.Empty(await dbContext.PaymentCashLedgerLinks.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync());
        Assert.Empty(await dbContext.BankTransactionCashLedgerLinks.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync());
        Assert.Empty(await dbContext.LedgerEntrySourceMappings.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && (x.SourceType == FinanceCashPostingSourceTypes.BankTransaction || x.SourceType == FinanceCashPostingSourceTypes.PaymentAllocation || x.SourceType == FinanceCashPostingSourceTypes.PaymentSettlement)).ToListAsync());
        Assert.Empty(await dbContext.LedgerEntryLines.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.LedgerEntryId == ledgerEntryId).ToListAsync());
        Assert.False(await dbContext.LedgerEntries.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == ledgerEntryId));
    }

    private static async Task<string[]> ReadSeededMappingsAsync(VirtualCompanyDbContext dbContext, Guid companyId) =>
        await dbContext.FinancialStatementMappings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.FinanceAccount.Code)
            .ThenBy(x => x.StatementType)
            .Select(x => string.Concat(
                x.FinanceAccount.Code,
                "|",
                x.StatementType.ToStorageValue(),
                "|",
                x.ReportSection.ToStorageValue(),
                "|",
                x.LineClassification.ToStorageValue(),
                "|",
                x.IsActive))
            .ToArrayAsync();

    private static async Task<Guid[]> ReadSeededPaymentIdsAsync(VirtualCompanyDbContext dbContext, Guid companyId) =>
        await dbContext.Payments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.PaymentDate)
            .ThenBy(x => x.Id)
            .Select(x => x.Id)
            .ToArrayAsync();

    private static async Task<HashSet<string>> ReadColumnsAsync(SqliteConnection connection, string tableName)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task<Dictionary<string, IndexMetadata>> ReadIndexMetadataAsync(SqliteConnection connection, string tableName)
    {
        var indexes = new Dictionary<string, IndexMetadata>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list('{tableName}');";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes[reader.GetString(1)] = new IndexMetadata(
                IsUnique: reader.GetInt64(2) == 1,
                IsPartial: reader.GetInt64(4) == 1);
        }

        return indexes;
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options);

    private readonly record struct IndexMetadata(
        bool IsUnique,
        bool IsPartial);
}
