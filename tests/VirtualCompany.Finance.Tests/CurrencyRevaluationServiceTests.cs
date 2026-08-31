using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class CurrencyRevaluationServiceTests
{
    [Fact]
    public async Task Preview_reproduces_population_rate_proposal_rounding_and_control_reconciliation()
    {
        await using var fixture = await Fixture.CreateAsync();

        var run = await fixture.Service.PreviewAsync(fixture.Preview("first"), default);

        var item = Assert.Single(run.Population);
        Assert.Equal("USD", item.DocumentCurrency);
        Assert.Equal(100m, item.DocumentBalance);
        Assert.Equal(1_000m, item.CarryingFunctionalAmount);
        Assert.Equal(1_100m, item.RevaluedFunctionalAmount);
        Assert.Equal(100m, item.AdjustmentAmount);
        Assert.Equal(11m, item.PeriodEndRate);
        Assert.Equal(2, run.ProposalLines.Count);
        Assert.Equal(100m, run.ProposalLines.Sum(x => x.DebitAmount));
        Assert.Equal(100m, run.ProposalLines.Sum(x => x.CreditAmount));
        Assert.All(run.Reconciliations, x => Assert.True(x.IsReconciled));
        Assert.Equal(64, run.PopulationChecksum!.Length);
        Assert.Equal(64, run.RateSetChecksum!.Length);
        Assert.Equal(64, run.ProposalChecksum!.Length);
    }

    [Fact]
    public async Task Missing_rate_is_retained_for_review_and_documented_exclusion_regenerates_reconciled_proposal()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Rates.IsReady = false;
        var preview = await fixture.Service.PreviewAsync(fixture.Preview("missing-rate"), default);
        var item = Assert.Single(preview.Population);
        Assert.Equal(CurrencyRevaluationRunStatuses.NeedsReview, preview.Status);
        Assert.Equal(CurrencyRevaluationPopulationStatuses.NeedsReview, item.Status);

        fixture.BeginRequest();
        var reviewed = await fixture.Service.ReviewItemAsync(new(fixture.CompanyId, preview.Id, item.Id,
            CurrencyRevaluationReviewActions.Exclude, "Authoritative period-end rate evidence is unavailable.",
            preview.Version, fixture.ActorId), default);

        Assert.Equal(CurrencyRevaluationRunStatuses.Draft, reviewed.Status);
        Assert.Equal(0, reviewed.ReviewCount);
        Assert.Equal(1, reviewed.ExcludedCount);
        Assert.Empty(reviewed.ProposalLines);
        Assert.All(reviewed.Reconciliations, x => Assert.True(x.IsReconciled));
    }

    [Fact]
    public async Task Regeneration_after_rate_change_supersedes_run_and_invalidates_pending_approval()
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = await fixture.Service.PreviewAsync(fixture.Preview("original"), default);
        fixture.BeginRequest();
        var submitted = await fixture.Service.SubmitAsync(new(fixture.CompanyId, original.Id,
            original.Version, fixture.ActorId), default);
        fixture.Rates.Rate = 12m;

        fixture.BeginRequest();
        var replacement = await fixture.Service.PreviewAsync(fixture.Preview("replacement"), default);
        var stale = await fixture.Service.GetAsync(new(fixture.CompanyId, submitted.Id), default);

        Assert.Equal(CurrencyRevaluationRunStatuses.Superseded, stale.Status);
        Assert.Equal(replacement.Id, stale.SupersededByRunId);
        Assert.Equal("cancelled", stale.Approval!.Status);
        Assert.NotEqual(original.RateSetChecksum, replacement.RateSetChecksum);
        Assert.NotEqual(original.ProposalChecksum, replacement.ProposalChecksum);
        Assert.Equal(200m, replacement.ProposedAdjustmentTotal);
    }

    [Fact]
    public async Task Approved_post_and_replayed_next_period_reversal_create_one_reversal_journal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Service.PreviewAsync(fixture.Preview("post-and-reverse"), default);
        fixture.BeginRequest();
        var submitted = await fixture.Service.SubmitAsync(new(fixture.CompanyId, preview.Id,
            preview.Version, fixture.ActorId), default);

        fixture.BeginRequest();
        var approval = await fixture.Db.ApprovalRequests.IgnoreQueryFilters().Include(x => x.Steps)
            .SingleAsync(x => x.Id == submitted.Approval!.Id);
        approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, fixture.ActorId,
            "Approved exact period-end revaluation evidence.");
        await fixture.Db.SaveChangesAsync();

        fixture.BeginRequest();
        var posted = await fixture.Service.PostAsync(new(fixture.CompanyId, preview.Id,
            submitted.Version, "currency-revaluation-post", fixture.ActorId), default);
        fixture.BeginRequest();
        var reversed = await fixture.Service.ReverseAsync(new(fixture.CompanyId, preview.Id,
            posted.Version, "currency-revaluation-reverse", fixture.ActorId), default);
        fixture.BeginRequest();
        var replay = await fixture.Service.ReverseAsync(new(fixture.CompanyId, preview.Id,
            reversed.Version, "currency-revaluation-reverse", fixture.ActorId), default);

        Assert.Equal(CurrencyRevaluationRunStatuses.Reversed, replay.Status);
        Assert.Equal(reversed.ReversalLedgerEntryId, replay.ReversalLedgerEntryId);
        Assert.Single(fixture.Posting.Reversals);
    }

    [Fact]
    public async Task Run_history_query_enforces_bounded_page_size()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.PreviewAsync(fixture.Preview("bounded-history"), default);
        fixture.BeginRequest();

        var history = await fixture.Service.ListAsync(new(fixture.CompanyId, fixture.PeriodId,
            Skip: 0, Take: int.MaxValue), default);

        Assert.Equal(100, history.Take);
        Assert.Single(history.Items);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _metrics;
        private Fixture(VirtualCompanyDbContext db, ServiceProvider metrics, CurrencyRevaluationService service,
            FakeRates rates, RecordingPosting posting, Guid companyId, Guid periodId, Guid actorId)
        { Db = db; _metrics = metrics; Service = service; Rates = rates; Posting = posting; CompanyId = companyId; PeriodId = periodId; ActorId = actorId; }
        public VirtualCompanyDbContext Db { get; }
        public CurrencyRevaluationService Service { get; }
        public FakeRates Rates { get; }
        public RecordingPosting Posting { get; }
        public Guid CompanyId { get; }
        public Guid PeriodId { get; }
        public Guid ActorId { get; }
        public void BeginRequest() => Db.ChangeTracker.Clear();
        public PreviewCurrencyRevaluationCommand Preview(string identity) => new(CompanyId, PeriodId, "A",
            $"currency-revaluation-test:{identity}", ActorId);

        public static async Task<Fixture> CreateAsync()
        {
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite("Data Source=:memory:;Foreign Keys=False").Options);
            await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
            var company = Guid.NewGuid(); var actor = Guid.NewGuid(); var period = Guid.NewGuid();
            var configId = Guid.NewGuid(); var receivable = Guid.NewGuid(); var gain = Guid.NewGuid(); var loss = Guid.NewGuid();
            var now = new DateTime(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), company, actor,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.FiscalPeriods.Add(new FiscalPeriod(period, company, "2026-08",
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
            db.FiscalPeriods.Add(new FiscalPeriod(Guid.NewGuid(), company, "2026-09",
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
            db.AccountingConfigurations.Add(new AccountingConfiguration(configId, company, "SEK", 1, 1,
                "core", "1", new DateOnly(2026, 1, 1), 2, AccountingRoundingModeValues.MidpointToEven, actor, now));
            db.FinanceAccounts.AddRange(
                Account(receivable, company, "1510", "Trade receivables", FinanceAccountClassValues.Asset, FinanceNormalBalanceValues.Debit, now),
                Account(gain, company, "3960", "Unrealized exchange gains", FinanceAccountClassValues.Income, FinanceNormalBalanceValues.Credit, now),
                Account(loss, company, "7960", "Unrealized exchange losses", FinanceAccountClassValues.Expense, FinanceNormalBalanceValues.Debit, now));
            db.AccountingConfigurationAccountRoles.AddRange(
                new AccountingConfigurationAccountRole(Guid.NewGuid(), company, configId, AccountingAccountRoleKeys.AccountsReceivable, receivable, now),
                new AccountingConfigurationAccountRole(Guid.NewGuid(), company, configId, AccountingAccountRoleKeys.ExchangeGain, gain, now),
                new AccountingConfigurationAccountRole(Guid.NewGuid(), company, configId, AccountingAccountRoleKeys.ExchangeLoss, loss, now));
            var entryId = Guid.NewGuid();
            db.LedgerEntries.Add(new LedgerEntry(entryId, company, period, "A-2026-000001", now,
                LedgerEntryStatuses.Posted, "Foreign customer invoice", "customer_invoice", Guid.NewGuid().ToString("N"),
                now, documentDate: new DateOnly(2026, 8, 15), postingDate: new DateOnly(2026, 8, 15),
                baseCurrency: "SEK", postingType: LedgerPostingTypeValues.SourceDocument));
            db.LedgerEntryLines.Add(new LedgerEntryLine(Guid.NewGuid(), company, entryId, receivable,
                1_000m, 0m, "SEK", description: "USD receivable", createdUtc: now,
                documentDebitAmount: 100m, documentCreditAmount: 0m, documentCurrency: "USD",
                exchangeRate: 10m, exchangeRateDate: new DateOnly(2026, 8, 15), exchangeRateIdentity: "source-rate"));
            await db.SaveChangesAsync();
            var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
            var rates = new FakeRates(now);
            var posting = new RecordingPosting();
            var service = new CurrencyRevaluationService(db, rates, posting, new RecordingAudit(),
                new FixedTimeProvider(now), new CurrencyRevaluationTelemetry(metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>()));
            return new(db, metrics, service, rates, posting, company, period, actor);
        }

        private static FinanceAccount Account(Guid id, Guid company, string code, string name, string classification,
            string normalBalance, DateTime now) => new(id, company, code, name, classification, "SEK", 0m, now,
            accountClass: classification, normalBalance: normalBalance, effectiveFrom: new DateOnly(2026, 1, 1), isPostingEnabled: true);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _metrics.DisposeAsync(); }
    }

    private sealed class FakeRates(DateTime now) : IExchangeRateService
    {
        public decimal Rate { get; set; } = 11m; public bool IsReady { get; set; } = true;
        private ExchangeRateLookupLeg Leg(DateOnly date) => new(Guid.Parse($"00000000-0000-0000-0000-{(Rate * 100):000000000000}"),
            "test_authority", (long)(Rate * 100), "USD", "SEK", Rate, Rate, 6, date, 0,
            ExchangeRateQuotationConventions.BaseCurrencyPerQuoteCurrency, new string('e', 64));
        public Task<ExchangeRateLookupResult> LookupAsync(ExchangeRateLookupQuery query, CancellationToken cancellationToken)
        {
            var leg = Leg(query.Date);
            return Task.FromResult(IsReady
                ? new ExchangeRateLookupResult(ExchangeRateDecisionStatuses.Ready, ExchangeRateReasonCodes.None,
                    "Authoritative test rate.", query.FromCurrency, query.ToCurrency, query.Date, query.Purpose,
                    Rate, query.Date, [leg])
                : new ExchangeRateLookupResult(ExchangeRateDecisionStatuses.ReviewRequired,
                    ExchangeRateReasonCodes.MissingRate, "No authoritative period-end rate.", query.FromCurrency,
                    query.ToCurrency, query.Date, query.Purpose, null, null, []));
        }
        public Task<ExchangeRateConversionResult> ConvertAsync(ConvertCurrencyCommand command, CancellationToken cancellationToken)
        {
            var rounded = decimal.Round(command.Amount * Rate, 2, MidpointRounding.ToEven); var leg = Leg(command.Date);
            return Task.FromResult(new ExchangeRateConversionResult(Guid.NewGuid(), command.IdempotencyKey,
                command.Purpose, command.Date, command.Amount, command.FromCurrency, command.ToCurrency, Rate,
                command.Amount * Rate, rounded, command.Amount * Rate - rounded, 2,
                AccountingRoundingModeValues.MidpointToEven, now, [leg]));
        }
        public Task<IReadOnlyList<CurrencyDefinitionResult>> GetCurrenciesAsync(Guid companyId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExchangeRateSourceResult>> GetSourcesAsync(Guid companyId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExchangeRateObservationResult> GetObservationAsync(Guid companyId, Guid observationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExchangeRateReadinessResult> GetReadinessAsync(Guid companyId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CurrencyDefinitionResult> ConfigureCurrencyAsync(ConfigureCurrencyCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExchangeRateSourceResult> ConfigureSourceAsync(ConfigureExchangeRateSourceCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExchangeRateSetResult> ImportManualAsync(ImportManualExchangeRateSetCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExchangeRateSetResult> ReviewSetAsync(ReviewExchangeRateSetCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExchangeRateRefreshJobResult> QueueRefreshAsync(QueueExchangeRateRefreshCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingPosting : IAccountingPostingService
    {
        private ProposedAccountingEntry? _posted;
        public List<ReverseAccountingEntryCommand> Reversals { get; } = [];
        public Task<AccountingPostingPreview> PreviewAsync(PreviewAccountingEntryCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Preview(command.Entry));
        public Task<AccountingPostingPreview> PreviewNonAuthoritativeCandidateAsync(PreviewNonAuthoritativeAccountingCandidateCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(Preview(command.Entry));
        public Task<PostedAccountingJournal> PostAsync(PostAccountingEntryCommand command, CancellationToken cancellationToken)
        {
            _posted = command.Entry;
            return Task.FromResult(new PostedAccountingJournal(Journal(command.Entry, Guid.NewGuid(), command.Entry.FiscalPeriodId, "A-1"), false));
        }
        public Task<PostedAccountingJournal> MaterializeProviderSwitchJournalAsync(MaterializeAccountingProviderSwitchJournalCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PostedAccountingJournal> ReverseAsync(ReverseAccountingEntryCommand command, CancellationToken cancellationToken)
        {
            Reversals.Add(command);
            return Task.FromResult(new PostedAccountingJournal(Journal(_posted!, Guid.NewGuid(), command.FiscalPeriodId, "A-REV-1"), false));
        }
        private static AccountingPostingPreview Preview(ProposedAccountingEntry entry)
        {
            var debit = entry.Lines.Sum(x => x.DebitAmount);
            var credit = entry.Lines.Sum(x => x.CreditAmount);
            return new(debit == credit, debit, credit, debit - credit, entry.Lines[0].Currency, 2, []);
        }
        private static AccountingJournalDto Journal(ProposedAccountingEntry entry, Guid id, Guid periodId, string number) =>
            new(id, entry.CompanyId, periodId, number, "posted", entry.VoucherSeriesCode, 1,
                entry.PostingDate.Year, entry.DocumentDate, entry.PostingDate, entry.Lines[0].Currency,
                entry.PostingType, entry.Description, entry.SourceType, entry.SourceId, entry.SourceVersion,
                "core", "1", entry.ActorUserId, entry.ApprovalRequestId, entry.OriginalLedgerEntryId,
                entry.CorrectionReason, DateTime.UtcNow, entry.Lines.Sum(x => x.DebitAmount),
                entry.Lines.Sum(x => x.CreditAmount), []);
    }
    private sealed class RecordingAudit : IAuditEventWriter
    { public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken) => Task.CompletedTask; }
    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(now); }
}
