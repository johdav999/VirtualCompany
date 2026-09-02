using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class TodayAgentActivityQueryService(VirtualCompanyDbContext db) : ITodayAgentActivityQueryService
{
    private const int SourceLimit = 40;

    public async Task<IReadOnlyList<TodayWorkspaceAgentUpdateDto>> GetAsync(
        TodayWorkspaceLensResolution resolution,
        CancellationToken cancellationToken)
    {
        var directlyAssigned = resolution.AvailableLenses
            .Where(x => x.WorkingAgentId.HasValue && x.IsPrimary)
            .Select(x => x.WorkingAgentId!.Value)
            .ToHashSet();
        var oversightAgents = resolution.AvailableLenses
            .Where(x => x.WorkingAgentId.HasValue && x.IsExecutiveOversight)
            .Select(x => x.WorkingAgentId!.Value)
            .ToHashSet();

        var tasks = await db.WorkTasks.AsNoTracking()
            .Where(x => x.CompanyId == resolution.CompanyId && x.AssignedAgentId.HasValue &&
                (directlyAssigned.Contains(x.AssignedAgentId.Value) ||
                 (oversightAgents.Contains(x.AssignedAgentId.Value) && x.Priority == WorkTaskPriority.Critical)))
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(SourceLimit)
            .ToListAsync(cancellationToken);

        var visibleAgentIds = directlyAssigned.Concat(oversightAgents)
            .Concat(tasks.Where(x => x.AssignedAgentId.HasValue).Select(x => x.AssignedAgentId!.Value))
            .ToHashSet();
        if (visibleAgentIds.Count == 0) return [];

        var agents = await db.Agents.AsNoTracking()
            .Where(x => x.CompanyId == resolution.CompanyId && visibleAgentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var runs = await db.AgentOrchestrationRuns.AsNoTracking()
            .Where(x => x.CompanyId == resolution.CompanyId &&
                (directlyAssigned.Contains(x.AgentId) ||
                 (oversightAgents.Contains(x.AgentId) &&
                  (x.Status == "needs_review" || x.Status == "blocked" || x.Status == "failed"))))
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(SourceLimit)
            .ToListAsync(cancellationToken);
        var approvals = await db.ApprovalRequests.AsNoTracking()
            .Where(x => x.CompanyId == resolution.CompanyId &&
                (directlyAssigned.Contains(x.AgentId) || oversightAgents.Contains(x.AgentId)))
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(SourceLimit)
            .ToListAsync(cancellationToken);
        var audit = await db.AuditEvents.AsNoTracking()
            .Where(x => x.CompanyId == resolution.CompanyId && x.RelatedAgentId.HasValue &&
                        (directlyAssigned.Contains(x.RelatedAgentId.Value) ||
                         (oversightAgents.Contains(x.RelatedAgentId.Value) &&
                          (x.Outcome == "failed" || x.Action.Contains("recommend")))))
            .OrderByDescending(x => x.OccurredUtc)
            .Take(SourceLimit)
            .ToListAsync(cancellationToken);

        var candidates = new List<ActivityCandidate>();
        candidates.AddRange(tasks.Select(task => FromTask(task, Agent(task.AssignedAgentId, agents), resolution)));
        candidates.AddRange(runs.Select(run => FromRun(run, Agent(run.AgentId, agents), resolution)));
        candidates.AddRange(approvals.Select(approval => FromApproval(approval, Agent(approval.AgentId, agents), resolution)));
        candidates.AddRange(audit.Select(item => FromAudit(item, Agent(item.RelatedAgentId, agents), resolution)));

        return Deduplicate(candidates)
            .OrderByDescending(x => StateRank(x.Update.AgentState))
            .ThenByDescending(x => x.Update.UpdatedUtc ?? x.Update.ObservedAtUtc)
            .ThenBy(x => x.Update.Key, StringComparer.Ordinal)
            .Take(8)
            .Select(x => x.Update)
            .ToList();
    }

    internal static IReadOnlyList<ActivityCandidate> Deduplicate(IEnumerable<ActivityCandidate> candidates) =>
        candidates
            .Where(x => !string.IsNullOrWhiteSpace(x.DeduplicationKey))
            .GroupBy(x => x.DeduplicationKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => StateRank(x.Update.AgentState))
                .ThenByDescending(x => x.Update.UpdatedUtc ?? x.Update.ObservedAtUtc)
                .First())
            .ToList();

    internal static string VisibilityReason(Guid? agentId, TodayWorkspaceLensResolution resolution, bool directlyInvolved = false)
    {
        if (directlyInvolved) return "Shown because you are directly involved in this work.";
        var access = resolution.AvailableLenses.FirstOrDefault(x => x.WorkingAgentId == agentId);
        if (access?.IsPrimary == true) return $"Shown because you own the {access.Label} responsibility.";
        if (access?.IsExecutiveOversight == true) return $"Shown because you have executive oversight of {access.Label}.";
        return "Shown because this is a material executive-oversight exception.";
    }

    private static ActivityCandidate FromTask(WorkTask task, Agent? agent, TodayWorkspaceLensResolution resolution)
    {
        var state = TodayAgentStateMapper.FromTask(task.Status);
        var link = $"/work?companyId={resolution.CompanyId:D}&tab=tasks&taskId={task.Id:D}&source=dashboard";
        return new ActivityCandidate(UnderlyingKey(task.Id, task.WorkflowInstanceId, null, task.CorrelationId), new(
            $"task:{task.Id:N}", task.Title, Safe(task.Description, "Agent work is available for review."), agent?.DisplayName,
            task.UpdatedUtc, "work_task", link, agent?.RoleName, state, agent?.AvatarUrl,
            Safe(task.RationaleSummary, "This update reflects the current authoritative task state."),
            VisibilityReason(task.AssignedAgentId, resolution, task.CreatedByActorId == resolution.UserId),
            task.Id, task.WorkflowInstanceId, null, task.UpdatedUtc));
    }

    private static ActivityCandidate FromRun(AgentOrchestrationRun run, Agent? agent, TodayWorkspaceLensResolution resolution)
    {
        var link = run.TaskId.HasValue
            ? $"/work?companyId={resolution.CompanyId:D}&tab=tasks&taskId={run.TaskId:D}&source=dashboard"
            : $"/agents/{run.AgentId:D}?companyId={resolution.CompanyId:D}&source=dashboard";
        return new ActivityCandidate(UnderlyingKey(run.TaskId, null, null, run.CorrelationId, run.Id), new(
            $"agent-run:{run.Id:N}", Humanize(run.CapabilityId), Safe(run.Summary, "The agent run state changed. Open the related record for recovery details."),
            agent?.DisplayName, run.CompletedUtc ?? run.UpdatedUtc, "agent_run", link, agent?.RoleName,
            TodayAgentStateMapper.FromAgentRun(run.Status), agent?.AvatarUrl,
            Safe(run.Summary, "The update is derived from the persisted agent execution result."),
            VisibilityReason(run.AgentId, resolution, run.ActorUserId == resolution.UserId),
            run.TaskId, null, null, run.UpdatedUtc));
    }

    private static ActivityCandidate FromApproval(ApprovalRequest approval, Agent? agent, TodayWorkspaceLensResolution resolution)
    {
        var taskId = string.Equals(approval.TargetEntityType, "work_task", StringComparison.OrdinalIgnoreCase)
            ? approval.TargetEntityId : (Guid?)null;
        var link = $"/approvals?companyId={resolution.CompanyId:D}&approvalId={approval.Id:D}&source=dashboard";
        var needsCurrentUser = approval.RequiredUserId == resolution.UserId;
        return new ActivityCandidate(UnderlyingKey(taskId, null, approval.Id, null), new(
            $"approval:{approval.Id:N}", Humanize(approval.ApprovalType),
            Safe(approval.DecisionSummary, approval.Status == ApprovalRequestStatus.Pending
                ? "Agent work is waiting for an authoritative approval decision."
                : "The authoritative approval state has changed."),
            agent?.DisplayName, approval.UpdatedUtc, "approval", link, agent?.RoleName,
            TodayAgentStateMapper.FromApproval(approval.Status), agent?.AvatarUrl,
            "The related action cannot bypass the existing approval workflow.",
            needsCurrentUser ? "Shown because this decision needs your approval." : VisibilityReason(approval.AgentId, resolution),
            taskId, null, approval.Id, approval.UpdatedUtc));
    }

    private static ActivityCandidate FromAudit(AuditEvent item, Agent? agent, TodayWorkspaceLensResolution resolution)
    {
        var link = item.RelatedApprovalRequestId.HasValue
            ? $"/approvals?companyId={resolution.CompanyId:D}&approvalId={item.RelatedApprovalRequestId:D}&source=dashboard"
            : item.RelatedTaskId.HasValue
                ? $"/work?companyId={resolution.CompanyId:D}&tab=tasks&taskId={item.RelatedTaskId:D}&source=dashboard"
                : $"/history?companyId={resolution.CompanyId:D}&source=dashboard";
        var state = item.Outcome.Equals("failed", StringComparison.OrdinalIgnoreCase)
            ? TodayAgentStates.Blocked : item.Action.Contains("recommend", StringComparison.OrdinalIgnoreCase)
                ? TodayAgentStates.Recommended : TodayAgentStates.Completed;
        return new ActivityCandidate(UnderlyingKey(item.RelatedTaskId, item.RelatedWorkflowInstanceId,
            item.RelatedApprovalRequestId, item.CorrelationId, item.Id), new(
            $"audit:{item.Id:N}", Humanize(item.Action), Safe(item.RationaleSummary, "Recorded agent activity is available in history."),
            agent?.DisplayName ?? item.AgentName, item.OccurredUtc, "audit", link,
            agent?.RoleName ?? item.AgentRole, state, agent?.AvatarUrl,
            Safe(item.RationaleSummary, "This update is backed by persisted audit evidence."),
            VisibilityReason(item.RelatedAgentId, resolution), item.RelatedTaskId,
            item.RelatedWorkflowInstanceId, item.RelatedApprovalRequestId, item.OccurredUtc));
    }

    private static Agent? Agent(Guid? id, IReadOnlyDictionary<Guid, Agent> agents) =>
        id.HasValue && agents.TryGetValue(id.Value, out var agent) ? agent : null;

    private static string UnderlyingKey(Guid? taskId, Guid? workflowId, Guid? approvalId, string? correlationId, Guid? fallback = null) =>
        taskId.HasValue ? $"task:{taskId:N}" : workflowId.HasValue ? $"workflow:{workflowId:N}" :
        approvalId.HasValue ? $"approval:{approvalId:N}" : !string.IsNullOrWhiteSpace(correlationId) ? $"correlation:{correlationId}" :
        $"record:{fallback:N}";

    private static string Safe(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static string Humanize(string value) => string.Join(' ', value.Replace('.', '_').Replace('-', '_')
        .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select((part, index) => index == 0 ? char.ToUpperInvariant(part[0]) + part[1..] : part));
    private static int StateRank(string? state) => state switch
    {
        TodayAgentStates.NeedsUser => 600,
        TodayAgentStates.Blocked => 500,
        TodayAgentStates.Recommended => 400,
        TodayAgentStates.Working => 300,
        TodayAgentStates.Monitoring => 200,
        TodayAgentStates.Completed => 100,
        _ => 0
    };

    internal sealed record ActivityCandidate(string DeduplicationKey, TodayWorkspaceAgentUpdateDto Update);
}
