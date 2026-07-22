namespace VirtualCompany.Domain.Entities;
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

