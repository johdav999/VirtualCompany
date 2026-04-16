using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Insights;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyActionInsightService : IActionInsightService
{
    private const int MaxCandidatesPerSource = 50;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor _companyContextAccessor;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IInsightScoringService _scoringService;
    private readonly IActionDeepLinkResolver _deepLinkResolver;
    private readonly TimeProvider _timeProvider;

    public CompanyActionInsightService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor companyContextAccessor,
        ICurrentUserAccessor currentUserAccessor,
        IInsightScoringService scoringService,
        IActionDeepLinkResolver deepLinkResolver,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _currentUserAccessor = currentUserAccessor;
        _scoringService = scoringService;
        _deepLinkResolver = deepLinkResolver;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ActionQueueItemDto>> GetActionQueueAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var candidates = await GetCandidatesAsync(companyId, nowUtc, cancellationToken);

        var keys = candidates.Select(candidate => candidate.InsightKey).Distinct(StringComparer.Ordinal).ToArray();
        var acknowledgments = await _dbContext.InsightAcknowledgments
            .Where(ack => ack.CompanyId == companyId && ack.UserId == userId && keys.Contains(ack.InsightKey))
            .ToDictionaryAsync(ack => ack.InsightKey, ack => ack.AcknowledgedUtc, StringComparer.Ordinal, cancellationToken);

        return _scoringService
            .Prioritize(candidates, nowUtc)
            .Select(scored => Map(scored, acknowledgments))
            .ToList();
    }

    public async Task<ActionQueueItemDto?> AcknowledgeAsync(Guid companyId, string insightKey, CancellationToken cancellationToken)
    {
        var userId = ResolveUserId();
        var normalizedKey = NormalizeInsightKey(insightKey);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var candidates = await GetCandidatesAsync(companyId, nowUtc, cancellationToken);
        if (!candidates.Any(candidate => string.Equals(candidate.InsightKey, normalizedKey, StringComparison.Ordinal)))
        {
            return null;
        }

        var existing = await _dbContext.InsightAcknowledgments.SingleOrDefaultAsync(
            ack => ack.CompanyId == companyId && ack.UserId == userId && ack.InsightKey == normalizedKey,
            cancellationToken);

        if (existing is null)
        {
            _dbContext.InsightAcknowledgments.Add(new InsightAcknowledgment(Guid.NewGuid(), companyId, userId, normalizedKey, nowUtc));
        }
        else
        {
            existing.MarkAcknowledged(nowUtc);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var queue = await GetActionQueueAsync(companyId, cancellationToken);
        return queue.FirstOrDefault(item => string.Equals(item.InsightKey, normalizedKey, StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<InsightCandidate>> GetCandidatesAsync(Guid companyId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var candidates = new List<InsightCandidate>();
        candidates.AddRange(await GetPendingApprovalsAsync(companyId, nowUtc, cancellationToken));
        candidates.AddRange(await GetTaskInsightsAsync(companyId, nowUtc, cancellationToken));
        candidates.AddRange(await GetBlockedWorkflowsAsync(companyId, nowUtc, cancellationToken));
        candidates.AddRange(await GetAlertInsightsAsync(companyId, AlertType.Risk, nowUtc, cancellationToken));
        candidates.AddRange(await GetAlertInsightsAsync(companyId, AlertType.Opportunity, nowUtc, cancellationToken));

        return candidates;
    }

    private Guid ResolveUserId()
    {
        var userId = _companyContextAccessor.UserId ?? _currentUserAccessor.UserId;
        return userId ?? throw new UnauthorizedAccessException("A resolved user is required for action insights.");
    }

    private async Task<IReadOnlyList<InsightCandidate>> GetPendingApprovalsAsync(Guid companyId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var approvals = await _dbContext.ApprovalRequests
            .Where(approval => approval.CompanyId == companyId && approval.Status == ApprovalRequestStatus.Pending)
            .OrderBy(approval => approval.CreatedUtc)
            .Take(MaxCandidatesPerSource)
            .Select(approval => new
            {
                approval.Id,
                approval.CompanyId,
                approval.TargetEntityType,
                approval.TargetEntityId,
                approval.ApprovalType,
                approval.RequiredRole,
                approval.RequiredUserId,
                approval.CreatedUtc,
                approval.DecisionSummary
            })
            .ToListAsync(cancellationToken);

        return approvals
            .Select(approval =>
            {
                var dueUtc = approval.CreatedUtc.AddHours(8);
                return new InsightCandidate(
                    InsightKey.For(companyId, ActionInsightType.Approval, approval.Id),
                    approval.CompanyId,
                    ActionInsightType.Approval,
                    "approval",
                    approval.Id,
                    ActionInsightTargetType.Approval,
                    approval.Id,
                    "Approval required",
                    string.IsNullOrWhiteSpace(approval.DecisionSummary)
                        ? $"Pending {approval.ApprovalType} approval for {approval.TargetEntityType}."
                        : approval.DecisionSummary,
                    ResolveApprovalOwner(approval.RequiredRole, approval.RequiredUserId),
                    dueUtc,
                    ResolveSlaState(dueUtc, nowUtc, TimeSpan.FromHours(2)),
                    approval.CreatedUtc);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<InsightCandidate>> GetTaskInsightsAsync(Guid companyId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var dueSoonUtc = nowUtc.AddHours(24);
        var tasks = await _dbContext.WorkTasks
            .Where(task =>
                task.CompanyId == companyId &&
                task.Status != WorkTaskStatus.Completed &&
                task.Status != WorkTaskStatus.Failed &&
                (task.Status == WorkTaskStatus.Blocked ||
                 task.Status == WorkTaskStatus.AwaitingApproval ||
                 (task.DueUtc.HasValue && task.DueUtc <= dueSoonUtc)))
            .OrderBy(task => task.DueUtc ?? DateTime.MaxValue)
            .ThenBy(task => task.CreatedUtc)
            .Take(MaxCandidatesPerSource)
            .Select(task => new
            {
                task.Id,
                task.CompanyId,
                task.Title,
                task.Status,
                task.Priority,
                task.DueUtc,
                task.CreatedUtc,
                task.SourceLifecycleVersion,
                Owner = task.AssignedAgent == null ? null : task.AssignedAgent.DisplayName
            })
            .ToListAsync(cancellationToken);

        return tasks
            .Select(task =>
            {
                var dueUtc = task.DueUtc ?? task.CreatedUtc.AddHours(24);
                return new InsightCandidate(
                    InsightKey.For(companyId, ActionInsightType.Task, task.Id, task.SourceLifecycleVersion),
                    task.CompanyId,
                    ActionInsightType.Task,
                    "task",
                    task.Id,
                    ActionInsightTargetType.Task,
                    task.Id,
                    task.Title,
                    ResolveTaskReason(task.Status, task.DueUtc, nowUtc),
                    string.IsNullOrWhiteSpace(task.Owner) ? "Operations" : task.Owner,
                    task.DueUtc,
                    ResolveSlaState(dueUtc, nowUtc, TimeSpan.FromHours(4)),
                    task.CreatedUtc,
                    TaskPriorityScore(task.Priority));
            })
            .ToList();
    }

    private async Task<IReadOnlyList<InsightCandidate>> GetBlockedWorkflowsAsync(Guid companyId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var workflows = await _dbContext.WorkflowInstances
            .Where(instance => instance.CompanyId == companyId && instance.State == WorkflowInstanceStatus.Blocked)
            .OrderBy(instance => instance.StartedUtc)
            .Take(MaxCandidatesPerSource)
            .Select(instance => new
            {
                instance.Id,
                instance.CompanyId,
                instance.StartedUtc,
                instance.CurrentStep,
                DefinitionName = instance.Definition.Name,
                Department = instance.Definition.Department
            })
            .ToListAsync(cancellationToken);

        return workflows
            .Select(workflow =>
            {
                var dueUtc = workflow.StartedUtc.AddHours(4);
                return new InsightCandidate(
                    InsightKey.For(companyId, ActionInsightType.BlockedWorkflow, workflow.Id),
                    workflow.CompanyId,
                    ActionInsightType.BlockedWorkflow,
                    "workflow",
                    workflow.Id,
                    ActionInsightTargetType.Workflow,
                    workflow.Id,
                    workflow.DefinitionName,
                    string.IsNullOrWhiteSpace(workflow.CurrentStep)
                        ? "Workflow is blocked and needs intervention."
                        : $"Workflow is blocked at step {workflow.CurrentStep}.",
                    string.IsNullOrWhiteSpace(workflow.Department) ? "Operations" : workflow.Department,
                    dueUtc,
                    ResolveSlaState(dueUtc, nowUtc, TimeSpan.FromHours(1)),
                    workflow.StartedUtc);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<InsightCandidate>> GetAlertInsightsAsync(Guid companyId, AlertType type, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var alerts = await _dbContext.Alerts
            .Where(alert =>
                alert.CompanyId == companyId &&
                alert.Type == type &&
                (alert.Status == AlertStatus.Open || alert.Status == AlertStatus.Acknowledged))
            .OrderByDescending(alert => alert.Severity)
            .ThenBy(alert => alert.CreatedUtc)
            .Take(MaxCandidatesPerSource)
            .Select(alert => new
            {
                alert.Id,
                alert.CompanyId,
                alert.Title,
                alert.Summary,
                alert.Type,
                alert.Severity,
                alert.OccurrenceCount,
                alert.SourceLifecycleVersion,
                alert.CreatedUtc,
                alert.LastDetectedUtc,
                Owner = alert.SourceAgent == null ? null : alert.SourceAgent.DisplayName
            })
            .ToListAsync(cancellationToken);

        return alerts
            .Select(alert =>
            {
                var insightType = alert.Type == AlertType.Opportunity
                    ? ActionInsightType.Opportunity
                    : ActionInsightType.Risk;
                var dueUtc = CalculateAlertDueUtc(alert.CreatedUtc, alert.Severity, insightType);
                var targetType = insightType == ActionInsightType.Opportunity
                    ? ActionInsightTargetType.Alert
                    : ActionInsightTargetType.Alert;

                return new InsightCandidate(
                    InsightKey.For(companyId, insightType, alert.Id, alert.SourceLifecycleVersion),
                    alert.CompanyId,
                    insightType,
                    "alert",
                    alert.Id,
                    targetType,
                    alert.Id,
                    alert.Title,
                    alert.Summary,
                    string.IsNullOrWhiteSpace(alert.Owner) ? "Operations" : alert.Owner,
                    dueUtc,
                    ResolveSlaState(dueUtc, nowUtc, TimeSpan.FromHours(6)),
                    alert.LastDetectedUtc ?? alert.CreatedUtc,
                    SeverityScore(alert.Severity),
                    alert.OccurrenceCount);
            })
            .ToList();
    }

    private ActionQueueItemDto Map(ScoredInsightCandidate scored, IReadOnlyDictionary<string, DateTime> acknowledgments)
    {
        var candidate = scored.Candidate;
        var acknowledged = acknowledgments.TryGetValue(candidate.InsightKey, out var acknowledgedAt);
        var deepLink = _deepLinkResolver.Resolve(candidate.CompanyId, new ActionDeepLinkTarget(candidate.TargetType, candidate.TargetId));

        return new ActionQueueItemDto(
            candidate.InsightKey,
            candidate.CompanyId,
            candidate.Type.ToStorageValue(),
            candidate.SourceEntityType,
            candidate.SourceEntityId,
            candidate.TargetType.ToStorageValue(),
            candidate.TargetId,
            candidate.Title,
            candidate.Reason,
            candidate.Owner,
            candidate.DueUtc,
            candidate.SlaState.ToStorageValue(),
            scored.PriorityScore,
            scored.Priority.ToStorageValue(),
            deepLink.Href,
            acknowledged,
            acknowledged ? acknowledgedAt : null,
            candidate.InsightKey);
    }

    private static string ResolveApprovalOwner(string? requiredRole, Guid? requiredUserId)
    {
        if (requiredUserId.HasValue)
        {
            return $"User {requiredUserId.Value:N}";
        }

        return string.IsNullOrWhiteSpace(requiredRole) ? "Approver" : requiredRole.Trim();
    }

    private static string ResolveTaskReason(WorkTaskStatus status, DateTime? dueUtc, DateTime nowUtc) =>
        status switch
        {
            WorkTaskStatus.Blocked => "Task is blocked and needs intervention.",
            WorkTaskStatus.AwaitingApproval => "Task is waiting for an approval decision.",
            _ when dueUtc.HasValue && dueUtc.Value <= nowUtc => "Task is past due.",
            _ when dueUtc.HasValue => "Task is due soon.",
            _ => "Task needs follow-up."
        };

    private static int TaskPriorityScore(WorkTaskPriority priority) =>
        priority switch
        {
            WorkTaskPriority.Critical => 20,
            WorkTaskPriority.High => 15,
            WorkTaskPriority.Normal => 8,
            _ => 2
        };

    private static DateTime CalculateAlertDueUtc(DateTime createdUtc, AlertSeverity severity, ActionInsightType insightType)
    {
        if (insightType == ActionInsightType.Opportunity)
        {
            return createdUtc.AddHours(48);
        }

        return severity switch
        {
            AlertSeverity.Critical => createdUtc.AddHours(4),
            AlertSeverity.High => createdUtc.AddHours(8),
            AlertSeverity.Medium => createdUtc.AddHours(24),
            _ => createdUtc.AddHours(72)
        };
    }

    private static int SeverityScore(AlertSeverity severity) =>
        severity switch
        {
            AlertSeverity.Critical => 20,
            AlertSeverity.High => 15,
            AlertSeverity.Medium => 8,
            _ => 2
        };

    private static ActionInsightSlaState ResolveSlaState(DateTime? dueUtc, DateTime nowUtc, TimeSpan dueSoonWindow)
    {
        if (!dueUtc.HasValue)
        {
            return ActionInsightSlaState.None;
        }

        if (dueUtc.Value <= nowUtc)
        {
            return ActionInsightSlaState.Breached;
        }

        return dueUtc.Value - nowUtc <= dueSoonWindow
            ? ActionInsightSlaState.DueSoon
            : ActionInsightSlaState.OnTrack;
    }

    private static string NormalizeInsightKey(string insightKey) =>
        string.IsNullOrWhiteSpace(insightKey)
            ? throw new ArgumentException("Insight key is required.", nameof(insightKey))
            : insightKey.Trim().ToLowerInvariant();
}