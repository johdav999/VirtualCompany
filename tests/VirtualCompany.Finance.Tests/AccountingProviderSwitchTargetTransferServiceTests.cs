using System.Text.Json.Nodes;
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

public sealed class AccountingProviderSwitchTargetTransferServiceTests
{
    [Fact]
    public async Task Approved_internal_to_fortnox_plan_builds_idempotent_approval_backed_package_without_changing_authority()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Service.StartAsync(new(fixture.CompanyId, fixture.SwitchId, fixture.Plan.Id,
            fixture.SwitchVersion, fixture.OwnerId, "target-package-1", "target-package-1"), CancellationToken.None);
        var duplicate = await fixture.Service.StartAsync(new(fixture.CompanyId, fixture.SwitchId, fixture.Plan.Id,
            fixture.SwitchVersion, fixture.OwnerId, "target-package-1", "target-package-1"), CancellationToken.None);

        Assert.Equal(first.Id, duplicate.Id);
        Assert.Equal(1, await fixture.Service.RunDueAsync(CancellationToken.None));
        Assert.Equal(0, await fixture.Service.RunDueAsync(CancellationToken.None));
        var result = await fixture.Service.GetAsync(new(fixture.CompanyId, fixture.SwitchId, first.Id), CancellationToken.None);

        Assert.Equal(AccountingProviderSwitchTargetTransferBatchStatuses.AwaitingApproval, result.Status);
        Assert.Equal(2, result.TotalItemCount);
        var account = Assert.Single(result.Items, x => x.Dataset == AccountingProviderSwitchStagingDatasets.Accounts);
        Assert.Equal(AccountingProviderSwitchTargetTransferItemStatuses.AwaitingApproval, account.Status);
        Assert.NotNull(account.ApprovalRequestId);
        Assert.NotNull(account.WriteRequestId);
        var opening = Assert.Single(result.Items, x => x.Dataset == AccountingProviderSwitchStagingDatasets.OpeningBalanceCandidates);
        Assert.Equal(AccountingProviderSwitchTargetTransferItemStatuses.HeldForCutover, opening.Status);
        Assert.Null(opening.WriteRequestId);
        Assert.Single(await fixture.Context.FinanceIntegrationWriteCommands.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await fixture.Context.ApprovalRequests.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(AccountingAuthorityValues.InternalLedger,
            (await fixture.Context.AccountingAuthorityPeriods.IgnoreQueryFilters().SingleAsync()).Authority);
    }

    [Fact]
    public async Task Ambiguous_preparatory_write_enters_reconciliation_and_is_not_requeued()
    {
        await using var fixture = await Fixture.CreateAsync();
        var started = await fixture.Service.StartAsync(new(fixture.CompanyId, fixture.SwitchId, fixture.Plan.Id,
            fixture.SwitchVersion, fixture.OwnerId, "ambiguous", "ambiguous"), CancellationToken.None);
        Assert.Equal(1, await fixture.Service.RunDueAsync(CancellationToken.None));
        var item = (await fixture.Service.GetAsync(new(fixture.CompanyId, fixture.SwitchId, started.Id), CancellationToken.None))
            .Items.Single(x => x.OperationMode == AccountingProviderSwitchTargetOperationModes.PreparatoryNonPosting);

        await fixture.Service.MarkExecutionStartedAsync(fixture.CompanyId, item.WriteRequestId!.Value, CancellationToken.None);
        await fixture.Service.MarkExecutionFailedAsync(fixture.CompanyId, item.WriteRequestId.Value,
            new TaskCanceledException("Provider timed out."), providerAcceptedRequest: false, CancellationToken.None);
        var result = await fixture.Service.GetAsync(new(fixture.CompanyId, fixture.SwitchId, started.Id), CancellationToken.None);

        Assert.Equal(AccountingProviderSwitchTargetTransferBatchStatuses.ReconciliationRequired, result.Status);
        var failed = result.Items.Single(x => x.Id == item.Id);
        Assert.True(failed.ReconciliationNeeded);
        Assert.Equal(AccountingProviderSwitchTargetTransferItemStatuses.ReconciliationRequired, failed.Status);
        Assert.Single(failed.Attempts);
        Assert.Equal("ambiguous", failed.Attempts[0].Outcome);
        Assert.Equal(0, await fixture.Service.RunDueAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cross_company_batch_is_not_visible()
    {
        await using var fixture = await Fixture.CreateAsync();
        var started = await fixture.Service.StartAsync(new(fixture.CompanyId, fixture.SwitchId, fixture.Plan.Id,
            fixture.SwitchVersion, fixture.OwnerId, "tenant", "tenant"), CancellationToken.None);

        var error = await Assert.ThrowsAsync<AccountingAuthorityException>(() => fixture.Service.GetAsync(
            new(Guid.NewGuid(), fixture.SwitchId, started.Id), CancellationToken.None));

        Assert.Equal(AccountingProviderSwitchTargetTransferReasonCodes.BatchNotFound, error.ReasonCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly DateTime Now = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext context,
            AccountingProviderSwitchTargetTransferService service, Guid companyId, Guid ownerId,
            Guid switchId, long switchVersion, AccountingProviderSwitchCutoverPlan plan)
        {
            _connection = connection; Context = context; Service = service; CompanyId = companyId;
            OwnerId = ownerId; SwitchId = switchId; SwitchVersion = switchVersion; Plan = plan;
        }
        public VirtualCompanyDbContext Context { get; }
        public AccountingProviderSwitchTargetTransferService Service { get; }
        public Guid CompanyId { get; } public Guid OwnerId { get; } public Guid SwitchId { get; }
        public long SwitchVersion { get; } public AccountingProviderSwitchCutoverPlan Plan { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var companyId = Guid.NewGuid(); var ownerId = Guid.NewGuid(); var switchId = Guid.NewGuid();
            var periodId = Guid.NewGuid(); var rehearsalId = Guid.NewGuid(); var extractionId = Guid.NewGuid();
            db.Companies.Add(new Company(companyId, "Outbound migration company"));
            db.Users.Add(new User(ownerId, "owner@example.com", "Owner", "test", ownerId.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, ownerId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.FiscalPeriods.Add(new FiscalPeriod(periodId, companyId, "September 2026", Now.AddDays(11), Now.AddDays(41)));
            db.AccountingAuthorityPeriods.Add(new AccountingAuthorityPeriod(Guid.NewGuid(), companyId,
                new DateOnly(2026, 1, 1), null, AccountingAuthorityValues.InternalLedger, null, ownerId,
                "Virtual Company remains authoritative during target preparation.", Now));
            var sw = new AccountingProviderSwitch(switchId, companyId, new("internal", null),
                new("external", "fortnox"), periodId, AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems,
                "Move accounting to Fortnox.", ownerId, null, ownerId, "switch", Now);
            sw.TransitionTo(AccountingProviderSwitchStatuses.Assessing, ownerId, "assess", Now);
            sw.TransitionTo(AccountingProviderSwitchStatuses.ReadyForPlanning, ownerId, "plan", Now);
            db.AccountingProviderSwitches.Add(sw);
            var rehearsal = new AccountingProviderSwitchRehearsal(rehearsalId, companyId, switchId, ownerId,
                "rehearsal", "rehearsal", Now);
            db.AccountingProviderSwitchRehearsals.Add(rehearsal);
            var plan = new AccountingProviderSwitchCutoverPlan(companyId, switchId, rehearsalId, 1,
                Hash('a'), Hash('b'), sw.MigrationStrategy, Now.AddHours(1), Now.AddHours(2),
                "Keep the source authoritative until activation.", $"[\"{ownerId:D}\"]", "{}", ownerId, Now);
            db.AccountingProviderSwitchCutoverPlans.Add(plan);
            var provider = new FinanceIntegrationConnection(Guid.NewGuid(), companyId, "fortnox",
                FinanceIntegrationConnectionStatuses.Connected, ownerId, Now);
            provider.Scopes.AddRange(["bookkeeping", "customer", "supplier", "project", "costcenter", "invoice", "supplierinvoice", "payment"]);
            db.FinanceIntegrationConnections.Add(provider);
            db.AccountingProviderSwitchStagedRecords.AddRange(
                new AccountingProviderSwitchStagedRecord(Guid.NewGuid(), companyId, switchId, extractionId, sw.Source,
                    AccountingProviderSwitchStagingDatasets.Accounts, "1930", "v1", Now, Hash('c'), Hash('d'),
                    """{"number":"1930","description":"Bank account"}""", "{}", 0m, "SEK",
                    AccountingProviderSwitchDispositions.Ready, Now),
                new AccountingProviderSwitchStagedRecord(Guid.NewGuid(), companyId, switchId, extractionId, sw.Source,
                    AccountingProviderSwitchStagingDatasets.OpeningBalanceCandidates, "opening-2026", "v1", Now,
                    Hash('e'), Hash('f'), """{"postingDate":"2026-09-01","lines":[]}""", "{}", 100m, "SEK",
                    AccountingProviderSwitchDispositions.Ready, Now));
            await db.SaveChangesAsync();

            var readiness = new ReadyRehearsalService(plan);
            var service = new AccountingProviderSwitchTargetTransferService(db, new CompleteStagingService(switchId),
                readiness, new RecordingWriteService(db, ownerId),
                [new FortnoxAccountingProviderSwitchTargetPreparationAdapter()], new AuditEventWriter(db),
                new FixedTimeProvider(Now), Options.Create(new AccountingProviderSwitchTargetTransferWorkerOptions
                    { ClaimBatchSize = 4, LeaseSeconds = 60, MaximumAttempts = 2 }));
            return new(connection, db, service, companyId, ownerId, switchId, sw.Version, plan);
        }
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class RecordingWriteService(VirtualCompanyDbContext db, Guid ownerId) : IFinanceIntegrationWriteCommandService
    {
        public string ProviderKey => "fortnox";
        public async Task<FinanceIntegrationWriteResult> RequestApprovalAsync(FinanceIntegrationWriteCommand request,
            CancellationToken cancellationToken)
        {
            var existing = await db.FinanceIntegrationWriteCommands.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == request.CompanyId && x.Id == request.WriteRequestId, cancellationToken);
            if (existing is not null) return new("fortnox", existing.Id, existing.ApprovalId, existing.Status, "Existing approval.", false);
            var command = new FinanceIntegrationWriteCommandRecord(request.WriteRequestId, request.CompanyId,
                request.ConnectionId, request.ActorUserId, request.CommandType, request.HttpMethod, request.Path,
                request.TargetCompany, request.PayloadSummary, request.PayloadHash, request.Payload.SanitizedJson,
                request.CorrelationId, DateTime.UtcNow);
            db.FinanceIntegrationWriteCommands.Add(command);
            var approval = ApprovalRequest.CreateForTarget(Guid.NewGuid(), request.CompanyId,
                ApprovalTargetEntityType.FinanceIntegrationWrite, command.Id, "human", ownerId,
                "finance_integration_write", new Dictionary<string, JsonNode?> { ["payloadHash"] = request.PayloadHash },
                "finance_approver", null, []);
            db.ApprovalRequests.Add(approval); command.AttachApproval(approval.Id, DateTime.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            return new("fortnox", command.Id, approval.Id, command.Status, "Approval requested.", false);
        }
        public Task<FinanceIntegrationWriteResult> EnsureApprovedForExecutionAsync(FinanceIntegrationWriteCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RecordExecutionSucceededAsync(FinanceIntegrationWriteCommand command, object? responsePayload, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RecordExecutionFailedAsync(FinanceIntegrationWriteCommand command, Exception exception, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CompleteStagingService(Guid switchId) : IAccountingProviderSwitchStagingService
    {
        public Task<AccountingProviderSwitchCompletenessDto> GetCompletenessAsync(GetAccountingProviderSwitchCompletenessQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new AccountingProviderSwitchCompletenessDto(switchId, true, 2, 2, 2, 0, [], [], "Staging is complete."));
        public Task<AccountingProviderSwitchStagedRecordDto> StageAsync(StageAccountingProviderSwitchRecordCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountingProviderSwitchStagedRecordDto>> ListAsync(ListAccountingProviderSwitchStagedRecordsQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AccountingProviderSwitchMappingDecisionDto>> ListMappingsAsync(ListAccountingProviderSwitchMappingsQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AccountingProviderSwitchMappingDecisionDto>>([]);
        public Task<AccountingProviderSwitchMappingDecisionDto> PreviewMappingAsync(PreviewAccountingProviderSwitchMappingCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchMappingDecisionDto> RequestMappingApprovalAsync(RequestAccountingProviderSwitchMappingApprovalCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchStagedRecordDto> ResolveDispositionAsync(ResolveAccountingProviderSwitchDispositionCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ReadyRehearsalService(AccountingProviderSwitchCutoverPlan plan) : IAccountingProviderSwitchRehearsalService
    {
        public Task<AccountingProviderSwitchPlanReadinessDto> GetPlanReadinessAsync(GetAccountingProviderSwitchPlanReadinessQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new AccountingProviderSwitchPlanReadinessDto(query.SwitchId, new(plan.Id, plan.CompanyId,
                plan.SwitchId, plan.RehearsalId, plan.PlanVersion, plan.PlanHash, plan.SourceSnapshotHash, plan.Strategy,
                plan.FreezeStartsUtc, plan.FreezeEndsUtc, plan.RecoveryBoundary, plan.ParticipantsJson, plan.SnapshotJson,
                plan.GeneratedByUserId, plan.GeneratedUtc, Guid.NewGuid(), "approved", true, true), true, null,
                "The immutable plan is approved and current."));
        public Task<AccountingProviderSwitchRehearsalDto> StartAsync(StartAccountingProviderSwitchRehearsalCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchRehearsalDto> ReplayAsync(ReplayAccountingProviderSwitchRehearsalCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchRehearsalDto> GetAsync(GetAccountingProviderSwitchRehearsalQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchManualEvidenceDto> RecordManualEvidenceAsync(RecordAccountingProviderSwitchManualEvidenceCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchCutoverPlanDto> GeneratePlanAsync(GenerateAccountingProviderSwitchCutoverPlanCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchCutoverPlanDto> RequestPlanApprovalAsync(RequestAccountingProviderSwitchPlanApprovalCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
    private static string Hash(char value) => new(value, 64);
}
