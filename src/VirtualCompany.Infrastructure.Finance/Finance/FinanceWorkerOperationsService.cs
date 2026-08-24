using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceWorkerRecoveryOptions
{
    public const string SectionName = "FinanceWorkerRecovery";
    public int BacklogWarningMinutes { get; set; } = 30;
    public int LeaseGraceSeconds { get; set; } = 30;
    public int MaximumVisibleItems { get; set; } = 200;
}

internal sealed record FinanceWorkerDefinition(
    string Key, string DisplayName, string Category, string DurableUnit, string Trigger,
    string ClaimAndLease, string BatchBound, string IdempotencyIdentity, string RetryContract,
    string CancellationContract, string ProgressAndTerminalStates, string OperatorAction,
    string ConfigurationSection);

internal static class FinanceWorkerCatalog
{
    public static readonly IReadOnlyList<FinanceWorkerDefinition> All =
    [
        Worker("finance-seed-backfill", "Finance setup backfill", "setup", "Backfill run, company attempt, and Finance seed execution", "Scheduled scan", "Distributed scan lock plus atomic company execution claim", "Configured page, enqueue, and company limits", "Run + company + seed version", "Transient failures use bounded exponential retry; invalid state is terminal", "The scan can stop; a claimed company resumes from durable seed checkpoints", "Run and company attempt counts with completed, skipped, and failed outcomes", "Retry an eligible company execution or acknowledge a permanent failure", FinanceSeedBackfillWorkerOptions.SectionName),
        Worker("report-regeneration", "Financial report regeneration", "reporting", "Background execution linked to a fiscal period", "Close/reopen request", "Atomic SQL claim with owner and expiry", "Configured claim batch", "Company + fiscal period + snapshot version", "Transient failures retry with bounded backoff", "Queued work can stop; completed immutable snapshots are never undone", "Execution status, attempt history, and snapshot version", "Retry, stop queued work, or acknowledge failure", ReportingPeriodRegenerationWorkerOptions.SectionName),
        Worker("accounting-export", "Accounting file export", "export", "Accounting export job and immutable artifact", "Authorized export request", "Atomic job claim owned by the export service", "Bounded due-job batch", "Company + period + format + source checksum", "Storage and persistence failures retry; validation is terminal", "Only future queued work can stop; completed artifacts remain immutable", "Job status, checksum, artifact reference, and safe failure", "Retry safe failures or acknowledge a permanent failure", AccountingExportWorkerOptions.SectionName),
        Worker("historical-migration", "Historical accounting migration", "migration", "Migration run, conflicts, reports, and phase checkpoint", "Authorized migration request", "Company-explicit SQL lease", "Configured claim and phase batch", "Company + target version", "Expired leases recover to the last phase; exhaustion is terminal", "Safe queued work may stop; migrated journal facts are not rolled back", "Phase, counts, conflicts, reports, and terminal failure", "Resolve conflicts, retry eligible work, or acknowledge failure", AccountingMigrationWorkerOptions.SectionName),
        Worker("provider-switch-assessment", "Provider switch assessment", "provider switch", "Assessment and dataset capability evidence", "Provider switch request", "Company-explicit SQL lease", "Configured claim/page limits", "Company + switch + source evidence version", "Transient provider reads retry within configured attempts", "Cancellation follows the provider-switch lifecycle", "Assessment status, evidence hash, gaps, and failure", "Retry through provider-switch actions or cancel the switch", AccountingProviderSwitchAssessmentWorkerOptions.SectionName),
        Worker("provider-switch-rehearsal", "Provider switch rehearsal", "provider switch", "Rehearsal, inputs, results, and reconciliation checks", "Approved mapping state", "Company-explicit SQL lease", "Configured claim batch", "Switch + plan version + evidence hashes", "Transient reads retry; stale or invalid evidence is terminal", "Cancellation follows the provider-switch lifecycle", "Dataset results, checks, plan, and failure", "Replay after remediation or cancel the switch", AccountingProviderSwitchRehearsalWorkerOptions.SectionName),
        Worker("provider-switch-preparation", "Provider switch preparation", "provider switch", "Preparation, readiness checks, candidates, and archive dependencies", "Approved cutover plan", "Company-explicit SQL lease", "Configured claim/save batches", "Switch + plan version + candidate source identity", "Transient failures retry; invalid readiness blocks", "Cancellation follows the provider-switch lifecycle", "Candidate/checkpoint counts and terminal state", "Replay rejected candidates or cancel safely", AccountingProviderSwitchPreparationWorkerOptions.SectionName),
        Worker("provider-switch-target-transfer", "Provider target transfer", "provider switch", "Transfer batch, item, attempt, and provider acknowledgement", "Prepared target package", "Company-explicit SQL lease and per-item execution tracker", "Configured claim and item batches", "Stable target identity + operation mode + version", "Rate limits and definite transient failures retry; ambiguity never replays", "Only safe future work stops; possible provider success requires reconciliation", "Per-item attempts, provider acknowledgement, and reconciliation state", "Retry a replayable batch or reconcile ambiguous items", AccountingProviderSwitchTargetTransferWorkerOptions.SectionName),
        Worker("provider-switch-cutover", "Provider switch cutover", "provider switch", "Cutover execution and immutable final checkpoints", "Approved cutover schedule", "Company-explicit SQL lease", "Configured claim batch", "Switch + plan + cutover boundary", "Safe blocked work can resume; provider ambiguity requires reconciliation", "Cancellation is allowed only before irreversible target activity", "Current step, final checks, attempts, and allowed actions", "Resume, recover, cancel, or perform a corrective cutover", AccountingProviderSwitchCutoverWorkerOptions.SectionName),
        Worker("provider-switch-monitoring", "Provider switch monitoring", "provider switch", "Monitoring run, checks, incidents, and closure approval", "Activated switch and scheduled checks", "Company-explicit SQL lease", "Configured claim batch", "Switch + monitoring sequence", "Consecutive failures retry to exhaustion", "Closing requires completed evidence and approval", "Check sequence, incidents, failures, and closure state", "Run now, retry, accept an exception, or request closure", AccountingProviderSwitchMonitoringOptions.SectionName),
        Worker("approval-task-backfill", "Finance approval task backfill", "approval", "Idempotent approval/task identities", "Scheduled compatibility scan", "Company-explicit bounded scan", "Configured company and record limits", "Company + target record + approval policy version", "The next scheduled scan recovers transient failure", "The worker can be disabled; created approvals remain governed records", "Created/skipped counts and structured logs", "Disable the worker or rerun an authorized bounded backfill", FinanceApprovalTaskBackfillWorkerOptions.SectionName),
        Worker("insights-snapshot", "Finance insights snapshot", "analytics", "Background execution linked to a normalized snapshot descriptor", "Read-model refresh request", "Atomic SQL claim with owner and expiry", "Configured claim batch", "Company + snapshot descriptor", "Transient failures retry with bounded backoff", "Queued refresh can stop; published snapshots are replaced by version", "Execution status, attempts, and snapshot timestamp", "Retry, stop queued work, or acknowledge failure", FinanceInsightsSnapshotWorkerOptions.SectionName),
        Worker("analytics-startup-refresh", "Finance analytics startup refresh", "analytics", "Per-company durable insight refresh execution", "Application startup", "Company-explicit enqueue with idempotent snapshot identity", "One bounded pass over configured companies", "Company + normalized startup snapshot descriptor", "Queued snapshot executions own retry", "Stopping the host cancels enumeration without losing queued work", "Queued count plus downstream execution progress", "Inspect and recover the insight execution", FinanceAnalyticsStartupRefreshOptions.SectionName),
        Worker("integration-startup-sync", "Finance connection startup sync", "integration", "Connection sync state and provider cursor", "Application startup", "Company-explicit connection claim", "Configured connection batch", "Company + connection + sync cursor", "Transient provider failures use connection retry state", "Host cancellation preserves provider cursor", "Connection health, cursor, last success, and safe issue", "Reconnect or run an authorized sync", FinanceIntegrationStartupSyncOptions.SectionName),
        Worker("bill-registration-reconciliation", "Supplier bill registration reconciliation", "integration", "Provider write request and bill registration state", "Scheduled reconciliation scan", "Company-explicit provider write claim", "Configured bounded scan", "Company + bill source/version + provider command", "Lookup reconciles possible success; no blind replay", "Possible external success cannot be cancelled", "Write attempts, provider reference, and reconciliation state", "Reconcile the provider outcome or retry a definite failure", FinanceBillRegistrationReconciliationOptions.SectionName),
        Worker("finance-seed", "Finance setup execution", "setup", "Background execution and deterministic seed checkpoints", "Company setup request", "Atomic SQL claim with owner and expiry", "Configured claim batch", "Company + seed dataset version", "Transient failures retry with bounded backoff", "Active setup cannot be stopped after data checkpoints begin", "Execution attempts plus durable dataset checks", "Retry eligible setup work or acknowledge failure", FinanceSeedWorkerOptions.SectionName),
        Worker("simulation-progression", "Simulation progression", "simulation", "Simulation run, transitions, and day logs", "Explicit Simulation Lab run", "Company-explicit simulation run claim", "Configured company/run limits", "Company + simulation run + virtual day", "Failed days remain visible and resume from the last completed virtual day", "Only explicit simulation work can stop; production Finance is isolated", "Run state, day log, transition history, and failure", "Pause or stop from Simulation Lab", CompanySimulationProgressionWorkerOptions.SectionName)
    ];

    private static FinanceWorkerDefinition Worker(string key, string name, string category, string unit, string trigger,
        string claim, string batch, string identity, string retry, string cancellation, string progress,
        string action, string section) => new(key, name, category, unit, trigger, claim, batch, identity, retry,
            cancellation, progress, action, section);
}

public sealed class FinanceBackgroundExecutionAttemptRecorder
{
    private readonly VirtualCompanyDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly FinanceWorkerOperationsTelemetry _telemetry;
    private readonly ILogger<FinanceBackgroundExecutionAttemptRecorder> _logger;

    public FinanceBackgroundExecutionAttemptRecorder(VirtualCompanyDbContext db, TimeProvider timeProvider)
        : this(db, timeProvider, new FinanceWorkerOperationsTelemetry(),
            NullLogger<FinanceBackgroundExecutionAttemptRecorder>.Instance)
    {
    }

    public FinanceBackgroundExecutionAttemptRecorder(VirtualCompanyDbContext db, TimeProvider timeProvider,
        FinanceWorkerOperationsTelemetry telemetry, ILogger<FinanceBackgroundExecutionAttemptRecorder> logger)
    {
        _db = db;
        _timeProvider = timeProvider;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<BackgroundExecutionAttempt> StartAsync(BackgroundExecution execution, string workerName,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var staleAttempts = await _db.BackgroundExecutionAttempts.IgnoreQueryFilters()
            .Where(x => x.CompanyId == execution.CompanyId && x.BackgroundExecutionId == execution.Id &&
                x.Outcome == BackgroundExecutionAttemptOutcomes.InProgress && x.CompletedUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var stale in staleAttempts)
        {
            stale.Complete(BackgroundExecutionAttemptOutcomes.LeaseExpired, now,
                BackgroundExecutionFailureCategory.TransientInfrastructure, "lease_expired",
                "The previous worker process stopped before completing this attempt; work resumed from durable state.");
        }

        var attemptNumber = execution.AttemptCount + 1;
        execution.StartAttempt(execution.CorrelationId, attemptNumber, Math.Max(1, execution.MaxAttempts));
        var attempt = new BackgroundExecutionAttempt(Guid.NewGuid(), execution.CompanyId, execution.Id, workerName,
            attemptNumber, execution.LeaseOwner ?? execution.CorrelationId,
            execution.LeaseExpiresUtc ?? now.AddMinutes(5), now);
        _db.BackgroundExecutionAttempts.Add(attempt);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Finance worker attempt started. CompanyId={CompanyId} Worker={Worker} ExecutionId={ExecutionId} CorrelationId={CorrelationId} Attempt={Attempt} MaxAttempts={MaxAttempts} LeaseOwner={LeaseOwner} LeaseExpiresUtc={LeaseExpiresUtc}.",
            execution.CompanyId, workerName, execution.Id, execution.CorrelationId, attemptNumber,
            execution.MaxAttempts, attempt.LeaseOwner, attempt.LeaseExpiresUtc);
        return attempt;
    }

    public async Task CompleteAsync(BackgroundExecution execution, BackgroundExecutionAttempt attempt,
        CancellationToken cancellationToken)
    {
        var outcome = execution.Status switch
        {
            BackgroundExecutionStatus.Succeeded => BackgroundExecutionAttemptOutcomes.Succeeded,
            BackgroundExecutionStatus.RetryScheduled => BackgroundExecutionAttemptOutcomes.RetryScheduled,
            BackgroundExecutionStatus.Blocked => BackgroundExecutionAttemptOutcomes.Blocked,
            BackgroundExecutionStatus.Cancelled => BackgroundExecutionAttemptOutcomes.Cancelled,
            _ => BackgroundExecutionAttemptOutcomes.Failed
        };
        attempt.Complete(outcome, _timeProvider.GetUtcNow().UtcDateTime, execution.FailureCategory,
            execution.FailureCode, execution.FailureMessage);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.AttemptCompleted(execution, attempt);
        _logger.LogInformation(
            "Finance worker attempt completed. CompanyId={CompanyId} Worker={Worker} ExecutionId={ExecutionId} CorrelationId={CorrelationId} Attempt={Attempt} Outcome={Outcome} FailureClass={FailureClass} DurationMs={DurationMs} NextRetryUtc={NextRetryUtc}.",
            execution.CompanyId, attempt.WorkerName, execution.Id, execution.CorrelationId,
            attempt.AttemptNumber, attempt.Outcome, execution.FailureCategory?.ToStorageValue(),
            attempt.DurationMilliseconds, execution.NextRetryUtc);
    }
}

public sealed class FinanceWorkerOperationsService : IFinanceWorkerOperationsService
{
    private static readonly BackgroundExecutionType[] FinanceExecutionTypes =
    [BackgroundExecutionType.FinanceSeed, BackgroundExecutionType.FinanceReportRegeneration, BackgroundExecutionType.FinanceInsightRefresh];
    private readonly VirtualCompanyDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IOptions<FinanceWorkerRecoveryOptions> _options;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _timeProvider;
    private readonly FinanceWorkerOperationsTelemetry _telemetry;
    private readonly ILogger<FinanceWorkerOperationsService> _logger;

    public FinanceWorkerOperationsService(VirtualCompanyDbContext db, IConfiguration configuration,
        IOptions<FinanceWorkerRecoveryOptions> options, IAuditEventWriter audit, TimeProvider timeProvider,
        FinanceWorkerOperationsTelemetry telemetry, ILogger<FinanceWorkerOperationsService> logger)
    {
        _db = db;
        _configuration = configuration;
        _options = options;
        _audit = audit;
        _timeProvider = timeProvider;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task<FinanceWorkerOperationsReadModel> GetAsync(GetFinanceWorkerOperationsQuery query,
        CancellationToken cancellationToken)
    {
        EnsureCompany(query.CompanyId);
        var take = Math.Clamp(query.Take, 1, Math.Max(1, _options.Value.MaximumVisibleItems));
        var source = _db.BackgroundExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && FinanceExecutionTypes.Contains(x.ExecutionType));
        if (!string.IsNullOrWhiteSpace(query.WorkerKey))
        {
            var type = ResolveExecutionType(query.WorkerKey);
            source = type.HasValue ? source.Where(x => x.ExecutionType == type.Value) : source.Where(_ => false);
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var statuses = ResolveStatuses(query.Status);
            source = source.Where(x => statuses.Contains(x.Status));
        }

        var total = await source.CountAsync(cancellationToken);
        var executions = await source.OrderByDescending(x => x.UpdatedUtc).Skip(Math.Max(0, query.Skip)).Take(take)
            .ToListAsync(cancellationToken);
        var executionIds = executions.Select(x => x.Id).ToArray();
        var attempts = executionIds.Length == 0 ? [] : await _db.BackgroundExecutionAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && executionIds.Contains(x.BackgroundExecutionId))
            .OrderByDescending(x => x.AttemptNumber).ToListAsync(cancellationToken);
        var attemptLookup = attempts.GroupBy(x => x.BackgroundExecutionId).ToDictionary(x => x.Key, x => (IReadOnlyList<BackgroundExecutionAttempt>)x.ToList());
        var catalog = BuildCatalog();
        var items = executions.Select(x => Map(x, attemptLookup.GetValueOrDefault(x.Id) ?? [], catalog)).ToList();
        var health = await EvaluateHealthAsync(query.CompanyId, catalog, cancellationToken);
        _telemetry.ObserveHealth(health);
        return new FinanceWorkerOperationsReadModel(query.CompanyId, health, catalog, items, total);
    }

    public Task<FinanceWorkerWorkItemDto> RetryAsync(RetryFinanceWorkerExecutionCommand command, CancellationToken cancellationToken) =>
        MutateAsync(command.CompanyId, command.ExecutionId, command.ExpectedVersion, command.ActorUserId, command.Reason,
            command.CorrelationId, "retry", cancellationToken);

    public Task<FinanceWorkerWorkItemDto> StopAsync(StopFinanceWorkerExecutionCommand command, CancellationToken cancellationToken) =>
        MutateAsync(command.CompanyId, command.ExecutionId, command.ExpectedVersion, command.ActorUserId, command.Reason,
            command.CorrelationId, "stop", cancellationToken);

    public Task<FinanceWorkerWorkItemDto> AcknowledgeAsync(AcknowledgeFinanceWorkerExecutionCommand command, CancellationToken cancellationToken) =>
        MutateAsync(command.CompanyId, command.ExecutionId, command.ExpectedVersion, command.ActorUserId, command.Acknowledgement,
            command.CorrelationId, "acknowledge", cancellationToken);

    private async Task<FinanceWorkerWorkItemDto> MutateAsync(Guid companyId, Guid executionId, long expectedVersion,
        Guid actorUserId, string reason, string? correlationId, string action, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A plain-English reason is required.", nameof(reason));
        var execution = await _db.BackgroundExecutions.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.Id == executionId && x.CompanyId == companyId && FinanceExecutionTypes.Contains(x.ExecutionType), cancellationToken)
            ?? throw new FinanceWorkerOperationException(FinanceWorkerOperationReasonCodes.WorkNotFound,
                "Finance background work was not found.", isConflict: false);
        if (execution.Version != expectedVersion)
        {
            throw new FinanceWorkerOperationException(FinanceWorkerOperationReasonCodes.StaleVersion,
                "This work item changed after it was loaded. Refresh it before taking action.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var auditAction = action switch
        {
            "retry" => AuditEventActions.FinanceWorkerRetryRequested,
            "stop" => AuditEventActions.FinanceWorkerStopped,
            _ => AuditEventActions.FinanceWorkerFailureAcknowledged
        };
        try
        {
            switch (action)
            {
                case "retry" when CanRetry(execution):
                    execution.Queue(now, string.IsNullOrWhiteSpace(correlationId) ? $"finance-worker-retry:{execution.Id:N}:{Guid.NewGuid():N}" : correlationId, resetAttempts: true);
                    break;
                case "retry":
                    throw new FinanceWorkerOperationException(FinanceWorkerOperationReasonCodes.RetryNotAllowed,
                        "This failure is not safe to retry. Resolve or acknowledge it instead.");
                case "stop" when CanStop(execution):
                    execution.Cancel(actorUserId, reason, now);
                    break;
                case "stop":
                    throw new FinanceWorkerOperationException(FinanceWorkerOperationReasonCodes.StopNotAllowed,
                        "This work has already started or can no longer be stopped safely.");
                case "acknowledge" when CanAcknowledge(execution):
                    execution.Acknowledge(actorUserId, reason, now);
                    break;
                default:
                    throw new FinanceWorkerOperationException(FinanceWorkerOperationReasonCodes.AcknowledgeNotAllowed,
                        "Only an unacknowledged terminal failure can be acknowledged.");
            }
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, actorUserId, auditAction,
                "finance_background_execution", execution.Id.ToString("N"), AuditEventOutcomes.Succeeded, reason,
                Metadata: new Dictionary<string, string?>
                {
                    ["worker"] = ResolveWorkerKey(execution), ["status"] = execution.Status.ToStorageValue(),
                    ["attemptCount"] = execution.AttemptCount.ToString(), ["version"] = execution.Version.ToString()
                }, CorrelationId: correlationId), cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new FinanceWorkerOperationException(FinanceWorkerOperationReasonCodes.StaleVersion,
                "This work item changed while the action was being applied. Refresh it before trying again.");
        }

        _telemetry.OperatorAction(action, execution.CompanyId, ResolveWorkerKey(execution), execution.Status.ToStorageValue());
        _logger.LogInformation("Finance worker operator action {Action} applied to execution {ExecutionId} for company {CompanyId}. Worker={WorkerKey} Status={Status} Attempt={AttemptCount}.",
            action, execution.Id, companyId, ResolveWorkerKey(execution), execution.Status, execution.AttemptCount);
        var attempts = await _db.BackgroundExecutionAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BackgroundExecutionId == execution.Id)
            .OrderByDescending(x => x.AttemptNumber).ToListAsync(cancellationToken);
        return Map(execution, attempts, BuildCatalog());
    }

    private async Task<FinanceWorkerHealthDto> EvaluateHealthAsync(Guid companyId,
        IReadOnlyList<FinanceWorkerCatalogItemDto> catalog, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var executions = _db.BackgroundExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && FinanceExecutionTypes.Contains(x.ExecutionType));
        var queued = await executions.LongCountAsync(x => x.Status == BackgroundExecutionStatus.Pending || x.Status == BackgroundExecutionStatus.RetryScheduled, cancellationToken);
        var leased = await executions.LongCountAsync(x => x.Status == BackgroundExecutionStatus.InProgress && x.LeaseExpiresUtc > now, cancellationToken);
        var expiredBefore = now.AddSeconds(-Math.Max(0, _options.Value.LeaseGraceSeconds));
        var expired = await executions.LongCountAsync(x => x.Status == BackgroundExecutionStatus.InProgress &&
            (x.LeaseExpiresUtc == null || x.LeaseExpiresUtc <= expiredBefore), cancellationToken);
        var exhausted = await executions.LongCountAsync(x => x.AcknowledgedUtc == null &&
            (x.Status == BackgroundExecutionStatus.Failed || x.Status == BackgroundExecutionStatus.Blocked || x.Status == BackgroundExecutionStatus.Escalated) &&
            x.AttemptCount >= x.MaxAttempts, cancellationToken);
        exhausted += await _db.AccountingMigrationRuns.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == companyId && x.Status == AccountingMigrationRunStatuses.Failed, cancellationToken);
        var poison = await executions.LongCountAsync(x => x.AcknowledgedUtc == null &&
            (x.FailureCategory == BackgroundExecutionFailureCategory.Validation ||
             x.FailureCategory == BackgroundExecutionFailureCategory.PoisonPayload), cancellationToken);
        var reconciliation = await _db.AccountingProviderExports.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == companyId && x.Status == AccountingProviderExportStatuses.ReconciliationRequired, cancellationToken);
        reconciliation += await _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == companyId && x.ReconciliationNeeded, cancellationToken);
        reconciliation += await _db.AccountingProviderSwitchCutoverExecutions.IgnoreQueryFilters().AsNoTracking()
            .LongCountAsync(x => x.CompanyId == companyId && x.ProviderReconciliationRequired, cancellationToken);
        var oldest = await executions.Where(x => x.Status == BackgroundExecutionStatus.Pending || x.Status == BackgroundExecutionStatus.RetryScheduled)
            .OrderBy(x => x.CreatedUtc).Select(x => (DateTime?)x.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
        var missing = catalog.Where(x => !x.IsConfigured).Select(x => x.ConfigurationSection).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
        var issues = new List<string>();
        if (missing.Count > 0) issues.Add("One or more Finance workers do not have an explicit production configuration section.");
        if (expired > 0) issues.Add("Expired worker leases are waiting for bounded recovery.");
        if (exhausted > 0) issues.Add("Exhausted failures need an operator decision.");
        if (poison > 0) issues.Add("Invalid or poison work needs correction or acknowledgement.");
        if (reconciliation > 0) issues.Add("Possible provider success must be reconciled before retry.");
        if (oldest.HasValue && oldest.Value <= now.AddMinutes(-Math.Max(1, _options.Value.BacklogWarningMinutes)))
            issues.Add("The oldest queued Finance work is above the configured backlog age objective.");
        var status = missing.Count > 0 ? AccountingReadinessStatuses.Blocked : issues.Count > 0 ? AccountingReadinessStatuses.Attention : AccountingReadinessStatuses.Ready;
        return new FinanceWorkerHealthDto(companyId, status, now, queued, leased, expired, exhausted, poison,
            reconciliation, oldest, missing, issues);
    }

    private IReadOnlyList<FinanceWorkerCatalogItemDto> BuildCatalog() => FinanceWorkerCatalog.All.Select(x =>
    {
        var section = _configuration.GetSection(x.ConfigurationSection);
        var configured = section.Exists();
        return new FinanceWorkerCatalogItemDto(x.Key, x.DisplayName, x.Category, x.DurableUnit, x.Trigger,
            x.ClaimAndLease, x.BatchBound, x.IdempotencyIdentity, x.RetryContract, x.CancellationContract,
            x.ProgressAndTerminalStates, x.OperatorAction, x.ConfigurationSection, configured,
            configured ? section.GetValue<bool?>("Enabled") ?? true : false);
    }).ToList();

    private static FinanceWorkerWorkItemDto Map(BackgroundExecution execution, IReadOnlyList<BackgroundExecutionAttempt> attempts,
        IReadOnlyList<FinanceWorkerCatalogItemDto> catalog)
    {
        var key = ResolveWorkerKey(execution);
        var name = catalog.FirstOrDefault(x => x.Key == key)?.DisplayName ?? "Finance background work";
        var status = MapStatus(execution);
        var actions = new FinanceWorkerAllowedActionsDto(CanRetry(execution), CanStop(execution), CanAcknowledge(execution), false,
            CanRetry(execution) ? "The recorded failure is safe for a bounded manual retry after remediation."
            : CanStop(execution) ? "The work is still queued and can be stopped before it starts."
            : CanAcknowledge(execution) ? "Review the safe failure details and acknowledge it when no retry is appropriate."
            : "No operator action is safe for this state.");
        return new FinanceWorkerWorkItemDto(execution.Id, execution.CompanyId, key, name, execution.RelatedEntityId,
            status, Label(status), execution.AttemptCount, execution.MaxAttempts, execution.CreatedUtc, execution.UpdatedUtc,
            execution.NextRetryUtc, execution.LeaseExpiresUtc, execution.FailureCategory?.ToStorageValue(), execution.FailureCode,
            execution.FailureMessage, execution.AcknowledgedUtc, execution.Version, actions,
            attempts.Select(x => new FinanceWorkerAttemptDto(x.Id, x.AttemptNumber, x.Outcome,
                x.FailureCategory?.ToStorageValue(), x.FailureCode, x.SafeSummary, x.StartedUtc, x.CompletedUtc,
                x.DurationMilliseconds)).ToList());
    }

    private static bool CanRetry(BackgroundExecution x) => x.AcknowledgedUtc == null &&
        x.Status is BackgroundExecutionStatus.Failed or BackgroundExecutionStatus.Blocked or BackgroundExecutionStatus.Escalated &&
        x.FailureCategory is BackgroundExecutionFailureCategory.TransientInfrastructure or BackgroundExecutionFailureCategory.LockContention or
            BackgroundExecutionFailureCategory.ExternalDependencyTimeout or BackgroundExecutionFailureCategory.ExternalDependencyUnavailable or
            BackgroundExecutionFailureCategory.RateLimited or BackgroundExecutionFailureCategory.Configuration or
            BackgroundExecutionFailureCategory.Concurrency or BackgroundExecutionFailureCategory.Persistence or BackgroundExecutionFailureCategory.ObjectStorage;
    private static bool CanStop(BackgroundExecution x) =>
        x.Status is BackgroundExecutionStatus.Pending or BackgroundExecutionStatus.RetryScheduled &&
        x.ExecutionType is BackgroundExecutionType.FinanceReportRegeneration or BackgroundExecutionType.FinanceInsightRefresh;
    private static bool CanAcknowledge(BackgroundExecution x) => x.AcknowledgedUtc == null &&
        x.Status is BackgroundExecutionStatus.Failed or BackgroundExecutionStatus.Blocked or BackgroundExecutionStatus.Escalated;

    private static string ResolveWorkerKey(BackgroundExecution x) => x.ExecutionType switch
    {
        BackgroundExecutionType.FinanceSeed => "finance-seed",
        BackgroundExecutionType.FinanceReportRegeneration => "report-regeneration",
        BackgroundExecutionType.FinanceInsightRefresh => "insights-snapshot",
        _ => "finance-background-work"
    };
    private static BackgroundExecutionType? ResolveExecutionType(string key) => key.Trim().ToLowerInvariant() switch
    {
        "finance-seed" or "finance-seed-backfill" => BackgroundExecutionType.FinanceSeed,
        "report-regeneration" => BackgroundExecutionType.FinanceReportRegeneration,
        "insights-snapshot" or "analytics-startup-refresh" => BackgroundExecutionType.FinanceInsightRefresh,
        _ => null
    };
    private static BackgroundExecutionStatus[] ResolveStatuses(string status) => status.Trim().ToLowerInvariant() switch
    {
        FinanceWorkerWorkStatuses.Queued => [BackgroundExecutionStatus.Pending],
        FinanceWorkerWorkStatuses.InProgress => [BackgroundExecutionStatus.InProgress],
        FinanceWorkerWorkStatuses.RetryScheduled => [BackgroundExecutionStatus.RetryScheduled],
        FinanceWorkerWorkStatuses.NeedsAttention => [BackgroundExecutionStatus.Failed, BackgroundExecutionStatus.Blocked, BackgroundExecutionStatus.Escalated],
        FinanceWorkerWorkStatuses.Completed => [BackgroundExecutionStatus.Succeeded],
        FinanceWorkerWorkStatuses.Stopped => [BackgroundExecutionStatus.Cancelled],
        _ => Enum.GetValues<BackgroundExecutionStatus>()
    };
    private static string MapStatus(BackgroundExecution x) => x.Status switch
    {
        BackgroundExecutionStatus.Pending => FinanceWorkerWorkStatuses.Queued,
        BackgroundExecutionStatus.InProgress => FinanceWorkerWorkStatuses.InProgress,
        BackgroundExecutionStatus.RetryScheduled => FinanceWorkerWorkStatuses.RetryScheduled,
        BackgroundExecutionStatus.Succeeded => FinanceWorkerWorkStatuses.Completed,
        BackgroundExecutionStatus.Cancelled => FinanceWorkerWorkStatuses.Stopped,
        _ => FinanceWorkerWorkStatuses.NeedsAttention
    };
    private static string Label(string status) => status switch
    {
        FinanceWorkerWorkStatuses.Queued => "Queued",
        FinanceWorkerWorkStatuses.InProgress => "In progress",
        FinanceWorkerWorkStatuses.RetryScheduled => "Retry scheduled",
        FinanceWorkerWorkStatuses.Completed => "Completed",
        FinanceWorkerWorkStatuses.Stopped => "Stopped",
        _ => "Needs attention"
    };
    private static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
    }
}

public sealed class FinanceWorkerOperationsTelemetry
{
    internal const string MeterName = "VirtualCompany.Finance.Workers";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> OperatorActions = Meter.CreateCounter<long>("finance.worker.operator_actions");
    private static readonly Histogram<long> Backlog = Meter.CreateHistogram<long>("finance.worker.backlog");
    private static readonly Histogram<long> Failures = Meter.CreateHistogram<long>("finance.worker.failures");
    private static readonly Counter<long> Attempts = Meter.CreateCounter<long>("finance.worker.attempts");
    private static readonly Histogram<long> AttemptDuration = Meter.CreateHistogram<long>("finance.worker.attempt.duration", "ms");
    private static readonly Histogram<long> RetryDelay = Meter.CreateHistogram<long>("finance.worker.retry.delay", "ms");

    public void OperatorAction(string action, Guid companyId, string worker, string status) =>
        OperatorActions.Add(1, new("company_id", companyId.ToString("D")), new("action", action),
            new("worker", worker), new("status", status));

    public void AttemptCompleted(BackgroundExecution execution, BackgroundExecutionAttempt attempt)
    {
        var tags = new TagList
        {
            { "company_id", execution.CompanyId.ToString("D") },
            { "worker", attempt.WorkerName },
            { "outcome", attempt.Outcome },
            { "failure_class", execution.FailureCategory?.ToStorageValue() ?? "none" }
        };
        Attempts.Add(1, tags);
        if (attempt.DurationMilliseconds.HasValue) AttemptDuration.Record(attempt.DurationMilliseconds.Value, tags);
        if (execution.NextRetryUtc.HasValue && attempt.CompletedUtc.HasValue)
        {
            RetryDelay.Record(Math.Max(0, (long)(execution.NextRetryUtc.Value - attempt.CompletedUtc.Value).TotalMilliseconds), tags);
        }
    }

    public void ObserveHealth(FinanceWorkerHealthDto health)
    {
        Backlog.Record(
            health.QueuedCount,
            new KeyValuePair<string, object?>("company_id", health.CompanyId.ToString("D")),
            new KeyValuePair<string, object?>("status", health.Status));
        Failures.Record(health.ExhaustedFailureCount + health.PoisonWorkCount,
            new KeyValuePair<string, object?>("company_id", health.CompanyId.ToString("D")),
            new KeyValuePair<string, object?>(
                "reconciliation_required",
                health.ReconciliationRequiredCount > 0));
    }
}

public sealed class FinanceWorkerReadinessHealthCheck : IHealthCheck
{
    private readonly IConfiguration _configuration;
    public FinanceWorkerReadinessHealthCheck(IConfiguration configuration) => _configuration = configuration;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var missing = FinanceWorkerCatalog.All.Select(x => x.ConfigurationSection).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(x => !_configuration.GetSection(x).Exists()).Order().ToArray();
        return Task.FromResult(missing.Length == 0
            ? HealthCheckResult.Healthy("Finance worker configuration is explicit.", new Dictionary<string, object> { ["configuredWorkers"] = FinanceWorkerCatalog.All.Count })
            : HealthCheckResult.Unhealthy("Finance worker configuration is incomplete.", data: new Dictionary<string, object>
            {
                ["readinessCode"] = "finance_worker_configuration_missing", ["missingSections"] = missing
            }));
    }
}
