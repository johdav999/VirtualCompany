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

public sealed class AccountingProviderSwitchMonitoringOptions
{
    public const string SectionName = "AccountingProviderSwitchMonitoring";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
    public int ClaimBatchSize { get; set; } = 4;
    public int LeaseSeconds { get; set; } = 120;
    public int MaximumAttempts { get; set; } = 4;
    public int DefaultWindowDays { get; set; } = 14;
    public int CheckIntervalHours { get; set; } = 24;
    public int SyncStaleHours { get; set; } = 24;
    public int StaleFreezeHours { get; set; } = 4;
}

public sealed class AccountingProviderSwitchMonitoringService : IAccountingProviderSwitchMonitoringService,
    IAccountingProviderSwitchMonitoringJobRunner
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IApprovalRequestService _approvals;
    private readonly IAuditEventWriter _audit;
    private readonly AccountingOperationsTelemetry _telemetry;
    private readonly AccountingProviderSwitchMonitoringOptions _options;
    private readonly TimeProvider _time;

    public AccountingProviderSwitchMonitoringService(VirtualCompanyDbContext db, IApprovalRequestService approvals,
        IAuditEventWriter audit, AccountingOperationsTelemetry telemetry,
        IOptions<AccountingProviderSwitchMonitoringOptions> options, TimeProvider time)
    {
        _db = db; _approvals = approvals; _audit = audit; _telemetry = telemetry;
        _options = options.Value; _time = time;
    }

    public async Task<AccountingProviderSwitchMonitoringDto> GetAsync(GetAccountingProviderSwitchMonitoringQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId); ValidateSwitch(query.SwitchId);
        var run = await Runs(query.CompanyId, query.SwitchId, false).SingleOrDefaultAsync(cancellationToken)
            ?? throw Error(AccountingProviderSwitchMonitoringReasonCodes.NotFound,
                "Post-activation monitoring has not started for this accounting migration.");
        return await ToDtoAsync(run, cancellationToken);
    }

    public async Task<AccountingProviderSwitchOperationsDto> GetOperationsAsync(
        GetAccountingProviderSwitchOperationsQuery query, CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId); var now = Now(); var stuckBefore = now.AddHours(-2);
        var stuck = await _db.AccountingProviderSwitchAssessments.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == query.CompanyId && x.Status == AccountingProviderSwitchAssessmentStatuses.Running && x.UpdatedUtc < stuckBefore, cancellationToken)
            + await _db.AccountingProviderSwitchRehearsals.IgnoreQueryFilters().AsNoTracking()
                .LongCountAsync(x => x.CompanyId == query.CompanyId && x.Status == AccountingProviderSwitchRehearsalStatuses.Running && x.StartedUtc < stuckBefore, cancellationToken)
            + await _db.AccountingProviderSwitchPreparations.IgnoreQueryFilters().AsNoTracking()
                .LongCountAsync(x => x.CompanyId == query.CompanyId && x.Status == AccountingProviderSwitchPreparationStatuses.Running && x.StartedUtc < stuckBefore, cancellationToken)
            + await _db.AccountingProviderSwitchTargetTransferBatches.IgnoreQueryFilters().AsNoTracking()
                .LongCountAsync(x => x.CompanyId == query.CompanyId && x.Status == AccountingProviderSwitchTargetTransferBatchStatuses.Building && x.StartedUtc < stuckBefore, cancellationToken)
            + await _db.AccountingProviderSwitchMonitoringRuns.IgnoreQueryFilters().AsNoTracking()
                .LongCountAsync(x => x.CompanyId == query.CompanyId && x.LeaseExpiresUtc < now, cancellationToken);
        var expiredApprovals = await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == query.CompanyId && x.Status == ApprovalRequestStatus.Expired &&
                (x.TargetEntityType == ApprovalTargetEntityType.AccountingProviderSwitchCutoverPlan.ToStorageValue() ||
                 x.TargetEntityType == ApprovalTargetEntityType.AccountingProviderSwitchActivation.ToStorageValue() ||
                 x.TargetEntityType == ApprovalTargetEntityType.AccountingProviderSwitchClosure.ToStorageValue()), cancellationToken);
        var staleFreezes = await _db.AccountingProviderSwitchCutoverExecutions.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == query.CompanyId && x.Status == AccountingProviderSwitchCutoverStatuses.Freezing &&
                x.FreezeStartedUtc < now.AddHours(-_options.StaleFreezeHours), cancellationToken);
        var exhausted = await _db.AccountingProviderSwitchMonitoringRuns.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == query.CompanyId && x.Status == AccountingProviderSwitchMonitoringStatuses.Failed, cancellationToken)
            + await _db.AccountingProviderSwitchCutoverExecutions.IgnoreQueryFilters().AsNoTracking()
                .LongCountAsync(x => x.CompanyId == query.CompanyId && x.Status == AccountingProviderSwitchCutoverStatuses.Blocked && x.AttemptCount >= _options.MaximumAttempts, cancellationToken);
        var ambiguous = await _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == query.CompanyId && x.ReconciliationNeeded, cancellationToken)
            + await _db.AccountingProviderExports.IgnoreQueryFilters().AsNoTracking()
                .LongCountAsync(x => x.CompanyId == query.CompanyId && x.Status == AccountingProviderExportStatuses.ReconciliationRequired, cancellationToken);
        var unreconciled = await _db.AccountingProviderSwitchMonitoringIncidents.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == query.CompanyId && x.Status == AccountingProviderSwitchMonitoringIncidentStatuses.Open &&
                (x.CheckKey == AccountingProviderSwitchMonitoringCheckKeys.BankReconciliation || x.CheckKey == AccountingProviderSwitchMonitoringCheckKeys.FinancialControls), cancellationToken);
        var issues = new List<AccountingProviderSwitchOperationIssueDto>();
        AddIssue(issues, "stuck_workflows", stuck, "danger", "Migration work has stopped making progress.", "Inspect the worker lease and resume safely.");
        AddIssue(issues, "expired_approvals", expiredApprovals, "warning", "Migration approvals expired before execution.", "Request a fresh approval from current evidence.");
        AddIssue(issues, "stale_freezes", staleFreezes, "danger", "A source freeze is older than the safe operating window.", "Review authority and recover or resume the cutover.");
        AddIssue(issues, "exhausted_retries", exhausted, "danger", "Automatic retries are exhausted.", "Resolve the failure before an operator retry.");
        AddIssue(issues, "ambiguous_outcomes", ambiguous, "danger", "Provider outcomes still need reconciliation.", "Confirm the provider result before retrying.");
        AddIssue(issues, "unreconciled_totals", unreconciled, "danger", "Financial controls still differ.", "Investigate and retain reconciliation evidence.");
        return new(query.CompanyId, now, stuck, expiredApprovals, staleFreezes, exhausted, ambiguous, unreconciled, issues);
    }

    public async Task<AccountingProviderSwitchMonitoringDto> RunNowAsync(
        RunAccountingProviderSwitchMonitoringCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var run = await RunAsync(command.CompanyId, command.SwitchId, true, cancellationToken);
        EnsureVersion(run.Version, command.ExpectedVersion); run.QueueNow(Now()); await SaveAsync(cancellationToken);
        await RunOneAsync(run.CompanyId, run.Id, cancellationToken);
        return await GetAsync(new(command.CompanyId, command.SwitchId), cancellationToken);
    }

    public async Task<AccountingProviderSwitchMonitoringDto> RetryAsync(
        RetryAccountingProviderSwitchMonitoringCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var run = await RunAsync(command.CompanyId, command.SwitchId, true, cancellationToken);
        EnsureVersion(run.Version, command.ExpectedVersion); run.Retry(Now());
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingProviderSwitchMonitoringRetryRequested,
            run.Id, AuditEventOutcomes.Requested, "Post-activation monitoring was queued for a safe operator retry.", command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken); return await ToDtoAsync(run, cancellationToken);
    }

    public async Task<AccountingProviderSwitchMonitoringDto> AcceptExceptionAsync(
        AcceptAccountingProviderSwitchMonitoringExceptionCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var incident = await _db.AccountingProviderSwitchMonitoringIncidents.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId && x.Id == command.IncidentId, cancellationToken)
            ?? throw Error(AccountingProviderSwitchMonitoringReasonCodes.NotFound, "The monitoring issue was not found for this company.");
        EnsureVersion(incident.Version, command.ExpectedIncidentVersion);
        incident.AcceptException(command.ActorUserId, command.Explanation, command.Scope, command.FinancialImpact,
            command.EvidenceReference, Now());
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingProviderSwitchMonitoringExceptionAccepted,
            incident.Id, AuditEventOutcomes.Succeeded, "A non-blocking monitoring exception was accepted with retained evidence.",
            command.CorrelationId, cancellationToken, new() { ["scope"] = command.Scope, ["financialImpact"] = command.FinancialImpact.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await SaveAsync(cancellationToken); return await GetAsync(new(command.CompanyId, command.SwitchId), cancellationToken);
    }

    public async Task<AccountingProviderSwitchMonitoringDto> RequestClosureAsync(
        RequestAccountingProviderSwitchMonitoringClosureCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var run = await RunAsync(command.CompanyId, command.SwitchId, true, cancellationToken); EnsureVersion(run.Version, command.ExpectedVersion);
        var now = Now();
        if (now < run.WindowEndsUtc) throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.WindowIncomplete, "The configured monitoring window has not finished.");
        if (run.NextRunUtc <= now || (run.LeaseOwner is not null && run.LeaseExpiresUtc > now))
            throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.CheckPending,
                "The queued or in-progress monitoring check must finish before closure can be requested.");
        if (run.LastSuccessfulCheckUtc is null || run.LastSuccessfulCheckUtc < run.WindowEndsUtc ||
            run.Status == AccountingProviderSwitchMonitoringStatuses.Failed || run.FailureCode is not null)
            throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.FinalCheckRequired,
                "A successful monitoring check at or after the end of the configured window is required before closure.");
        var openIncident = await Incidents(run).AnyAsync(
            x => x.Status == AccountingProviderSwitchMonitoringIncidentStatuses.Open, cancellationToken);
        if (openIncident) throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.BlockingIncident,
            "Resolve blocking issues and accept or resolve every non-blocking exception before requesting closure.");
        var hash = await ClosureHashAsync(run, cancellationToken);
        if (run.ClosureApprovalRequestId.HasValue && run.ClosureEvidenceHash == hash)
        {
            var existingStatus = await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.Id == run.ClosureApprovalRequestId)
                .Select(x => (ApprovalRequestStatus?)x.Status).SingleOrDefaultAsync(cancellationToken);
            if (existingStatus is ApprovalRequestStatus.Pending or ApprovalRequestStatus.Approved)
                return await ToDtoAsync(run, cancellationToken);
        }
        var approval = await _approvals.CreateAsync(command.CompanyId, new CreateApprovalRequestCommand(
            ApprovalTargetEntityType.AccountingProviderSwitchClosure.ToStorageValue(), run.Id, "human", command.ActorUserId,
            "accounting_provider_switch_closure", new Dictionary<string, JsonNode?> { ["switchId"] = command.SwitchId,
                ["monitoringRunId"] = run.Id, ["closureEvidenceHash"] = hash, ["windowEndsUtc"] = run.WindowEndsUtc },
            RequiredRole: "finance_approver"), cancellationToken);
        run.AwaitClosureApproval(approval.Id, hash);
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingProviderSwitchMonitoringClosureRequested,
            run.Id, AuditEventOutcomes.Requested, "Migration closure approval was requested from current monitoring evidence.", command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken); return await ToDtoAsync(run, cancellationToken);
    }

    public async Task<AccountingProviderSwitchMonitoringDto> CloseAsync(CloseAccountingProviderSwitchMonitoringCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var run = await RunAsync(command.CompanyId, command.SwitchId, true, cancellationToken); EnsureVersion(run.Version, command.ExpectedVersion);
        var now = Now();
        if (run.NextRunUtc <= now || (run.LeaseOwner is not null && run.LeaseExpiresUtc > now))
            throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.CheckPending,
                "The queued or in-progress monitoring check must finish before closure can be completed.");
        if (now < run.WindowEndsUtc || run.LastSuccessfulCheckUtc is null ||
            run.LastSuccessfulCheckUtc < run.WindowEndsUtc || run.Status == AccountingProviderSwitchMonitoringStatuses.Failed ||
            run.FailureCode is not null)
            throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.FinalCheckRequired,
                "Closure requires a successful monitoring check at or after the end of the configured window.");
        if (!run.ClosureApprovalRequestId.HasValue || run.ClosureEvidenceHash != await ClosureHashAsync(run, cancellationToken))
            throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.ApprovalStale, "Closure evidence changed after approval was requested.");
        var approved = await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == command.CompanyId &&
            x.Id == run.ClosureApprovalRequestId && x.Status == ApprovalRequestStatus.Approved, cancellationToken);
        if (!approved) throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.ApprovalRequired, "Approved migration closure is required.");
        if (await Incidents(run).AnyAsync(x => x.Status == AccountingProviderSwitchMonitoringIncidentStatuses.Open, cancellationToken))
            throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.BlockingIncident, "Resolve or accept every monitoring issue before closure.");
        var sw = await _db.AccountingProviderSwitches.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == command.SwitchId, cancellationToken);
        if (sw.Status != AccountingProviderSwitchStatuses.Monitoring) throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.InvalidState, "The switch is not in post-activation monitoring.");
        run.Close(command.ActorUserId, "monitoring_passed", command.Summary, Now());
        sw.TransitionTo(AccountingProviderSwitchStatuses.Completed, command.ActorUserId, command.CorrelationId, Now());
        await AuditAsync(command.CompanyId, command.ActorUserId, AuditEventActions.AccountingProviderSwitchMonitoringClosed,
            run.Id, AuditEventOutcomes.Succeeded, "The migration closed after its monitoring window and approval.", command.CorrelationId, cancellationToken);
        await SaveAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return await ToDtoAsync(run, cancellationToken);
    }

    public async Task<AccountingProviderSwitchMonitoringDto> CreateCorrectiveCutoverAsync(
        CreateCorrectiveAccountingProviderSwitchCommand command, CancellationToken cancellationToken)
    {
        ValidateCommand(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var run = await RunAsync(command.CompanyId, command.SwitchId, true, cancellationToken);
        EnsureVersion(run.Version, command.ExpectedVersion);
        if (run.Status is AccountingProviderSwitchMonitoringStatuses.Closed or AccountingProviderSwitchMonitoringStatuses.ClosureAwaitingApproval ||
            !await Incidents(run).AnyAsync(x => x.Status == AccountingProviderSwitchMonitoringIncidentStatuses.Open && x.IsBlocking, cancellationToken))
            throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.CorrectiveCutoverUnavailable,
                "A corrective cutover is available only for an activated migration with an unresolved blocking discrepancy.");

        var current = await _db.AccountingProviderSwitches.IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == command.SwitchId, cancellationToken);
        if (current.Status != AccountingProviderSwitchStatuses.Monitoring)
            throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.CorrectiveCutoverUnavailable,
                "The accounting-system switch is not in post-activation monitoring.");
        var period = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.EffectiveFiscalPeriodId, cancellationToken)
            ?? throw Error(AccountingProviderSwitchReasonCodes.FiscalPeriodNotFound, "Select an existing accounting period for this company.");
        var effectiveFrom = DateOnly.FromDateTime(period.StartUtc);
        if (period.StartUtc.TimeOfDay != TimeSpan.Zero || period.EndUtc.TimeOfDay != TimeSpan.Zero || effectiveFrom.Day != 1 ||
            DateOnly.FromDateTime(period.EndUtc) != effectiveFrom.AddMonths(1))
            throw Error(AccountingProviderSwitchReasonCodes.MonthlyBoundaryRequired,
                "A corrective cutover must start at an existing monthly accounting boundary.");
        if (effectiveFrom <= DateOnly.FromDateTime(Now()))
            throw Error(AccountingProviderSwitchReasonCodes.FutureBoundaryRequired,
                "Choose a future monthly accounting period for the corrective cutover.");
        var authority = await _db.AccountingAuthorityPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.EffectiveFrom <= effectiveFrom &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= effectiveFrom))
            .OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(cancellationToken);
        var targetIsAuthority = authority is not null &&
            ((current.TargetKind == AccountingProviderEndpointKinds.Internal && authority.Authority == AccountingAuthorityValues.InternalLedger) ||
             (current.TargetKind == AccountingProviderEndpointKinds.External && authority.Authority == AccountingAuthorityValues.ExternalProvider &&
              string.Equals(current.TargetProviderKey, authority.ProviderKey, StringComparison.OrdinalIgnoreCase)));
        if (!targetIsAuthority)
            throw Error(AccountingProviderSwitchReasonCodes.SourceAuthorityMismatch,
                "The current target must remain authoritative until the corrective cutover boundary.");
        if (!await _db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == command.CompanyId &&
                x.UserId == current.ResponsibleUserId && x.Status == CompanyMembershipStatus.Active, cancellationToken))
            throw Error(AccountingProviderSwitchReasonCodes.ResponsibleUserInvalid,
                "The corrective cutover owner is no longer an active company member.");

        current.TransitionTo(AccountingProviderSwitchStatuses.Completed, command.ActorUserId, command.CorrelationId, Now());
        var corrective = new AccountingProviderSwitch(Guid.NewGuid(), command.CompanyId,
            new AccountingProviderEndpoint(current.TargetKind, current.TargetProviderKey),
            new AccountingProviderEndpoint(current.SourceKind, current.SourceProviderKey), period.Id,
            current.MigrationStrategy, command.Reason, current.ResponsibleUserId, current.ResponsibleAgentId,
            command.ActorUserId, command.CorrelationId, Now());
        _db.AccountingProviderSwitches.Add(corrective);
        run.Close(command.ActorUserId, "corrective_cutover_created",
            "Monitoring closed into a separately controlled corrective cutover.", Now(), corrective.Id);
        await AuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchCorrectiveCutoverCreated, run.Id, AuditEventOutcomes.Succeeded,
            "A corrective cutover was created after target activity; the former authority was not restored.",
            command.CorrelationId, cancellationToken, new() { ["correctiveSwitchId"] = corrective.Id.ToString("D") });
        await SaveAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        return await ToDtoAsync(run, cancellationToken);
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var now = Now();
        var ids = await _db.AccountingProviderSwitchMonitoringRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == AccountingProviderSwitchMonitoringStatuses.Active ||
                         x.Status == AccountingProviderSwitchMonitoringStatuses.AttentionRequired ||
                         x.Status == AccountingProviderSwitchMonitoringStatuses.ClosureAwaitingApproval) &&
                x.NextRunUtc <= now && (x.LeaseExpiresUtc == null || x.LeaseExpiresUtc <= now))
            .OrderBy(x => x.NextRunUtc).Select(x => new { x.CompanyId, x.Id }).Take(_options.ClaimBatchSize).ToListAsync(cancellationToken);
        var handled = 0;
        foreach (var item in ids) { if (await RunOneAsync(item.CompanyId, item.Id, cancellationToken)) handled++; }
        return handled;
    }

    private async Task<bool> RunOneAsync(Guid companyId, Guid id, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear(); var now = Now();
        var run = await _db.AccountingProviderSwitchMonitoringRuns.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);
        if (run is null || run.NextRunUtc > now || run.LeaseExpiresUtc > now) return false;
        try { run.Claim($"{Environment.MachineName}:{Environment.ProcessId}", now, now.AddSeconds(_options.LeaseSeconds)); await SaveAsync(cancellationToken); }
        catch (AccountingAuthorityException) { return false; }
        try
        {
            var observations = await EvaluateAsync(run, cancellationToken); var sequence = run.CompletePass(
                observations.Any(x => x.Status != AccountingProviderSwitchMonitoringCheckStatuses.Healthy), Now(), Now().AddHours(_options.CheckIntervalHours));
            foreach (var item in observations) _db.AccountingProviderSwitchMonitoringChecks.Add(new(run.CompanyId, run.SwitchId, run.Id, sequence,
                item.CheckKey, item.Status, item.Severity, item.IsBlocking, item.ReasonCode, item.Explanation, item.EvidenceJson, item.EvidenceFingerprint, Now()));
            await ReconcileIncidentsAsync(run, observations, cancellationToken);
            await AuditAsync(run.CompanyId, run.AssignedOwnerUserId, AuditEventActions.AccountingProviderSwitchMonitoringChecked,
                run.Id, AuditEventOutcomes.Succeeded, "Post-activation accounting checks completed and durable evidence was recorded.", run.CorrelationId, cancellationToken,
                new() { ["checkSequence"] = sequence.ToString(), ["violationCount"] = observations.Count(x => x.Status != AccountingProviderSwitchMonitoringCheckStatuses.Healthy).ToString() });
            await SaveAsync(cancellationToken); _telemetry.ProviderSwitchMonitoring(run.CompanyId, run.SwitchId, run.Id, "completed", sequence,
                observations.Count(x => x.Status != AccountingProviderSwitchMonitoringCheckStatuses.Healthy), run.CorrelationId); return true;
        }
        catch (Exception exception)
        {
            _db.ChangeTracker.Clear(); run = await _db.AccountingProviderSwitchMonitoringRuns.IgnoreQueryFilters()
                .SingleAsync(x => x.CompanyId == companyId && x.Id == id, cancellationToken);
            var exhausted = run.ConsecutiveFailureCount + 1 >= _options.MaximumAttempts;
            run.Fail(exhausted ? AccountingProviderSwitchMonitoringReasonCodes.RetryExhausted : AccountingProviderSwitchMonitoringReasonCodes.CheckFailed,
                Safe(exception.Message), exhausted, Now(), exhausted ? null : Now().AddMinutes(Math.Min(60, 5 * (run.ConsecutiveFailureCount + 1))));
            await SaveAsync(cancellationToken); _telemetry.ProviderSwitchMonitoringFailed(run.CompanyId, run.SwitchId, run.Id,
                run.FailureCode!, run.ConsecutiveFailureCount, run.CorrelationId, exception); return true;
        }
    }

    private async Task<IReadOnlyList<Observation>> EvaluateAsync(AccountingProviderSwitchMonitoringRun run, CancellationToken cancellationToken)
    {
        var sw = await _db.AccountingProviderSwitches.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == run.CompanyId && x.Id == run.SwitchId, cancellationToken);
        var results = new List<Observation>();
        var connection = sw.TargetKind == AccountingProviderEndpointKinds.External
            ? await _db.FinanceIntegrationConnections.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == run.CompanyId && x.ProviderKey == sw.TargetProviderKey).OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(cancellationToken) : null;
        var syncFailures = sw.TargetProviderKey is null ? 0 : await _db.FinanceIntegrationSyncStates.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.CompanyId == run.CompanyId && x.ProviderKey == sw.TargetProviderKey && x.Status == FinanceIntegrationSyncStatuses.Failed, cancellationToken);
        var staleSyncs = sw.TargetProviderKey is null ? 0 : await _db.FinanceIntegrationSyncStates.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
            x.CompanyId == run.CompanyId && x.ProviderKey == sw.TargetProviderKey &&
            (x.LastCompletedUtc == null || x.LastCompletedUtc < Now().AddHours(-_options.SyncStaleHours)), cancellationToken);
        if (sw.TargetKind == AccountingProviderEndpointKinds.External && staleSyncs == 0 &&
            (connection?.LastSyncUtc is null || connection.LastSyncUtc < Now().AddHours(-_options.SyncStaleHours)))
            staleSyncs = 1;
        var providerFailures = sw.TargetProviderKey is null ? 0 : await _db.AccountingProviderExports.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
            x.CompanyId == run.CompanyId && x.ProviderKey == sw.TargetProviderKey && x.UpdatedUtc >= run.StartedUtc &&
            (x.Status == AccountingProviderExportStatuses.Failed || x.Status == AccountingProviderExportStatuses.ReconciliationRequired), cancellationToken);
        results.Add(Observe(AccountingProviderSwitchMonitoringCheckKeys.ProviderSyncHealth, syncFailures + staleSyncs + providerFailures,
            providerFailures > 0, $"Provider synchronization has {syncFailures} failed, {staleSyncs} stale, and {providerFailures} uncertain operation(s).", new { syncFailures, staleSyncs, providerFailures }));
        var badDispositions = await CurrentStaging(run).CountAsync(x => x.Disposition == AccountingProviderSwitchDispositions.Missing || x.Disposition == AccountingProviderSwitchDispositions.Conflicting || x.Disposition == AccountingProviderSwitchDispositions.Blocked, cancellationToken);
        results.Add(Observe(AccountingProviderSwitchMonitoringCheckKeys.ProjectionIntegrity, badDispositions, badDispositions > 0,
            $"Internal migration projections contain {badDispositions} unresolved record(s).", new { badDispositions }));
        var invoiceGaps = await CurrentStaging(run).CountAsync(x => x.Dataset == AccountingProviderSwitchStagingDatasets.Invoices &&
            (x.Disposition == AccountingProviderSwitchDispositions.Missing || x.Disposition == AccountingProviderSwitchDispositions.Conflicting || x.Disposition == AccountingProviderSwitchDispositions.Blocked), cancellationToken);
        results.Add(Observe(AccountingProviderSwitchMonitoringCheckKeys.InvoiceCompleteness, invoiceGaps, invoiceGaps > 0,
            $"Invoice completeness has {invoiceGaps} unresolved record(s).", new { invoiceGaps }));
        var mappingProblems = await _db.AccountingProviderSwitchMappingDecisions.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId &&
            (x.Status == AccountingProviderSwitchMappingStatuses.Stale || x.Status == AccountingProviderSwitchMappingStatuses.Rejected), cancellationToken);
        results.Add(Observe(AccountingProviderSwitchMonitoringCheckKeys.MappingIntegrity, mappingProblems, mappingProblems > 0,
            $"Migration mappings contain {mappingProblems} stale or rejected decision(s).", new { mappingProblems }));
        var scopeProblems = await _db.AccountingProviderSwitchCapabilities.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.EndpointRole == "target" &&
            x.RequiredScope != null && x.Level != AccountingProviderSwitchCapabilityLevels.Supported, cancellationToken);
        var connectionProblems = sw.TargetKind == AccountingProviderEndpointKinds.External && (connection is null || connection.Status != FinanceIntegrationConnectionStatuses.Connected) ? 1 : 0;
        results.Add(Observe(AccountingProviderSwitchMonitoringCheckKeys.ConnectionAndScopes, scopeProblems + connectionProblems, true,
            $"Connection and access checks found {scopeProblems + connectionProblems} problem(s).", new { scopeProblems, connectionStatus = connection?.Status ?? "not_required" }));
        var bankProblems = await _db.BankReconciliationFollowUps.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.CompanyId == run.CompanyId && x.Status == BankReconciliationFollowUpStatuses.Open && x.CreatedUtc >= run.StartedUtc, cancellationToken);
        results.Add(Observe(AccountingProviderSwitchMonitoringCheckKeys.BankReconciliation, bankProblems, bankProblems > 0,
            $"Bank reconciliation has {bankProblems} open follow-up(s) since activation.", new { bankProblems }));
        var formerAttempts = await _db.AuditEvents.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.CompanyId == run.CompanyId && x.Action == AuditEventActions.AccountingFormerAuthorityPostingBlocked && x.OccurredUtc >= run.StartedUtc, cancellationToken);
        results.Add(Observe(AccountingProviderSwitchMonitoringCheckKeys.FormerAuthorityPostingAttempts, formerAttempts, formerAttempts > 0,
            $"The former authority received {formerAttempts} blocked posting attempt(s).", new { formerAttempts }));
        var financialProblems = await _db.AccountingProviderSwitchFinalChecks.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.Result == AccountingProviderSwitchCutoverCheckResults.Failed &&
            (x.CheckKey.Contains("tax") || x.CheckKey.Contains("currency") || x.CheckKey.Contains("control") || x.CheckKey.Contains("trial")), cancellationToken);
        results.Add(Observe(AccountingProviderSwitchMonitoringCheckKeys.FinancialControls, financialProblems, financialProblems > 0,
            $"Tax, currency, and control-account checks have {financialProblems} unresolved variance(s).", new { financialProblems }));
        var ambiguous = await _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.ReconciliationNeeded, cancellationToken)
            + (sw.TargetProviderKey is null ? 0 : await _db.AccountingProviderExports.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
                x.CompanyId == run.CompanyId && x.ProviderKey == sw.TargetProviderKey && x.UpdatedUtc >= run.StartedUtc &&
                x.Status == AccountingProviderExportStatuses.ReconciliationRequired, cancellationToken));
        results.Add(Observe(AccountingProviderSwitchMonitoringCheckKeys.ExternalOutcomes, ambiguous, ambiguous > 0,
            $"External operations have {ambiguous} outcome(s) requiring reconciliation.", new { ambiguous }));
        var archiveCount = await _db.AccountingProviderSwitchArchiveDependencies.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId, cancellationToken);
        var archiveUnavailable = archiveCount > 0 && sw.SourceKind == AccountingProviderEndpointKinds.External &&
            !await _db.FinanceIntegrationConnections.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == run.CompanyId && x.ProviderKey == sw.SourceProviderKey && x.Status == FinanceIntegrationConnectionStatuses.Connected, cancellationToken);
        results.Add(Observe(AccountingProviderSwitchMonitoringCheckKeys.ArchiveAvailability, archiveUnavailable ? archiveCount : 0, archiveUnavailable,
            archiveUnavailable ? "Accepted exceptions depend on a source archive that is not connected." : $"Archive evidence is available for {archiveCount} retained dependenc(ies).", new { archiveCount, archiveUnavailable }));
        return results;
    }

    private async Task ReconcileIncidentsAsync(AccountingProviderSwitchMonitoringRun run, IReadOnlyList<Observation> observations,
        CancellationToken cancellationToken)
    {
        var failures = observations.Where(x => x.Status != AccountingProviderSwitchMonitoringCheckStatuses.Healthy).ToArray();
        var existing = await Incidents(run).ToListAsync(cancellationToken);
        foreach (var item in failures)
        {
            var incident = existing.SingleOrDefault(x => x.Fingerprint == item.IncidentFingerprint);
            if (incident is null)
            {
                var task = new WorkTask(Guid.NewGuid(), run.CompanyId, "accounting_migration_monitoring",
                    $"Review {Friendly(item.CheckKey)} after accounting migration", item.Explanation,
                    item.IsBlocking ? WorkTaskPriority.Critical : WorkTaskPriority.High, run.AssignedOwnerAgentId, null,
                    "system", null, new Dictionary<string, JsonNode?> { ["switchId"] = run.SwitchId, ["monitoringRunId"] = run.Id,
                        ["checkKey"] = item.CheckKey, ["fingerprint"] = item.IncidentFingerprint }, null, null,
                    item.Explanation, null, $"migration-monitor:{run.Id:N}:{item.IncidentFingerprint}", WorkTaskSourceTypes.Agent,
                    run.AssignedOwnerAgentId, "accounting_migration_monitoring", "Post-activation monitoring found a material discrepancy.", item.IncidentFingerprint);
                _db.WorkTasks.Add(task);
                incident = new(run.CompanyId, run.SwitchId, run.Id, item.IncidentFingerprint, item.CheckKey, item.Severity,
                    item.IsBlocking, item.Explanation, task.Id, Now()); _db.AccountingProviderSwitchMonitoringIncidents.Add(incident);
            }
            else if (incident.Status != AccountingProviderSwitchMonitoringIncidentStatuses.AcceptedException)
                incident.ObserveAgain(item.Severity, item.IsBlocking, item.Explanation, Now());
        }
        var activeFingerprints = failures.Select(x => x.IncidentFingerprint).ToHashSet(StringComparer.Ordinal);
        foreach (var incident in existing.Where(x => x.Status == AccountingProviderSwitchMonitoringIncidentStatuses.Open && !activeFingerprints.Contains(x.Fingerprint)))
            incident.Resolve(Now());
    }

    private Observation Observe(string key, int problemCount, bool blocking, string failureExplanation, object evidence)
    {
        var healthy = problemCount == 0; var evidenceJson = JsonSerializer.Serialize(evidence);
        var status = healthy ? AccountingProviderSwitchMonitoringCheckStatuses.Healthy : blocking ? AccountingProviderSwitchMonitoringCheckStatuses.Critical : AccountingProviderSwitchMonitoringCheckStatuses.Attention;
        var explanation = healthy ? $"{Friendly(key)} is healthy." : failureExplanation;
        return new(key, status, healthy ? "info" : blocking ? "critical" : "warning", !healthy && blocking,
            healthy ? $"{key}_healthy" : $"{key}_violation", explanation, evidenceJson,
            Hash($"{key}|{status}|{evidenceJson}"), Hash(key));
    }

    private async Task<string> ClosureHashAsync(AccountingProviderSwitchMonitoringRun run, CancellationToken cancellationToken)
    {
        var checks = await _db.AccountingProviderSwitchMonitoringChecks.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == run.CompanyId && x.MonitoringRunId == run.Id)
            .OrderBy(x => x.CheckSequence).ThenBy(x => x.CheckKey).Select(x => x.Fingerprint).ToListAsync(cancellationToken);
        var incidents = await Incidents(run).AsNoTracking().OrderBy(x => x.Fingerprint).Select(x => $"{x.Fingerprint}:{x.Status}:{x.Version}").ToListAsync(cancellationToken);
        return Hash($"{run.Id:N}|{run.WindowEndsUtc:O}|{run.CheckSequence}|{run.AttemptCount}|{run.ConsecutiveFailureCount}|" +
            $"{run.LastCheckStartedUtc:O}|{run.LastSuccessfulCheckUtc:O}|{run.NextRunUtc:O}|{run.FailureCode}|" +
            $"{string.Join('|', checks)}|{string.Join('|', incidents)}");
    }

    private async Task<AccountingProviderSwitchMonitoringDto> ToDtoAsync(AccountingProviderSwitchMonitoringRun run,
        CancellationToken cancellationToken)
    {
        var checks = await _db.AccountingProviderSwitchMonitoringChecks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.MonitoringRunId == run.Id && x.CheckSequence == run.CheckSequence)
            .OrderBy(x => x.CheckKey).Select(x => new AccountingProviderSwitchMonitoringCheckDto(x.CheckKey, x.Status, x.Severity,
                x.IsBlocking, x.ReasonCode, x.Explanation, x.EvidenceJson, x.ObservedUtc)).ToListAsync(cancellationToken);
        var incidents = await Incidents(run).AsNoTracking().OrderByDescending(x => x.Status == AccountingProviderSwitchMonitoringIncidentStatuses.Open)
            .ThenByDescending(x => x.IsBlocking).ThenByDescending(x => x.LastObservedUtc)
            .Select(x => new AccountingProviderSwitchMonitoringIncidentDto(x.Id, x.CheckKey, x.Severity, x.IsBlocking,
                x.Explanation, x.Status, x.TaskId, x.OccurrenceCount, x.FirstObservedUtc, x.LastObservedUtc,
                x.AcceptedByUserId, x.ExceptionExplanation, x.ExceptionScope, x.FinancialImpact, x.EvidenceReference, x.Version)).ToListAsync(cancellationToken);
        var open = incidents.Where(x => x.Status == AccountingProviderSwitchMonitoringIncidentStatuses.Open).ToArray();
        var connectionIssue = checks.Any(x => x.CheckKey == AccountingProviderSwitchMonitoringCheckKeys.ConnectionAndScopes && x.Status != AccountingProviderSwitchMonitoringCheckStatuses.Healthy);
        var externalIssue = checks.Any(x => x.CheckKey == AccountingProviderSwitchMonitoringCheckKeys.ExternalOutcomes && x.Status != AccountingProviderSwitchMonitoringCheckStatuses.Healthy);
        var approvalStatus = run.ClosureApprovalRequestId.HasValue
            ? await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == run.CompanyId && x.Id == run.ClosureApprovalRequestId)
                .Select(x => (ApprovalRequestStatus?)x.Status).SingleOrDefaultAsync(cancellationToken)
            : null;
        var approvalEvidenceIsCurrent = run.ClosureApprovalRequestId.HasValue && run.ClosureEvidenceHash == await ClosureHashAsync(run, cancellationToken);
        var approvalCanBeReused = approvalEvidenceIsCurrent && approvalStatus is ApprovalRequestStatus.Pending or ApprovalRequestStatus.Approved;
        var now = Now();
        var checkPending = run.NextRunUtc <= now || (run.LeaseOwner is not null && run.LeaseExpiresUtc > now);
        var hasFinalSuccessfulCheck = run.LastSuccessfulCheckUtc >= run.WindowEndsUtc && run.FailureCode is null &&
            run.Status != AccountingProviderSwitchMonitoringStatuses.Failed;
        var canRequestClosure = now >= run.WindowEndsUtc && !checkPending && hasFinalSuccessfulCheck && open.Length == 0 &&
            run.Status != AccountingProviderSwitchMonitoringStatuses.Closed && !approvalCanBeReused;
        var approved = approvalEvidenceIsCurrent && approvalStatus == ApprovalRequestStatus.Approved;
        var actionExplanation = open.Any(x => x.IsBlocking)
            ? "Resolve blocking discrepancies before closure."
            : open.Length > 0
                ? "Resolve or document every remaining non-blocking issue before closure."
                : now < run.WindowEndsUtc
                    ? "Monitoring continues until the configured window ends."
                    : checkPending
                        ? "Wait for the queued monitoring check to finish before closure."
                    : !hasFinalSuccessfulCheck
                        ? "Run a successful monitoring check at or after the end of the window."
                        : approved
                            ? "The current approval allows this migration to close."
                            : approvalCanBeReused
                                ? "Closure approval is pending from the current evidence."
                                : canRequestClosure
                                    ? "The monitoring window can be submitted for closure."
                                    : "Refresh the monitoring evidence before choosing the next action.";
        var actions = new AccountingProviderSwitchMonitoringAllowedActionsDto(
            run.Status is AccountingProviderSwitchMonitoringStatuses.Active or AccountingProviderSwitchMonitoringStatuses.AttentionRequired or AccountingProviderSwitchMonitoringStatuses.ClosureAwaitingApproval,
            run.Status is AccountingProviderSwitchMonitoringStatuses.Failed or AccountingProviderSwitchMonitoringStatuses.AttentionRequired,
            connectionIssue, externalIssue, canRequestClosure, approved && !checkPending && hasFinalSuccessfulCheck && open.Length == 0,
            open.Any(x => x.IsBlocking), actionExplanation);
        return new(run.Id, run.CompanyId, run.SwitchId, run.ActivationExecutionId, run.WindowDays, run.AssignedOwnerUserId,
            run.AssignedOwnerAgentId, run.Status, run.CheckSequence, run.AttemptCount, run.ConsecutiveFailureCount,
            run.StartedUtc, run.WindowEndsUtc, run.LastSuccessfulCheckUtc, run.NextRunUtc, run.FailureCode,
            run.FailureSummary, run.ClosureApprovalRequestId, run.CorrectiveSwitchId, run.ClosedUtc, run.Version,
            checks, incidents, actions);
    }

    private IQueryable<AccountingProviderSwitchMonitoringRun> Runs(Guid companyId, Guid switchId, bool tracking)
    { var query = _db.AccountingProviderSwitchMonitoringRuns.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.SwitchId == switchId); return tracking ? query : query.AsNoTracking(); }
    private Task<AccountingProviderSwitchMonitoringRun> RunAsync(Guid companyId, Guid switchId, bool tracking, CancellationToken cancellationToken) =>
        Runs(companyId, switchId, tracking).SingleOrDefaultAsync(cancellationToken).ContinueWith(x => x.Result ?? throw Error(AccountingProviderSwitchMonitoringReasonCodes.NotFound, "Post-activation monitoring was not found for this company."), cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    private IQueryable<AccountingProviderSwitchMonitoringIncident> Incidents(AccountingProviderSwitchMonitoringRun run) =>
        _db.AccountingProviderSwitchMonitoringIncidents.IgnoreQueryFilters().Where(x => x.CompanyId == run.CompanyId && x.MonitoringRunId == run.Id);
    private IQueryable<AccountingProviderSwitchStagedRecord> CurrentStaging(AccountingProviderSwitchMonitoringRun run) =>
        _db.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.IsCurrent);
    private async Task SaveAsync(CancellationToken cancellationToken) { try { await _db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.ConcurrencyConflict, "Monitoring changed concurrently. Reload before continuing."); } }
    private Task AuditAsync(Guid companyId, Guid actor, string action, Guid targetId, string outcome, string summary,
        string correlation, CancellationToken cancellationToken, Dictionary<string, string?>? metadata = null) => _audit.WriteAsync(
        new AuditEventWriteRequest(companyId, AuditActorTypes.User, actor, action, AuditTargetTypes.AccountingProviderSwitchMonitoring,
            targetId.ToString("D"), outcome, summary, ["accounting_provider_switch_monitoring"], metadata ?? [], correlation, Now()), cancellationToken);
    private static void AddIssue(List<AccountingProviderSwitchOperationIssueDto> issues, string category, long count, string severity, string explanation, string nextAction) { if (count > 0) issues.Add(new(category, severity, count, explanation, nextAction)); }
    private static string Friendly(string key) => key.Replace('_', ' ');
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Safe(string? value) => string.IsNullOrWhiteSpace(value) ? "Monitoring stopped safely." : value.Trim().Length <= 1000 ? value.Trim() : value.Trim()[..1000];
    private static void EnsureVersion(long current, long expected) { if (current != expected) throw Conflict(AccountingProviderSwitchMonitoringReasonCodes.ConcurrencyConflict, "Monitoring changed. Reload before continuing."); }
    private static void ValidateCompany(Guid companyId) { if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId)); }
    private static void ValidateSwitch(Guid switchId) { if (switchId == Guid.Empty) throw new ArgumentException("SwitchId is required.", nameof(switchId)); }
    private static void ValidateCommand(Guid companyId, Guid switchId, Guid actor, string correlation) { ValidateCompany(companyId); ValidateSwitch(switchId); if (actor == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actor)); if (string.IsNullOrWhiteSpace(correlation)) throw new ArgumentException("CorrelationId is required.", nameof(correlation)); }
    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
    private static AccountingAuthorityException Error(string code, string message) => new(code, message);
    private static AccountingAuthorityException Conflict(string code, string message) => new(code, message, true);
    private sealed record Observation(string CheckKey, string Status, string Severity, bool IsBlocking,
        string ReasonCode, string Explanation, string EvidenceJson, string EvidenceFingerprint,
        string IncidentFingerprint);
}
