namespace VirtualCompany.Domain.Entities;
public sealed class Lead : ICompanyOwnedEntity
{
    private Lead()
    {
    }

    public Lead(
        Guid id,
        Guid companyId,
        string title,
        Guid pipelineStageId,
        string status = SalesStatuses.Open,
        Guid? primaryContactId = null,
        Guid? customerCompanyId = null,
        decimal? estimatedValue = null,
        string? currency = null,
        string? source = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Title = SalesEntityText.NormalizeRequired(title, nameof(title), 200);
        PipelineStageId = pipelineStageId == Guid.Empty ? throw new ArgumentException("PipelineStageId is required.", nameof(pipelineStageId)) : pipelineStageId;
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        PrimaryContactId = SalesEntityText.NormalizeOptionalId(primaryContactId, nameof(primaryContactId));
        CustomerCompanyId = SalesEntityText.NormalizeOptionalId(customerCompanyId, nameof(customerCompanyId));
        EstimatedValue = estimatedValue;
        Currency = SalesEntityText.NormalizeOptional(currency, nameof(currency), 3)?.ToUpperInvariant();
        Source = SalesEntityText.NormalizeOptional(source, nameof(source), 120);
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? PrimaryContactId { get; private set; }
    public Guid? CustomerCompanyId { get; private set; }
    public Guid PipelineStageId { get; private set; }
    public Guid? ConvertedDealId { get; private set; }
    public string Title { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public decimal? EstimatedValue { get; private set; }
    public string? Currency { get; private set; }
    public string? Source { get; private set; }
    public string? Fit { get; private set; }
    public string? Temperature { get; private set; }
    public string? Priority { get; private set; }
    public string? SuggestedNextAction { get; private set; }
    public DateTime? QualifiedUtc { get; private set; }
    public string? WebsiteSubmissionEmail { get; private set; }
    public Guid? WebsiteLeadSubmissionId { get; private set; }
    public Guid? QualifiedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Contact? PrimaryContact { get; private set; }
    public CustomerCompany? CustomerCompany { get; private set; }
    public SalesPipelineStage PipelineStage { get; private set; } = null!;
    public Deal? ConvertedDeal { get; private set; }
    public ICollection<Deal> Deals { get; } = new List<Deal>();
    public ICollection<SalesActivity> Activities { get; } = new List<SalesActivity>();
    public ICollection<SalesAgentRecommendation> Recommendations { get; } = new List<SalesAgentRecommendation>();

    public void Qualify(string? fit = null, string? temperature = null, string? priority = null, string? suggestedNextAction = null, Guid? qualifiedByUserId = null)
    {
        if (Status is SalesStatuses.Converted or SalesStatuses.Rejected)
        {
            throw new InvalidOperationException("Only open leads can be qualified.");
        }

        Status = SalesStatuses.Qualified;
        PipelineStageId = SalesPipelineStage.QualifiedStageId;
        Fit = SalesEntityText.NormalizeOptional(fit, nameof(fit), 80) ?? Fit;
        Temperature = SalesEntityText.NormalizeOptional(temperature, nameof(temperature), 32)?.ToLowerInvariant() ?? Temperature;
        Priority = SalesEntityText.NormalizeOptional(priority, nameof(priority), 32)?.ToLowerInvariant() ?? Priority;
        SuggestedNextAction = SalesEntityText.NormalizeOptional(suggestedNextAction, nameof(suggestedNextAction), 500) ?? SuggestedNextAction;
        QualifiedUtc = DateTime.UtcNow;
        QualifiedByUserId = qualifiedByUserId;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Reject()
    {
        if (Status == SalesStatuses.Converted)
        {
            throw new InvalidOperationException("Converted leads cannot be rejected.");
        }

        Status = SalesStatuses.Rejected;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void ConvertToDeal(Guid dealId)
    {
        if (Status != SalesStatuses.Qualified)
        {
            throw new InvalidOperationException("Only qualified leads can be converted to a deal.");
        }

        ConvertedDealId = dealId == Guid.Empty ? throw new ArgumentException("DealId is required.", nameof(dealId)) : dealId;
        Status = SalesStatuses.Converted;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void ApplyEmailSignal(
        string title,
        Guid? primaryContactId,
        Guid? customerCompanyId,
        decimal confidence,
        string? source)
    {
        if (!string.IsNullOrWhiteSpace(title) &&
            (string.IsNullOrWhiteSpace(Title) || confidence >= 0.65m))
        {
            Title = SalesEntityText.NormalizeRequired(title, nameof(title), 200);
        }

        if (!PrimaryContactId.HasValue && primaryContactId.HasValue)
        {
            PrimaryContactId = SalesEntityText.NormalizeOptionalId(primaryContactId, nameof(primaryContactId));
        }

        if (!CustomerCompanyId.HasValue && customerCompanyId.HasValue)
        {
            CustomerCompanyId = SalesEntityText.NormalizeOptionalId(customerCompanyId, nameof(customerCompanyId));
        }

        if (string.IsNullOrWhiteSpace(Source) && !string.IsNullOrWhiteSpace(source))
        {
            Source = SalesEntityText.NormalizeOptional(source, nameof(source), 120);
        }

        UpdatedUtc = DateTime.UtcNow;
    }

    public void ApplyWebsiteSubmission(Guid submissionId, string normalizedEmail, string? message)
    {
        WebsiteLeadSubmissionId = submissionId == Guid.Empty ? throw new ArgumentException("SubmissionId is required.", nameof(submissionId)) : submissionId;
        WebsiteSubmissionEmail = SalesEntityText.NormalizeRequired(normalizedEmail, nameof(normalizedEmail), 256).ToLowerInvariant();
        SuggestedNextAction = SalesEntityText.NormalizeOptional(message, nameof(message), 500) ?? SuggestedNextAction ?? "Review website enquiry";
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

