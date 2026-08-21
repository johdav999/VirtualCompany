namespace VirtualCompany.Domain.Entities;

public static class ManualJournalDraftStatusValues
{
    public const string Draft = "draft";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Posted = "posted";
    public const string Discarded = "discarded";

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Manual journal status is required.", nameof(value))
            : value.Trim().ToLowerInvariant() switch
            {
                Draft => Draft,
                AwaitingApproval => AwaitingApproval,
                Posted => Posted,
                Discarded => Discarded,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Manual journal status is not supported.")
            };
}

public sealed class ManualJournalDraft : ICompanyOwnedEntity
{
    private ManualJournalDraft() { }

    public ManualJournalDraft(
        Guid id,
        Guid companyId,
        Guid fiscalPeriodId,
        string voucherSeriesCode,
        DateOnly documentDate,
        DateOnly postingDate,
        string explanation,
        string currency,
        string payloadHash,
        Guid createdByUserId,
        DateTime createdUtc,
        Guid? originalLedgerEntryId = null,
        string? correctionReason = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (fiscalPeriodId == Guid.Empty) throw new ArgumentException("FiscalPeriodId is required.", nameof(fiscalPeriodId));
        if (createdByUserId == Guid.Empty) throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId));
        if (originalLedgerEntryId == Guid.Empty) throw new ArgumentException("OriginalLedgerEntryId cannot be empty.", nameof(originalLedgerEntryId));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        FiscalPeriodId = fiscalPeriodId;
        VoucherSeriesCode = Required(voucherSeriesCode, nameof(voucherSeriesCode), 32).ToUpperInvariant();
        DocumentDate = documentDate;
        PostingDate = postingDate;
        Explanation = Required(explanation, nameof(explanation), 1000);
        Currency = Required(currency, nameof(currency), 3).ToUpperInvariant();
        PayloadHash = Required(payloadHash, nameof(payloadHash), 64).ToLowerInvariant();
        CreatedByUserId = createdByUserId;
        UpdatedByUserId = createdByUserId;
        OriginalLedgerEntryId = originalLedgerEntryId;
        CorrectionReason = Optional(correctionReason, nameof(correctionReason), 1000);
        Status = ManualJournalDraftStatusValues.Draft;
        Version = 1;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public string VoucherSeriesCode { get; private set; } = null!;
    public DateOnly DocumentDate { get; private set; }
    public DateOnly PostingDate { get; private set; }
    public string Explanation { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public long Version { get; private set; }
    public string PayloadHash { get; private set; } = null!;
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? LedgerEntryId { get; private set; }
    public Guid? OriginalLedgerEntryId { get; private set; }
    public string? CorrectionReason { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? PostedUtc { get; private set; }
    public DateTime? DiscardedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;
    public ApprovalRequest? ApprovalRequest { get; private set; }
    public LedgerEntry? LedgerEntry { get; private set; }
    public LedgerEntry? OriginalLedgerEntry { get; private set; }
    public ICollection<ManualJournalDraftLine> Lines { get; } = new List<ManualJournalDraftLine>();
    public ICollection<ManualJournalEvidenceLink> EvidenceLinks { get; } = new List<ManualJournalEvidenceLink>();

    public void ReplaceContent(
        Guid fiscalPeriodId,
        string voucherSeriesCode,
        DateOnly documentDate,
        DateOnly postingDate,
        string explanation,
        string currency,
        string payloadHash,
        string? correctionReason,
        Guid actorUserId,
        DateTime updatedUtc)
    {
        EnsureEditable();
        if (fiscalPeriodId == Guid.Empty) throw new ArgumentException("FiscalPeriodId is required.", nameof(fiscalPeriodId));
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        FiscalPeriodId = fiscalPeriodId;
        VoucherSeriesCode = Required(voucherSeriesCode, nameof(voucherSeriesCode), 32).ToUpperInvariant();
        DocumentDate = documentDate;
        PostingDate = postingDate;
        Explanation = Required(explanation, nameof(explanation), 1000);
        Currency = Required(currency, nameof(currency), 3).ToUpperInvariant();
        PayloadHash = Required(payloadHash, nameof(payloadHash), 64).ToLowerInvariant();
        CorrectionReason = OriginalLedgerEntryId.HasValue
            ? Optional(correctionReason, nameof(correctionReason), 1000)
            : null;
        Status = ManualJournalDraftStatusValues.Draft;
        ApprovalRequestId = null;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        Version++;
    }

    public void BindApproval(Guid approvalRequestId, Guid actorUserId, DateTime updatedUtc)
    {
        EnsureEditable();
        if (approvalRequestId == Guid.Empty) throw new ArgumentException("ApprovalRequestId is required.", nameof(approvalRequestId));
        ApprovalRequestId = approvalRequestId;
        Status = ManualJournalDraftStatusValues.AwaitingApproval;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }

    public void MarkPosted(Guid ledgerEntryId, Guid actorUserId, DateTime postedUtc)
    {
        if (ledgerEntryId == Guid.Empty) throw new ArgumentException("LedgerEntryId is required.", nameof(ledgerEntryId));
        if (Status == ManualJournalDraftStatusValues.Posted)
        {
            if (LedgerEntryId != ledgerEntryId) throw new InvalidOperationException("The manual journal is already linked to another posted entry.");
            return;
        }
        if (Status == ManualJournalDraftStatusValues.Discarded) throw new InvalidOperationException("A discarded manual journal cannot be posted.");
        LedgerEntryId = ledgerEntryId;
        Status = ManualJournalDraftStatusValues.Posted;
        UpdatedByUserId = actorUserId;
        PostedUtc = EntityTimestampNormalizer.NormalizeUtc(postedUtc, nameof(postedUtc));
        UpdatedUtc = PostedUtc.Value;
    }

    public void Discard(Guid actorUserId, DateTime discardedUtc)
    {
        EnsureEditable();
        Status = ManualJournalDraftStatusValues.Discarded;
        ApprovalRequestId = null;
        UpdatedByUserId = actorUserId;
        DiscardedUtc = EntityTimestampNormalizer.NormalizeUtc(discardedUtc, nameof(discardedUtc));
        UpdatedUtc = DiscardedUtc.Value;
        Version++;
    }

    private void EnsureEditable()
    {
        if (Status is ManualJournalDraftStatusValues.Posted or ManualJournalDraftStatusValues.Discarded)
            throw new InvalidOperationException("Posted or discarded manual journals cannot be edited.");
    }

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        return normalized;
    }

    private static string? Optional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, name, maxLength);
}

public sealed class ManualJournalDraftLine : ICompanyOwnedEntity
{
    private ManualJournalDraftLine() { }

    public ManualJournalDraftLine(Guid id, Guid companyId, Guid draftId, Guid financeAccountId, int lineNumber,
        decimal debitAmount, decimal creditAmount, string currency, string? description, Guid? costCenterId,
        string? taxFactsJson, string? dimensionFactsJson)
    {
        if (companyId == Guid.Empty || draftId == Guid.Empty || financeAccountId == Guid.Empty) throw new ArgumentException("Company, draft, and account are required.");
        if (lineNumber <= 0) throw new ArgumentOutOfRangeException(nameof(lineNumber));
        if (debitAmount < 0 || creditAmount < 0 || debitAmount == 0 && creditAmount == 0 || debitAmount > 0 && creditAmount > 0)
            throw new ArgumentException("A journal line must contain one positive debit or credit amount.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DraftId = draftId;
        FinanceAccountId = financeAccountId;
        LineNumber = lineNumber;
        DebitAmount = debitAmount;
        CreditAmount = creditAmount;
        Currency = currency.Trim().ToUpperInvariant();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        CostCenterId = costCenterId;
        TaxFactsJson = taxFactsJson;
        DimensionFactsJson = dimensionFactsJson;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DraftId { get; private set; }
    public Guid FinanceAccountId { get; private set; }
    public int LineNumber { get; private set; }
    public decimal DebitAmount { get; private set; }
    public decimal CreditAmount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid? CostCenterId { get; private set; }
    public string? TaxFactsJson { get; private set; }
    public string? DimensionFactsJson { get; private set; }
    public ManualJournalDraft Draft { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!;
}

public sealed class ManualJournalEvidenceLink : ICompanyOwnedEntity
{
    private ManualJournalEvidenceLink() { }
    public ManualJournalEvidenceLink(Guid id, Guid companyId, Guid draftId, Guid documentId, string contentHash, string title, DateTime createdUtc)
    {
        if (companyId == Guid.Empty || draftId == Guid.Empty || documentId == Guid.Empty) throw new ArgumentException("Company, draft, and document are required.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DraftId = draftId;
        DocumentId = documentId;
        ContentHash = contentHash.Trim().ToLowerInvariant();
        Title = title.Trim();
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DraftId { get; private set; }
    public Guid DocumentId { get; private set; }
    public string ContentHash { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public ManualJournalDraft Draft { get; private set; } = null!;
    public CompanyKnowledgeDocument Document { get; private set; } = null!;
}

public sealed class ManualJournalOperation : ICompanyOwnedEntity
{
    private ManualJournalOperation() { }
    public ManualJournalOperation(Guid id, Guid companyId, Guid draftId, string action, string idempotencyKey,
        string payloadHash, long resultVersion, Guid? approvalRequestId, Guid? ledgerEntryId, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DraftId = draftId;
        Action = action.Trim().ToLowerInvariant();
        IdempotencyKey = idempotencyKey.Trim();
        PayloadHash = payloadHash.Trim().ToLowerInvariant();
        ResultVersion = resultVersion;
        ApprovalRequestId = approvalRequestId;
        LedgerEntryId = ledgerEntryId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DraftId { get; private set; }
    public string Action { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!;
    public long ResultVersion { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? LedgerEntryId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public ManualJournalDraft Draft { get; private set; } = null!;
}

public sealed class LedgerEntryEvidenceLink : ICompanyOwnedEntity
{
    private LedgerEntryEvidenceLink() { }
    public LedgerEntryEvidenceLink(Guid id, Guid companyId, Guid ledgerEntryId, Guid documentId, string contentHash, string title, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        LedgerEntryId = ledgerEntryId;
        DocumentId = documentId;
        ContentHash = contentHash.Trim().ToLowerInvariant();
        Title = title.Trim();
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid LedgerEntryId { get; private set; }
    public Guid DocumentId { get; private set; }
    public string ContentHash { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public LedgerEntry LedgerEntry { get; private set; } = null!;
    public CompanyKnowledgeDocument Document { get; private set; } = null!;
}
