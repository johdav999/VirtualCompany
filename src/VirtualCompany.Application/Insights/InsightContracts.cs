using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Insights;

public sealed record ActionQueueItemDto(
    string InsightKey,
    Guid CompanyId,
    string Type,
    string SourceEntityType,
    Guid SourceEntityId,
    string TargetType,
    Guid TargetId,
    string Title,
    string Reason,
    string Owner,
    DateTime? DueUtc,
    string SlaState,
    int PriorityScore,
    int ImpactScore,
    string Priority,
    string DeepLink,
    bool IsAcknowledged,
    DateTime? AcknowledgedAt,
    string StableSortKey);

public sealed record AcknowledgeInsightCommand(string InsightKey);

public sealed record ActionQueuePageDto(
    IReadOnlyList<ActionQueueItemDto> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);

public sealed record InsightCandidate(
    string InsightKey,
    Guid CompanyId,
    ActionInsightType Type,
    string SourceEntityType,
    Guid SourceEntityId,
    ActionInsightTargetType TargetType,
    Guid TargetId,
    string Title,
    string Reason,
    string Owner,
    DateTime? DueUtc,
    ActionInsightSlaState SlaState,
    DateTime CreatedUtc,
    int SeverityWeight = 0,
    int OccurrenceCount = 1);

public sealed record ScoredInsightCandidate(
    InsightCandidate Candidate,
    int PriorityScore,
    int ImpactScore,
    ActionInsightPriority Priority);

public sealed record ActionDeepLinkTarget(ActionInsightTargetType TargetType, Guid TargetId);

public sealed record ActionDeepLink(
    string Href,
    ActionInsightTargetType TargetType,
    Guid TargetId);

public static class ActionDeepLinkRoutes
{
    public const string ApprovalDetail = "/approvals";
    public const string TaskDetail = "/tasks";
    public const string WorkflowInstanceDetail = "/workflows";
    public const string Dashboard = "/dashboard";
}

public interface IActionInsightService
{
    Task<IReadOnlyList<ActionQueueItemDto>> GetActionQueueAsync(Guid companyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ActionQueueItemDto>> GetTopActionsAsync(Guid companyId, int count, CancellationToken cancellationToken);

    Task<ActionQueuePageDto> GetAllActionsAsync(Guid companyId, int pageNumber, int pageSize, CancellationToken cancellationToken);

    Task<ActionQueueItemDto?> AcknowledgeAsync(Guid companyId, string insightKey, CancellationToken cancellationToken);
}

public interface IInsightScoringService
{
    ScoredInsightCandidate Score(InsightCandidate candidate, DateTime nowUtc);

    IReadOnlyList<ScoredInsightCandidate> Prioritize(IEnumerable<InsightCandidate> candidates, DateTime nowUtc);
}

public interface IActionDeepLinkResolver
{
    ActionDeepLink Resolve(Guid companyId, ActionDeepLinkTarget target);
}

public sealed class DefaultInsightScoringService : IInsightScoringService
{
    public ScoredInsightCandidate Score(InsightCandidate candidate, DateTime nowUtc)
    {
        var score =
            SourceWeight(candidate.Type) +
            SlaWeight(candidate.SlaState) +
            DueTimeWeight(candidate.DueUtc, nowUtc) +
            SeverityWeight(candidate.SeverityWeight) +
            OccurrenceWeight(candidate.OccurrenceCount);

        return new ScoredInsightCandidate(
            candidate,
            score,
            score,
            ActionInsightPriorityValues.FromScore(score));
    }

    public IReadOnlyList<ScoredInsightCandidate> Prioritize(IEnumerable<InsightCandidate> candidates, DateTime nowUtc) =>
        ActionQueueOrdering.Order(candidates.Select(candidate => Score(candidate, nowUtc)));

    private static int SourceWeight(ActionInsightType type) =>
        type switch
        {
            ActionInsightType.Approval => 55,
            ActionInsightType.BlockedWorkflow => 50,
            ActionInsightType.Risk => 45,
            ActionInsightType.Task => 40,
            ActionInsightType.Opportunity => 25,
            _ => 0
        };

    private static int SlaWeight(ActionInsightSlaState state) =>
        state switch
        {
            ActionInsightSlaState.Breached => 35,
            ActionInsightSlaState.DueSoon => 20,
            ActionInsightSlaState.OnTrack => 5,
            _ => 0
        };

    private static int DueTimeWeight(DateTime? dueUtc, DateTime nowUtc)
    {
        if (!dueUtc.HasValue)
        {
            return 0;
        }

        var remaining = dueUtc.Value - nowUtc;
        if (remaining <= TimeSpan.Zero)
        {
            return 20;
        }

        if (remaining <= TimeSpan.FromHours(4))
        {
            return 15;
        }

        if (remaining <= TimeSpan.FromDays(1))
        {
            return 8;
        }

        return 0;
    }

    private static int SeverityWeight(int severityWeight) => Math.Clamp(severityWeight, 0, 20);

    private static int OccurrenceWeight(int occurrenceCount) => Math.Clamp(Math.Max(occurrenceCount, 1) - 1, 0, 5);
}

public static class ActionQueueOrdering
{
    public static IReadOnlyList<ScoredInsightCandidate> Order(IEnumerable<ScoredInsightCandidate> candidates) =>
        candidates
            .OrderByDescending(scored => (int)scored.Priority)
            .ThenBy(scored => scored.Candidate.DueUtc.HasValue ? 0 : 1)
            .ThenBy(scored => scored.Candidate.DueUtc ?? DateTime.MaxValue)
            .ThenByDescending(scored => scored.ImpactScore)
            .ThenBy(scored => scored.Candidate.SourceEntityId)
            .ThenBy(scored => scored.Candidate.InsightKey, StringComparer.Ordinal)
            .ToList();
}

public sealed class DefaultActionDeepLinkResolver : IActionDeepLinkResolver
{
    public ActionDeepLink Resolve(Guid companyId, ActionDeepLinkTarget target)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (target.TargetId == Guid.Empty)
        {
            throw new ArgumentException("TargetId is required.", nameof(target));
        }

        var companyQuery = $"companyId={companyId:D}";
        var href = target.TargetType switch
        {
            ActionInsightTargetType.Approval => $"{ActionDeepLinkRoutes.ApprovalDetail}?{companyQuery}&approvalId={target.TargetId:D}",
            ActionInsightTargetType.Task => $"{ActionDeepLinkRoutes.TaskDetail}?{companyQuery}&taskId={target.TargetId:D}",
            ActionInsightTargetType.Workflow => $"{ActionDeepLinkRoutes.WorkflowInstanceDetail}?{companyQuery}&workflowInstanceId={target.TargetId:D}",
            ActionInsightTargetType.Alert => $"{ActionDeepLinkRoutes.Dashboard}?{companyQuery}&alertId={target.TargetId:D}",
            _ => $"{ActionDeepLinkRoutes.Dashboard}?{companyQuery}"
        };

        return new ActionDeepLink(href, target.TargetType, target.TargetId);
    }
}

public static class InsightKey
{
    public static string For(Guid companyId, ActionInsightType type, Guid sourceEntityId, int sourceLifecycleVersion = 0) =>
        $"{companyId:N}:{type.ToStorageValue()}:{sourceEntityId:N}:v{sourceLifecycleVersion}";
}