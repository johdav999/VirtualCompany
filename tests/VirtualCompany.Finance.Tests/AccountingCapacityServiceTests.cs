using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingCapacityServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 24, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Capacity_and_retention_are_company_scoped_bounded_audited_and_idempotent()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var otherPeriodId = Guid.NewGuid();
        var accessor = new TestCompanyContextAccessor(companyId, actorId);
        await using var db = new VirtualCompanyDbContext(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options, accessor);
        await db.Database.EnsureCreatedAsync();

        db.Users.Add(new User(actorId, "capacity@example.test", "Capacity owner", "test", actorId.ToString("N")));
        db.Companies.AddRange(new Company(companyId, "Capacity company"), new Company(otherCompanyId, "Other company"));
        db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, actorId,
            CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
        db.FiscalPeriods.AddRange(
            new FiscalPeriod(periodId, companyId, "August 2026", NowUtc.AddDays(-23), NowUtc.AddDays(8)),
            new FiscalPeriod(otherPeriodId, otherCompanyId, "August 2026", NowUtc.AddDays(-23), NowUtc.AddDays(8)));
        var expired = Export(companyId, periodId, actorId, "expired", NowUtc.AddDays(-31), NowUtc.AddDays(-1));
        var current = Export(companyId, periodId, actorId, "current", NowUtc.AddDays(-3), NowUtc.AddDays(27));
        var other = Export(otherCompanyId, otherPeriodId, actorId, "other", NowUtc.AddDays(-31), NowUtc.AddDays(-1));
        db.AccountingExportJobs.AddRange(expired, current, other);
        db.LedgerEntries.Add(new LedgerEntry(Guid.NewGuid(), companyId, periodId, "G-1", NowUtc.AddDays(-2),
            LedgerEntryStatuses.Posted, "Preserved journal", "manual_journal", "journal-1", NowUtc.AddDays(-2),
            postingDate: DateOnly.FromDateTime(NowUtc.AddDays(-2)), baseCurrency: "USD",
            postingType: LedgerPostingTypeValues.Manual, sourceVersion: "1", idempotencyKey: "journal-1"));
        accessor.SetCompanyId(null);
        await db.SaveChangesAsync();
        accessor.SetCompanyId(companyId);

        var telemetry = new AccountingOperationsTelemetry(NullLogger<AccountingOperationsTelemetry>.Instance);
        var service = new AccountingCapacityService(db,
            Options.Create(new AccountingCapacityOptions { DefaultCleanupBatchSize = 1, MaximumCleanupBatchSize = 2 }),
            new AuditEventWriter(db), telemetry, new FixedTimeProvider(NowUtc),
            NullLogger<AccountingCapacityService>.Instance);

        var capacity = await service.GetAsync(new GetAccountingCapacityQuery(companyId), CancellationToken.None);
        var exportVolume = Assert.Single(capacity.Volumes, x => x.Resource == "exports");
        Assert.Equal(2, exportVolume.CurrentCount);
        Assert.DoesNotContain(capacity.Volumes, x => x.CurrentCount == 3);
        Assert.Contains(capacity.RetentionClasses, x => x.Key == AccountingRetentionClassKeys.AccountingTruth &&
            x.Mode == AccountingRetentionModes.Preserve);

        var preview = await service.PreviewRetentionAsync(
            new PreviewAccountingRetentionCommand(companyId, 1), CancellationToken.None);
        Assert.Equal(1, preview.EligibleCount);
        Assert.Equal(expired.Id, Assert.Single(preview.Targets).ExportId);
        Assert.DoesNotContain(preview.Targets, x => x.ExportId == other.Id);

        var result = await service.RunRetentionCleanupAsync(new RunAccountingRetentionCleanupCommand(
            companyId, preview.PreviewToken, 1, actorId, "Expired binary content was reviewed against the retention policy.",
            "retention-test"), CancellationToken.None);
        Assert.Equal(1, result.ProcessedCount);
        Assert.Equal(64, result.ReleasedBytes);

        db.ChangeTracker.Clear();
        var persistedExpired = await db.AccountingExportJobs.IgnoreQueryFilters().SingleAsync(x => x.Id == expired.Id);
        var persistedCurrent = await db.AccountingExportJobs.IgnoreQueryFilters().SingleAsync(x => x.Id == current.Id);
        var persistedOther = await db.AccountingExportJobs.IgnoreQueryFilters().SingleAsync(x => x.Id == other.Id);
        Assert.Null(persistedExpired.Content);
        Assert.Equal(64, persistedExpired.ContentLength);
        Assert.Equal(new string('a', 64), persistedExpired.Checksum);
        Assert.NotNull(persistedCurrent.Content);
        Assert.NotNull(persistedOther.Content);
        Assert.Single(await db.LedgerEntries.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync());
        Assert.Contains(await db.AuditEvents.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync(),
            x => x.Action == AuditEventActions.AccountingExportContentExpired);

        var replay = await service.RunRetentionCleanupAsync(new RunAccountingRetentionCleanupCommand(
            companyId, preview.PreviewToken, 1, actorId, "Idempotent operator replay.", "retention-test"),
            CancellationToken.None);
        Assert.Equal(0, replay.ProcessedCount);
        Assert.Single(await db.AuditEvents.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Action == AuditEventActions.AccountingExportContentExpired)
            .ToListAsync());
    }

    [Fact]
    public async Task Cleanup_rejects_a_stale_preview_without_expiring_newly_eligible_content()
    {
        await using var fixture = await RetentionFixture.CreateAsync();
        var preview = await fixture.Service.PreviewRetentionAsync(
            new PreviewAccountingRetentionCommand(fixture.CompanyId, 10), CancellationToken.None);
        var second = Export(fixture.CompanyId, fixture.PeriodId, fixture.ActorId, "second",
            NowUtc.AddDays(-40), NowUtc.AddDays(-2));
        fixture.Db.AccountingExportJobs.Add(second);
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AccountingLifecycleException>(() =>
            fixture.Service.RunRetentionCleanupAsync(new RunAccountingRetentionCleanupCommand(
                fixture.CompanyId, preview.PreviewToken, 10, fixture.ActorId, "Preview should now be stale."),
                CancellationToken.None));

        Assert.Equal(AccountingLifecycleReasonCodes.PreviewStale, exception.ReasonCode);
        Assert.NotNull((await fixture.Db.AccountingExportJobs.IgnoreQueryFilters().SingleAsync(x => x.Id == second.Id)).Content);
    }

    private static AccountingExportJob Export(Guid companyId, Guid periodId, Guid actorId, string key,
        DateTime requestedUtc, DateTime expiresUtc)
    {
        var job = new AccountingExportJob(Guid.NewGuid(), companyId, periodId, actorId, key, requestedUtc, expiresUtc);
        job.Start(requestedUtc.AddMinutes(1));
        job.Complete(Enumerable.Repeat((byte)7, 64).ToArray(), new string('a', 64), $"{key}.json", requestedUtc.AddMinutes(2));
        return job;
    }

    private sealed class RetentionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private RetentionFixture(SqliteConnection connection, VirtualCompanyDbContext db,
            AccountingCapacityService service, Guid companyId, Guid periodId, Guid actorId)
        {
            _connection = connection;
            Db = db;
            Service = service;
            CompanyId = companyId;
            PeriodId = periodId;
            ActorId = actorId;
        }

        public VirtualCompanyDbContext Db { get; }
        public AccountingCapacityService Service { get; }
        public Guid CompanyId { get; }
        public Guid PeriodId { get; }
        public Guid ActorId { get; }

        public static async Task<RetentionFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var companyId = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var db = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options,
                new TestCompanyContextAccessor(companyId, actorId));
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new User(actorId, "stale@example.test", "Retention owner", "test", actorId.ToString("N")));
            db.Companies.Add(new Company(companyId, "Retention company"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, actorId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.FiscalPeriods.Add(new FiscalPeriod(periodId, companyId, "August 2026", NowUtc.AddDays(-23), NowUtc.AddDays(8)));
            db.AccountingExportJobs.Add(Export(companyId, periodId, actorId, "first", NowUtc.AddDays(-35), NowUtc.AddDays(-3)));
            await db.SaveChangesAsync();
            var service = new AccountingCapacityService(db, Options.Create(new AccountingCapacityOptions()),
                new AuditEventWriter(db), new AccountingOperationsTelemetry(NullLogger<AccountingOperationsTelemetry>.Instance),
                new FixedTimeProvider(NowUtc), NullLogger<AccountingCapacityService>.Instance);
            return new RetentionFixture(connection, db, service, companyId, periodId, actorId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class TestCompanyContextAccessor(Guid? companyId, Guid? userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => userId;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? resolvedCompanyId) => CompanyId = resolvedCompanyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }
}
