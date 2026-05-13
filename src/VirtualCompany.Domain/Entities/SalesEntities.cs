namespace VirtualCompany.Domain.Entities;

public sealed class SalesPipelineStage : ICompanyOwnedEntity
{
    public static readonly Guid NewStageId = Guid.Parse("6d305bcb-3d87-40b0-a89d-bbe48b3f1891");
    public static readonly Guid QualifiedStageId = Guid.Parse("a7c6f0bf-2136-46a5-a82b-73506f91b79a");
    public static readonly Guid ProposalStageId = Guid.Parse("62e3f3e1-bfc3-4cf7-a24a-92d216d8d859");
    public static readonly Guid WonStageId = Guid.Parse("cbad0a5d-d5da-4c8e-a414-6fa5ce7d6f43");
    public static readonly Guid LostStageId = Guid.Parse("5c449a94-81b8-4edc-a0d6-8b42dd8ce47a");
    public static readonly Guid SystemCompanyId = Guid.Empty;

    private SalesPipelineStage()
    {
    }

    public SalesPipelineStage(
        Guid id,
        Guid companyId,
        string name,
        int displayOrder,
        bool isSystem = false,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        if (!isSystem && companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required for tenant pipeline stages.", nameof(companyId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = isSystem ? SystemCompanyId : companyId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 80);
        DisplayOrder = displayOrder;
        IsSystem = isSystem;
        IsActive = true;
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public int DisplayOrder { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedUtc { get; private set; }
    public ICollection<Lead> Leads { get; } = new List<Lead>();
    public ICollection<Deal> Deals { get; } = new List<Deal>();

    public void Rename(string name, int displayOrder)
    {
        if (IsSystem)
        {
            throw new InvalidOperationException("System pipeline stages cannot be changed.");
        }

        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 80);
        DisplayOrder = displayOrder;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException("System pipeline stages cannot be deactivated.");
        }

        IsActive = false;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SoftDelete()
    {
        if (IsSystem)
        {
            throw new InvalidOperationException("System pipeline stages cannot be deleted.");
        }

        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedUtc = DateTime.UtcNow;
        UpdatedUtc = DeletedUtc.Value;
    }
}

public sealed class CustomerCompany : ICompanyOwnedEntity
{
    private CustomerCompany()
    {
    }

    public CustomerCompany(
        Guid id,
        Guid companyId,
        string name,
        string status = SalesStatuses.Active,
        string? website = null,
        string? industry = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 200);
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        Website = SalesEntityText.NormalizeOptional(website, nameof(website), 256);
        Industry = SalesEntityText.NormalizeOptional(industry, nameof(industry), 120);
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? Website { get; private set; }
    public string? Industry { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<Contact> Contacts { get; } = new List<Contact>();
    public ICollection<Lead> Leads { get; } = new List<Lead>();
    public ICollection<Deal> Deals { get; } = new List<Deal>();

    public void Update(string name, string status, string? website, string? industry)
    {
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 200);
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        Website = SalesEntityText.NormalizeOptional(website, nameof(website), 256);
        Industry = SalesEntityText.NormalizeOptional(industry, nameof(industry), 120);
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

public sealed class Contact : ICompanyOwnedEntity
{
    private Contact()
    {
    }

    public Contact(
        Guid id,
        Guid companyId,
        string fullName,
        string email,
        Guid? customerCompanyId = null,
        string status = SalesStatuses.Active,
        string? title = null,
        string? phone = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CustomerCompanyId = SalesEntityText.NormalizeOptionalId(customerCompanyId, nameof(customerCompanyId));
        FullName = SalesEntityText.NormalizeRequired(fullName, nameof(fullName), 160);
        Email = SalesEntityText.NormalizeRequired(email, nameof(email), 256).ToLowerInvariant();
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        Title = SalesEntityText.NormalizeOptional(title, nameof(title), 120);
        Phone = SalesEntityText.NormalizeOptional(phone, nameof(phone), 64);
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? CustomerCompanyId { get; private set; }
    public string FullName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? Title { get; private set; }
    public string? Phone { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public CustomerCompany? CustomerCompany { get; private set; }

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

public sealed class RevenueForecastSnapshot : ICompanyOwnedEntity
{
    private RevenueForecastSnapshot()
    {
    }

    public RevenueForecastSnapshot(
        Guid id,
        Guid companyId,
        DateTime asOfUtc,
        string currency,
        decimal grossPipeline30Days,
        decimal expectedRevenue30Days,
        int dealCount30Days,
        decimal grossPipeline60Days,
        decimal expectedRevenue60Days,
        int dealCount60Days,
        decimal grossPipeline90Days,
        decimal expectedRevenue90Days,
        int dealCount90Days,
        int unknownRiskDeals,
        int lowRiskDeals,
        int mediumRiskDeals,
        int highRiskDeals,
        DateTime calculatedUtc)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AsOfUtc = SalesEntityText.NormalizeUtc(asOfUtc, nameof(asOfUtc));
        Currency = SalesEntityText.NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        GrossPipeline30Days = grossPipeline30Days;
        ExpectedRevenue30Days = expectedRevenue30Days;
        DealCount30Days = dealCount30Days;
        GrossPipeline60Days = grossPipeline60Days;
        ExpectedRevenue60Days = expectedRevenue60Days;
        DealCount60Days = dealCount60Days;
        GrossPipeline90Days = grossPipeline90Days;
        ExpectedRevenue90Days = expectedRevenue90Days;
        DealCount90Days = dealCount90Days;
        UnknownRiskDeals = unknownRiskDeals;
        LowRiskDeals = lowRiskDeals;
        MediumRiskDeals = mediumRiskDeals;
        HighRiskDeals = highRiskDeals;
        CalculatedUtc = SalesEntityText.NormalizeUtc(calculatedUtc, nameof(calculatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public DateTime AsOfUtc { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal GrossPipeline30Days { get; private set; }
    public decimal ExpectedRevenue30Days { get; private set; }
    public int DealCount30Days { get; private set; }
    public decimal GrossPipeline60Days { get; private set; }
    public decimal ExpectedRevenue60Days { get; private set; }
    public int DealCount60Days { get; private set; }
    public decimal GrossPipeline90Days { get; private set; }
    public decimal ExpectedRevenue90Days { get; private set; }
    public int DealCount90Days { get; private set; }
    public int UnknownRiskDeals { get; private set; }
    public int LowRiskDeals { get; private set; }
    public int MediumRiskDeals { get; private set; }
    public int HighRiskDeals { get; private set; }
    public DateTime CalculatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
}

public sealed class DealRiskScoreSnapshot : ICompanyOwnedEntity
{
    private DealRiskScoreSnapshot()
    {
    }

    public DealRiskScoreSnapshot(
        Guid id,
        Guid companyId,
        Guid dealId,
        DateTime scoreDateUtc,
        decimal score,
        string band,
        string factorsSummary,
        DateTime calculatedUtc)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DealId = SalesEntityText.NormalizeOptionalId(dealId, nameof(dealId))!.Value;
        ScoreDateUtc = SalesEntityText.NormalizeUtc(scoreDateUtc, nameof(scoreDateUtc)).Date;
        Score = ClampScore(score);
        Band = SalesEntityText.NormalizeRequired(band, nameof(band), 32).ToLowerInvariant();
        FactorsSummary = SalesEntityText.NormalizeRequired(factorsSummary, nameof(factorsSummary), 1000);
        CalculatedUtc = SalesEntityText.NormalizeUtc(calculatedUtc, nameof(calculatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DealId { get; private set; }
    public DateTime ScoreDateUtc { get; private set; }
    public decimal Score { get; private set; }
    public string Band { get; private set; } = null!;
    public string FactorsSummary { get; private set; } = null!;
    public DateTime CalculatedUtc { get; private set; }
    public Deal Deal { get; private set; } = null!;

    public void Recalculate(decimal score, string band, string factorsSummary, DateTime calculatedUtc)
    {
        Score = ClampScore(score);
        Band = SalesEntityText.NormalizeRequired(band, nameof(band), 32).ToLowerInvariant();
        FactorsSummary = SalesEntityText.NormalizeRequired(factorsSummary, nameof(factorsSummary), 1000);
        CalculatedUtc = SalesEntityText.NormalizeUtc(calculatedUtc, nameof(calculatedUtc));
    }

    private static decimal ClampScore(decimal score) =>
        Math.Round(Math.Clamp(score, 0m, 1m), 4, MidpointRounding.AwayFromZero);
}

public sealed class SalesActivity : ICompanyOwnedEntity
{
    private SalesActivity()
    {
    }

    public SalesActivity(
        Guid id,
        Guid companyId,
        string activityType,
        string summary,
        DateTime occurredUtc,
        Guid? leadId = null,
        Guid? dealId = null,
        Guid? contactId = null,
        Guid? customerCompanyId = null,
        string status = SalesStatuses.Completed,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ActivityType = SalesEntityText.NormalizeRequired(activityType, nameof(activityType), 64).ToLowerInvariant();
        Summary = SalesEntityText.NormalizeRequired(summary, nameof(summary), 500);
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        OccurredUtc = SalesEntityText.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        LeadId = SalesEntityText.NormalizeOptionalId(leadId, nameof(leadId));
        DealId = SalesEntityText.NormalizeOptionalId(dealId, nameof(dealId));
        ContactId = SalesEntityText.NormalizeOptionalId(contactId, nameof(contactId));
        CustomerCompanyId = SalesEntityText.NormalizeOptionalId(customerCompanyId, nameof(customerCompanyId));
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? LeadId { get; private set; }
    public Guid? DealId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? CustomerCompanyId { get; private set; }
    public string ActivityType { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateTime OccurredUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Lead? Lead { get; private set; }
    public Deal? Deal { get; private set; }
    public Contact? Contact { get; private set; }
    public CustomerCompany? CustomerCompany { get; private set; }
}

public sealed class SalesAgentRecommendation : ICompanyOwnedEntity
{
    private SalesAgentRecommendation()
    {
    }

    public SalesAgentRecommendation(
        Guid id,
        Guid companyId,
        string recommendation,
        string rationale,
        Guid? leadId = null,
        Guid? dealId = null,
        string status = SalesStatuses.Open,
        string category = "follow_up",
        string triggerCondition = "manual_review",
        string actionType = "create_draft_reply",
        string riskLevel = "medium",
        bool requiresApproval = true,
        string approvalStatus = SalesStatuses.WaitingForApproval,
        string executionStatus = SalesStatuses.Pending,
        string? dedupeKey = null,
        decimal? confidence = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        LeadId = SalesEntityText.NormalizeOptionalId(leadId, nameof(leadId));
        DealId = SalesEntityText.NormalizeOptionalId(dealId, nameof(dealId));
        Recommendation = SalesEntityText.NormalizeRequired(recommendation, nameof(recommendation), 1000);
        Rationale = SalesEntityText.NormalizeRequired(rationale, nameof(rationale), 2000);
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        Category = SalesEntityText.NormalizeRequired(category, nameof(category), 64).ToLowerInvariant();
        TriggerCondition = SalesEntityText.NormalizeRequired(triggerCondition, nameof(triggerCondition), 80).ToLowerInvariant();
        ActionType = SalesEntityText.NormalizeRequired(actionType, nameof(actionType), 80).ToLowerInvariant();
        RiskLevel = SalesEntityText.NormalizeRequired(riskLevel, nameof(riskLevel), 32).ToLowerInvariant();
        RequiresApproval = requiresApproval;
        ApprovalStatus = SalesEntityText.NormalizeRequired(approvalStatus, nameof(approvalStatus), 32).ToLowerInvariant();
        ExecutionStatus = SalesEntityText.NormalizeRequired(executionStatus, nameof(executionStatus), 32).ToLowerInvariant();
        DedupeKey = SalesEntityText.NormalizeOptional(dedupeKey, nameof(dedupeKey), 256);
        Confidence = confidence;
        CreatedUtc = DateTime.UtcNow;
        ExecutionIdempotencyKey = $"sales-recommendation:{CompanyId:N}:{Id:N}:{ActionType}";
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? LeadId { get; private set; }
    public Guid? DealId { get; private set; }
    public string Recommendation { get; private set; } = null!;
    public string Rationale { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public string TriggerCondition { get; private set; } = null!;
    public string ActionType { get; private set; } = null!;
    public string RiskLevel { get; private set; } = null!;
    public bool RequiresApproval { get; private set; }
    public string ApprovalStatus { get; private set; } = null!;
    public string ExecutionStatus { get; private set; } = null!;
    public string? FailureSummary { get; private set; }
    public string? DedupeKey { get; private set; }
    public decimal? Confidence { get; private set; }
    public string Status { get; private set; } = null!;
    public int ExecutionAttemptCount { get; private set; }
    public string? LastExecutionErrorCode { get; private set; }
    public string? Provider { get; private set; }
    public Guid? MailboxConnectionId { get; private set; }
    public string? ProviderThreadId { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? ProviderDraftId { get; private set; }
    public Guid? ActivityId { get; private set; }
    public string ExecutionIdempotencyKey { get; private set; } = null!;
    public DateTime? ExecutedUtc { get; private set; }
    public bool CanRetryExecution => ExecutionStatus == SalesStatuses.RetryableFailed;
    public bool HasSucceeded => ExecutionStatus == SalesStatuses.Completed || ExecutionStatus == SalesStatuses.DraftCreated;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Lead? Lead { get; private set; }
    public Deal? Deal { get; private set; }
    public ICollection<SalesActionApproval> Approvals { get; } = new List<SalesActionApproval>();

    public void MarkApproved()
    {
        ApprovalStatus = SalesStatuses.Approved;
        Status = SalesStatuses.Approved;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkExecuting(Guid mailboxConnectionId, string provider, string? providerThreadId)
    {
        if (ApprovalStatus != SalesStatuses.Approved)
        {
            throw new InvalidOperationException("Recommendation must be approved before execution.");
        }

        if (HasSucceeded)
        {
            return;
        }

        ExecutionStatus = SalesStatuses.InProgress;
        ExecutionAttemptCount++;
        MailboxConnectionId = SalesEntityText.NormalizeOptionalId(mailboxConnectionId, nameof(mailboxConnectionId));
        Provider = SalesEntityText.NormalizeOptional(provider, nameof(provider), 64);
        ProviderThreadId = SalesEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256) ?? ProviderThreadId;
        LastExecutionErrorCode = null;
        FailureSummary = null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkDraftCreated(string providerDraftId, string? providerThreadId, Guid activityId)
    {
        if (HasSucceeded)
        {
            return;
        }

        ExecutionStatus = SalesStatuses.DraftCreated;
        Status = SalesStatuses.Completed;
        ProviderDraftId = SalesEntityText.NormalizeRequired(providerDraftId, nameof(providerDraftId), 256);
        ProviderThreadId = SalesEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256) ?? ProviderThreadId;
        ActivityId = SalesEntityText.NormalizeOptionalId(activityId, nameof(activityId));
        ExecutedUtc = DateTime.UtcNow;
        LastExecutionErrorCode = null;
        FailureSummary = null;
        UpdatedUtc = ExecutedUtc.Value;
    }

    public void MarkSent(string providerMessageId, string? providerThreadId, Guid activityId)
    {
        if (HasSucceeded)
        {
            return;
        }

        ExecutionStatus = SalesStatuses.Completed;
        Status = SalesStatuses.Completed;
        ProviderMessageId = SalesEntityText.NormalizeRequired(providerMessageId, nameof(providerMessageId), 256);
        ProviderThreadId = SalesEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256) ?? ProviderThreadId;
        ActivityId = SalesEntityText.NormalizeOptionalId(activityId, nameof(activityId));
        ExecutedUtc = DateTime.UtcNow;
        LastExecutionErrorCode = null;
        FailureSummary = null;
        UpdatedUtc = ExecutedUtc.Value;
    }

    public void MarkFailed(string errorCode, string failureSummary, bool retryable)
    {
        ExecutionStatus = retryable ? SalesStatuses.RetryableFailed : SalesStatuses.Failed;
        Status = SalesStatuses.Failed;
        LastExecutionErrorCode = SalesEntityText.NormalizeOptional(errorCode, nameof(errorCode), 120);
        FailureSummary = SalesEntityText.NormalizeOptional(failureSummary, nameof(failureSummary), 1000);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkRetrying()
    {
        if (!CanRetryExecution)
        {
            throw new InvalidOperationException("Only retryable failed recommendation executions can be retried.");
        }

        ExecutionStatus = SalesStatuses.InProgress;
        Status = SalesStatuses.Open;
        LastExecutionErrorCode = null;
        FailureSummary = null;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void EnsureExecutionKey()
    {
        ExecutionIdempotencyKey = SalesEntityText.NormalizeOptional(ExecutionIdempotencyKey, nameof(ExecutionIdempotencyKey), 256)
            ?? $"sales-recommendation:{CompanyId:N}:{Id:N}:{ActionType}";
    }
}

public sealed class SalesSequence : ICompanyOwnedEntity
{
    private SalesSequence()
    {
    }

    public SalesSequence(
        Guid id,
        Guid companyId,
        string name,
        string status = SalesStatuses.Draft,
        string? description = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 160);
        Description = SalesEntityText.NormalizeOptional(description, nameof(description), 1000);
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<SalesSequenceStep> Steps { get; } = new List<SalesSequenceStep>();
    public bool HasEnoughSteps => Steps.Count >= 4;
    public ICollection<SalesCampaign> Campaigns { get; } = new List<SalesCampaign>();

    public void Update(string name, string? description, string status)
    {
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 160);
        Description = SalesEntityText.NormalizeOptional(description, nameof(description), 1000);
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Activate()
    {
        Status = SalesStatuses.Active;
        UpdatedUtc = DateTime.UtcNow;
    }
}

public sealed class SalesSequenceStep : ICompanyOwnedEntity
{
    private SalesSequenceStep()
    {
    }

    public SalesSequenceStep(
        Guid id,
        Guid companyId,
        Guid salesSequenceId,
        int stepOrder,
        int delayDays,
        string templateContent,
        string channel = "email",
        string? templateSubject = null,
        bool aiPersonalizationEnabled = true,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (stepOrder <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepOrder), "Step order must be positive.");
        }

        if (delayDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayDays), "Delay days cannot be negative.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesSequenceId = salesSequenceId == Guid.Empty ? throw new ArgumentException("SalesSequenceId is required.", nameof(salesSequenceId)) : salesSequenceId;
        StepOrder = stepOrder;
        DelayDays = delayDays;
        Channel = SalesEntityText.NormalizeRequired(channel, nameof(channel), 32).ToLowerInvariant();
        TemplateSubject = SalesEntityText.NormalizeOptional(templateSubject, nameof(templateSubject), 300);
        TemplateContent = SalesEntityText.NormalizeRequired(templateContent, nameof(templateContent), 8000);
        AiPersonalizationEnabled = aiPersonalizationEnabled;
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesSequenceId { get; private set; }
    public int StepOrder { get; private set; }
    public int DelayDays { get; private set; }
    public string Channel { get; private set; } = null!;
    public string? TemplateSubject { get; private set; }
    public string TemplateContent { get; private set; } = null!;
    public bool AiPersonalizationEnabled { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SalesSequence SalesSequence { get; private set; } = null!;

    public void Update(int stepOrder, int delayDays, string channel, string? templateSubject, string templateContent, bool aiPersonalizationEnabled)
    {
        if (stepOrder <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepOrder), "Step order must be positive.");
        }

        if (delayDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delayDays), "Delay days cannot be negative.");
        }

        StepOrder = stepOrder;
        DelayDays = delayDays;
        Channel = SalesEntityText.NormalizeRequired(channel, nameof(channel), 32).ToLowerInvariant();
        TemplateSubject = SalesEntityText.NormalizeOptional(templateSubject, nameof(templateSubject), 300);
        TemplateContent = SalesEntityText.NormalizeRequired(templateContent, nameof(templateContent), 8000);
        AiPersonalizationEnabled = aiPersonalizationEnabled;
        UpdatedUtc = DateTime.UtcNow;
    }
}

public sealed class SalesCampaign : ICompanyOwnedEntity
{
    private SalesCampaign()
    {
    }

    public SalesCampaign(
        Guid id,
        Guid companyId,
        Guid salesSequenceId,
        string name,
        string audienceType,
        string status = SalesStatuses.Draft,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesSequenceId = salesSequenceId == Guid.Empty ? throw new ArgumentException("SalesSequenceId is required.", nameof(salesSequenceId)) : salesSequenceId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 160);
        AudienceType = SalesEntityText.NormalizeRequired(audienceType, nameof(audienceType), 64).ToLowerInvariant();
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesSequenceId { get; private set; }
    public string Name { get; private set; } = null!;
    public string AudienceType { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public bool OutboundEnabled { get; private set; } = true;
    public int MaxEmailsPerDay { get; private set; } = 50;
    public bool ApprovalRequired { get; private set; }
    public DateTime? ApprovalRequestedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public string? ApprovalStatus { get; private set; }
    public DateTime? LaunchRequestedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? PausedUtc { get; private set; }
    public DateTime? StoppedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public SalesSequence SalesSequence { get; private set; } = null!;
    public ICollection<SalesCampaignContact> Contacts { get; } = new List<SalesCampaignContact>();

    public void SetPolicy(bool outboundEnabled, int maxEmailsPerDay, bool approvalRequired)
    {
        if (maxEmailsPerDay <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEmailsPerDay), "Max emails per day must be greater than zero.");
        }

        OutboundEnabled = outboundEnabled;
        MaxEmailsPerDay = maxEmailsPerDay;
        ApprovalRequired = approvalRequired;
        ApprovalStatus = approvalRequired ? SalesStatuses.WaitingForApproval : SalesStatuses.Approved;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void RequestLaunch()
    {
        if (!OutboundEnabled)
        {
            throw new InvalidOperationException("Outbound email is disabled for this company.");
        }

        LaunchRequestedUtc = DateTime.UtcNow;
        if (ApprovalRequired && ApprovedUtc is null)
        {
            Status = SalesStatuses.WaitingForApproval;
            ApprovalRequestedUtc ??= LaunchRequestedUtc;
            ApprovalStatus = SalesStatuses.WaitingForApproval;
        }
        else
        {
            Status = SalesStatuses.Active;
            StartedUtc ??= LaunchRequestedUtc;
            ApprovalStatus = SalesStatuses.Approved;
        }

        UpdatedUtc = LaunchRequestedUtc.Value;
    }

    public void ApproveLaunch()
    {
        ApprovedUtc = DateTime.UtcNow;
        ApprovalStatus = SalesStatuses.Approved;
        Status = SalesStatuses.Active;
        StartedUtc ??= ApprovedUtc;
        UpdatedUtc = ApprovedUtc.Value;
    }

    public void Pause()
    {
        if (Status is SalesStatuses.Stopped or SalesStatuses.Completed)
        {
            return;
        }

        Status = SalesStatuses.Paused;
        PausedUtc = DateTime.UtcNow;
        UpdatedUtc = PausedUtc.Value;
    }

    public void Stop()
    {
        if (Status == SalesStatuses.Stopped)
        {
            return;
        }

        Status = SalesStatuses.Stopped;
        StoppedUtc = DateTime.UtcNow;
        UpdatedUtc = StoppedUtc.Value;
    }
}

public sealed class SalesCampaignContact : ICompanyOwnedEntity
{
    private SalesCampaignContact()
    {
    }

    public SalesCampaignContact(
        Guid id,
        Guid companyId,
        Guid salesCampaignId,
        Guid contactId,
        string status = SalesStatuses.Pending,
        int? currentStepOrder = null,
        DateTime? enrolledUtc = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        if (currentStepOrder.HasValue && currentStepOrder.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentStepOrder), "Current step order must be positive when set.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesCampaignId = salesCampaignId == Guid.Empty ? throw new ArgumentException("SalesCampaignId is required.", nameof(salesCampaignId)) : salesCampaignId;
        ContactId = contactId == Guid.Empty ? throw new ArgumentException("ContactId is required.", nameof(contactId)) : contactId;
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        CurrentStepOrder = currentStepOrder;
        EnrolledUtc = SalesEntityText.NormalizeUtc(enrolledUtc ?? DateTime.UtcNow, nameof(enrolledUtc));
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? EnrolledUtc, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Status { get; private set; } = null!;
    public int? CurrentStepOrder { get; private set; }
    public DateTime EnrolledUtc { get; private set; }
    public DateTime? LastScheduledUtc { get; private set; }
    public DateTime? LastSentUtc { get; private set; }
    public DateTime? CancelledUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SalesCampaign SalesCampaign { get; private set; } = null!;
    public Contact Contact { get; private set; } = null!;

    public void MarkScheduled(int stepOrder, DateTime scheduledUtc)
    {
        CurrentStepOrder = stepOrder;
        LastScheduledUtc = SalesEntityText.NormalizeUtc(scheduledUtc, nameof(scheduledUtc));
        Status = SalesStatuses.InProgress;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkSent(int stepOrder, DateTime sentUtc)
    {
        CurrentStepOrder = stepOrder;
        LastSentUtc = SalesEntityText.NormalizeUtc(sentUtc, nameof(sentUtc));
        Status = SalesStatuses.InProgress;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkCancelled()
    {
        if (Status is SalesStatuses.Cancelled or SalesStatuses.Completed)
        {
            return;
        }

        Status = SalesStatuses.Cancelled;
        CancelledUtc = DateTime.UtcNow;
        UpdatedUtc = CancelledUtc.Value;
    }

    public void MarkCompleted()
    {
        Status = SalesStatuses.Completed;
        CompletedUtc = DateTime.UtcNow;
        UpdatedUtc = CompletedUtc.Value;
    }
}

public sealed class SalesSequenceExecution : ICompanyOwnedEntity
{
    private SalesSequenceExecution()
    {
    }

    public SalesSequenceExecution(Guid id, Guid companyId, Guid salesCampaignId, Guid campaignContactId, Guid contactId)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesCampaignId = salesCampaignId == Guid.Empty ? throw new ArgumentException("SalesCampaignId is required.", nameof(salesCampaignId)) : salesCampaignId;
        SalesCampaignContactId = campaignContactId == Guid.Empty ? throw new ArgumentException("Campaign contact is required.", nameof(campaignContactId)) : campaignContactId;
        ContactId = contactId == Guid.Empty ? throw new ArgumentException("ContactId is required.", nameof(contactId)) : contactId;
        Status = SalesStatuses.Pending;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public Guid SalesCampaignContactId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Status { get; private set; } = null!;
    public string? StopReason { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? StoppedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SalesCampaign SalesCampaign { get; private set; } = null!;
    public SalesCampaignContact SalesCampaignContact { get; private set; } = null!;
    public Contact Contact { get; private set; } = null!;
    public ICollection<SalesSequenceExecutionStep> Steps { get; } = new List<SalesSequenceExecutionStep>();

    public void MarkStarted()
    {
        if (Status == SalesStatuses.Pending)
        {
            Status = SalesStatuses.InProgress;
            StartedUtc = DateTime.UtcNow;
            UpdatedUtc = StartedUtc.Value;
        }
    }

    public void Stop(string reason)
    {
        if (Status is SalesStatuses.Stopped or SalesStatuses.Completed)
        {
            return;
        }

        Status = SalesStatuses.Stopped;
        StopReason = SalesEntityText.NormalizeRequired(reason, nameof(reason), 80).ToLowerInvariant();
        StoppedUtc = DateTime.UtcNow;
        UpdatedUtc = StoppedUtc.Value;
    }

    public void Complete()
    {
        Status = SalesStatuses.Completed;
        CompletedUtc = DateTime.UtcNow;
        UpdatedUtc = CompletedUtc.Value;
    }
}

public sealed partial class SalesSequenceExecutionStep : ICompanyOwnedEntity
{
    private SalesSequenceExecutionStep()
    {
    }

    public SalesSequenceExecutionStep(Guid id, Guid companyId, Guid sequenceExecutionId, Guid salesCampaignId, Guid contactId, Guid salesSequenceStepId, int stepOrder, DateTime scheduledSendUtc, string idempotencyKey)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SequenceExecutionId = sequenceExecutionId == Guid.Empty ? throw new ArgumentException("SequenceExecutionId is required.", nameof(sequenceExecutionId)) : sequenceExecutionId;
        SalesCampaignId = salesCampaignId == Guid.Empty ? throw new ArgumentException("SalesCampaignId is required.", nameof(salesCampaignId)) : salesCampaignId;
        ContactId = contactId == Guid.Empty ? throw new ArgumentException("ContactId is required.", nameof(contactId)) : contactId;
        SalesSequenceStepId = salesSequenceStepId == Guid.Empty ? throw new ArgumentException("SalesSequenceStepId is required.", nameof(salesSequenceStepId)) : salesSequenceStepId;
        StepOrder = stepOrder <= 0 ? throw new ArgumentOutOfRangeException(nameof(stepOrder), "Step order must be positive.") : stepOrder;
        ScheduledSendUtc = SalesEntityText.NormalizeUtc(scheduledSendUtc, nameof(scheduledSendUtc));
        Status = SalesStatuses.Pending;
        DeliveryStatus = SalesStatuses.Pending;
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 256);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SequenceExecutionId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public Guid ContactId { get; private set; }
    public Guid SalesSequenceStepId { get; private set; }
    public int StepOrder { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTime ScheduledSendUtc { get; private set; }
    public DateTime? SentUtc { get; private set; }
    public DateTime? CancelledUtc { get; private set; }
    public string DeliveryStatus { get; private set; } = null!;
    public string? BounceStatus { get; private set; }
    public string? BounceReason { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? CancellationSourceReference { get; private set; }
    public string? Provider { get; private set; }
    public Guid? MailboxConnectionId { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? ProviderThreadId { get; private set; }
    public string? InternetMessageId { get; private set; }
    public string? OriginalGeneratedSubject { get; private set; }
    public string? OriginalGeneratedBody { get; private set; }
    public string? CurrentDraftSubject { get; private set; }
    public string? CurrentDraftBody { get; private set; }
    public string? FinalSentSubject { get; private set; }
    public string? FinalSentBody { get; private set; }
    public DateTime? GeneratedDraftUtc { get; private set; }
    public DateTime? DraftUpdatedUtc { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SalesSequenceExecution SequenceExecution { get; private set; } = null!;
    public SalesSequenceStep SalesSequenceStep { get; private set; } = null!;

    public void RecordGeneratedDraft(string subject, string body, DateTime generatedUtc)
    {
        var effectiveGeneratedUtc = SalesEntityText.NormalizeUtc(generatedUtc, nameof(generatedUtc));
        var normalizedSubject = SalesEntityText.NormalizeRequired(subject, nameof(subject), 300);
        var normalizedBody = SalesEntityText.NormalizeRequired(body, nameof(body), 16000);

        OriginalGeneratedSubject = string.IsNullOrWhiteSpace(OriginalGeneratedSubject) ? normalizedSubject : OriginalGeneratedSubject;
        OriginalGeneratedBody = string.IsNullOrWhiteSpace(OriginalGeneratedBody) ? normalizedBody : OriginalGeneratedBody;
        CurrentDraftSubject = normalizedSubject;
        CurrentDraftBody = normalizedBody;
        GeneratedDraftUtc ??= effectiveGeneratedUtc;
        DraftUpdatedUtc = effectiveGeneratedUtc;
        UpdatedUtc = effectiveGeneratedUtc;
    }

    public void UpdateDraftContent(string subject, string body, DateTime updatedUtc)
    {
        CurrentDraftSubject = SalesEntityText.NormalizeRequired(subject, nameof(subject), 300);
        CurrentDraftBody = SalesEntityText.NormalizeRequired(body, nameof(body), 16000);
        DraftUpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        UpdatedUtc = DraftUpdatedUtc.Value;
    }

    public void MarkSending()
    {
        if (Status != SalesStatuses.Pending)
        {
            throw new InvalidOperationException("Only pending sequence steps can be sent.");
        }

        Status = SalesStatuses.InProgress;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkSent(string provider, Guid? mailboxConnectionId, string providerMessageId, string? providerThreadId, string? internetMessageId, string deliveryStatus, DateTime sentUtc, string? finalSubject = null, string? finalBody = null)
    {
        Provider = SalesEntityText.NormalizeRequired(provider, nameof(provider), 64);
        MailboxConnectionId = mailboxConnectionId;
        ProviderMessageId = SalesEntityText.NormalizeRequired(providerMessageId, nameof(providerMessageId), 256);
        ProviderThreadId = SalesEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256);
        InternetMessageId = SalesEntityText.NormalizeOptional(internetMessageId, nameof(internetMessageId), 512);
        DeliveryStatus = SalesEntityText.NormalizeRequired(deliveryStatus, nameof(deliveryStatus), 32).ToLowerInvariant();
        Status = SalesStatuses.Completed;
        FinalSentSubject = SalesEntityText.NormalizeOptional(finalSubject, nameof(finalSubject), 300) ?? CurrentDraftSubject ?? OriginalGeneratedSubject;
        FinalSentBody = SalesEntityText.NormalizeOptional(finalBody, nameof(finalBody), 16000) ?? CurrentDraftBody ?? OriginalGeneratedBody;
        SentUtc = SalesEntityText.NormalizeUtc(sentUtc, nameof(sentUtc));
        UpdatedUtc = SentUtc.Value;
    }

    public void Cancel(string? reason = null, string? sourceReference = null, DateTime? cancelledUtc = null)
    {
        if (Status is SalesStatuses.Completed or SalesStatuses.Cancelled)
        {
            return;
        }

        var effectiveCancelledUtc = SalesEntityText.NormalizeUtc(cancelledUtc ?? DateTime.UtcNow, nameof(cancelledUtc));
        Status = SalesStatuses.Cancelled;
        DeliveryStatus = SalesStatuses.Cancelled;
        CancelledUtc = effectiveCancelledUtc;
        CancellationReason = string.IsNullOrWhiteSpace(reason)
            ? CancellationReason
            : SalesEntityText.NormalizeOptional(reason, nameof(reason), 80)?.ToLowerInvariant();
        CancellationSourceReference = string.IsNullOrWhiteSpace(sourceReference)
            ? CancellationSourceReference
            : SalesEntityText.NormalizeOptional(sourceReference, nameof(sourceReference), 256);
        UpdatedUtc = effectiveCancelledUtc;
    }

    public void MarkDeliveryStatus(string deliveryStatus, DateTime occurredUtc)
    {
        DeliveryStatus = SalesEntityText.NormalizeRequired(deliveryStatus, nameof(deliveryStatus), 32).ToLowerInvariant();
        UpdatedUtc = SalesEntityText.NormalizeUtc(occurredUtc, nameof(occurredUtc));
    }

    public void MarkBounce(string bounceStatus, string? reason, DateTime occurredUtc)
    {
        BounceStatus = SalesEntityText.NormalizeRequired(bounceStatus, nameof(bounceStatus), 32).ToLowerInvariant();
        BounceReason = SalesEntityText.NormalizeOptional(reason, nameof(reason), 1000);
        DeliveryStatus = SalesStatuses.Bounced;
        UpdatedUtc = SalesEntityText.NormalizeUtc(occurredUtc, nameof(occurredUtc));
    }
}

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

public sealed class OutboundMessageReview : ICompanyOwnedEntity
{
    private OutboundMessageReview() { }
    public OutboundMessageReview(Guid id, Guid companyId, Guid sequenceExecutionStepId, Guid campaignId, Guid contactId, string category, string reasonCode, string reason, string subject, string body)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SequenceExecutionStepId = SalesEntityText.NormalizeOptionalId(sequenceExecutionStepId, nameof(sequenceExecutionStepId))!.Value;
        SalesCampaignId = SalesEntityText.NormalizeOptionalId(campaignId, nameof(campaignId))!.Value;
        ContactId = SalesEntityText.NormalizeOptionalId(contactId, nameof(contactId))!.Value;
        Category = SalesEntityText.NormalizeRequired(category, nameof(category), 64).ToLowerInvariant();
        ReasonCode = SalesEntityText.NormalizeRequired(reasonCode, nameof(reasonCode), 120).ToLowerInvariant();
        Reason = SalesEntityText.NormalizeRequired(reason, nameof(reason), 1000);
        OriginalSubject = SalesEntityText.NormalizeRequired(subject, nameof(subject), 300);
        OriginalBody = SalesEntityText.NormalizeRequired(body, nameof(body), 16000);
        Status = SalesStatuses.WaitingForApproval;
        RequestedUtc = DateTime.UtcNow;
        CreatedUtc = RequestedUtc;
        UpdatedUtc = CreatedUtc;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SequenceExecutionStepId { get; private set; }
    public Guid SalesCampaignId { get; private set; }
    public Guid ContactId { get; private set; }
    public string Category { get; private set; } = null!;
    public string ReasonCode { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public string OriginalSubject { get; private set; } = null!;
    public string OriginalBody { get; private set; } = null!;
    public string? EditedSubject { get; private set; }
    public string? EditedBody { get; private set; }
    public string Status { get; private set; } = null!;
    public Guid? DecidedByUserId { get; private set; }
    public DateTime? DecidedUtc { get; private set; }
    public string? DecisionComment { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public SalesSequenceExecutionStep SequenceExecutionStep { get; private set; } = null!;
    public Contact Contact { get; private set; } = null!;

    public void Approve(Guid userId, string? comment)
    {
        Decide(userId, SalesStatuses.Approved, comment);
    }

    public void Reject(Guid userId, string? comment)
    {
        Decide(userId, SalesStatuses.Rejected, comment);
    }

    public void EditAndApprove(Guid userId, string subject, string body, string? comment)
    {
        EditedSubject = SalesEntityText.NormalizeRequired(subject, nameof(subject), 300);
        EditedBody = SalesEntityText.NormalizeRequired(body, nameof(body), 16000);
        Decide(userId, SalesStatuses.Approved, comment);
    }

    private void Decide(Guid userId, string status, string? comment)
    {
        if (Status != SalesStatuses.WaitingForApproval)
        {
            throw new InvalidOperationException("This outbound message has already been reviewed.");
        }

        DecidedByUserId = SalesEntityText.NormalizeOptionalId(userId, nameof(userId))!.Value;
        Status = status;
        DecisionComment = SalesEntityText.NormalizeOptional(comment, nameof(comment), 1000);
        DecidedUtc = DateTime.UtcNow;
        UpdatedUtc = DecidedUtc.Value;
    }
}

public sealed class WebsiteLeadSubmission : ICompanyOwnedEntity
{
    private WebsiteLeadSubmission() { }

    public WebsiteLeadSubmission(
        Guid id,
        Guid companyId,
        string normalizedEmail,
        string? name,
        string? companyName,
        string? message,
        string? sourceUrl,
        string? formId,
        string? phone = null,
        string? externalSubmissionId = null,
        string? sourceMetadataJson = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        NormalizedEmail = SalesEntityText.NormalizeRequired(normalizedEmail, nameof(normalizedEmail), 256).ToLowerInvariant();
        Name = SalesEntityText.NormalizeOptional(name, nameof(name), 160);
        CompanyName = SalesEntityText.NormalizeOptional(companyName, nameof(companyName), 200);
        Message = SalesEntityText.NormalizeOptional(message, nameof(message), 2000);
        SourceUrl = SalesEntityText.NormalizeOptional(sourceUrl, nameof(sourceUrl), 512);
        FormId = SalesEntityText.NormalizeOptional(formId, nameof(formId), 120);
        Phone = SalesEntityText.NormalizeOptional(phone, nameof(phone), 64);
        ExternalSubmissionId = SalesEntityText.NormalizeOptional(externalSubmissionId, nameof(externalSubmissionId), 256);
        SourceMetadataJson = SalesEntityText.NormalizeOptional(sourceMetadataJson, nameof(sourceMetadataJson), 8000);
        DeduplicationDecision = "new";
        SequenceEnrollmentStatus = SalesStatuses.Pending;
        Status = SalesStatuses.Open;
        ReceivedUtc = DateTime.UtcNow;
        CreatedUtc = ReceivedUtc;
        UpdatedUtc = CreatedUtc;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? LeadId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? MergedIntoSubmissionId { get; private set; }
    public Guid? EnrollmentOutboxMessageId { get; private set; }
    public Guid? FollowUpSequenceId { get; private set; }
    public Guid? SequenceExecutionId { get; private set; }
    public string NormalizedEmail { get; private set; } = null!;
    public string? Name { get; private set; }
    public string? CompanyName { get; private set; }
    public string? Message { get; private set; }
    public string? SourceUrl { get; private set; }
    public string? FormId { get; private set; }
    public string? Phone { get; private set; }
    public string? ExternalSubmissionId { get; private set; }
    public string? SourceMetadataJson { get; private set; }
    public string DeduplicationDecision { get; private set; } = null!;
    public string SequenceEnrollmentStatus { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateTime ReceivedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Lead? Lead { get; private set; }

    public void LinkLead(Guid leadId, Guid contactId)
    {
        LeadId = SalesEntityText.NormalizeOptionalId(leadId, nameof(leadId))!.Value;
        ContactId = SalesEntityText.NormalizeOptionalId(contactId, nameof(contactId))!.Value;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkMerged(Guid targetSubmissionId)
    {
        MergedIntoSubmissionId = SalesEntityText.NormalizeOptionalId(targetSubmissionId, nameof(targetSubmissionId))!.Value;
        DeduplicationDecision = "merged";
        Status = "merged";
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkExistingLeadUpdated() => DeduplicationDecision = "updated_existing_lead";

    public void MarkEnrollmentQueued(Guid outboxMessageId)
    {
        EnrollmentOutboxMessageId = SalesEntityText.NormalizeOptionalId(outboxMessageId, nameof(outboxMessageId))!.Value;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void RecordSequenceEnrollment(Guid sequenceId, Guid sequenceExecutionId)
    {
        FollowUpSequenceId = SalesEntityText.NormalizeOptionalId(sequenceId, nameof(sequenceId))!.Value;
        SequenceExecutionId = SalesEntityText.NormalizeOptionalId(sequenceExecutionId, nameof(sequenceExecutionId))!.Value;
        SequenceEnrollmentStatus = "enrolled";
        UpdatedUtc = DateTime.UtcNow;
    }
}

public sealed class SalesFinanceHandoff : ICompanyOwnedEntity
{
    private SalesFinanceHandoff() { }

    public SalesFinanceHandoff(
        Guid id,
        Guid companyId,
        Guid dealId,
        string summary,
        string documentType,
        string dedupeKey,
        string idempotencyKey)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DealId = SalesEntityText.NormalizeOptionalId(dealId, nameof(dealId))!.Value;
        Summary = SalesEntityText.NormalizeRequired(summary, nameof(summary), 1000);
        DocumentType = NormalizeDocumentType(documentType);
        DedupeKey = SalesEntityText.NormalizeRequired(dedupeKey, nameof(dedupeKey), 256);
        IdempotencyKey = SalesEntityText.NormalizeRequired(idempotencyKey, nameof(idempotencyKey), 256);
        ExternalSystem = "Fortnox";
        Status = SalesStatuses.WaitingForApproval;
        ApprovalStatus = SalesStatuses.WaitingForApproval;
        ExecutionStatus = SalesStatuses.Pending;
        RequestedUtc = DateTime.UtcNow;
        CreatedUtc = RequestedUtc;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DealId { get; private set; }
    public string Status { get; private set; } = null!;
    public string ApprovalStatus { get; private set; } = null!;
    public string ExecutionStatus { get; private set; } = null!;
    public string DocumentType { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string DedupeKey { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public Guid? ApprovalId { get; private set; }
    public Guid? WriteRequestId { get; private set; }
    public string ExternalSystem { get; private set; } = null!;
    public string? ExternalDocumentId { get; private set; }
    public string? ExternalDocumentNumber { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public int ExecutionAttemptCount { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? ExecutionStartedUtc { get; private set; }
    public DateTime? ExecutedUtc { get; private set; }
    public DateTime? FailedUtc { get; private set; }
    public DateTime? RetriedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Deal Deal { get; private set; } = null!;

    public bool CanRetry => ExecutionStatus == SalesStatuses.RetryableFailed || ExecutionStatus == SalesStatuses.Failed;
    public bool HasExternalDocument => !string.IsNullOrWhiteSpace(ExternalDocumentId);

    public void AttachApproval(Guid approvalId, Guid writeRequestId)
    {
        if (approvalId == Guid.Empty)
        {
            throw new ArgumentException("ApprovalId is required.", nameof(approvalId));
        }

        if (writeRequestId == Guid.Empty)
        {
            throw new ArgumentException("WriteRequestId is required.", nameof(writeRequestId));
        }

        ApprovalId = approvalId;
        WriteRequestId = writeRequestId;
        Status = SalesStatuses.WaitingForApproval;
        ApprovalStatus = SalesStatuses.WaitingForApproval;
        ExecutionStatus = SalesStatuses.Pending;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void MarkApproved()
    {
        if (HasExternalDocument)
        {
            return;
        }

        ApprovalStatus = SalesStatuses.Approved;
        Status = SalesStatuses.Approved;
        ApprovedUtc ??= DateTime.UtcNow;
        FailureSummary = null;
        LastErrorCode = null;
        UpdatedUtc = ApprovedUtc.Value;
    }

    public void MarkExecutionStarted()
    {
        if (HasExternalDocument)
        {
            return;
        }

        ExecutionStatus = SalesStatuses.InProgress;
        Status = SalesStatuses.InProgress;
        ExecutionAttemptCount++;
        ExecutionStartedUtc = DateTime.UtcNow;
        FailureSummary = null;
        LastErrorCode = null;
        UpdatedUtc = ExecutionStartedUtc.Value;
    }

    public void MarkCompleted(string externalDocumentId, string? externalDocumentNumber)
    {
        if (HasExternalDocument)
        {
            return;
        }

        ExternalDocumentId = SalesEntityText.NormalizeRequired(externalDocumentId, nameof(externalDocumentId), 256);
        ExternalDocumentNumber = SalesEntityText.NormalizeOptional(externalDocumentNumber, nameof(externalDocumentNumber), 128);
        Status = SalesStatuses.Completed;
        ApprovalStatus = SalesStatuses.Approved;
        ExecutionStatus = SalesStatuses.Completed;
        FailureSummary = null;
        LastErrorCode = null;
        ExecutedUtc = DateTime.UtcNow;
        UpdatedUtc = ExecutedUtc.Value;
    }

    public void MarkFailed(string errorCode, string failureSummary, bool retryable)
    {
        if (HasExternalDocument)
        {
            return;
        }

        ExecutionStatus = retryable ? SalesStatuses.RetryableFailed : SalesStatuses.Failed;
        Status = SalesStatuses.Failed;
        LastErrorCode = SalesEntityText.NormalizeOptional(errorCode, nameof(errorCode), 120);
        FailureSummary = SalesEntityText.NormalizeOptional(failureSummary, nameof(failureSummary), 1000);
        FailedUtc = DateTime.UtcNow;
        UpdatedUtc = FailedUtc.Value;
    }

    public void MarkRetrying()
    {
        if (!CanRetry)
        {
            throw new InvalidOperationException("Only failed finance handoffs can be retried.");
        }

        ExecutionStatus = SalesStatuses.InProgress;
        Status = SalesStatuses.InProgress;
        RetriedUtc = DateTime.UtcNow;
        FailureSummary = null;
        LastErrorCode = null;
        UpdatedUtc = RetriedUtc.Value;
    }

    private static string NormalizeDocumentType(string documentType)
    {
        var value = SalesEntityText.NormalizeRequired(documentType, nameof(documentType), 32).ToLowerInvariant();
        return value is "quote" or "invoice"
            ? value
            : throw new ArgumentException("Finance handoff document type must be quote or invoice.", nameof(documentType));
    }
}

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

public sealed class SalesEmailLink : ICompanyOwnedEntity
{
    private SalesEmailLink()
    {
    }

    public SalesEmailLink(
        Guid id,
        Guid companyId,
        string externalMessageId,
        Guid? leadId = null,
        Guid? dealId = null,
        Guid? contactId = null,
        Guid? customerCompanyId = null,
        string status = SalesStatuses.Linked,
        string? provider = null,
        Guid? mailboxConnectionId = null,
        string? externalThreadId = null,
        string? internetMessageId = null,
        string linkKind = "message",
        string? ignoreReason = null,
        string? rationale = null,
        string? detectedIntent = null,
        string? productOrServiceInterest = null,
        decimal? confidence = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ExternalMessageId = SalesEntityText.NormalizeRequired(externalMessageId, nameof(externalMessageId), 256);
        LeadId = SalesEntityText.NormalizeOptionalId(leadId, nameof(leadId));
        DealId = SalesEntityText.NormalizeOptionalId(dealId, nameof(dealId));
        ContactId = SalesEntityText.NormalizeOptionalId(contactId, nameof(contactId));
        CustomerCompanyId = SalesEntityText.NormalizeOptionalId(customerCompanyId, nameof(customerCompanyId));
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        Provider = SalesEntityText.NormalizeOptional(provider, nameof(provider), 64);
        MailboxConnectionId = SalesEntityText.NormalizeOptionalId(mailboxConnectionId, nameof(mailboxConnectionId));
        ExternalThreadId = SalesEntityText.NormalizeOptional(externalThreadId, nameof(externalThreadId), 256);
        InternetMessageId = SalesEntityText.NormalizeOptional(internetMessageId, nameof(internetMessageId), 512);
        LinkKind = SalesEntityText.NormalizeRequired(linkKind, nameof(linkKind), 32).ToLowerInvariant();
        IgnoreReason = SalesEntityText.NormalizeOptional(ignoreReason, nameof(ignoreReason), 120);
        Rationale = SalesEntityText.NormalizeOptional(rationale, nameof(rationale), 1000);
        DetectedIntent = SalesEntityText.NormalizeOptional(detectedIntent, nameof(detectedIntent), 120);
        ProductOrServiceInterest = SalesEntityText.NormalizeOptional(productOrServiceInterest, nameof(productOrServiceInterest), 200);
        Confidence = confidence;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ExternalMessageId { get; private set; } = null!;
    public Guid? LeadId { get; private set; }
    public Guid? DealId { get; private set; }
    public Guid? ContactId { get; private set; }
    public Guid? CustomerCompanyId { get; private set; }
    public string Status { get; private set; } = null!;
    public string? Provider { get; private set; }
    public Guid? MailboxConnectionId { get; private set; }
    public string? ExternalThreadId { get; private set; }
    public string? InternetMessageId { get; private set; }
    public string LinkKind { get; private set; } = null!;
    public string? IgnoreReason { get; private set; }
    public string? Rationale { get; private set; }
    public string? DetectedIntent { get; private set; }
    public string? ProductOrServiceInterest { get; private set; }
    public decimal? Confidence { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public const string DuplicateOffer = "duplicate_offer";
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
}

public sealed class DealIntelligenceSignal : ICompanyOwnedEntity
{
    private DealIntelligenceSignal()
    {
    }

    public DealIntelligenceSignal(
        Guid id,
        Guid companyId,
        string signalType,
        decimal confidenceScore,
        string explanation,
        DateTime detectedUtc,
        Guid? dealId = null,
        Guid? conversationId = null,
        Guid? messageId = null,
        Guid? sequenceId = null,
        Guid? sequenceStepId = null,
        string signalState = DealIntelligenceSignalStates.Detected,
        string sourceType = DealIntelligenceSignalSourceTypes.InboundReply,
        string? sourceMessageId = null,
        string? sourceThreadId = null,
        string? sourceMetadataJson = null,
        DateTime? sourceWindowStartedUtc = null,
        DateTime? sourceWindowEndedUtc = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DealId = SalesEntityText.NormalizeOptionalId(dealId, nameof(dealId));
        ConversationId = SalesEntityText.NormalizeOptionalId(conversationId, nameof(conversationId));
        MessageId = SalesEntityText.NormalizeOptionalId(messageId, nameof(messageId));
        SequenceId = SalesEntityText.NormalizeOptionalId(sequenceId, nameof(sequenceId));
        SequenceStepId = SalesEntityText.NormalizeOptionalId(sequenceStepId, nameof(sequenceStepId));
        SignalType = DealIntelligenceSignalTypes.Normalize(signalType);
        SignalState = SalesEntityText.NormalizeRequired(signalState, nameof(signalState), 32).ToLowerInvariant();
        ConfidenceScore = ValidateConfidence(confidenceScore);
        Explanation = SalesEntityText.NormalizeRequired(explanation, nameof(explanation), 1000);
        SourceType = SalesEntityText.NormalizeRequired(sourceType, nameof(sourceType), 64).ToLowerInvariant();
        SourceMessageId = SalesEntityText.NormalizeOptional(sourceMessageId, nameof(sourceMessageId), 256);
        SourceThreadId = SalesEntityText.NormalizeOptional(sourceThreadId, nameof(sourceThreadId), 256);
        SourceMetadataJson = SalesEntityText.NormalizeOptional(sourceMetadataJson, nameof(sourceMetadataJson), 8000);
        DetectedUtc = SalesEntityText.NormalizeUtc(detectedUtc, nameof(detectedUtc));
        SourceWindowStartedUtc = sourceWindowStartedUtc.HasValue ? SalesEntityText.NormalizeUtc(sourceWindowStartedUtc.Value, nameof(sourceWindowStartedUtc)) : null;
        SourceWindowEndedUtc = sourceWindowEndedUtc.HasValue ? SalesEntityText.NormalizeUtc(sourceWindowEndedUtc.Value, nameof(sourceWindowEndedUtc)) : null;
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? DealId { get; private set; }
    public Guid? ConversationId { get; private set; }
    public Guid? MessageId { get; private set; }
    public Guid? SequenceId { get; private set; }
    public Guid? SequenceStepId { get; private set; }
    public string SignalType { get; private set; } = null!;
    public string SignalState { get; private set; } = null!;
    public decimal ConfidenceScore { get; private set; }
    public string Explanation { get; private set; } = null!;
    public string SourceType { get; private set; } = null!;
    public string? SourceMessageId { get; private set; }
    public string? SourceThreadId { get; private set; }
    public string? SourceMetadataJson { get; private set; }
    public DateTime DetectedUtc { get; private set; }
    public DateTime? SourceWindowStartedUtc { get; private set; }
    public DateTime? SourceWindowEndedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Deal? Deal { get; private set; }

    public void UpdateDetection(decimal confidenceScore, string explanation, string? sourceMetadataJson, DateTime detectedUtc, DateTime? sourceWindowStartedUtc, DateTime? sourceWindowEndedUtc)
    {
        ConfidenceScore = ValidateConfidence(confidenceScore);
        Explanation = SalesEntityText.NormalizeRequired(explanation, nameof(explanation), 1000);
        SourceMetadataJson = SalesEntityText.NormalizeOptional(sourceMetadataJson, nameof(sourceMetadataJson), 8000);
        DetectedUtc = SalesEntityText.NormalizeUtc(detectedUtc, nameof(detectedUtc));
        SourceWindowStartedUtc = sourceWindowStartedUtc.HasValue ? SalesEntityText.NormalizeUtc(sourceWindowStartedUtc.Value, nameof(sourceWindowStartedUtc)) : null;
        SourceWindowEndedUtc = sourceWindowEndedUtc.HasValue ? SalesEntityText.NormalizeUtc(sourceWindowEndedUtc.Value, nameof(sourceWindowEndedUtc)) : null;
        UpdatedUtc = DateTime.UtcNow;
    }

    private static decimal ValidateConfidence(decimal value) =>
        value is < 0m or > 1m
            ? throw new ArgumentOutOfRangeException(nameof(value), "Confidence score must be between 0 and 1.")
            : Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

public static class DealIntelligenceSignalTypes
{
    public const string Ghosting = "ghosting";
    public const string PriceResistance = "price_resistance";
    public const string BuyingIntent = "buying_intent";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Ghosting,
        PriceResistance,
        BuyingIntent
    };

    public static string Normalize(string value)
    {
        var normalized = SalesEntityText.NormalizeRequired(value, nameof(value), 64).ToLowerInvariant();
        return Supported.Contains(normalized)
            ? normalized
            : throw new ArgumentException("Unsupported deal intelligence signal type.", nameof(value));
    }
}

public static class DealIntelligenceSignalStates
{
    public const string Detected = "detected";
}

public static class DealIntelligenceSignalSourceTypes
{
    public const string InboundReply = "inbound_reply";
    public const string ConversationReanalysis = "conversation_reanalysis";
}

public static class SalesStatuses
{
    public const string Active = "active";
    public const string Open = "open";
    public const string Converted = "converted";
    public const string Qualified = "qualified";
    public const string Rejected = "rejected";
    public const string Won = "won";
    public const string Lost = "lost";
    public const string Completed = "completed";
    public const string WaitingForApproval = "waiting_for_approval";
    public const string Linked = "linked";
    public const string Ignored = "ignored";
    public const string Blocked = "blocked";
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Approved = "approved";
    public const string Failed = "failed";
    public const string DraftCreated = "draft_created";
    public const string RetryableFailed = "retryable_failed";
    public const string Cancelled = "cancelled";
    public const string Draft = "draft";
    public const string Paused = "paused";
    public const string Stopped = "stopped";
    public const string Bounced = "bounced";
    public const string Delivered = "delivered";
    public const string Deferred = "deferred";
}

internal static class SalesEntityText
{
    public static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }
    }

    public static Guid? NormalizeOptionalId(Guid? value, string name) =>
        value is null ? null : value.Value == Guid.Empty ? throw new ArgumentException($"{name} cannot be empty.", name) : value;

    public static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    public static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    public static DateTime NormalizeUtc(DateTime value, string name) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
}