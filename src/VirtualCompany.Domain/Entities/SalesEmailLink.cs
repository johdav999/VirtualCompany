namespace VirtualCompany.Domain.Entities;
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

