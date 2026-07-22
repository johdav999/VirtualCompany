using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class MailboxFolderSyncCursor : ICompanyOwnedEntity
{
    private MailboxFolderSyncCursor()
    {
    }

    public MailboxFolderSyncCursor(Guid id, Guid companyId, Guid mailboxConnectionId, string folderId, DateTime createdUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (mailboxConnectionId == Guid.Empty) throw new ArgumentException("MailboxConnectionId is required.", nameof(mailboxConnectionId));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MailboxConnectionId = mailboxConnectionId;
        FolderId = NormalizeRequired(folderId, nameof(folderId), 512);
        Status = MailboxCursorStatus.Active;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MailboxConnectionId { get; private set; }
    public string FolderId { get; private set; } = null!;
    public long? UidValidity { get; private set; }
    public long LastProcessedUid { get; private set; }
    public long? HighestModSequence { get; private set; }
    public MailboxCursorStatus Status { get; private set; }
    public DateTime? LastSuccessfulSyncUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public MailboxConnection MailboxConnection { get; private set; } = null!;

    public void Advance(long uidValidity, long lastProcessedUid, long? highestModSequence, DateTime completedUtc)
    {
        if (uidValidity <= 0) throw new ArgumentOutOfRangeException(nameof(uidValidity));
        if (lastProcessedUid < 0) throw new ArgumentOutOfRangeException(nameof(lastProcessedUid));
        if (UidValidity.HasValue && UidValidity.Value != uidValidity)
        {
            MarkReconciliationRequired(completedUtc);
            return;
        }

        UidValidity = uidValidity;
        LastProcessedUid = Math.Max(LastProcessedUid, lastProcessedUid);
        HighestModSequence = highestModSequence;
        Status = MailboxCursorStatus.Active;
        LastSuccessfulSyncUtc = EntityTimestampNormalizer.NormalizeUtc(completedUtc, nameof(completedUtc));
        UpdatedUtc = LastSuccessfulSyncUtc.Value;
    }

    public void ResetAfterReconciliation(long uidValidity, DateTime completedUtc)
    {
        if (uidValidity <= 0) throw new ArgumentOutOfRangeException(nameof(uidValidity));
        UidValidity = uidValidity;
        LastProcessedUid = 0;
        HighestModSequence = null;
        Status = MailboxCursorStatus.Active;
        LastSuccessfulSyncUtc = EntityTimestampNormalizer.NormalizeUtc(completedUtc, nameof(completedUtc));
        UpdatedUtc = LastSuccessfulSyncUtc.Value;
    }

    private void MarkReconciliationRequired(DateTime detectedUtc)
    {
        Status = MailboxCursorStatus.ReconciliationRequired;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(detectedUtc, nameof(detectedUtc));
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        return normalized;
    }
}
