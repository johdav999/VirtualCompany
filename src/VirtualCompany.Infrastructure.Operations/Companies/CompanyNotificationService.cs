using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
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
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<CompanyNotificationService> _logger;

    public CompanyNotificationService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        IApprovalRequestService approvalRequestService,
        IAuditEventWriter auditEventWriter,
        ILogger<CompanyNotificationService> logger)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _approvalRequestService = approvalRequestService;
        _auditEventWriter = auditEventWriter;
        _logger = logger;
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
            .ApplyApprovalInboxOrdering()
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
        _logger.LogInformation(
            "Resolving approval inbox detail. CompanyId: {CompanyId}. ApprovalId: {ApprovalId}. UserId: {UserId}. MembershipRole: {MembershipRole}.",
            companyId,
            approvalId,
            membership.UserId,
            membership.MembershipRole);
        var approval = await _approvalRequestService.GetAsync(companyId, approvalId, cancellationToken);

        if (!string.Equals(approval.Status, ApprovalRequestStatus.Pending.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Approval inbox detail rejected because approval is not pending. CompanyId: {CompanyId}. ApprovalId: {ApprovalId}. Status: {Status}.",
                companyId,
                approvalId,
                approval.Status);
            throw new KeyNotFoundException("Approval request is not pending.");
        }

        if (!IsEligibleApprover(approval, membership))
        {
            _logger.LogWarning(
                "Approval inbox detail rejected because current user is not eligible approver. CompanyId: {CompanyId}. ApprovalId: {ApprovalId}. UserId: {UserId}. MembershipRole: {MembershipRole}. CurrentStepApproverType: {ApproverType}. CurrentStepApproverRef: {ApproverRef}.",
                companyId,
                approvalId,
                membership.UserId,
                membership.MembershipRole,
                approval.CurrentStep?.ApproverType,
                approval.CurrentStep?.ApproverRef);
            throw new UnauthorizedAccessException("The current user is not an approver for this approval request.");
        }

        _logger.LogInformation(
            "Approval inbox detail resolved for eligible approver. CompanyId: {CompanyId}. ApprovalId: {ApprovalId}. TargetType: {TargetType}. TargetId: {TargetId}. AffectedEntities: {AffectedEntities}.",
            companyId,
            approvalId,
            approval.TargetEntityType,
            approval.TargetEntityId,
            approval.AffectedEntities.Count);
        return approval;
    }

    public async Task<ApprovalDecisionResultDto> DecideApprovalAsync(
        Guid companyId,
        Guid approvalId,
        ApprovalDecisionCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        _logger.LogInformation(
            "Attempting approval inbox decision. CompanyId: {CompanyId}. ApprovalId: {ApprovalId}. UserId: {UserId}. MembershipRole: {MembershipRole}. StepId: {StepId}. Decision: {Decision}.",
            companyId,
            approvalId,
            membership.UserId,
            membership.MembershipRole,
            command.StepId,
            command.Decision);
        var approval = await _approvalRequestService.GetAsync(companyId, approvalId, cancellationToken);

        if (!string.Equals(approval.Status, ApprovalRequestStatus.Pending.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Approval inbox decision rejected because approval is not pending. CompanyId: {CompanyId}. ApprovalId: {ApprovalId}. Status: {Status}.",
                companyId,
                approvalId,
                approval.Status);
            throw new ApprovalValidationException(new Dictionary<string, string[]>
            {
                [nameof(command.Decision)] = [$"Only pending approvals can be decided from the inbox. Current status: {approval.Status}."]
            });
        }

        if (!IsEligibleApprover(approval, membership))
        {
            _logger.LogWarning(
                "Approval inbox decision rejected because current user is not eligible approver. CompanyId: {CompanyId}. ApprovalId: {ApprovalId}. UserId: {UserId}. MembershipRole: {MembershipRole}. CurrentStepApproverType: {ApproverType}. CurrentStepApproverRef: {ApproverRef}.",
                companyId,
                approvalId,
                membership.UserId,
                membership.MembershipRole,
                approval.CurrentStep?.ApproverType,
                approval.CurrentStep?.ApproverRef);
            throw new UnauthorizedAccessException("The current user is not an approver for this approval request.");
        }

        var normalizedCommand = command.ApprovalId == Guid.Empty
            ? command with { ApprovalId = approvalId }
            : command;

        if (normalizedCommand.ApprovalId != approvalId)
        {
            _logger.LogWarning(
                "Approval inbox decision rejected because payload approval id did not match route. CompanyId: {CompanyId}. RouteApprovalId: {ApprovalId}. PayloadApprovalId: {PayloadApprovalId}.",
                companyId,
                approvalId,
                normalizedCommand.ApprovalId);
            throw new ApprovalValidationException(new Dictionary<string, string[]>
            {
                [nameof(command.ApprovalId)] = ["Approval id must match the route."]
            });
        }

        var result = await _approvalRequestService.DecideAsync(companyId, normalizedCommand, cancellationToken);
        _logger.LogInformation(
            "Approval inbox decision persisted. CompanyId: {CompanyId}. ApprovalId: {ApprovalId}. Finalized: {Finalized}. Status: {Status}. NextStepId: {NextStepId}.",
            companyId,
            approvalId,
            result.IsFinalized,
            result.Approval.Status,
            result.NextStep?.Id);
        return result;
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
        var previousStatus = notification.Status;

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

        if (notification.Status == CompanyNotificationStatus.Actioned &&
            previousStatus != CompanyNotificationStatus.Actioned)
        {
            await WriteNotificationActionedAuditAsync(notification, membership.UserId, previousStatus, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(notification);
    }

    private async Task WriteNotificationActionedAuditAsync(
        CompanyNotification notification,
        Guid actorUserId,
        CompanyNotificationStatus previousStatus,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["notificationId"] = notification.Id.ToString("N"),
            ["notificationType"] = notification.Type.ToStorageValue(),
            ["previousStatus"] = previousStatus.ToStorageValue(),
            ["newStatus"] = notification.Status.ToStorageValue(),
            ["recipientUserId"] = notification.UserId.ToString("N"),
            ["relatedEntityType"] = notification.RelatedEntityType,
            ["relatedEntityId"] = notification.RelatedEntityId?.ToString("N")
        };

        if (string.Equals(notification.RelatedEntityType, AuditTargetTypes.ApprovalRequest, StringComparison.OrdinalIgnoreCase) &&
            notification.RelatedEntityId is Guid approvalId)
        {
            metadata["approvalRequestId"] = approvalId.ToString("N");
        }

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                notification.CompanyId,
                AuditActorTypes.User,
                actorUserId,
                AuditEventActions.CompanyNotificationActioned,
                AuditTargetTypes.CompanyNotification,
                notification.Id.ToString("N"),
                AuditEventOutcomes.Succeeded,
                RationaleSummary: $"Notification {notification.Type.ToStorageValue()} was marked actioned from inbox.",
                DataSources: ["notifications", "http_request"],
                Metadata: metadata),
            cancellationToken);
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
            "proactive_message" => CompanyNotificationType.ProactiveMessage,
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
            approval.CompanyId,
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
