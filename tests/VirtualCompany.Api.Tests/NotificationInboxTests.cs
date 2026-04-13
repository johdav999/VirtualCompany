using VirtualCompany.Application.Notifications;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class NotificationInboxTests
{
    [Fact]
    public void Notification_state_transitions_track_read_and_actioned_timestamps()
    {
        var notification = CreateNotification(CompanyNotificationType.ApprovalRequested, CompanyNotificationPriority.High);

        Assert.Equal(CompanyNotificationStatus.Unread, notification.Status);
        Assert.Null(notification.ReadUtc);
        Assert.Null(notification.ActionedUtc);

        notification.MarkRead();

        Assert.Equal(CompanyNotificationStatus.Read, notification.Status);
        Assert.NotNull(notification.ReadUtc);
        Assert.Null(notification.ActionedUtc);

        notification.MarkUnread();

        Assert.Equal(CompanyNotificationStatus.Unread, notification.Status);
        Assert.Null(notification.ReadUtc);
        Assert.Null(notification.ActionedUtc);

        var actionedByUserId = Guid.NewGuid();
        notification.MarkActioned(actionedByUserId);

        Assert.Equal(CompanyNotificationStatus.Actioned, notification.Status);
        Assert.NotNull(notification.ReadUtc);
        Assert.NotNull(notification.ActionedUtc);
        Assert.Equal(actionedByUserId, notification.ActionedByUserId);
    }

    [Fact]
    public void Actioned_notification_cannot_be_marked_unread_or_downgraded_to_read()
    {
        var notification = CreateNotification(CompanyNotificationType.ApprovalRequested, CompanyNotificationPriority.High);
        var actionedByUserId = Guid.NewGuid();

        notification.MarkActioned(actionedByUserId);
        var actionedAt = notification.ActionedUtc;

        notification.MarkRead();

        Assert.Equal(CompanyNotificationStatus.Actioned, notification.Status);
        Assert.Equal(actionedAt, notification.ActionedUtc);
        Assert.Equal(actionedByUserId, notification.ActionedByUserId);
        Assert.Throws<InvalidOperationException>(notification.MarkUnread);
    }

    [Fact]
    public void Notification_priority_sort_places_approvals_then_exceptions_then_briefings()
    {
        var approval = CreateNotification(CompanyNotificationType.ApprovalRequested, CompanyNotificationPriority.High);
        var escalation = CreateNotification(CompanyNotificationType.Escalation, CompanyNotificationPriority.High);
        var failure = CreateNotification(CompanyNotificationType.WorkflowFailure, CompanyNotificationPriority.Critical);
        var briefing = CreateNotification(CompanyNotificationType.BriefingAvailable, CompanyNotificationPriority.Normal);

        var ordered = new[] { briefing, escalation, approval, failure }
            .AsQueryable()
            .ApplyInboxOrdering()
            .ToList();

        Assert.Same(approval, ordered[0]);
        Assert.Equal([CompanyNotificationType.WorkflowFailure, CompanyNotificationType.Escalation], ordered.Skip(1).Take(2).Select(x => x.Type));
        Assert.Same(briefing, ordered[3]);
    }

    [Fact]
    public void Notification_priority_sort_places_read_and_actioned_items_last()
    {
        var readApproval = CreateNotification(CompanyNotificationType.ApprovalRequested, CompanyNotificationPriority.High);
        var actionedException = CreateNotification(CompanyNotificationType.WorkflowFailure, CompanyNotificationPriority.Critical);
        var unreadRoutine = CreateNotification(CompanyNotificationType.BriefingAvailable, CompanyNotificationPriority.Normal);

        readApproval.MarkRead();
        actionedException.MarkActioned(Guid.NewGuid());

        var ordered = new[] { readApproval, actionedException, unreadRoutine }
            .AsQueryable()
            .ApplyInboxOrdering()
            .ToList();

        Assert.Same(unreadRoutine, ordered[0]);
        Assert.Equal([CompanyNotificationStatus.Read, CompanyNotificationStatus.Actioned], ordered.Skip(1).Select(x => x.Status));
    }

    [Fact]
    public void Notification_priority_sort_uses_newest_then_id_within_same_bucket()
    {
        var older = CreateNotification(CompanyNotificationType.BriefingAvailable, CompanyNotificationPriority.Normal);
        var newer = CreateNotification(CompanyNotificationType.BriefingAvailable, CompanyNotificationPriority.Normal);
        SetCreatedUtc(older, new DateTime(2026, 4, 13, 8, 0, 0, DateTimeKind.Utc));
        SetCreatedUtc(newer, new DateTime(2026, 4, 13, 9, 0, 0, DateTimeKind.Utc));

        var ordered = new[] { older, newer }
            .AsQueryable()
            .ApplyInboxOrdering()
            .ToList();

        Assert.Equal([newer.Id, older.Id], ordered.Select(x => x.Id));
    }

    [Fact]
    public void Notification_priority_sort_preserves_existing_tenant_scope()
    {
        var companyId = Guid.NewGuid();
        var matching = CreateNotification(CompanyNotificationType.BriefingAvailable, CompanyNotificationPriority.Normal, companyId);
        var otherCompanyApproval = CreateNotification(CompanyNotificationType.ApprovalRequested, CompanyNotificationPriority.High, Guid.NewGuid());

        var ordered = new[] { otherCompanyApproval, matching }
            .AsQueryable()
            .Where(x => x.CompanyId == companyId)
            .ApplyInboxOrdering()
            .ToList();

        var item = Assert.Single(ordered);
        Assert.Same(matching, item);
    }

    [Fact]
    public void Notification_dedupe_key_preserves_recipient_specific_delivery_key()
    {
        var userId = Guid.NewGuid();
        var notification = new CompanyNotification(
            Guid.NewGuid(),
            Guid.NewGuid(),
            userId,
            CompanyNotificationType.WorkflowFailure,
            CompanyNotificationPriority.Critical,
            "Workflow failed",
            "A workflow step failed.",
            "workflow_exception",
            Guid.NewGuid(),
            "/workflows",
            "{}",
            $"workflow-exception:{Guid.NewGuid():N}:{userId:N}");

        Assert.Contains(userId.ToString("N"), notification.DedupeKey);
    }

    private static CompanyNotification CreateNotification(CompanyNotificationType type, CompanyNotificationPriority priority, Guid? companyId = null) =>
        new(
            Guid.NewGuid(),
            companyId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            type,
            priority,
            type.ToStorageValue(),
            "Notification body.",
            type == CompanyNotificationType.BriefingAvailable ? "company_briefing" : "approval_request",
            Guid.NewGuid(),
            "/inbox",
            "{}",
            $"{type.ToStorageValue()}:{Guid.NewGuid():N}");

    private static void SetCreatedUtc(CompanyNotification notification, DateTime createdUtc) =>
        typeof(CompanyNotification).GetProperty(nameof(CompanyNotification.CreatedUtc))!.SetValue(notification, createdUtc);
}
