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
    public const string SupportMemoryUpdateRequested = "support.memory.update_requested";
    public const string SupportReplyDeliveryRequested = "support.reply.delivery_requested";
    public const string SalesMeetingInvitationDeliveryRequested = "sales.meeting_invitation.delivery_requested";
    public const string SalesMeetingChangeDeliveryRequested = "sales.meeting_change.delivery_requested";
    public const string SalesMeetingConfirmationDeliveryRequested = "sales.meeting_confirmation.delivery_requested";
    public const string AgentScheduledTriggerExecutionRequested = "company.agent_scheduled_trigger.execution_requested";
    public const string GuidedResearchContinuationRequested = "guided_work.research_continuation_requested";
    public const string OnboardingDocumentGenerationRequested = "company.onboarding_document_generation_requested";
    public const string TaskCreated = SupportedPlatformEventTypeRegistry.TaskCreated;
    public const string TaskUpdated = SupportedPlatformEventTypeRegistry.TaskUpdated;
    public const string DocumentUploaded = SupportedPlatformEventTypeRegistry.DocumentUploaded;
    public const string WorkflowStateChanged = SupportedPlatformEventTypeRegistry.WorkflowStateChanged;
    public const string ApprovalUpdated = SupportedPlatformEventTypeRegistry.ApprovalUpdated;
    public const string AgentStatusUpdated = SupportedPlatformEventTypeRegistry.AgentStatusUpdated;
    public const string FinanceTransactionCreated = SupportedPlatformEventTypeRegistry.FinanceTransactionCreated;
    public const string FinanceInvoiceCreated = SupportedPlatformEventTypeRegistry.FinanceInvoiceCreated;
    public const string FinanceBillCreated = SupportedPlatformEventTypeRegistry.FinanceBillCreated;
    public const string FinancePaymentCreated = SupportedPlatformEventTypeRegistry.FinancePaymentCreated;
    public const string FinanceSimulationDayAdvanced = SupportedPlatformEventTypeRegistry.FinanceSimulationDayAdvanced;
    public const string FinanceThresholdBreached = SupportedPlatformEventTypeRegistry.FinanceThresholdBreached;
    public const string SalesEmailReceived = SupportedPlatformEventTypeRegistry.SalesEmailReceived;
    public const string SalesLeadDetected = SupportedPlatformEventTypeRegistry.SalesLeadDetected;
    public const string SalesLeadQualified = SupportedPlatformEventTypeRegistry.SalesLeadQualified;
    public const string SalesDealWon = SupportedPlatformEventTypeRegistry.SalesDealWon;
    public const string SalesDealCreated = SupportedPlatformEventTypeRegistry.SalesDealCreated;
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

public sealed record SupportMemoryUpdateRequestedMessage(Guid CompanyId, Guid SupportCaseId, Guid JobId, string EventKey, string? CorrelationId);

public sealed record GuidedResearchContinuationRequestedMessage(
    Guid CompanyId, Guid SessionId, Guid AgentId, Guid UserMessageId, Guid ClientRequestId,
    string ArtifactType, string SchemaVersion, string Query, string IdempotencyKey, string? CorrelationId);

public sealed record OnboardingDocumentGenerationRequestedMessage(
    Guid CompanyId,Guid SessionId,Guid RequestedByUserId,string DocumentKey,string Title,string FileName,
    string Markdown,string ContentHash,string SchemaVersion,string? CorrelationId);

public interface ICompanyOnboardingDocumentGenerationService
{
    Task ProcessAsync(OnboardingDocumentGenerationRequestedMessage request,CancellationToken cancellationToken);
}

public sealed record SupportReplyDeliveryRequestedMessage(
    Guid CompanyId,
    Guid SupportCaseId,
    Guid DraftId,
    Guid RequestedByUserId,
    bool Autonomous,
    bool ResolveAfterSend,
    Guid? MailboxConnectionId,
    string ToEmail,
    string? ToDisplayName,
    string Subject,
    string OriginalMessageId,
    string? ProviderThreadId,
    string? InternetMessageId,
    string IdempotencyKey,
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

public interface ISupportReplyDeliveryDispatcher
{
    Task DispatchAsync(SupportReplyDeliveryRequestedMessage message, CancellationToken cancellationToken);
}
