namespace VirtualCompany.Domain.Entities;
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

