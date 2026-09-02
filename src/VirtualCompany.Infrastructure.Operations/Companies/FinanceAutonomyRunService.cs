using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceAutonomyRunService : IFinanceAutonomyRunService
{
    private const int MaximumSteps = 100;
    private const int MaximumSources = 200;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceAutonomyPolicyEvaluator _policyEvaluator;
    private readonly ICompanyMembershipContextResolver _membershipResolver;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _timeProvider;
    private readonly IFinanceAutonomyBudgetService? _budgets;

    public FinanceAutonomyRunService(
        VirtualCompanyDbContext dbContext,
        IFinanceAutonomyPolicyEvaluator policyEvaluator,
        ICompanyMembershipContextResolver membershipResolver,
        IAuditEventWriter audit,
        TimeProvider timeProvider,
        IFinanceAutonomyBudgetService? budgets = null)
    {
        _dbContext = dbContext;
        _policyEvaluator = policyEvaluator;
        _membershipResolver = membershipResolver;
        _audit = audit;
        _timeProvider = timeProvider;
        _budgets = budgets;
    }

    public Task<FinanceAutonomyRunDto> CreateOrCoalesceAsync(
        Guid companyId, CreateOrCoalesceFinanceAutonomyRunCommand command, CancellationToken cancellationToken) =>
        CreateCoreAsync(companyId, command, null, null, null, null, null, 1, cancellationToken);

    public async Task<FinanceAutonomyRunListResult> ListAsync(
        Guid companyId, FinanceAutonomyRunFilter filter, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        ArgumentNullException.ThrowIfNull(filter);
        var skip = Math.Max(0, filter.Skip);
        var take = Math.Clamp(filter.Take, 1, 200);
        var query = _dbContext.FinanceAutonomyRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId);
        if (filter.AgentId.HasValue) query = query.Where(x => x.AgentId == filter.AgentId.Value);
        if (filter.GrantId.HasValue) query = query.Where(x => x.GrantId == filter.GrantId.Value);
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = ParseRunStatus(filter.Status);
            query = query.Where(x => x.Status == status);
        }
        if (filter.FromUtc.HasValue) query = query.Where(x => x.CreatedUtc >= Utc(filter.FromUtc.Value));
        if (filter.ToUtc.HasValue) query = query.Where(x => x.CreatedUtc < Utc(filter.ToUtc.Value));
        var total = await query.CountAsync(cancellationToken);
        var runs = await query.Include(x => x.Steps).OrderByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.CreatedUtc).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return new FinanceAutonomyRunListResult(runs.Select(MapListItem).ToArray(), total, skip, take);
    }

    public async Task<FinanceAutonomyRunDto> GetAsync(Guid companyId, Guid runId, CancellationToken cancellationToken) =>
        Map(await LoadRunAsync(companyId, runId, false, cancellationToken));

    public async Task<FinanceAutonomyRunDto> TransitionAsync(
        Guid companyId, Guid runId, TransitionFinanceAutonomyRunCommand command, CancellationToken cancellationToken)
    {
        var actor = await RequireManagerAsync(companyId, cancellationToken);
        var run = await LoadRunAsync(companyId, runId, true, cancellationToken);
        EnsureVersion(run, command.ExpectedVersion);
        var next = ParseRunStatus(command.Status);
        var previous = run.Status;
        run.Transition(next, command.ReasonCode, command.SafeSummary, UtcNow());
        AddHistory(run, previous, command.ReasonCode, command.SafeSummary, AuditActorTypes.User, actor.UserId, UtcNow());
        await WriteAuditAsync(run, actor.UserId, AuditEventActions.FinanceAutonomyRunTransitioned,
            AuditEventOutcomes.Succeeded, command.SafeSummary ?? command.ReasonCode, cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(run);
    }

    public async Task<FinanceAutonomyRunDto> BindApprovalAsync(
        Guid companyId, Guid runId, Guid stepId, BindFinanceAutonomyStepApprovalCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await RequireManagerAsync(companyId, cancellationToken);
        var run = await LoadRunAsync(companyId, runId, true, cancellationToken);
        var step = RequireStep(run, stepId);
        var approval = await _dbContext.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == command.ApprovalRequestId, cancellationToken);
        if (approval?.ToolExecutionAttemptId is not Guid toolAttemptId || approval.Status != ApprovalRequestStatus.Pending ||
            !approval.ThresholdContext.TryGetValue("financeAutonomy", out var contextNode) || contextNode is null)
            throw Validation(nameof(command.ApprovalRequestId), "Only a pending durable exact-action autonomy approval can be linked.");
        FinanceAutonomyApprovalContextDto? context;
        try
        {
            context = JsonSerializer.Deserialize<FinanceAutonomyApprovalContextDto>(contextNode.ToJsonString(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            context = null;
        }
        if (context is null || context.RunId != run.Id || context.StepId != step.Id ||
            context.GrantId != run.GrantId || context.GrantVersionId != run.GrantVersionId ||
            !string.Equals(context.PlanHash, run.PlanHash, StringComparison.Ordinal) ||
            !string.Equals(context.EvidenceHash, run.EvidenceHash, StringComparison.Ordinal) ||
            !string.Equals(context.BudgetHash, run.BudgetHash, StringComparison.Ordinal))
            throw Validation(nameof(command.ApprovalRequestId), "The approval is not bound to this exact immutable run and step.");
        step.BindApproval(command.ApprovalRequestId, toolAttemptId, UtcNow());
        var previous = run.Status;
        if (run.Status != FinanceAutonomyRunStatus.AwaitingApproval)
            run.Transition(FinanceAutonomyRunStatus.AwaitingApproval, FinanceAutonomyRunReasonCodes.AwaitingApproval,
                "A planned step is waiting for its linked approval.", UtcNow());
        AddHistory(run, previous, FinanceAutonomyRunReasonCodes.AwaitingApproval,
            "A planned step is waiting for its linked approval.", AuditActorTypes.User, actor.UserId, UtcNow());
        await WriteAuditAsync(run, actor.UserId, AuditEventActions.FinanceAutonomyStepApprovalBound,
            AuditEventOutcomes.Pending, "A Finance autonomy step was linked to an approval request.", cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(run);
    }

    public async Task<FinanceAutonomyRunDto> CancelAsync(
        Guid companyId, Guid runId, CancelFinanceAutonomyRunCommand command, CancellationToken cancellationToken)
    {
        var actor = await RequireManagerAsync(companyId, cancellationToken);
        var run = await LoadRunAsync(companyId, runId, true, cancellationToken);
        EnsureVersion(run, command.ExpectedVersion);
        if (FinanceAutonomyRun.IsTerminal(run.Status)) return Map(run);
        var previous = run.Status;
        run.Transition(FinanceAutonomyRunStatus.Cancelled, FinanceAutonomyRunReasonCodes.Cancelled,
            CancellationSummary(command.Reason, run.HasCompletedEffects), UtcNow());
        await InvalidatePendingApprovalsAsync(run, ApprovalRequestStatus.Cancelled, actor.UserId,
            command.Reason, cancellationToken);
        if (_budgets is not null) await _budgets.ReleaseForRunAsync(companyId, run.Id, cancellationToken);
        foreach (var step in run.Steps) step.CancelOrSupersede(false, UtcNow());
        AddHistory(run, previous, FinanceAutonomyRunReasonCodes.Cancelled, run.SafeSummary,
            AuditActorTypes.User, actor.UserId, UtcNow());
        await WriteAuditAsync(run, actor.UserId, AuditEventActions.FinanceAutonomyRunCancelled,
            AuditEventOutcomes.Succeeded, run.SafeSummary!, cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(run);
    }

    public async Task<FinanceAutonomyRunDto> SupersedeAsync(
        Guid companyId, Guid runId, SupersedeFinanceAutonomyRunCommand command, CancellationToken cancellationToken)
    {
        var actor = await RequireManagerAsync(companyId, cancellationToken);
        var run = await LoadRunAsync(companyId, runId, true, cancellationToken);
        EnsureVersion(run, command.ExpectedVersion);
        if (FinanceAutonomyRun.IsTerminal(run.Status)) return Map(run);
        var previous = run.Status;
        run.Transition(FinanceAutonomyRunStatus.Superseded, FinanceAutonomyRunReasonCodes.Superseded,
            CancellationSummary(command.Reason, run.HasCompletedEffects), UtcNow());
        await InvalidatePendingApprovalsAsync(run, ApprovalRequestStatus.Superseded, actor.UserId,
            command.Reason, cancellationToken);
        if (_budgets is not null) await _budgets.ReleaseForRunAsync(companyId, run.Id, cancellationToken);
        foreach (var step in run.Steps) step.CancelOrSupersede(true, UtcNow());
        AddHistory(run, previous, FinanceAutonomyRunReasonCodes.Superseded, run.SafeSummary,
            AuditActorTypes.User, actor.UserId, UtcNow());
        await WriteAuditAsync(run, actor.UserId, AuditEventActions.FinanceAutonomyRunSuperseded,
            AuditEventOutcomes.Succeeded, run.SafeSummary!, cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(run);
    }

    public async Task<FinanceAutonomyRunDto> RedactAsync(
        Guid companyId, Guid runId, RedactFinanceAutonomyRunCommand command, CancellationToken cancellationToken)
    {
        var actor = await RequireManagerAsync(companyId, cancellationToken);
        var run = await LoadRunAsync(companyId, runId, true, cancellationToken);
        EnsureVersion(run, command.ExpectedVersion);
        if (!FinanceAutonomyRun.IsTerminal(run.Status))
            throw Validation(nameof(run.Status), "Sensitive content can be redacted only after the run reaches a terminal state.");
        run.RedactSensitiveContent(actor.UserId, UtcNow());
        foreach (var step in run.Steps) step.RedactSensitiveContent(UtcNow());
        foreach (var source in run.Sources) source.RedactLabel();
        AddHistory(run, run.Status, FinanceAutonomyRunReasonCodes.Redacted, command.Reason,
            AuditActorTypes.User, actor.UserId, UtcNow());
        await WriteAuditAsync(run, actor.UserId, AuditEventActions.FinanceAutonomyRunRedacted,
            AuditEventOutcomes.Succeeded, command.Reason, cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(run);
    }

    public async Task<FinanceAutonomyRunDto> ReplayAsync(
        Guid companyId, Guid runId, ReplayFinanceAutonomyRunCommand command, CancellationToken cancellationToken)
    {
        var actor = await RequireManagerAsync(companyId, cancellationToken);
        var source = await LoadRunAsync(companyId, runId, false, cancellationToken);
        if (!FinanceAutonomyRun.IsTerminal(source.Status) && source.Status != FinanceAutonomyRunStatus.Blocked)
            throw Validation(nameof(source.Status), "Only a terminal or blocked run can be replayed.");
        if (source.SensitiveContentRedactedUtc.HasValue)
            throw Validation(nameof(source.SensitiveContentRedactedUtc), "A redacted run cannot be replayed because its immutable inputs are no longer retained.");
        var checkpoint = RequireStep(source, command.CheckpointStepId);
        if (!checkpoint.ReplayPermitted)
            throw Validation(nameof(command.CheckpointStepId), "The selected checkpoint was not explicitly marked as replayable in the original plan.");

        var evidence = JsonSerializer.Deserialize<Dictionary<string, string?>>(source.EvidenceSnapshotJson) ?? [];
        var budget = JsonSerializer.Deserialize<Dictionary<string, decimal>>(source.BudgetSnapshotJson) ?? [];
        var replaySteps = source.Steps.Where(x => x.Sequence >= checkpoint.Sequence).OrderBy(x => x.Sequence)
            .Select(x => new FinanceAutonomyRunPlanStepDefinition(
                x.StepKey, x.ActionClass, x.ToolName,
                x.DependencyStepKeys.Where(dependency => source.Steps.Any(s => s.Sequence >= checkpoint.Sequence && s.StepKey == dependency)).ToArray(),
                x.RequestedEffectHash, x.RequestedEffectSummary, x.MaximumAttempts, x.ReplayPermitted, x.WorkTaskId,
                BusinessIdempotencyKey: x.BusinessIdempotencyKey)).ToArray();
        var replayKey = Hash(command.IdempotencyKey)[..16];
        var create = new CreateOrCoalesceFinanceAutonomyRunCommand(
            source.AgentId, source.CapabilityId, source.Trigger, $"{source.TriggerKey}:replay:{replayKey}",
            UtcNow(), UtcNow().AddMinutes(Math.Max(1, (source.WindowEndUtc - source.WindowStartUtc).TotalMinutes)),
            source.AuthoritativeEventId,
            string.IsNullOrWhiteSpace(source.AuthoritativeEventVersion) ? $"replay:{replayKey}" : $"{source.AuthoritativeEventVersion}:replay:{replayKey}",
            command.IdempotencyKey, command.CorrelationId, source.EvidenceObservedUtc, evidence, source.PlanVersion,
            replaySteps, budget, source.Sources.Select(x => new FinanceAutonomyRunSourceDefinition(
                x.SourceType, x.EntityType, x.EntityId, x.SourceVersion, x.ContentHash, x.SafeLabel)).ToArray(),
            OriginatingGoalId: source.OriginatingGoalId, OriginatingTaskId: source.OriginatingTaskId,
            WorkflowInstanceId: source.WorkflowInstanceId, OrchestrationRunId: source.OrchestrationRunId);
        return await CreateCoreAsync(companyId, create, source.Id, checkpoint.Id,
            source.Steps.ToDictionary(x => x.StepKey, x => x.Id, StringComparer.Ordinal), actor.UserId,
            null, 1, cancellationToken);
    }

    public async Task<FinanceAutonomyRunDto> NarrowAsync(
        Guid companyId, Guid runId, NarrowFinanceAutonomyRunCommand command, CancellationToken cancellationToken)
    {
        var actor = await RequireManagerAsync(companyId, cancellationToken);
        var source = await LoadRunAsync(companyId, runId, true, cancellationToken);
        EnsureVersion(source, command.ExpectedVersion);
        if (source.Status is not (FinanceAutonomyRunStatus.AwaitingApproval or FinanceAutonomyRunStatus.Blocked or FinanceAutonomyRunStatus.Paused))
            throw Validation(nameof(source.Status), "Only pending, blocked, or paused Finance autonomy work can be narrowed.");
        if (source.SensitiveContentRedactedUtc.HasValue)
            throw Validation(nameof(source.SensitiveContentRedactedUtc), "A redacted plan cannot be revised.");
        var retained = command.RetainedStepKeys.Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => Normalize(key)).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (retained.Count == 0)
            throw Validation(nameof(command.RetainedStepKeys), "At least one existing pending step must be retained.");

        var definitions = JsonSerializer.Deserialize<List<FinanceAutonomyRunPlanStepDefinition>>(source.PlanJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        var pendingDefinitions = definitions.Where(definition => source.Steps.Any(step =>
            step.StepKey == Normalize(definition.StepKey) && step.Status != FinanceAutonomyStepStatus.Completed)).ToArray();
        if (retained.Any(key => pendingDefinitions.All(definition => Normalize(definition.StepKey) != key)))
            throw Validation(nameof(command.RetainedStepKeys), "A retained key is not an existing pending step.");
        if (retained.Count >= pendingDefinitions.Length)
            throw Validation(nameof(command.RetainedStepKeys), "A narrower revision must remove at least one pending step. Scope expansion requires a new plan and grant review.");
        var completedKeys = source.Steps.Where(step => step.Status == FinanceAutonomyStepStatus.Completed)
            .Select(step => step.StepKey).ToHashSet(StringComparer.Ordinal);
        foreach (var definition in pendingDefinitions.Where(item => retained.Contains(Normalize(item.StepKey))))
        {
            var removedDependency = definition.DependencyStepKeys.Select(Normalize)
                .FirstOrDefault(key => !retained.Contains(key) && !completedKeys.Contains(key));
            if (removedDependency is not null)
                throw Validation(nameof(command.RetainedStepKeys),
                    $"Step '{definition.StepKey}' still depends on removed pending step '{removedDependency}'. Remove the dependent step too.");
        }

        var revisedSteps = pendingDefinitions.Where(item => retained.Contains(Normalize(item.StepKey)))
            .Select(item => item with
            {
                DependencyStepKeys = item.DependencyStepKeys.Where(key => retained.Contains(Normalize(key))).ToArray()
            }).ToArray();
        var evidence = JsonSerializer.Deserialize<Dictionary<string, string?>>(source.EvidenceSnapshotJson) ?? [];
        var budget = JsonSerializer.Deserialize<Dictionary<string, decimal>>(source.BudgetSnapshotJson) ?? [];
        var revisionNumber = source.RevisionNumber + 1;
        var revisionKey = Hash(command.IdempotencyKey)[..16];
        var create = new CreateOrCoalesceFinanceAutonomyRunCommand(
            source.AgentId, source.CapabilityId, source.Trigger,
            $"{source.TriggerKey}:revision:{revisionKey}", source.WindowStartUtc, source.WindowEndUtc,
            source.AuthoritativeEventId, source.AuthoritativeEventVersion,
            command.IdempotencyKey, command.CorrelationId, source.EvidenceObservedUtc, evidence,
            $"{source.PlanVersion}:revision:{revisionNumber}", revisedSteps, budget,
            source.Sources.Select(item => new FinanceAutonomyRunSourceDefinition(
                item.SourceType, item.EntityType, item.EntityId, item.SourceVersion, item.ContentHash, item.SafeLabel)).ToArray(),
            OriginatingGoalId: source.OriginatingGoalId, OriginatingTaskId: source.OriginatingTaskId,
            WorkflowInstanceId: source.WorkflowInstanceId, OrchestrationRunId: source.OrchestrationRunId);
        var revision = await CreateCoreAsync(companyId, create, null, null, null, actor.UserId,
            source.Id, revisionNumber, cancellationToken);

        if (!FinanceAutonomyRun.IsTerminal(source.Status))
        {
            var previous = source.Status;
            var summary = CancellationSummary(command.Reason, source.HasCompletedEffects);
            source.Transition(FinanceAutonomyRunStatus.Superseded, FinanceAutonomyRunReasonCodes.Superseded, summary, UtcNow());
            await InvalidatePendingApprovalsAsync(source, ApprovalRequestStatus.Superseded, actor.UserId,
                "The pending approval was superseded by a narrower validated run revision.", cancellationToken);
            foreach (var step in source.Steps) step.CancelOrSupersede(true, UtcNow());
            if (_budgets is not null) await _budgets.ReleaseForRunAsync(companyId, source.Id, cancellationToken);
            AddHistory(source, previous, FinanceAutonomyRunReasonCodes.Superseded, summary,
                AuditActorTypes.User, actor.UserId, UtcNow());
            await WriteAuditAsync(source, actor.UserId, AuditEventActions.FinanceAutonomyRunSuperseded,
                AuditEventOutcomes.Succeeded, summary, cancellationToken);
            await SaveAsync(cancellationToken);
        }
        return revision;
    }

    private async Task InvalidatePendingApprovalsAsync(
        FinanceAutonomyRun run,
        ApprovalRequestStatus outcome,
        Guid actorUserId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var approvalIds = run.Steps.Where(step => step.ApprovalRequestId.HasValue)
            .Select(step => step.ApprovalRequestId!.Value).Distinct().ToArray();
        if (approvalIds.Length == 0) return;
        var approvals = await _dbContext.ApprovalRequests.IgnoreQueryFilters().Include(item => item.Steps)
            .Where(item => item.CompanyId == run.CompanyId && approvalIds.Contains(item.Id) &&
                           item.Status == ApprovalRequestStatus.Pending)
            .ToListAsync(cancellationToken);
        foreach (var approval in approvals)
        {
            var summary = string.IsNullOrWhiteSpace(reason)
                ? outcome == ApprovalRequestStatus.Cancelled
                    ? "The authorized human cancelled the pending autonomous work."
                    : "A newer validated run revision superseded this exact-action approval."
                : reason.Trim();
            if (outcome == ApprovalRequestStatus.Cancelled) approval.MarkCancelled(summary);
            else approval.MarkSuperseded(summary);

            var attempt = await _dbContext.ToolExecutionAttempts.IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.CompanyId == run.CompanyId &&
                                              item.Id == approval.TargetEntityId &&
                                              item.Status == ToolExecutionStatus.AwaitingApproval,
                    cancellationToken);
            if (attempt is not null)
            {
                var reasonCode = approval.ExecutionBlockReasonCode!;
                attempt.MarkDenied(
                    new Dictionary<string, JsonNode?>
                    {
                        ["outcome"] = "deny",
                        ["reasonCode"] = reasonCode,
                        ["approvalRequestId"] = approval.Id
                    },
                    new Dictionary<string, JsonNode?>
                    {
                        ["success"] = false,
                        ["status"] = ToolExecutionStatus.Denied.ToStorageValue(),
                        ["errorCode"] = reasonCode,
                        ["approvalRequestId"] = approval.Id,
                        ["notificationIsApproval"] = false
                    }, UtcNow(), reasonCode);
            }
            var notifications = await _dbContext.CompanyNotifications.IgnoreQueryFilters()
                .Where(item => item.CompanyId == run.CompanyId &&
                               item.RelatedEntityType == AuditTargetTypes.ApprovalRequest &&
                               item.RelatedEntityId == approval.Id &&
                               item.Status != CompanyNotificationStatus.Actioned)
                .ToListAsync(cancellationToken);
            foreach (var notification in notifications) notification.MarkActioned(actorUserId);
            await _audit.WriteAsync(new AuditEventWriteRequest(run.CompanyId, AuditActorTypes.User,
                actorUserId, AuditEventActions.ApprovalCompleted, AuditTargetTypes.ApprovalRequest,
                approval.Id.ToString("N"), AuditEventOutcomes.Blocked, summary,
                DataSources: ["finance_autonomy", "approvals", "human_control"],
                Metadata: new Dictionary<string, string?>
                {
                    ["runId"] = run.Id.ToString("N"),
                    ["approvalStatus"] = approval.Status.ToStorageValue(),
                    ["notificationIsApproval"] = "false"
                }, CorrelationId: run.CorrelationId), cancellationToken);
        }
    }

    public async Task<FinanceAutonomyStepLeaseDto?> ClaimStepAsync(
        Guid companyId, ClaimFinanceAutonomyStepCommand command, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        if (command.LeaseSeconds is < 5 or > 1800) throw Validation(nameof(command.LeaseSeconds), "Lease seconds must be between 5 and 1800.");
        var run = await LoadRunAsync(companyId, command.RunId, true, cancellationToken);
        var step = RequireStep(run, command.StepId);
        if (FinanceAutonomyRun.IsTerminal(run.Status) || run.Status is FinanceAutonomyRunStatus.Paused or FinanceAutonomyRunStatus.Blocked or FinanceAutonomyRunStatus.AwaitingApproval)
            return null;
        if (!string.Equals(command.CurrentEvidenceHash, run.EvidenceHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(step.EvidenceHash, run.EvidenceHash, StringComparison.OrdinalIgnoreCase))
        {
            await BlockStaleAsync(run, step, FinanceAutonomyRunReasonCodes.EvidenceChanged,
                "The evidence snapshot changed before the step was claimed.", cancellationToken);
            if (_budgets is not null) await _budgets.RecordCircuitSignalAsync(companyId,
                new(run.AgentId, run.CapabilityId, FinanceAutonomyCircuitSignals.StaleEvidence,
                    run.CorrelationId, "Repeated stale evidence blocked Finance autonomy work."), cancellationToken);
            return null;
        }
        if (step.DependencyStepKeys.Any(key => run.Steps.All(x => x.StepKey != key || x.Status != FinanceAutonomyStepStatus.Completed)))
            return null;

        if (step.Status == FinanceAutonomyStepStatus.Running && step.LeaseExpiresUtc <= UtcNow())
        {
            var expiredAttempt = step.AttemptCount;
            var executeMayHaveEscaped = IsExecute(step.ActionClass);
            var recoveryStatus = executeMayHaveEscaped
                ? FinanceAutonomyStepStatus.Reconciling
                : FinanceAutonomyStepStatus.Queued;
            var recoveryReason = executeMayHaveEscaped
                ? FinanceAutonomyRunReasonCodes.AmbiguousOutcome
                : FinanceAutonomyRunReasonCodes.LeaseRecovered;
            var recoverySummary = executeMayHaveEscaped
                ? "The execute lease expired after dispatch may have begun. Provider reconciliation is required before any retry."
                : "The expired read or recommendation lease was recovered for a bounded retry.";
            step.RecoverExpiredLease(recoveryStatus, recoveryReason, recoverySummary, UtcNow());
            if (_budgets is not null)
                await _budgets.ReconcileForAttemptAsync(companyId, run.Id, step.Id, expiredAttempt,
                    DefaultActualUsage(step, UtcNow()), false, cancellationToken);
            CompleteAttempt(step, recoveryStatus.ToStorageValue(), recoveryReason, recoverySummary, null, UtcNow());
            if (executeMayHaveEscaped)
            {
                var previousStatus = run.Status;
                run.Transition(FinanceAutonomyRunStatus.Reconciling, recoveryReason, recoverySummary, UtcNow());
                AddHistory(run, previousStatus, recoveryReason, recoverySummary, AuditActorTypes.System, null, UtcNow());
                await WriteAuditAsync(run, null, AuditEventActions.FinanceAutonomyStepReleased,
                    AuditEventOutcomes.Blocked, recoverySummary, cancellationToken);
                await SaveAsync(cancellationToken);
                return null;
            }
        }

        var decision = await _policyEvaluator.EvaluateAsync(new FinanceAutonomyEvaluationRequest(
            companyId, run.AgentId, run.CapabilityId, run.Trigger, step.ActionClass, step.ToolName,
            EvidenceObservedUtc: run.EvidenceObservedUtc), cancellationToken);
        if (!decision.IsAllowed || decision.GrantVersionId != run.GrantVersionId ||
            !string.Equals(decision.PolicyVersion, run.PolicyVersion, StringComparison.Ordinal) ||
            !string.Equals(decision.AuthorityVersion, step.AuthorityVersion, StringComparison.Ordinal) ||
            !string.Equals(decision.AuthorityHash, step.AuthorityHash, StringComparison.OrdinalIgnoreCase))
        {
            await BlockStaleAsync(run, step, FinanceAutonomyRunReasonCodes.PolicyChanged,
                "Current grant, tool policy, or agent authority no longer matches the immutable step snapshot.", cancellationToken);
            if (_budgets is not null) await _budgets.RecordCircuitSignalAsync(companyId,
                new(run.AgentId, run.CapabilityId, FinanceAutonomyCircuitSignals.PolicyDenial,
                    run.CorrelationId, "Repeated current-policy denials blocked Finance autonomy work."), cancellationToken);
            return null;
        }

        var now = UtcNow();
        var plannedUsage = command.PlannedUsage ?? DefaultAttemptUsage(step, command.LeaseSeconds);
        if (_budgets is not null)
        {
            var budget = await _budgets.ReserveForClaimAsync(companyId, run.Id, step.Id,
                step.AttemptCount + 1, plannedUsage, cancellationToken);
            if (!budget.Allowed)
            {
                await BlockStaleAsync(run, step, budget.ReasonCode, budget.SafeSummary, cancellationToken);
                return null;
            }
        }
        if (!step.TryClaim(command.WorkerId, command.LeaseToken, now, TimeSpan.FromSeconds(command.LeaseSeconds)))
        {
            // A reservation is deliberately staged in the same unit of work as the lease. If the
            // lease lost a local race, discard both rather than leaking capacity.
            _dbContext.ChangeTracker.Clear();
            return null;
        }
        var attempt = new FinanceAutonomyStepAttempt(Guid.NewGuid(), companyId, run.Id, step.Id,
            step.AttemptCount, command.WorkerId, Hash(command.LeaseToken), run.PolicyVersion,
            step.AuthorityVersion, step.AuthorityHash, step.EvidenceHash, now);
        _dbContext.FinanceAutonomyStepAttempts.Add(attempt);
        var previous = run.Status;
        if (run.Status != FinanceAutonomyRunStatus.Running)
        {
            run.Transition(FinanceAutonomyRunStatus.Running, FinanceAutonomyRunReasonCodes.Validated,
                "An eligible Finance autonomy step was leased to a worker.", now);
            AddHistory(run, previous, FinanceAutonomyRunReasonCodes.Validated,
                "An eligible Finance autonomy step was leased to a worker.", AuditActorTypes.System, null, now);
        }
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return null;
        }
        catch (DbUpdateException)
        {
            // A concurrent first reservation can lose the unique-window/key race. The winning
            // transaction owns the lease and capacity; this worker safely backs off.
            _dbContext.ChangeTracker.Clear();
            return null;
        }
        return new FinanceAutonomyStepLeaseDto(run.Id, step.Id, command.LeaseToken, step.LeaseExpiresUtc!.Value,
            step.AttemptCount, step.ToolName, step.ActionClass, run.GrantVersionId, run.PolicyVersion,
            step.AuthorityVersion, step.AuthorityHash, step.EvidenceHash, step.RequestedEffectHash);
    }

    public async Task<bool> HeartbeatStepAsync(
        Guid companyId, HeartbeatFinanceAutonomyStepCommand command, CancellationToken cancellationToken)
    {
        var run = await LoadRunAsync(companyId, command.RunId, true, cancellationToken);
        var step = RequireStep(run, command.StepId);
        try
        {
            step.Heartbeat(command.LeaseToken, UtcNow(), TimeSpan.FromSeconds(command.LeaseSeconds));
            await SaveAsync(cancellationToken);
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }

    public async Task<FinanceAutonomyRunDto> CompleteStepAsync(
        Guid companyId, CompleteFinanceAutonomyStepCommand command, CancellationToken cancellationToken)
    {
        var run = await LoadRunAsync(companyId, command.RunId, true, cancellationToken);
        var step = RequireStep(run, command.StepId);
        var now = UtcNow();
        step.Complete(command.LeaseToken, command.ToolExecutionAttemptId, command.ActualEffectHash,
            command.ActualEffectStatus, command.ActualEffectSummary, now);
        if (_budgets is not null) await _budgets.ReconcileForAttemptAsync(companyId, run.Id, step.Id,
            step.AttemptCount, command.ActualUsage ?? DefaultActualUsage(step, now), false, cancellationToken);
        CompleteAttempt(step, "completed", FinanceAutonomyRunReasonCodes.StepCompleted,
            command.ActualEffectSummary, command.ToolExecutionAttemptId, now);
        if (!string.Equals(command.ActualEffectStatus, "no_effect", StringComparison.OrdinalIgnoreCase)) run.MarkCompletedEffect(now);
        var previous = run.Status;
        var allComplete = run.Steps.All(x => x.Status == FinanceAutonomyStepStatus.Completed);
        var next = allComplete ? FinanceAutonomyRunStatus.Completed : FinanceAutonomyRunStatus.Queued;
        run.Transition(next, FinanceAutonomyRunReasonCodes.StepCompleted,
            allComplete ? "All planned Finance autonomy steps completed." : "A step completed and dependent work is queued.", now);
        AddHistory(run, previous, FinanceAutonomyRunReasonCodes.StepCompleted, run.SafeSummary,
            AuditActorTypes.System, null, now);
        await WriteAuditAsync(run, null, AuditEventActions.FinanceAutonomyStepCompleted,
            AuditEventOutcomes.Succeeded, run.SafeSummary!, cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(run);
    }

    public async Task<FinanceAutonomyRunDto> ReleaseStepAsync(
        Guid companyId, ReleaseFinanceAutonomyStepCommand command, CancellationToken cancellationToken)
    {
        var run = await LoadRunAsync(companyId, command.RunId, true, cancellationToken);
        var step = RequireStep(run, command.StepId);
        var next = ParseStepStatus(command.NextStatus);
        if (next == FinanceAutonomyStepStatus.Queued && step.AttemptCount >= step.MaximumAttempts)
            next = FinanceAutonomyStepStatus.DeadLettered;
        var now = UtcNow();
        step.Release(command.LeaseToken, next, command.ReasonCode, command.SafeSummary, now,
            command.ReconciliationReference);
        if (_budgets is not null) await _budgets.ReconcileForAttemptAsync(companyId, run.Id, step.Id,
            step.AttemptCount, command.ActualUsage ?? DefaultActualUsage(step, now), false, cancellationToken);
        CompleteAttempt(step, next.ToStorageValue(), command.ReasonCode, command.SafeSummary, command.ToolExecutionAttemptId, now);
        var runNext = next switch
        {
            FinanceAutonomyStepStatus.Queued => FinanceAutonomyRunStatus.Queued,
            FinanceAutonomyStepStatus.Reconciling => FinanceAutonomyRunStatus.Reconciling,
            FinanceAutonomyStepStatus.Paused => FinanceAutonomyRunStatus.Paused,
            FinanceAutonomyStepStatus.DeadLettered => FinanceAutonomyRunStatus.DeadLettered,
            FinanceAutonomyStepStatus.Failed => FinanceAutonomyRunStatus.Failed,
            _ => FinanceAutonomyRunStatus.Blocked
        };
        var previous = run.Status;
        run.Transition(runNext, command.ReasonCode, command.SafeSummary, now);
        AddHistory(run, previous, command.ReasonCode, command.SafeSummary, AuditActorTypes.System, null, now);
        await WriteAuditAsync(run, null, AuditEventActions.FinanceAutonomyStepReleased,
            runNext == FinanceAutonomyRunStatus.DeadLettered ? AuditEventOutcomes.Failed : AuditEventOutcomes.Blocked,
            command.SafeSummary ?? command.ReasonCode, cancellationToken);
        await SaveAsync(cancellationToken);
        if (_budgets is not null && next is (FinanceAutonomyStepStatus.Reconciling or FinanceAutonomyStepStatus.Queued or
            FinanceAutonomyStepStatus.Failed or FinanceAutonomyStepStatus.DeadLettered))
            await _budgets.RecordCircuitSignalAsync(companyId, new(run.AgentId, run.CapabilityId,
                next == FinanceAutonomyStepStatus.Reconciling ? FinanceAutonomyCircuitSignals.ProviderAmbiguity : FinanceAutonomyCircuitSignals.Error,
                run.CorrelationId, command.SafeSummary), cancellationToken);
        return Map(run);
    }

    public async Task<FinanceAutonomyRunDto> AwaitApprovalStepAsync(
        Guid companyId, AwaitFinanceAutonomyStepApprovalCommand command, CancellationToken cancellationToken)
    {
        var run = await LoadRunAsync(companyId, command.RunId, true, cancellationToken);
        var step = RequireStep(run, command.StepId);
        var approvalExists = await _dbContext.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == command.ApprovalRequestId &&
                           x.ToolExecutionAttemptId == command.ToolExecutionAttemptId, cancellationToken);
        if (!approvalExists)
            throw Validation(nameof(command.ApprovalRequestId), "The durable tool approval is unavailable in this company.");
        var now = UtcNow();
        step.AwaitApproval(command.LeaseToken, command.ApprovalRequestId, command.ToolExecutionAttemptId,
            command.SafeSummary, now);
        if (_budgets is not null)
            await _budgets.ReconcileForAttemptAsync(companyId, run.Id, step.Id, step.AttemptCount,
                command.ActualUsage ?? DefaultActualUsage(step, now), false, cancellationToken);
        CompleteAttempt(step, "awaiting_approval", FinanceAutonomyRunReasonCodes.ApprovalRequired,
            command.SafeSummary, command.ToolExecutionAttemptId, now);
        var previous = run.Status;
        run.Transition(FinanceAutonomyRunStatus.AwaitingApproval, FinanceAutonomyRunReasonCodes.ApprovalRequired,
            command.SafeSummary ?? "The exact tool action is awaiting approval.", now);
        AddHistory(run, previous, FinanceAutonomyRunReasonCodes.ApprovalRequired, run.SafeSummary,
            AuditActorTypes.System, null, now);
        await WriteAuditAsync(run, null, AuditEventActions.FinanceAutonomyStepApprovalBound,
            AuditEventOutcomes.Pending, run.SafeSummary!, cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(run);
    }

    public async Task<bool> ResolveApprovalAsync(
        Guid companyId, ResolveFinanceAutonomyApprovalCommand command, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var run = await QueryRun(companyId, true)
            .SingleOrDefaultAsync(item => item.Steps.Any(step => step.ApprovalRequestId == command.ApprovalRequestId), cancellationToken);
        if (run is null) return false;
        var step = run.Steps.Single(item => item.ApprovalRequestId == command.ApprovalRequestId);
        if (step.Status != FinanceAutonomyStepStatus.AwaitingApproval) return false;

        var approvalStatus = Normalize(command.ApprovalStatus);
        var toolStatus = Normalize(command.ToolExecutionStatus);
        if (approvalStatus == ApprovalRequestStatus.Pending.ToStorageValue() ||
            approvalStatus == ApprovalRequestStatus.Approved.ToStorageValue() &&
            toolStatus == ToolExecutionStatus.AwaitingApproval.ToStorageValue())
            return false;

        var now = UtcNow();
        var previous = run.Status;
        string reasonCode;
        string summary;
        string outcome;
        FinanceAutonomyRunStatus next;
        if (approvalStatus == ApprovalRequestStatus.Approved.ToStorageValue() &&
            toolStatus == ToolExecutionStatus.Executed.ToStorageValue())
        {
            reasonCode = FinanceAutonomyRunReasonCodes.ApprovalApproved;
            summary = command.DecisionSummary ?? "The independently approved exact action passed P0 revalidation and completed.";
            outcome = "executed";
            var resultJson = CanonicalJson(command.ToolResult ?? new Dictionary<string, JsonNode?>());
            step.ResolveApproval(outcome, reasonCode, Hash(resultJson),
                IsExecute(step.ActionClass) ? "approved_effect" : "no_effect", summary, now);
            if (IsExecute(step.ActionClass)) run.MarkCompletedEffect(now);
            next = run.Steps.All(item => item.Status == FinanceAutonomyStepStatus.Completed)
                ? FinanceAutonomyRunStatus.Completed
                : FinanceAutonomyRunStatus.Queued;
        }
        else if (approvalStatus == ApprovalRequestStatus.Approved.ToStorageValue() &&
                 toolStatus == ToolExecutionStatus.ReconciliationRequired.ToStorageValue())
        {
            reasonCode = FinanceAutonomyRunReasonCodes.ReconciliationRequired;
            summary = "The approved action has an ambiguous provider outcome and requires human reconciliation.";
            outcome = "reconciliation_required";
            step.ResolveApproval(outcome, reasonCode, null, null, summary, now);
            next = FinanceAutonomyRunStatus.Reconciling;
        }
        else
        {
            reasonCode = approvalStatus switch
            {
                "rejected" => FinanceAutonomyRunReasonCodes.ApprovalRejected,
                "changes_requested" => FinanceAutonomyRunReasonCodes.ApprovalChangesRequested,
                "cancelled" => FinanceAutonomyRunReasonCodes.ApprovalCancelled,
                "expired" => FinanceAutonomyRunReasonCodes.ApprovalExpired,
                "revoked" => FinanceAutonomyRunReasonCodes.ApprovalRevoked,
                "superseded" => FinanceAutonomyRunReasonCodes.ApprovalSuperseded,
                "stale" => FinanceAutonomyRunReasonCodes.ApprovalStale,
                _ => FinanceAutonomyRunReasonCodes.ApprovalStale
            };
            summary = command.DecisionSummary ?? command.DenialReason ?? approvalStatus switch
            {
                "changes_requested" => "Changes were requested. Narrow the plan into a new validated revision before review.",
                "cancelled" => "The pending approved work was cancelled by an authorized human.",
                "expired" => "The exact-action approval expired. No replacement request was created.",
                "revoked" => "Approval authority was revoked. Review the grant and create a new plan if work remains.",
                "superseded" => "The approval was superseded. Review the replacement work before continuing.",
                _ => "The exact-action approval did not authorize continuation. Review the run before any further work."
            };
            outcome = approvalStatus == "cancelled" ? "cancelled" :
                approvalStatus == "superseded" ? "superseded" : "blocked";
            step.ResolveApproval(outcome, reasonCode, null, null, summary, now);
            next = approvalStatus switch
            {
                "cancelled" => FinanceAutonomyRunStatus.Cancelled,
                "superseded" => FinanceAutonomyRunStatus.Superseded,
                _ => FinanceAutonomyRunStatus.Blocked
            };
            if (next is FinanceAutonomyRunStatus.Cancelled or FinanceAutonomyRunStatus.Superseded)
                foreach (var dependent in run.Steps.Where(item => item.Id != step.Id))
                    dependent.CancelOrSupersede(next == FinanceAutonomyRunStatus.Superseded, now);
        }

        run.Transition(next, reasonCode, summary, now);
        AddHistory(run, previous, reasonCode, summary, AuditActorTypes.System, null, now);
        await WriteAuditAsync(run, null, AuditEventActions.FinanceAutonomyRunTransitioned,
            next is FinanceAutonomyRunStatus.Queued or FinanceAutonomyRunStatus.Completed
                ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
            summary, cancellationToken);
        await SaveAsync(cancellationToken);
        return true;
    }

    public async Task<FinanceAutonomyRunDto> ReconcileStepAsync(
        Guid companyId, Guid runId, Guid stepId, ReconcileFinanceAutonomyStepCommand command,
        CancellationToken cancellationToken)
    {
        var actor = await RequireManagerAsync(companyId, cancellationToken);
        var run = await LoadRunAsync(companyId, runId, true, cancellationToken);
        var step = RequireStep(run, stepId);
        if (command.ExpectedStepVersion > 0 && step.Version != command.ExpectedStepVersion)
            throw new FinanceAutonomyRunConcurrencyException("The Finance autonomy step changed. Refresh and retry.");
        var outcome = Normalize(command.Outcome);
        if (!FinanceAutonomyReconciliationOutcomes.All.Contains(outcome))
            throw Validation(nameof(command.Outcome), "The reconciliation outcome is not supported.");
        ValidateHash(command.ActualEffectHash, nameof(command.ActualEffectHash));
        if ((command.ProviderReference?.Length ?? 0) > 240)
            throw Validation(nameof(command.ProviderReference), "The provider reference must be 240 characters or fewer.");
        var now = UtcNow();
        step.ResolveReconciliation(outcome, command.ActualEffectHash, command.ActualEffectSummary,
            command.ProviderReference, now);
        if (outcome == FinanceAutonomyReconciliationOutcomes.ConfirmedApplied) run.MarkCompletedEffect(now);
        var previous = run.Status;
        var next = step.Status switch
        {
            FinanceAutonomyStepStatus.Completed when run.Steps.All(x => x.Status == FinanceAutonomyStepStatus.Completed) => FinanceAutonomyRunStatus.Completed,
            FinanceAutonomyStepStatus.Completed => FinanceAutonomyRunStatus.Queued,
            FinanceAutonomyStepStatus.Queued => FinanceAutonomyRunStatus.Queued,
            FinanceAutonomyStepStatus.DeadLettered => FinanceAutonomyRunStatus.DeadLettered,
            _ => FinanceAutonomyRunStatus.Failed
        };
        run.Transition(next, step.ReasonCode!, command.ActualEffectSummary, now);
        AddHistory(run, previous, step.ReasonCode!, command.ActualEffectSummary,
            AuditActorTypes.User, actor.UserId, now);
        await WriteAuditAsync(run, actor.UserId, AuditEventActions.FinanceAutonomyStepReconciled,
            next is FinanceAutonomyRunStatus.Completed or FinanceAutonomyRunStatus.Queued
                ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Failed,
            command.ActualEffectSummary ?? "The ambiguous provider outcome was reconciled.", cancellationToken);
        await SaveAsync(cancellationToken);
        return Map(run);
    }

    private async Task<FinanceAutonomyRunDto> CreateCoreAsync(
        Guid companyId, CreateOrCoalesceFinanceAutonomyRunCommand command, Guid? replayOfRunId,
        Guid? replayCheckpointStepId, IReadOnlyDictionary<string, Guid>? replayStepIds,
        Guid? replayActorId, Guid? revisionOfRunId, int revisionNumber,
        CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        try { ValidateCreate(command); }
        catch (FinanceAutonomyRunValidationException)
        {
            if (_budgets is not null && command.AgentId != Guid.Empty && !string.IsNullOrWhiteSpace(command.CapabilityId))
                await _budgets.RecordCircuitSignalAsync(companyId, new(command.AgentId, command.CapabilityId,
                    FinanceAutonomyCircuitSignals.InvalidPlan,
                    string.IsNullOrWhiteSpace(command.CorrelationId)
                        ? $"finance-invalid-plan:{companyId:N}:{command.AgentId:N}"
                        : command.CorrelationId,
                    "Repeated invalid Finance autonomy plans require operator review."), cancellationToken);
            throw;
        }
        await ValidateOwnedLinksAsync(companyId, command, cancellationToken);
        var evidenceJson = CanonicalJson(command.EvidenceSnapshot);
        var evidenceHash = Hash(evidenceJson);
        var planJson = CanonicalJson(command.Steps);
        var planHash = Hash(planJson);
        var budgetJson = CanonicalJson(command.BudgetSnapshot);
        var budgetHash = Hash(budgetJson);
        FinanceAutonomyDecisionDto? governingDecision = null;
        foreach (var step in command.Steps)
        {
            var decision = await _policyEvaluator.EvaluateAsync(new FinanceAutonomyEvaluationRequest(
                companyId, command.AgentId, command.CapabilityId, command.Trigger, step.ActionClass, step.ToolName,
                command.RecordCount, command.Amount, Utc(command.EvidenceObservedUtc), command.Steps.Count), cancellationToken);
            if (!decision.IsAllowed || decision.GrantId is null || decision.GrantVersionId is null || decision.GrantVersionNumber is null)
                throw Validation(nameof(command.Steps), $"Step '{step.StepKey}' is not permitted: {decision.ReasonCode}.");
            governingDecision ??= decision;
            if (decision.GrantVersionId != governingDecision.GrantVersionId ||
                !string.Equals(decision.AuthorityHash, governingDecision.AuthorityHash, StringComparison.OrdinalIgnoreCase))
                throw Validation(nameof(command.Steps), "All steps must be authorized by the same active grant and authority snapshot.");
        }
        var governing = governingDecision!;
        var logicalKey = BuildLogicalKey(companyId, governing.GrantVersionId!.Value, command);
        var existing = await QueryRun(companyId, true).SingleOrDefaultAsync(x => x.LogicalKey == logicalKey, cancellationToken);
        if (existing is not null)
        {
            await MergeCoalescedSourcesAsync(existing, command.Sources, cancellationToken);
            return Map(existing);
        }

        var now = UtcNow();
        var run = new FinanceAutonomyRun(Guid.NewGuid(), companyId, command.AgentId, command.CapabilityId,
            governing.GrantId!.Value, governing.GrantVersionId.Value, governing.GrantVersionNumber!.Value,
            command.Trigger, command.TriggerKey, Utc(command.WindowStartUtc), Utc(command.WindowEndUtc),
            command.AuthoritativeEventId, command.AuthoritativeEventVersion, logicalKey, command.IdempotencyKey,
            command.CorrelationId, evidenceJson, evidenceHash, Utc(command.EvidenceObservedUtc),
            planJson, planHash, command.PlanVersion, budgetJson, budgetHash, governing.PolicyVersion,
            governing.CatalogueVersion ?? "unknown", governing.AuthorityVersion ?? "unknown",
            governing.AuthorityHash ?? throw Validation(nameof(governing.AuthorityHash), "The authority snapshot is missing."),
            command.OriginatingGoalId, command.OriginatingTaskId, command.WorkflowInstanceId, command.OrchestrationRunId,
            replayOfRunId, replayCheckpointStepId, now, revisionOfRunId, revisionNumber);
        _dbContext.FinanceAutonomyRuns.Add(run);
        for (var index = 0; index < command.Steps.Count; index++)
        {
            var definition = command.Steps[index];
            var step = new FinanceAutonomyRunStep(Guid.NewGuid(), companyId, run.Id, index + 1,
                definition.StepKey, definition.ActionClass, definition.ToolName, definition.DependencyStepKeys,
                definition.MaximumAttempts, governing.PolicyVersion, governing.AuthorityVersion!, governing.AuthorityHash!,
                evidenceHash, definition.RequestedEffectHash, definition.RequestedEffectSummary,
                definition.ReplayPermitted, definition.WorkTaskId, now,
                replayStepIds is not null && replayStepIds.TryGetValue(definition.StepKey, out var replayStepId) ? replayStepId : null,
                definition.BusinessIdempotencyKey);
            step.Queue(now);
            run.Steps.Add(step);
        }
        foreach (var source in command.Sources)
            run.Sources.Add(new FinanceAutonomyRunSourceReference(Guid.NewGuid(), companyId, run.Id,
                source.SourceType, source.EntityType, source.EntityId, source.SourceVersion, source.ContentHash, source.SafeLabel, now));
        AddHistory(run, null, replayOfRunId.HasValue ? FinanceAutonomyRunReasonCodes.Replayed : FinanceAutonomyRunReasonCodes.Created,
            replayOfRunId.HasValue ? "A safe operator replay was created from a permitted checkpoint." : "A durable Finance autonomy run was created.",
            replayActorId.HasValue ? AuditActorTypes.User : AuditActorTypes.System, replayActorId, now);
        var previous = run.Status;
        run.Transition(FinanceAutonomyRunStatus.Validating, FinanceAutonomyRunReasonCodes.Validated,
            "The immutable run inputs passed creation validation.", now);
        AddHistory(run, previous, FinanceAutonomyRunReasonCodes.Validated, run.SafeSummary,
            replayActorId.HasValue ? AuditActorTypes.User : AuditActorTypes.System, replayActorId, now);
        previous = run.Status;
        run.Transition(FinanceAutonomyRunStatus.Queued, FinanceAutonomyRunReasonCodes.Validated,
            "Eligible steps are queued for leased execution.", now);
        AddHistory(run, previous, FinanceAutonomyRunReasonCodes.Validated, run.SafeSummary,
            replayActorId.HasValue ? AuditActorTypes.User : AuditActorTypes.System, replayActorId, now);
        await WriteAuditAsync(run, replayActorId,
            replayOfRunId.HasValue ? AuditEventActions.FinanceAutonomyRunReplayed : AuditEventActions.FinanceAutonomyRunCreated,
            AuditEventOutcomes.Succeeded, run.SafeSummary!, cancellationToken);
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            existing = await QueryRun(companyId, true).SingleOrDefaultAsync(x => x.LogicalKey == logicalKey, cancellationToken);
            if (existing is not null)
            {
                await MergeCoalescedSourcesAsync(existing, command.Sources, cancellationToken);
                return Map(existing);
            }
            throw;
        }
        return Map(run);
    }

    private async Task MergeCoalescedSourcesAsync(FinanceAutonomyRun run,
        IReadOnlyList<FinanceAutonomyRunSourceDefinition> sources, CancellationToken cancellationToken)
    {
        var existingKeys = run.Sources.Select(x =>
            $"{Normalize(x.SourceType)}|{Normalize(x.EntityType)}|{Normalize(x.EntityId)}|{Normalize(x.SourceVersion)}")
            .ToHashSet(StringComparer.Ordinal);
        var now = UtcNow();
        var added = 0;
        foreach (var source in sources)
        {
            if (run.Sources.Count >= MaximumSources) break;
            var key = $"{Normalize(source.SourceType)}|{Normalize(source.EntityType)}|{Normalize(source.EntityId)}|{Normalize(source.SourceVersion)}";
            if (!existingKeys.Add(key)) continue;
            var reference = new FinanceAutonomyRunSourceReference(Guid.NewGuid(), run.CompanyId, run.Id,
                source.SourceType, source.EntityType, source.EntityId, source.SourceVersion,
                source.ContentHash, source.SafeLabel, now);
            run.Sources.Add(reference);
            _dbContext.FinanceAutonomyRunSources.Add(reference);
            added++;
        }
        if (added == 0) return;
        await WriteAuditAsync(run, null, AuditEventActions.FinanceAutonomyRunCoalesced,
            AuditEventOutcomes.Succeeded, $"{added} additional authoritative source reference(s) were retained on the existing run.",
            cancellationToken);
        await SaveAsync(cancellationToken);
    }

    private async Task BlockStaleAsync(FinanceAutonomyRun run, FinanceAutonomyRunStep step,
        string reasonCode, string summary, CancellationToken cancellationToken)
    {
        step.Block(reasonCode, summary, UtcNow());
        var previous = run.Status;
        run.Transition(FinanceAutonomyRunStatus.Blocked, reasonCode, summary, UtcNow());
        AddHistory(run, previous, reasonCode, summary, AuditActorTypes.System, null, UtcNow());
        await WriteAuditAsync(run, null, AuditEventActions.FinanceAutonomyRunBlocked,
            AuditEventOutcomes.Blocked, summary, cancellationToken);
        await SaveAsync(cancellationToken);
    }

    private void CompleteAttempt(FinanceAutonomyRunStep step, string outcome, string? reasonCode,
        string? safeSummary, Guid? toolExecutionAttemptId, DateTime now)
    {
        var attempt = step.Attempts.SingleOrDefault(x => x.AttemptNumber == step.AttemptCount && !x.CompletedUtc.HasValue)
            ?? _dbContext.FinanceAutonomyStepAttempts.Local.SingleOrDefault(x => x.StepId == step.Id && x.AttemptNumber == step.AttemptCount);
        attempt?.Complete(outcome, reasonCode, safeSummary, toolExecutionAttemptId, now);
    }

    private IQueryable<FinanceAutonomyRun> QueryRun(Guid companyId, bool tracking)
    {
        var query = _dbContext.FinanceAutonomyRuns.IgnoreQueryFilters().Where(x => x.CompanyId == companyId)
            .Include(x => x.Steps).ThenInclude(x => x.Attempts).Include(x => x.History).Include(x => x.Sources);
        return tracking ? query : query.AsNoTracking();
    }

    private async Task<FinanceAutonomyRun> LoadRunAsync(Guid companyId, Guid runId, bool tracking, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        if (runId == Guid.Empty) throw new ArgumentException("RunId is required.", nameof(runId));
        return await QueryRun(companyId, tracking).SingleOrDefaultAsync(x => x.Id == runId, cancellationToken)
            ?? throw new KeyNotFoundException("Finance autonomy run was not found.");
    }

    private static FinanceAutonomyRunStep RequireStep(FinanceAutonomyRun run, Guid stepId) =>
        run.Steps.SingleOrDefault(x => x.Id == stepId) ?? throw new KeyNotFoundException("Finance autonomy run step was not found.");

    private static void ValidateCreate(CreateOrCoalesceFinanceAutonomyRunCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.AgentId == Guid.Empty) throw Validation(nameof(command.AgentId), "AgentId is required.");
        if (!FinanceAutonomyTriggers.All.Contains(Normalize(command.Trigger)))
            throw Validation(nameof(command.Trigger), "The trigger is not supported by the active Finance autonomy policy.");
        if (Utc(command.WindowEndUtc) <= Utc(command.WindowStartUtc))
            throw Validation(nameof(command.WindowEndUtc), "The trigger window must end after it starts.");
        if (Normalize(command.Trigger) == FinanceAutonomyTriggers.BusinessEvent &&
            (string.IsNullOrWhiteSpace(command.AuthoritativeEventId) || string.IsNullOrWhiteSpace(command.AuthoritativeEventVersion)))
            throw Validation(nameof(command.AuthoritativeEventId), "Business-event runs require an authoritative event id and version.");
        if (command.Steps.Count is < 1 or > MaximumSteps)
            throw Validation(nameof(command.Steps), $"A run must contain between 1 and {MaximumSteps} bounded steps.");
        if (command.Sources.Count > MaximumSources)
            throw Validation(nameof(command.Sources), $"A run can retain at most {MaximumSources} source references.");
        if (command.EvidenceSnapshot.Count > 200)
            throw Validation(nameof(command.EvidenceSnapshot), "The evidence snapshot is too broad.");
        if (command.EvidenceSnapshot.Any(x => x.Key.Length > 160 || (x.Value?.Length ?? 0) > 2000))
            throw Validation(nameof(command.EvidenceSnapshot), "Evidence snapshot keys and values must be bounded summaries, not raw provider content.");
        var forbiddenEvidenceKeys = new[] { "chain_of_thought", "hidden_reasoning", "raw_provider", "provider_payload", "access_token", "refresh_token", "secret" };
        if (command.EvidenceSnapshot.Keys.Any(key => forbiddenEvidenceKeys.Any(forbidden => key.Contains(forbidden, StringComparison.OrdinalIgnoreCase))))
            throw Validation(nameof(command.EvidenceSnapshot), "Evidence snapshots cannot contain hidden reasoning, credentials, or raw provider payloads.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in command.Steps)
        {
            var key = Normalize(step.StepKey);
            if (string.IsNullOrWhiteSpace(key) || !seen.Add(key))
                throw Validation(nameof(command.Steps), "Step keys must be non-empty and unique.");
            if (step.DependencyStepKeys.Any(dependency => !seen.Contains(Normalize(dependency))))
                throw Validation(nameof(command.Steps), $"Step '{step.StepKey}' has a missing, forward, or cyclic dependency.");
            ValidateHash(step.RequestedEffectHash, nameof(step.RequestedEffectHash));
            if ((step.BusinessIdempotencyKey?.Length ?? 0) > 200)
                throw Validation(nameof(command.Steps), $"Step '{step.StepKey}' has an oversized business idempotency key.");
            if ((step.Scope?.Length ?? 0) > 160)
                throw Validation(nameof(command.Steps), $"Step '{step.StepKey}' has an oversized tool scope.");
            if (step.RequestPayload is not null)
            {
                if (step.RequestPayload.Count > 100 || JsonSerializer.Serialize(step.RequestPayload).Length > 64_000)
                    throw Validation(nameof(command.Steps), $"Step '{step.StepKey}' has an oversized tool payload.");
                var forbiddenPayloadKeys = new[] { "access_token", "refresh_token", "client_secret", "password", "chain_of_thought", "hidden_reasoning" };
                if (step.RequestPayload.Keys.Any(key => forbiddenPayloadKeys.Any(forbidden => key.Contains(forbidden, StringComparison.OrdinalIgnoreCase))))
                    throw Validation(nameof(command.Steps), $"Step '{step.StepKey}' payload contains credentials or hidden reasoning.");
            }
        }
        foreach (var source in command.Sources) ValidateHash(source.ContentHash, nameof(source.ContentHash));
    }

    private async Task ValidateOwnedLinksAsync(
        Guid companyId, CreateOrCoalesceFinanceAutonomyRunCommand command, CancellationToken cancellationToken)
    {
        if (command.OriginatingGoalId.HasValue && !await _dbContext.CompanyGoals.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == command.OriginatingGoalId.Value, cancellationToken))
            throw Validation(nameof(command.OriginatingGoalId), "The originating goal does not belong to this company.");
        var taskIds = command.Steps.Where(x => x.WorkTaskId.HasValue).Select(x => x.WorkTaskId!.Value)
            .Append(command.OriginatingTaskId ?? Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToArray();
        if (taskIds.Length > 0)
        {
            var ownedTaskCount = await _dbContext.WorkTasks.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.CompanyId == companyId && taskIds.Contains(x.Id), cancellationToken);
            if (ownedTaskCount != taskIds.Length)
                throw Validation(nameof(command.OriginatingTaskId), "Every linked task must belong to this company.");
        }
        if (command.WorkflowInstanceId.HasValue && !await _dbContext.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == command.WorkflowInstanceId.Value, cancellationToken))
            throw Validation(nameof(command.WorkflowInstanceId), "The linked workflow instance does not belong to this company.");
        if (command.OrchestrationRunId.HasValue && !await _dbContext.AgentOrchestrationRuns.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == command.OrchestrationRunId.Value, cancellationToken))
            throw Validation(nameof(command.OrchestrationRunId), "The linked orchestration run does not belong to this company.");
    }

    private static string BuildLogicalKey(Guid companyId, Guid grantVersionId, CreateOrCoalesceFinanceAutonomyRunCommand command) =>
        Hash(string.Join("|", companyId.ToString("N"), grantVersionId.ToString("N"), Normalize(command.Trigger),
            Normalize(command.TriggerKey), Utc(command.WindowStartUtc).ToString("O"), Utc(command.WindowEndUtc).ToString("O"),
            Normalize(command.AuthoritativeEventId), Normalize(command.AuthoritativeEventVersion)));

    private void AddHistory(FinanceAutonomyRun run, FinanceAutonomyRunStatus? from, string reasonCode,
        string? summary, string actorType, Guid? actorId, DateTime now)
    {
        var history = new FinanceAutonomyRunHistory(Guid.NewGuid(), run.CompanyId, run.Id,
            from?.ToStorageValue(), run.Status.ToStorageValue(), reasonCode, summary, actorType, actorId,
            run.CorrelationId, now);
        run.History.Add(history);
        _dbContext.FinanceAutonomyRunHistory.Add(history);
    }

    private async Task WriteAuditAsync(FinanceAutonomyRun run, Guid? actorId, string action, string outcome,
        string summary, CancellationToken cancellationToken) =>
        await _audit.WriteAsync(new AuditEventWriteRequest(run.CompanyId,
            actorId.HasValue ? AuditActorTypes.User : AuditActorTypes.System, actorId, action,
            AuditTargetTypes.FinanceAutonomyRun, run.Id.ToString("N"), outcome, summary,
            DataSources: run.Sources.Select(x => $"{x.SourceType}:{x.EntityType}:{x.EntityId}:{x.SourceVersion}").ToArray(),
            Metadata: new Dictionary<string, string?>
            {
                ["grantId"] = run.GrantId.ToString("N"), ["grantVersionId"] = run.GrantVersionId.ToString("N"),
                ["grantVersionNumber"] = run.GrantVersionNumber.ToString(), ["status"] = run.Status.ToStorageValue(),
                ["evidenceHash"] = run.EvidenceHash, ["planHash"] = run.PlanHash,
                ["policyVersion"] = run.PolicyVersion, ["authorityVersion"] = run.AuthorityVersion,
                ["authorityHash"] = run.AuthorityHash, ["hasCompletedEffects"] = run.HasCompletedEffects.ToString()
            }, CorrelationId: run.CorrelationId), cancellationToken);

    private async Task<ResolvedCompanyMembershipContext> RequireManagerAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _membershipResolver.ResolveAsync(companyId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Active company membership is required.");
        if (membership.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager))
            throw new UnauthorizedAccessException("Company manager access is required.");
        return membership;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException ex)
        {
            var entries = string.Join(", ", ex.Entries.Select(entry =>
                $"{entry.Metadata.ClrType.Name}:{entry.State}:{string.Join("/", entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).Select(p => p.CurrentValue))}"));
            throw new FinanceAutonomyRunConcurrencyException($"Finance autonomy run changed concurrently ({entries}): {ex.Message}");
        }
    }

    private static FinanceAutonomyRunDto Map(FinanceAutonomyRun run) => new(
        run.Id, run.CompanyId, run.AgentId, run.CapabilityId, run.GrantId, run.GrantVersionId,
        run.GrantVersionNumber, run.Trigger, run.TriggerKey, run.WindowStartUtc, run.WindowEndUtc,
        run.AuthoritativeEventId, run.AuthoritativeEventVersion, run.LogicalKey, run.IdempotencyKey,
        run.CorrelationId, run.EvidenceHash, run.EvidenceObservedUtc, run.PlanHash, run.PlanVersion,
        run.BudgetHash, run.PolicyVersion, run.CatalogueVersion, run.AuthorityVersion, run.AuthorityHash,
        run.OriginatingGoalId, run.OriginatingTaskId, run.WorkflowInstanceId, run.OrchestrationRunId,
        run.ReplayOfRunId, run.ReplayCheckpointStepId, run.Status.ToStorageValue(), run.ReasonCode,
        run.SafeSummary, run.HasCompletedEffects, run.CreatedUtc, run.UpdatedUtc, run.StartedUtc,
        run.TerminalUtc, run.SensitiveContentRedactedUtc, run.Version,
        run.Steps.OrderBy(x => x.Sequence).Select(MapStep).ToArray(),
        run.History.OrderBy(x => x.OccurredUtc).ThenBy(x => x.Id).Select(x => new FinanceAutonomyRunHistoryDto(
            x.Id, x.FromStatus, x.ToStatus, x.ReasonCode, x.SafeSummary, x.ActorType, x.ActorId,
            x.CorrelationId, x.OccurredUtc)).ToArray(),
        run.Sources.OrderBy(x => x.SourceType).ThenBy(x => x.EntityType).ThenBy(x => x.EntityId)
            .Select(x => new FinanceAutonomyRunSourceDto(x.Id, x.SourceType, x.EntityType, x.EntityId,
                x.SourceVersion, x.ContentHash, x.SafeLabel, x.CreatedUtc)).ToArray(),
        run.RevisionOfRunId, run.RevisionNumber);

    private static FinanceAutonomyRunStepDto MapStep(FinanceAutonomyRunStep x) => new(
        x.Id, x.Sequence, x.StepKey, x.ActionClass, x.ToolName, x.DependencyStepKeys, x.Status.ToStorageValue(),
        x.AttemptCount, x.MaximumAttempts, x.ToolPolicyVersion, x.AuthorityVersion, x.AuthorityHash,
        x.EvidenceHash, x.RequestedEffectHash, x.RequestedEffectSummary, x.ActualEffectHash,
        x.ActualEffectStatus, x.ActualEffectSummary, x.BusinessIdempotencyKey,
        x.ReconciliationReference, x.ApprovalRequestId, x.WorkTaskId,
        x.ToolExecutionAttemptId, x.LeaseOwner, x.LeaseExpiresUtc, x.LastHeartbeatUtc, x.ReplayPermitted,
        x.ReplayOfStepId, x.ReasonCode, x.SafeSummary, x.CreatedUtc, x.UpdatedUtc, x.StartedUtc,
        x.CompletedUtc, x.Version, x.Attempts.OrderBy(a => a.AttemptNumber).Select(a => new FinanceAutonomyStepAttemptDto(
            a.Id, a.AttemptNumber, a.LeaseOwner, a.PolicyVersion, a.AuthorityVersion, a.AuthorityHash,
            a.EvidenceHash, a.Outcome, a.ReasonCode, a.SafeSummary, a.ToolExecutionAttemptId,
            a.StartedUtc, a.CompletedUtc)).ToArray());

    private static FinanceAutonomyRunListItemDto MapListItem(FinanceAutonomyRun run) => new(
        run.Id, run.AgentId, run.CapabilityId, run.GrantId, run.GrantVersionNumber, run.Trigger,
        run.TriggerKey, run.Status.ToStorageValue(), run.ReasonCode, run.HasCompletedEffects,
        run.Steps.Count(x => x.Status == FinanceAutonomyStepStatus.Completed), run.Steps.Count,
        run.CreatedUtc, run.UpdatedUtc, run.Version);

    private static string CanonicalJson<T>(T value) => JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    });
    private static string CanonicalJson(IReadOnlyDictionary<string, string?> value) =>
        CanonicalJson<Dictionary<string, string?>>(value.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
    private static string CanonicalJson(IReadOnlyDictionary<string, decimal> value) =>
        CanonicalJson<Dictionary<string, decimal>>(value.OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static void ValidateHash(string value, string name)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit)) throw Validation(name, "A SHA-256 hash is required.");
    }
    private static FinanceAutonomyRunStatus ParseRunStatus(string value)
    {
        try { return FinanceAutonomyRunEnumValues.ParseRunStatus(Normalize(value)); }
        catch (InvalidOperationException) { throw Validation(nameof(value), "Unknown Finance autonomy run status."); }
    }
    private static FinanceAutonomyStepStatus ParseStepStatus(string value)
    {
        try { return FinanceAutonomyRunEnumValues.ParseStepStatus(Normalize(value)); }
        catch (InvalidOperationException) { throw Validation(nameof(value), "Unknown Finance autonomy step status."); }
    }
    private static string CancellationSummary(string reason, bool hasEffects) => hasEffects
        ? $"{reason.Trim()} Completed effects remain recorded and were not rolled back."
        : reason.Trim();
    private static FinanceAutonomyUsageDefinition DefaultAttemptUsage(FinanceAutonomyRunStep step, int leaseSeconds) => new(
        ExecuteAttempts: IsExecute(step.ActionClass) ? 1 : 0, ToolCalls: 1,
        RuntimeSeconds: Math.Clamp(leaseSeconds, 1, 1800));
    private static FinanceAutonomyUsageDefinition DefaultActualUsage(FinanceAutonomyRunStep step, DateTime now)
    {
        var attemptStartedUtc = step.Attempts.OrderByDescending(x => x.AttemptNumber).FirstOrDefault()?.StartedUtc
            ?? step.StartedUtc ?? now;
        return new(ExecuteAttempts: IsExecute(step.ActionClass) ? 1 : 0, ToolCalls: 1,
            RuntimeSeconds: Math.Max(1, (int)Math.Ceiling((now - attemptStartedUtc).TotalSeconds)));
    }
    private static bool IsExecute(string actionClass) => actionClass.Contains("execute", StringComparison.OrdinalIgnoreCase) ||
        actionClass.Contains("mutation", StringComparison.OrdinalIgnoreCase) || actionClass.Contains("write", StringComparison.OrdinalIgnoreCase);
    private static void EnsureVersion(FinanceAutonomyRun run, long expected)
    {
        if (expected > 0 && run.Version != expected) throw new FinanceAutonomyRunConcurrencyException("The Finance autonomy run changed. Refresh and retry.");
    }
    private static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
    }
    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    private static FinanceAutonomyRunValidationException Validation(string key, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [key] = [message] });
}
