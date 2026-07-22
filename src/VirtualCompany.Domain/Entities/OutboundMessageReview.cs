namespace VirtualCompany.Domain.Entities;
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

