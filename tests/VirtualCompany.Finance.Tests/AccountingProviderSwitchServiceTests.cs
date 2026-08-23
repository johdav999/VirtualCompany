using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Create_persists_audited_draft_without_changing_authority_and_rejects_a_duplicate()
    {
        await using var fixture = await Fixture.CreateAsync(AccountingAuthorityValues.InternalLedger, null);
        var created = await fixture.Service.CreateAsync(fixture.CreateCommand(), CancellationToken.None);

        Assert.Equal(AccountingProviderSwitchStatuses.Draft, created.Status);
        Assert.Equal("outbound", created.Direction);
        Assert.Equal("fortnox", created.Target.ProviderKey);
        Assert.Equal(1, created.Version);
        var authority = await fixture.DbContext.AccountingAuthorityPeriods.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(AccountingAuthorityValues.InternalLedger, authority.Authority);
        Assert.Null(authority.EffectiveTo);
        Assert.Equal(1, await fixture.DbContext.AuditEvents.IgnoreQueryFilters().CountAsync(x =>
            x.Action == AuditEventActions.AccountingProviderSwitchCreated));

        var exception = await Assert.ThrowsAsync<AccountingAuthorityException>(() =>
            fixture.Service.CreateAsync(fixture.CreateCommand(), CancellationToken.None));
        Assert.Equal(AccountingProviderSwitchReasonCodes.DuplicateActiveSwitch, exception.ReasonCode);
        Assert.True(exception.IsConflict);
    }

    [Fact]
    public async Task Stale_plan_write_changes_no_switch_state_and_persists_rejection_audit()
    {
        await using var fixture = await Fixture.CreateAsync(AccountingAuthorityValues.InternalLedger, null);
        var created = await fixture.Service.CreateAsync(fixture.CreateCommand(), CancellationToken.None);
        var exception = await Assert.ThrowsAsync<AccountingAuthorityException>(() =>
            fixture.Service.UpdatePlanAsync(new UpdateAccountingProviderSwitchPlanCommand(
                fixture.CompanyId, created.Id, "internal", null, "external", "fortnox",
                fixture.FiscalPeriodId, AccountingProviderSwitchStrategies.FullHistory, "Stale update.",
                fixture.OwnerId, null, ExpectedVersion: created.Version - 1, fixture.OwnerId, "stale-plan"),
                CancellationToken.None));

        Assert.Equal(AccountingProviderSwitchReasonCodes.ConcurrencyConflict, exception.ReasonCode);
        var persisted = await fixture.DbContext.AccountingProviderSwitches.IgnoreQueryFilters()
            .AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems, persisted.MigrationStrategy);
        Assert.Equal(created.Version, persisted.Version);
        var rejectionAudit = await fixture.DbContext.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Action == AuditEventActions.AccountingProviderSwitchMutationRejected);
        Assert.Equal(AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
            rejectionAudit.Metadata["reasonCode"]);
    }

    [Fact]
    public async Task External_authority_supports_inbound_and_provider_to_provider_directions()
    {
        await using var fixture = await Fixture.CreateAsync(AccountingAuthorityValues.ExternalProvider, "fortnox");
        var inbound = await fixture.Service.CreateAsync(
            fixture.CreateCommand(sourceKind: "external", sourceProvider: "fortnox", targetKind: "internal", targetProvider: null),
            CancellationToken.None);
        Assert.Equal("inbound", inbound.Direction);
        var cancelled = await fixture.Service.CancelAsync(new(
            fixture.CompanyId, inbound.Id, "Test the next direction.", inbound.Version,
            fixture.OwnerId, "cancel-inbound"), CancellationToken.None);
        Assert.Equal(AccountingProviderSwitchStatuses.Cancelled, cancelled.Status);

        var providerToProvider = await fixture.Service.CreateAsync(
            fixture.CreateCommand(sourceKind: "external", sourceProvider: "fortnox", targetKind: "external", targetProvider: "next-provider"),
            CancellationToken.None);
        Assert.Equal("provider_to_provider", providerToProvider.Direction);
        Assert.Equal("fortnox", providerToProvider.Source.ProviderKey);
        Assert.Equal("next-provider", providerToProvider.Target.ProviderKey);
    }

    [Fact]
    public async Task Queries_and_mutations_are_explicitly_company_scoped()
    {
        await using var fixture = await Fixture.CreateAsync(AccountingAuthorityValues.InternalLedger, null);
        var created = await fixture.Service.CreateAsync(fixture.CreateCommand(), CancellationToken.None);
        var otherCompany = Guid.NewGuid();

        var getException = await Assert.ThrowsAsync<AccountingAuthorityException>(() =>
            fixture.Service.GetAsync(new(otherCompany, created.Id), CancellationToken.None));
        var cancelException = await Assert.ThrowsAsync<AccountingAuthorityException>(() =>
            fixture.Service.CancelAsync(new(otherCompany, created.Id, "Cross-company request.", created.Version,
                fixture.OwnerId, "cross-company"), CancellationToken.None));

        Assert.Equal(AccountingProviderSwitchReasonCodes.NotFound, getException.ReasonCode);
        Assert.Equal(AccountingProviderSwitchReasonCodes.NotFound, cancelException.ReasonCode);
        Assert.Equal(AccountingProviderSwitchStatuses.Draft,
            (await fixture.DbContext.AccountingProviderSwitches.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.Id == created.Id)).Status);
    }

    [Fact]
    public async Task Period_and_source_authority_validation_return_stable_policy_results()
    {
        await using var fixture = await Fixture.CreateAsync(AccountingAuthorityValues.InternalLedger, null);
        var missingPeriod = fixture.CreateCommand() with { EffectiveFiscalPeriodId = Guid.NewGuid() };
        var missingException = await Assert.ThrowsAsync<AccountingAuthorityException>(() =>
            fixture.Service.CreateAsync(missingPeriod, CancellationToken.None));
        Assert.Equal(AccountingProviderSwitchReasonCodes.FiscalPeriodNotFound, missingException.ReasonCode);

        var sourceMismatch = fixture.CreateCommand(
            sourceKind: "external", sourceProvider: "fortnox", targetKind: "internal", targetProvider: null);
        var sourceException = await Assert.ThrowsAsync<AccountingAuthorityException>(() =>
            fixture.Service.CreateAsync(sourceMismatch, CancellationToken.None));
        Assert.Equal(AccountingProviderSwitchReasonCodes.SourceAuthorityMismatch, sourceException.ReasonCode);

        var pastPeriodId = Guid.NewGuid();
        var nonMonthlyPeriodId = Guid.NewGuid();
        fixture.DbContext.FiscalPeriods.AddRange(
            new FiscalPeriod(pastPeriodId, fixture.CompanyId, "August 2026",
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
            new FiscalPeriod(nonMonthlyPeriodId, fixture.CompanyId, "Non-monthly future period",
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc)));
        await fixture.DbContext.SaveChangesAsync();

        var pastException = await Assert.ThrowsAsync<AccountingAuthorityException>(() =>
            fixture.Service.CreateAsync(fixture.CreateCommand() with { EffectiveFiscalPeriodId = pastPeriodId }, CancellationToken.None));
        var monthlyException = await Assert.ThrowsAsync<AccountingAuthorityException>(() =>
            fixture.Service.CreateAsync(fixture.CreateCommand() with { EffectiveFiscalPeriodId = nonMonthlyPeriodId }, CancellationToken.None));
        Assert.Equal(AccountingProviderSwitchReasonCodes.FutureBoundaryRequired, pastException.ReasonCode);
        Assert.Equal(AccountingProviderSwitchReasonCodes.MonthlyBoundaryRequired, monthlyException.ReasonCode);
    }

    [Fact]
    public async Task Status_changes_and_blocking_are_versioned_and_audited()
    {
        await using var fixture = await Fixture.CreateAsync(AccountingAuthorityValues.InternalLedger, null);
        var created = await fixture.Service.CreateAsync(fixture.CreateCommand(), CancellationToken.None);
        var assessing = await fixture.Service.TransitionAsync(new(
            fixture.CompanyId, created.Id, AccountingProviderSwitchStatuses.Assessing,
            created.Version, fixture.OwnerId, "start-assessment"), CancellationToken.None);
        var blocked = await fixture.Service.BlockAsync(new(
            fixture.CompanyId, created.Id, "missing_scope", "A required read scope is missing.",
            assessing.Version, fixture.OwnerId, "block-assessment"), CancellationToken.None);

        Assert.Equal(AccountingProviderSwitchStatuses.Blocked, blocked.Status);
        Assert.Equal(AccountingProviderSwitchStatuses.Assessing, blocked.BlockedFromStatus);
        Assert.Equal(created.Version + 2, blocked.Version);
        Assert.Equal(1, await fixture.DbContext.AuditEvents.IgnoreQueryFilters().CountAsync(x =>
            x.Action == AuditEventActions.AccountingProviderSwitchStatusChanged));
        Assert.Equal(1, await fixture.DbContext.AuditEvents.IgnoreQueryFilters().CountAsync(x =>
            x.Action == AuditEventActions.AccountingProviderSwitchBlocked));

        var illegal = await Assert.ThrowsAsync<AccountingAuthorityException>(() =>
            fixture.Service.TransitionAsync(new(
                fixture.CompanyId, created.Id, AccountingProviderSwitchStatuses.Scheduled,
                blocked.Version, fixture.OwnerId, "illegal-transition"), CancellationToken.None));
        Assert.Equal(AccountingProviderSwitchReasonCodes.IllegalTransition, illegal.ReasonCode);
        Assert.Equal(1, await fixture.DbContext.AuditEvents.IgnoreQueryFilters().CountAsync(x =>
            x.Action == AuditEventActions.AccountingProviderSwitchMutationRejected));
    }

    [Theory]
    [InlineData("external", null, "internal", null, AccountingProviderSwitchReasonCodes.InvalidEndpoint, AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems)]
    [InlineData("internal", null, "internal", null, AccountingProviderSwitchReasonCodes.SameEndpoint, AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems)]
    [InlineData("internal", null, "external", "fortnox", AccountingProviderSwitchReasonCodes.InvalidStrategy, "unsupported")]
    public async Task Invalid_endpoint_same_endpoint_and_strategy_return_stable_reason_codes(
        string sourceKind,
        string? sourceProvider,
        string targetKind,
        string? targetProvider,
        string expectedReason,
        string strategy)
    {
        await using var fixture = await Fixture.CreateAsync(AccountingAuthorityValues.InternalLedger, null);
        var command = fixture.CreateCommand(sourceKind, sourceProvider, targetKind, targetProvider, strategy);

        var exception = await Assert.ThrowsAsync<AccountingAuthorityException>(() =>
            fixture.Service.CreateAsync(command, CancellationToken.None));

        Assert.Equal(expectedReason, exception.ReasonCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private Fixture(
            SqliteConnection connection,
            VirtualCompanyDbContext dbContext,
            AccountingProviderSwitchService service,
            Guid companyId,
            Guid ownerId,
            Guid fiscalPeriodId)
        {
            _connection = connection;
            DbContext = dbContext;
            Service = service;
            CompanyId = companyId;
            OwnerId = ownerId;
            FiscalPeriodId = fiscalPeriodId;
        }

        public VirtualCompanyDbContext DbContext { get; }
        public AccountingProviderSwitchService Service { get; }
        public Guid CompanyId { get; }
        public Guid OwnerId { get; }
        public Guid FiscalPeriodId { get; }

        public static async Task<Fixture> CreateAsync(string authority, string? providerKey)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var dbContext = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
            await dbContext.Database.EnsureCreatedAsync();
            var companyId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var fiscalPeriodId = Guid.NewGuid();
            dbContext.Companies.Add(new Company(companyId, "Provider switch company"));
            dbContext.Users.Add(new User(ownerId, $"{ownerId:N}@example.com", "Accounting owner", "test", ownerId.ToString("N")));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(), companyId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.FiscalPeriods.Add(new FiscalPeriod(
                fiscalPeriodId, companyId, "September 2026",
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
            dbContext.AccountingAuthorityPeriods.Add(new AccountingAuthorityPeriod(
                Guid.NewGuid(), companyId, new DateOnly(2026, 1, 1), null,
                authority, providerKey, ownerId, "Current accounting authority.", NowUtc));
            await dbContext.SaveChangesAsync();
            var service = new AccountingProviderSwitchService(
                dbContext, new AuditEventWriter(dbContext), new FixedTimeProvider(NowUtc));
            return new Fixture(connection, dbContext, service, companyId, ownerId, fiscalPeriodId);
        }

        public CreateAccountingProviderSwitchCommand CreateCommand(
            string sourceKind = "internal",
            string? sourceProvider = null,
            string targetKind = "external",
            string? targetProvider = "fortnox",
            string strategy = AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems) =>
            new(CompanyId, sourceKind, sourceProvider, targetKind, targetProvider, FiscalPeriodId,
                strategy, "Move accounting at the September boundary.", OwnerId, null, OwnerId, Guid.NewGuid().ToString("N"));

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
