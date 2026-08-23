using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchCutoverWorkerOptions
{
    public const string SectionName = "AccountingProviderSwitchCutover";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 10;
    public int ClaimBatchSize { get; set; } = 4;
    public int LeaseSeconds { get; set; } = 120;
    public int MaximumAttempts { get; set; } = 4;
}

public sealed class AccountingProviderSwitchCutoverService : IAccountingProviderSwitchCutoverService,
    IAccountingProviderSwitchCutoverJobRunner
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IAccountingProviderSwitchRehearsalService _rehearsal;
    private readonly IAccountingProviderSwitchAdapterResolver _adapterResolver;
    private readonly IAccountingPostingService _posting;
    private readonly IFinanceIntegrationWriteCommandService _writes;
    private readonly IApprovalRequestService _approvals;
    private readonly IReadOnlyDictionary<string, IAccountingProviderSwitchFinalTransferExecutor> _executors;
    private readonly IAccountingProviderSwitchCutoverPolicy _policy;
    private readonly IAuditEventWriter _audit;
    private readonly AccountingOperationsTelemetry _telemetry;
    private readonly TimeProvider _time;
    private readonly AccountingProviderSwitchCutoverWorkerOptions _options;
    private readonly AccountingProviderSwitchMonitoringOptions _monitoringOptions;

    public AccountingProviderSwitchCutoverService(VirtualCompanyDbContext db,
        IAccountingProviderSwitchRehearsalService rehearsal,
        IAccountingProviderSwitchAdapterResolver adapterResolver, IAccountingPostingService posting,
        IFinanceIntegrationWriteCommandService writes, IApprovalRequestService approvals,
        IEnumerable<IAccountingProviderSwitchFinalTransferExecutor> executors,
        IAccountingProviderSwitchCutoverPolicy policy, IAuditEventWriter audit,
        AccountingOperationsTelemetry telemetry, TimeProvider time,
        IOptions<AccountingProviderSwitchCutoverWorkerOptions> options,
        IOptions<AccountingProviderSwitchMonitoringOptions> monitoringOptions)
    {
        _db = db; _rehearsal = rehearsal; _adapterResolver = adapterResolver; _posting = posting;
        _writes = writes; _approvals = approvals;
        _executors = executors.ToDictionary(x => x.ProviderKey, StringComparer.OrdinalIgnoreCase);
        _policy = policy; _audit = audit; _telemetry = telemetry; _time = time; _options = options.Value;
        _monitoringOptions = monitoringOptions.Value;
    }

    public async Task<AccountingProviderSwitchCutoverDto> ScheduleAsync(
        ScheduleAccountingProviderSwitchCutoverCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var key = Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 128);
        var existing = await Executions(command.CompanyId, command.SwitchId, false)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == key, cancellationToken);
        if (existing is not null) return await ToDtoAsync(existing, cancellationToken);
        var sw = await SwitchAsync(command.CompanyId, command.SwitchId, true, cancellationToken);
        EnsureVersion(sw.Version, command.ExpectedSwitchVersion, "The switch changed while cutover was scheduled.");
        var readiness = await _rehearsal.GetPlanReadinessAsync(new(command.CompanyId, command.SwitchId, command.PlanId), cancellationToken);
        if (!readiness.IsReady || readiness.Plan is null)
            throw Conflict(readiness.BlockingReasonCode ?? AccountingProviderSwitchCutoverReasonCodes.NotReady, readiness.Explanation);
        var plan = await _db.AccountingProviderSwitchCutoverPlans.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId && x.Id == command.PlanId, cancellationToken);
        Guid? preparationId = null;
        Guid? targetBatchId = null;
        if (sw.TargetKind == AccountingProviderEndpointKinds.Internal)
        {
            var preparation = await _db.AccountingProviderSwitchPreparations.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId && x.PlanId == command.PlanId)
                .OrderByDescending(x => x.RequestedUtc).FirstOrDefaultAsync(cancellationToken);
            if (preparation is null || preparation.Status != AccountingProviderSwitchPreparationStatuses.Completed || preparation.RejectedCandidateCount > 0)
                throw Conflict(AccountingProviderSwitchCutoverReasonCodes.NotReady,
                    "Native target preparation must complete without rejected candidates before cutover can be scheduled.");
            preparationId = preparation.Id;
        }
        else
        {
            var batch = await _db.AccountingProviderSwitchTargetTransferBatches.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId && x.PlanId == command.PlanId)
                .OrderByDescending(x => x.RequestedUtc).FirstOrDefaultAsync(cancellationToken);
            if (batch is null || batch.Status != AccountingProviderSwitchTargetTransferBatchStatuses.ReadyForCutover)
                throw Conflict(AccountingProviderSwitchCutoverReasonCodes.NotReady,
                    "The approved external target package must be ready before cutover can be scheduled.");
            targetBatchId = batch.Id;
        }
        if (sw.Status == AccountingProviderSwitchStatuses.PreparingTarget)
            sw.TransitionTo(AccountingProviderSwitchStatuses.RehearsalPassed, command.ActorUserId, command.CorrelationId, Now());
        if (sw.Status == AccountingProviderSwitchStatuses.RehearsalPassed)
            sw.TransitionTo(AccountingProviderSwitchStatuses.Scheduled, command.ActorUserId, command.CorrelationId, Now());
        if (sw.Status != AccountingProviderSwitchStatuses.Scheduled)
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.InvalidState,
                "The accounting-system switch is not ready to enter its scheduled cutover stage.");
        var scheduled = plan.FreezeStartsUtc > Now() ? plan.FreezeStartsUtc : Now();
        var execution = new AccountingProviderSwitchCutoverExecution(Guid.NewGuid(), command.CompanyId,
            command.SwitchId, command.PlanId, plan.PlanVersion, plan.PlanHash, preparationId, targetBatchId,
            command.ActorUserId, key, command.CorrelationId, scheduled, Now());
        _db.AccountingProviderSwitchCutoverExecutions.Add(execution);
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingProviderSwitchCutoverScheduled,
            execution.Id, AuditEventOutcomes.Requested, "The approved accounting cutover was scheduled at its freeze boundary.",
            command.CorrelationId, new() { ["switchId"] = command.SwitchId.ToString("D"), ["planHash"] = plan.PlanHash,
                ["scheduledUtc"] = scheduled.ToString("O") }, cancellationToken);
        await SaveAsync(cancellationToken);
        _telemetry.ProviderSwitchCutover(command.CompanyId, command.SwitchId, execution.Id,
            AccountingProviderSwitchCutoverStatuses.Queued, Direction(sw), TargetProvider(sw), command.CorrelationId);
        return await ToDtoAsync(execution, cancellationToken);
    }

    public async Task<AccountingProviderSwitchCutoverDto> StartFreezeAsync(
        StartAccountingProviderSwitchFreezeCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var execution = await ExecutionAsync(command.CompanyId, command.SwitchId, command.ExecutionId, true, cancellationToken);
        EnsureVersion(execution.Version, command.ExpectedExecutionVersion, "The cutover changed while freeze was requested.");
        if (Now() < execution.ScheduledUtc)
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.BoundaryNotReached,
                $"The approved freeze window begins at {execution.ScheduledUtc:O}.");
        try
        {
            await FreezeAsync(execution, command.ActorUserId, cancellationToken);
            return await ToDtoAsync(execution, cancellationToken);
        }
        catch (AccountingAuthorityException exception)
        {
            _db.ChangeTracker.Clear();
            execution = await ExecutionAsync(command.CompanyId, command.SwitchId, command.ExecutionId, true,
                cancellationToken);
            execution.Block(exception.ReasonCode, Safe(exception.Message), false, false,
                "Resolve the freeze prerequisite, recover the source state, and schedule a current approved plan.",
                Now());
            var blockedSwitch = await SwitchAsync(command.CompanyId, command.SwitchId, true, cancellationToken);
            if (!blockedSwitch.IsTerminal && blockedSwitch.Status != AccountingProviderSwitchStatuses.Blocked)
                blockedSwitch.Block(exception.ReasonCode, Safe(exception.Message), command.ActorUserId,
                    command.CorrelationId, Now());
            await AuditAsync(command.CompanyId, command.ActorUserId,
                AuditEventActions.AccountingProviderSwitchCutoverBlocked, execution.Id,
                AuditEventOutcomes.Blocked, Safe(exception.Message), command.CorrelationId,
                new() { ["switchId"] = command.SwitchId.ToString("D"), ["step"] = "freeze",
                    ["reasonCode"] = exception.ReasonCode, ["retryIsSafe"] = "false" }, cancellationToken);
            await SaveAsync(cancellationToken);
            _telemetry.ProviderSwitchBlocked(command.CompanyId, command.SwitchId, execution.Id,
                "freeze", exception.ReasonCode, false, false, command.CorrelationId);
            throw;
        }
    }

    public async Task<AccountingProviderSwitchCutoverDto> GetAsync(GetAccountingProviderSwitchCutoverQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty || query.SwitchId == Guid.Empty) throw new ArgumentException("Company and switch are required.");
        var executions = Executions(query.CompanyId, query.SwitchId, false);
        var execution = query.ExecutionId.HasValue
            ? await executions.SingleOrDefaultAsync(x => x.Id == query.ExecutionId.Value, cancellationToken)
            : await executions.OrderByDescending(x => x.RequestedUtc).FirstOrDefaultAsync(cancellationToken);
        return await ToDtoAsync(execution ?? throw Error(AccountingProviderSwitchCutoverReasonCodes.NotFound,
            "The cutover execution was not found for this company."), cancellationToken);
    }

    public async Task<AccountingProviderSwitchCutoverDto> RequestActivationApprovalAsync(
        RequestAccountingProviderSwitchActivationApprovalCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var execution = await ExecutionAsync(command.CompanyId, command.SwitchId, command.ExecutionId, true, cancellationToken);
        EnsureVersion(execution.Version, command.ExpectedExecutionVersion, "The cutover changed while activation approval was requested.");
        if (execution.Status != AccountingProviderSwitchCutoverStatuses.AwaitingActivationApproval || !execution.FinalSnapshotId.HasValue)
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.InvalidState,
                "Final transfer and reconciliation must complete before activation approval can be requested.");
        var existing = await _db.AccountingProviderSwitchActivationApprovals.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.ExecutionId == execution.Id, cancellationToken);
        if (existing is not null) return await ToDtoAsync(execution, cancellationToken);
        var sw = await SwitchAsync(command.CompanyId, command.SwitchId, false, cancellationToken);
        var snapshot = await SnapshotAsync(command.CompanyId, command.SwitchId, execution.Id, cancellationToken);
        var checks = await Checks(command.CompanyId, command.SwitchId, execution.Id).ToListAsync(cancellationToken);
        if (checks.Count == 0 || checks.Any(x => x.Result != AccountingProviderSwitchCutoverCheckResults.Passed))
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.FinalReconciliationFailed,
                "Every final reconciliation control must pass before activation approval is requested.");
        var reconciliationHash = ReconciliationHash(checks);
        var acknowledgementHashes = execution.TargetTransferBatchId.HasValue
            ? await _db.AccountingProviderSwitchTargetAcknowledgements.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId && x.BatchId == execution.TargetTransferBatchId)
                .OrderBy(x => x.ItemId).Select(x => x.AcknowledgementHash).ToListAsync(cancellationToken) : [];
        var approval = await _approvals.CreateAsync(command.CompanyId, new CreateApprovalRequestCommand(
            ApprovalTargetEntityType.AccountingProviderSwitchActivation.ToStorageValue(), execution.Id, "human",
            command.ActorUserId, "accounting_provider_switch_activation",
            new Dictionary<string, JsonNode?> { ["switchId"] = command.SwitchId, ["executionId"] = execution.Id,
                ["planHash"] = execution.PlanHash, ["finalSnapshotHash"] = snapshot.FinalSourceSnapshotHash,
                ["recordCount"] = snapshot.RecordCount, ["financialTotal"] = snapshot.FinancialTotal,
                ["deltaRecordCount"] = snapshot.DeltaRecordCount, ["reconciliationHash"] = reconciliationHash,
                ["providerAcknowledgementHashes"] = JsonSerializer.SerializeToNode(acknowledgementHashes),
                ["switchVersion"] = sw.Version }, RequiredRole: "finance_approver"), cancellationToken);
        _db.AccountingProviderSwitchActivationApprovals.Add(new(command.CompanyId, command.SwitchId,
            execution.Id, snapshot.Id, snapshot.FinalSourceSnapshotHash, reconciliationHash, sw.Version,
            approval.Id, command.ActorUserId, Now()));
        await AuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchActivationApprovalRequested, execution.Id,
            AuditEventOutcomes.Requested, "Separate activation approval was requested from immutable final reconciliation evidence.",
            command.CorrelationId, new() { ["switchId"] = command.SwitchId.ToString("D"),
                ["finalSnapshotHash"] = snapshot.FinalSourceSnapshotHash, ["reconciliationHash"] = reconciliationHash,
                ["approvalRequestId"] = approval.Id.ToString("D") }, cancellationToken);
        await SaveAsync(cancellationToken);
        return await ToDtoAsync(execution, cancellationToken);
    }

    public async Task<AccountingProviderSwitchCutoverDto> ActivateAsync(
        ActivateAccountingProviderSwitchCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
        var execution = await ExecutionAsync(command.CompanyId, command.SwitchId, command.ExecutionId, true, cancellationToken);
        EnsureVersion(execution.Version, command.ExpectedExecutionVersion, "The cutover changed while activation was requested.");
        var sw = await SwitchAsync(command.CompanyId, command.SwitchId, true, cancellationToken);
        var snapshot = await SnapshotAsync(command.CompanyId, command.SwitchId, execution.Id, cancellationToken);
        var binding = await _db.AccountingProviderSwitchActivationApprovals.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.ExecutionId == execution.Id, cancellationToken)
            ?? throw Conflict(AccountingProviderSwitchCutoverReasonCodes.ActivationApprovalRequired,
                "Separate activation approval is required before authority can change.");
        var approvalStatus = await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.Id == binding.ApprovalRequestId)
            .Select(x => (ApprovalRequestStatus?)x.Status).SingleOrDefaultAsync(cancellationToken);
        var checks = await Checks(command.CompanyId, command.SwitchId, execution.Id).ToListAsync(cancellationToken);
        if (approvalStatus != ApprovalRequestStatus.Approved || binding.FinalSnapshotId != snapshot.Id ||
            binding.FinalSnapshotHash != snapshot.FinalSourceSnapshotHash || binding.ReconciliationHash != ReconciliationHash(checks) ||
            binding.SwitchVersion != sw.Version)
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.ActivationApprovalStale,
                "The activation approval is missing, stale, expired, or no longer matches the final evidence.");
        if (checks.Count == 0 || checks.Any(x => x.Result != AccountingProviderSwitchCutoverCheckResults.Passed))
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.FinalReconciliationFailed,
                "Final reconciliation changed after approval. Reconcile and request activation approval again.");
        execution.BeginActivation();
        await SaveAsync(cancellationToken);
        if (sw.TargetKind == AccountingProviderEndpointKinds.Internal)
            await MaterializeNativeCandidatesAsync(sw, execution, snapshot, binding, command.ActorUserId,
                command.CorrelationId, cancellationToken);
        var authority = await _db.AccountingAuthorityPeriods.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == execution.AuthorityPeriodId,
                cancellationToken)
            ?? throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceAuthorityChanged,
                "The bounded migration authority period is missing.");
        if (!authority.IsCutoverReady || authority.TargetAuthority != TargetAuthority(sw))
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.FinalReconciliationFailed,
                "The authority period is not bound to the reconciled target.");
        authority.CompleteCutover(command.ActorUserId, Now());
        var configuration = await _db.AccountingConfigurations.IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
        configuration.SetAuthority(TargetAuthority(sw), command.ActorUserId, Now());
        if (!execution.TargetActivityRecorded) execution.RecordTargetActivity();
        sw.TransitionTo(AccountingProviderSwitchStatuses.TargetAuthoritative, command.ActorUserId,
            command.CorrelationId, Now());
        execution.CompleteActivation(Now());
        var monitoring = new AccountingProviderSwitchMonitoringRun(command.CompanyId, command.SwitchId,
            execution.Id, _monitoringOptions.DefaultWindowDays, sw.ResponsibleUserId, sw.ResponsibleAgentId,
            command.CorrelationId, Now(), Now());
        _db.AccountingProviderSwitchMonitoringRuns.Add(monitoring);
        sw.TransitionTo(AccountingProviderSwitchStatuses.Monitoring, command.ActorUserId, command.CorrelationId, Now());
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingProviderSwitchMonitoringStarted,
            monitoring.Id, AuditEventOutcomes.Started, "Post-activation accounting monitoring started in the activation transaction.",
            command.CorrelationId, new() { ["switchId"] = sw.Id.ToString("D"),
                ["windowDays"] = monitoring.WindowDays.ToString(), ["windowEndsUtc"] = monitoring.WindowEndsUtc.ToString("O") }, cancellationToken);
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingProviderSwitchActivated,
            execution.Id, AuditEventOutcomes.Succeeded,
            "Accounting authority and the provider-switch activation were committed atomically after final approval.",
            command.CorrelationId, new() { ["switchId"] = sw.Id.ToString("D"),
                ["authorityPeriodId"] = authority.Id.ToString("D"), ["targetAuthority"] = TargetAuthority(sw),
                ["targetProviderKey"] = sw.TargetProviderKey, ["finalSnapshotHash"] = snapshot.FinalSourceSnapshotHash,
                ["activationApprovalId"] = binding.ApprovalRequestId.ToString("D") }, cancellationToken);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _telemetry.ProviderSwitchCutover(command.CompanyId, command.SwitchId, execution.Id,
            AccountingProviderSwitchCutoverStatuses.Activated, Direction(sw), TargetProvider(sw), command.CorrelationId);
        return await ToDtoAsync(execution, cancellationToken);
        }
        catch (AccountingAuthorityException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            var blocked = await ExecutionAsync(command.CompanyId, command.SwitchId, command.ExecutionId, true,
                cancellationToken);
            if (blocked.Status is not AccountingProviderSwitchCutoverStatuses.Activated and
                not AccountingProviderSwitchCutoverStatuses.Cancelled and
                not AccountingProviderSwitchCutoverStatuses.Recovered and
                not AccountingProviderSwitchCutoverStatuses.CorrectiveCutoverRequired)
            {
                blocked.Block(exception.ReasonCode, Safe(exception.Message), false,
                    exception.ReasonCode == AccountingProviderSwitchCutoverReasonCodes.ProviderReconciliationRequired,
                    blocked.TargetActivityRecorded
                        ? "Reconcile target activity and perform a corrective cutover."
                        : "Resolve the final evidence or approval, then recover source authority and schedule a new cutover.",
                    Now());
                var blockedSwitch = await SwitchAsync(command.CompanyId, command.SwitchId, true, cancellationToken);
                if (!blockedSwitch.IsTerminal && blockedSwitch.Status != AccountingProviderSwitchStatuses.Blocked)
                    blockedSwitch.Block(exception.ReasonCode, Safe(exception.Message), command.ActorUserId,
                        command.CorrelationId, Now());
                await AuditAsync(command.CompanyId, command.ActorUserId,
                    AuditEventActions.AccountingProviderSwitchCutoverBlocked, blocked.Id,
                    AuditEventOutcomes.Blocked, Safe(exception.Message), command.CorrelationId,
                    new() { ["switchId"] = command.SwitchId.ToString("D"), ["step"] = "activation",
                        ["reasonCode"] = exception.ReasonCode, ["retryIsSafe"] = "false",
                        ["providerReconciliationRequired"] = blocked.ProviderReconciliationRequired.ToString() },
                    cancellationToken);
                await SaveAsync(cancellationToken);
                _telemetry.ProviderSwitchBlocked(command.CompanyId, command.SwitchId, blocked.Id,
                    "activation", exception.ReasonCode, false, blocked.ProviderReconciliationRequired,
                    command.CorrelationId);
            }
            throw;
        }
    }

    public async Task<AccountingProviderSwitchCutoverDto> CancelAsync(
        CancelAccountingProviderSwitchCutoverCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var execution = await ExecutionAsync(command.CompanyId, command.SwitchId, command.ExecutionId, true, cancellationToken);
        EnsureVersion(execution.Version, command.ExpectedExecutionVersion, "The cutover changed while cancellation was requested.");
        var sw = await SwitchAsync(command.CompanyId, command.SwitchId, true, cancellationToken);
        execution.Cancel(Now()); sw.Cancel(command.Reason, command.ActorUserId, command.CorrelationId, Now());
        await SaveAsync(cancellationToken); return await ToDtoAsync(execution, cancellationToken);
    }

    public async Task<AccountingProviderSwitchCutoverDto> ResumeAsync(
        ResumeAccountingProviderSwitchCutoverCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var execution = await ExecutionAsync(command.CompanyId, command.SwitchId, command.ExecutionId, true, cancellationToken);
        EnsureVersion(execution.Version, command.ExpectedExecutionVersion, "The cutover changed while retry was requested.");
        execution.Resume(Now()); await SaveAsync(cancellationToken); return await ToDtoAsync(execution, cancellationToken);
    }

    public async Task<AccountingProviderSwitchCutoverDto> RecoverAsync(
        RecoverAccountingProviderSwitchCutoverCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var execution = await ExecutionAsync(command.CompanyId, command.SwitchId, command.ExecutionId, true, cancellationToken);
        EnsureVersion(execution.Version, command.ExpectedExecutionVersion, "The cutover changed while recovery was requested.");
        var sw = await SwitchAsync(command.CompanyId, command.SwitchId, true, cancellationToken);
        if (execution.TargetActivityRecorded)
        {
            if (sw.Status != AccountingProviderSwitchStatuses.Blocked)
                sw.Block(AccountingProviderSwitchCutoverReasonCodes.RecoveryUnsafe,
                    "Target activity exists and must be reconciled through a corrective cutover.", command.ActorUserId,
                    command.CorrelationId, Now());
            execution.RecordRecovery(true, "Authority was not flipped back because target activity already exists.", Now());
            await AuditAsync(command.CompanyId, command.ActorUserId,
                AuditEventActions.AccountingProviderSwitchCorrectiveCutoverRequired, execution.Id,
                AuditEventOutcomes.Blocked, "Destructive rollback was refused after target activity.", command.CorrelationId,
                new() { ["switchId"] = sw.Id.ToString("D"), ["targetActivityRecorded"] = "true" }, cancellationToken);
        }
        else
        {
            if (execution.AuthorityPeriodId.HasValue)
            {
                var authority = await _db.AccountingAuthorityPeriods.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == execution.AuthorityPeriodId,
                        cancellationToken)
                    ?? throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceAuthorityChanged,
                        "The migration authority period cannot be recovered safely.");
                authority.RestoreSourceAuthority(SourceAuthority(sw), sw.SourceProviderKey, command.Reason,
                    command.ActorUserId, Now());
                var configuration = await _db.AccountingConfigurations.IgnoreQueryFilters()
                    .SingleAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
                configuration.SetAuthority(SourceAuthority(sw), command.ActorUserId, Now());
            }
            sw.Cancel(command.Reason, command.ActorUserId, command.CorrelationId, Now());
            execution.RecordRecovery(false, "The source authority was restored because no target activity was recorded.", Now());
            await AuditAsync(command.CompanyId, command.ActorUserId,
                AuditEventActions.AccountingProviderSwitchCutoverRecovered, execution.Id,
                AuditEventOutcomes.Succeeded, "The source authority was restored without deleting accounting history.",
                command.CorrelationId, new() { ["switchId"] = sw.Id.ToString("D"),
                    ["sourceAuthority"] = SourceAuthority(sw), ["sourceProviderKey"] = sw.SourceProviderKey }, cancellationToken);
        }
        await SaveAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return await ToDtoAsync(execution, cancellationToken);
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var now = Now();
        var ids = await _db.AccountingProviderSwitchCutoverExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == AccountingProviderSwitchCutoverStatuses.Queued ||
                         x.Status == AccountingProviderSwitchCutoverStatuses.Transferring ||
                         x.Status == AccountingProviderSwitchCutoverStatuses.Reconciling) &&
                        x.NextAttemptUtc <= now && (x.LeaseExpiresUtc == null || x.LeaseExpiresUtc <= now))
            .OrderBy(x => x.NextAttemptUtc).Select(x => x.Id).Take(_options.ClaimBatchSize).ToListAsync(cancellationToken);
        var handled = 0;
        foreach (var id in ids)
        {
            var execution = await _db.AccountingProviderSwitchCutoverExecutions.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == id, cancellationToken);
            var owner = $"cutover:{Environment.MachineName}:{Guid.NewGuid():N}";
            try
            {
                execution.Claim(owner, now.AddSeconds(_options.LeaseSeconds), now); await SaveAsync(cancellationToken);
                switch (execution.Status)
                {
                    case AccountingProviderSwitchCutoverStatuses.Queued:
                        await FreezeAsync(execution, execution.RequestedByUserId, cancellationToken); break;
                    case AccountingProviderSwitchCutoverStatuses.Transferring:
                        await TransferAsync(execution, cancellationToken); break;
                    case AccountingProviderSwitchCutoverStatuses.Reconciling:
                        await ReconcileAsync(execution, cancellationToken); break;
                }
                handled++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _db.ChangeTracker.Clear();
                execution = await _db.AccountingProviderSwitchCutoverExecutions.IgnoreQueryFilters()
                    .SingleAsync(x => x.Id == id, cancellationToken);
                var ambiguous = await HasAmbiguousProviderOutcomeAsync(execution, cancellationToken);
                var retryable = !ambiguous && execution.AttemptCount < _options.MaximumAttempts &&
                    exception is DbUpdateException or HttpRequestException or TimeoutException;
                execution.Block(ambiguous ? AccountingProviderSwitchCutoverReasonCodes.ProviderReconciliationRequired :
                        AccountingProviderSwitchCutoverReasonCodes.NotReady, Safe(exception.Message), retryable,
                    ambiguous, ambiguous ? "Reconcile the provider outcome before continuing."
                        : retryable ? "Retry the cutover after the temporary failure is resolved."
                        : "Review the failed cutover step and its persisted evidence.", Now());
                var sw = await SwitchAsync(execution.CompanyId, execution.SwitchId, true, cancellationToken);
                if (sw.Status != AccountingProviderSwitchStatuses.Blocked && !sw.IsTerminal)
                    sw.Block(execution.FailureCode!, execution.FailureSummary!, execution.RequestedByUserId,
                        execution.CorrelationId, Now());
                await AuditAsync(execution.CompanyId, execution.RequestedByUserId,
                    AuditEventActions.AccountingProviderSwitchCutoverBlocked, execution.Id,
                    AuditEventOutcomes.Blocked, execution.FailureSummary!, execution.CorrelationId,
                    new() { ["switchId"] = execution.SwitchId.ToString("D"), ["step"] = execution.CurrentStep,
                        ["retryIsSafe"] = retryable.ToString(), ["providerReconciliationRequired"] = ambiguous.ToString() }, cancellationToken);
                await SaveAsync(cancellationToken);
                _telemetry.ProviderSwitchBlocked(execution.CompanyId, execution.SwitchId, execution.Id,
                    execution.CurrentStep, execution.FailureCode!, retryable, ambiguous, execution.CorrelationId);
            }
        }
        return handled;
    }

    private async Task FreezeAsync(AccountingProviderSwitchCutoverExecution execution, Guid actor,
        CancellationToken cancellationToken)
    {
        var started = Now();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        execution.BeginFreeze(Now());
        var sw = await SwitchAsync(execution.CompanyId, execution.SwitchId, true, cancellationToken);
        if (sw.Status != AccountingProviderSwitchStatuses.Scheduled)
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.InvalidState, "The switch is no longer scheduled for freeze.");
        var plan = await _db.AccountingProviderSwitchCutoverPlans.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == execution.CompanyId && x.SwitchId == execution.SwitchId && x.Id == execution.PlanId, cancellationToken);
        var approvalBinding = await _db.AccountingProviderSwitchPlanApprovals.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == execution.CompanyId && x.PlanId == plan.Id && x.PlanHash == plan.PlanHash, cancellationToken);
        if (approvalBinding is null || !await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == execution.CompanyId && x.Id == approvalBinding.ApprovalRequestId &&
                    x.Status == ApprovalRequestStatus.Approved, cancellationToken))
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.PlanStale,
                "The approved cutover plan is no longer approved at freeze time.");
        await ValidatePreFreezeAsync(sw, execution, cancellationToken);
        var extractionStarted = Now();
        var records = await CurrentRecords(execution.CompanyId, execution.SwitchId).ToListAsync(cancellationToken);
        var hashes = await CurrentHashesAsync(sw, records, cancellationToken);
        if (hashes.Source != plan.SourceSnapshotHash)
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceChanged,
                "Source activity or approved migration evidence changed after rehearsal. Run a new bounded rehearsal and approve a new plan.");
        var firstInventory = await CaptureFinalSourceInventoryAsync(sw, execution, records, cancellationToken);
        var verify = await CurrentRecords(execution.CompanyId, execution.SwitchId).ToListAsync(cancellationToken);
        var verifyHashes = await CurrentHashesAsync(sw, verify, cancellationToken);
        var secondInventory = await CaptureFinalSourceInventoryAsync(sw, execution, verify, cancellationToken);
        if (hashes.Source != verifyHashes.Source || firstInventory.Hash != secondInventory.Hash)
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceChanged,
                "Source activity continued during final extraction. The freeze was not stable.");
        var approvedSnapshotHash = Hash($"{plan.SourceSnapshotHash}|{firstInventory.ExpectedHash}");
        var finalSnapshotHash = Hash($"{hashes.Source}|{secondInventory.Hash}");
        var snapshotJson = JsonSerializer.Serialize(new { schemaVersion = 1, execution.PlanHash,
            source = new { sw.SourceKind, sw.SourceProviderKey }, target = new { sw.TargetKind, sw.TargetProviderKey },
            sw.EffectiveFiscalPeriodId, sw.MigrationStrategy, records = records.Select(x => new
            { x.Id, x.Dataset, x.SourceIdentity, x.SourceVersion, x.SourceHash, x.NormalizedHash, x.MappingVersion,
                x.Disposition, x.FinancialAmount, x.Currency }), finalInventory = secondInventory.Datasets });
        var snapshot = new AccountingProviderSwitchFinalSnapshot(execution.CompanyId, execution.SwitchId,
            execution.Id, approvedSnapshotHash, finalSnapshotHash, hashes.Staging, hashes.Mapping, hashes.Gap,
            records.Count, records.Sum(x => x.FinancialAmount), 0, 0, snapshotJson, extractionStarted, Now());
        _db.AccountingProviderSwitchFinalSnapshots.Add(snapshot);
        var period = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == sw.CompanyId && x.Id == sw.EffectiveFiscalPeriodId, cancellationToken);
        var effectiveFrom = DateOnly.FromDateTime(period.StartUtc);
        var sourceAuthority = await _db.AccountingAuthorityPeriods.IgnoreQueryFilters()
            .Where(x => x.CompanyId == sw.CompanyId && x.EffectiveFrom <= effectiveFrom &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo >= effectiveFrom))
            .OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(cancellationToken)
            ?? throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceAuthorityChanged,
                "The source authority period no longer covers the approved boundary.");
        if (!MatchesSource(sw, sourceAuthority))
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceAuthorityChanged,
                "Accounting authority no longer matches the approved source system.");
        sourceAuthority.EndBefore(effectiveFrom, actor, Now());
        var migration = new AccountingAuthorityPeriod(Guid.NewGuid(), sw.CompanyId, effectiveFrom, null,
            AccountingAuthorityValues.Migration, sw.TargetProviderKey, actor,
            $"Provider switch {sw.Id:D}: {sw.Reason}", Now(), TargetAuthority(sw));
        _db.AccountingAuthorityPeriods.Add(migration);
        var configuration = await _db.AccountingConfigurations.IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == sw.CompanyId, cancellationToken);
        configuration.SetAuthority(AccountingAuthorityValues.Migration, actor, Now());
        sw.TransitionTo(AccountingProviderSwitchStatuses.SourceFrozen, actor, execution.CorrelationId, Now());
        execution.RecordFrozen(snapshot.Id, migration.Id, Now());
        await AuditAsync(sw.CompanyId, actor, AuditEventActions.AccountingProviderSwitchFinalSnapshotCaptured,
            snapshot.Id, AuditEventOutcomes.Succeeded, "An immutable final source snapshot was captured after a stable freeze.",
            execution.CorrelationId, new() { ["switchId"] = sw.Id.ToString("D"),
                ["finalSnapshotHash"] = snapshot.FinalSourceSnapshotHash, ["recordCount"] = snapshot.RecordCount.ToString(),
                ["deltaRecordCount"] = snapshot.DeltaRecordCount.ToString() }, cancellationToken);
        await AuditAsync(sw.CompanyId, actor, AuditEventActions.AccountingProviderSwitchSourceFrozen,
            migration.Id, AuditEventOutcomes.Succeeded, "Only the affected accounting period entered bounded migration authority.",
            execution.CorrelationId, new() { ["switchId"] = sw.Id.ToString("D"),
                ["effectiveFrom"] = effectiveFrom.ToString("yyyy-MM-dd"), ["sourceAuthority"] = SourceAuthority(sw),
                ["targetAuthority"] = TargetAuthority(sw) }, cancellationToken);
        await SaveAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        _telemetry.ProviderSwitchStageCompleted("freeze", Now() - started, Direction(sw), TargetProvider(sw));
    }

    private async Task TransferAsync(AccountingProviderSwitchCutoverExecution execution,
        CancellationToken cancellationToken)
    {
        var sw = await SwitchAsync(execution.CompanyId, execution.SwitchId, true, cancellationToken);
        if (sw.Status == AccountingProviderSwitchStatuses.SourceFrozen)
            sw.TransitionTo(AccountingProviderSwitchStatuses.Reconciling, execution.RequestedByUserId,
                execution.CorrelationId, Now());
        if (sw.TargetKind == AccountingProviderEndpointKinds.Internal)
        {
            var candidates = await _db.AccountingProviderSwitchNativeCandidates.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == execution.CompanyId && x.SwitchId == execution.SwitchId).ToListAsync(cancellationToken);
            if (candidates.Count == 0 || candidates.Any(x => x.Status != AccountingProviderSwitchNativeCandidateStatuses.Valid))
                throw Conflict(AccountingProviderSwitchCutoverReasonCodes.TransferIncomplete,
                    "Every native candidate must remain valid before final reconciliation.");
            execution.BeginReconciliation(Now()); await SaveAsync(cancellationToken); return;
        }
        if (!execution.TargetTransferBatchId.HasValue || string.IsNullOrWhiteSpace(sw.TargetProviderKey) ||
            !_executors.TryGetValue(sw.TargetProviderKey, out var executor))
            throw Error(AccountingProviderSwitchCutoverReasonCodes.TransferIncomplete,
                "No production final-transfer executor is registered for the target provider.");
        var connection = await _db.FinanceIntegrationConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == execution.CompanyId && x.ProviderKey == sw.TargetProviderKey &&
                x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw Conflict(AccountingProviderSwitchCutoverReasonCodes.ConnectionUnhealthy,
                "Reconnect the target provider before final transfer.");
        var items = await _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters()
            .Where(x => x.CompanyId == execution.CompanyId && x.SwitchId == execution.SwitchId &&
                x.BatchId == execution.TargetTransferBatchId &&
                x.OperationMode == AccountingProviderSwitchTargetOperationModes.FinalAuthoritative)
            .OrderBy(x => x.Dataset).ThenBy(x => x.SourceIdentity).ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            if (item.Status == AccountingProviderSwitchTargetTransferItemStatuses.Succeeded) continue;
            if (item.ReconciliationNeeded || item.Status == AccountingProviderSwitchTargetTransferItemStatuses.ReconciliationRequired)
                throw Conflict(AccountingProviderSwitchCutoverReasonCodes.ProviderReconciliationRequired,
                    "A provider outcome is ambiguous and must be reconciled before activation.");
            if (!item.WriteRequestId.HasValue)
            {
                if (item.CommandType is null || item.HttpMethod is null || item.Path is null ||
                    item.SanitizedPayloadJson is null || item.ProviderPayloadType is null)
                    throw Error(AccountingProviderSwitchCutoverReasonCodes.TransferIncomplete,
                        "The immutable final provider command is missing from the approved package.");
                var writeId = DeterministicGuid($"cutover-final|{item.StableIdentity}");
                var request = await _writes.RequestApprovalAsync(new(sw.TargetProviderKey, sw.CompanyId,
                    connection.Id, execution.RequestedByUserId, item.CommandType, item.HttpMethod, item.Path,
                    "Current company", item.SafePayloadSummary, item.PayloadHash,
                    new(item.SanitizedPayloadJson, item.ProviderPayloadType), writeId,
                    execution.CorrelationId), cancellationToken);
                item.AttachFinalApproval(writeId, request.ApprovalId ?? throw new InvalidOperationException(
                    "The final provider operation did not create its durable approval."), Now());
                await SaveAsync(cancellationToken);
                execution.WaitForTransfer(Now().AddSeconds(10),
                    "Approve the immutable final provider operation before cutover continues.");
                await SaveAsync(cancellationToken); return;
            }
            var approvalStatus = await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == execution.CompanyId && x.Id == item.ApprovalRequestId)
                .Select(x => (ApprovalRequestStatus?)x.Status).SingleOrDefaultAsync(cancellationToken);
            if (approvalStatus != ApprovalRequestStatus.Approved)
            {
                execution.WaitForTransfer(Now().AddSeconds(10),
                    "Final provider operations are waiting for approval."); await SaveAsync(cancellationToken); return;
            }
            var result = await executor.ExecuteApprovedAsync(execution.CompanyId, item.WriteRequestId.Value, cancellationToken);
            _db.ChangeTracker.Clear();
            execution = await ExecutionAsync(execution.CompanyId, execution.SwitchId, execution.Id, true, cancellationToken);
            var reloadedItem = await _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == execution.CompanyId && x.Id == item.Id, cancellationToken);
            if (reloadedItem.ReconciliationNeeded || result.IsAmbiguous)
                throw Conflict(AccountingProviderSwitchCutoverReasonCodes.ProviderReconciliationRequired, result.SafeSummary);
            if (!result.Succeeded)
            {
                execution.WaitForTransfer(Now().AddSeconds(result.IsRetryable ? 30 : 300), result.SafeSummary);
                await SaveAsync(cancellationToken); return;
            }
            if (!execution.TargetActivityRecorded) execution.RecordTargetActivity();
            await SaveAsync(cancellationToken);
        }
        execution.BeginReconciliation(Now());
        await AuditAsync(execution.CompanyId, execution.RequestedByUserId,
            AuditEventActions.AccountingProviderSwitchFinalTransferCompleted, execution.Id,
            AuditEventOutcomes.Succeeded, "All approved final target operations have provider acknowledgements.",
            execution.CorrelationId, new() { ["switchId"] = execution.SwitchId.ToString("D"),
                ["targetProviderKey"] = sw.TargetProviderKey }, cancellationToken);
        await SaveAsync(cancellationToken);
    }

    private async Task ReconcileAsync(AccountingProviderSwitchCutoverExecution execution,
        CancellationToken cancellationToken)
    {
        var sw = await SwitchAsync(execution.CompanyId, execution.SwitchId, true, cancellationToken);
        var snapshot = await SnapshotAsync(execution.CompanyId, execution.SwitchId, execution.Id, cancellationToken);
        var records = await CurrentRecords(execution.CompanyId, execution.SwitchId).ToListAsync(cancellationToken);
        var hashes = await CurrentHashesAsync(sw, records, cancellationToken);
        var inventory = await CaptureFinalSourceInventoryAsync(sw, execution, records, cancellationToken);
        var currentFinalSnapshotHash = Hash($"{hashes.Source}|{inventory.Hash}");
        var transferAmbiguous = await HasAmbiguousProviderOutcomeAsync(execution, cancellationToken);
        var transferComplete = sw.TargetKind == AccountingProviderEndpointKinds.Internal ||
            !await _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == execution.CompanyId && x.BatchId == execution.TargetTransferBatchId &&
                x.OperationMode == AccountingProviderSwitchTargetOperationModes.FinalAuthoritative &&
                x.Status != AccountingProviderSwitchTargetTransferItemStatuses.Succeeded, cancellationToken);
        var rehearsal = await _db.AccountingProviderSwitchRehearsals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == execution.CompanyId && x.SwitchId == execution.SwitchId &&
                x.Status == AccountingProviderSwitchRehearsalStatuses.Completed)
            .OrderByDescending(x => x.CompletedUtc).FirstOrDefaultAsync(cancellationToken);
        var rehearsalFailed = rehearsal is null || await _db.AccountingProviderSwitchReconciliationChecks.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == execution.CompanyId && x.SwitchId == execution.SwitchId &&
                x.RehearsalId == rehearsal.Id && x.Result == AccountingProviderSwitchReconciliationResults.Failed,
                cancellationToken);
        var checks = new[]
        {
            Check(execution, "final_snapshot_stable", currentFinalSnapshotHash == snapshot.FinalSourceSnapshotHash,
                AccountingProviderSwitchCutoverReasonCodes.SourceChanged,
                "The frozen source snapshot still matches staged and live source evidence.",
                new { snapshot.FinalSourceSnapshotHash, currentHash = currentFinalSnapshotHash, inventory.Hash }),
            Check(execution, "approved_plan_bound", snapshot.ApprovedSourceSnapshotHash == snapshot.FinalSourceSnapshotHash,
                AccountingProviderSwitchCutoverReasonCodes.PlanStale,
                "The final snapshot remains bound to the approved rehearsal snapshot.", new { snapshot.ApprovedSourceSnapshotHash, snapshot.FinalSourceSnapshotHash }),
            Check(execution, "final_transfer_confirmed", transferComplete && !transferAmbiguous,
                transferAmbiguous ? AccountingProviderSwitchCutoverReasonCodes.ProviderReconciliationRequired : AccountingProviderSwitchCutoverReasonCodes.TransferIncomplete,
                "Every final target operation has a durable confirmed outcome.", new { transferComplete, transferAmbiguous }),
            Check(execution, "financial_controls_current", !rehearsalFailed,
                AccountingProviderSwitchCutoverReasonCodes.FinalReconciliationFailed,
                "The approved deterministic financial controls remain successful.", new { rehearsalId = rehearsal?.Id }),
            Check(execution, "source_dispositions_complete", records.All(x => !AccountingProviderSwitchDispositions.BlocksProgress(x.Disposition)),
                AccountingProviderSwitchCutoverReasonCodes.FinalReconciliationFailed,
                "Every current source record retains an explicit non-blocking disposition.", new { recordCount = records.Count })
        };
        var previous = await Checks(execution.CompanyId, execution.SwitchId, execution.Id).ToListAsync(cancellationToken);
        if (previous.Count > 0) _db.AccountingProviderSwitchFinalChecks.RemoveRange(previous);
        _db.AccountingProviderSwitchFinalChecks.AddRange(checks);
        if (checks.Any(x => x.Result == AccountingProviderSwitchCutoverCheckResults.Failed))
        {
            await SaveAsync(cancellationToken);
            _telemetry.ProviderSwitchReconciled(execution.CompanyId, execution.SwitchId, execution.Id,
                false, checks.Length, execution.CorrelationId);
            throw Conflict(checks.First(x => x.Result == AccountingProviderSwitchCutoverCheckResults.Failed).ReasonCode,
                checks.First(x => x.Result == AccountingProviderSwitchCutoverCheckResults.Failed).Explanation);
        }
        var authority = await _db.AccountingAuthorityPeriods.IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == execution.CompanyId && x.Id == execution.AuthorityPeriodId, cancellationToken);
        authority.RecordCutoverValidation(true, true, true, 0,
            "Persisted switch final snapshot, transfer acknowledgements, and deterministic controls passed.",
            execution.RequestedByUserId, Now());
        execution.AwaitActivationApproval(Now());
        if (sw.Status == AccountingProviderSwitchStatuses.Reconciling)
            sw.TransitionTo(AccountingProviderSwitchStatuses.ActivationAwaitingApproval,
                execution.RequestedByUserId, execution.CorrelationId, Now());
        await AuditAsync(execution.CompanyId, execution.RequestedByUserId,
            AuditEventActions.AccountingProviderSwitchFinalReconciliationCompleted, execution.Id,
            AuditEventOutcomes.Succeeded, "Final reconciliation passed and activation now requires separate approval.",
            execution.CorrelationId, new() { ["switchId"] = execution.SwitchId.ToString("D"),
                ["finalSnapshotHash"] = snapshot.FinalSourceSnapshotHash,
                ["reconciliationHash"] = ReconciliationHash(checks) }, cancellationToken);
        await SaveAsync(cancellationToken);
        _telemetry.ProviderSwitchReconciled(execution.CompanyId, execution.SwitchId, execution.Id,
            true, checks.Length, execution.CorrelationId);
    }

    private async Task ValidatePreFreezeAsync(AccountingProviderSwitch sw,
        AccountingProviderSwitchCutoverExecution execution, CancellationToken cancellationToken)
    {
        var latestAssessment = await _db.AccountingProviderSwitchAssessments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id && x.Status == AccountingProviderSwitchAssessmentStatuses.Completed)
            .OrderByDescending(x => x.CompletedUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw Conflict(AccountingProviderSwitchCutoverReasonCodes.NotReady, "A completed source assessment is required at freeze time.");
        if (!latestAssessment.CompletedUtc.HasValue || Now() - latestAssessment.CompletedUtc.Value > TimeSpan.FromHours(24))
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceChanged,
                "The source assessment is older than 24 hours. Refresh it before freeze.");
        if (await _db.AccountingProviderSwitchGaps.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id && x.AssessmentId == latestAssessment.Id && x.IsBlocking,
            cancellationToken))
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.NotReady, "Blocking migration gaps remain at freeze time.");
        if (await _db.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id && x.IsCurrent &&
                x.ExtractionBatchId != latestAssessment.Id, cancellationToken))
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceChanged,
                "Current staged records are not bound to the latest completed source assessment. Refresh staging and rehearse again.");
        foreach (var endpoint in new[]
        {
            (Kind: sw.SourceKind, Provider: sw.SourceProviderKey, Role: AccountingProviderSwitchEndpointRoles.Source),
            (Kind: sw.TargetKind, Provider: sw.TargetProviderKey, Role: AccountingProviderSwitchEndpointRoles.Target)
        })
        {
            if (endpoint.Kind != AccountingProviderEndpointKinds.External) continue;
            var connection = await _db.FinanceIntegrationConnections.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == sw.CompanyId && x.ProviderKey == endpoint.Provider &&
                    x.Status == FinanceIntegrationConnectionStatuses.Connected)
                .OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(cancellationToken);
            if (connection is null)
                throw Conflict(AccountingProviderSwitchCutoverReasonCodes.ConnectionUnhealthy,
                    $"Reconnect {endpoint.Provider} before freeze.");
            var requiredScopes = await _db.AccountingProviderSwitchCapabilities.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id &&
                    x.AssessmentId == latestAssessment.Id && x.EndpointRole == endpoint.Role &&
                    x.RequiredScope != null)
                .Select(x => x.RequiredScope!).ToListAsync(cancellationToken);
            var granted = connection.Scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = requiredScopes.SelectMany(x => x.Split(',', StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(x => !granted.Contains(x)).ToArray();
            if (missing.Length > 0)
                throw Conflict(AccountingProviderSwitchCutoverReasonCodes.ConnectionUnhealthy,
                    $"Reconnect {endpoint.Provider} with the required {string.Join(", ", missing)} scope(s) before freeze.");
        }
        var period = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == sw.CompanyId && x.Id == sw.EffectiveFiscalPeriodId, cancellationToken);
        var boundary = DateOnly.FromDateTime(period.StartUtc);
        var pendingExports = await _db.AccountingProviderExports.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.LedgerEntry).AnyAsync(x => x.CompanyId == sw.CompanyId && x.LedgerEntry.PostingDate >= boundary &&
                x.Status != AccountingProviderExportStatuses.Exported && x.Status != AccountingProviderExportStatuses.Cancelled,
                cancellationToken);
        var ambiguousWrites = await _db.FinanceIntegrationWriteCommands.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == sw.CompanyId && x.Status == FinanceIntegrationWriteCommandRecordStatuses.Executing,
            cancellationToken);
        if (pendingExports || ambiguousWrites)
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.PendingWrites,
                "Pending or uncertain accounting-provider writes must be completed or reconciled before freeze.");
        if (execution.PlanHash.Length != 64) throw Conflict(AccountingProviderSwitchCutoverReasonCodes.PlanStale,
            "The immutable cutover plan binding is invalid.");
    }

    private async Task MaterializeNativeCandidatesAsync(AccountingProviderSwitch sw,
        AccountingProviderSwitchCutoverExecution execution, AccountingProviderSwitchFinalSnapshot snapshot,
        AccountingProviderSwitchActivationApproval binding, Guid actor, string correlation,
        CancellationToken cancellationToken)
    {
        var candidates = await _db.AccountingProviderSwitchNativeCandidates.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id &&
                x.Status == AccountingProviderSwitchNativeCandidateStatuses.Valid)
            .OrderBy(x => x.CandidateKind).ThenBy(x => x.SourceIdentity).ToListAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            var existing = await _db.AccountingProviderSwitchNativeMaterializations.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id &&
                    x.CandidateId == candidate.Id, cancellationToken);
            if (existing is not null)
            {
                if (existing.CandidateHash != candidate.SourceHash)
                    throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceChanged,
                        "A native candidate changed after it was materialized.");
                continue;
            }
            Guid targetId;
            string targetType;
            if (candidate.CandidateKind is AccountingProviderSwitchNativeCandidateKinds.OpeningJournal or
                AccountingProviderSwitchNativeCandidateKinds.HistoricalJournal)
            {
                var posted = await _posting.MaterializeProviderSwitchJournalAsync(new(sw.CompanyId, sw.Id,
                    execution.Id, candidate.Id, snapshot.FinalSourceSnapshotHash, binding.ApprovalRequestId,
                    actor, correlation), cancellationToken);
                targetId = posted.Journal.Id; targetType = "accounting_journal";
            }
            else
            {
                targetId = candidate.Id; targetType = $"prepared_{candidate.CandidateKind}";
            }
            _db.AccountingProviderSwitchNativeMaterializations.Add(new(sw.CompanyId, sw.Id, execution.Id,
                candidate.Id, candidate.SourceHash, targetId, targetType, Now()));
            await SaveAsync(cancellationToken);
        }
    }

    private AccountingProviderSwitchFinalCheck Check(AccountingProviderSwitchCutoverExecution execution,
        string key, bool passed, string reason, string explanation, object evidence) => new(execution.CompanyId,
        execution.SwitchId, execution.Id, key, passed ? AccountingProviderSwitchCutoverCheckResults.Passed :
            AccountingProviderSwitchCutoverCheckResults.Failed, passed ? "reconciliation_passed" : reason,
        explanation, JsonSerializer.Serialize(evidence), Now());

    private async Task<CurrentHashes> CurrentHashesAsync(AccountingProviderSwitch sw,
        IReadOnlyList<AccountingProviderSwitchStagedRecord> records, CancellationToken cancellationToken)
    {
        var mappings = await _db.AccountingProviderSwitchMappingDecisions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id && x.Status == AccountingProviderSwitchMappingStatuses.Approved)
            .OrderBy(x => x.MappingType).ThenBy(x => x.SourceKey).Select(x => x.BindingHash).ToListAsync(cancellationToken);
        var assessmentId = await _db.AccountingProviderSwitchAssessments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id && x.Status == AccountingProviderSwitchAssessmentStatuses.Completed)
            .OrderByDescending(x => x.CompletedUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        var gaps = assessmentId.HasValue ? await _db.AccountingProviderSwitchGaps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id && x.AssessmentId == assessmentId)
            .OrderBy(x => x.ReasonCode).Select(x => x.ReasonCode + ":" + x.EvidenceJson).ToListAsync(cancellationToken) : [];
        var staging = Hash(string.Join("|", records.Select(x => $"{x.Id:D}:{x.SourceVersion}:{x.SourceHash}:{x.NormalizedHash}:{x.Disposition}:{x.MappingVersion}")));
        var mapping = Hash(string.Join("|", mappings)); var gap = Hash(string.Join("|", gaps));
        return new(Hash($"{sw.Id:D}|{sw.MigrationStrategy}|{staging}|{mapping}|{gap}"), staging, mapping, gap);
    }

    private async Task<bool> HasAmbiguousProviderOutcomeAsync(AccountingProviderSwitchCutoverExecution execution,
        CancellationToken cancellationToken) => execution.TargetTransferBatchId.HasValue &&
        await _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == execution.CompanyId && x.SwitchId == execution.SwitchId &&
            x.BatchId == execution.TargetTransferBatchId && x.ReconciliationNeeded, cancellationToken);

    private async Task<FinalSourceInventory> CaptureFinalSourceInventoryAsync(AccountingProviderSwitch sw,
        AccountingProviderSwitchCutoverExecution execution,
        IReadOnlyList<AccountingProviderSwitchStagedRecord> records, CancellationToken cancellationToken)
    {
        var extractionIds = records.Select(x => x.ExtractionBatchId).Distinct().ToArray();
        if (extractionIds.Length > 1)
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceChanged,
                "Final staging must be bound to one completed source assessment before freeze.");
        var assessmentId = extractionIds.Length == 1 ? extractionIds[0] : await _db.AccountingProviderSwitchAssessments
            .IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id &&
                x.Status == AccountingProviderSwitchAssessmentStatuses.Completed)
            .OrderByDescending(x => x.CompletedUtc).Select(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (assessmentId == Guid.Empty)
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceChanged,
                "A completed source assessment is required for final extraction.");
        var expected = await _db.AccountingProviderSwitchDatasets.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id &&
                x.AssessmentId == assessmentId && x.EndpointRole == AccountingProviderSwitchEndpointRoles.Source)
            .OrderBy(x => x.DatasetKey).ToListAsync(cancellationToken);
        if (expected.Count == 0)
            throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceChanged,
                "The source assessment inventory required for final extraction is missing.");

        var endpoint = new AccountingProviderSwitchEndpointDto(sw.SourceKind, sw.SourceProviderKey,
            sw.SourceProviderKey ?? "Virtual Company");
        var adapter = _adapterResolver.GetRequired(sw.SourceKind, sw.SourceProviderKey);
        var captured = new List<FinalSourceInventoryDataset>(expected.Count);
        foreach (var dataset in expected)
        {
            string? cursor = null;
            var count = 0L;
            var total = 0m;
            var integrityHash = string.Empty;
            string? sourceVersion = null;
            var complete = false;
            for (var page = 0; page < 1000 && !complete; page++)
            {
                var result = await adapter.ExtractInventoryAsync(new(sw.CompanyId, sw.Id,
                    AccountingProviderSwitchEndpointRoles.Source, endpoint, dataset.DatasetKey, cursor, 500,
                    execution.CorrelationId), cancellationToken);
                integrityHash = count == 0 && integrityHash.Length == 0
                    ? result.IntegrityHash
                    : Hash($"{integrityHash}|{result.IntegrityHash}");
                count = checked(count + result.RecordCount);
                total += result.FinancialTotal;
                sourceVersion = result.SourceVersion ?? sourceVersion;
                complete = result.IsComplete;
                cursor = result.NextCursor;
                if (!complete && string.IsNullOrWhiteSpace(cursor))
                    throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceChanged,
                        $"Final extraction of '{dataset.DatasetKey}' returned no continuation cursor.");
            }
            if (!complete)
                throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceChanged,
                    $"Final extraction of '{dataset.DatasetKey}' exceeded the bounded page limit.");
            if (count != dataset.RecordCount || total != dataset.FinancialTotal ||
                integrityHash != dataset.IntegrityHash || sourceVersion != dataset.SourceVersion)
                throw Conflict(AccountingProviderSwitchCutoverReasonCodes.SourceChanged,
                    $"Source activity changed the '{dataset.DatasetKey}' inventory after the approved rehearsal.");
            captured.Add(new(dataset.DatasetKey, count, total, integrityHash, sourceVersion));
        }
        var expectedHash = Hash(string.Join("|", expected.Select(x =>
            $"{x.DatasetKey}:{x.RecordCount}:{x.FinancialTotal}:{x.IntegrityHash}:{x.SourceVersion}")));
        var capturedHash = Hash(string.Join("|", captured.Select(x =>
            $"{x.DatasetKey}:{x.RecordCount}:{x.FinancialTotal}:{x.IntegrityHash}:{x.SourceVersion}")));
        return new(expectedHash, capturedHash, captured);
    }

    private async Task<AccountingProviderSwitchCutoverDto> ToDtoAsync(
        AccountingProviderSwitchCutoverExecution execution, CancellationToken cancellationToken)
    {
        var snapshot = await _db.AccountingProviderSwitchFinalSnapshots.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == execution.CompanyId && x.SwitchId == execution.SwitchId &&
                x.ExecutionId == execution.Id, cancellationToken);
        var checks = await Checks(execution.CompanyId, execution.SwitchId, execution.Id).ToListAsync(cancellationToken);
        var binding = await _db.AccountingProviderSwitchActivationApprovals.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == execution.CompanyId && x.ExecutionId == execution.Id, cancellationToken);
        ApprovalRequestStatus? approvalStatus = binding is null ? null : await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == execution.CompanyId && x.Id == binding.ApprovalRequestId)
            .Select(x => (ApprovalRequestStatus?)x.Status).SingleOrDefaultAsync(cancellationToken);
        var approved = approvalStatus == ApprovalRequestStatus.Approved;
        return new(execution.Id, execution.CompanyId, execution.SwitchId, execution.PlanId,
            execution.PlanVersion, execution.PlanHash, execution.PreparationId, execution.TargetTransferBatchId,
            execution.AuthorityPeriodId, execution.Status, execution.CurrentStep, execution.TargetActivityRecorded,
            execution.RetryIsSafe, execution.ProviderReconciliationRequired, execution.FailureCode,
            execution.FailureSummary, execution.NextAction, execution.AttemptCount, execution.NextAttemptUtc,
            execution.ScheduledUtc, execution.RequestedUtc, execution.FreezeStartedUtc, execution.ReconciledUtc,
            execution.ActivatedUtc, execution.CompletedUtc, execution.Version,
            snapshot is null ? null : new(snapshot.Id, snapshot.ApprovedSourceSnapshotHash,
                snapshot.FinalSourceSnapshotHash, snapshot.RecordCount, snapshot.FinancialTotal,
                snapshot.DeltaRecordCount, snapshot.DeltaFinancialTotal, snapshot.ExtractionStartedUtc,
                snapshot.ExtractionCompletedUtc),
            checks.Select(x => new AccountingProviderSwitchFinalCheckDto(x.Id, x.CheckKey, x.Result,
                x.ReasonCode, x.Explanation, x.EvidenceJson, x.CalculatedUtc)).ToArray(),
            binding is null ? null : new(binding.ApprovalRequestId, approvalStatus?.ToStorageValue() ?? "missing",
                binding.FinalSnapshotHash, binding.ReconciliationHash, binding.SwitchVersion, binding.RequestedUtc),
            _policy.AllowedActions(execution.Status, execution.TargetActivityRecorded, execution.RetryIsSafe,
                execution.ProviderReconciliationRequired, approved));
    }

    private IQueryable<AccountingProviderSwitchCutoverExecution> Executions(Guid companyId, Guid switchId, bool tracking)
    { var query = _db.AccountingProviderSwitchCutoverExecutions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.SwitchId == switchId); return tracking ? query : query.AsNoTracking(); }
    private Task<AccountingProviderSwitchCutoverExecution> ExecutionAsync(Guid companyId, Guid switchId, Guid executionId, bool tracking, CancellationToken cancellationToken) =>
        Executions(companyId, switchId, tracking).SingleOrDefaultAsync(x => x.Id == executionId, cancellationToken).ContinueWith(x =>
            x.Result ?? throw Error(AccountingProviderSwitchCutoverReasonCodes.NotFound, "The cutover execution was not found for this company."), cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    private Task<AccountingProviderSwitch> SwitchAsync(Guid companyId, Guid switchId, bool tracking, CancellationToken cancellationToken)
    { var query = _db.AccountingProviderSwitches.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.Id == switchId); return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken).ContinueWith(x => x.Result ?? throw Error(AccountingProviderSwitchReasonCodes.NotFound, "The accounting-system switch was not found for this company."), cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default); }
    private IQueryable<AccountingProviderSwitchStagedRecord> CurrentRecords(Guid companyId, Guid switchId) =>
        _db.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.SwitchId == switchId && x.IsCurrent).OrderBy(x => x.Dataset).ThenBy(x => x.SourceIdentity);
    private IQueryable<AccountingProviderSwitchFinalCheck> Checks(Guid companyId, Guid switchId, Guid executionId) =>
        _db.AccountingProviderSwitchFinalChecks.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.SwitchId == switchId && x.ExecutionId == executionId).OrderBy(x => x.CheckKey);
    private Task<AccountingProviderSwitchFinalSnapshot> SnapshotAsync(Guid companyId, Guid switchId, Guid executionId, CancellationToken cancellationToken) =>
        _db.AccountingProviderSwitchFinalSnapshots.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.SwitchId == switchId && x.ExecutionId == executionId, cancellationToken);
    private Task AuditAsync(Guid companyId, Guid actor, string action, Guid targetId, string outcome,
        string summary, string correlation, Dictionary<string, string?> evidence, CancellationToken cancellationToken) =>
        _audit.WriteAsync(new(companyId, AuditActorTypes.User, actor, action, AuditTargetTypes.AccountingProviderSwitchCutover,
            targetId.ToString("D"), outcome, summary, ["accounting_provider_switch", "cutover", "authority"],
            evidence, correlation, Now()), cancellationToken);
    private async Task SaveAsync(CancellationToken cancellationToken)
    { try { await _db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { throw Conflict(AccountingProviderSwitchCutoverReasonCodes.ConcurrencyConflict, "Cutover state changed concurrently. Reload before continuing."); } }
    private static bool MatchesSource(AccountingProviderSwitch sw, AccountingAuthorityPeriod period) =>
        period.Authority == SourceAuthority(sw) && (sw.SourceKind == AccountingProviderEndpointKinds.Internal || string.Equals(period.ProviderKey, sw.SourceProviderKey, StringComparison.OrdinalIgnoreCase));
    private static string SourceAuthority(AccountingProviderSwitch sw) => sw.SourceKind == AccountingProviderEndpointKinds.Internal ? AccountingAuthorityValues.InternalLedger : AccountingAuthorityValues.ExternalProvider;
    private static string TargetAuthority(AccountingProviderSwitch sw) => sw.TargetKind == AccountingProviderEndpointKinds.Internal ? AccountingAuthorityValues.InternalLedger : AccountingAuthorityValues.ExternalProvider;
    private static string Direction(AccountingProviderSwitch sw) => $"{sw.SourceKind}_to_{sw.TargetKind}";
    private static string? TargetProvider(AccountingProviderSwitch sw) => sw.TargetKind == AccountingProviderEndpointKinds.External ? sw.TargetProviderKey : null;
    private static string ReconciliationHash(IEnumerable<AccountingProviderSwitchFinalCheck> checks) => Hash(string.Join("|", checks.OrderBy(x => x.CheckKey).Select(x => $"{x.CheckKey}:{x.Result}:{x.ReasonCode}:{x.EvidenceJson}")));
    private static void Validate(Guid companyId, Guid switchId, Guid actor, string correlation) { if (companyId == Guid.Empty || switchId == Guid.Empty || actor == Guid.Empty) throw new ArgumentException("Company, switch, and actor are required."); Required(correlation, nameof(correlation), 128); }
    private static void EnsureVersion(long current, long expected, string message) { if (current != expected) throw Conflict(AccountingProviderSwitchCutoverReasonCodes.ConcurrencyConflict, message); }
    private static string Required(string? value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static string Safe(string? value) => string.IsNullOrWhiteSpace(value) ? "The cutover stopped safely." : value.Trim().Length <= 1000 ? value.Trim() : value.Trim()[..1000];
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static Guid DeterministicGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16]);
    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
    private static AccountingAuthorityException Error(string code, string message) => new(code, message);
    private static AccountingAuthorityException Conflict(string code, string message) => new(code, message, true);
    private sealed record CurrentHashes(string Source, string Staging, string Mapping, string Gap);
    private sealed record FinalSourceInventory(string ExpectedHash, string Hash,
        IReadOnlyList<FinalSourceInventoryDataset> Datasets);
    private sealed record FinalSourceInventoryDataset(string DatasetKey, long RecordCount,
        decimal FinancialTotal, string IntegrityHash, string? SourceVersion);
}
