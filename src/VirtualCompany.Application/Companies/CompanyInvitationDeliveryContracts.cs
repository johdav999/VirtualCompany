using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Events;

namespace VirtualCompany.Application.Companies;

public static class CompanyOutboxTopics
{
    public const string InvitationCreated = "company.invitation.created";
    public const string InvitationDeliveryRequested = "company.invitation.delivery_requested";
    public const string InvitationResent = "company.invitation.resent";
    public const string InvitationRevoked = "company.invitation.revoked";
    public const string InvitationAccepted = "company.invitation.accepted";
    public const string MembershipRoleChanged = "company.membership.role_changed";
    public const string NotificationDeliveryRequested = "company.notification.delivery_requested";
    public const string AgentScheduledTriggerExecutionRequested = "company.agent_scheduled_trigger.execution_requested";
    public const string TaskCreated = SupportedPlatformEventTypeRegistry.TaskCreated;
    public const string TaskUpdated = SupportedPlatformEventTypeRegistry.TaskUpdated;
    public const string DocumentUploaded = SupportedPlatformEventTypeRegistry.DocumentUploaded;
    public const string WorkflowStateChanged = SupportedPlatformEventTypeRegistry.WorkflowStateChanged;
    public const string ApprovalUpdated = SupportedPlatformEventTypeRegistry.ApprovalUpdated;
    public const string AgentStatusUpdated = SupportedPlatformEventTypeRegistry.AgentStatusUpdated;
}

public sealed record CompanyInvitationDeliveryRequestedMessage(
    Guid InvitationId,
    Guid CompanyId,
    string CompanyName,
    string Email,
    CompanyMembershipRole Role,
    string AcceptanceToken,
    DateTime ExpiresAtUtc,
    Guid InvitedByUserId,
    string? CorrelationId);

public sealed record NotificationDeliveryRequestedMessage(
    Guid CompanyId,
    string NotificationType,
    string Priority,
    string Title,
    string Body,
    string RelatedEntityType,
    Guid? RelatedEntityId,
    string? ActionUrl,
    Guid? RecipientUserId,
    string? RecipientRole,
    Guid? BriefingId,
    string? MetadataJson,
    string DedupeKey,
    string? CorrelationId);

public sealed record CompanyInvitationSendResult(string? ProviderMessageId);

public interface ICompanyOutboxEnqueuer
{
    void Enqueue(
        Guid companyId,
        string topic,
        object payload,
        string? correlationId = null,
        DateTime? availableAtUtc = null,
        string? idempotencyKey = null,
        string? messageType = null,
        string? causationId = null,
        IReadOnlyDictionary<string, string?>? headers = null);
}

public interface ICompanyInvitationDeliveryDispatcher
{
    Task DispatchAsync(CompanyInvitationDeliveryRequestedMessage message, CancellationToken cancellationToken);
}

public interface ICompanyNotificationDispatcher
{
    Task DispatchAsync(NotificationDeliveryRequestedMessage message, CancellationToken cancellationToken);
}

public interface ICompanyInvitationSender
{
    Task<CompanyInvitationSendResult> SendAsync(CompanyInvitationDeliveryRequestedMessage invitation, CancellationToken cancellationToken);
}