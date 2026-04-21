using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BankTransactionPersistenceTests
{
    [Fact]
    public async Task EnsureCreated_maps_bank_transaction_tables_and_indexes()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var bankAccountColumns = await ReadColumnsAsync(connection, "company_bank_accounts");
        Assert.Contains("finance_account_id", bankAccountColumns);
        Assert.Contains("display_name", bankAccountColumns);
        Assert.Contains("masked_account_number", bankAccountColumns);

        var bankTransactionColumns = await ReadColumnsAsync(connection, "bank_transactions");
        Assert.Contains("bank_account_id", bankTransactionColumns);
        Assert.Contains("booking_date", bankTransactionColumns);
        Assert.Contains("value_date", bankTransactionColumns);
        Assert.Contains("status", bankTransactionColumns);
        Assert.Contains("reconciled_amount", bankTransactionColumns);

        var paymentLinkColumns = await ReadColumnsAsync(connection, "bank_transaction_payment_links");
        Assert.Contains("bank_transaction_id", paymentLinkColumns);
        Assert.Contains("payment_id", paymentLinkColumns);
        Assert.Contains("allocated_amount", paymentLinkColumns);

        var cashLedgerLinkColumns = await ReadColumnsAsync(connection, "bank_transaction_cash_ledger_links");
        Assert.Contains("bank_transaction_id", cashLedgerLinkColumns);
        Assert.Contains("ledger_entry_id", cashLedgerLinkColumns);
        Assert.Contains("idempotency_key", cashLedgerLinkColumns);

        var postingStateColumns = await ReadColumnsAsync(connection, "bank_transaction_posting_states");
        Assert.Contains("bank_transaction_id", postingStateColumns);
        Assert.Contains("matching_status", postingStateColumns);
        Assert.Contains("posting_state", postingStateColumns);

        var paymentLedgerLinkColumns = await ReadColumnsAsync(connection, "payment_cash_ledger_links");
        Assert.Contains("payment_id", paymentLedgerLinkColumns);
        Assert.Contains("ledger_entry_id", paymentLedgerLinkColumns);
        Assert.Contains("source_type", paymentLedgerLinkColumns);

        var sourceMappingColumns = await ReadColumnsAsync(connection, "ledger_entry_source_mappings");
        Assert.Contains("ledger_entry_id", sourceMappingColumns);
        Assert.Contains("source_type", sourceMappingColumns);
        Assert.Contains("posted_at", sourceMappingColumns);


        var bankTransactionIndexes = await ReadIndexesAsync(connection, "bank_transactions");
        Assert.Contains("IX_bank_transactions_company_id_bank_account_id_booking_date", bankTransactionIndexes);
        Assert.Contains("IX_bank_transactions_company_id_status_booking_date", bankTransactionIndexes);
        Assert.Contains("IX_bank_transactions_company_id_booking_date", bankTransactionIndexes);
        Assert.Contains("IX_bank_transactions_company_id_amount", bankTransactionIndexes);

        var cashLedgerIndexes = await ReadIndexesAsync(connection, "bank_transaction_cash_ledger_links");
        Assert.Contains("IX_bank_transaction_cash_ledger_links_company_id_bank_transaction_id", cashLedgerIndexes);
        Assert.Contains("IX_bank_transaction_cash_ledger_links_company_id_idempotency_key", cashLedgerIndexes);

        var postingStateIndexes = await ReadIndexesAsync(connection, "bank_transaction_posting_states");
        Assert.Contains("IX_bank_transaction_posting_states_company_id_bank_transaction_id", postingStateIndexes);
        Assert.Contains("IX_bank_transaction_posting_states_company_id_matching_status_posting_state", postingStateIndexes);

        var paymentLedgerIndexes = await ReadIndexesAsync(connection, "payment_cash_ledger_links");
        Assert.Contains("IX_payment_cash_ledger_links_company_id_payment_id_ledger_entry_id", paymentLedgerIndexes);
        Assert.Contains("IX_payment_cash_ledger_links_company_id_payment_id_source_type_source_id_posted_at", paymentLedgerIndexes);

        var sourceMappingIndexes = await ReadIndexesAsync(connection, "ledger_entry_source_mappings");
        Assert.Contains("IX_ledger_entry_source_mappings_company_id_ledger_entry_id", sourceMappingIndexes);
        Assert.Contains("IX_ledger_entry_source_mappings_company_id_source_type_source_id_posted_at", sourceMappingIndexes);
   }

    [Fact]
    public async Task SaveChanges_enforces_exact_once_cash_ledger_link_uniqueness()
    {
        await using var connection = await OpenConnectionAsync();

        var duplicateByTransaction = await CreateContextWithSchemaAsync(connection);
        var seed = await SeedCashLedgerLinkDependenciesAsync(duplicateByTransaction);
        duplicateByTransaction.BankTransactionCashLedgerLinks.Add(new BankTransactionCashLedgerLink(
            Guid.NewGuid(),
            seed.CompanyId,
            seed.FirstBankTransactionId,
            seed.FirstLedgerEntryId,
            "bank-transaction-ledger:first",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));
        await duplicateByTransaction.SaveChangesAsync();

        duplicateByTransaction.BankTransactionCashLedgerLinks.Add(new BankTransactionCashLedgerLink(
            Guid.NewGuid(),
            seed.CompanyId,
            seed.FirstBankTransactionId,
            seed.SecondLedgerEntryId,
            "bank-transaction-ledger:first-retry",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateByTransaction.SaveChangesAsync());
        await duplicateByTransaction.DisposeAsync();

        var duplicateByIdempotency = await CreateContextWithSchemaAsync(connection);
        duplicateByIdempotency.BankTransactionCashLedgerLinks.Add(new BankTransactionCashLedgerLink(
            Guid.NewGuid(),
            seed.CompanyId,
            seed.SecondBankTransactionId,
            seed.SecondLedgerEntryId,
            "bank-transaction-ledger:first",
            new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc)));

        await Assert.ThrowsAsync<DbUpdateException>(() => duplicateByIdempotency.SaveChangesAsync());
    }

    private static async Task<VirtualCompanyDbContext> CreateContextWithSchemaAsync(SqliteConnection connection)
    {
        var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        return dbContext;
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

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<CashLedgerLinkSeed> SeedCashLedgerLinkDependenciesAsync(VirtualCompanyDbContext dbContext)
    {
        var companyId = Guid.NewGuid();
        var cashAccountId = Guid.NewGuid();
        var offsetAccountId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();
        var firstBankTransactionId = Guid.NewGuid();
        var secondBankTransactionId = Guid.NewGuid();
        var firstLedgerEntryId = Guid.NewGuid();
        var secondLedgerEntryId = Guid.NewGuid();

        dbContext.Companies.Add(new Company(companyId, "Persistence Bank Transaction Company"));
        dbContext.FinanceAccounts.AddRange(
            new FinanceAccount(
                cashAccountId,
                companyId,
                "1000",
                "Operating Cash",
                FinanceAccountTypes.Asset,
                "USD",
                0m,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(
                offsetAccountId,
                companyId,
                "1100",
                "Receivables",
                FinanceAccountTypes.Asset,
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
            "**** 7781",
            "USD",
            "operating",
            true,
            true,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        dbContext.BankTransactions.AddRange(
            new BankTransaction(
                firstBankTransactionId,
                companyId,
                bankAccountId,
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                100m,
                "USD",
                "Receipt one",
                "Counterparty one"),
            new BankTransaction(
                secondBankTransactionId,
                companyId,
                bankAccountId,
                new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc),
                50m,
                "USD",
                "Receipt two",
                "Counterparty two"));

        dbContext.LedgerEntries.AddRange(
            new LedgerEntry(firstLedgerEntryId, companyId, fiscalPeriodId, "BTX-001", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc), LedgerEntryStatuses.Posted, "Bank reconciliation one"),
            new LedgerEntry(secondLedgerEntryId, companyId, fiscalPeriodId, "BTX-002", new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc), LedgerEntryStatuses.Posted, "Bank reconciliation two"));

        await dbContext.SaveChangesAsync();
        return new CashLedgerLinkSeed(companyId, firstBankTransactionId, secondBankTransactionId, firstLedgerEntryId, secondLedgerEntryId);
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options);

    private sealed record CashLedgerLinkSeed(
        Guid CompanyId,
        Guid FirstBankTransactionId,
        Guid SecondBankTransactionId,
        Guid FirstLedgerEntryId,
        Guid SecondLedgerEntryId);
}