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

public sealed class CashPostingTraceabilityBackfillServiceTests
{
    [Fact]
    public async Task Backfill_creates_missing_traceability_and_unmatched_state_without_duplicates_on_rerun()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        var logger = new ScopeCapturingLogger<CompanyCashPostingTraceabilityBackfillService>();

        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        var seed = await SeedLegacyScenarioAsync(dbContext, companyId);
        var service = new CompanyCashPostingTraceabilityBackfillService(dbContext, accessor, logger);

        var first = await service.BackfillAsync(
            new BackfillCashPostingTraceabilityCommand(companyId, BatchSize: 1, CorrelationId: "backfill-run-001"),
            CancellationToken.None);

        Assert.Equal(companyId, first.CompanyId);
        Assert.Equal("backfill-run-001", first.CorrelationId);
        Assert.Equal(1, first.MigratedRecordCount);
        Assert.Equal(3, first.BackfilledRecordCount);
        Assert.Equal(0, first.ConflictCount);

        var postingStates = await dbContext.BankTransactionPostingStateRecords
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.BankTransactionId)
            .ToListAsync();
        var matchedState = Assert.Single(postingStates.Where(x => x.BankTransactionId == seed.MatchedBankTransactionId));
        var unmatchedState = Assert.Single(postingStates.Where(x => x.BankTransactionId == seed.UnmatchedBankTransactionId));
        Assert.Equal(BankTransactionMatchingStatuses.Matched, matchedState.MatchingStatus);
        Assert.Equal(BankTransactionPostingStates.Posted, matchedState.PostingState);
        Assert.Equal(1, matchedState.LinkedPaymentCount);
        Assert.Equal(BankTransactionMatchingStatuses.Unmatched, unmatchedState.MatchingStatus);
        Assert.Equal(BankTransactionPostingStates.SkippedUnmatched, unmatchedState.PostingState);
        Assert.Equal("no_payment_match", unmatchedState.UnmatchedReason);

        Assert.Single(await dbContext.BankTransactionCashLedgerLinks
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.BankTransactionId == seed.MatchedBankTransactionId)
            .ToListAsync());

        var paymentLedgerLink = await dbContext.PaymentCashLedgerLinks
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == companyId && x.PaymentId == seed.PaymentId);
        Assert.Equal(seed.LedgerEntryId, paymentLedgerLink.LedgerEntryId);
        Assert.Equal(FinanceCashPostingSourceTypes.BankTransaction, paymentLedgerLink.SourceType);
        Assert.Equal(seed.MatchedBankTransactionId.ToString("D"), paymentLedgerLink.SourceId);

        var second = await service.BackfillAsync(
            new BackfillCashPostingTraceabilityCommand(companyId, BatchSize: 10, CorrelationId: "backfill-run-002"),
            CancellationToken.None);

        Assert.Equal(0, second.MigratedRecordCount);
        Assert.Equal(0, second.BackfilledRecordCount);
        Assert.Equal(0, second.ConflictCount);
        Assert.True(second.SkippedRecordCount > 0);
        Assert.Single(await dbContext.PaymentCashLedgerLinks.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync());
        Assert.Equal(2, await dbContext.BankTransactionPostingStateRecords.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId));

        var entry = Assert.Single(logger.Entries.Where(x => x.Message.Contains("Completed cash posting traceability backfill", StringComparison.Ordinal)));
        Assert.Equal(companyId, AssertScopeValue(entry.Scope, "CompanyId"));
        Assert.Equal("backfill-run-001", AssertScopeValue(entry.Scope, "CorrelationId"));
    }

    [Fact]
    public async Task Backfill_preserves_manually_classified_state_and_counts_conflicts_per_company()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        var logger = new ScopeCapturingLogger<CompanyCashPostingTraceabilityBackfillService>();

        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        var seed = await SeedLegacyScenarioAsync(dbContext, companyId);
        var fiscalPeriodId = await dbContext.FiscalPeriods.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).Select(x => x.Id).SingleAsync();
        var conflictingLedgerEntryId = Guid.NewGuid();

        dbContext.LedgerEntries.Add(new LedgerEntry(
            conflictingLedgerEntryId,
            companyId,
            fiscalPeriodId,
            "BTX-CONFLICT-001",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            LedgerEntryStatuses.Posted,
            "Conflicting legacy bank transaction posting",
            createdUtc: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            updatedUtc: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.BankTransactionCashLedgerLinks.Add(new BankTransactionCashLedgerLink(
            Guid.NewGuid(),
            companyId,
            seed.MatchedBankTransactionId,
            conflictingLedgerEntryId,
            "bank-transaction-ledger-conflict",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.BankTransactionPostingStateRecords.Add(new BankTransactionPostingStateRecord(
            Guid.NewGuid(),
            companyId,
            seed.UnmatchedBankTransactionId,
            BankTransactionMatchingStatuses.ManuallyClassified,
            BankTransactionPostingStates.Pending,
            0,
            new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc),
            createdUtc: new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc),
            updatedUtc: new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        var service = new CompanyCashPostingTraceabilityBackfillService(dbContext, accessor, logger);
        var result = await service.BackfillAsync(
            new BackfillCashPostingTraceabilityCommand(companyId, BatchSize: 10, CorrelationId: "backfill-run-conflict"),
            CancellationToken.None);

        Assert.Equal(1, result.ConflictCount);

        var conflictedState = await dbContext.BankTransactionPostingStateRecords.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId && x.BankTransactionId == seed.MatchedBankTransactionId);
        Assert.Equal(BankTransactionPostingStates.Conflict, conflictedState.PostingState);
        Assert.Equal("conflicting_cash_posting_traceability", conflictedState.ConflictCode);

        var manuallyClassifiedState = await dbContext.BankTransactionPostingStateRecords.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId && x.BankTransactionId == seed.UnmatchedBankTransactionId);
        Assert.Equal(BankTransactionMatchingStatuses.ManuallyClassified, manuallyClassifiedState.MatchingStatus);
        Assert.Equal(BankTransactionPostingStates.Pending, manuallyClassifiedState.PostingState);
        Assert.Null(manuallyClassifiedState.UnmatchedReason);

        var entry = Assert.Single(logger.Entries.Where(x => x.Message.Contains("Completed cash posting traceability backfill", StringComparison.Ordinal) && x.Message.Contains("Conflicts=1", StringComparison.Ordinal)));
        Assert.Equal(companyId, AssertScopeValue(entry.Scope, "CompanyId"));
        Assert.Equal("backfill-run-conflict", AssertScopeValue(entry.Scope, "CorrelationId"));
    }

    private static async Task<LegacyBackfillSeed> SeedLegacyScenarioAsync(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var cashAccountId = Guid.NewGuid();
        var receivablesAccountId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var matchedBankTransactionId = Guid.NewGuid();
        var unmatchedBankTransactionId = Guid.NewGuid();
        var ledgerEntryId = Guid.NewGuid();

        dbContext.Companies.Add(new Company(companyId, "Backfill Company"));
        dbContext.FinanceAccounts.AddRange(
            new FinanceAccount(cashAccountId, companyId, "1000", "Operating Cash", FinanceAccountTypes.Asset, "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FinanceAccount(receivablesAccountId, companyId, "1100", "Accounts Receivable", FinanceAccountTypes.Asset, "USD", 0m, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
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
            "**** 1200",
            "USD",
            "ops",
            true,
            true,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.Payments.Add(new Payment(
            paymentId,
            companyId,
            PaymentTypes.Incoming,
            100m,
            "USD",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            "bank_transfer",
            PaymentStatuses.Completed,
            "INV-2000"));
        dbContext.BankTransactions.AddRange(
            new BankTransaction(
                matchedBankTransactionId,
                companyId,
                bankAccountId,
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                100m,
                "USD",
                "Customer receipt",
                "Northwind",
                reconciledAmount: 100m),
            new BankTransaction(
                unmatchedBankTransactionId,
                companyId,
                bankAccountId,
                new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc),
                55m,
                "USD",
                "Unknown deposit",
                "Unknown"));
        dbContext.BankTransactionPaymentLinks.Add(new BankTransactionPaymentLink(
            Guid.NewGuid(),
            companyId,
            matchedBankTransactionId,
            paymentId,
            100m,
            "USD",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.LedgerEntries.Add(new LedgerEntry(
            ledgerEntryId,
            companyId,
            fiscalPeriodId,
            "BTX-TRACE-001",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            LedgerEntryStatuses.Posted,
            "Legacy bank transaction posting",
            FinanceCashPostingSourceTypes.BankTransaction,
            matchedBankTransactionId.ToString("D"),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.LedgerEntrySourceMappings.Add(new LedgerEntrySourceMapping(
            Guid.NewGuid(),
            companyId,
            ledgerEntryId,
            FinanceCashPostingSourceTypes.BankTransaction,
            matchedBankTransactionId.ToString("D"),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc)));

        await dbContext.SaveChangesAsync();
        await dbContext.BankTransactionPostingStateRecords
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ExecuteDeleteAsync();

        return new LegacyBackfillSeed(companyId, paymentId, matchedBankTransactionId, unmatchedBankTransactionId, ledgerEntryId);
    }

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

    private static object? AssertScopeValue(IReadOnlyDictionary<string, object?> scope, string key)
    {
        Assert.True(scope.ContainsKey(key), $"Missing scope value '{key}'.");
        return scope[key];
    }

    private sealed record LegacyBackfillSeed(
        Guid CompanyId,
        Guid PaymentId,
        Guid MatchedBankTransactionId,
        Guid UnmatchedBankTransactionId,
        Guid LedgerEntryId);

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

    private sealed class ScopeCapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        private readonly List<object> _activeScopes = [];
        public IList<CapturedLogEntry> Entries { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull { _activeScopes.Add(state); return new ScopeHandle(_activeScopes); }
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scope = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var activeScope in _activeScopes.OfType<IEnumerable<KeyValuePair<string, object?>>>())
            {
                foreach (var pair in activeScope)
                {
                    scope[pair.Key] = pair.Value;
                }
            }
            Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), scope));
        }
        internal sealed record CapturedLogEntry(Microsoft.Extensions.Logging.LogLevel LogLevel, string Message, IReadOnlyDictionary<string, object?> Scope);
        private sealed class ScopeHandle(List<object> activeScopes) : IDisposable { public void Dispose() { activeScopes.RemoveAt(activeScopes.Count - 1); } }
    }
}