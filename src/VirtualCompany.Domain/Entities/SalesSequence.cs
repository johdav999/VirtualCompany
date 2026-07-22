namespace VirtualCompany.Domain.Entities;
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

