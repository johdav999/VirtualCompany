namespace VirtualCompany.Domain.Entities;

public sealed class DetectedBill : ICompanyOwnedEntity
{
    private readonly List<DetectedBillField> _fields = [];

    private DetectedBill()
    {
    }

    public DetectedBill(
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
        decimal? confidence,
        string confidenceLevel,
        string validationStatus,
        string reviewStatus,
        bool requiresReview,
        bool isEligibleForApprovalProposal,
        bool validationStatusPersisted,
        string validationIssuesJson,
        string? sourceEmailId,
        string? sourceAttachmentId,
        Guid? duplicateCheckId = null,
        DateTime? validationStatusPersistedAtUtc = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
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
        Iban = NormalizeOptional(iban, nameof(iban), 34)?.ToUpperInvariant();
        Bic = NormalizeOptional(bic, nameof(bic), 11)?.ToUpperInvariant();
        Confidence = NormalizeScore(confidence, nameof(confidence));
        ConfidenceLevel = NormalizeConfidenceLevel(confidenceLevel);
        ValidationStatus = NormalizeValidationStatus(validationStatus);
        ReviewStatus = NormalizeReviewStatus(reviewStatus);
        RequiresReview = requiresReview;
        ValidationStatusPersisted = validationStatusPersisted;
        ValidationStatusPersistedAtUtc = validationStatusPersistedAtUtc.HasValue
            ? EntityTimestampNormalizer.NormalizeUtc(validationStatusPersistedAtUtc.Value, nameof(validationStatusPersistedAtUtc))
            : null;
        IsEligibleForApprovalProposal = validationStatusPersisted && ValidationStatusPersistedAtUtc.HasValue && isEligibleForApprovalProposal;
        ValidationIssuesJson = NormalizeJsonArray(validationIssuesJson, nameof(validationIssuesJson));
        SourceEmailId = NormalizeOptional(sourceEmailId, nameof(sourceEmailId), 512);
        SourceAttachmentId = NormalizeOptional(sourceAttachmentId, nameof(sourceAttachmentId), 512);
        DuplicateCheckId = duplicateCheckId == Guid.Empty ? null : duplicateCheckId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
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
    public decimal? Confidence { get; private set; }
    public string ConfidenceLevel { get; private set; } = "low";
    public string ValidationStatus { get; private set; } = "pending";
    public string ReviewStatus { get; private set; } = "required";
    public bool RequiresReview { get; private set; }
    public bool IsEligibleForApprovalProposal { get; private set; }
    public bool ValidationStatusPersisted { get; private set; }
    public DateTime? ValidationStatusPersistedAtUtc { get; private set; }
    public string ValidationIssuesJson { get; private set; } = "[]";
    public string? SourceEmailId { get; private set; }
    public string? SourceAttachmentId { get; private set; }
    public Guid? DuplicateCheckId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public BillDuplicateCheck? DuplicateCheck { get; private set; }
    public IReadOnlyCollection<DetectedBillField> Fields => _fields;

    public void AddField(DetectedBillField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        if (field.CompanyId != CompanyId)
        {
            throw new InvalidOperationException("Detected bill fields must belong to the same company as the bill.");
        }

        if (field.DetectedBillId != Id)
        {
            throw new InvalidOperationException("Detected bill field is linked to a different bill.");
        }

        if (_fields.Any(existing => string.Equals(existing.FieldName, field.FieldName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A detected bill field already exists for '{field.FieldName}'.");
        }

        _fields.Add(field);
    }

    private static DateTime? NormalizeOptionalUtc(DateTime? value, string name) =>
        value.HasValue ? EntityTimestampNormalizer.NormalizeUtc(value.Value, name) : null;

    private static decimal? NormalizeScore(decimal? value, string name)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be between 0 and 1.");
        }

        return value;
    }

    private static string NormalizeConfidenceLevel(string value)
    {
        var normalized = NormalizeRequired(value, nameof(value), 16).ToLowerInvariant();
        return normalized is "high" or "medium" or "low"
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported detected bill confidence level.");
    }

    private static string NormalizeValidationStatus(string value)
    {
        var normalized = NormalizeRequired(value, nameof(value), 32).ToLowerInvariant();
        return normalized is "pending" or "valid" or "flagged" or "rejected"
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported detected bill validation status.");
    }

    private static string NormalizeReviewStatus(string value)
    {
        var normalized = NormalizeRequired(value, nameof(value), 32).ToLowerInvariant();
        return normalized is "not_required" or "required" or "completed"
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported detected bill review status.");
    }

    private static string NormalizeJsonArray(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? "[]" : value.Trim();

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
