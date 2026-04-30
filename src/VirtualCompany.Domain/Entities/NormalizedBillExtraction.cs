namespace VirtualCompany.Domain.Entities;

public sealed class NormalizedBillExtraction : ICompanyOwnedEntity
{
    private NormalizedBillExtraction()
    {
    }

    public NormalizedBillExtraction(
        Guid id,
        Guid companyId,
        string? supplierName,
        string? supplierOrgNumber,
        string? invoiceNumber,
        DateTime? invoiceDateUtc,
        DateTime? dueDateUtc,
        string? currency,
        decimal? totalAmount,
        decimal? vatAmount,
        string? paymentReference,
        string? bankgiro,
        string? plusgiro,
        string? iban,
        string? bic,
        string confidence,
        string? sourceEmailId,
        string? sourceAttachmentId,
        string evidenceJson,
        string validationStatus,
        string validationFindingsJson,
        Guid duplicateCheckId,
        bool requiresReview,
        bool isEligibleForApprovalProposal,
        DateTime validationStatusPersistedAtUtc,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (duplicateCheckId == Guid.Empty)
        {
            throw new ArgumentException("DuplicateCheckId is required.", nameof(duplicateCheckId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SupplierName = NormalizeOptional(supplierName, nameof(supplierName), 200);
        SupplierOrgNumber = NormalizeOptional(supplierOrgNumber, nameof(supplierOrgNumber), 64);
        InvoiceNumber = NormalizeOptional(invoiceNumber, nameof(invoiceNumber), 64);
        InvoiceDateUtc = NormalizeOptionalUtc(invoiceDateUtc, nameof(invoiceDateUtc));
        DueDateUtc = NormalizeOptionalUtc(dueDateUtc, nameof(dueDateUtc));
        Currency = NormalizeOptional(currency, nameof(currency), 3)?.ToUpperInvariant();
        TotalAmount = totalAmount;
        VatAmount = vatAmount;
        PaymentReference = NormalizeOptional(paymentReference, nameof(paymentReference), 128);
        Bankgiro = NormalizeOptional(bankgiro, nameof(bankgiro), 32);
        Plusgiro = NormalizeOptional(plusgiro, nameof(plusgiro), 32);
        Iban = NormalizeOptional(iban, nameof(iban), 34);
        Bic = NormalizeOptional(bic, nameof(bic), 11);
        Confidence = NormalizeConfidence(confidence);
        SourceEmailId = NormalizeOptional(sourceEmailId, nameof(sourceEmailId), 512);
        SourceAttachmentId = NormalizeOptional(sourceAttachmentId, nameof(sourceAttachmentId), 512);
        EvidenceJson = NormalizeRequired(evidenceJson, nameof(evidenceJson), int.MaxValue);
        ValidationStatus = NormalizeValidationStatus(validationStatus);
        ValidationFindingsJson = NormalizeRequired(validationFindingsJson, nameof(validationFindingsJson), int.MaxValue);
        DuplicateCheckId = duplicateCheckId;
        RequiresReview = requiresReview;
        ValidationStatusPersistedAtUtc = EntityTimestampNormalizer.NormalizeUtc(validationStatusPersistedAtUtc, nameof(validationStatusPersistedAtUtc));
        IsEligibleForApprovalProposal = validationStatusPersistedAtUtc != default && isEligibleForApprovalProposal;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string? SupplierName { get; private set; }
    public string? SupplierOrgNumber { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public DateTime? InvoiceDateUtc { get; private set; }
    public DateTime? DueDateUtc { get; private set; }
    public string? Currency { get; private set; }
    public decimal? TotalAmount { get; private set; }
    public decimal? VatAmount { get; private set; }
    public string? PaymentReference { get; private set; }
    public string? Bankgiro { get; private set; }
    public string? Plusgiro { get; private set; }
    public string? Iban { get; private set; }
    public string? Bic { get; private set; }
    public string Confidence { get; private set; } = null!;
    public string? SourceEmailId { get; private set; }
    public string? SourceAttachmentId { get; private set; }
    public string EvidenceJson { get; private set; } = "[]";
    public string ValidationStatus { get; private set; } = null!;
    public string ValidationFindingsJson { get; private set; } = "[]";
    public Guid DuplicateCheckId { get; private set; }
    public bool RequiresReview { get; private set; }
    public bool IsEligibleForApprovalProposal { get; private set; }
    public DateTime ValidationStatusPersistedAtUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public BillDuplicateCheck DuplicateCheck { get; private set; } = null!;

    private static DateTime? NormalizeOptionalUtc(DateTime? value, string name) =>
        value.HasValue ? EntityTimestampNormalizer.NormalizeUtc(value.Value, name) : null;

    private static string NormalizeConfidence(string value)
    {
        var normalized = NormalizeRequired(value, nameof(value), 16).ToLowerInvariant();
        return normalized is "high" or "medium" or "low"
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported bill confidence.");
    }

    private static string NormalizeValidationStatus(string value)
    {
        var normalized = NormalizeRequired(value, nameof(value), 32).ToLowerInvariant();
        return normalized is "pending" or "valid" or "flagged" or "rejected"
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported bill validation status.");
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}
