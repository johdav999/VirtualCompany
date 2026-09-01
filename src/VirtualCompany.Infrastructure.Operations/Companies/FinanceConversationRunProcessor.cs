using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceConversationRunProcessor : IFinanceConversationRunProcessor
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IFinanceDurableToolExecutionService _executor;
    private readonly IAgentEffectiveAuthorityResolver _authority;
    private readonly ICompanyToolRegistry _registry;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _clock;
    private readonly FinanceConversationRunOptions _options;
    private readonly ILogger<FinanceConversationRunProcessor> _logger;
    private readonly string _owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public FinanceConversationRunProcessor(VirtualCompanyDbContext db, IFinanceDurableToolExecutionService executor,
        IAgentEffectiveAuthorityResolver authority, ICompanyToolRegistry registry, IAuditEventWriter audit,
        TimeProvider clock, IOptions<FinanceConversationRunOptions> options,
        ILogger<FinanceConversationRunProcessor> logger)
    {
        _db = db; _executor = executor; _authority = authority; _registry = registry; _audit = audit;
        _clock = clock; _options = options.Value; _logger = logger;
    }

    public async Task<FinanceConversationRunProcessResult> RunOnceAsync(int batchSize,
        CancellationToken cancellationToken)
    {
        batchSize = Math.Clamp(batchSize, 1, 25);
        var now = _clock.GetUtcNow().UtcDateTime;
        var ids = await _db.FinanceConversationRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CancelledUtc == null && x.SupersededByRunId == null &&
                        !FinanceConversationRunStatuses.Terminal.Contains(x.Status) &&
                        x.Status != FinanceConversationRunStatuses.AwaitingConfirmation &&
                        x.Status != FinanceConversationRunStatuses.AwaitingClarification &&
                        (x.NextAttemptUtc == null || x.NextAttemptUtc <= now) &&
                        (x.LeaseExpiresUtc == null || x.LeaseExpiresUtc <= now))
            .OrderBy(x => x.NextAttemptUtc).ThenBy(x => x.CreatedUtc).Select(x => new { x.CompanyId, x.Id })
            .Take(batchSize).ToArrayAsync(cancellationToken);
        var completed = 0; var waiting = 0; var retried = 0; var failed = 0; var claimed = 0;
        foreach (var item in ids)
        {
            try
            {
                var before = await ClaimAsync(item.CompanyId, item.Id, cancellationToken);
                if (!before) continue;
                claimed++;
                await ProcessClaimedAsync(item.CompanyId, item.Id, cancellationToken);
                var state = await _db.FinanceConversationRuns.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.CompanyId == item.CompanyId && x.Id == item.Id).Select(x => x.Status)
                    .SingleAsync(cancellationToken);
                if (state == FinanceConversationRunStatuses.Completed) completed++;
                else if (state is FinanceConversationRunStatuses.Failed or FinanceConversationRunStatuses.Stale) failed++;
                else if (state == FinanceConversationRunStatuses.Ready) retried++;
                else waiting++;
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.ChangeTracker.Clear();
            }
        }
        return new FinanceConversationRunProcessResult(claimed, completed, waiting, retried, failed);
    }

    public async Task ProcessRunAsync(Guid companyId, Guid runId, CancellationToken cancellationToken)
    {
        if (await ClaimAsync(companyId, runId, cancellationToken))
            await ProcessClaimedAsync(companyId, runId, cancellationToken);
    }

    public async Task<int> RedactExpiredAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var runs = await _db.FinanceConversationRuns.IgnoreQueryFilters().Include(x => x.Steps)
            .Where(x => x.RedactedUtc == null && x.RetainUntilUtc <= now &&
                        FinanceConversationRunStatuses.Terminal.Contains(x.Status))
            .OrderBy(x => x.RetainUntilUtc).Take(Math.Clamp(batchSize, 1, 100)).ToArrayAsync(cancellationToken);
        foreach (var run in runs)
        {
            foreach (var step in run.Steps) step.Redact(now);
            run.MarkRedacted(now);
            await _audit.WriteAsync(new AuditEventWriteRequest(run.CompanyId, AuditActorTypes.System, null,
                AuditEventActions.FinanceConversationRunRedacted, AuditTargetTypes.AgentToolExecution,
                run.Id.ToString("N"), AuditEventOutcomes.Succeeded, run.SafeSummary,
                ["finance_conversation_run_retention"], CorrelationId: run.CorrelationId), cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
        return runs.Length;
    }

    private async Task<bool> ClaimAsync(Guid companyId, Guid runId, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        var run = await _db.FinanceConversationRuns.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == runId, cancellationToken);
        if (run is null || !run.TryClaim(_owner, _clock.GetUtcNow().UtcDateTime,
                TimeSpan.FromSeconds(Math.Clamp(_options.LeaseSeconds, 15, 300)))) return false;
        try { await _db.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateConcurrencyException) { _db.ChangeTracker.Clear(); return false; }
    }

    private async Task ProcessClaimedAsync(Guid companyId, Guid runId, CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();
        var run = await _db.FinanceConversationRuns.IgnoreQueryFilters().Include(x => x.Steps)
            .SingleAsync(x => x.CompanyId == companyId && x.Id == runId, cancellationToken);
        if (run.CancelledUtc.HasValue || run.SupersededByRunId.HasValue)
        {
            CancelRemaining(run, _clock.GetUtcNow().UtcDateTime);
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            var currentAuthority = await _authority.ResolveAsync(run.CompanyId, run.AgentId, cancellationToken);
            if (!string.Equals(currentAuthority.AuthorityHash, run.EffectiveAuthorityHash, StringComparison.Ordinal))
            {
                var now = _clock.GetUtcNow().UtcDateTime;
                foreach (var step in run.Steps.Where(x => !FinanceConversationRunStepStatuses.Terminal.Contains(x.Status)))
                    step.MarkStale("finance_run_authority_stale", "Agent authority changed before continuation.", now);
                run.SetState(FinanceConversationRunStatuses.Stale,
                    "Agent authority changed. The durable run was stopped before another step executed.", now,
                    "finance_run_authority_stale");
                await WriteTransitionAuditAsync(run, AuditEventOutcomes.Blocked, cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            var progressed = true;
            while (progressed && !run.CancelledUtc.HasValue)
            {
                progressed = false;
                foreach (var step in run.Steps.OrderBy(x => x.Sequence))
                {
                    if (FinanceConversationRunStepStatuses.Terminal.Contains(step.Status) ||
                        step.Status == FinanceConversationRunStepStatuses.AwaitingConfirmation) continue;
                    var dependencies = JsonSerializer.Deserialize<string[]>(step.DependenciesJson) ?? [];
                    var dependencyRows = run.Steps.Where(x => dependencies.Contains(x.StepKey, StringComparer.Ordinal)).ToArray();
                    if (dependencyRows.Any(x => x.Status is FinanceConversationRunStepStatuses.Failed or
                            FinanceConversationRunStepStatuses.Blocked or FinanceConversationRunStepStatuses.Cancelled or
                            FinanceConversationRunStepStatuses.Stale))
                    {
                        step.Block("finance_run_dependency_failed",
                            "A required predecessor did not complete successfully; this step was not executed.",
                            _clock.GetUtcNow().UtcDateTime);
                        progressed = true;
                        continue;
                    }
                    if (dependencyRows.Any(x => x.Status != FinanceConversationRunStepStatuses.Completed)) continue;
                    if (step.Status == FinanceConversationRunStepStatuses.Planned) step.SetReady(_clock.GetUtcNow().UtcDateTime);
                    if (step.Status == FinanceConversationRunStepStatuses.AwaitingApproval)
                    {
                        progressed |= await RefreshApprovalAsync(step, cancellationToken);
                        continue;
                    }
                    if (step.Status is FinanceConversationRunStepStatuses.Queued or FinanceConversationRunStepStatuses.Reconciling)
                    {
                        step.MarkReconciling(_clock.GetUtcNow().UtcDateTime);
                        continue;
                    }
                    if (step.Status is FinanceConversationRunStepStatuses.Ready or FinanceConversationRunStepStatuses.Executing)
                    {
                        if (run.CancelledUtc.HasValue) break;
                        progressed |= await ExecuteStepAsync(run, step, cancellationToken);
                    }
                }
            }
            FinalizeRun(run, _clock.GetUtcNow().UtcDateTime);
            run.ReleaseLease(_owner, _clock.GetUtcNow().UtcDateTime,
                NeedsPolling(run) ? _clock.GetUtcNow().UtcDateTime.AddSeconds(15) : null);
            await WriteTransitionAuditAsync(run,
                FinanceConversationRunStatuses.Terminal.Contains(run.Status)
                    ? run.Status == FinanceConversationRunStatuses.Completed
                        ? AuditEventOutcomes.Succeeded
                        : AuditEventOutcomes.Failed
                    : AuditEventOutcomes.Pending,
                cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Durable Finance run continuation failed safely for run {RunId}.", run.Id);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (run.AttemptCount >= run.MaxAttempts)
                run.SetState(FinanceConversationRunStatuses.Failed,
                    "The durable Finance run exhausted its bounded continuation attempts.", now,
                    "finance_run_retry_limit_reached");
            else
                run.ScheduleRetry(_owner, now, now.AddSeconds(Math.Min(300, 1 << Math.Min(run.AttemptCount + 1, 8))));
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<bool> ExecuteStepAsync(FinanceConversationRun run, FinanceConversationRunStep step,
        CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        if (step.ActionType == ToolActionType.Execute.ToStorageValue())
        {
            if (!step.ConfirmedUtc.HasValue || step.ConfirmationExpiresUtc <= now)
            {
                step.MarkStale("finance_run_confirmation_expired",
                    "The stored confirmation expired before execution. Supersede the run with a refreshed preview.", now);
                return true;
            }
            if (!await ConfirmationStillCurrentAsync(run, step, cancellationToken)) return true;
        }

        var stableCorrelation = "fcr:" + Hash(step.BusinessIdempotencyKey);
        var prior = await _db.ToolExecutionAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.AgentId == run.AgentId && x.CorrelationId == stableCorrelation)
            .OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(cancellationToken);
        if (prior is not null) return RecoverPriorAttempt(step, prior, now);

        var stopped = await _db.FinanceConversationRuns.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == run.CompanyId && x.Id == run.Id &&
                           (x.CancelledUtc != null || x.SupersededByRunId != null), cancellationToken);
        if (stopped)
        {
            step.Cancel(now);
            return true;
        }

        if (step.AttemptCount >= step.MaxAttempts)
        {
            step.Fail("finance_run_step_retry_limit_reached",
                "The durable step exhausted its bounded execution attempts and was not invoked again.", now);
            return true;
        }

        if (!step.TryClaim(_owner, now, TimeSpan.FromSeconds(Math.Clamp(_options.LeaseSeconds, 15, 300)))) return false;
        var attempt = new FinanceConversationRunAttempt(Guid.NewGuid(), run.CompanyId, step.Id,
            step.AttemptCount, _owner, step.LeaseExpiresUtc!.Value, now);
        _db.FinanceConversationRunAttempts.Add(attempt);
        await _db.SaveChangesAsync(cancellationToken);

        var payload = FinanceConversationRunSerialization.ParseArguments(step.NormalizedArgumentsJson);
        var response = await _executor.ExecuteDurableAsync(run.CompanyId, run.AgentId, run.InitiatingUserId,
            new ExecuteAgentToolCommand(step.ToolName, step.ActionType, step.Scope, payload, null, null, null,
                TaskId: run.TaskId, WorkflowInstanceId: run.WorkflowInstanceId, CorrelationId: stableCorrelation,
                DelegationAuthorityId: run.DelegationAuthorityId,
                ExpectedAuthorityVersion: run.EffectiveAuthorityVersion, ExpectedAuthorityHash: run.EffectiveAuthorityHash),
            cancellationToken);
        var resultJson = SafeResult(response.ExecutionResult);
        var policyJson = SafePolicy(response.PolicyDecision);
        if (response.ApprovalRequestId.HasValue || response.Status == ToolExecutionStatus.AwaitingApproval.ToStorageValue())
        {
            step.AwaitApproval(response.ExecutionId, response.ApprovalRequestId!.Value, policyJson, now);
            attempt.Complete("awaiting_approval", now, response.ExecutionId, summary: "The step is awaiting P0 approval.");
        }
        else if (response.Status == ToolExecutionStatus.Executed.ToStorageValue() && !HasPendingSemantics(response.ExecutionResult))
        {
            step.Complete(response.ExecutionId, resultJson, policyJson, now);
            attempt.Complete("completed", now, response.ExecutionId, summary: "The step completed with a structured result.");
        }
        else if (response.Status == ToolExecutionStatus.Executed.ToStorageValue() ||
                 response.Status == ToolExecutionStatus.ReconciliationRequired.ToStorageValue())
        {
            step.MarkQueued(response.ExecutionId, resultJson, policyJson, now);
            attempt.Complete("reconciling", now, response.ExecutionId, summary: "Completion is pending authoritative reconciliation.");
        }
        else
        {
            step.Fail(response.Denial?.Code ?? "finance_run_step_failed",
                FinanceConversationRunSerialization.SafeText(response.Message, 2000), now);
            attempt.Complete("failed", now, response.ExecutionId, step.FailureCode, step.SafeFailureSummary);
        }
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> ConfirmationStillCurrentAsync(FinanceConversationRun run,
        FinanceConversationRunStep step, CancellationToken cancellationToken)
    {
        if (!string.Equals(step.ConfirmationAuthorityHash, run.EffectiveAuthorityHash, StringComparison.Ordinal) ||
            !_registry.TryGetTool(step.ToolName, out var registration) || registration.FinanceRiskClassification is null)
        {
            step.MarkStale("finance_run_confirmation_stale", "The confirmation binding is no longer current.", _clock.GetUtcNow().UtcDateTime);
            return false;
        }
        var payload = FinanceConversationRunSerialization.ParseArguments(step.NormalizedArgumentsJson);
        var attempt = new ToolExecutionAttempt(Guid.NewGuid(), run.CompanyId, run.AgentId, step.ToolName,
            ToolActionType.Execute, step.Scope, payload, run.TaskId, run.WorkflowInstanceId, run.CorrelationId,
            toolVersion: step.ToolVersion);
        var snapshot = await FinanceApprovalContinuationBinding.BuildTargetSnapshotAsync(_db, attempt, cancellationToken);
        var integration = await FinanceApprovalContinuationBinding.BuildIntegrationStateHashAsync(_db, run.CompanyId,
            registration.FinanceRiskClassification, cancellationToken);
        var current = Hash(FinanceApprovalContinuationBinding.ComputeTargetSnapshotHash(snapshot) + "|" + integration);
        if (string.Equals(current, step.ConfirmationTargetSnapshotHash, StringComparison.Ordinal)) return true;
        step.MarkStale("finance_run_target_stale",
            "The target or integration state changed after confirmation. No mutation was executed.",
            _clock.GetUtcNow().UtcDateTime);
        return false;
    }

    private async Task<bool> RefreshApprovalAsync(FinanceConversationRunStep step, CancellationToken cancellationToken)
    {
        if (!step.ToolExecutionAttemptId.HasValue) return false;
        var attempt = await _db.ToolExecutionAttempts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == step.CompanyId && x.Id == step.ToolExecutionAttemptId.Value,
                cancellationToken);
        if (attempt is null) { step.Fail("finance_run_approval_attempt_missing", "The approval execution link is unavailable.", _clock.GetUtcNow().UtcDateTime); return true; }
        return RecoverPriorAttempt(step, attempt, _clock.GetUtcNow().UtcDateTime);
    }

    private static bool RecoverPriorAttempt(FinanceConversationRunStep step, ToolExecutionAttempt attempt, DateTime now)
    {
        var result = JsonSerializer.Serialize(FinanceConversationRunSerialization.SafeArguments(attempt.ResultPayload));
        var policy = JsonSerializer.Serialize(FinanceConversationRunSerialization.SafeArguments(attempt.PolicyDecision));
        if (attempt.Status == ToolExecutionStatus.AwaitingApproval)
        {
            if (attempt.ApprovalRequestId.HasValue) step.AwaitApproval(attempt.Id, attempt.ApprovalRequestId.Value, policy, now);
            return false;
        }
        if (attempt.Status == ToolExecutionStatus.Executed && !HasPendingSemantics(attempt.ResultPayload))
        { step.Complete(attempt.Id, result, policy, now); return true; }
        if (attempt.Status == ToolExecutionStatus.ReconciliationRequired ||
            attempt.Status == ToolExecutionStatus.Executed && HasPendingSemantics(attempt.ResultPayload))
        { step.MarkReconciling(now); return false; }
        if (attempt.Status is ToolExecutionStatus.Failed or ToolExecutionStatus.Denied or ToolExecutionStatus.Rejected)
        { step.Fail("finance_run_prior_attempt_failed", "The persisted tool attempt did not complete successfully.", now); return true; }
        return false;
    }

    private static void FinalizeRun(FinanceConversationRun run, DateTime now)
    {
        if (run.CancelledUtc.HasValue) { CancelRemaining(run, now); return; }
        var steps = run.Steps.ToArray();
        if (steps.Length == 0)
        {
            if (run.Status != FinanceConversationRunStatuses.AwaitingClarification)
                run.SetState(FinanceConversationRunStatuses.Failed, "The plan produced no executable steps.", now, "finance_run_no_steps");
            return;
        }
        if (steps.All(x => x.Status == FinanceConversationRunStepStatuses.Completed))
            run.SetState(FinanceConversationRunStatuses.Completed, "All durable Finance run steps completed successfully.", now, "finance_run_completed");
        else if (steps.All(x => FinanceConversationRunStepStatuses.Terminal.Contains(x.Status)))
            run.SetState(steps.Any(x => x.Status == FinanceConversationRunStepStatuses.Completed)
                    ? FinanceConversationRunStatuses.PartiallyCompleted
                    : steps.Any(x => x.Status == FinanceConversationRunStepStatuses.Stale)
                        ? FinanceConversationRunStatuses.Stale
                        : FinanceConversationRunStatuses.Failed,
                "The Finance run reached a terminal state with one or more incomplete branches.", now,
                "finance_run_partially_completed");
        else if (steps.Any(x => x.Status == FinanceConversationRunStepStatuses.AwaitingConfirmation))
            run.SetState(FinanceConversationRunStatuses.AwaitingConfirmation, "One or more exact steps require confirmation.", now);
        else if (steps.Any(x => x.Status == FinanceConversationRunStepStatuses.AwaitingApproval))
            run.SetState(FinanceConversationRunStatuses.AwaitingApproval, "The run is waiting for the existing P0 approval workflow.", now);
        else if (steps.Any(x => x.Status == FinanceConversationRunStepStatuses.Queued))
            run.SetState(FinanceConversationRunStatuses.Queued, "External or stored work is queued; completion is not yet claimed.", now);
        else if (steps.Any(x => x.Status == FinanceConversationRunStepStatuses.Reconciling))
            run.SetState(FinanceConversationRunStatuses.Reconciling, "The run is reconciling an ambiguous or pending outcome.", now);
        else run.SetState(FinanceConversationRunStatuses.Ready, "The next dependency-safe step is ready.", now);
    }

    private static bool NeedsPolling(FinanceConversationRun run) => run.Status is
        FinanceConversationRunStatuses.AwaitingApproval or FinanceConversationRunStatuses.Queued or
        FinanceConversationRunStatuses.Reconciling or FinanceConversationRunStatuses.Ready;
    private static void CancelRemaining(FinanceConversationRun run, DateTime now)
    { foreach (var step in run.Steps.Where(x => !FinanceConversationRunStepStatuses.Terminal.Contains(x.Status))) step.Cancel(now); }
    private static string SafeResult(IReadOnlyDictionary<string, JsonNode?>? result) =>
        JsonSerializer.Serialize(FinanceConversationRunSerialization.SafeArguments(result ?? new Dictionary<string, JsonNode?>()));
    private static string SafePolicy(ToolExecutionDecisionDto decision) => JsonSerializer.Serialize(new
    { decision.Outcome, decision.ReasonCodes, decision.ApprovalRequired, Metadata = FinanceConversationRunSerialization.SafeArguments(decision.Metadata) });
    private static bool HasPendingSemantics(IReadOnlyDictionary<string, JsonNode?>? values) => values?.Values.Any(HasPendingNode) == true;
    private static bool HasPendingNode(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => new[] { "pending", "queued", "processing", "reconcil", "in_progress" }.Any(x => text.Contains(x, StringComparison.OrdinalIgnoreCase)),
        JsonObject obj => obj.Any(x => HasPendingNode(x.Value)), JsonArray array => array.Any(HasPendingNode), _ => false
    };
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private Task WriteTransitionAuditAsync(FinanceConversationRun run, string outcome,
        CancellationToken cancellationToken) => _audit.WriteAsync(new AuditEventWriteRequest(
        run.CompanyId, AuditActorTypes.System, null, AuditEventActions.FinanceConversationRunTransitioned,
        AuditTargetTypes.AgentToolExecution, run.Id.ToString("N"), outcome, run.SafeSummary,
        ["finance_conversation_run", "tool_execution_attempt"],
        new Dictionary<string, string?>
        {
            ["status"] = run.Status,
            ["finalOutcomeCode"] = run.FinalOutcomeCode,
            ["agentId"] = run.AgentId.ToString("N")
        }, run.CorrelationId), cancellationToken);
}

public sealed class FinanceConversationRunBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<FinanceConversationRunOptions> _options;
    private readonly ILogger<FinanceConversationRunBackgroundService> _logger;
    public FinanceConversationRunBackgroundService(IServiceScopeFactory scopes,
        IOptions<FinanceConversationRunOptions> options, ILogger<FinanceConversationRunBackgroundService> logger)
    { _scopes = scopes; _options = options; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.Value;
            if (options.WorkerEnabled)
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<IFinanceConversationRunProcessor>();
                    await processor.RunOnceAsync(options.BatchSize, stoppingToken);
                    await processor.RedactExpiredAsync(options.BatchSize, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { _logger.LogError(ex, "Durable Finance conversation run worker failed safely."); }
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.PollIntervalSeconds, 2, 300)), stoppingToken);
        }
    }
}
