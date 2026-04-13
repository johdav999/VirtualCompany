using VirtualCompany.Application.Approvals;

namespace VirtualCompany.Application.Notifications;

public sealed record NotificationInboxDto(
    IReadOnlyList<NotificationListItemDto> Notifications,
    IReadOnlyList<ApprovalInboxItemDto> PendingApprovals,
    int UnreadCount,
    int PendingApprovalCount);

public sealed record NotificationListItemDto(
    Guid Id,
    Guid CompanyId,
    Guid RecipientUserId,
    string Type,
    string Priority,
    string Title,
    string Body,
    string RelatedEntityType,
    Guid? RelatedEntityId,
    string? ActionUrl,
    string Status,
    DateTime CreatedAt,
    DateTime? ReadAt,
    DateTime? ActionedAt,
    Guid? ActionedByUserId);

public sealed record ListNotificationInboxQuery(
    bool UnreadOnly = false,
    string? Type = null,
    string? Priority = null,
    int Skip = 0,
    int Take = 100);

public sealed record CreateNotificationCommand(
    string Type,
    string Priority,
    string Title,
    string Body,
    string RelatedEntityType,
    Guid? RelatedEntityId,
    string? ActionUrl = null,
    Guid? RecipientUserId = null,
    string? MetadataJson = null,
    string? DedupeKey = null);

public sealed record NotificationUnreadCountDto(int UnreadCount);

public sealed record ApprovalInboxItemDto(
    Guid Id,
    Guid CompanyId,
    string ApprovalType,
    string TargetEntityType,
    Guid TargetEntityId,
    string Status,
    string RationaleSummary,
    string RequestedByActorType,
    Guid RequestedByActorId,
    string? RequiredRole,
    Guid? RequiredUserId,
    string AffectedDataSummary,
    string? ThresholdSummary,
    ApprovalStepDto? CurrentStep,
    DateTime CreatedAt);

public sealed record SetNotificationStatusCommand(string Status);

public interface INotificationInboxService
{
    Task<NotificationInboxDto> GetInboxAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationListItemDto>> ListAsync(Guid companyId, ListNotificationInboxQuery query, CancellationToken cancellationToken);
    Task<NotificationUnreadCountDto> GetUnreadCountAsync(Guid companyId, CancellationToken cancellationToken);
    Task<NotificationListItemDto> CreateAsync(Guid companyId, CreateNotificationCommand command, CancellationToken cancellationToken);
    Task<ApprovalRequestDto> GetApprovalDetailAsync(Guid companyId, Guid approvalId, CancellationToken cancellationToken);
    Task<ApprovalDecisionResultDto> DecideApprovalAsync(Guid companyId, Guid approvalId, ApprovalDecisionCommand command, CancellationToken cancellationToken);
    Task<NotificationListItemDto> SetStatusAsync(Guid companyId, Guid notificationId, SetNotificationStatusCommand command, CancellationToken cancellationToken);
}