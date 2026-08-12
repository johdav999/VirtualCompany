using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Mailbox;

public static class EmailClassificationDomains
{
    public const string Finance = "finance";
    public const string Sales = "sales";
    public const string Support = "support";
    public const string Unknown = "unknown";
}

public static class EmailClassificationIntents
{
    public const string Invoice = "invoice";
    public const string Receipt = "receipt";
    public const string SupplierSubscriptionAgreement = "supplier_subscription_agreement";
    public const string SalesLead = "sales_lead";
    public const string SupportRequest = "support_request";
    public const string Newsletter = "newsletter";
    public const string NonBusiness = "non_business";
    public const string Operational = "operational";
    public const string Unknown = "unknown";
}

public static class EmailClassificationActions
{
    public const string Ignore = "ignore";
    public const string RouteToFinanceReview = "route_to_finance_review";
    public const string CreateSalesLeadDraft = "create_sales_lead_draft";
    public const string CreateSupportCase = "create_support_case";
    public const string HumanReview = "human_review";
}

public sealed record EmailClassificationRequest(
    Guid CompanyId,
    MailboxPurpose MailboxPurpose,
    string Provider,
    Guid? MailboxConnectionId,
    MailboxMessageSummary? Summary,
    IReadOnlyList<MailboxInboundMessage> Messages,
    bool AllowAi = true);

public sealed record EmailClassificationResult(
    string Domain,
    string Intent,
    decimal Confidence,
    string EvidenceSummary,
    string RecommendedAction,
    bool RequiresHumanReview,
    bool UsedDeterministicRules,
    bool UsedAi,
    IReadOnlyList<string> RuleMatches,
    string? IgnoreReason = null,
    string? Urgency = null,
    string? ProductOrServiceInterest = null);

public interface IEmailClassificationService
{
    Task<EmailClassificationResult> ClassifyAsync(EmailClassificationRequest request, CancellationToken cancellationToken);
}