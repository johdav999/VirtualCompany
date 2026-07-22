using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public sealed class SupportMessage : ICompanyOwnedEntity
{
    private SupportMessage()
    {
    }

    public SupportMessage(
        Guid id,
        Guid companyId,
        Guid supportCaseId,
        string direction,
        string channel,
        string sender,
        string? recipient,
        string body,
        DateTime occurredUtc,
        Guid? emailMessageSnapshotId = null,
        string? providerMessageId = null,
        string? providerThreadId = null,
        Guid? replyDraftId = null)
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupportCaseId = supportCaseId == Guid.Empty ? throw new ArgumentException("SupportCaseId is required.", nameof(supportCaseId)) : supportCaseId;
        Direction = SupportMessageDirections.Normalize(direction);
        Channel = SupportEntityText.NormalizeRequired(channel, nameof(channel), 80);
        Sender = SupportEntityText.NormalizeRequired(sender, nameof(sender), 256);
        Recipient = SupportEntityText.NormalizeOptional(recipient, nameof(recipient), 256);
        Body = SupportEntityText.NormalizeRequired(body, nameof(body), 8000);
        OccurredUtc = SupportEntityText.NormalizeUtc(occurredUtc, nameof(occurredUtc));
        EmailMessageSnapshotId = SupportEntityText.NormalizeOptionalId(emailMessageSnapshotId, nameof(emailMessageSnapshotId));
        ProviderMessageId = SupportEntityText.NormalizeOptional(providerMessageId, nameof(providerMessageId), 256);
        ProviderThreadId = SupportEntityText.NormalizeOptional(providerThreadId, nameof(providerThreadId), 256);
        ReplyDraftId = SupportEntityText.NormalizeOptionalId(replyDraftId, nameof(replyDraftId));
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SupportCaseId { get; private set; }
    public string Direction { get; private set; } = null!;
    public string Channel { get; private set; } = null!;
    public string Sender { get; private set; } = null!;
    public string? Recipient { get; private set; }
    public string Body { get; private set; } = null!;
    public Guid? EmailMessageSnapshotId { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public string? ProviderThreadId { get; private set; }
    public Guid? ReplyDraftId { get; private set; }
    public DateTime OccurredUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public SupportCase SupportCase { get; private set; } = null!;
}

