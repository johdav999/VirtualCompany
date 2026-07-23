using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Mobile;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared.Mobile;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyMobileSummaryService : IMobileSummaryService
{
    private const int DefaultTaskFollowUpLimit = 5;
    private const int MaxTaskFollowUpLimit = 10;
    private const int MobileSummaryMaxLength = 180;

    private static readonly WorkTaskStatus[] OpenTaskStatuses =
    [
        WorkTaskStatus.New,
        WorkTaskStatus.InProgress,
        WorkTaskStatus.Blocked,
        WorkTaskStatus.AwaitingApproval
    ];

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;
    private readonly TimeProvider _timeProvider;

    public CompanyMobileSummaryService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _timeProvider = timeProvider;
    }

    public async Task<MobileHomeSummaryResponse> GetHomeSummaryAsync(
        GetMobileHomeSummaryQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(query));
        }

        await RequireMembershipAsync(query.CompanyId, cancellationToken);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var take = Math.Clamp(query.TaskFollowUpLimit <= 0 ? DefaultTaskFollowUpLimit : query.TaskFollowUpLimit, 1, MaxTaskFollowUpLimit);

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == query.CompanyId)
            .Select(x => new { x.Id, x.Name })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Company not found.");

        var pendingApprovalCountTask = _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == query.CompanyId && x.Status == ApprovalRequestStatus.Pending, cancellationToken);

        var notificationAlertCountTask = _dbContext.CompanyNotifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(
                x => x.CompanyId == query.CompanyId &&
                     x.Status != CompanyNotificationStatus.Actioned &&
                     x.Status != CompanyNotificationStatus.Suppressed &&
                     (x.Priority == CompanyNotificationPriority.High ||
                      x.Priority == CompanyNotificationPriority.Critical ||
                      x.Type == CompanyNotificationType.Escalation ||
                      x.Type == CompanyNotificationType.WorkflowFailure),
                cancellationToken);

        var workflowAlertCountTask = _dbContext.WorkflowExceptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == query.CompanyId && x.Status == WorkflowExceptionStatus.Open, cancellationToken);

        var openTaskCountTask = _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(x => x.CompanyId == query.CompanyId && OpenTaskStatuses.Contains(x.Status), cancellationToken);

        var blockedTaskCountTask = _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(
                x => x.CompanyId == query.CompanyId &&
                     (x.Status == WorkTaskStatus.Blocked || x.Status == WorkTaskStatus.Failed),
                cancellationToken);

        var overdueTaskCountTask = _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .CountAsync(
                x => x.CompanyId == query.CompanyId &&
                     x.DueUtc.HasValue &&
                     x.DueUtc.Value < nowUtc &&
                     OpenTaskStatuses.Contains(x.Status),
                cancellationToken);

        var taskRowsTask = _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(take)
            .Select(x => new MobileTaskFollowUpRow(
                x.Id,
                x.Title,
                x.Status,
                x.Priority,
                x.AssignedAgent == null ? null : x.AssignedAgent.DisplayName,
                x.RationaleSummary,
                x.UpdatedUtc,
                x.DueUtc))
            .ToListAsync(cancellationToken);

        var latestTaskActivityTask = _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => (DateTime?)x.UpdatedUtc)
            .MaxAsync(cancellationToken);

        var latestNotificationActivityTask = _dbContext.CompanyNotifications
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId)
            .Select(x => (DateTime?)x.CreatedUtc)
            .MaxAsync(cancellationToken);

        await Task.WhenAll(
            pendingApprovalCountTask,
            notificationAlertCountTask,
            workflowAlertCountTask,
            openTaskCountTask,
            blockedTaskCountTask,
            overdueTaskCountTask,
            taskRowsTask,
            latestTaskActivityTask,
            latestNotificationActivityTask);

        var pendingApprovalCount = await pendingApprovalCountTask;
        var activeAlertCount = await notificationAlertCountTask + await workflowAlertCountTask;
        var openTaskCount = await openTaskCountTask;
        var blockedTaskCount = await blockedTaskCountTask;
        var overdueTaskCount = await overdueTaskCountTask;
        var latestActivityUtc = Max(await latestTaskActivityTask, await latestNotificationActivityTask);

        var companyStatus = new MobileCompanyStatusSummaryDto(
            company.Id,
            company.Name,
            nowUtc,
            latestActivityUtc,
            BuildHeadline(pendingApprovalCount, activeAlertCount, blockedTaskCount, overdueTaskCount),
            latestActivityUtc is null ? "No recent task or alert activity." : "Latest task and alert activity is summarized below.",
            pendingApprovalCount,
            activeAlertCount,
            openTaskCount,
            blockedTaskCount,
            overdueTaskCount,
            [
                new MobileCompanyStatusMetricDto("pending_approvals", "Approvals", pendingApprovalCount, pendingApprovalCount > 0 ? "attention" : "neutral"),
                new MobileCompanyStatusMetricDto("active_alerts", "Alerts", activeAlertCount, activeAlertCount > 0 ? "attention" : "neutral"),
                new MobileCompanyStatusMetricDto("open_tasks", "Open tasks", openTaskCount, openTaskCount > 0 ? "active" : "neutral"),
                new MobileCompanyStatusMetricDto("blocked_tasks", "Blocked", blockedTaskCount, blockedTaskCount > 0 ? "attention" : "neutral"),
                new MobileCompanyStatusMetricDto("overdue_tasks", "Overdue", overdueTaskCount, overdueTaskCount > 0 ? "attention" : "neutral")
            ]);

        var followUps = (await taskRowsTask)
            .Select(x => new MobileTaskFollowUpSummaryDto(
                x.Id,
                x.Title,
                x.Status.ToStorageValue(),
                x.Priority.ToStorageValue(),
                x.AssignedAgentDisplayName,
                BuildTaskSummary(x, nowUtc),
                x.UpdatedUtc,
                x.DueUtc,
                x.DueUtc.HasValue && x.DueUtc.Value < nowUtc && OpenTaskStatuses.Contains(x.Status)))
            .ToList();

        return new MobileHomeSummaryResponse(companyStatus, followUps, followUps.Count > 0);
    }

    private async Task RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _membershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }
    }

    private static string BuildHeadline(int pendingApprovalCount, int activeAlertCount, int blockedTaskCount, int overdueTaskCount)
    {
        if (activeAlertCount > 0 || blockedTaskCount > 0 || overdueTaskCount > 0)
        {
            return "Needs executive follow-up";
        }

        return pendingApprovalCount > 0 ? "Approvals waiting" : "Operations steady";
    }

    private static string BuildTaskSummary(MobileTaskFollowUpRow task, DateTime nowUtc)
    {
        if (!string.IsNullOrWhiteSpace(task.RationaleSummary))
        {
            return TrimForMobile(task.RationaleSummary);
        }

        var assignee = string.IsNullOrWhiteSpace(task.AssignedAgentDisplayName) ? "Unassigned" : task.AssignedAgentDisplayName;
        var dueText = task.DueUtc.HasValue && task.DueUtc.Value < nowUtc ? " Past due." : string.Empty;
        return TrimForMobile($"{assignee}. Task is {task.Status.ToStorageValue().Replace('_', ' ')}.{dueText}");
    }

    private static string TrimForMobile(string value) =>
        value.Trim().Length <= MobileSummaryMaxLength
            ? value.Trim()
            : value.Trim()[..MobileSummaryMaxLength].TrimEnd() + "...";

    private static DateTime? Max(DateTime? left, DateTime? right) =>
        left is null ? right : right is null ? left : left > right ? left : right;

    private sealed record MobileTaskFollowUpRow(
        Guid Id,
        string Title,
        WorkTaskStatus Status,
        WorkTaskPriority Priority,
        string? AssignedAgentDisplayName,
        string? RationaleSummary,
        DateTime UpdatedUtc,
        DateTime? DueUtc);
}
