

namespace VirtualCompany.Application.Sales;


public sealed record OutboundReviewDecisionRequest(string? Comment);


public sealed record UpdateOutboundAutomationPolicyRequest(
    bool OutboundEnabled,
    int MaxEmailsPerDay,
    bool RequireApprovalFirstContact,
    bool RequireApprovalPricingDiscussion,
    bool RequireApprovalFollowUps,
    bool RequireApprovalReEngagement,
    int WebsiteLeadDeduplicationWindowMinutes,
    Guid? WebsiteLeadFollowUpSequenceId);


public sealed record OutboundEditAndApproveRequest(
    string Subject,
    string Body,
    string? Comment);
