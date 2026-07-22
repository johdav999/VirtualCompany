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

public sealed record SaveSequenceDraftRequest(
    string Subject,
    string Body);

public sealed record CreateOutboundCampaignRequest(
    string Name,
    string? Description,
    string AudienceType,
    IReadOnlyList<Guid> ContactIds,
    OutboundPolicyRequest Policy,
    IReadOnlyList<CreateSequenceStepRequest> Steps,
    string? CommunicationLanguage = null);

public sealed record OutboundPolicyRequest(
    bool OutboundEnabled,
    int MaxEmailsPerDay,
    bool ApprovalRequired);

public sealed record CreateSequenceStepRequest(
    int StepOrder,
    int DelayDays,
    string Subject,
    string Body,
    bool AiPersonalizationEnabled);

public sealed record OutboundCampaignSummaryResponse(
    Guid Id,
    string Name,
    string Status,
    int AudienceCount,
    int PendingSteps,
    int SentSteps,
    int BouncedSteps,
    DateTime UpdatedUtc);

public sealed record OutboundCampaignDetailResponse(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    string AudienceType,
    OutboundPolicyResponse Policy,
    IReadOnlyList<OutboundCampaignContactResponse> Audience,
    IReadOnlyList<SequenceStepResponse> Steps,
    IReadOnlyList<SequenceExecutionResponse> Executions,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    string? CommunicationLanguage = null,
    string? CommunicationLanguageSource = null,
    decimal? CommunicationLanguageConfidence = null,
    bool CommunicationLanguageRequiresReview = false);

public sealed record OutboundAudienceOptionsResponse(
    IReadOnlyList<OutboundAudienceContactResponse> Contacts,
    IReadOnlyList<OutboundAudienceSourceResponse> Sources);

public sealed record OutboundAudienceContactResponse(
    Guid ContactId,
    string ContactName,
    string Email,
    string? CustomerCompanyName,
    IReadOnlyList<string> SourceTypes,
    string? PreferredLanguage = null);

public sealed record OutboundAudienceSourceResponse(
    string SourceType,
    string Label,
    int ContactCount);

public sealed record OutboundPolicyResponse(
    bool OutboundEnabled,
    int MaxEmailsPerDay,
    bool ApprovalRequired);

public sealed record OutboundCampaignContactResponse(
    Guid ContactId,
    string ContactName,
    string Email,
    string Status,
    int? CurrentStepOrder,
    DateTime EnrolledUtc);

public sealed record SequenceStepResponse(
    Guid Id,
    int StepOrder,
    int DelayDays,
    string Subject,
    bool AiPersonalizationEnabled);

public sealed record SequenceExecutionResponse(
    Guid Id,
    Guid ContactId,
    string ContactName,
    string Status,
    string? StopReason,
    IReadOnlyList<SequenceExecutionStepResponse> Steps);

public sealed record SequenceExecutionStepResponse(
    Guid Id,
    int StepOrder,
    string Status,
    DateTime ScheduledSendUtc,
    DateTime? SentUtc,
    string? ProviderMessageId,
    string DeliveryStatus,
    string? BounceStatus,
    string? CancellationReason = null,
    string? CancellationSourceReference = null,
    string? OriginalGeneratedSubject = null,
    string? OriginalGeneratedBody = null,
    string? CurrentDraftSubject = null,
    string? CurrentDraftBody = null,
    string? FinalSentSubject = null,
    string? FinalSentBody = null,
    DateTime? GeneratedDraftUtc = null,
    DateTime? DraftUpdatedUtc = null);
