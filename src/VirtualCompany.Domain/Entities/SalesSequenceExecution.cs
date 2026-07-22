namespace VirtualCompany.Domain.Entities;
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

