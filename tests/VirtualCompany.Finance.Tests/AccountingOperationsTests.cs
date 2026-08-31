using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingOperationsTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task New_company_check_is_not_required_and_replays_the_same_run()
    {
        await using var fixture = await OperationsFixture.CreateAsync();
        var command = fixture.StartCommand("migration:no-op");

        var first = await fixture.Migration.StartAsync(command, CancellationToken.None);
        var replay = await fixture.Migration.StartAsync(command, CancellationToken.None);

        Assert.Equal(AccountingMigrationRunStatuses.NotRequired, first.Status);
        Assert.Equal(first.Id, replay.Id);
        Assert.Single(await fixture.Context.AccountingMigrationRuns.ToListAsync());
    }

    [Fact]
    public async Task Unambiguous_history_is_backfilled_once_and_passes_recovery_verification()
    {
        await using var fixture = await OperationsFixture.CreateAsync();
        var journalId = await fixture.AddLegacyJournalAsync();

        var started = await fixture.Migration.StartAsync(fixture.StartCommand("migration:legacy:1"), CancellationToken.None);
        var completed = await fixture.RunToCompletionAsync(started.Id);

        Assert.Equal(AccountingMigrationRunStatuses.Completed, completed.Status);
        Assert.Empty(completed.Conflicts);
        Assert.Equal(12, completed.Reports.Count);

        fixture.Context.ChangeTracker.Clear();
        var journal = await fixture.Context.LedgerEntries.SingleAsync(x => x.Id == journalId);
        Assert.NotNull(journal.VoucherSeriesId);
        Assert.Equal(1, journal.VoucherSequenceNumber);
        Assert.Equal(2026, journal.VoucherFiscalYear);
        Assert.Equal(new DateOnly(2026, 8, 20), journal.PostingDate);
        Assert.Equal("USD", journal.BaseCurrency);
        Assert.Equal(LedgerPostingTypeValues.Bank, journal.PostingType);
        Assert.StartsWith("legacy-", journal.SourceVersion, StringComparison.Ordinal);
        Assert.Equal($"accounting-migration:{fixture.CompanyId:N}:{journalId:N}", journal.IdempotencyKey);
        Assert.Equal(AccountingPolicyPackDefaults.CountryNeutralPackKey, journal.PolicyPackKey);
        Assert.Single(await fixture.Context.LedgerEntrySourceMappings.Where(x => x.LedgerEntryId == journalId).ToListAsync());
        Assert.Single(await fixture.Context.LedgerPostingIdentities.Where(x => x.LedgerEntryId == journalId).ToListAsync());

        var replay = await fixture.Migration.StartAsync(fixture.StartCommand("migration:legacy:1"), CancellationToken.None);
        Assert.Equal(completed.Id, replay.Id);
        Assert.Single(await fixture.Context.AccountingMigrationRuns.ToListAsync());

        var recovery = await fixture.Recovery.VerifyAsync(new VerifyAccountingRecoveryCommand(
            fixture.CompanyId, null, true, fixture.ActorId, "accounting-operations-test"), CancellationToken.None);
        Assert.True(recovery.IsValid, string.Join(Environment.NewLine, recovery.Issues.Select(x => x.Explanation)));
        Assert.True(recovery.ObjectContentVerified);
        Assert.Equal(1, recovery.JournalCount);
        Assert.Equal(2, recovery.LineCount);
        Assert.Equal(1, recovery.EvidenceLinkCount);
        Assert.Equal(recovery.TotalDebit, recovery.TotalCredit);
        Assert.Equal(7, recovery.AdvancedControls.Count);
        Assert.All(recovery.AdvancedControls, control => Assert.False(string.IsNullOrWhiteSpace(control.Checksum)));
        Assert.Equal(AccountingReadinessStatuses.Ready,
            Assert.Single(recovery.AdvancedControls, control => control.Key == "functional_currency").Status);

        var replayedRecovery = await fixture.Recovery.VerifyAsync(new VerifyAccountingRecoveryCommand(
            fixture.CompanyId, null, true, fixture.ActorId, "accounting-operations-test-replay"), CancellationToken.None);
        Assert.Equal(recovery.EvidenceChecksum, replayedRecovery.EvidenceChecksum);
        Assert.Equal(recovery.AdvancedControls.Select(control => control.Checksum),
            replayedRecovery.AdvancedControls.Select(control => control.Checksum));
    }

    [Fact]
    public async Task Ambiguous_account_is_preserved_as_one_operator_visible_conflict()
    {
        await using var fixture = await OperationsFixture.CreateAsync();
        var ambiguousId = Guid.NewGuid();
        fixture.Context.FinanceAccounts.Add(new FinanceAccount(ambiguousId, fixture.CompanyId, "9998",
            "Historical clearing", "legacy_other", "USD", 0m, NowUtc));
        await fixture.Context.SaveChangesAsync();

        var started = await fixture.Migration.StartAsync(fixture.StartCommand("migration:ambiguous"), CancellationToken.None);
        var completed = await fixture.RunToCompletionAsync(started.Id);

        Assert.Equal(AccountingMigrationRunStatuses.CompletedWithConflicts, completed.Status);
        var conflict = Assert.Single(completed.Conflicts);
        Assert.Equal(AccountingMigrationConflictReasonCodes.AmbiguousAccountSemantics, conflict.ReasonCode);
        Assert.Equal(ambiguousId.ToString("D"), conflict.EntityId);

        fixture.Context.ChangeTracker.Clear();
        var account = await fixture.Context.FinanceAccounts.SingleAsync(x => x.Id == ambiguousId);
        Assert.Null(account.AccountClass);
        Assert.Null(account.NormalBalance);
        Assert.False(account.IsPostingEnabled);
        Assert.Single(await fixture.Context.AccountingMigrationConflicts.ToListAsync());
    }

    [Fact]
    public async Task Expired_worker_lease_resumes_the_same_run_without_replaying_completed_work()
    {
        await using var fixture = await OperationsFixture.CreateAsync();
        await fixture.AddLegacyJournalAsync();
        var started = await fixture.Migration.StartAsync(
            fixture.StartCommand("migration:expired-lease:resume"), CancellationToken.None);

        await fixture.Context.AccountingMigrationRuns
            .Where(x => x.Id == started.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, AccountingMigrationRunStatuses.Running)
                .SetProperty(x => x.AttemptCount, 1)
                .SetProperty(x => x.LeaseOwner, "terminated-worker")
                .SetProperty(x => x.LeaseExpiresUtc, NowUtc.AddMinutes(-1)));
        fixture.Context.ChangeTracker.Clear();

        Assert.Equal(1, await fixture.Migration.RunDueAsync(CancellationToken.None));
        var resumed = await fixture.Migration.GetLatestAsync(fixture.CompanyId, CancellationToken.None);

        Assert.NotNull(resumed);
        Assert.Equal(started.Id, resumed.Id);
        Assert.Equal(AccountingMigrationRunStatuses.Queued, resumed.Status);
        Assert.Equal(AccountingMigrationPhases.Accounts, resumed.Phase);
        Assert.Equal(0, resumed.AttemptCount);
    }

    [Fact]
    public async Task Repeated_expired_worker_lease_ends_in_explicit_operator_visible_failure()
    {
        await using var fixture = await OperationsFixture.CreateAsync();
        await fixture.AddLegacyJournalAsync();
        var started = await fixture.Migration.StartAsync(
            fixture.StartCommand("migration:expired-lease:exhausted"), CancellationToken.None);

        await fixture.Context.AccountingMigrationRuns
            .Where(x => x.Id == started.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, AccountingMigrationRunStatuses.Running)
                .SetProperty(x => x.AttemptCount, 3)
                .SetProperty(x => x.LeaseOwner, "terminated-worker")
                .SetProperty(x => x.LeaseExpiresUtc, NowUtc.AddMinutes(-1)));
        fixture.Context.ChangeTracker.Clear();

        Assert.Equal(0, await fixture.Migration.RunDueAsync(CancellationToken.None));
        var failed = await fixture.Migration.GetLatestAsync(fixture.CompanyId, CancellationToken.None);

        Assert.NotNull(failed);
        Assert.Equal(AccountingMigrationRunStatuses.Failed, failed.Status);
        Assert.Equal("accounting_migration_lease_recovery_exhausted", failed.FailureCode);
        Assert.False(string.IsNullOrWhiteSpace(failed.FailureSummary));
    }

    [Fact]
    public async Task Transient_persistence_failure_requeues_the_same_run_without_replaying_the_completed_batch()
    {
        await using var fixture = await OperationsFixture.CreateAsync();
        await fixture.AddLegacyJournalAsync();
        var started = await fixture.Migration.StartAsync(
            fixture.StartCommand("migration:transient-persistence"), CancellationToken.None);

        fixture.DatabaseFailure.Arm();

        Assert.Equal(1, await fixture.Migration.RunDueAsync(CancellationToken.None));
        var retry = await fixture.Migration.GetLatestAsync(fixture.CompanyId, CancellationToken.None);

        Assert.NotNull(retry);
        Assert.Equal(started.Id, retry.Id);
        Assert.Equal(AccountingMigrationRunStatuses.Queued, retry.Status);
        Assert.Equal(AccountingMigrationPhases.Accounts, retry.Phase);
        Assert.Equal(1, retry.AttemptCount);
        Assert.Equal("accounting_migration_batch_failed", retry.FailureCode);

        Assert.Equal(1, await fixture.Migration.RunDueAsync(CancellationToken.None));
        var resumed = await fixture.Migration.GetLatestAsync(fixture.CompanyId, CancellationToken.None);

        Assert.NotNull(resumed);
        Assert.Equal(started.Id, resumed.Id);
        Assert.Equal(AccountingMigrationRunStatuses.Queued, resumed.Status);
        Assert.Equal(AccountingMigrationPhases.Journals, resumed.Phase);
        Assert.Equal(0, resumed.AttemptCount);
        Assert.Null(resumed.FailureCode);
    }

    private sealed class OperationsFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly InMemoryDocumentStorage _documentStorage;

        private OperationsFixture(
            SqliteConnection connection,
            VirtualCompanyDbContext context,
            AccountingHistoricalMigrationService migration,
            AccountingRecoveryVerificationService recovery,
            InMemoryDocumentStorage documentStorage,
            FailOnceSaveChangesInterceptor databaseFailure,
            Guid companyId,
            Guid actorId)
        {
            _connection = connection;
            Context = context;
            Migration = migration;
            Recovery = recovery;
            _documentStorage = documentStorage;
            DatabaseFailure = databaseFailure;
            CompanyId = companyId;
            ActorId = actorId;
        }

        public VirtualCompanyDbContext Context { get; }
        public AccountingHistoricalMigrationService Migration { get; }
        public AccountingRecoveryVerificationService Recovery { get; }
        public FailOnceSaveChangesInterceptor DatabaseFailure { get; }
        public Guid CompanyId { get; }
        public Guid ActorId { get; }

        public StartAccountingMigrationCommand StartCommand(string key) =>
            new(CompanyId, key, ActorId, "accounting-operations-test");

        public async Task<AccountingMigrationRunDto> RunToCompletionAsync(Guid runId)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                await Migration.RunDueAsync(CancellationToken.None);
                var current = await Migration.GetLatestAsync(CompanyId, CancellationToken.None)
                    ?? throw new InvalidOperationException("Migration disappeared during the test.");
                Assert.Equal(runId, current.Id);
                if (AccountingMigrationRunStatuses.IsTerminal(current.Status)) return current;
            }

            throw new TimeoutException("Accounting migration did not reach a terminal state.");
        }

        public async Task<Guid> AddLegacyJournalAsync()
        {
            var period = await Context.FiscalPeriods.SingleAsync(x => x.StartUtc == new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
            var series = await Context.VoucherSeries.OrderBy(x => x.Code).FirstAsync();
            var debitAccount = await Context.FinanceAccounts.FirstAsync(x => x.AccountClass == FinanceAccountClassValues.Asset);
            var creditAccount = await Context.FinanceAccounts.FirstAsync(x => x.AccountClass == FinanceAccountClassValues.Equity);
            var journalId = Guid.NewGuid();
            Context.LedgerEntries.Add(new LedgerEntry(journalId, CompanyId, period.Id,
                $"{series.NumberPrefix}-2026-000001", NowUtc, LedgerEntryStatuses.Posted,
                "Historical bank receipt", "bank_transaction", "bank-legacy-1"));
            Context.LedgerEntryLines.AddRange(
                new LedgerEntryLine(Guid.NewGuid(), CompanyId, journalId, debitAccount.Id, 125m, 0m, "USD", "Receipt", NowUtc),
                new LedgerEntryLine(Guid.NewGuid(), CompanyId, journalId, creditAccount.Id, 0m, 125m, "USD", "Receipt", NowUtc));
            var evidence = Encoding.UTF8.GetBytes("Verified historical bank receipt evidence.");
            var evidenceHash = Convert.ToHexString(SHA256.HashData(evidence)).ToLowerInvariant();
            var documentId = Guid.NewGuid();
            const string storageKey = "accounting-operations/historical-bank-receipt.txt";
            _documentStorage.Put(storageKey, evidence);
            Context.CompanyKnowledgeDocuments.Add(new CompanyKnowledgeDocument(documentId, CompanyId,
                "Historical bank receipt", CompanyKnowledgeDocumentType.Reference, storageKey, null,
                "historical-bank-receipt.txt", "text/plain", ".txt", evidence.Length,
                accessScope: new CompanyKnowledgeDocumentAccessScope(CompanyId,
                    CompanyKnowledgeDocumentAccessScope.CompanyVisibility)));
            Context.LedgerEntryEvidenceLinks.Add(new LedgerEntryEvidenceLink(Guid.NewGuid(), CompanyId,
                journalId, documentId, evidenceHash, "Historical bank receipt", NowUtc));
            await Context.SaveChangesAsync();
            return journalId;
        }

        public static async Task<OperationsFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var companyId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var accessor = new TestCompanyContextAccessor(companyId, actorId);
            var databaseFailure = new FailOnceSaveChangesInterceptor();
            var context = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                    .UseSqlite(connection)
                    .AddInterceptors(databaseFailure)
                    .Options,
                accessor);
            await context.Database.EnsureCreatedAsync();
            context.Companies.Add(new Company(companyId, "Accounting operations company"));
            await context.SaveChangesAsync();

            var pack = new CountryNeutralAccountingPolicyPack();
            var resolver = new AccountingPolicyPackResolver([pack]);
            var clock = new FixedTimeProvider(new DateTimeOffset(NowUtc));
            var auditWriter = new AuditEventWriter(context);
            var configurationService = new AccountingConfigurationService(context, resolver, auditWriter, clock);
            var chartCatalogResolver = new AccountingChartCatalogResolver([new Bas2026AccountingChartCatalog()]);
            var administration = new AccountingAdministrationService(context, resolver, chartCatalogResolver, configurationService, auditWriter, clock);
            await administration.CompleteSetupAsync(new CompleteAccountingSetupCommand(
                companyId, "USD", new DateOnly(2026, 1, 1),
                AccountingPolicyPackDefaults.CountryNeutralPackKey,
                AccountingPolicyPackDefaults.CountryNeutralVersion,
                "generic-accrual", null, actorId, $"accounting-setup:{companyId:N}",
                "accounting-operations-test"), CancellationToken.None);

            var telemetry = new AccountingOperationsTelemetry(NullLogger<AccountingOperationsTelemetry>.Instance);
            var migration = new AccountingHistoricalMigrationService(context, auditWriter, telemetry, clock,
                Options.Create(new AccountingMigrationWorkerOptions
                {
                    Enabled = true,
                    BatchSize = 50,
                    ClaimBatchSize = 1,
                    LeaseSeconds = 30,
                    MaximumAttempts = 3
                }), NullLogger<AccountingHistoricalMigrationService>.Instance);
            var documentStorage = new InMemoryDocumentStorage();
            var recovery = new AccountingRecoveryVerificationService(context, documentStorage,
                auditWriter, telemetry, clock);
            return new OperationsFixture(connection, context, migration, recovery, documentStorage,
                databaseFailure, companyId, actorId);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FailOnceSaveChangesInterceptor : SaveChangesInterceptor
    {
        private int _remainingFailures;

        public void Arm() => Interlocked.Exchange(ref _remainingFailures, 1);

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            ThrowIfArmed();
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ThrowIfArmed();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void ThrowIfArmed()
        {
            if (Interlocked.Exchange(ref _remainingFailures, 0) == 1)
            {
                throw new TimeoutException("Injected one-shot persistence timeout.");
            }
        }
    }

    private sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
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

    private sealed class InMemoryDocumentStorage : ICompanyDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public void Put(string storageKey, byte[] content) => _files[storageKey] = content.ToArray();

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_files.TryGetValue(storageKey, out var content)) throw new FileNotFoundException(storageKey);
            return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
        }

        public async Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken cancellationToken)
        {
            await using var buffer = new MemoryStream();
            await request.Content.CopyToAsync(buffer, cancellationToken);
            _files[request.StorageKey] = buffer.ToArray();
            return new DocumentStorageWriteResult(request.StorageKey, null);
        }

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _files.Remove(storageKey);
            return Task.CompletedTask;
        }
    }
}
