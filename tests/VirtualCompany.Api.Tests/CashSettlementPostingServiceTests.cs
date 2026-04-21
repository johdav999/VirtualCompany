using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CashSettlementPostingServiceTests
{
    [Fact]
    public async Task SettledCustomerPayment_CreatesSingleBalancedCashToReceivablesJournalEntry()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var postedAtUtc = new DateTime(2026, 4, 20, 10, 15, 0, DateTimeKind.Utc);
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        var seed = await SeedPostingScenarioAsync(dbContext, companyId);
        var service = new CompanyCashSettlementPostingService(dbContext, accessor);

        var result = await service.PostCashSettlementAsync(
            new PostCashSettlementCommand(
                companyId,
                FinanceCashPostingSourceTypes.PaymentAllocation,
                "allocation-incoming-001",
                seed.IncomingPaymentId,
                100m,
                postedAtUtc),
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(companyId, result.CompanyId);
        Assert.Equal(FinanceCashPostingSourceTypes.PaymentAllocation, result.SourceType);
        Assert.Equal("allocation-incoming-001", result.SourceId);
        Assert.Equal(100m, result.PostedAmount);
        Assert.Equal(postedAtUtc, result.PostedAtUtc);

        var entry = await LoadLedgerEntryAsync(dbContext, companyId, result.LedgerEntryId);
        var lines = await LoadLedgerLinesAsync(dbContext, companyId, entry.Id);
        var sourceMapping = await LoadSourceMappingAsync(dbContext, companyId, entry.Id);

        Assert.Equal(
            1,
            await dbContext.LedgerEntries
                .IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId && x.SourceType == FinanceCashPostingSourceTypes.PaymentAllocation && x.SourceId == "allocation-incoming-001"));
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(result.LedgerEntryId, entry.Id);
        Assert.Equal(FinanceCashPostingSourceTypes.PaymentAllocation, entry.SourceType);
        Assert.Equal("allocation-incoming-001", entry.SourceId);
        Assert.Equal(postedAtUtc, entry.PostedAtUtc);
        Assert.Equal(LedgerEntryStatuses.Posted, entry.Status);
        Assert.Equal(companyId, sourceMapping.CompanyId);
        Assert.Equal(entry.Id, sourceMapping.Id);
        Assert.Equal(entry.Id, sourceMapping.LedgerEntryId);
        Assert.Equal(FinanceCashPostingSourceTypes.PaymentAllocation, sourceMapping.SourceType);
        Assert.Equal("allocation-incoming-001", sourceMapping.SourceId);
        Assert.Equal(new DateTime(2026, 4, 20, 10, 15, 0, DateTimeKind.Utc), sourceMapping.PostedAtUtc);
        Assert.Equal(2, lines.Count);
        Assert.Equal(seed.CashAccountId, lines[0].FinanceAccountId);
        Assert.Equal(100m, lines[0].DebitAmount);
        Assert.Equal(0m, lines[0].CreditAmount);
        Assert.Equal(seed.ReceivablesAccountId, lines[1].FinanceAccountId);
        Assert.Equal(0m, lines[1].DebitAmount);
        Assert.Equal(100m, lines[1].CreditAmount);
        Assert.Equal(lines.Sum(x => x.DebitAmount), lines.Sum(x => x.CreditAmount));
    }

    [Fact]
    public async Task SettledSupplierPayment_CreatesSingleBalancedPayablesToCashJournalEntry()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var postedAtUtc = new DateTime(2026, 4, 21, 11, 0, 0, DateTimeKind.Utc);
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        var seed = await SeedPostingScenarioAsync(dbContext, companyId);
        var service = new CompanyCashSettlementPostingService(dbContext, accessor);

        var result = await service.PostCashSettlementAsync(
            new PostCashSettlementCommand(
                companyId,
                FinanceCashPostingSourceTypes.PaymentAllocation,
                "allocation-outgoing-001",
                seed.OutgoingPaymentId,
                80m,
                postedAtUtc),
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(companyId, result.CompanyId);
        Assert.Equal(FinanceCashPostingSourceTypes.PaymentAllocation, result.SourceType);
        Assert.Equal("allocation-outgoing-001", result.SourceId);
        Assert.Equal(80m, result.PostedAmount);
        Assert.Equal(postedAtUtc, result.PostedAtUtc);

        var entry = await LoadLedgerEntryAsync(dbContext, companyId, result.LedgerEntryId);
        var lines = await LoadLedgerLinesAsync(dbContext, companyId, result.LedgerEntryId);
        var sourceMapping = await LoadSourceMappingAsync(dbContext, companyId, result.LedgerEntryId);

        Assert.Equal(
            1,
            await dbContext.LedgerEntries
                .IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId && x.SourceType == FinanceCashPostingSourceTypes.PaymentAllocation && x.SourceId == "allocation-outgoing-001"));
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(FinanceCashPostingSourceTypes.PaymentAllocation, entry.SourceType);
        Assert.Equal("allocation-outgoing-001", entry.SourceId);
        Assert.Equal(postedAtUtc, entry.PostedAtUtc);
        Assert.Equal(2, lines.Count);
        Assert.Equal(companyId, sourceMapping.CompanyId);
        Assert.Equal(FinanceCashPostingSourceTypes.PaymentAllocation, sourceMapping.SourceType);
        Assert.Equal("allocation-outgoing-001", sourceMapping.SourceId);
        Assert.Equal(postedAtUtc, sourceMapping.PostedAtUtc);
        Assert.Equal(seed.PayablesAccountId, lines[0].FinanceAccountId);
        Assert.Equal(80m, lines[0].DebitAmount);
        Assert.Equal(0m, lines[0].CreditAmount);
        Assert.Equal(seed.CashAccountId, lines[1].FinanceAccountId);
        Assert.Equal(0m, lines[1].DebitAmount);
        Assert.Equal(80m, lines[1].CreditAmount);
        Assert.Equal(lines.Sum(x => x.DebitAmount), lines.Sum(x => x.CreditAmount));
    }

    [Fact]
    public async Task PartialSettlement_PostsSettledAmountOnly()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var postedAtUtc = new DateTime(2026, 4, 22, 9, 30, 0, DateTimeKind.Utc);
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        var seed = await SeedPostingScenarioAsync(dbContext, companyId);
        var service = new CompanyCashSettlementPostingService(dbContext, accessor);

        var result = await service.PostCashSettlementAsync(
            new PostCashSettlementCommand(
                companyId,
                FinanceCashPostingSourceTypes.PaymentAllocation,
                "allocation-partial-001",
                seed.IncomingPaymentId,
                40.25m,
                postedAtUtc),
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(40.25m, result.PostedAmount);
        Assert.Equal(postedAtUtc, result.PostedAtUtc);

        var entry = await LoadLedgerEntryAsync(dbContext, companyId, result.LedgerEntryId);
        var lines = await LoadLedgerLinesAsync(dbContext, companyId, result.LedgerEntryId);
        var sourceMapping = await LoadSourceMappingAsync(dbContext, companyId, result.LedgerEntryId);

        Assert.Equal(
            1,
            await dbContext.LedgerEntries
                .IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId && x.SourceType == FinanceCashPostingSourceTypes.PaymentAllocation && x.SourceId == "allocation-partial-001"));
        Assert.Equal(FinanceCashPostingSourceTypes.PaymentAllocation, entry.SourceType);
        Assert.Equal("allocation-partial-001", entry.SourceId);
        Assert.Equal(postedAtUtc, entry.PostedAtUtc);
        Assert.Equal(postedAtUtc, sourceMapping.PostedAtUtc);
        Assert.Equal(2, lines.Count);
        Assert.Equal(seed.CashAccountId, lines[0].FinanceAccountId);
        Assert.Equal(40.25m, lines[0].DebitAmount);
        Assert.Equal(0m, lines[0].CreditAmount);
        Assert.Equal(seed.ReceivablesAccountId, lines[1].FinanceAccountId);
        Assert.Equal(0m, lines[1].DebitAmount);
        Assert.Equal(40.25m, lines[1].CreditAmount);
        Assert.Equal(lines.Sum(x => x.DebitAmount), lines.Sum(x => x.CreditAmount));
    }

    [Fact]
    public async Task ReprocessingLaterSettlementTimestamp_CreatesNewJournalEntryWhileSameTimestampReplayReturnsExistingResult()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var firstPostedAtUtc = new DateTime(2026, 4, 22, 12, 0, 0, DateTimeKind.Utc);
        var secondPostedAtUtc = new DateTime(2026, 4, 23, 12, 0, 0, DateTimeKind.Utc);
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        var seed = await SeedPostingScenarioAsync(dbContext, companyId);
        var service = new CompanyCashSettlementPostingService(dbContext, accessor);

        var firstCommand = new PostCashSettlementCommand(
            companyId,
            FinanceCashPostingSourceTypes.PaymentSettlement,
            "payment-progress-001",
            seed.IncomingPaymentId,
            40m, firstPostedAtUtc);

        var secondCommand = firstCommand with
        {
            SettledAmount = 15m,
            SettledAtUtc = secondPostedAtUtc
        };

        var first = await service.PostCashSettlementAsync(firstCommand, CancellationToken.None);
        var replay = await service.PostCashSettlementAsync(firstCommand, CancellationToken.None);
        var second = await service.PostCashSettlementAsync(secondCommand, CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.True(second.Created);
        Assert.Equal(first.LedgerEntryId, replay.LedgerEntryId);
        Assert.NotEqual(first.LedgerEntryId, second.LedgerEntryId);
        Assert.Equal(first.SourceType, replay.SourceType);
        Assert.Equal(first.SourceId, replay.SourceId);
        Assert.Equal(first.PostedAtUtc, replay.PostedAtUtc);
        Assert.Equal(40m, first.PostedAmount);
        Assert.Equal(15m, second.PostedAmount);

        Assert.Equal(
            2,
            await dbContext.LedgerEntries
                .IgnoreQueryFilters()
                .CountAsync(x =>
                    x.CompanyId == companyId &&
                    x.SourceType == FinanceCashPostingSourceTypes.PaymentSettlement &&
                    x.SourceId == "payment-progress-001"));
        Assert.Equal(
            4,
            await dbContext.LedgerEntryLines
                .IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId && (x.LedgerEntryId == first.LedgerEntryId || x.LedgerEntryId == second.LedgerEntryId)));
        var sourceMappings = await dbContext.LedgerEntrySourceMappings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.SourceType == FinanceCashPostingSourceTypes.PaymentSettlement &&
                x.SourceId == "payment-progress-001")
            .OrderBy(x => x.PostedAtUtc)
            .ToListAsync();

        Assert.Equal(2, sourceMappings.Select(x => x.LedgerEntryId).Distinct().Count());
        Assert.Equal(2, sourceMappings.Count);
        Assert.Equal(firstPostedAtUtc, sourceMappings[0].PostedAtUtc);
        Assert.Equal(secondPostedAtUtc, sourceMappings[1].PostedAtUtc);
    }

    [Fact]
    public async Task ReprocessingSameSettledPayment_DoesNotCreateDuplicateJournalEntryAndReturnsExistingPostingResult()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var postedAtUtc = new DateTime(2026, 4, 23, 8, 0, 0, DateTimeKind.Utc);
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        var seed = await SeedPostingScenarioAsync(dbContext, companyId);
        var service = new CompanyCashSettlementPostingService(dbContext, accessor);

        var command = new PostCashSettlementCommand(
            companyId,
            FinanceCashPostingSourceTypes.PaymentSettlement,
            "payment-replay-001",
            seed.IncomingPaymentId,
            55m,
            postedAtUtc);

        var first = await service.PostCashSettlementAsync(command, CancellationToken.None);
        var second = await service.PostCashSettlementAsync(command, CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.LedgerEntryId, second.LedgerEntryId);
        Assert.Equal(first.CompanyId, second.CompanyId);
        Assert.Equal(first.SourceType, second.SourceType);
        Assert.Equal(first.SourceId, second.SourceId);
        Assert.Equal(first.PostedAmount, second.PostedAmount);
        Assert.Equal(first.PostedAtUtc, second.PostedAtUtc);
        Assert.Equal(postedAtUtc, second.PostedAtUtc);

        var entry = await LoadLedgerEntryAsync(dbContext, companyId, first.LedgerEntryId);
        var lines = await LoadLedgerLinesAsync(dbContext, companyId, first.LedgerEntryId);
        var sourceMapping = await LoadSourceMappingAsync(dbContext, companyId, first.LedgerEntryId);

        Assert.Equal(
            1,
            await dbContext.LedgerEntrySourceMappings
                .IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId && x.SourceType == FinanceCashPostingSourceTypes.PaymentSettlement && x.SourceId == "payment-replay-001"));
        Assert.Equal(
            1,
            await dbContext.LedgerEntries
                .IgnoreQueryFilters()
                .CountAsync(x =>
                    x.CompanyId == companyId &&
                    x.SourceType == FinanceCashPostingSourceTypes.PaymentSettlement &&
                    x.SourceId == "payment-replay-001"));
        Assert.Equal(
            2,
            await dbContext.LedgerEntryLines
                .IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId && x.LedgerEntryId == first.LedgerEntryId));
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(FinanceCashPostingSourceTypes.PaymentSettlement, entry.SourceType);
        Assert.Equal("payment-replay-001", entry.SourceId);
        Assert.Equal(postedAtUtc, entry.PostedAtUtc);
        Assert.Equal(companyId, sourceMapping.CompanyId);
        Assert.Equal(FinanceCashPostingSourceTypes.PaymentSettlement, sourceMapping.SourceType);
        Assert.Equal("payment-replay-001", sourceMapping.SourceId);
        Assert.Equal(postedAtUtc, sourceMapping.PostedAtUtc);
        Assert.Equal(lines.Sum(x => x.DebitAmount), lines.Sum(x => x.CreditAmount));
    }

    [Fact]
    public async Task Posting_cash_settlement_creates_payment_cash_traceability_link()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var postedAtUtc = new DateTime(2026, 4, 24, 8, 30, 0, DateTimeKind.Utc);
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        var seed = await SeedPostingScenarioAsync(dbContext, companyId);
        var service = new CompanyCashSettlementPostingService(dbContext, accessor);

        var result = await service.PostCashSettlementAsync(
            new PostCashSettlementCommand(
                companyId,
                FinanceCashPostingSourceTypes.PaymentSettlement,
                "payment-traceability-001",
                seed.IncomingPaymentId,
                25m,
                postedAtUtc),
            CancellationToken.None);

        var paymentLedgerLink = await dbContext.PaymentCashLedgerLinks
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.PaymentId == seed.IncomingPaymentId && x.LedgerEntryId == result.LedgerEntryId);

        Assert.Equal(FinanceCashPostingSourceTypes.PaymentSettlement, paymentLedgerLink.SourceType);
        Assert.Equal("payment-traceability-001", paymentLedgerLink.SourceId);
        Assert.Equal(postedAtUtc, paymentLedgerLink.PostedAtUtc);
    }

    [Fact]
    public async Task Unmatched_bank_transaction_source_does_not_create_cash_settlement_journal_entry()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        var seed = await SeedPostingScenarioAsync(dbContext, companyId);
        var bankAccountId = await dbContext.CompanyBankAccounts
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Select(x => x.Id)
            .SingleAsync();
        var bankTransactionId = Guid.NewGuid();
        dbContext.BankTransactions.Add(new BankTransaction(
            bankTransactionId,
            companyId,
            bankAccountId,
            new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc),
            100m,
            "USD",
            "Unmatched remittance",
            "Unknown counterparty"));
        await dbContext.SaveChangesAsync();

        var service = new CompanyCashSettlementPostingService(dbContext, accessor);
        await Assert.ThrowsAsync<FinanceValidationException>(() => service.PostCashSettlementAsync(
            new PostCashSettlementCommand(companyId, FinanceCashPostingSourceTypes.BankTransaction, bankTransactionId.ToString("D"), seed.IncomingPaymentId, 100m, new DateTime(2026, 4, 24, 1, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None));
    }

    [Fact]
    public async Task Manually_classified_bank_transaction_source_can_create_cash_settlement_journal_entry()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        var seed = await SeedPostingScenarioAsync(dbContext, companyId);
        var bankAccountId = await dbContext.CompanyBankAccounts
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Select(x => x.Id)
            .SingleAsync();
        var bankTransactionId = Guid.NewGuid();
        dbContext.BankTransactions.Add(new BankTransaction(
            bankTransactionId,
            companyId,
            bankAccountId,
            new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc),
            25m,
            "USD",
            "Manually classified remittance",
            "Unknown counterparty"));
        await dbContext.SaveChangesAsync();

        var state = await dbContext.BankTransactionPostingStateRecords
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.BankTransactionId == bankTransactionId);
        state.SyncSnapshot(
            BankTransactionMatchingStatuses.ManuallyClassified,
            BankTransactionPostingStates.Pending,
            0,
            new DateTime(2026, 4, 24, 0, 30, 0, DateTimeKind.Utc));
        await dbContext.SaveChangesAsync();

        var service = new CompanyCashSettlementPostingService(dbContext, accessor);
        var result = await service.PostCashSettlementAsync(
            new PostCashSettlementCommand(
                companyId,
                FinanceCashPostingSourceTypes.BankTransaction,
                bankTransactionId.ToString("D"),
                seed.IncomingPaymentId,
                25m,
                new DateTime(2026, 4, 24, 1, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.Equal(FinanceCashPostingSourceTypes.BankTransaction, result.SourceType);
        Assert.Equal(bankTransactionId.ToString("D"), result.SourceId);
    }

    [Fact]
    public async Task Ensure_created_maps_traceability_columns_unique_source_index_and_source_mapping_table()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection, new TestCompanyContextAccessor(Guid.NewGuid()));
        await dbContext.Database.EnsureCreatedAsync();

        var columns = await ReadColumnsAsync(connection, "ledger_entries");
        var indexes = await ReadIndexesAsync(connection, "ledger_entries");
        var sourceMappingColumns = await ReadColumnsAsync(connection, "ledger_entry_source_mappings");
        var sourceMappingIndexes = await ReadIndexesAsync(connection, "ledger_entry_source_mappings");

        Assert.Contains("source_type", columns);
        Assert.Contains("source_id", columns);
        Assert.Contains("posted_at", columns);
        Assert.Contains("IX_ledger_entries_company_id_source_type_source_id_posted_at", indexes);
        Assert.Contains("ledger_entry_id", sourceMappingColumns);
        Assert.Contains("source_type", sourceMappingColumns);
        Assert.Contains("source_id", sourceMappingColumns);
        Assert.Contains("posted_at", sourceMappingColumns);
        Assert.Contains("IX_ledger_entry_source_mappings_company_id_ledger_entry_id", sourceMappingIndexes);
        Assert.Contains("IX_ledger_entry_source_mappings_company_id_source_type_source_id_posted_at", sourceMappingIndexes);
    }


    private static async Task<PostingSeed> SeedPostingScenarioAsync(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var cashAccountId = Guid.NewGuid();
        var receivablesAccountId = Guid.NewGuid();
        var payablesAccountId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();
        var incomingPaymentId = Guid.NewGuid();
        var outgoingPaymentId = Guid.NewGuid();

        dbContext.Companies.Add(new Company(companyId, "Cash Settlement Company"));
        dbContext.FinanceAccounts.AddRange(
            new FinanceAccount(
                cashAccountId,
                companyId,
                "1000",
                "Operating Cash",
                "asset",
                "USD",
                0m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(
                receivablesAccountId,
                companyId,
                "1100",
                "Accounts Receivable",
                "asset",
                "USD",
                0m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(
                payablesAccountId,
                companyId,
                "2000",
                "Accounts Payable",
                "liability",
                "USD",
                0m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        dbContext.FiscalPeriods.Add(new FiscalPeriod(
            fiscalPeriodId,
            companyId,
            "FY 2026",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        dbContext.CompanyBankAccounts.Add(new CompanyBankAccount(
            bankAccountId,
            companyId,
            cashAccountId,
            "Operating Account",
            "Northwind Bank",
            "**** 1000",
            "USD",
            "operating",
            true,
            true,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        dbContext.Payments.AddRange(
            new Payment(
                incomingPaymentId,
                companyId,
                PaymentTypes.Incoming,
                100m,
                "USD",
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                "bank_transfer",
                PaymentStatuses.Completed,
                "INV-SETTLEMENT-001"),
            new Payment(
                outgoingPaymentId,
                companyId,
                PaymentTypes.Outgoing,
                80m,
                "USD",
                new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc),
                "bank_transfer",
                PaymentStatuses.Completed,
                "BILL-SETTLEMENT-001"));

        await dbContext.SaveChangesAsync();

        return new PostingSeed(
            companyId,
            cashAccountId,
            receivablesAccountId,
            payablesAccountId,
            incomingPaymentId,
            outgoingPaymentId);
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(SqliteConnection connection, string tableName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(1));
        }

        return values;
    }

    private static async Task<HashSet<string>> ReadIndexesAsync(SqliteConnection connection, string tableName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(1));
        }

        return values;
    }

    private static Task<LedgerEntry> LoadLedgerEntryAsync(
        VirtualCompanyDbContext dbContext,
        Guid companyId,
        Guid ledgerEntryId) =>
        dbContext.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == ledgerEntryId);

    private static Task<List<LedgerEntryLine>> LoadLedgerLinesAsync(
        VirtualCompanyDbContext dbContext,
        Guid companyId,
        Guid ledgerEntryId) =>
        dbContext.LedgerEntryLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.LedgerEntryId == ledgerEntryId)
            .OrderBy(x => x.DebitAmount == 0m)
            .ToListAsync();

    private static Task<LedgerEntrySourceMapping> LoadSourceMappingAsync(
        VirtualCompanyDbContext dbContext,
        Guid companyId,
        Guid ledgerEntryId) =>
        dbContext.LedgerEntrySourceMappings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.LedgerEntryId == ledgerEntryId);

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection, ICompanyContextAccessor accessor) =>
        new(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options,
            accessor);

    private sealed record PostingSeed(
        Guid CompanyId,
        Guid CashAccountId,
        Guid ReceivablesAccountId,
        Guid PayablesAccountId,
        Guid IncomingPaymentId,
        Guid OutgoingPaymentId);

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(Guid companyId)
        {
            CompanyId = companyId;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId { get; private set; }
        public bool IsResolved => Membership is not null;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }

        public void SetCompanyId(Guid? companyId)
        {
            CompanyId = companyId;
            Membership = null;
            UserId = null;
        }

        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            Membership = companyContext;
            CompanyId = companyContext?.CompanyId;
            UserId = companyContext?.UserId;
        }
    }
}
