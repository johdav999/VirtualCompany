using System.Linq.Expressions;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Notifications;

public static class CompanyNotificationInboxOrdering
{
    public const int PendingApprovalPriority = 0;
    public const int ExceptionAlertPriority = 1;
    public const int UnreadRoutinePriority = 2;
    public const int ReadOrActionedPriority = 3;

    public static readonly Expression<Func<CompanyNotification, int>> SortPriorityExpression =
        notification =>
            notification.Status == CompanyNotificationStatus.Read ||
            notification.Status == CompanyNotificationStatus.Actioned ||
            notification.Status == CompanyNotificationStatus.Suppressed
                ? ReadOrActionedPriority
                : notification.Type == CompanyNotificationType.ApprovalRequested
                    ? PendingApprovalPriority
                    : notification.Type == CompanyNotificationType.WorkflowFailure ||
                      notification.Type == CompanyNotificationType.Escalation
                        ? ExceptionAlertPriority
                        : UnreadRoutinePriority;

    public static int GetSortPriority(CompanyNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (notification.Status is CompanyNotificationStatus.Read or CompanyNotificationStatus.Actioned or CompanyNotificationStatus.Suppressed)
        {
            return ReadOrActionedPriority;
        }

        return notification.Type switch
        {
            CompanyNotificationType.ApprovalRequested => PendingApprovalPriority,
            CompanyNotificationType.WorkflowFailure or CompanyNotificationType.Escalation => ExceptionAlertPriority,
            _ => UnreadRoutinePriority
        };
    }

    public static IOrderedQueryable<CompanyNotification> ApplyInboxOrdering(this IQueryable<CompanyNotification> query) =>
        query
            .OrderBy(SortPriorityExpression)
            .ThenByDescending(notification => notification.CreatedUtc)
            .ThenBy(notification => notification.Id);
}
