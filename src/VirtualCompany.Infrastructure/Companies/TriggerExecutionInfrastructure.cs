using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class TriggerWorkerOptions
{
    public const string SectionName = "TriggerWorker";

    public bool Enabled { get; set; } = true;
    public int PollingIntervalSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 50;
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryBackoffSeconds { get; set; } = 10;
    public string LockKey { get; set; } = "trigger-evaluation-worker";
    public int LockTtlSeconds { get; set; } = 120;
}

public sealed class EfTriggerExecutionAttemptRepository : ITriggerExecutionAttemptRepository
{
    private readonly VirtualCompanyDbContext _dbContext;

    public EfTriggerExecutionAttemptRepository(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TriggerExecutionAttemptReservation> ReserveAsync(
        TriggerExecutionWorkItem workItem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        var attempt = new TriggerExecutionAttempt(
            Guid.NewGuid(),
            workItem.CompanyId,
            workItem.TriggerId,
            workItem.TriggerType,
            workItem.AgentId,
            workItem.OccurrenceUtc,
            workItem.CorrelationId,
            workItem.IdempotencyKey,
            retryAttempt: 1);

        await _dbContext.TriggerExecutionAttempts.AddAsync(attempt, cancellationToken);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new TriggerExecutionAttemptReservation(attempt, true, false);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _dbContext.ChangeTracker.Clear();
            var existing = await _dbContext.TriggerExecutionAttempts
                .IgnoreQueryFilters()
                .SingleAsync(x => x.IdempotencyKey == workItem.IdempotencyKey, cancellationToken);

            if (existing.HasFinalOutcome)
            {
                return new TriggerExecutionAttemptReservation(existing, false, true);
            }

            if (existing.Status == TriggerExecutionAttemptStatus.RetryScheduled &&
                existing.NextRetryUtc.HasValue &&
                existing.NextRetryUtc.Value > DateTime.UtcNow)
            {
                return new TriggerExecutionAttemptReservation(existing, false, false, true);
            }

            existing.MarkRetried(existing.RetryAttemptCount + 1);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new TriggerExecutionAttemptReservation(existing, false, false);
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("trigger_execution_attempts", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("idempotency_key", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class AgentTriggerExecutionPolicyChecker : ITriggerExecutionPolicyChecker
{
    private readonly VirtualCompanyDbContext _dbContext;

    public AgentTriggerExecutionPolicyChecker(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TriggerExecutionPolicyDecision> CheckAsync(
        TriggerExecutionWorkItem workItem,
        CancellationToken cancellationToken)
    {
        if (workItem.CompanyId == Guid.Empty)
        {
            return TriggerExecutionPolicyDecision.Deny("Trigger execution is missing tenant context.");
        }

        if (workItem.AgentId is not Guid agentId)
        {
            return TriggerExecutionPolicyDecision.Allow(new Dictionary<string, string?>
            {
                ["policy"] = "tenant_context_only"
            });
        }

        var agent = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == workItem.CompanyId && x.Id == agentId, cancellationToken);

        if (agent is null)
        {
            return TriggerExecutionPolicyDecision.Deny("Trigger execution was blocked because the target agent was not found in the tenant.");
        }

        if (!agent.CanReceiveAssignments)
        {
            return TriggerExecutionPolicyDecision.Deny(
                Agent.GetAssignmentErrorMessage(agent.Status),
                new Dictionary<string, string?>
                {
                    ["agentStatus"] = agent.Status.ToStorageValue()
                });
        }

        return TriggerExecutionPolicyDecision.Allow(new Dictionary<string, string?>
        {
            ["agentStatus"] = agent.Status.ToStorageValue(),
            ["autonomyLevel"] = agent.AutonomyLevel.ToStorageValue()
        });
    }
}

public sealed class SingleAgentTriggerOrchestrationDispatcher : ITriggerOrchestrationDispatcher
{
    private readonly ISingleAgentOrchestrationService _orchestrationService;
    private readonly IInternalWorkflowEventTriggerService _workflowEventTriggerService;

    public SingleAgentTriggerOrchestrationDispatcher(
        ISingleAgentOrchestrationService orchestrationService,
        IInternalWorkflowEventTriggerService workflowEventTriggerService)
    {
        _orchestrationService = orchestrationService;
        _workflowEventTriggerService = workflowEventTriggerService;
    }

    public async Task<TriggerExecutionDispatchResult> DispatchAsync(
        TriggerExecutionWorkItem workItem,
        CancellationToken cancellationToken)
    {
        if (string.Equals(workItem.TriggerType, TriggerExecutionTypes.WorkflowCondition, StringComparison.OrdinalIgnoreCase))
        {
            var workflowResult = await _workflowEventTriggerService.HandleAsync(
                new InternalWorkflowEvent(
                    workItem.CompanyId,
                    "condition",
                    workItem.IdempotencyKey,
                    workItem.Payload),
                cancellationToken);

            var referenceId = workflowResult.StartedInstances.FirstOrDefault()?.Id.ToString("N") ?? workItem.IdempotencyKey;
            return new TriggerExecutionDispatchResult(
                AuditTargetTypes.WorkflowInstance,
                referenceId,
                new Dictionary<string, string?>
                {
                    ["workflowInstancesStarted"] = workflowResult.StartedInstances.Count.ToString(),
                    ["eventName"] = workflowResult.EventName
                });
        }

        if (workItem.AgentId is not Guid agentId)
        {
            throw new TriggerExecutionPermanentException("Agent scheduled trigger dispatch requires an agent id.");
        }

        var result = await _orchestrationService.ExecuteAsync(
            new OrchestrationRequest(
                workItem.CompanyId,
                AgentId: agentId,
                UserInput: $"Execute scheduled trigger {workItem.TriggerId:N}.",
                InitiatingActorType: AuditActorTypes.System,
                CorrelationId: workItem.CorrelationId,
                IntentHint: OrchestrationIntentValues.ExecuteTask,
                ActorMetadata: workItem.Payload),
            cancellationToken);

        return new TriggerExecutionDispatchResult(
            AuditTargetTypes.WorkTask,
            result.TaskId.ToString("N"),
            new Dictionary<string, string?>
            {
                ["orchestrationId"] = result.OrchestrationId.ToString("N"),
                ["orchestrationStatus"] = result.Status,
                ["agentId"] = result.AgentId.ToString("N")
            });
    }
}

public sealed class TriggerAuditEventWriter : ITriggerAuditEventWriter
{
    private readonly IAuditEventWriter _auditEventWriter;

    public TriggerAuditEventWriter(IAuditEventWriter auditEventWriter)
    {
        _auditEventWriter = auditEventWriter;
    }

    public Task WriteEvaluationStartedAsync(
        TriggerAuditEventContext context,
        CancellationToken cancellationToken) =>
        WriteEvaluationAsync(
            context,
            AuditEventActions.TriggerEvaluationStarted,
            AuditEventOutcomes.Pending,
            "Trigger evaluation started.",
            null,
            cancellationToken);

    public Task WriteEvaluationSkippedAsync(
        TriggerAuditEventContext context,
        string reason,
        IReadOnlyDictionary<string, string?>? metadata,
        CancellationToken cancellationToken) =>
        WriteEvaluationAsync(
            context,
            AuditEventActions.TriggerEvaluationSkipped,
            AuditEventOutcomes.Succeeded,
            string.IsNullOrWhiteSpace(reason) ? "Trigger evaluation skipped." : reason.Trim(),
            metadata,
            cancellationToken);

    public Task WriteExecutionAttemptCreatedAsync(
        TriggerExecutionAttempt attempt,
        CancellationToken cancellationToken) =>
        WriteAttemptAsync(
            attempt,
            attempt.Status == TriggerExecutionAttemptStatus.Retried
                ? AuditEventActions.TriggerExecutionAttemptRetried
                : AuditEventActions.TriggerExecutionAttemptStarted,
            AuditEventOutcomes.Pending,
            attempt.Status == TriggerExecutionAttemptStatus.Retried
                ? "Retrying trigger execution attempt."
                : "Trigger execution attempt started.",
            null,
            cancellationToken);

    public Task WriteRetryDeferredAsync(
        TriggerExecutionAttempt attempt,
        string reason,
        CancellationToken cancellationToken) =>
        WriteAttemptAsync(
            attempt,
            AuditEventActions.TriggerExecutionAttemptRetryDeferred,
            AuditEventOutcomes.Pending,
            string.IsNullOrWhiteSpace(reason)
                ? "Trigger execution retry was deferred until the recorded retry window."
                : reason.Trim(),
            new Dictionary<string, string?>
            {
                ["retryDeferred"] = "true"
            },
            cancellationToken);

    public Task WriteDuplicatePreventedAsync(
        TriggerExecutionAttempt attempt,
        CancellationToken cancellationToken) =>
        WriteAttemptAsync(
            attempt,
            AuditEventActions.TriggerExecutionAttemptDuplicateSkipped,
            AuditEventOutcomes.Succeeded,
            "Duplicate trigger execution attempt skipped by persisted idempotency key.",
            new Dictionary<string, string?>
            {
                ["executionStatus"] = TriggerExecutionAttemptStatus.DuplicateSkipped.ToStorageValue(),
                ["previousExecutionStatus"] = attempt.Status.ToStorageValue()
            },
            cancellationToken);

    public Task WritePolicyDeniedAsync(
        TriggerExecutionAttempt attempt,
        IReadOnlyDictionary<string, string?>? metadata,
        CancellationToken cancellationToken) =>
        WriteAttemptAsync(
            attempt,
            AuditEventActions.TriggerExecutionAttemptBlocked,
            AuditEventOutcomes.Denied,
            attempt.DenialReason,
            metadata,
            cancellationToken);

    public Task WriteOrchestrationStartRequestedAsync(
        TriggerExecutionAttempt attempt,
        CancellationToken cancellationToken) =>
        WriteAttemptAsync(
            attempt,
            AuditEventActions.TriggerOrchestrationStartRequested,
            AuditEventOutcomes.Requested,
            "Trigger orchestration start requested after policy approval.",
            null,
            cancellationToken);

    public Task WriteOrchestrationStartedAsync(
        TriggerExecutionAttempt attempt,
        IReadOnlyDictionary<string, string?>? metadata,
        CancellationToken cancellationToken) =>
        WriteAttemptAsync(
            attempt,
            AuditEventActions.TriggerExecutionAttemptDispatched,
            AuditEventOutcomes.Succeeded,
            "Trigger execution dispatched to orchestration.",
            metadata,
            cancellationToken);

    public Task WriteRetryScheduledAsync(TriggerExecutionAttempt attempt, CancellationToken cancellationToken) =>
        WriteAttemptAsync(
            attempt,
            AuditEventActions.TriggerExecutionAttemptRetryScheduled,
            AuditEventOutcomes.Pending,
            "Transient trigger execution failure recorded; retry remains eligible under the idempotency key.",
            null,
            cancellationToken);

    public Task WriteDeadLetteredAsync(TriggerExecutionAttempt attempt, CancellationToken cancellationToken) =>
        WriteAttemptAsync(
            attempt,
            AuditEventActions.TriggerExecutionAttemptDeadLettered,
            AuditEventOutcomes.Failed,
            attempt.FailureDetails,
            new Dictionary<string, string?> { ["deadLettered"] = "true" },
            cancellationToken);

    public Task WriteExecutionFailedAsync(TriggerExecutionAttempt attempt, CancellationToken cancellationToken) =>
        WriteAttemptAsync(
            attempt,
            AuditEventActions.TriggerExecutionAttemptFailed,
            AuditEventOutcomes.Failed,
            attempt.FailureDetails,
            null,
            cancellationToken);

    private Task WriteEvaluationAsync(
        TriggerAuditEventContext context,
        string action,
        string outcome,
        string rationale,
        IReadOnlyDictionary<string, string?>? extraMetadata,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["triggerId"] = context.TriggerId.ToString("N"),
            ["companyId"] = context.CompanyId.ToString("N"),
            ["triggerType"] = context.TriggerType,
            ["agentId"] = context.AgentId?.ToString("N"),
            ["executionStatus"] = action == AuditEventActions.TriggerEvaluationSkipped ? "evaluation_skipped" : "evaluation_started",
            ["correlationId"] = context.CorrelationId,
            ["idempotencyKey"] = context.IdempotencyKey,
            ["occurrenceUtc"] = context.OccurrenceUtc.ToString("O"),
            ["workerName"] = context.WorkerName
        };

        MergeMetadata(metadata, extraMetadata);
        return _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                context.CompanyId,
                AuditActorTypes.System,
                context.AgentId,
                action,
                AuditTargetTypes.TriggerEvaluation,
                context.TriggerId.ToString("N"),
                outcome,
                rationale,
                ["trigger_execution_worker", "trigger_evaluation"],
                metadata,
                context.CorrelationId,
                DateTime.UtcNow),
            cancellationToken);
    }

    private Task WriteAttemptAsync(
        TriggerExecutionAttempt attempt,
        string action,
        string outcome,
        string? rationale,
        IReadOnlyDictionary<string, string?>? extraMetadata,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["triggerId"] = attempt.TriggerId.ToString("N"),
            ["companyId"] = attempt.CompanyId.ToString("N"),
            ["triggerType"] = attempt.TriggerType,
            ["agentId"] = attempt.AgentId?.ToString("N"),
            ["executionStatus"] = attempt.Status.ToStorageValue(),
            ["correlationId"] = attempt.CorrelationId,
            ["idempotencyKey"] = attempt.IdempotencyKey,
            ["retryAttemptCount"] = attempt.RetryAttemptCount.ToString(),
            ["denialReason"] = attempt.DenialReason,
            ["failureDetails"] = attempt.FailureDetails,
            ["dispatchReferenceType"] = attempt.DispatchReferenceType,
            ["dispatchReferenceId"] = attempt.DispatchReferenceId,
            ["nextRetryUtc"] = attempt.NextRetryUtc?.ToString("O"),
            ["deadLettered"] = (attempt.Status == TriggerExecutionAttemptStatus.DeadLettered).ToString()
        };

        MergeMetadata(metadata, extraMetadata);
        return _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                attempt.CompanyId,
                AuditActorTypes.System,
                attempt.AgentId,
                action,
                AuditTargetTypes.TriggerExecutionAttempt,
                attempt.Id.ToString("N"),
                outcome,
                rationale,
                ["trigger_execution_worker", "trigger_execution_attempts"],
                metadata,
                attempt.CorrelationId,
                DateTime.UtcNow),
            cancellationToken);
    }

    private static void MergeMetadata(
        Dictionary<string, string?> metadata,
        IReadOnlyDictionary<string, string?>? extraMetadata)
    {
        if (extraMetadata is null)
        {
            return;
        }

        foreach (var (key, value) in extraMetadata)
        {
            metadata[key] = value;
        }
    }
}

public sealed class TriggerExecutionService : ITriggerExecutionService
{
    private readonly ITriggerExecutionAttemptRepository _attemptRepository;
    private readonly ITriggerExecutionPolicyChecker _policyChecker;
    private readonly ITriggerOrchestrationDispatcher _dispatcher;
    private readonly ITriggerAuditEventWriter _triggerAuditEventWriter;
    private readonly VirtualCompanyDbContext _dbContext;

    public TriggerExecutionService(
        ITriggerExecutionAttemptRepository attemptRepository,
        ITriggerExecutionPolicyChecker policyChecker,
        ITriggerOrchestrationDispatcher dispatcher,
        ITriggerAuditEventWriter triggerAuditEventWriter,
        VirtualCompanyDbContext dbContext)
    {
        _attemptRepository = attemptRepository;
        _policyChecker = policyChecker;
        _dispatcher = dispatcher;
        _triggerAuditEventWriter = triggerAuditEventWriter;
        _dbContext = dbContext;
    }

    public async Task<TriggerExecutionAttemptStatus> ProcessScheduledTriggerAsync(
        AgentScheduledTriggerExecutionRequestMessage message,
        int maxRetryAttempts,
        CancellationToken cancellationToken,
        int retryBackoffSeconds = 0)
    {
        var idempotencyKey = string.IsNullOrWhiteSpace(message.IdempotencyKey)
            ? TriggerExecutionIdempotency.ForScheduledAgentTrigger(message.CompanyId, message.TriggerId, message.ScheduledAtUtc)
            : message.IdempotencyKey.Trim();

        var workItem = new TriggerExecutionWorkItem(
            message.CompanyId,
            message.TriggerId,
            TriggerExecutionTypes.AgentScheduled,
            message.AgentId,
            NormalizeUtc(message.ScheduledAtUtc),
            string.IsNullOrWhiteSpace(message.CorrelationId) ? TriggerExecutionIdempotency.CorrelationFromIdempotencyKey(idempotencyKey) : message.CorrelationId.Trim(),
            idempotencyKey,
            new Dictionary<string, JsonNode?>(message.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase))
            {
                ["triggerId"] = JsonValue.Create(message.TriggerId.ToString("N")),
                ["triggerCode"] = JsonValue.Create(message.TriggerCode),
                ["triggerType"] = JsonValue.Create(TriggerExecutionTypes.AgentScheduled),
                ["scheduledAtUtc"] = JsonValue.Create(NormalizeUtc(message.ScheduledAtUtc)),
                ["cronExpression"] = JsonValue.Create(message.CronExpression),
                ["timeZoneId"] = JsonValue.Create(message.TimeZoneId),
                ["correlationId"] = JsonValue.Create(string.IsNullOrWhiteSpace(message.CorrelationId) ? idempotencyKey : message.CorrelationId.Trim()),
                ["idempotencyKey"] = JsonValue.Create(idempotencyKey)
            });

        return await EvaluateAndDispatchAsync(workItem, maxRetryAttempts, cancellationToken, retryBackoffSeconds);
    }

    public async Task<TriggerExecutionAttemptStatus> EvaluateAndDispatchAsync(
        TriggerExecutionWorkItem workItem,
        int maxRetryAttempts,
        CancellationToken cancellationToken,
        int retryBackoffSeconds = 0)
    {
        var effectiveMaxRetryAttempts = Math.Max(1, maxRetryAttempts);
        var reservation = await _attemptRepository.ReserveAsync(workItem, cancellationToken);
        var attempt = reservation.Attempt;
        if (reservation.DuplicateFinalOutcome)
        {
            await _triggerAuditEventWriter.WriteDuplicatePreventedAsync(attempt, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return attempt.Status == TriggerExecutionAttemptStatus.DuplicateSkipped ? attempt.Status : TriggerExecutionAttemptStatus.DuplicateSkipped;
        }

        if (reservation.RetryDeferred)
        {
            if (!await HasRetryDeferredAuditAsync(attempt, cancellationToken))
            {
                await _triggerAuditEventWriter.WriteRetryDeferredAsync(attempt, "Trigger execution retry is not due yet.", cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return TriggerExecutionAttemptStatus.RetryScheduled;
        }

        await _triggerAuditEventWriter.WriteExecutionAttemptCreatedAsync(attempt, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var policy = await _policyChecker.CheckAsync(workItem, cancellationToken);
            if (!policy.Allowed)
            {
                attempt.MarkBlocked(policy.DenialReason ?? "Trigger execution was blocked by policy.");
                await _attemptRepository.SaveChangesAsync(cancellationToken);
                await _triggerAuditEventWriter.WritePolicyDeniedAsync(attempt, policy.Metadata, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return TriggerExecutionAttemptStatus.Blocked;
            }

            await _triggerAuditEventWriter.WriteOrchestrationStartRequestedAsync(attempt, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var dispatch = await _dispatcher.DispatchAsync(workItem, cancellationToken);
            attempt.MarkDispatched(dispatch.DispatchReferenceType, dispatch.DispatchReferenceId);
            await MarkSourceTriggerRunAsync(workItem, cancellationToken);
            await _attemptRepository.SaveChangesAsync(cancellationToken);

            await _triggerAuditEventWriter.WriteOrchestrationStartedAsync(attempt, dispatch.Metadata, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return TriggerExecutionAttemptStatus.Dispatched;
        }
        catch (TriggerExecutionPermanentException ex)
        {
            attempt.MarkFailed(ex.Message);
            await _attemptRepository.SaveChangesAsync(cancellationToken);
            await _triggerAuditEventWriter.WriteExecutionFailedAsync(attempt, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return TriggerExecutionAttemptStatus.Failed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (attempt.RetryAttemptCount >= effectiveMaxRetryAttempts)
            {
                attempt.MarkDeadLettered(ex.Message);
                await _attemptRepository.SaveChangesAsync(cancellationToken);
                await _triggerAuditEventWriter.WriteExecutionFailedAsync(attempt, cancellationToken);
                await _triggerAuditEventWriter.WriteDeadLetteredAsync(attempt, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return TriggerExecutionAttemptStatus.DeadLettered;
            }

            attempt.MarkRetryScheduled(
                ex.Message,
                DateTime.UtcNow.Add(ComputeRetryDelay(attempt.RetryAttemptCount, retryBackoffSeconds)));
            await _attemptRepository.SaveChangesAsync(cancellationToken);
            await _triggerAuditEventWriter.WriteExecutionFailedAsync(attempt, cancellationToken);
            await _triggerAuditEventWriter.WriteRetryScheduledAsync(attempt, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private static TimeSpan ComputeRetryDelay(int attemptCount, int retryBackoffSeconds)
    {
        var baseDelaySeconds = Math.Max(0, retryBackoffSeconds);
        var multiplier = Math.Pow(2, Math.Max(0, attemptCount - 1));
        return TimeSpan.FromSeconds(Math.Min(3600, baseDelaySeconds * multiplier));
    }

    private Task<bool> HasRetryDeferredAuditAsync(TriggerExecutionAttempt attempt, CancellationToken cancellationToken) =>
        _dbContext.AuditEvents
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.CompanyId == attempt.CompanyId &&
                    x.Action == AuditEventActions.TriggerExecutionAttemptRetryDeferred &&
                    x.TargetType == AuditTargetTypes.TriggerExecutionAttempt &&
                    x.TargetId == attempt.Id.ToString("N"),
                cancellationToken);

    private async Task MarkSourceTriggerRunAsync(TriggerExecutionWorkItem workItem, CancellationToken cancellationToken)
    {
        if (!string.Equals(workItem.TriggerType, TriggerExecutionTypes.AgentScheduled, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var trigger = await _dbContext.AgentScheduledTriggers
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == workItem.CompanyId && x.Id == workItem.TriggerId, cancellationToken);

        trigger?.MarkRun(DateTime.UtcNow);
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public sealed class TriggerEvaluationWorker : ITriggerEvaluationWorker
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ITriggerExecutionService _executionService;
    private readonly IConditionTriggerEvaluationService _conditionEvaluationService;
    private readonly IScheduledTriggerNextRunCalculator _nextRunCalculator;
    private readonly ITriggerAuditEventWriter _triggerAuditEventWriter;
    private readonly ILogger<TriggerEvaluationWorker> _logger;

    public TriggerEvaluationWorker(
        VirtualCompanyDbContext dbContext,
        ITriggerExecutionService executionService,
        IConditionTriggerEvaluationService conditionEvaluationService,
        IScheduledTriggerNextRunCalculator nextRunCalculator,
        ITriggerAuditEventWriter triggerAuditEventWriter,
        ILogger<TriggerEvaluationWorker> logger)
    {
        _dbContext = dbContext;
        _executionService = executionService;
        _conditionEvaluationService = conditionEvaluationService;
        _nextRunCalculator = nextRunCalculator;
        _triggerAuditEventWriter = triggerAuditEventWriter;
        _logger = logger;
    }

    public async Task<TriggerExecutionRunResult> RunOnceAsync(
        DateTime dueAtUtc,
        int batchSize,
        int maxRetryAttempts,
        CancellationToken cancellationToken,
        int retryBackoffSeconds = 0)
    {
        var normalizedDueAtUtc = dueAtUtc.Kind == DateTimeKind.Utc ? dueAtUtc : dueAtUtc.ToUniversalTime();
        var effectiveBatchSize = Math.Max(1, batchSize);

        var scheduledEvaluated = 0;
        var conditionEvaluated = 0;
        var dispatched = 0;
        var blocked = 0;
        var duplicateSkipped = 0;
        var failed = 0;
        var retried = 0;
        var deadLettered = 0;

        var retryAttempts = await _dbContext.TriggerExecutionAttempts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.Status == TriggerExecutionAttemptStatus.RetryScheduled &&
                x.NextRetryUtc.HasValue &&
                x.NextRetryUtc <= normalizedDueAtUtc)
            .OrderBy(x => x.NextRetryUtc)
            .ThenBy(x => x.UpdatedUtc)
            .ThenBy(x => x.Id)
            .Take(effectiveBatchSize)
            .ToListAsync(cancellationToken);

        foreach (var retryAttempt in retryAttempts)
        {
            if (string.Equals(retryAttempt.TriggerType, TriggerExecutionTypes.AgentScheduled, StringComparison.OrdinalIgnoreCase))
            {
                scheduledEvaluated++;
            }
            else if (string.Equals(retryAttempt.TriggerType, TriggerExecutionTypes.WorkflowCondition, StringComparison.OrdinalIgnoreCase))
            {
                conditionEvaluated++;
            }

            var outcome = await ProcessRetryAttemptAsync(
                retryAttempt,
                normalizedDueAtUtc,
                maxRetryAttempts,
                retryBackoffSeconds,
                cancellationToken);
            Increment(outcome, ref dispatched, ref blocked, ref duplicateSkipped, ref failed, ref retried, ref deadLettered);
        }

        var remainingForScheduled = Math.Max(0, effectiveBatchSize - scheduledEvaluated - conditionEvaluated);
        var scheduled = remainingForScheduled == 0
            ? []
            : await _dbContext.AgentScheduledTriggers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.IsEnabled && x.NextRunUtc.HasValue && x.NextRunUtc <= normalizedDueAtUtc && (!x.DisabledUtc.HasValue || normalizedDueAtUtc <= x.DisabledUtc.Value))
                .OrderBy(x => x.NextRunUtc)
                .ThenBy(x => x.Id)
                .Take(remainingForScheduled)
                .ToListAsync(cancellationToken);

        foreach (var trigger in scheduled)
        {
            scheduledEvaluated++;
            if (!trigger.NextRunUtc.HasValue)
            {
                continue;
            }

            var scheduledAtUtc = trigger.NextRunUtc.Value.Kind == DateTimeKind.Utc ? trigger.NextRunUtc.Value : trigger.NextRunUtc.Value.ToUniversalTime();
            var nextRunUtc = _nextRunCalculator.GetNextRunUtc(trigger.CronExpression, trigger.TimeZoneId, scheduledAtUtc);
            var idempotencyKey = TriggerExecutionIdempotency.ForScheduledAgentTrigger(trigger.CompanyId, trigger.Id, scheduledAtUtc);
            var correlationId = TriggerExecutionIdempotency.CorrelationFromIdempotencyKey(idempotencyKey);
            await WriteEvaluationStartedAsync(trigger.CompanyId, trigger.Id, TriggerExecutionTypes.AgentScheduled, trigger.AgentId, scheduledAtUtc, correlationId, idempotencyKey, cancellationToken);

            TriggerExecutionAttemptStatus outcome;
            try
            {
                outcome = await _executionService.ProcessScheduledTriggerAsync(
                    new AgentScheduledTriggerExecutionRequestMessage(
                        trigger.CompanyId,
                        trigger.AgentId,
                        trigger.Id,
                        trigger.Code,
                        scheduledAtUtc,
                        trigger.CronExpression,
                        trigger.TimeZoneId,
                        trigger.Metadata,
                        correlationId,
                        idempotencyKey),
                    maxRetryAttempts,
                    cancellationToken,
                    retryBackoffSeconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Scheduled trigger {TriggerId} failed during evaluation and remains eligible for retry.", trigger.Id);
                outcome = TriggerExecutionAttemptStatus.RetryScheduled;
            }

            if (outcome != TriggerExecutionAttemptStatus.RetryScheduled)
            {
                await AdvanceScheduledTriggerAsync(trigger.CompanyId, trigger.Id, normalizedDueAtUtc, nextRunUtc, cancellationToken);
            }

            Increment(outcome, ref dispatched, ref blocked, ref duplicateSkipped, ref failed, ref retried, ref deadLettered);
        }

        var remaining = Math.Max(0, effectiveBatchSize - scheduledEvaluated - conditionEvaluated);
        if (remaining > 0)
        {
            var conditionTriggers = await _dbContext.WorkflowTriggers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(x => x.Definition)
                .Where(x => x.IsEnabled && x.EventName == "condition" && x.Definition.Active && x.Definition.TriggerType == WorkflowTriggerType.Event)
                .OrderBy(x => x.CompanyId)
                .ThenBy(x => x.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);

            foreach (var trigger in conditionTriggers)
            {
                if (!TryBuildCondition(trigger, out var condition, out var diagnostic))
                {
                    var skippedIdempotencyKey = TriggerExecutionIdempotency.ForWorkflowCondition(trigger.CompanyId, trigger.Id, normalizedDueAtUtc);
                    await _triggerAuditEventWriter.WriteEvaluationSkippedAsync(
                        new TriggerAuditEventContext(
                            trigger.CompanyId,
                            trigger.Id,
                            TriggerExecutionTypes.WorkflowCondition,
                            null,
                            normalizedDueAtUtc,
                            TriggerExecutionIdempotency.CorrelationFromIdempotencyKey(skippedIdempotencyKey),
                            skippedIdempotencyKey),
                        diagnostic ?? "Condition criteria could not be parsed.",
                        new Dictionary<string, string?> { ["skipReason"] = "invalid_condition_criteria" },
                        cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    _logger.LogWarning("Skipped condition trigger {TriggerId} because its condition criteria could not be parsed: {Diagnostic}", trigger.Id, diagnostic);
                    continue;
                }

                conditionEvaluated++;
                var evaluation = await _conditionEvaluationService.EvaluateAndPersistAsync(
                    new EvaluateConditionTriggerCommand(trigger.CompanyId, trigger.Id.ToString("N"), trigger.Id, condition, normalizedDueAtUtc),
                    cancellationToken);
                var idempotencyKey = TriggerExecutionIdempotency.ForWorkflowCondition(trigger.CompanyId, trigger.Id, evaluation.EvaluatedUtc);
                var correlationId = TriggerExecutionIdempotency.CorrelationFromIdempotencyKey(idempotencyKey);
                await WriteEvaluationStartedAsync(trigger.CompanyId, trigger.Id, TriggerExecutionTypes.WorkflowCondition, null, evaluation.EvaluatedUtc, correlationId, idempotencyKey, cancellationToken);

                if (!evaluation.ShouldFire)
                {
                    await _triggerAuditEventWriter.WriteEvaluationSkippedAsync(
                        new TriggerAuditEventContext(trigger.CompanyId, trigger.Id, TriggerExecutionTypes.WorkflowCondition, null, evaluation.EvaluatedUtc, correlationId, idempotencyKey),
                        evaluation.Diagnostic ?? "Condition evaluation did not fire.",
                        new Dictionary<string, string?>
                        {
                            ["skipReason"] = "condition_not_fired",
                            ["conditionOutcome"] = evaluation.Outcome.ToString(),
                            ["previousOutcome"] = evaluation.PreviousOutcome?.ToString(),
                            ["shouldFire"] = evaluation.ShouldFire.ToString()
                        },
                        cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    continue;
                }

                TriggerExecutionAttemptStatus outcome;
                try
                {
                    outcome = await _executionService.EvaluateAndDispatchAsync(
                        new TriggerExecutionWorkItem(
                            trigger.CompanyId,
                            trigger.Id,
                            TriggerExecutionTypes.WorkflowCondition,
                            null,
                            evaluation.EvaluatedUtc,
                            correlationId,
                            idempotencyKey,
                            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["workflowTriggerId"] = JsonValue.Create(trigger.Id.ToString("N")),
                                ["workflowDefinitionId"] = JsonValue.Create(trigger.DefinitionId.ToString("N")),
                                ["conditionDefinitionId"] = JsonValue.Create(trigger.Id.ToString("N")),
                                ["evaluatedAtUtc"] = JsonValue.Create(evaluation.EvaluatedUtc),
                                ["conditionOutcome"] = JsonValue.Create(evaluation.Outcome),
                                ["correlationId"] = JsonValue.Create(correlationId),
                                ["idempotencyKey"] = JsonValue.Create(idempotencyKey)
                            }),
                        maxRetryAttempts,
                        cancellationToken,
                        retryBackoffSeconds);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Condition trigger {TriggerId} failed during dispatch and remains eligible for retry.", trigger.Id);
                    outcome = TriggerExecutionAttemptStatus.RetryScheduled;
                }

                Increment(outcome, ref dispatched, ref blocked, ref duplicateSkipped, ref failed, ref retried, ref deadLettered);
            }
        }

        return new TriggerExecutionRunResult(scheduledEvaluated, conditionEvaluated, dispatched, blocked, duplicateSkipped, failed, retried, deadLettered);
    }

    private async Task<TriggerExecutionAttemptStatus> ProcessRetryAttemptAsync(
        TriggerExecutionAttempt retryAttempt,
        DateTime dueAtUtc,
        int maxRetryAttempts,
        int retryBackoffSeconds,
        CancellationToken cancellationToken)
    {
        var workItem = await BuildRetryWorkItemAsync(retryAttempt, cancellationToken);
        TriggerExecutionAttemptStatus outcome;
        try
        {
            outcome = await _executionService.EvaluateAndDispatchAsync(
                workItem,
                maxRetryAttempts,
                cancellationToken,
                retryBackoffSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Retry for trigger execution attempt {AttemptId} failed and remains eligible for retry or dead-letter handling.",
                retryAttempt.Id);
            outcome = TriggerExecutionAttemptStatus.RetryScheduled;
        }

        if (outcome != TriggerExecutionAttemptStatus.RetryScheduled &&
            string.Equals(retryAttempt.TriggerType, TriggerExecutionTypes.AgentScheduled, StringComparison.OrdinalIgnoreCase))
        {
            var trigger = await _dbContext.AgentScheduledTriggers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == retryAttempt.CompanyId && x.Id == retryAttempt.TriggerId, cancellationToken);

            if (trigger is not null)
            {
                var nextRunUtc = _nextRunCalculator.GetNextRunUtc(
                    trigger.CronExpression,
                    trigger.TimeZoneId,
                    retryAttempt.OccurrenceUtc);
                await AdvanceScheduledTriggerAsync(trigger.CompanyId, trigger.Id, dueAtUtc, nextRunUtc, cancellationToken);
            }
        }

        return outcome;
    }

    private async Task<TriggerExecutionWorkItem> BuildRetryWorkItemAsync(
        TriggerExecutionAttempt retryAttempt,
        CancellationToken cancellationToken)
    {
        Dictionary<string, JsonNode?> payload;
        Guid? agentId = retryAttempt.AgentId;

        if (string.Equals(retryAttempt.TriggerType, TriggerExecutionTypes.AgentScheduled, StringComparison.OrdinalIgnoreCase))
        {
            var trigger = await _dbContext.AgentScheduledTriggers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == retryAttempt.CompanyId && x.Id == retryAttempt.TriggerId, cancellationToken);

            agentId = trigger?.AgentId ?? agentId;
            payload = trigger is null
                ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, JsonNode?>(
                    trigger.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["triggerCode"] = JsonValue.Create(trigger.Code),
                    ["cronExpression"] = JsonValue.Create(trigger.CronExpression),
                    ["timeZoneId"] = JsonValue.Create(trigger.TimeZoneId)
                };
        }
        else
        {
            payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        payload["triggerId"] = JsonValue.Create(retryAttempt.TriggerId.ToString("N"));
        payload["triggerType"] = JsonValue.Create(retryAttempt.TriggerType);
        payload["companyId"] = JsonValue.Create(retryAttempt.CompanyId.ToString("N"));
        payload["agentId"] = agentId.HasValue ? JsonValue.Create(agentId.Value.ToString("N")) : null;
        payload["scheduledAtUtc"] = JsonValue.Create(retryAttempt.OccurrenceUtc);
        payload["evaluatedAtUtc"] = JsonValue.Create(retryAttempt.OccurrenceUtc);
        payload["correlationId"] = JsonValue.Create(retryAttempt.CorrelationId);
        payload["idempotencyKey"] = JsonValue.Create(retryAttempt.IdempotencyKey);
        payload["retryAttemptId"] = JsonValue.Create(retryAttempt.Id.ToString("N"));
        payload["retryAttemptCount"] = JsonValue.Create(retryAttempt.RetryAttemptCount);

        if (string.Equals(retryAttempt.TriggerType, TriggerExecutionTypes.WorkflowCondition, StringComparison.OrdinalIgnoreCase))
        {
            payload["workflowTriggerId"] = JsonValue.Create(retryAttempt.TriggerId.ToString("N"));
            payload["conditionDefinitionId"] = JsonValue.Create(retryAttempt.TriggerId.ToString("N"));
        }

        return new TriggerExecutionWorkItem(
            retryAttempt.CompanyId,
            retryAttempt.TriggerId,
            retryAttempt.TriggerType,
            agentId,
            retryAttempt.OccurrenceUtc,
            retryAttempt.CorrelationId,
            retryAttempt.IdempotencyKey,
            payload);
    }

    private async Task WriteEvaluationStartedAsync(
        Guid companyId,
        Guid triggerId,
        string triggerType,
        Guid? agentId,
        DateTime occurrenceUtc,
        string correlationId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await _triggerAuditEventWriter.WriteEvaluationStartedAsync(
            new TriggerAuditEventContext(companyId, triggerId, triggerType, agentId, occurrenceUtc, correlationId, idempotencyKey),
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task AdvanceScheduledTriggerAsync(Guid companyId, Guid triggerId, DateTime evaluatedUtc, DateTime? nextRunUtc, CancellationToken cancellationToken)
    {
        var trigger = await _dbContext.AgentScheduledTriggers
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == triggerId, cancellationToken);

        if (trigger is not null)
        {
            trigger.MarkEvaluated(evaluatedUtc, nextRunUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool TryBuildCondition(WorkflowTrigger trigger, out ConditionExpression condition, out string? diagnostic)
    {
        condition = null!;
        diagnostic = null;

        if (TryGetNode(trigger.CriteriaJson, ConditionExpressionCriteriaMapper.CriteriaConditionKey, out var nestedConditionNode))
        {
            if (nestedConditionNode is not JsonObject nestedConditionObject)
            {
                diagnostic = "Condition criteria must be an object.";
                return false;
            }

            return TryBuildCondition(nestedConditionObject, nestedConditionObject["target"] as JsonObject, out condition, out diagnostic);
        }

        var flatConditionObject = new JsonObject();
        foreach (var pair in trigger.CriteriaJson)
        {
            flatConditionObject[pair.Key] = pair.Value?.DeepClone();
        }

        return TryBuildCondition(flatConditionObject, flatConditionObject, out condition, out diagnostic);
    }

    private static bool TryBuildCondition(
        JsonObject conditionObject,
        JsonObject? targetObject,
        out ConditionExpression condition,
        out string? diagnostic)
    {
        condition = null!;
        diagnostic = null;

        if (targetObject is null)
        {
            diagnostic = "Condition criteria target is required.";
            return false;
        }

        var sourceType = ReadString(targetObject, "sourceType");
        var operatorValue = ReadString(conditionObject, "operator");
        if (!ConditionTriggerStorageValues.TryParseSourceType(sourceType, out var parsedSourceType) ||
            !ConditionTriggerStorageValues.TryParseOperator(operatorValue, out var parsedOperator))
        {
            diagnostic = "Condition criteria must include sourceType and operator.";
            return false;
        }

        ConditionValueType? valueType = null;
        var valueTypeValue = ReadString(conditionObject, "valueType");
        if (!string.IsNullOrWhiteSpace(valueTypeValue))
        {
            if (!ConditionTriggerStorageValues.TryParseValueType(valueTypeValue, out var parsedValueType))
            {
                diagnostic = "Condition criteria value type is invalid.";
                return false;
            }

            valueType = parsedValueType;
        }

        var repeatMode = RepeatFiringMode.FalseToTrueTransition;
        var repeatValue = ReadString(conditionObject, "repeatFiringMode");
        if (!string.IsNullOrWhiteSpace(repeatValue) &&
            !ConditionTriggerStorageValues.TryParseRepeatFiringMode(repeatValue, out repeatMode))
        {
            diagnostic = "Condition criteria repeat firing mode is invalid.";
            return false;
        }

        condition = new ConditionExpression(
            new ConditionTargetReference(
                parsedSourceType,
                ReadString(targetObject, "metricName"),
                ReadString(targetObject, "entityType"),
                ReadString(targetObject, "fieldPath")),
            parsedOperator,
            valueType,
            conditionObject.TryGetPropertyValue("comparisonValue", out var comparisonValue) ? comparisonValue?.DeepClone() : null,
            repeatMode);

        var validationErrors = ConditionExpressionValidator.Validate(condition, "criteriaJson.condition");
        if (validationErrors.Count == 0)
        {
            return true;
        }

        diagnostic = string.Join(
            "; ",
            validationErrors.SelectMany(pair => pair.Value.Select(message => $"{pair.Key}: {message}")));
        condition = null!;
        return false;
    }

    private static bool TryGetNode(IReadOnlyDictionary<string, JsonNode?> json, string key, out JsonNode? node)
    {
        foreach (var pair in json)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                node = pair.Value;
                return true;
            }
        }

        node = null;
        return false;
    }

    private static string? ReadString(JsonObject json, string key) =>
        json.TryGetPropertyValue(key, out var node) &&
        node is JsonValue value &&
        value.TryGetValue<string>(out var text) &&
        !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static void Increment(
        TriggerExecutionAttemptStatus outcome,
        ref int dispatched,
        ref int blocked,
        ref int duplicateSkipped,
        ref int failed,
        ref int retried,
        ref int deadLettered)
    {
        switch (outcome)
        {
            case TriggerExecutionAttemptStatus.Dispatched:
                dispatched++;
                break;
            case TriggerExecutionAttemptStatus.Blocked:
                blocked++;
                break;
            case TriggerExecutionAttemptStatus.DuplicateSkipped:
                duplicateSkipped++;
                break;
            case TriggerExecutionAttemptStatus.Failed:
                failed++;
                break;
            case TriggerExecutionAttemptStatus.Retried:
                retried++;
                break;
            case TriggerExecutionAttemptStatus.RetryScheduled:
                retried++;
                break;
            case TriggerExecutionAttemptStatus.DeadLettered:
                deadLettered++;
                break;
        }
    }
}

public sealed class TriggerEvaluationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<TriggerWorkerOptions> _options;
    private readonly ILogger<TriggerEvaluationBackgroundService> _logger;

    public TriggerEvaluationBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<TriggerWorkerOptions> options,
        ILogger<TriggerEvaluationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Trigger evaluation background service is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollingIntervalSeconds));
        using var timer = new PeriodicTimer(pollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<TriggerWorkerOptions>>().Value;
                await using var handle = await lockProvider.TryAcquireAsync(
                    string.IsNullOrWhiteSpace(options.LockKey) ? "trigger-evaluation-worker" : options.LockKey.Trim(),
                    TimeSpan.FromSeconds(Math.Max(5, options.LockTtlSeconds)),
                    stoppingToken);

                if (handle is not null)
                {
                    var worker = scope.ServiceProvider.GetRequiredService<ITriggerEvaluationWorker>();
                    var result = await worker.RunOnceAsync(
                        _timeProvider.GetUtcNow().UtcDateTime,
                        Math.Max(1, options.BatchSize),
                        Math.Max(1, options.MaxRetryAttempts),
                        stoppingToken,
                        Math.Max(0, options.RetryBackoffSeconds));

                    if (result.ScheduledTriggersEvaluated > 0 || result.ConditionChecksEvaluated > 0)
                    {
                        _logger.LogInformation(
                            "Trigger evaluation completed. Scheduled: {Scheduled}. Conditions: {Conditions}. Dispatched: {Dispatched}. Blocked: {Blocked}. Duplicates: {Duplicates}. Failed: {Failed}.",
                            result.ScheduledTriggersEvaluated,
                            result.ConditionChecksEvaluated,
                            result.Dispatched,
                            result.Blocked,
                            result.DuplicateSkipped,
                            result.Failed + result.DeadLettered);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trigger evaluation polling loop failed unexpectedly.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.Value.RetryBackoffSeconds)), stoppingToken);
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
