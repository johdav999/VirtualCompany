namespace VirtualCompany.Application.Sales;

public interface IOutboundAutomationPolicyService
{
    Task<OutboundAutomationPolicyResponse> GetPolicyAsync(Guid companyId, CancellationToken cancellationToken);
    Task<OutboundAutomationPolicyResponse> UpdatePolicyAsync(Guid companyId, Guid userId, UpdateOutboundAutomationPolicyRequest request, CancellationToken cancellationToken);
}

public interface IOutboundAutomationEnforcementService
{
    Task<OutboundPolicyEvaluationResult> EvaluateSequenceStepAsync(Guid companyId, Guid stepId, CancellationToken cancellationToken);
}

public interface IOutboundReviewQueueService
{
    Task<IReadOnlyList<OutboundReviewQueueItemResponse>> ListPendingAsync(Guid companyId, CancellationToken cancellationToken);
    Task<OutboundReviewQueueDetailResponse?> GetAsync(Guid companyId, Guid reviewId, CancellationToken cancellationToken);
    Task<OutboundReviewQueueDetailResponse?> ApproveAsync(Guid companyId, Guid userId, Guid reviewId, OutboundReviewDecisionRequest request, CancellationToken cancellationToken);
    Task<OutboundReviewQueueDetailResponse?> RejectAsync(Guid companyId, Guid userId, Guid reviewId, OutboundReviewDecisionRequest request, CancellationToken cancellationToken);
    Task<OutboundReviewQueueDetailResponse?> EditAndApproveAsync(Guid companyId, Guid userId, Guid reviewId, OutboundEditAndApproveRequest request, CancellationToken cancellationToken);
}

public interface IWebsiteLeadCaptureService
{
    Task<WebsiteLeadSubmissionResponse> SubmitAsync(WebsiteLeadSubmissionRequest request, CancellationToken cancellationToken);
}

public sealed record OutboundAutomationPolicyResponse(
    Guid Id,
    bool OutboundEnabled,
    int MaxEmailsPerDay,
    bool RequireApprovalFirstContact,
    bool RequireApprovalPricingDiscussion,
    bool RequireApprovalFollowUps,
    bool RequireApprovalReEngagement,
    int WebsiteLeadDeduplicationWindowMinutes,
    string WebsiteLeadFormKey,
    Guid? WebsiteLeadFollowUpSequenceId,
    DateTime UpdatedUtc);

public sealed record UpdateOutboundAutomationPolicyRequest(
    bool OutboundEnabled,
    int MaxEmailsPerDay,
    bool RequireApprovalFirstContact,
    bool RequireApprovalPricingDiscussion,
    bool RequireApprovalFollowUps,
    bool RequireApprovalReEngagement,
    int WebsiteLeadDeduplicationWindowMinutes,
    Guid? WebsiteLeadFollowUpSequenceId);

public sealed record OutboundPolicyEvaluationResult(
    string Outcome,
    string ReasonCode,
    string Reason,
    Guid? ReviewId);

public static class OutboundPolicyOutcomes
{
    public const string Allowed = "allowed";
    public const string Blocked = "blocked";
    public const string RequiresApproval = "requires_approval";
}

public static class OutboundPolicyReasonCodes
{
    public const string MissingPolicy = "missing_policy";
    public const string OutboundDisabled = "outbound_disabled";
    public const string DailyLimitReached = "daily_limit_reached";
    public const string FirstContactApprovalRequired = "first_contact_approval_required";
    public const string PricingApprovalRequired = "pricing_approval_required";
    public const string FollowUpApprovalRequired = "follow_up_approval_required";
    public const string ReEngagementApprovalRequired = "re_engagement_approval_required";
    public const string Allowed = "allowed";
}

public static class OutboundMessageCategories
{
    public const string FirstContact = "first_contact";
    public const string PricingDiscussion = "pricing_discussion";
    public const string FollowUp = "follow_up";
    public const string ReEngagement = "re_engagement";
}

public sealed record OutboundReviewQueueItemResponse(
    Guid Id,
    Guid SequenceExecutionStepId,
    Guid CampaignId,
    Guid ContactId,
    string ContactName,
    string ContactEmail,
    string Category,
    string Status,
    string Reason,
    DateTime RequestedUtc);

public sealed record OutboundReviewQueueDetailResponse(
    Guid Id,
    Guid SequenceExecutionStepId,
    Guid CampaignId,
    Guid ContactId,
    string ContactName,
    string ContactEmail,
    string Category,
    string Status,
    string ReasonCode,
    string Reason,
    string Subject,
    string Body,
    string? EditedSubject,
    string? EditedBody,
    Guid? DecidedByUserId,
    DateTime? DecidedUtc,
    string? DecisionComment,
    DateTime RequestedUtc);

public sealed record OutboundReviewDecisionRequest(string? Comment);

public sealed record OutboundEditAndApproveRequest(
    string Subject,
    string Body,
    string? Comment);

public sealed record WebsiteLeadSubmissionRequest(
    string TenantKey,
    string Email,
    string? Name,
    string? CompanyName,
    string? Message,
    string? SourceUrl,
    string? FormId,
    string? Phone = null,
    string? ExternalSubmissionId = null,
    IReadOnlyDictionary<string, string?>? Utm = null,
    IReadOnlyDictionary<string, string?>? Metadata = null,
    bool ContactConsent = false,
    string? ConsentLegalBasis = null,
    string? Referrer = null);

public sealed record WebsiteLeadSubmissionResponse(
    string Status,
    DateTime ReceivedUtc,
    Guid? LeadId = null,
    bool Deduplicated = false,
    bool EnrollmentAccepted = false,
    Guid? SequenceId = null,
    Guid? SequenceExecutionId = null);
