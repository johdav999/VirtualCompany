namespace VirtualCompany.Domain.Entities;

public static class MarketingPlanSegmentRoles
{
    public const string Primary = "primary";
    public const string Secondary = "secondary";
    public static bool IsValid(string value) => value is Primary or Secondary;
}

public static class MarketingPlanCampaignStatuses
{
    public const string DraftCreated = "draft_created";
    public const string ReadyForReview = "ready_for_review";
    public const string Blocked = "blocked";
}

public sealed class MarketingPlanSegment : ICompanyOwnedEntity
{
    private MarketingPlanSegment() { }
    public MarketingPlanSegment(Guid id, Guid companyId, Guid planId, Guid segmentVersionId, string role, int priority, string rationale, string expectedContribution)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (planId == Guid.Empty || segmentVersionId == Guid.Empty) throw new ArgumentException("Plan and segment version are required.");
        role = SalesEntityText.NormalizeRequired(role, nameof(role), 32).ToLowerInvariant();
        if (!MarketingPlanSegmentRoles.IsValid(role)) throw new ArgumentException("Segment role must be primary or secondary.");
        if (priority < 1) throw new ArgumentOutOfRangeException(nameof(priority));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingPlanId = planId; MarketingCustomerSegmentVersionId = segmentVersionId;
        Role = role; Priority = priority; Rationale = SalesEntityText.NormalizeRequired(rationale, nameof(rationale), 2000);
        ExpectedContribution = SalesEntityText.NormalizeRequired(expectedContribution, nameof(expectedContribution), 2000); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingPlanId { get; private set; }
    public Guid MarketingCustomerSegmentVersionId { get; private set; } public string Role { get; private set; } = null!;
    public int Priority { get; private set; } public string Rationale { get; private set; } = null!;
    public string ExpectedContribution { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
}

public sealed class MarketingPlanCampaign : ICompanyOwnedEntity
{
    private MarketingPlanCampaign() { }
    public MarketingPlanCampaign(Guid id, Guid companyId, Guid planId, Guid campaignId, string purpose, decimal? allocatedBudget,
        string currency, int priority, string expectedContribution, Guid? creatingAgentId, string idempotencyKey, Guid? objectiveId = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (planId == Guid.Empty || campaignId == Guid.Empty) throw new ArgumentException("Plan and campaign are required.");
        if (allocatedBudget is < 0) throw new ArgumentOutOfRangeException(nameof(allocatedBudget));
        if (priority < 1) throw new ArgumentOutOfRangeException(nameof(priority));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingPlanId = planId; SalesCampaignId = campaignId;
        Purpose = SalesEntityText.NormalizeRequired(purpose, nameof(purpose), 2000); AllocatedBudget = allocatedBudget;
        BudgetCurrency = SalesEntityText.NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant(); Priority = priority;
        ExpectedContribution = SalesEntityText.NormalizeRequired(expectedContribution, nameof(expectedContribution), 2000); CreatingAgentId = creatingAgentId;
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 200); MarketingObjectiveId = objectiveId;
        Status = MarketingPlanCampaignStatuses.DraftCreated; CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingPlanId { get; private set; }
    public Guid SalesCampaignId { get; private set; } public Guid? MarketingObjectiveId { get; private set; }
    public string Purpose { get; private set; } = null!; public decimal? AllocatedBudget { get; private set; }
    public string BudgetCurrency { get; private set; } = null!; public int Priority { get; private set; }
    public string ExpectedContribution { get; private set; } = null!; public string Status { get; private set; } = null!;
    public Guid? CreatingAgentId { get; private set; } public string IdempotencyKey { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
}

public sealed class MarketingPlanCampaignSegment : ICompanyOwnedEntity
{
    private MarketingPlanCampaignSegment() { }
    public MarketingPlanCampaignSegment(Guid id, Guid companyId, Guid planCampaignId, Guid planSegmentId, string rationale, string expectedAudienceContribution)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (planCampaignId == Guid.Empty || planSegmentId == Guid.Empty) throw new ArgumentException("Plan campaign and plan segment are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; MarketingPlanCampaignId = planCampaignId; MarketingPlanSegmentId = planSegmentId;
        Rationale = SalesEntityText.NormalizeRequired(rationale, nameof(rationale), 2000);
        ExpectedAudienceContribution = SalesEntityText.NormalizeRequired(expectedAudienceContribution, nameof(expectedAudienceContribution), 2000); CreatedUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid MarketingPlanCampaignId { get; private set; }
    public Guid MarketingPlanSegmentId { get; private set; } public string Rationale { get; private set; } = null!;
    public string ExpectedAudienceContribution { get; private set; } = null!; public DateTime CreatedUtc { get; private set; }
}
