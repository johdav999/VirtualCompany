using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceAutonomyApprovalCoordinator(
    VirtualCompanyDbContext db,
    IFinanceAutonomyRunService runs,
    IAuditEventWriter audit,
    TimeProvider clock) : IFinanceAutonomyApprovalCoordinator
{
    private const string EscalationTaskType = "finance.autonomy.human_review";

    public async Task<FinanceAutonomyApprovalCoordinatorBatchResult> ProcessBatchAsync(
        DateTime utcNow, int batchSize, CancellationToken cancellationToken)
    {
        var now = Utc(utcNow);
        var bounded = Math.Clamp(batchSize, 1, 100);
        var approvalIds = await db.FinanceAutonomyRunSteps.IgnoreQueryFilters().AsNoTracking()
            .Where(step => step.Status == FinanceAutonomyStepStatus.AwaitingApproval && step.ApprovalRequestId != null)
            .OrderBy(step => step.UpdatedUtc)
            .Select(step => new { step.CompanyId, ApprovalId = step.ApprovalRequestId!.Value })
            .Take(bounded)
            .ToListAsync(cancellationToken);
        var pending = 0;
        var continued = 0;
        var blocked = 0;
        var escalated = 0;
        foreach (var item in approvalIds)
        {
            var outcome = await ProcessApprovalCoreAsync(item.CompanyId, item.ApprovalId, now, cancellationToken);
            pending += outcome.Pending ? 1 : 0;
            continued += outcome.Continued ? 1 : 0;
            blocked += outcome.Blocked ? 1 : 0;
            escalated += outcome.Escalated ? 1 : 0;
        }

        var operationalRuns = await db.FinanceAutonomyRuns.IgnoreQueryFilters()
            .Include(run => run.Steps)
            .Include(run => run.GrantVersion)
            .Where(run => run.Status == FinanceAutonomyRunStatus.Reconciling ||
                          run.Status == FinanceAutonomyRunStatus.DeadLettered ||
                          run.Status == FinanceAutonomyRunStatus.Blocked)
            .OrderBy(run => run.UpdatedUtc)
            .Take(bounded)
            .ToListAsync(cancellationToken);
        foreach (var run in operationalRuns)
        {
            var kind = run.Status switch
            {
                FinanceAutonomyRunStatus.Reconciling => "reconciliation_required",
                FinanceAutonomyRunStatus.DeadLettered => "repeated_failure",
                _ when run.ReasonCode?.Contains("evidence", StringComparison.OrdinalIgnoreCase) == true => "evidence_gap",
                _ => "blocked_run"
            };
            if (await EnsureEscalationAsync(run, run.Steps.FirstOrDefault(step =>
                    step.Status is FinanceAutonomyStepStatus.Reconciling or FinanceAutonomyStepStatus.DeadLettered or FinanceAutonomyStepStatus.Blocked),
                    null, kind, run.SafeSummary ?? "Finance autonomy requires human review.", now, cancellationToken))
                escalated++;
        }

        var openCircuits = await db.FinanceAutonomyCircuitBreakers.IgnoreQueryFilters().AsNoTracking()
            .Where(circuit => circuit.Status == FinanceAutonomyCircuitStatus.Open)
            .OrderBy(circuit => circuit.UpdatedUtc)
            .Take(bounded)
            .ToListAsync(cancellationToken);
        foreach (var circuit in openCircuits)
        {
            var run = await db.FinanceAutonomyRuns.IgnoreQueryFilters()
                .Include(item => item.Steps).Include(item => item.GrantVersion)
                .Where(item => item.CompanyId == circuit.CompanyId && item.AgentId == circuit.AgentId &&
                               item.CapabilityId == circuit.CapabilityId)
                .OrderByDescending(item => item.UpdatedUtc).FirstOrDefaultAsync(cancellationToken);
            if (run is not null && await EnsureEscalationAsync(run, null, null, "circuit_breaker",
                    circuit.SafeSummary ?? "The Finance autonomy circuit is open and requires operator review.", now, cancellationToken))
                escalated++;
        }
        return new(approvalIds.Count + operationalRuns.Count + openCircuits.Count,
            pending, continued, blocked, escalated);
    }

    public async Task ProcessApprovalAsync(Guid companyId, Guid approvalRequestId, CancellationToken cancellationToken) =>
        _ = await ProcessApprovalCoreAsync(companyId, approvalRequestId, clock.GetUtcNow().UtcDateTime, cancellationToken);

    private async Task<ProcessingOutcome> ProcessApprovalCoreAsync(
        Guid companyId, Guid approvalRequestId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var approval = await db.ApprovalRequests.IgnoreQueryFilters()
            .Include(item => item.Steps)
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.Id == approvalRequestId, cancellationToken);
        if (approval is null) return default;
        var step = await db.FinanceAutonomyRunSteps.IgnoreQueryFilters().AsNoTracking()
            .Include(item => item.Run).ThenInclude(run => run.GrantVersion)
            .SingleOrDefaultAsync(item => item.CompanyId == companyId && item.ApprovalRequestId == approvalRequestId, cancellationToken);
        if (step is null) return default;

        if (approval.Status == ApprovalRequestStatus.Pending && IsExpired(approval, utcNow))
        {
            approval.MarkExpired("The exact-action approval expired. Review current evidence and create a new request if work remains.");
            var attempt = await db.ToolExecutionAttempts.IgnoreQueryFilters()
                .SingleAsync(item => item.CompanyId == companyId && item.Id == step.ToolExecutionAttemptId, cancellationToken);
            attempt.MarkDenied(attempt.PolicyDecision, new Dictionary<string, JsonNode?>
            {
                ["status"] = ToolExecutionStatus.Denied.ToStorageValue(),
                ["reasonCode"] = "approval_expired",
                ["approvalRequestId"] = approval.Id,
                ["notificationIsApproval"] = false
            }, denialReason: "approval_expired");
            await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.System, null,
                AuditEventActions.ApprovalCompleted, AuditTargetTypes.ApprovalRequest,
                approval.Id.ToString("N"), AuditEventOutcomes.Blocked,
                approval.DecisionSummary!, DataSources: ["finance_autonomy", "approvals", "clock"],
                CorrelationId: step.Run.CorrelationId), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (approval.Status == ApprovalRequestStatus.Pending)
        {
            var escalated = await EnsureEscalationAsync(step.Run, step, approval, "approval_pending",
                "Independent human approval is required. No mutation or dependent step will run while this request is pending.",
                utcNow, cancellationToken);
            return new(true, false, false, escalated);
        }

        var toolAttempt = await db.ToolExecutionAttempts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(item => item.CompanyId == companyId && item.Id == step.ToolExecutionAttemptId, cancellationToken);
        var changed = await runs.ResolveApprovalAsync(companyId, new ResolveFinanceAutonomyApprovalCommand(
            approval.Id, approval.Status.ToStorageValue(), toolAttempt.Status.ToStorageValue(),
            toolAttempt.ResultPayload, toolAttempt.DenialReason, approval.DecisionSummary), cancellationToken);
        var success = approval.Status == ApprovalRequestStatus.Approved &&
                      toolAttempt.Status == ToolExecutionStatus.Executed;
        var needsHuman = !success;
        var escalatedAfterDecision = needsHuman && await EnsureEscalationAsync(step.Run, step, approval,
            approval.Status == ApprovalRequestStatus.ChangesRequested ? "changes_requested" : "approval_decided",
            approval.DecisionSummary ?? "The approval outcome requires human review before further work.",
            utcNow, cancellationToken);
        return new(false, changed && success, changed && !success, escalatedAfterDecision);
    }

    private async Task<bool> EnsureEscalationAsync(
        FinanceAutonomyRun run, FinanceAutonomyRunStep? step, ApprovalRequest? approval,
        string kind, string summary, DateTime utcNow, CancellationToken cancellationToken)
    {
        var triggerEventId = $"finance-autonomy:{run.Id:N}:{step?.Id.ToString("N") ?? "run"}:{kind}";
        var exists = await db.WorkTasks.IgnoreQueryFilters().AsNoTracking().AnyAsync(task =>
            task.CompanyId == run.CompanyId && task.Type == EscalationTaskType &&
            task.TriggerEventId == triggerEventId, cancellationToken);
        if (exists) return false;

        var route = NormalizeRoute(run.GrantVersion.EscalationRoute);
        var nextAction = kind switch
        {
            "approval_pending" => "Review the exact action in the approval inbox. This task and its notifications do not approve it.",
            "changes_requested" => "Narrow the retained pending steps into a new validated revision; any expansion requires a new plan and grant review.",
            "reconciliation_required" => "Reconcile the stable provider request before retrying or cancelling remaining work.",
            "circuit_breaker" => "Investigate the repeated failures and reset the circuit only after the cause is understood.",
            "evidence_gap" => "Refresh authoritative evidence, then create a new validated plan if work remains.",
            "repeated_failure" => "Inspect attempt history and choose reconcile, narrow, cancel, or a new reviewed plan.",
            _ => "Review the run history and choose narrow, cancel, reconcile, or a new reviewed plan."
        };
        var task = new WorkTask(Guid.NewGuid(), run.CompanyId, EscalationTaskType,
            kind == "approval_pending" ? "Finance autonomy approval required" : "Finance autonomy human review required",
            summary, kind is "circuit_breaker" or "repeated_failure" ? WorkTaskPriority.Critical : WorkTaskPriority.High,
            null, run.OriginatingTaskId, AuditActorTypes.System, null,
            new Dictionary<string, JsonNode?>
            {
                ["runId"] = run.Id,
                ["stepId"] = step?.Id,
                ["approvalRequestId"] = approval?.Id,
                ["reasonCode"] = run.ReasonCode,
                ["escalationKind"] = kind,
                ["requiredHumanRole"] = route,
                ["nextAction"] = nextAction,
                ["notificationIsApproval"] = false
            }, run.WorkflowInstanceId, rationaleSummary: summary, correlationId: run.CorrelationId,
            sourceType: WorkTaskSourceTypes.Agent, originatingAgentId: run.AgentId,
            triggerSource: "finance_autonomy", creationReason: nextAction, triggerEventId: triggerEventId);
        task.SetDueDate(kind == "approval_pending" ? utcNow.AddHours(4) : utcNow.AddHours(1));
        db.WorkTasks.Add(task);

        var recipients = await ResolveRecipientsAsync(run.CompanyId, route, cancellationToken);
        foreach (var userId in recipients)
        {
            var dedupe = $"finance-autonomy:{run.Id:N}:{step?.Id.ToString("N") ?? "run"}:{kind}:{userId:N}";
            db.CompanyNotifications.Add(new CompanyNotification(Guid.NewGuid(), run.CompanyId, userId,
                kind == "approval_pending" ? CompanyNotificationType.ApprovalRequested : CompanyNotificationType.Escalation,
                kind is "circuit_breaker" or "repeated_failure" ? CompanyNotificationPriority.Critical : CompanyNotificationPriority.High,
                task.Title, $"{summary} Next: {nextAction}", "finance_autonomy_run", run.Id,
                $"/finance/autonomy/runs/{run.Id}", JsonSerializer.Serialize(new
                {
                    runId = run.Id, stepId = step?.Id, approvalRequestId = approval?.Id,
                    escalationKind = kind, requiredHumanRole = route, notificationIsApproval = false
                }), dedupe));
        }
        await audit.WriteAsync(new AuditEventWriteRequest(run.CompanyId, AuditActorTypes.System, null,
            AuditEventActions.FinanceAutonomyRunTransitioned, AuditTargetTypes.FinanceAutonomyRun,
            run.Id.ToString("N"), AuditEventOutcomes.Pending, summary,
            DataSources: ["finance_autonomy", "work_tasks", "notifications"],
            Metadata: new Dictionary<string, string?>
            {
                ["stepId"] = step?.Id.ToString("N"), ["approvalRequestId"] = approval?.Id.ToString("N"),
                ["escalationKind"] = kind, ["requiredHumanRole"] = route,
                ["notificationIsApproval"] = "false"
            }, CorrelationId: run.CorrelationId), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<Guid[]> ResolveRecipientsAsync(
        Guid companyId, string route, CancellationToken cancellationToken)
    {
        var preferred = CompanyMembershipRoles.TryParse(route, out var role) ? role : CompanyMembershipRole.FinanceApprover;
        var recipients = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(member => member.CompanyId == companyId && member.Status == CompanyMembershipStatus.Active &&
                             member.UserId != null && member.Role == preferred)
            .Select(member => member.UserId!.Value).Distinct().ToArrayAsync(cancellationToken);
        if (recipients.Length > 0) return recipients;
        return await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(member => member.CompanyId == companyId && member.Status == CompanyMembershipStatus.Active &&
                             member.UserId != null && (member.Role == CompanyMembershipRole.Owner || member.Role == CompanyMembershipRole.Admin))
            .Select(member => member.UserId!.Value).Distinct().ToArrayAsync(cancellationToken);
    }

    private static string NormalizeRoute(string? route) =>
        CompanyMembershipRoles.TryParse(route, out var role) ? role.ToStorageValue() : CompanyMembershipRole.FinanceApprover.ToStorageValue();

    private static bool IsExpired(ApprovalRequest approval, DateTime utcNow)
    {
        if (!approval.ThresholdContext.TryGetValue("approvalBinding", out var node) || node is not JsonObject binding)
            return false;
        var expires = FinanceApprovalContinuationBinding.ReadBindingUtc(binding, "expiresUtc");
        return expires.HasValue && expires.Value <= utcNow;
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private readonly record struct ProcessingOutcome(bool Pending, bool Continued, bool Blocked, bool Escalated);
}
