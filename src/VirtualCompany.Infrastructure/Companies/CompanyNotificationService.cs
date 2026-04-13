using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Notifications;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyNotificationService : INotificationInboxService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;
    private readonly IApprovalRequestService _approvalRequestService;

    public CompanyNotificationService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        IApprovalRequestService approvalRequestService)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _approvalRequestService = approvalRequestService;
    }

    public async Task<NotificationInboxDto> GetInboxAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);

        var notifications = await _dbContext.CompanyNotifications
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.UserId == membership.UserId)
            .ApplyInboxOrdering()
            .Take(100)
            .ToListAsync(cancellationToken);

        var approvals = (await _approvalRequestService.ListAsync(companyId, "pending", cancellationToken))
            .Where(x => IsEligibleApprover(x, membership))
            .OrderByDescending(x => x.CreatedAt)
            .Take(50)
            .Select(ToInboxItemDto)
            .ToList();

        return new NotificationInboxDto(
            notifications.Select(ToDto).ToList(),
            approvals,
            notifications.Count(x => x.Status == CompanyNotificationStatus.Unread),
            approvals.Count);
    }

    public async Task<IReadOnlyList<NotificationListItemDto>> ListAsync(
        Guid companyId,
        ListNotificationInboxQuery query,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var notifications = _dbContext.CompanyNotifications
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.UserId == membership.UserId);

        if (query.UnreadOnly)
        {
            notifications = notifications.Where(x => x.Status == CompanyNotificationStatus.Unread);
        }

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = ParseNotificationType(query.Type);
            notifications = notifications.Where(x => x.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(query.Priority))
        {
            var priority = CompanyNotificationPriorityValues.Parse(query.Priority);
            notifications = notifications.Where(x => x.Priority == priority);
        }

        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 200);

        return await notifications
            .ApplyInboxOrdering()
            .Skip(skip)
            .Take(take)
            .Select(x => ToDto(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<NotificationUnreadCountDto> GetUnreadCountAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var unreadCount = await _dbContext.CompanyNotifications
            .AsNoTracking()
            .CountAsync(
                x => x.CompanyId == companyId &&
                     x.UserId == membership.UserId &&
                     x.Status == CompanyNotificationStatus.Unread,
                cancellationToken);

        return new NotificationUnreadCountDto(unreadCount);
    }

    public async Task<NotificationListItemDto> CreateAsync(
        Guid companyId,
        CreateNotificationCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var recipientUserId = command.RecipientUserId is Guid userId && userId != Guid.Empty
            ? userId
            : membership.UserId;

        if (recipientUserId != membership.UserId &&
            membership.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin))
        {
            throw new UnauthorizedAccessException("Only company owners and admins can create notifications for another user.");
        }

        var recipientIsActive = await _dbContext.CompanyMemberships
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.CompanyId == companyId &&
                     x.UserId == recipientUserId &&
                     x.Status == CompanyMembershipStatus.Active,
                cancellationToken);
        if (!recipientIsActive)
        {
            throw new KeyNotFoundException("Notification recipient is not an active company member.");
        }

        var notification = new CompanyNotification(
            Guid.NewGuid(),
            companyId,
            recipientUserId,
            ParseNotificationType(command.Type),
            CompanyNotificationPriorityValues.Parse(command.Priority),
            command.Title,
            command.Body,
            command.RelatedEntityType,
            command.RelatedEntityId,
            command.ActionUrl,
            command.MetadataJson,
            string.IsNullOrWhiteSpace(command.DedupeKey) ? $"manual-notification:{Guid.NewGuid():N}" : command.DedupeKey);

        _dbContext.CompanyNotifications.Add(notification);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(notification);
    }

    public async Task<ApprovalRequestDto> GetApprovalDetailAsync(
        Guid companyId,
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var approval = await _approvalRequestService.GetAsync(companyId, approvalId, cancellationToken);

        if (!string.Equals(approval.Status, ApprovalRequestStatus.Pending.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            throw new KeyNotFoundException("Approval request is not pending.");
        }

        if (!IsEligibleApprover(approval, membership))
        {
            throw new UnauthorizedAccessException("The current user is not an approver for this approval request.");
        }

        return approval;
    }

    public async Task<NotificationListItemDto> SetStatusAsync(
        Guid companyId,
        Guid notificationId,
        SetNotificationStatusCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        var notification = await _dbContext.CompanyNotifications
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.UserId == membership.UserId && x.Id == notificationId, cancellationToken)
            ?? throw new KeyNotFoundException("Notification not found.");

        switch (command.Status.Trim().ToLowerInvariant())
        {
            case "unread":
                notification.MarkUnread();
                break;
            case "read":
                notification.MarkRead();
                break;
            case "actioned":
                notification.MarkActioned(membership.UserId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command.Status, "Notification status must be unread, read, or actioned.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(notification);
    }

    private async Task<ResolvedCompanyMembershipContext> RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _membershipContextResolver.ResolveAsync(companyId, cancellationToken)
        ?? throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");

    private static CompanyNotificationType ParseNotificationType(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "approval_requested" => CompanyNotificationType.ApprovalRequested,
            "escalation" => CompanyNotificationType.Escalation,
            "workflow_failure" => CompanyNotificationType.WorkflowFailure,
            "briefing_available" => CompanyNotificationType.BriefingAvailable,
            _ when Enum.TryParse<CompanyNotificationType>(value.Trim(), ignoreCase: true, out var parsed) => parsed,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported notification type value.")
        };

    private static bool IsEligibleApprover(ApprovalRequestDto approval, ResolvedCompanyMembershipContext membership)
    {
        var current = approval.CurrentStep;
        if (current is null)
        {
            return false;
        }

        if (string.Equals(current.ApproverType, "user", StringComparison.OrdinalIgnoreCase))
        {
            return Guid.TryParse(current.ApproverRef, out var userId) && userId == membership.UserId;
        }

        if (!string.Equals(current.ApproverType, "role", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return membership.MembershipRole is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin ||
               CompanyMembershipRoles.TryParse(current.ApproverRef, out var role) && role == membership.MembershipRole;
    }

    private static NotificationListItemDto ToDto(CompanyNotification notification) =>
        new(
            notification.Id,
            notification.CompanyId,
            notification.UserId,
            notification.Type.ToStorageValue(),
            notification.Priority.ToStorageValue(),
            notification.Title,
            notification.Body,
            notification.RelatedEntityType,
            notification.RelatedEntityId,
            notification.ActionUrl,
            notification.Status.ToStorageValue(),
            notification.CreatedUtc,
            notification.ReadUtc,
            notification.ActionedUtc,
            notification.ActionedByUserId);

    private static ApprovalInboxItemDto ToInboxItemDto(ApprovalRequestDto approval) =>
        new(
            approval.Id,
            approval.ApprovalType,
            approval.TargetEntityType,
            approval.TargetEntityId,
            approval.Status,
            approval.RationaleSummary,
            approval.RequestedByActorType,
            approval.RequestedByActorId,
            approval.RequiredRole,
            approval.RequiredUserId,
            approval.AffectedDataSummary,
            approval.ThresholdSummary,
            approval.CurrentStep,
            approval.CreatedAt);
}