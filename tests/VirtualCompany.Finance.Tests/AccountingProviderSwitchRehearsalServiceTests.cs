using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingProviderSwitchRehearsalServiceTests
{
    [Fact]
    public async Task Rehearsal_is_idempotent_non_authoritative_and_binds_approval_to_immutable_current_plan()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.Service.StartAsync(f.Start("rehearsal-1"), CancellationToken.None);
        var replay = await f.Service.StartAsync(f.Start("rehearsal-1"), CancellationToken.None);
        Assert.Equal(first.Id, replay.Id);

        Assert.Equal(1, await f.Service.RunDueAsync(CancellationToken.None));
        var completed = await f.Service.GetAsync(new(f.CompanyId, f.SwitchId, first.Id), CancellationToken.None);
        Assert.Equal(AccountingProviderSwitchRehearsalStatuses.Completed, completed.Status);
        Assert.True(completed.IsReadyForPlan);
        Assert.False(completed.ProviderAcceptanceProven);
        Assert.Equal("local_target_simulation", completed.SimulationKind);
        Assert.Contains("Fortnox", completed.Disclosure, StringComparison.OrdinalIgnoreCase);
        Assert.All(completed.Checks, check => Assert.Equal(AccountingProviderSwitchReconciliationResults.Passed, check.Result));

        var plan = await f.Service.GeneratePlanAsync(new(f.CompanyId, f.SwitchId, first.Id, f.SwitchVersion,
            f.Now.AddHours(1), f.Now.AddHours(2), "No authoritative target postings exist before activation.",
            [f.OwnerId], f.OwnerId, "plan"), CancellationToken.None);
        Assert.Equal(64, plan.PlanHash.Length);
        Assert.False(plan.IsApprovedAndCurrent);

        var requested = await f.Service.RequestPlanApprovalAsync(new(f.CompanyId, f.SwitchId, plan.Id,
            f.SwitchVersion, f.OwnerId, "approval"), CancellationToken.None);
        Assert.Equal(ApprovalRequestStatus.Pending.ToStorageValue(), requested.ApprovalStatus);
        Assert.True(requested.IsCurrent);
        Assert.NotNull(requested.ApprovalRequestId);
        var binding = await f.Context.AccountingProviderSwitchPlanApprovals.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(plan.PlanHash, binding.PlanHash);

        var authority = await f.Context.AccountingAuthorityPeriods.IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == f.CompanyId);
        Assert.Equal(AccountingAuthorityValues.InternalLedger, authority.Authority);
        Assert.Null(authority.EffectiveTo);
    }

    [Fact]
    public async Task Changed_staging_evidence_makes_generated_plan_stale()
    {
        await using var f = await Fixture.CreateAsync();
        var run = await f.Service.StartAsync(f.Start("rehearsal-stale"), CancellationToken.None);
        await f.Service.RunDueAsync(CancellationToken.None);
        var plan = await f.Service.GeneratePlanAsync(new(f.CompanyId, f.SwitchId, run.Id, f.SwitchVersion,
            f.Now.AddHours(1), f.Now.AddHours(2), "Recover before target activation.", [f.OwnerId],
            f.OwnerId, "plan"), CancellationToken.None);

        var staged = await f.Context.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().SingleAsync();
        staged.ReplaceNormalizedSnapshot(f.AssessmentId, f.Now, new string('c', 64), new string('d', 64),
            "{\"code\":\"EUR\"}", "{\"document\":\"changed\"}", 10m, "SEK", f.Now.AddMinutes(1));
        await f.Context.SaveChangesAsync();

        var readiness = await f.Service.GetPlanReadinessAsync(new(f.CompanyId, f.SwitchId, plan.Id), CancellationToken.None);
        Assert.False(readiness.IsReady);
        Assert.Equal(AccountingProviderSwitchRehearsalReasonCodes.PlanStale, readiness.BlockingReasonCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext context,
            AccountingProviderSwitchRehearsalService service, Guid companyId, Guid ownerId, Guid switchId,
            Guid assessmentId, long switchVersion, DateTime now)
        { _connection = connection; Context = context; Service = service; CompanyId = companyId; OwnerId = ownerId;
            SwitchId = switchId; AssessmentId = assessmentId; SwitchVersion = switchVersion; Now = now; }
        public VirtualCompanyDbContext Context { get; }
        public AccountingProviderSwitchRehearsalService Service { get; }
        public Guid CompanyId { get; } public Guid OwnerId { get; } public Guid SwitchId { get; }
        public Guid AssessmentId { get; } public long SwitchVersion { get; } public DateTime Now { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var now = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var companyId = Guid.NewGuid(); var ownerId = Guid.NewGuid(); var switchId = Guid.NewGuid(); var periodId = Guid.NewGuid();
            db.Companies.Add(new Company(companyId, "Rehearsal company"));
            db.Users.Add(new User(ownerId, $"{ownerId:N}@example.com", "Owner", "test", ownerId.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, ownerId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.FiscalPeriods.Add(new FiscalPeriod(periodId, companyId, "September 2026", now.AddDays(11), now.AddDays(41)));
            db.AccountingAuthorityPeriods.Add(new AccountingAuthorityPeriod(Guid.NewGuid(), companyId,
                DateOnly.FromDateTime(now.AddMonths(-1)), null, AccountingAuthorityValues.InternalLedger, null,
                ownerId, "Current authority", now));
            var sw = new AccountingProviderSwitch(switchId, companyId, new("internal", null),
                new("external", "fortnox"), periodId, AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems,
                "Move accounting system.", ownerId, null, ownerId, "switch", now);
            sw.TransitionTo(AccountingProviderSwitchStatuses.Assessing, ownerId, "assess", now);
            sw.TransitionTo(AccountingProviderSwitchStatuses.ReadyForPlanning, ownerId, "ready", now);
            db.AccountingProviderSwitches.Add(sw);
            var assessment = new AccountingProviderSwitchAssessment(Guid.NewGuid(), companyId, switchId, ownerId,
                "assessment", "assessment", 1, now); assessment.Complete(now); db.AccountingProviderSwitchAssessments.Add(assessment);
            var dataset = new AccountingProviderSwitchDataset(companyId, switchId, assessment.Id,
                AccountingProviderSwitchEndpointRoles.Source, AccountingProviderSwitchDatasetKeys.Currencies, now);
            dataset.Record(AccountingProviderSwitchDatasetAvailability.Available,
                AccountingProviderSwitchCapabilityLevels.Supported, 1, 10m, "SEK", null, "v1",
                new string('b', 64), "{\"source\":\"test\"}", null, null, now);
            db.AccountingProviderSwitchDatasets.Add(dataset);
            db.AccountingProviderSwitchStagedRecords.Add(new AccountingProviderSwitchStagedRecord(Guid.NewGuid(),
                companyId, switchId, assessment.Id, sw.Source, AccountingProviderSwitchStagingDatasets.Currencies,
                "SEK", "v1", now, new string('a', 64), new string('b', 64), "{\"code\":\"SEK\"}",
                "{\"document\":\"currency-list\"}", 10m, "SEK", AccountingProviderSwitchDispositions.Ready, now));
            await db.SaveChangesAsync();
            var approval = new RecordingApprovalService(db);
            var staging = new AccountingProviderSwitchStagingService(db, approval, new AuditEventWriter(db), new FixedTimeProvider(now));
            var service = new AccountingProviderSwitchRehearsalService(db, staging, approval,
                [new InternalLedgerProviderSwitchRehearsalAdapter(), new FortnoxProviderSwitchRehearsalAdapter(), new UnavailableProviderSwitchRehearsalAdapter()],
                new AuditEventWriter(db), new FixedTimeProvider(now), Options.Create(new AccountingProviderSwitchRehearsalWorkerOptions()));
            return new(connection, db, service, companyId, ownerId, switchId, assessment.Id, sw.Version, now);
        }
        public StartAccountingProviderSwitchRehearsalCommand Start(string key) => new(CompanyId, SwitchId,
            SwitchVersion, OwnerId, key, key);
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class RecordingApprovalService(VirtualCompanyDbContext db) : IApprovalRequestService
    {
        public Task<ApprovalRequestDto> CreateAsync(Guid companyId, CreateApprovalRequestCommand command, CancellationToken cancellationToken)
        {
            var entity = ApprovalRequest.CreateForTarget(Guid.NewGuid(), companyId,
                ApprovalTargetEntityTypeValues.Parse(command.TargetEntityType), command.TargetEntityId,
                command.RequestedByActorType, command.RequestedByActorId, command.ApprovalType,
                command.ThresholdContext ?? new Dictionary<string, JsonNode?> { ["reason"] = "plan" },
                command.RequiredRole, command.RequiredUserId, []);
            db.ApprovalRequests.Add(entity);
            return Task.FromResult(new ApprovalRequestDto(entity.Id, companyId, entity.TargetEntityType,
                entity.TargetEntityId, entity.RequestedByActorType, entity.RequestedByActorId,
                entity.ApprovalType, entity.RequiredRole, entity.RequiredUserId, entity.Status.ToStorageValue(),
                entity.ThresholdContext, [], null, null, null, "Cutover plan approval", "Accounting migration plan",
                [], null, entity.CreatedUtc));
        }
        public Task<IReadOnlyList<ApprovalRequestDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ApprovalRequestDto> GetAsync(Guid companyId, Guid approvalId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ApprovalDecisionResultDto> DecideAsync(Guid companyId, ApprovalDecisionCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class FixedTimeProvider(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero); }
}
