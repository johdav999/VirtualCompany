using VirtualCompany.Application.Mailbox;

namespace VirtualCompany.Application.Sales;

public interface IOutboundCampaignService
{
    Task<OutboundCampaignDetailResponse> CreateCampaignAsync(Guid companyId, Guid userId, CreateOutboundCampaignRequest request, CancellationToken cancellationToken);
    Task<OutboundCampaignDetailResponse?> GetCampaignAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken);
    Task<OutboundAudienceOptionsResponse> GetAudienceOptionsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OutboundCampaignSummaryResponse>> ListCampaignsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<OutboundCampaignDetailResponse?> LaunchCampaignAsync(Guid companyId, Guid userId, Guid campaignId, CancellationToken cancellationToken);
    Task<OutboundCampaignDetailResponse?> PauseCampaignAsync(Guid companyId, Guid userId, Guid campaignId, CancellationToken cancellationToken);
    Task<OutboundCampaignDetailResponse?> StopCampaignAsync(Guid companyId, Guid userId, Guid campaignId, string? reason, CancellationToken cancellationToken);
}

public interface ISequenceExecutionService
{
    Task<int> ScheduleExecutionsForCampaignAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken);
    Task<SequenceProcessingResult> ProcessDueStepsAsync(DateTime dueBeforeUtc, int batchSize, CancellationToken cancellationToken);
    Task<int> CancelPendingStepsForContactAsync(Guid companyId, Guid contactId, string stopReason, CancellationToken cancellationToken);
    Task QueueReplyReceivedAsync(Guid companyId, OutboundReplyReceived request, CancellationToken cancellationToken);
    Task<int> HandleReplyReceivedAsync(Guid companyId, OutboundReplyReceived request, CancellationToken cancellationToken);
    Task<int> HandleDealCreatedAsync(Guid companyId, Guid contactId, Guid dealId, CancellationToken cancellationToken);
    Task QueueDealCreatedAsync(Guid companyId, Guid contactId, Guid dealId, CancellationToken cancellationToken);
    Task HandleDeliveryStatusAsync(Guid companyId, OutboundDeliveryStatusRequest request, CancellationToken cancellationToken);
    Task<SequenceExecutionStepResponse?> SaveDraftAsync(Guid companyId, Guid userId, Guid campaignId, Guid stepId, SaveSequenceDraftRequest request, CancellationToken cancellationToken);
    Task HandleBounceAsync(Guid companyId, OutboundBounceRequest request, CancellationToken cancellationToken);
}

public interface IOutboundEmailSender
{
    Task<OutboundEmailSendResult> SendSequenceEmailAsync(OutboundEmailSendRequest request, CancellationToken cancellationToken);
}

public sealed record OutboundEmailSendRequest(
    Guid CompanyId,
    Guid CampaignId,
    Guid SequenceExecutionId,
    Guid SequenceExecutionStepId,
    Guid ContactId,
    string ToEmail,
    string? ToDisplayName,
    string Subject,
    string BodyText,
    string IdempotencyKey,
    string? OriginalGeneratedSubject = null,
    string? OriginalGeneratedBody = null);

public sealed record OutboundEmailSendResult(
    string Provider,
    Guid? MailboxConnectionId,
    string ProviderMessageId,
    string? ProviderThreadId,
    string? InternetMessageId,
    string DeliveryStatus);

public sealed record SequenceProcessingResult(int Sent, int Deferred, int Failed, int Cancelled);

public sealed record OutboundReplyReceived(
    string ProviderMessageId,
    string? ProviderThreadId,
    string? InternetMessageId,
    string SenderEmail,
    DateTime? OccurredUtc = null);

public sealed record OutboundDeliveryStatusRequest(
    string ProviderMessageId,
    string Status,
    DateTime OccurredUtc);

public sealed record OutboundBounceRequest(
    string ProviderMessageId,
    string BounceStatus,
    string? Reason,
    DateTime OccurredUtc);
