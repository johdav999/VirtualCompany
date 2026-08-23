using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchStagingServiceTests
{
    [Fact]
    public async Task Replay_reuses_identity_and_new_source_version_invalidates_mapping_readiness()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.StageAsync(fixture.Stage("v1"), CancellationToken.None);
        var replay = await fixture.Service.StageAsync(fixture.Stage("v1"), CancellationToken.None);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(1, await fixture.Context.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().CountAsync());

        var mapping = await fixture.Service.PreviewMappingAsync(new PreviewAccountingProviderSwitchMappingCommand(
            fixture.CompanyId, fixture.SwitchId, AccountingProviderSwitchMappingTypes.Currency, "SEK", null, null,
            [first.Id], false, fixture.OwnerId, "mapping-preview"), CancellationToken.None);
        Assert.Equal(AccountingProviderSwitchMappingStatuses.Approved, mapping.Status);
        var listedMappings = await fixture.Service.ListMappingsAsync(
            new ListAccountingProviderSwitchMappingsQuery(fixture.CompanyId, fixture.SwitchId),
            CancellationToken.None);
        Assert.Collection(listedMappings, listed => Assert.Equal(mapping.Id, listed.Id));
        var mapped = await fixture.Service.ResolveDispositionAsync(new ResolveAccountingProviderSwitchDispositionCommand(
            fixture.CompanyId, fixture.SwitchId, first.Id, AccountingProviderSwitchDispositions.Mapped,
            "Exact currency identifier.", mapping.Id, null, replay.Version, fixture.OwnerId, "mapping-resolution"),
            CancellationToken.None);
        Assert.Equal(AccountingProviderSwitchDispositions.Mapped, mapped.Disposition);
        Assert.True((await fixture.Service.GetCompletenessAsync(new(fixture.CompanyId, fixture.SwitchId),
            CancellationToken.None)).IsComplete);

        var next = await fixture.Service.StageAsync(fixture.Stage("v2"), CancellationToken.None);
        var records = await fixture.Service.ListAsync(new(fixture.CompanyId, fixture.SwitchId,
            IncludeSuperseded: true), CancellationToken.None);
        var refreshedMapping = await fixture.Context.AccountingProviderSwitchMappingDecisions.IgnoreQueryFilters()
            .SingleAsync(x => x.Id == mapping.Id);

        Assert.Equal(2, records.Count);
        Assert.Equal(AccountingProviderSwitchDispositions.AwaitingEvidence, next.Disposition);
        Assert.Equal(AccountingProviderSwitchMappingStatuses.Stale, refreshedMapping.Status);
        Assert.False((await fixture.Service.GetCompletenessAsync(new(fixture.CompanyId, fixture.SwitchId),
            CancellationToken.None)).IsComplete);
    }

    [Fact]
    public async Task Company_scope_and_sensitive_evidence_are_enforced()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<AccountingAuthorityException>(() => fixture.Service.StageAsync(
            fixture.Stage("v1") with { EvidenceJson = "{\"access_token\":\"secret\"}" }, CancellationToken.None));

        var record = await fixture.Service.StageAsync(fixture.Stage("v1"), CancellationToken.None);
        var crossTenant = await Assert.ThrowsAsync<AccountingAuthorityException>(() => fixture.Service.ListAsync(
            new ListAccountingProviderSwitchStagedRecordsQuery(Guid.NewGuid(), fixture.SwitchId),
            CancellationToken.None));
        var crossTenantMappings = await Assert.ThrowsAsync<AccountingAuthorityException>(() =>
            fixture.Service.ListMappingsAsync(
                new ListAccountingProviderSwitchMappingsQuery(Guid.NewGuid(), fixture.SwitchId),
                CancellationToken.None));

        Assert.Equal(AccountingProviderSwitchReasonCodes.NotFound, crossTenant.ReasonCode);
        Assert.Equal(AccountingProviderSwitchReasonCodes.NotFound, crossTenantMappings.ReasonCode);
        Assert.NotEqual(Guid.Empty, record.Id);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly DateTime Now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext context, Guid companyId,
            Guid ownerId, Guid switchId, Guid assessmentId, AccountingProviderSwitchStagingService service)
        {
            _connection = connection;
            Context = context;
            CompanyId = companyId;
            OwnerId = ownerId;
            SwitchId = switchId;
            AssessmentId = assessmentId;
            Service = service;
        }

        public VirtualCompanyDbContext Context { get; }
        public Guid CompanyId { get; }
        public Guid OwnerId { get; }
        public Guid SwitchId { get; }
        public Guid AssessmentId { get; }
        public AccountingProviderSwitchStagingService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var context = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var companyId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();
            var switchId = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            context.Companies.Add(new Company(companyId, "Staging company"));
            context.Users.Add(new User(ownerId, $"{ownerId:N}@example.com", "Staging owner", "test", ownerId.ToString("N")));
            context.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, ownerId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            context.FiscalPeriods.Add(new FiscalPeriod(periodId, companyId, "September 2026",
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
            var providerSwitch = new AccountingProviderSwitch(switchId, companyId,
                new AccountingProviderEndpoint("internal", null), new AccountingProviderEndpoint("external", "fortnox"),
                periodId, AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems, "Stage records.", ownerId,
                null, ownerId, "create-switch", Now);
            providerSwitch.TransitionTo(AccountingProviderSwitchStatuses.Assessing, ownerId, "assessing", Now);
            providerSwitch.TransitionTo(AccountingProviderSwitchStatuses.ReadyForPlanning, ownerId, "ready", Now);
            context.AccountingProviderSwitches.Add(providerSwitch);
            var assessment = new AccountingProviderSwitchAssessment(Guid.NewGuid(), companyId, switchId, ownerId,
                "assessment", "assessment", 1, Now);
            assessment.Complete(Now);
            context.AccountingProviderSwitchAssessments.Add(assessment);
            var dataset = new AccountingProviderSwitchDataset(companyId, switchId, assessment.Id,
                AccountingProviderSwitchEndpointRoles.Source, AccountingProviderSwitchDatasetKeys.Currencies, Now);
            dataset.Record(AccountingProviderSwitchDatasetAvailability.Available,
                AccountingProviderSwitchCapabilityLevels.Supported, 1, 10m, "SEK", null, "v1",
                new string('b', 64), "{}", null, null, Now);
            context.AccountingProviderSwitchDatasets.Add(dataset);
            await context.SaveChangesAsync();
            var time = new FixedTimeProvider(Now);
            var service = new AccountingProviderSwitchStagingService(context, new UnexpectedApprovalService(),
                new AuditEventWriter(context), time);
            return new Fixture(connection, context, companyId, ownerId, switchId, assessment.Id, service);
        }

        public StageAccountingProviderSwitchRecordCommand Stage(string version) => new(CompanyId, SwitchId,
            AssessmentId, AccountingProviderSwitchStagingDatasets.Currencies, "SEK", version, Now,
            new string(version == "v1" ? 'a' : 'c', 64), "{\"code\":\"SEK\"}", "{}", 10m, "SEK",
            AccountingProviderSwitchDispositions.Ready, OwnerId, $"stage-{version}");

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class UnexpectedApprovalService : IApprovalRequestService
    {
        public Task<IReadOnlyList<ApprovalRequestDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ApprovalRequestDto> GetAsync(Guid companyId, Guid approvalId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ApprovalRequestDto> CreateAsync(Guid companyId, CreateApprovalRequestCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ApprovalDecisionResultDto> DecideAsync(Guid companyId, ApprovalDecisionCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
