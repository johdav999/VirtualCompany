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

