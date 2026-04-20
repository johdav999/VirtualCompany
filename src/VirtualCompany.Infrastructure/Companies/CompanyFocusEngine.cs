using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Focus;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyFocusEngine : IFocusEngine
{
    private const int MaxItems = 5;

    private readonly IEnumerable<IFocusCandidateSource> _sources;
    private readonly ICompanyMembershipContextResolver _membershipResolver;

    public CompanyFocusEngine(
        IEnumerable<IFocusCandidateSource> sources,
        ICompanyMembershipContextResolver membershipResolver)
    {
        _sources = sources;
        _membershipResolver = membershipResolver;
    }

    public async Task<IReadOnlyList<FocusItemDto>> GetFocusAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(query));
        }

        if (query.UserId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(query));
        }

        var membership = await _membershipResolver.ResolveAsync(query.CompanyId, cancellationToken);
        if (membership is null || membership.UserId != query.UserId)
        {
            throw new UnauthorizedAccessException("The requested company focus feed is not available for the current user.");
        }

        var candidates = new List<FocusCandidate>();
        foreach (var source in _sources)
        {
            var sourceCandidates = await source.GetCandidatesAsync(query, cancellationToken);
            candidates.AddRange(sourceCandidates.Where(IsValidCandidate));
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.RawScore)
            .ThenByDescending(candidate => candidate.SortUtc ?? DateTime.MinValue)
            .ThenBy(candidate => candidate.StableSortKey, StringComparer.Ordinal)
            .ToList();

        var diversified = ordered
            .GroupBy(candidate => candidate.SourceType)
            .Select(group => group.First())
            .OrderByDescending(candidate => candidate.RawScore)
            .ThenByDescending(candidate => candidate.SortUtc ?? DateTime.MinValue)
            .ThenBy(candidate => candidate.StableSortKey, StringComparer.Ordinal)
            .Take(MaxItems)
            .ToList();

        var selectedKeys = new HashSet<string>(diversified.Select(candidate => candidate.StableSortKey), StringComparer.Ordinal);
        foreach (var candidate in ordered)
        {
            if (diversified.Count >= MaxItems)
            {
                break;
            }

            if (selectedKeys.Add(candidate.StableSortKey))
            {
                diversified.Add(candidate);
            }
        }

        var selected = diversified
            .OrderByDescending(candidate => candidate.RawScore)
            .ThenByDescending(candidate => candidate.SortUtc ?? DateTime.MinValue)
            .ThenBy(candidate => candidate.StableSortKey, StringComparer.Ordinal)
            .ToList();

        var scores = NormalizeScores(selected);
        return selected
            .Select(candidate => new FocusItemDto(
                candidate.Id,
                candidate.Title.Trim(),
                candidate.Description.Trim(),
                FocusActionTypes.Normalize(candidate.ActionType),
                scores[candidate.StableSortKey],
                candidate.NavigationTarget.Trim(),
                candidate.SourceType.ToStorageValue()))
            .ToList();
    }

    private static bool IsValidCandidate(FocusCandidate candidate) =>
        !string.IsNullOrWhiteSpace(candidate.Id) &&
        !string.IsNullOrWhiteSpace(candidate.Title) &&
        !string.IsNullOrWhiteSpace(candidate.Description) &&
        !string.IsNullOrWhiteSpace(candidate.ActionType) &&
        !string.IsNullOrWhiteSpace(candidate.NavigationTarget) &&
        !string.IsNullOrWhiteSpace(candidate.StableSortKey);

    private static IReadOnlyDictionary<string, int> NormalizeScores(IReadOnlyList<FocusCandidate> selected)
    {
        if (selected.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        if (selected.Count == 1)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [selected[0].StableSortKey] = 100
            };
        }

        var min = selected.Min(candidate => candidate.RawScore);
        var max = selected.Max(candidate => candidate.RawScore);

        if (Math.Abs(max - min) < 0.001d)
        {
            return selected.ToDictionary(
                candidate => candidate.StableSortKey,
                _ => 100,
                StringComparer.Ordinal);
        }

        return selected.ToDictionary(
            candidate => candidate.StableSortKey,
            candidate => Math.Clamp((int)Math.Round(((candidate.RawScore - min) / (max - min)) * 100d, MidpointRounding.AwayFromZero), 0, 100),
            StringComparer.Ordinal);
    }
}

public sealed class ApprovalFocusCandidateSource : IFocusCandidateSource
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipResolver;

    public ApprovalFocusCandidateSource(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipResolver)
    {
        _dbContext = dbContext;
        _membershipResolver = membershipResolver;
    }

    public async Task<IReadOnlyList<FocusCandidate>> GetCandidatesAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken)
    {
        var membership = await _membershipResolver.ResolveAsync(query.CompanyId, cancellationToken);
        if (membership is null)
        {
            return [];
        }

        var approvals = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .Include(request => request.Steps)
            .Where(request => request.CompanyId == query.CompanyId && request.Status == ApprovalRequestStatus.Pending)
            .ToListAsync(cancellationToken);

        var items = new List<FocusCandidate>();
        foreach (var approval in approvals)
        {
            var currentStep = approval.CurrentActionableStep;
            if (currentStep is null || !CanCurrentUserAct(currentStep, membership))
            {
                continue;
            }

            var createdAgeHours = Math.Max(0d, (DateTime.UtcNow - approval.CreatedUtc).TotalHours);
            var directAssignmentBonus = currentStep.ApproverType == ApprovalStepApproverType.User ? 8d : 3d;
            var targetBonus = approval.TargetEntityType switch
            {
                "task" => 6d,
                "workflow" => 5d,
                _ => 4d
            };
            var rawScore = 72d + Math.Min(createdAgeHours / 6d, 18d) + directAssignmentBonus + targetBonus;
            var title = $"Approval required for {ToDisplayLabel(approval.TargetEntityType)}";
            var description = !string.IsNullOrWhiteSpace(approval.DecisionSummary)
                ? approval.DecisionSummary!
                : $"Review the {approval.ApprovalType} approval for {ToDisplayLabel(approval.TargetEntityType).ToLowerInvariant()} {approval.TargetEntityId:N}.";

            items.Add(new FocusCandidate(
                approval.Id.ToString("N"),
                title,
                description,
                FocusActionTypes.Review,
                $"/approvals?companyId={query.CompanyId:D}&approvalId={approval.Id:D}",
                FocusSourceType.Approval,
                rawScore,
                approval.CreatedUtc,
                $"approval:{approval.Id:N}"));
        }

        return items;
    }

    private static bool CanCurrentUserAct(ApprovalStep step, ResolvedCompanyMembershipContext membership)
    {
        if (step.ApproverType == ApprovalStepApproverType.User)
        {
            return Guid.TryParse(step.ApproverRef, out var requiredUserId) && requiredUserId == membership.UserId;
        }

        if (step.ApproverType != ApprovalStepApproverType.Role)
        {
            return false;
        }

        return membership.MembershipRole is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin ||
               CompanyMembershipRoles.TryParse(step.ApproverRef, out var role) && role == membership.MembershipRole;
    }

    private static string ToDisplayLabel(string entityType) =>
        entityType.Replace("_", " ", StringComparison.OrdinalIgnoreCase);
}

public sealed class TaskFocusCandidateSource : IFocusCandidateSource
{
    private readonly VirtualCompanyDbContext _dbContext;

    public TaskFocusCandidateSource(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<FocusCandidate>> GetCandidatesAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken)
    {
        var tasks = await _dbContext.WorkTasks
            .AsNoTracking()
            .Where(task => task.CompanyId == query.CompanyId &&
                task.Status != WorkTaskStatus.Completed &&
                task.Status != WorkTaskStatus.Failed &&
                // Work tasks do not yet model direct human assignees, so focus items stay
                // user-scoped by the originating human actor to avoid leaking peer work.
                task.CreatedByActorType == WorkTaskSourceTypes.User &&
                task.CreatedByActorId == query.UserId)
            .OrderByDescending(task => task.UpdatedUtc)
            .Take(15)
            .ToListAsync(cancellationToken);

        return tasks.Select(task =>
        {
            var rawScore = 28d +
                PriorityScore(task.Priority) +
                StatusScore(task.Status) +
                DueDateScore(task.DueUtc) +
                Math.Min((DateTime.UtcNow - task.CreatedUtc).TotalHours / 24d, 8d);

            var description = !string.IsNullOrWhiteSpace(task.Description)
                ? task.Description!
                : !string.IsNullOrWhiteSpace(task.RationaleSummary)
                    ? task.RationaleSummary!
                    : $"Open the {task.Type} task and continue the next action.";

            return new FocusCandidate(
                task.Id.ToString("N"),
                task.Title,
                description,
                task.Status == WorkTaskStatus.AwaitingApproval ? FocusActionTypes.Review : FocusActionTypes.Open,
                $"/tasks?companyId={query.CompanyId:D}&taskId={task.Id:D}",
                FocusSourceType.Task,
                rawScore,
                task.DueUtc ?? task.UpdatedUtc,
                $"task:{task.Id:N}");
        }).ToList();
    }

    private static double PriorityScore(WorkTaskPriority priority) =>
        priority switch
        {
            WorkTaskPriority.Critical => 34d,
            WorkTaskPriority.High => 24d,
            WorkTaskPriority.Normal => 14d,
            _ => 6d
        };

    private static double StatusScore(WorkTaskStatus status) =>
        status switch
        {
            WorkTaskStatus.AwaitingApproval => 22d,
            WorkTaskStatus.Blocked => 20d,
            WorkTaskStatus.InProgress => 12d,
            _ => 6d
        };

    private static double DueDateScore(DateTime? dueUtc)
    {
        if (!dueUtc.HasValue)
        {
            return 0d;
        }

        var deltaHours = (dueUtc.Value - DateTime.UtcNow).TotalHours;
        if (deltaHours <= 0)
        {
            return 24d;
        }

        if (deltaHours <= 24)
        {
            return 16d;
        }

        if (deltaHours <= 72)
        {
            return 8d;
        }

        return 2d;
    }
}

public sealed class AlertAnomalyFocusCandidateSource : IFocusCandidateSource
{
    private readonly VirtualCompanyDbContext _dbContext;

    public AlertAnomalyFocusCandidateSource(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<FocusCandidate>> GetCandidatesAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken)
    {
        var anomalies = await _dbContext.Alerts
            .AsNoTracking()
            .Where(alert => alert.CompanyId == query.CompanyId &&
                alert.Type == AlertType.Anomaly &&
                (alert.Status == AlertStatus.Open || alert.Status == AlertStatus.Acknowledged))
            .OrderByDescending(alert => alert.LastDetectedUtc ?? alert.UpdatedUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        return anomalies.Select(alert => new FocusCandidate(
            alert.Id.ToString("N"),
            alert.Title,
            alert.Summary,
            FocusActionTypes.Investigate,
            $"/finance/anomalies/{alert.Id:D}?companyId={query.CompanyId:D}",
            FocusSourceType.Anomaly,
            48d + SeverityScore(alert.Severity) + RecencyScore(alert.LastDetectedUtc ?? alert.UpdatedUtc) + Math.Min(alert.OccurrenceCount, 5),
            alert.LastDetectedUtc ?? alert.UpdatedUtc,
            $"anomaly:{alert.Id:N}")).ToList();
    }

    private static double SeverityScore(AlertSeverity severity) =>
        severity switch
        {
            AlertSeverity.Critical => 26d,
            AlertSeverity.High => 20d,
            AlertSeverity.Medium => 12d,
            _ => 6d
        };

    private static double RecencyScore(DateTime? timestampUtc)
    {
        if (!timestampUtc.HasValue)
        {
            return 0d;
        }

        var hours = Math.Max(0d, (DateTime.UtcNow - timestampUtc.Value).TotalHours);
        return hours switch
        {
            <= 6d => 14d,
            <= 24d => 10d,
            <= 72d => 6d,
            _ => 2d
        };
    }
}

public sealed class FinanceAlertFocusCandidateSource : IFocusCandidateSource
{
    private readonly VirtualCompanyDbContext _dbContext;

    public FinanceAlertFocusCandidateSource(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<FocusCandidate>> GetCandidatesAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken)
    {
        var alerts = await _dbContext.Alerts
            .AsNoTracking()
            .Where(alert => alert.CompanyId == query.CompanyId &&
                alert.Type == AlertType.Risk &&
                (alert.Status == AlertStatus.Open || alert.Status == AlertStatus.Acknowledged))
            .OrderByDescending(alert => alert.LastDetectedUtc ?? alert.UpdatedUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        return alerts.Select(alert =>
        {
            var rawScore = 42d + SeverityScore(alert.Severity) + RecencyScore(alert.LastDetectedUtc ?? alert.UpdatedUtc);
            return new FocusCandidate(
                alert.Id.ToString("N"),
                alert.Title,
                alert.Summary,
                FocusActionTypes.Resolve,
                $"/finance/alerts/{alert.Id:D}?companyId={query.CompanyId:D}",
                FocusSourceType.FinanceAlert,
                rawScore,
                alert.LastDetectedUtc ?? alert.UpdatedUtc,
                $"finance-alert:{alert.Id:N}");
        }).ToList();
    }

    private static double SeverityScore(AlertSeverity severity) =>
        severity switch
        {
            AlertSeverity.Critical => 30d,
            AlertSeverity.High => 22d,
            AlertSeverity.Medium => 14d,
            _ => 8d
        };

    private static double RecencyScore(DateTime? timestampUtc)
    {
        if (!timestampUtc.HasValue)
        {
            return 0d;
        }

        var hours = Math.Max(0d, (DateTime.UtcNow - timestampUtc.Value).TotalHours);
        return hours switch
        {
            <= 6d => 14d,
            <= 24d => 10d,
            <= 72d => 6d,
            _ => 2d
        };
    }
}
