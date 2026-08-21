using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class BankReconciliationFollowUp : ICompanyOwnedEntity
{
    private BankReconciliationFollowUp() { }

    public BankReconciliationFollowUp(Guid id, Guid companyId, Guid bankTransactionId, Guid ledgerEntryId,
        string reason, Guid createdByUserId, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        BankTransactionId = bankTransactionId == Guid.Empty ? throw new ArgumentException("BankTransactionId is required.", nameof(bankTransactionId)) : bankTransactionId;
        LedgerEntryId = ledgerEntryId == Guid.Empty ? throw new ArgumentException("LedgerEntryId is required.", nameof(ledgerEntryId)) : ledgerEntryId;
        Reason = string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("Reason is required.", nameof(reason)) : reason.Trim();
        CreatedByUserId = createdByUserId == Guid.Empty ? throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId)) : createdByUserId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        Status = BankReconciliationFollowUpStatuses.Open;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BankTransactionId { get; private set; }
    public Guid LedgerEntryId { get; private set; }
    public string Status { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public Guid? ResolutionLedgerEntryId { get; private set; }
    public Company Company { get; private set; } = null!;
    public BankTransaction BankTransaction { get; private set; } = null!;
    public LedgerEntry LedgerEntry { get; private set; } = null!;

    public void Resolve(Guid actorUserId, Guid resolutionLedgerEntryId, DateTime resolvedUtc)
    {
        if (Status == BankReconciliationFollowUpStatuses.Resolved) return;
        ResolvedByUserId = actorUserId == Guid.Empty ? throw new ArgumentException("Actor is required.", nameof(actorUserId)) : actorUserId;
        ResolutionLedgerEntryId = resolutionLedgerEntryId == Guid.Empty ? throw new ArgumentException("Resolution ledger entry is required.", nameof(resolutionLedgerEntryId)) : resolutionLedgerEntryId;
        ResolvedUtc = EntityTimestampNormalizer.NormalizeUtc(resolvedUtc, nameof(resolvedUtc));
        Status = BankReconciliationFollowUpStatuses.Resolved;
    }
}
