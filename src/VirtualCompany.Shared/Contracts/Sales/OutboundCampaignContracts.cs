using VirtualCompany.Application.Mailbox;

namespace VirtualCompany.Application.Sales;


public sealed record OutboundAudienceContactResponse(
    Guid ContactId,
    string ContactName,
    string Email,
    string? CustomerCompanyName,
    IReadOnlyList<string> SourceTypes,
    string? PreferredLanguage = null);


public sealed record OutboundPolicyRequest(
    bool OutboundEnabled,
    int MaxEmailsPerDay,
    bool ApprovalRequired);


public sealed record SequenceStepResponse(
    Guid Id,
    int StepOrder,
    int DelayDays,
    string Subject,
    bool AiPersonalizationEnabled);


public sealed record OutboundAudienceSourceResponse(
    string SourceType,
    string Label,
    int ContactCount);


public sealed record SequenceExecutionResponse(
    Guid Id,
    Guid ContactId,
    string ContactName,
    string Status,
    string? StopReason,
    IReadOnlyList<SequenceExecutionStepResponse> Steps);


public sealed record OutboundCampaignSummaryResponse(
    Guid Id,
    string Name,
    string Status,
    int AudienceCount,
    int PendingSteps,
    int SentSteps,
    int BouncedSteps,
    DateTime UpdatedUtc);


public sealed record CreateOutboundCampaignRequest(
    string Name,
    string? Description,
    string AudienceType,
    IReadOnlyList<Guid> ContactIds,
    OutboundPolicyRequest Policy,
    IReadOnlyList<CreateSequenceStepRequest> Steps,
    string? CommunicationLanguage = null);


public sealed record OutboundCampaignContactResponse(
    Guid ContactId,
    string ContactName,
    string Email,
    string Status,
    int? CurrentStepOrder,
    DateTime EnrolledUtc);


public sealed record SaveSequenceDraftRequest(
    string Subject,
    string Body);


public sealed record CreateSequenceStepRequest(
    int StepOrder,
    int DelayDays,
    string Subject,
    string Body,
    bool AiPersonalizationEnabled);


public sealed record OutboundPolicyResponse(
    bool OutboundEnabled,
    int MaxEmailsPerDay,
    bool ApprovalRequired);


public sealed record OutboundAudienceOptionsResponse(
    IReadOnlyList<OutboundAudienceContactResponse> Contacts,
    IReadOnlyList<OutboundAudienceSourceResponse> Sources);


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
