namespace VirtualCompany.Domain.Entities;
public sealed class SalesAutomationPolicy : ICompanyOwnedEntity
{
    private SalesAutomationPolicy() { }
    public SalesAutomationPolicy(Guid id, Guid companyId, string mode)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Mode = SalesEntityText.NormalizeRequired(mode, nameof(mode), 80).ToLowerInvariant();
        FinanceDocumentsAlwaysRequireApproval = true;
        OutboundEnabled = false;
        MaxEmailsPerDay = 25;
        RequireApprovalFirstContact = true;
        RequireApprovalPricingDiscussion = true;
        RequireApprovalFollowUps = true;
        RequireApprovalReEngagement = true;
        WebsiteLeadFormKey = GenerateWebsiteLeadFormKey();
        WebsiteLeadDeduplicationWindowMinutes = 10080;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Mode { get; private set; } = null!;
    public bool FinanceDocumentsAlwaysRequireApproval { get; private set; }
    public bool OutboundEnabled { get; private set; }
    public int MaxEmailsPerDay { get; private set; }
    public bool RequireApprovalFirstContact { get; private set; }
    public bool RequireApprovalPricingDiscussion { get; private set; }
    public bool RequireApprovalFollowUps { get; private set; }
    public bool RequireApprovalReEngagement { get; private set; }
    public int WebsiteLeadDeduplicationWindowMinutes { get; private set; }
    public string WebsiteLeadFormKey { get; private set; } = null!;
    public Guid? WebsiteLeadFollowUpSequenceId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public void UpdateMode(string mode) { Mode = SalesEntityText.NormalizeRequired(mode, nameof(mode), 80).ToLowerInvariant(); FinanceDocumentsAlwaysRequireApproval = true; UpdatedUtc = DateTime.UtcNow; }

    public void UpdateOutboundSettings(
        bool outboundEnabled,
        int maxEmailsPerDay,
        bool requireApprovalFirstContact,
        bool requireApprovalPricingDiscussion,
        bool requireApprovalFollowUps,
        bool requireApprovalReEngagement,
        int websiteLeadDeduplicationWindowMinutes,
        Guid? websiteLeadFollowUpSequenceId)
    {
        if (maxEmailsPerDay < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEmailsPerDay), "Daily email limit cannot be negative.");
        }

        if (websiteLeadDeduplicationWindowMinutes is < 1 or > 43200)
        {
            throw new ArgumentOutOfRangeException(nameof(websiteLeadDeduplicationWindowMinutes), "Website lead deduplication window must be between 1 minute and 30 days.");
        }

        OutboundEnabled = outboundEnabled;
        MaxEmailsPerDay = maxEmailsPerDay;
        RequireApprovalFirstContact = requireApprovalFirstContact;
        RequireApprovalPricingDiscussion = requireApprovalPricingDiscussion;
        RequireApprovalFollowUps = requireApprovalFollowUps;
        RequireApprovalReEngagement = requireApprovalReEngagement;
        WebsiteLeadDeduplicationWindowMinutes = websiteLeadDeduplicationWindowMinutes;
        WebsiteLeadFollowUpSequenceId = SalesEntityText.NormalizeOptionalId(websiteLeadFollowUpSequenceId, nameof(websiteLeadFollowUpSequenceId));
        UpdatedUtc = DateTime.UtcNow;
    }

    public void EnsureWebsiteLeadFormKey()
    {
        WebsiteLeadFormKey = string.IsNullOrWhiteSpace(WebsiteLeadFormKey) ? GenerateWebsiteLeadFormKey() : WebsiteLeadFormKey;
    }

    private static string GenerateWebsiteLeadFormKey() => $"wlf_{Guid.NewGuid():N}";
}

