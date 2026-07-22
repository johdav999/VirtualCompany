namespace VirtualCompany.Domain.Entities;
public sealed class SalesActionApproval : ICompanyOwnedEntity
{
    private SalesActionApproval()
    {
    }

    public SalesActionApproval(Guid id, Guid companyId, string actionSummary, string reason, Guid? recommendationId = null, Guid? leadId = null, Guid? dealId = null, string status = SalesStatuses.WaitingForApproval)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        RecommendationId = SalesEntityText.NormalizeOptionalId(recommendationId, nameof(recommendationId));
        LeadId = SalesEntityText.NormalizeOptionalId(leadId, nameof(leadId));
        DealId = SalesEntityText.NormalizeOptionalId(dealId, nameof(dealId));
        ActionSummary = SalesEntityText.NormalizeRequired(actionSummary, nameof(actionSummary), 500);
        Reason = SalesEntityText.NormalizeRequired(reason, nameof(reason), 1000);
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? RecommendationId { get; private set; }
    public Guid? LeadId { get; private set; }
    public Guid? DealId { get; private set; }
    public string ActionSummary { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public SalesAgentRecommendation? Recommendation { get; private set; }
    public Lead? Lead { get; private set; }
    public Deal? Deal { get; private set; }
}

