using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Finance.Tests;

public sealed class ExchangeRateServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 29, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly RateDate = new(2026, 8, 28);

    [Fact]
    public async Task Manual_rates_require_independent_approval_and_support_exact_inverse_and_idempotent_conversion()
    {
        await using var fixture = await ExchangeRateFixture.CreateAsync();
        var imported = await fixture.ImportAsync("manual-authority", "batch-1",
            [Observation("SEK", "EUR", 11m, RateDate)]);

        var pending = await fixture.Service.LookupAsync(
            new(fixture.CompanyId, "EUR", "SEK", RateDate, ExchangeRateLookupPurposes.TransactionDate), default);
        Assert.Equal(ExchangeRateDecisionStatuses.ReviewRequired, pending.Status);
        Assert.Equal(ExchangeRateReasonCodes.PendingApproval, pending.ReasonCode);

        var selfApproval = await Assert.ThrowsAsync<ExchangeRateOperationException>(() => fixture.Service.ReviewSetAsync(
            new(fixture.CompanyId, fixture.ImporterId, imported.Id, imported.Version, true, "Self review", null), default));
        Assert.Equal(ExchangeRateReasonCodes.PendingApproval, selfApproval.ReasonCode);

        var approved = await fixture.ApproveAsync(imported);
        var staleReview = await Assert.ThrowsAsync<ExchangeRateOperationException>(() => fixture.Service.ReviewSetAsync(
            new(fixture.CompanyId, fixture.ReviewerId, imported.Id, imported.Version, true,
                "Stale duplicate review", null), default));
        Assert.Equal(ExchangeRateReasonCodes.ConcurrencyConflict, staleReview.ReasonCode);
        var direct = await fixture.Service.LookupAsync(
            new(fixture.CompanyId, "EUR", "SEK", RateDate, ExchangeRateLookupPurposes.TransactionDate), default);
        var inverse = await fixture.Service.LookupAsync(
            new(fixture.CompanyId, "SEK", "EUR", RateDate, ExchangeRateLookupPurposes.SettlementDate), default);

        Assert.Equal(11m, direct.EffectiveRate);
        Assert.Equal(decimal.Round(1m / 11m, 18, MidpointRounding.ToEven), inverse.EffectiveRate);
        Assert.Equal(approved.Id, (await fixture.Service.ImportManualAsync(
            fixture.ImportCommand("manual-authority", "batch-1", [Observation("SEK", "EUR", 11m, RateDate)]), default)).Id);

        var conversion = await fixture.Service.ConvertAsync(new(fixture.CompanyId, fixture.ImporterId,
            12.345m, "EUR", "SEK", RateDate, ExchangeRateLookupPurposes.TransactionDate,
            "invoice-line-1", null), default);
        var replay = await fixture.Service.ConvertAsync(new(fixture.CompanyId, fixture.ImporterId,
            12.345m, "EUR", "SEK", RateDate, ExchangeRateLookupPurposes.TransactionDate,
            "invoice-line-1", null), default);

        Assert.Equal(conversion.Id, replay.Id);
        Assert.Equal(conversion.EffectiveRate, replay.EffectiveRate);
        Assert.Equal(conversion.UnroundedAmount, replay.UnroundedAmount);
        Assert.Equal(conversion.RoundedAmount, replay.RoundedAmount);
        Assert.Equal(conversion.RoundingResidual, replay.RoundingResidual);
        Assert.Equal(conversion.Legs.Select(x => x.ObservationId), replay.Legs.Select(x => x.ObservationId));
        Assert.Equal(135.795m, conversion.UnroundedAmount);
        Assert.Equal(135.80m, conversion.RoundedAmount);
        Assert.Equal(-0.005m, conversion.RoundingResidual);
        Assert.Equal(2, conversion.OutputPrecision);
        Assert.Single(conversion.Legs);
        Assert.NotEqual(Guid.Empty, conversion.Legs[0].ObservationId);
        Assert.NotEmpty(conversion.Legs[0].EvidenceChecksum);

        var evidence = await fixture.Db.ExchangeRateEvidence.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        Assert.NotEqual(ExchangeRateEvidence.ExpiredPayloadMarker, evidence.ProtectedPayload);
        Assert.DoesNotContain("Evidence for batch-1", evidence.ProtectedPayload, StringComparison.Ordinal);
        Assert.Equal(NowUtc.AddDays(2555), evidence.RetentionExpiresUtc);

        var observationId = conversion.Legs[0].ObservationId;
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            fixture.Service.GetObservationAsync(Guid.NewGuid(), observationId, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ConfigureCurrencyAsync(
            new(fixture.CompanyId, Guid.NewGuid(), "GBP", "Pound sterling", 2, true, null, null), default));
    }

    [Fact]
    public async Task Lookup_selects_historical_and_cross_rates_and_blocks_stale_or_missing_rates()
    {
        await using var fixture = await ExchangeRateFixture.CreateAsync();
        await fixture.ImportAndApproveAsync("daily", "day-1",
        [
            Observation("SEK", "EUR", 11m, RateDate),
            Observation("SEK", "USD", 10m, RateDate)
        ]);
        await fixture.ImportAndApproveAsync("daily", "day-2",
            [Observation("SEK", "EUR", 12m, RateDate.AddDays(1))]);

        var historical = await fixture.Service.LookupAsync(
            new(fixture.CompanyId, "EUR", "SEK", RateDate, ExchangeRateLookupPurposes.TransactionDate), default);
        var current = await fixture.Service.LookupAsync(
            new(fixture.CompanyId, "EUR", "SEK", RateDate.AddDays(1), ExchangeRateLookupPurposes.PeriodEnd), default);
        var cross = await fixture.Service.LookupAsync(
            new(fixture.CompanyId, "USD", "EUR", RateDate, ExchangeRateLookupPurposes.SettlementDate), default);
        var stale = await fixture.Service.LookupAsync(
            new(fixture.CompanyId, "USD", "EUR", RateDate.AddDays(32), ExchangeRateLookupPurposes.PeriodEnd), default);
        var missing = await fixture.Service.LookupAsync(
            new(fixture.CompanyId, "GBP", "EUR", RateDate, ExchangeRateLookupPurposes.TransactionDate), default);

        Assert.Equal(11m, historical.EffectiveRate);
        Assert.Equal(12m, current.EffectiveRate);
        Assert.Equal(decimal.Round(10m / 11m, 12), decimal.Round(cross.EffectiveRate!.Value, 12));
        Assert.Equal(2, cross.Legs.Count);
        Assert.Equal(ExchangeRateReasonCodes.StaleRate, stale.ReasonCode);
        Assert.Equal(ExchangeRateDecisionStatuses.Blocked, stale.Status);
        Assert.Equal(ExchangeRateReasonCodes.UnsupportedCurrency, missing.ReasonCode);
    }

    [Fact]
    public async Task Corrections_are_linked_append_only_and_become_authoritative_only_after_approval()
    {
        await using var fixture = await ExchangeRateFixture.CreateAsync();
        var originalSet = await fixture.ImportAndApproveAsync("manual-correction", "original",
            [Observation("SEK", "EUR", 11m, RateDate)]);
        var originalObservation = await fixture.Db.ExchangeRateObservations.IgnoreQueryFilters()
            .AsNoTracking().SingleAsync(x => x.RateSetId == originalSet.Id);

        var correctionInput = Observation("SEK", "EUR", 11.2m, RateDate) with
        {
            CorrectsObservationId = originalObservation.Id
        };
        var correction = await fixture.Service.ImportManualAsync(
            fixture.ImportCommand("manual-correction", "correction-1", [correctionInput], originalSet.Id), default);

        var beforeApproval = await fixture.Service.LookupAsync(
            new(fixture.CompanyId, "EUR", "SEK", RateDate, ExchangeRateLookupPurposes.TransactionDate), default);
        Assert.Equal(11m, beforeApproval.EffectiveRate);

        await fixture.ApproveAsync(correction);
        var afterApproval = await fixture.Service.LookupAsync(
            new(fixture.CompanyId, "EUR", "SEK", RateDate, ExchangeRateLookupPurposes.TransactionDate), default);
        var persistedOriginal = await fixture.Db.ExchangeRateObservations.IgnoreQueryFilters()
            .AsNoTracking().SingleAsync(x => x.Id == originalObservation.Id);
        var persistedCorrection = await fixture.Db.ExchangeRateObservations.IgnoreQueryFilters()
            .AsNoTracking().SingleAsync(x => x.RateSetId == correction.Id);

        Assert.Equal(11.2m, afterApproval.EffectiveRate);
        Assert.Equal(11m, persistedOriginal.Rate);
        Assert.Equal(originalObservation.Id, persistedCorrection.CorrectsObservationId);
        Assert.Equal(originalSet.Id, (await fixture.Db.ExchangeRateSets.IgnoreQueryFilters()
            .AsNoTracking().SingleAsync(x => x.Id == correction.Id)).CorrectsRateSetId);
    }

    [Fact]
    public async Task Equally_ranked_disagreeing_sources_require_review_instead_of_selecting_silently()
    {
        await using var fixture = await ExchangeRateFixture.CreateAsync();
        await fixture.ImportAndApproveAsync("source-a", "a-1", [Observation("SEK", "EUR", 11m, RateDate)]);
        await fixture.ImportAndApproveAsync("source-b", "b-1", [Observation("SEK", "EUR", 11.5m, RateDate)]);

        var result = await fixture.Service.LookupAsync(
            new(fixture.CompanyId, "EUR", "SEK", RateDate, ExchangeRateLookupPurposes.TransactionDate), default);

        Assert.Equal(ExchangeRateDecisionStatuses.ReviewRequired, result.Status);
        Assert.Equal(ExchangeRateReasonCodes.AmbiguousRate, result.ReasonCode);
        Assert.Null(result.EffectiveRate);

        var lowerPriority = await fixture.Db.ExchangeRateSources.IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == fixture.CompanyId && x.SourceKey == "source_b");
        lowerPriority.Configure(10, true, 31, 24, true, lowerPriority.Version, NowUtc.AddMinutes(1));
        await fixture.Db.SaveChangesAsync();
        var resolved = await fixture.Service.LookupAsync(
            new(fixture.CompanyId, "EUR", "SEK", RateDate, ExchangeRateLookupPurposes.TransactionDate), default);
        Assert.Equal(ExchangeRateDecisionStatuses.Ready, resolved.Status);
        Assert.Equal(11m, resolved.EffectiveRate);
        Assert.Equal("source_a", Assert.Single(resolved.Legs).SourceKey);
    }

    private static ManualExchangeRateObservationInput Observation(string @base, string quote, decimal rate, DateOnly date) =>
        new(@base, quote, rate, 6, ExchangeRateQuotationConventions.BaseCurrencyPerQuoteCurrency, date);

    private sealed class ExchangeRateFixture : IAsyncDisposable
    {
        private ExchangeRateFixture(VirtualCompanyDbContext db, ExchangeRateService service,
            Guid companyId, Guid importerId, Guid reviewerId)
        {
            Db = db; Service = service; CompanyId = companyId; ImporterId = importerId; ReviewerId = reviewerId;
        }

        public VirtualCompanyDbContext Db { get; }
        public ExchangeRateService Service { get; }
        public Guid CompanyId { get; }
        public Guid ImporterId { get; }
        public Guid ReviewerId { get; }

        public static async Task<ExchangeRateFixture> CreateAsync()
        {
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite("Data Source=:memory:;Foreign Keys=False").Options);
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();
            var companyId = Guid.NewGuid();
            var importerId = Guid.NewGuid();
            var reviewerId = Guid.NewGuid();
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, importerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, reviewerId, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active));
            db.AccountingConfigurations.Add(new AccountingConfiguration(Guid.NewGuid(), companyId, "SEK", 1, 1,
                "core", "1", new DateOnly(2026, 1, 1), 2, AccountingRoundingModeValues.MidpointToEven,
                importerId, NowUtc));
            await db.SaveChangesAsync();

            var service = new ExchangeRateService(db, new ExchangeRateProviderRegistry([]),
                new DataProtectionFieldEncryptionService(new EphemeralDataProtectionProvider()), new RecordingAuditWriter(),
                Options.Create(new ExchangeRateAuthorityOptions()),
                new ExchangeRateTelemetry(NullLogger<ExchangeRateTelemetry>.Instance), new FixedTimeProvider(NowUtc));
            return new(db, service, companyId, importerId, reviewerId);
        }

        public ImportManualExchangeRateSetCommand ImportCommand(string sourceKey, string identity,
            IReadOnlyList<ManualExchangeRateObservationInput> observations, Guid? correctsSetId = null) =>
            new(CompanyId, ImporterId, sourceKey, sourceKey, identity, NowUtc, observations,
                $"Evidence for {identity}", correctsSetId, $"test:{identity}");

        public Task<ExchangeRateSetResult> ImportAsync(string sourceKey, string identity,
            IReadOnlyList<ManualExchangeRateObservationInput> observations) =>
            Service.ImportManualAsync(ImportCommand(sourceKey, identity, observations), default);

        public Task<ExchangeRateSetResult> ApproveAsync(ExchangeRateSetResult set) =>
            Service.ReviewSetAsync(new(CompanyId, ReviewerId, set.Id, set.Version, true,
                "Evidence independently checked.", "test:review"), default);

        public async Task<ExchangeRateSetResult> ImportAndApproveAsync(string sourceKey, string identity,
            IReadOnlyList<ManualExchangeRateObservationInput> observations) =>
            await ApproveAsync(await ImportAsync(sourceKey, identity, observations));

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class RecordingAuditWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
