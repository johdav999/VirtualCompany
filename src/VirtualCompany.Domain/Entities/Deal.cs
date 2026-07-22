namespace VirtualCompany.Domain.Entities;
public sealed class Deal : ICompanyOwnedEntity
{
    private Deal()
    {
    }

    public Deal(
        Guid id,
        Guid companyId,
        string title,
        Guid pipelineStageId,
        decimal amount,
        string currency,
        string status = SalesStatuses.Open,
        Guid? sourceLeadId = null,
        Guid? primaryContactId = null,
        Guid? customerCompanyId = null,
        DateTime? expectedCloseUtc = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Title = SalesEntityText.NormalizeRequired(title, nameof(title), 200);
        PipelineStageId = pipelineStageId == Guid.Empty ? throw new ArgumentException("PipelineStageId is required.", nameof(pipelineStageId)) : pipelineStageId;
        Amount = amount;
        Currency = SalesEntityText.NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        SourceLeadId = SalesEntityText.NormalizeOptionalId(sourceLeadId, nameof(sourceLeadId));
        PrimaryContactId = SalesEntityText.NormalizeOptionalId(primaryContactId, nameof(primaryContactId));
        CustomerCompanyId = SalesEntityText.NormalizeOptionalId(customerCompanyId, nameof(customerCompanyId));
        ExpectedCloseUtc = expectedCloseUtc.HasValue ? SalesEntityText.NormalizeUtc(expectedCloseUtc.Value, nameof(expectedCloseUtc)) : null;
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? SourceLeadId { get; private set; }
    public Guid? CustomerCompanyId { get; private set; }
    public Guid? PrimaryContactId { get; private set; }
    public Guid PipelineStageId { get; private set; }
    public string Title { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateTime? ExpectedCloseUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Lead? SourceLead { get; private set; }
    public CustomerCompany? CustomerCompany { get; private set; }
    public Contact? PrimaryContact { get; private set; }
    public SalesPipelineStage PipelineStage { get; private set; } = null!;
    public ICollection<SalesActivity> Activities { get; } = new List<SalesActivity>();
    public ICollection<SalesAgentRecommendation> Recommendations { get; } = new List<SalesAgentRecommendation>();
    public ICollection<DealIntelligenceSignal> IntelligenceSignals { get; } = new List<DealIntelligenceSignal>();

    public void ChangeStage(Guid pipelineStageId)
    {
        if (Status is SalesStatuses.Won or SalesStatuses.Lost)
        {
            throw new InvalidOperationException("Closed deals cannot change stage.");
        }

        PipelineStageId = pipelineStageId == Guid.Empty ? throw new ArgumentException("PipelineStageId is required.", nameof(pipelineStageId)) : pipelineStageId;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkWon()
    {
        if (Status == SalesStatuses.Lost)
        {
            throw new InvalidOperationException("Lost deals cannot be marked won.");
        }

        Status = SalesStatuses.Won;
        PipelineStageId = SalesPipelineStage.WonStageId;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkLost()
    {
        Status = SalesStatuses.Lost;
        PipelineStageId = SalesPipelineStage.LostStageId;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SoftDelete()
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedUtc = DateTime.UtcNow;
        UpdatedUtc = DeletedUtc.Value;
    }
}

