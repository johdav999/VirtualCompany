namespace VirtualCompany.Domain.Entities;
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

