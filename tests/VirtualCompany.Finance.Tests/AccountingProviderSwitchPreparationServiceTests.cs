using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchPreparationServiceTests
{
    [Fact]
    public async Task Approved_external_to_internal_plan_prepares_idempotent_non_authoritative_candidates()
    {
        await using var fixture = await Fixture.CreateAsync();

        var started = await fixture.Service.StartAsync(new(fixture.CompanyId, fixture.SwitchId,
            fixture.PlanId, fixture.SwitchVersion, fixture.OwnerId, "prepare-1", "prepare-1"),
            CancellationToken.None);
        var replayedStart = await fixture.Service.StartAsync(new(fixture.CompanyId, fixture.SwitchId,
            fixture.PlanId, fixture.SwitchVersion, fixture.OwnerId, "prepare-1", "prepare-1"),
            CancellationToken.None);

        Assert.Equal(started.Id, replayedStart.Id);
        Assert.Equal(1, await fixture.Service.RunDueAsync(CancellationToken.None));
        Assert.Equal(0, await fixture.Service.RunDueAsync(CancellationToken.None));

        var completed = await fixture.Service.GetAsync(new(fixture.CompanyId, fixture.SwitchId, started.Id),
            CancellationToken.None);
        Assert.Equal(AccountingProviderSwitchPreparationStatuses.Completed, completed.Status);
        Assert.True(completed.IsActivationReady);
        Assert.Equal(1, completed.CandidateCount);
        Assert.Equal(1, completed.ValidCandidateCount);
        Assert.Equal(AccountingProviderSwitchNativeCandidateKinds.Customer,
            Assert.Single(completed.Candidates).CandidateKind);
        Assert.Single(await fixture.Context.FinanceExternalReferences.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await fixture.Context.LedgerEntries.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(AccountingAuthorityValues.ExternalProvider,
            (await fixture.Context.AccountingAuthorityPeriods.IgnoreQueryFilters().SingleAsync()).Authority);
    }

    [Fact]
    public async Task Cross_company_switch_is_not_visible_to_preparation_service()
    {
        await using var fixture = await Fixture.CreateAsync();
        var error = await Assert.ThrowsAsync<AccountingAuthorityException>(() => fixture.Service.GetAsync(
            new(Guid.NewGuid(), fixture.SwitchId), CancellationToken.None));
        Assert.Equal(AccountingProviderSwitchReasonCodes.NotFound, error.ReasonCode);
        Assert.Empty(await fixture.Context.AccountingProviderSwitchNativeCandidates.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Opening_strategy_prepares_opening_and_open_item_candidates_and_records_history_archive()
    {
        var posting = new RecordingPostingService();
        await using var fixture = await Fixture.CreateAsync(seedCustomer: false, postingService: posting);
        var debitAccountId = Guid.NewGuid();
        var creditAccountId = Guid.NewGuid();
        await fixture.AddStagedRecordAsync(AccountingProviderSwitchStagingDatasets.OpeningBalanceCandidates,
            "opening-2026-09", $$"""
            {"voucherSeriesCode":"A","documentDate":"2026-09-01","lines":[
              {"financeAccountId":"{{debitAccountId:D}}","debitAmount":1250,"creditAmount":0,"currency":"SEK"},
              {"financeAccountId":"{{creditAccountId:D}}","debitAmount":0,"creditAmount":1250,"currency":"SEK"}
            ]}
            """, 1250m, "SEK");
        await fixture.AddStagedRecordAsync(AccountingProviderSwitchStagingDatasets.OpenItems,
            "invoice-100", """
            {"documentType":"customer_invoice","documentNumber":"100","issueDate":"2026-08-15","currency":"SEK"}
            """, 500m, "SEK");
        await fixture.AddStagedRecordAsync(AccountingProviderSwitchStagingDatasets.Journals,
            "historical-voucher-1", """
            {"voucherSeriesCode":"A","documentDate":"2026-08-01","postingDate":"2026-08-01","lines":[]}
            """, 0m, "SEK");

        var completed = await fixture.StartAndRunAsync("opening-open-items");

        Assert.True(completed.IsActivationReady);
        Assert.Equal(2, completed.CandidateCount);
        Assert.Contains(completed.Candidates,
            x => x.CandidateKind == AccountingProviderSwitchNativeCandidateKinds.OpeningJournal);
        Assert.Contains(completed.Candidates,
            x => x.CandidateKind == AccountingProviderSwitchNativeCandidateKinds.CustomerInvoice);
        var archive = Assert.Single(completed.ArchiveDependencies);
        Assert.Equal("strategy_source_archive_dependency", archive.ReasonCode);
        var preview = Assert.Single(posting.CandidatePreviews);
        Assert.Equal(new DateOnly(2026, 9, 1), preview.Entry.PostingDate);
        Assert.Empty(await fixture.Context.LedgerEntries.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Current_year_history_preserves_source_dates_currency_references_and_period()
    {
        var posting = new RecordingPostingService();
        await using var fixture = await Fixture.CreateAsync(
            AccountingProviderSwitchStrategies.CurrentFiscalYear, seedCustomer: false, postingService: posting);
        var augustPeriodId = Guid.NewGuid();
        fixture.Context.FiscalPeriods.Add(new FiscalPeriod(augustPeriodId, fixture.CompanyId, "August 2026",
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
        await fixture.Context.SaveChangesAsync();
        var debitAccountId = Guid.NewGuid();
        var creditAccountId = Guid.NewGuid();
        await fixture.AddStagedRecordAsync(AccountingProviderSwitchStagingDatasets.Journals,
            "fortnox-A-42", $$"""
            {"voucherSeriesCode":"A","documentDate":"2026-08-14","postingDate":"2026-08-15",
             "postingType":"source_document","sourceVoucherReference":"A-42","correctionReference":"A-41",
             "currency":"EUR","taxEvidence":{"source":"fortnox"},"lines":[
              {"financeAccountId":"{{debitAccountId:D}}","debitAmount":80,"creditAmount":0,"currency":"EUR"},
              {"financeAccountId":"{{creditAccountId:D}}","debitAmount":0,"creditAmount":80,"currency":"EUR"}
             ]}
            """, 80m, "EUR");

        var completed = await fixture.StartAndRunAsync("current-year-history");

        var candidate = Assert.Single(completed.Candidates);
        Assert.Equal(AccountingProviderSwitchNativeCandidateKinds.HistoricalJournal, candidate.CandidateKind);
        Assert.Equal(augustPeriodId, candidate.FiscalPeriodId);
        Assert.Equal(new DateOnly(2026, 8, 14), candidate.DocumentDate);
        Assert.Equal(new DateOnly(2026, 8, 15), candidate.PostingDate);
        Assert.Equal("EUR", candidate.Currency);
        Assert.Contains("\"sourceVoucherReference\":\"A-42\"", candidate.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("\"correctionReference\":\"A-41\"", candidate.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("\"taxEvidence\"", candidate.PayloadJson, StringComparison.Ordinal);
        Assert.Equal("EUR", Assert.Single(posting.CandidatePreviews).Entry.Lines[0].Currency);
    }

    [Fact]
    public async Task Existing_provider_reference_prevents_duplicate_candidate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var connection = await fixture.Context.FinanceIntegrationConnections.IgnoreQueryFilters().SingleAsync();
        fixture.Context.FinanceExternalReferences.Add(new FinanceExternalReference(Guid.NewGuid(), fixture.CompanyId,
            connection.Id, "fortnox", "customer", Guid.NewGuid(), "customer-100", "100", null,
            new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc)));
        await fixture.Context.SaveChangesAsync();

        var completed = await fixture.StartAndRunAsync("existing-reference");

        Assert.Equal(0, completed.CandidateCount);
        Assert.Equal(1, completed.ExistingReferenceCount);
        Assert.Empty(completed.Candidates);
        Assert.Single(await fixture.Context.FinanceExternalReferences.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Retryable_worker_failure_resumes_without_duplicate_candidates()
    {
        var posting = new RecordingPostingService(failFirstCandidatePreview: true);
        await using var fixture = await Fixture.CreateAsync(seedCustomer: false, postingService: posting);
        var debitAccountId = Guid.NewGuid();
        var creditAccountId = Guid.NewGuid();
        await fixture.AddStagedRecordAsync(AccountingProviderSwitchStagingDatasets.OpeningBalanceCandidates,
            "opening-retry", $$"""
            {"voucherSeriesCode":"A","documentDate":"2026-09-01","lines":[
              {"financeAccountId":"{{debitAccountId:D}}","debitAmount":10,"creditAmount":0,"currency":"SEK"},
              {"financeAccountId":"{{creditAccountId:D}}","debitAmount":0,"creditAmount":10,"currency":"SEK"}
            ]}
            """, 10m, "SEK");
        var started = await fixture.StartAsync("retryable-worker");

        Assert.Equal(0, await fixture.Service.RunDueAsync(CancellationToken.None));
        var retry = await fixture.Service.GetAsync(new(fixture.CompanyId, fixture.SwitchId, started.Id),
            CancellationToken.None);
        Assert.Equal(AccountingProviderSwitchPreparationStatuses.Queued, retry.Status);
        Assert.Equal(1, retry.AttemptCount);
        fixture.Clock.Advance(TimeSpan.FromSeconds(11));

        Assert.Equal(1, await fixture.Service.RunDueAsync(CancellationToken.None));
        var completed = await fixture.Service.GetAsync(new(fixture.CompanyId, fixture.SwitchId, started.Id),
            CancellationToken.None);
        Assert.Equal(AccountingProviderSwitchPreparationStatuses.Completed, completed.Status);
        Assert.Equal(2, completed.AttemptCount);
        Assert.Single(completed.Candidates);
        Assert.Single(await fixture.Context.FinanceExternalReferences.IgnoreQueryFilters().ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly DateTime Now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        private readonly SqliteConnection _connection;

        private Fixture(SqliteConnection connection, VirtualCompanyDbContext context,
            AccountingProviderSwitchPreparationService service, Guid companyId, Guid ownerId,
            Guid switchId, Guid planId, long switchVersion, FixedTimeProvider clock)
        {
            _connection = connection;
            Context = context;
            Service = service;
            CompanyId = companyId;
            OwnerId = ownerId;
            SwitchId = switchId;
            PlanId = planId;
            SwitchVersion = switchVersion;
            Clock = clock;
        }

        public VirtualCompanyDbContext Context { get; }
        public AccountingProviderSwitchPreparationService Service { get; }
        public Guid CompanyId { get; }
        public Guid OwnerId { get; }
        public Guid SwitchId { get; }
        public Guid PlanId { get; }
        public long SwitchVersion { get; }
        public FixedTimeProvider Clock { get; }

        public static async Task<Fixture> CreateAsync(
            string strategy = AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems,
            bool seedCustomer = true,
            IAccountingPostingService? postingService = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();

            var companyId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var switchId = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            var assessmentId = Guid.NewGuid();
            var rehearsalId = Guid.NewGuid();
            var planHash = new string('a', 64);
            db.Companies.Add(new Company(companyId, "Inbound preparation company"));
            db.Users.Add(new User(ownerId, "owner@example.com", "Owner", "test", ownerId.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, ownerId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.FiscalPeriods.Add(new FiscalPeriod(periodId, companyId, "September 2026",
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
            db.AccountingAuthorityPeriods.Add(new AccountingAuthorityPeriod(Guid.NewGuid(), companyId,
                new DateOnly(2026, 1, 1), null, AccountingAuthorityValues.ExternalProvider, "fortnox",
                ownerId, "Fortnox remains authoritative during preparation.", Now));
            var providerSwitch = new AccountingProviderSwitch(switchId, companyId,
                new("external", "fortnox"), new("internal", null), periodId,
                strategy,
                "Move accounting into Virtual Company.", ownerId, null, ownerId, "switch", Now);
            providerSwitch.TransitionTo(AccountingProviderSwitchStatuses.Assessing, ownerId, "assess", Now);
            providerSwitch.TransitionTo(AccountingProviderSwitchStatuses.ReadyForPlanning, ownerId, "ready", Now);
            db.AccountingProviderSwitches.Add(providerSwitch);
            var assessment = new AccountingProviderSwitchAssessment(assessmentId, companyId, switchId, ownerId,
                "assessment", "assessment", 1, Now);
            assessment.Complete(Now);
            db.AccountingProviderSwitchAssessments.Add(assessment);
            var rehearsal = new AccountingProviderSwitchRehearsal(rehearsalId, companyId, switchId, ownerId,
                "rehearsal", "rehearsal", Now);
            db.AccountingProviderSwitchRehearsals.Add(rehearsal);
            var plan = new AccountingProviderSwitchCutoverPlan(companyId, switchId, rehearsalId, 1,
                planHash, new string('b', 64), providerSwitch.MigrationStrategy, Now.AddHours(1),
                Now.AddHours(2), "Source remains authoritative until activation.",
                $"[\"{ownerId:D}\"]", "{}", ownerId, Now);
            db.AccountingProviderSwitchCutoverPlans.Add(plan);
            db.FinanceIntegrationConnections.Add(new FinanceIntegrationConnection(Guid.NewGuid(), companyId,
                "fortnox", FinanceIntegrationConnectionStatuses.Connected, ownerId, Now));
            if (seedCustomer)
                db.AccountingProviderSwitchStagedRecords.Add(new AccountingProviderSwitchStagedRecord(Guid.NewGuid(),
                    companyId, switchId, assessmentId, providerSwitch.Source,
                    AccountingProviderSwitchStagingDatasets.Counterparties, "customer-100", "v1", Now,
                    new string('c', 64), new string('d', 64),
                    "{\"counterpartyType\":\"customer\",\"name\":\"Customer 100\"}",
                    "{\"source\":\"fortnox-customer\"}", 0m, "SEK",
                    AccountingProviderSwitchDispositions.Ready, Now));
            await db.SaveChangesAsync();

            var readiness = new ReadyPolicy(companyId, switchId, plan.Id, planHash);
            var clock = new FixedTimeProvider(Now);
            var service = new AccountingProviderSwitchPreparationService(db, readiness,
                postingService ?? new RecordingPostingService(), new AuditEventWriter(db), clock,
                Options.Create(new AccountingProviderSwitchPreparationWorkerOptions
                    { ClaimBatchSize = 4, LeaseSeconds = 60, MaximumAttempts = 2, SaveBatchSize = 10 }));
            return new(connection, db, service, companyId, ownerId, switchId, plan.Id, providerSwitch.Version, clock);
        }

        public Task<AccountingProviderSwitchPreparationDto> StartAsync(string key) => Service.StartAsync(new(
            CompanyId, SwitchId, PlanId, SwitchVersion, OwnerId, key, key), CancellationToken.None);

        public async Task<AccountingProviderSwitchPreparationDto> StartAndRunAsync(string key)
        {
            var started = await StartAsync(key);
            Assert.Equal(1, await Service.RunDueAsync(CancellationToken.None));
            return await Service.GetAsync(new(CompanyId, SwitchId, started.Id), CancellationToken.None);
        }

        public async Task AddStagedRecordAsync(string dataset, string sourceIdentity, string normalizedDataJson,
            decimal amount, string? currency,
            string disposition = AccountingProviderSwitchDispositions.Ready)
        {
            var source = (await Context.AccountingProviderSwitches.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == CompanyId && x.Id == SwitchId)).Source;
            var assessmentId = await Context.AccountingProviderSwitchAssessments.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == CompanyId && x.SwitchId == SwitchId).Select(x => x.Id).SingleAsync();
            Context.AccountingProviderSwitchStagedRecords.Add(new AccountingProviderSwitchStagedRecord(Guid.NewGuid(),
                CompanyId, SwitchId, assessmentId, source, dataset, sourceIdentity, "v1", Now,
                new string('e', 64), new string('f', 64), normalizedDataJson,
                "{\"source\":\"fortnox\"}", amount, currency, disposition, Now));
            await Context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class ReadyPolicy(Guid companyId, Guid switchId, Guid planId, string planHash)
        : IAccountingProviderSwitchInternalReadinessPolicy
    {
        public Task<AccountingProviderSwitchInternalReadinessDto> EvaluateAsync(
            EvaluateAccountingProviderSwitchInternalReadinessQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new AccountingProviderSwitchInternalReadinessDto(companyId, switchId, planId,
                planHash, true, false, "Country-specific compliance is not configured.",
                [new("approved_current_plan", true, true, null, "Approved plan is current.", "{}")], []));
    }

    private sealed class RecordingPostingService(bool failFirstCandidatePreview = false) : IAccountingPostingService
    {
        private bool _failed;
        public List<PreviewNonAuthoritativeAccountingCandidateCommand> CandidatePreviews { get; } = [];
        public Task<AccountingPostingPreview> PreviewAsync(PreviewAccountingEntryCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingPostingPreview> PreviewNonAuthoritativeCandidateAsync(
            PreviewNonAuthoritativeAccountingCandidateCommand command, CancellationToken cancellationToken)
        {
            if (failFirstCandidatePreview && !_failed)
            {
                _failed = true;
                throw new TimeoutException("Transient posting-policy preview timeout.");
            }
            CandidatePreviews.Add(command);
            return Task.FromResult(new AccountingPostingPreview(true, 0m, 0m, 0m,
                command.Entry.Lines.FirstOrDefault()?.Currency ?? "SEK", command.Entry.Lines.Count, []));
        }
        public Task<PostedAccountingJournal> PostAsync(PostAccountingEntryCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PostedAccountingJournal> MaterializeProviderSwitchJournalAsync(
            MaterializeAccountingProviderSwitchJournalCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<PostedAccountingJournal> ReverseAsync(ReverseAccountingEntryCommand command,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    public sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        private DateTime _now = now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
        public override DateTimeOffset GetUtcNow() => new(_now, TimeSpan.Zero);
    }
}
