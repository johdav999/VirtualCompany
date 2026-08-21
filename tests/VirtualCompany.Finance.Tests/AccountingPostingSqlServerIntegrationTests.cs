using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingPostingSqlServerIntegrationTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly PostingDate = new(2026, 8, 19);

    [SqlServerFact]
    public async Task Sql_server_enforces_atomic_sequences_idempotency_rollback_concurrency_tenant_keys_and_immutability()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable)!;
        var builder = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"virtualcompany_ledger_{Guid.NewGuid():N}",
            MultipleActiveResultSets = false
        };
        var connectionString = builder.ConnectionString;
        var unscopedAccessor = new TestCompanyContextAccessor(null, null);

        await using (var setup = CreateContext(connectionString, unscopedAccessor))
        {
            await setup.Database.MigrateAsync();
        }

        try
        {
            var seed = await SeedAsync(connectionString, unscopedAccessor);

            var firstPair = await Task.WhenAll(
                PostAsync(connectionString, seed, "source-a", "1", "post:source-a:1"),
                PostAsync(connectionString, seed, "source-b", "1", "post:source-b:1"));
            Assert.Equal(new long?[] { 1, 2 }, firstPair.Select(item => item.Journal.VoucherSequenceNumber).Order().ToArray());
            Assert.Equal(2, firstPair.Select(item => item.Journal.EntryNumber).Distinct().Count());

            var concurrentReplay = await Task.WhenAll(
                PostAsync(connectionString, seed, "same-source", "7", "post:same-source:7"),
                PostAsync(connectionString, seed, "same-source", "7", "post:same-source:7"));
            Assert.Single(concurrentReplay.Select(item => item.Journal.Id).Distinct());

            await using (var verification = CreateContext(connectionString, new TestCompanyContextAccessor(seed.CompanyId, seed.ActorId)))
            {
                Assert.Equal(3, await verification.LedgerEntries.CountAsync());
                Assert.Equal(3, await verification.LedgerPostingIdentities.CountAsync());
                Assert.Equal(3, await verification.AuditEvents.CountAsync());
                Assert.Equal(3, (await verification.VoucherSequences.SingleAsync()).LastAllocatedNumber);

                var service = CreateService(verification);
                var rejected = CreateEntry(seed, "unbalanced", "1", "post:unbalanced:1") with
                {
                    Lines =
                    [
                        new(seed.DebitAccountId, 100m, 0m, "USD"),
                        new(seed.CreditAccountId, 0m, 99m, "USD")
                    ]
                };
                await Assert.ThrowsAsync<AccountingPostingException>(() =>
                    service.PostAsync(new PostAccountingEntryCommand(rejected), CancellationToken.None));
                Assert.Equal(3, (await verification.VoucherSequences.SingleAsync()).LastAllocatedNumber);
                Assert.Equal(3, await verification.LedgerEntries.CountAsync());

                var journal = await verification.LedgerEntries.SingleAsync(entry => entry.Id == firstPair[0].Journal.Id);
                verification.Entry(journal).Property(nameof(LedgerEntry.Description)).CurrentValue = "Forbidden edit";
                await Assert.ThrowsAsync<InvalidOperationException>(() => verification.SaveChangesAsync());
                verification.ChangeTracker.Clear();
            }

            await AssertStaleSequenceVersionRejectedAsync(connectionString, seed);
            await AssertCompositeTenantForeignKeyRejectedAsync(connectionString, seed);
            await AssertBankImportIdentityConstraintsAsync(connectionString, seed);
        }
        finally
        {
            await using var cleanup = CreateContext(connectionString, unscopedAccessor);
            await cleanup.Database.EnsureDeletedAsync();
        }
    }

    private static async Task AssertStaleSequenceVersionRejectedAsync(string connectionString, Seed seed)
    {
        await using var first = CreateContext(connectionString, new TestCompanyContextAccessor(seed.CompanyId, seed.ActorId));
        await using var second = CreateContext(connectionString, new TestCompanyContextAccessor(seed.CompanyId, seed.ActorId));
        var firstSequence = await first.VoucherSequences.SingleAsync();
        var staleSequence = await second.VoucherSequences.SingleAsync();

        firstSequence.Allocate(NowUtc.AddMinutes(1));
        await first.SaveChangesAsync();
        staleSequence.Allocate(NowUtc.AddMinutes(2));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private static async Task AssertCompositeTenantForeignKeyRejectedAsync(string connectionString, Seed seed)
    {
        await using var context = CreateContext(connectionString, new TestCompanyContextAccessor(seed.CompanyId, seed.ActorId));
        context.LedgerEntryLines.Add(new LedgerEntryLine(
            Guid.NewGuid(), seed.CompanyId, seed.ForeignJournalId, seed.DebitAccountId, 1m, 0m, "USD", createdUtc: NowUtc));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        var bankTransactionId = Guid.NewGuid();
        context.BankTransactions.Add(new BankTransaction(bankTransactionId, seed.CompanyId, seed.BankAccountId,
            NowUtc, NowUtc, 25m, "USD", "Tenant-key test", "Foreign payment", importSource: "sql-test",
            createdUtc: NowUtc, updatedUtc: NowUtc, rowIdentity: $"tenant-key:{bankTransactionId:N}"));
        await context.SaveChangesAsync();

        context.BankTransactionPaymentLinks.Add(new BankTransactionPaymentLink(Guid.NewGuid(), seed.CompanyId,
            bankTransactionId, seed.ForeignPaymentId, 25m, "USD", NowUtc));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        context.BankTransactionCashLedgerLinks.Add(new BankTransactionCashLedgerLink(Guid.NewGuid(), seed.CompanyId,
            bankTransactionId, seed.ForeignJournalId, $"tenant-key:{bankTransactionId:N}:{seed.ForeignJournalId:N}", NowUtc));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        context.PaymentCashLedgerLinks.Add(new PaymentCashLedgerLink(Guid.NewGuid(), seed.CompanyId,
            seed.PaymentId, seed.ForeignJournalId, "sql_test", "tenant-key", NowUtc, NowUtc));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static async Task AssertBankImportIdentityConstraintsAsync(string connectionString, Seed seed)
    {
        var importId = Guid.NewGuid();
        await using (var first = CreateContext(connectionString, new TestCompanyContextAccessor(seed.CompanyId, seed.ActorId)))
        {
            first.BankStatementImports.Add(new BankStatementImport(importId, seed.CompanyId, seed.BankAccountId,
                "csv", "statement-identity", new string('a', 64), seed.ActorId, NowUtc));
            await first.SaveChangesAsync();
        }

        await using (var duplicateStatement = CreateContext(connectionString, new TestCompanyContextAccessor(seed.CompanyId, seed.ActorId)))
        {
            duplicateStatement.BankStatementImports.Add(new BankStatementImport(Guid.NewGuid(), seed.CompanyId, seed.BankAccountId,
                "csv", "statement-identity", new string('a', 64), seed.ActorId, NowUtc));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateStatement.SaveChangesAsync());
        }

        var transactionId = Guid.NewGuid();
        await using (var firstRow = CreateContext(connectionString, new TestCompanyContextAccessor(seed.CompanyId, seed.ActorId)))
        {
            firstRow.BankTransactions.Add(new BankTransaction(transactionId, seed.CompanyId, seed.BankAccountId,
                NowUtc, NowUtc, 25m, "USD", "Imported receipt", "Customer", importSource: "csv",
                createdUtc: NowUtc, updatedUtc: NowUtc, rowIdentity: "stable-row", rowContentHash: new string('b', 64)));
            firstRow.BankTransactionPostingStateRecords.Add(new BankTransactionPostingStateRecord(Guid.NewGuid(),
                seed.CompanyId, transactionId, BankTransactionMatchingStatuses.Unmatched, BankTransactionPostingStates.Corrected,
                0, NowUtc, sourceVersion: 1));
            await firstRow.SaveChangesAsync();
        }

        await using (var duplicateRow = CreateContext(connectionString, new TestCompanyContextAccessor(seed.CompanyId, seed.ActorId)))
        {
            duplicateRow.BankTransactions.Add(new BankTransaction(Guid.NewGuid(), seed.CompanyId, seed.BankAccountId,
                NowUtc, NowUtc, 25m, "USD", "Imported receipt", "Customer", importSource: "csv",
                createdUtc: NowUtc, updatedUtc: NowUtc, rowIdentity: "stable-row", rowContentHash: new string('b', 64)));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateRow.SaveChangesAsync());
        }
    }

    private static async Task<PostedAccountingJournal> PostAsync(
        string connectionString,
        Seed seed,
        string sourceId,
        string sourceVersion,
        string idempotencyKey)
    {
        await using var context = CreateContext(connectionString, new TestCompanyContextAccessor(seed.CompanyId, seed.ActorId));
        return await CreateService(context).PostAsync(
            new PostAccountingEntryCommand(CreateEntry(seed, sourceId, sourceVersion, idempotencyKey)),
            CancellationToken.None);
    }

    private static AccountingPostingService CreateService(VirtualCompanyDbContext context) =>
        new(context, new AccountingJournalReadService(context), new AuditEventWriter(context), new FixedTimeProvider(new DateTimeOffset(NowUtc)));

    private static ProposedAccountingEntry CreateEntry(Seed seed, string sourceId, string sourceVersion, string idempotencyKey) =>
        new(
            seed.CompanyId,
            seed.FiscalPeriodId,
            "G",
            PostingDate,
            PostingDate,
            LedgerPostingTypeValues.SourceDocument,
            $"Post {sourceId}",
            "sql_server_test",
            sourceId,
            sourceVersion,
            idempotencyKey,
            [
                new(seed.DebitAccountId, 100m, 0m, "USD"),
                new(seed.CreditAccountId, 0m, 100m, "USD")
            ],
            seed.ActorId,
            PolicyFacts: new Dictionary<string, string> { ["taxTreatment"] = "none" });

    private static async Task<Seed> SeedAsync(string connectionString, TestCompanyContextAccessor accessor)
    {
        var companyId = Guid.NewGuid();
        var foreignCompanyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var fiscalPeriodId = Guid.NewGuid();
        var foreignPeriodId = Guid.NewGuid();
        var debitAccountId = Guid.NewGuid();
        var creditAccountId = Guid.NewGuid();
        var bankAccountId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var foreignPaymentId = Guid.NewGuid();
        var foreignJournalId = Guid.NewGuid();

        await using var context = CreateContext(connectionString, accessor);
        context.Companies.AddRange(new Company(companyId, "SQL ledger company"), new Company(foreignCompanyId, "Foreign SQL ledger company"));
        context.Users.Add(new User(actorId, "sql-ledger@example.com", "SQL Ledger Owner", "test", actorId.ToString("N")));
        context.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, actorId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
        context.FinanceAccounts.AddRange(
            CreateAccount(debitAccountId, companyId, "1000", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit),
            CreateAccount(creditAccountId, companyId, "3000", FinanceAccountClassValues.Equity, FinanceNormalBalanceValues.Credit));
        context.CompanyBankAccounts.Add(new CompanyBankAccount(bankAccountId, companyId, debitAccountId,
            "Operating account", "SQL Test Bank", "****1000", "USD", isPrimary: true, createdUtc: NowUtc, updatedUtc: NowUtc));
        context.Payments.AddRange(
            new Payment(paymentId, companyId, PaymentTypes.Incoming, 25m, "USD", NowUtc, "ach", PaymentStatuses.Completed, "SQL-PAYMENT-001"),
            new Payment(foreignPaymentId, foreignCompanyId, PaymentTypes.Incoming, 25m, "USD", NowUtc, "ach", PaymentStatuses.Completed, "SQL-FOREIGN-PAYMENT-001"));
        context.FiscalPeriods.AddRange(
            new FiscalPeriod(fiscalPeriodId, companyId, "August 2026", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FiscalPeriod(foreignPeriodId, foreignCompanyId, "August 2026", new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
        context.LedgerEntries.Add(new LedgerEntry(
            foreignJournalId, foreignCompanyId, foreignPeriodId, "FOREIGN-1", NowUtc, LedgerEntryStatuses.Draft, "Foreign journal"));
        context.VoucherSeries.Add(new VoucherSeries(Guid.NewGuid(), companyId, "G", "General journal", "G", true, NowUtc));
        var configuration = new AccountingConfiguration(
            Guid.NewGuid(), companyId, "USD", 1, 1,
            AccountingPolicyPackDefaults.CountryNeutralPackKey, AccountingPolicyPackDefaults.CountryNeutralVersion,
            new DateOnly(2026, 1, 1), 2, AccountingRoundingModeValues.MidpointToEven, actorId, NowUtc);
        configuration.SetSetupState(AccountingSetupStateValues.Ready, actorId, NowUtc);
        context.AccountingConfigurations.Add(configuration);
        await context.SaveChangesAsync();

        return new Seed(companyId, actorId, fiscalPeriodId, debitAccountId, creditAccountId, bankAccountId,
            paymentId, foreignPaymentId, foreignJournalId);
    }

    private static FinanceAccount CreateAccount(Guid id, Guid companyId, string code, string accountClass, string normalBalance) =>
        new(id, companyId, code, $"Account {code}", accountClass, "USD", 0m, NowUtc,
            accountClass: accountClass, normalBalance: normalBalance, effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true);

    private static VirtualCompanyDbContext CreateContext(string connectionString, ICompanyContextAccessor accessor)
    {
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(VirtualCompanyDbContextFactory).Assembly.GetName().Name))
            .Options;
        return new VirtualCompanyDbContext(options, accessor);
    }

    private sealed record Seed(
        Guid CompanyId,
        Guid ActorId,
        Guid FiscalPeriodId,
        Guid DebitAccountId,
        Guid CreditAccountId,
        Guid BankAccountId,
        Guid PaymentId,
        Guid ForeignPaymentId,
        Guid ForeignJournalId);

    private sealed class TestCompanyContextAccessor(Guid? companyId, Guid? userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => userId;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? resolvedCompanyId) => CompanyId = resolvedCompanyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
        {
            Skip = $"Set {ConnectionVariable} to run Docker/local SQL Server ledger integration tests.";
        }
    }
}
