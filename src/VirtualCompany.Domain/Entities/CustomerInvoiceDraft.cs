using System.Text.Json;

namespace VirtualCompany.Domain.Entities;

public static class CustomerInvoiceDraftStatusValues
{
    public const string Draft = "draft";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Issued = "issued";
    public const string Discarded = "discarded";
}

public static class CustomerInvoiceDraftDocumentTypes
{
    public const string Invoice = "customer_invoice";
    public const string CreditNote = "customer_credit_note";

    public static string Normalize(string value) => Required(value, nameof(value), 32).ToLowerInvariant() switch
    {
        Invoice => Invoice,
        CreditNote => CreditNote,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "The customer invoice document type is not supported.")
    };

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(name);
    }
}

public static class CustomerInvoiceDraftSourceKinds
{
    public const string User = "user";
    public const string Copy = "copy";
    public const string RecurringSchedule = "recurring_schedule";
    public const string ReceivablesCorrection = "receivables_correction";

    public static string Normalize(string value) => Required(value, nameof(value), 32).ToLowerInvariant() switch
    {
        User => User,
        Copy => Copy,
        RecurringSchedule => RecurringSchedule,
        ReceivablesCorrection => ReceivablesCorrection,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "The customer invoice draft source is not supported.")
    };

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(name);
    }
}

public sealed class CustomerInvoiceDraft : ICompanyOwnedEntity
{
    private CustomerInvoiceDraft() { }

    public CustomerInvoiceDraft(Guid id, Guid companyId, Guid customerId, string documentType,
        DateOnly issueDate, DateOnly supplyDate, DateOnly dueDate, string currency, string paymentTermKind,
        int paymentTermDays, string? buyerReference, string? sellerReference, string? notes,
        string deliveryIntent, string sourceKind, string? sourceReference, Guid createdByUserId, DateTime createdUtc,
        Guid? originalInvoiceId = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = Required(companyId, nameof(companyId));
        CustomerId = Required(customerId, nameof(customerId));
        CreatedByUserId = Required(createdByUserId, nameof(createdByUserId));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        Version = 1;
        Status = CustomerInvoiceDraftStatusValues.Draft;
        OriginalInvoiceId = originalInvoiceId == Guid.Empty ? null : originalInvoiceId;
        Apply(customerId, documentType, issueDate, supplyDate, dueDate, currency, paymentTermKind,
            paymentTermDays, buyerReference, sellerReference, notes, deliveryIntent, sourceKind,
            sourceReference, createdByUserId, CreatedUtc, incrementVersion: false);
        if (DocumentType == CustomerInvoiceDraftDocumentTypes.CreditNote && !OriginalInvoiceId.HasValue)
            throw new ArgumentException("A customer credit-note draft must reference its original invoice.", nameof(originalInvoiceId));
        if (DocumentType == CustomerInvoiceDraftDocumentTypes.Invoice && OriginalInvoiceId.HasValue)
            throw new ArgumentException("A customer invoice draft cannot reference an original invoice.", nameof(originalInvoiceId));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Status { get; private set; } = null!;
    public string DocumentType { get; private set; } = null!;
    public DateOnly IssueDate { get; private set; }
    public DateOnly SupplyDate { get; private set; }
    public DateOnly DueDate { get; private set; }
    public string Currency { get; private set; } = null!;
    public string PaymentTermKind { get; private set; } = null!;
    public int PaymentTermDays { get; private set; }
    public string? BuyerReference { get; private set; }
    public string? SellerReference { get; private set; }
    public string? Notes { get; private set; }
    public string DeliveryIntent { get; private set; } = null!;
    public string SourceKind { get; private set; } = null!;
    public string? SourceReference { get; private set; }
    public Guid? OriginalInvoiceId { get; private set; }
    public long Version { get; private set; }
    public string InputHash { get; private set; } = string.Empty;
    public string ResultHash { get; private set; } = string.Empty;
    public string PolicyPackKey { get; private set; } = string.Empty;
    public string PolicyPackVersion { get; private set; } = string.Empty;
    public string PolicyDefinitionHash { get; private set; } = string.Empty;
    public int RoundingPrecision { get; private set; }
    public string RoundingMode { get; private set; } = string.Empty;
    public decimal NetTotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal TaxTotal { get; private set; }
    public decimal GrossTotal { get; private set; }
    public decimal RoundingAmount { get; private set; }
    public string WarningsJson { get; private set; } = "[]";
    public string BlockersJson { get; private set; } = "[]";
    public Guid? ApprovalRequestId { get; private set; }
    public long? ApprovalDraftVersion { get; private set; }
    public string? ApprovalResultHash { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? DiscardedUtc { get; private set; }
    public Guid? IssuedInvoiceId { get; private set; }
    public Guid? IssuedStatutoryDocumentId { get; private set; }
    public Guid? IssuedLedgerEntryId { get; private set; }
    public string? IssuedSnapshotHash { get; private set; }
    public DateTime? IssuedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceCounterparty Customer { get; private set; } = null!;
    public ApprovalRequest? ApprovalRequest { get; private set; }
    public ICollection<CustomerInvoiceDraftLine> Lines { get; } = new List<CustomerInvoiceDraftLine>();
    public ICollection<CustomerInvoiceDraftEvidenceLink> EvidenceLinks { get; } = new List<CustomerInvoiceDraftEvidenceLink>();

    public void ReplaceContent(Guid customerId, string documentType, DateOnly issueDate, DateOnly supplyDate,
        DateOnly dueDate, string currency, string paymentTermKind, int paymentTermDays, string? buyerReference,
        string? sellerReference, string? notes, string deliveryIntent, string sourceKind, string? sourceReference,
        Guid actorUserId, DateTime updatedUtc)
    {
        EnsureEditable();
        Apply(customerId, documentType, issueDate, supplyDate, dueDate, currency, paymentTermKind,
            paymentTermDays, buyerReference, sellerReference, notes, deliveryIntent, sourceKind,
            sourceReference, actorUserId, updatedUtc, incrementVersion: true);
        Status = CustomerInvoiceDraftStatusValues.Draft;
    }

    public void ApplyCalculation(string inputHash, string resultHash, string policyPackKey, string policyPackVersion,
        string policyDefinitionHash, int roundingPrecision, string roundingMode, decimal netTotal,
        decimal discountTotal, decimal taxTotal, decimal grossTotal, decimal roundingAmount,
        string warningsJson, string blockersJson)
    {
        EnsureEditable();
        InputHash = Hash(inputHash, nameof(inputHash));
        ResultHash = Hash(resultHash, nameof(resultHash));
        PolicyPackKey = Text(policyPackKey, nameof(policyPackKey), 100);
        PolicyPackVersion = Text(policyPackVersion, nameof(policyPackVersion), 64);
        PolicyDefinitionHash = Hash(policyDefinitionHash, nameof(policyDefinitionHash));
        if (roundingPrecision is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(roundingPrecision));
        RoundingPrecision = roundingPrecision;
        RoundingMode = Text(roundingMode, nameof(roundingMode), 32);
        NetTotal = Money(netTotal, nameof(netTotal));
        DiscountTotal = Money(discountTotal, nameof(discountTotal));
        TaxTotal = Money(taxTotal, nameof(taxTotal));
        GrossTotal = Money(grossTotal, nameof(grossTotal));
        RoundingAmount = decimal.Round(roundingAmount, 6, MidpointRounding.ToEven);
        WarningsJson = Json(warningsJson, nameof(warningsJson));
        BlockersJson = Json(blockersJson, nameof(blockersJson));
    }

    public void BindApproval(Guid approvalRequestId, Guid actorUserId, DateTime updatedUtc)
    {
        EnsureEditable();
        ApprovalRequestId = Required(approvalRequestId, nameof(approvalRequestId));
        ApprovalDraftVersion = Version;
        ApprovalResultHash = ResultHash;
        Status = CustomerInvoiceDraftStatusValues.AwaitingApproval;
        UpdatedByUserId = Required(actorUserId, nameof(actorUserId));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }

    public void Discard(Guid actorUserId, DateTime discardedUtc)
    {
        EnsureEditable();
        Status = CustomerInvoiceDraftStatusValues.Discarded;
        ApprovalRequestId = null;
        ApprovalDraftVersion = null;
        ApprovalResultHash = null;
        UpdatedByUserId = Required(actorUserId, nameof(actorUserId));
        DiscardedUtc = EntityTimestampNormalizer.NormalizeUtc(discardedUtc, nameof(discardedUtc));
        UpdatedUtc = DiscardedUtc.Value;
        Version++;
    }

    public void MarkIssued(Guid invoiceId, Guid issuedStatutoryDocumentId, Guid ledgerEntryId,
        string snapshotHash, Guid actorUserId, DateTime issuedUtc)
    {
        EnsureEditable();
        if (invoiceId == Guid.Empty || issuedStatutoryDocumentId == Guid.Empty || ledgerEntryId == Guid.Empty)
            throw new ArgumentException("Issued invoice, document, and journal identities are required.");
        IssuedInvoiceId = invoiceId;
        IssuedStatutoryDocumentId = issuedStatutoryDocumentId;
        IssuedLedgerEntryId = ledgerEntryId;
        IssuedSnapshotHash = Hash(snapshotHash, nameof(snapshotHash));
        UpdatedByUserId = Required(actorUserId, nameof(actorUserId));
        IssuedUtc = EntityTimestampNormalizer.NormalizeUtc(issuedUtc, nameof(issuedUtc));
        UpdatedUtc = IssuedUtc.Value;
        Status = CustomerInvoiceDraftStatusValues.Issued;
        Version++;
    }

    private void Apply(Guid customerId, string documentType, DateOnly issueDate, DateOnly supplyDate,
        DateOnly dueDate, string currency, string paymentTermKind, int paymentTermDays, string? buyerReference,
        string? sellerReference, string? notes, string deliveryIntent, string sourceKind, string? sourceReference,
        Guid actorUserId, DateTime updatedUtc, bool incrementVersion)
    {
        CustomerId = Required(customerId, nameof(customerId));
        DocumentType = CustomerInvoiceDraftDocumentTypes.Normalize(documentType);
        if (supplyDate > issueDate) throw new ArgumentException("Supply date cannot be after the issue date.", nameof(supplyDate));
        if (dueDate < issueDate) throw new ArgumentException("Due date cannot be before the issue date.", nameof(dueDate));
        IssueDate = issueDate;
        SupplyDate = supplyDate;
        DueDate = dueDate;
        Currency = CurrencyCode(currency);
        PaymentTermKind = Choice(paymentTermKind, nameof(paymentTermKind), CustomerBillingPaymentTermKinds.FixedDays,
            CustomerBillingPaymentTermKinds.DueOnReceipt);
        PaymentTermDays = PaymentTermKind == CustomerBillingPaymentTermKinds.DueOnReceipt ? 0 :
            paymentTermDays is >= 0 and <= 365 ? paymentTermDays : throw new ArgumentOutOfRangeException(nameof(paymentTermDays));
        BuyerReference = Optional(buyerReference, nameof(buyerReference), 100);
        SellerReference = Optional(sellerReference, nameof(sellerReference), 100);
        Notes = Optional(notes, nameof(notes), 2000);
        DeliveryIntent = Choice(deliveryIntent, nameof(deliveryIntent), CustomerBillingDeliveryChannels.Email,
            CustomerBillingDeliveryChannels.EInvoice, CustomerBillingDeliveryChannels.Postal);
        SourceKind = CustomerInvoiceDraftSourceKinds.Normalize(sourceKind);
        SourceReference = Optional(sourceReference, nameof(sourceReference), 200);
        UpdatedByUserId = Required(actorUserId, nameof(actorUserId));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        if (incrementVersion) Version++;
    }

    private void EnsureEditable()
    {
        if (Status is CustomerInvoiceDraftStatusValues.Discarded or CustomerInvoiceDraftStatusValues.Issued)
            throw new InvalidOperationException("An issued or discarded customer invoice draft cannot be changed.");
    }

    private static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    private static string Text(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(name);
    }
    private static string? Optional(string? value, string name, int maxLength) => string.IsNullOrWhiteSpace(value) ? null : Text(value, name, maxLength);
    private static string Choice(string value, string name, params string[] choices)
    {
        var normalized = Text(value, name, 32).ToLowerInvariant();
        return choices.Contains(normalized, StringComparer.Ordinal) ? normalized : throw new ArgumentOutOfRangeException(name);
    }
    private static string CurrencyCode(string value)
    {
        var normalized = Text(value, nameof(value), 3).ToUpperInvariant();
        return normalized.Length == 3 && normalized.All(character => character is >= 'A' and <= 'Z')
            ? normalized : throw new ArgumentException("Currency must use a three-letter alphabetic code.", nameof(value));
    }
    private static string Hash(string value, string name)
    {
        var normalized = Text(value, name, 64).ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit) ? normalized : throw new ArgumentException($"{name} must be a SHA-256 hash.", name);
    }
    private static decimal Money(decimal value, string name) => value < 0m ? throw new ArgumentOutOfRangeException(name) : decimal.Round(value, 6, MidpointRounding.ToEven);
    private static string Json(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16000) throw new ArgumentOutOfRangeException(name);
        using var _ = JsonDocument.Parse(value);
        return value;
    }
}

public sealed class CustomerInvoiceDraftLine : ICompanyOwnedEntity
{
    private CustomerInvoiceDraftLine() { }

    public CustomerInvoiceDraftLine(Guid id, Guid companyId, Guid draftId, int sequence, string description,
        decimal quantity, string unit, decimal unitPrice, decimal discountPercent, decimal discountAmount,
        decimal netAmount, string taxRuleKey, string taxRuleVersion, string taxClassification, decimal taxRate,
        decimal taxAmount, decimal grossAmount, string? revenueAccountRoleKey, string? taxAccountRoleKey,
        string vatBoxMappingsJson, string taxEvidenceJson, string dimensionFactsJson, string? sourceReference,
        string? orderReference)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("Company is required.") : companyId;
        DraftId = draftId == Guid.Empty ? throw new ArgumentException("Draft is required.") : draftId;
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (quantity <= 0m) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitPrice < 0m || discountPercent is < 0m or > 100m) throw new ArgumentOutOfRangeException(nameof(unitPrice));
        Sequence = sequence;
        Description = Required(description, nameof(description), 500);
        Quantity = decimal.Round(quantity, 6, MidpointRounding.ToEven);
        Unit = Required(unit, nameof(unit), 32);
        UnitPrice = decimal.Round(unitPrice, 6, MidpointRounding.ToEven);
        DiscountPercent = decimal.Round(discountPercent, 6, MidpointRounding.ToEven);
        DiscountAmount = Amount(discountAmount, nameof(discountAmount));
        NetAmount = Amount(netAmount, nameof(netAmount));
        TaxRuleKey = Required(taxRuleKey, nameof(taxRuleKey), 100);
        TaxRuleVersion = Required(taxRuleVersion, nameof(taxRuleVersion), 64);
        TaxClassification = Required(taxClassification, nameof(taxClassification), 100);
        TaxRate = decimal.Round(taxRate, 6, MidpointRounding.ToEven);
        TaxAmount = Amount(taxAmount, nameof(taxAmount));
        GrossAmount = Amount(grossAmount, nameof(grossAmount));
        RevenueAccountRoleKey = Optional(revenueAccountRoleKey, nameof(revenueAccountRoleKey), 100);
        TaxAccountRoleKey = Optional(taxAccountRoleKey, nameof(taxAccountRoleKey), 100);
        VatBoxMappingsJson = Required(vatBoxMappingsJson, nameof(vatBoxMappingsJson), 2000);
        TaxEvidenceJson = Required(taxEvidenceJson, nameof(taxEvidenceJson), 8000);
        DimensionFactsJson = Required(dimensionFactsJson, nameof(dimensionFactsJson), 8000);
        SourceReference = Optional(sourceReference, nameof(sourceReference), 200);
        OrderReference = Optional(orderReference, nameof(orderReference), 200);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DraftId { get; private set; }
    public int Sequence { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public decimal DiscountPercent { get; private set; }
    public decimal DiscountAmount { get; private set; }
    public decimal NetAmount { get; private set; }
    public string TaxRuleKey { get; private set; } = null!;
    public string TaxRuleVersion { get; private set; } = null!;
    public string TaxClassification { get; private set; } = null!;
    public decimal TaxRate { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal GrossAmount { get; private set; }
    public string? RevenueAccountRoleKey { get; private set; }
    public string? TaxAccountRoleKey { get; private set; }
    public string VatBoxMappingsJson { get; private set; } = null!;
    public string TaxEvidenceJson { get; private set; } = null!;
    public string DimensionFactsJson { get; private set; } = null!;
    public string? SourceReference { get; private set; }
    public string? OrderReference { get; private set; }
    public CustomerInvoiceDraft Draft { get; private set; } = null!;

    private static decimal Amount(decimal value, string name) => value < 0m ? throw new ArgumentOutOfRangeException(name) : decimal.Round(value, 6, MidpointRounding.ToEven);
    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(name);
    }
    private static string? Optional(string? value, string name, int maxLength) => string.IsNullOrWhiteSpace(value) ? null : Required(value, name, maxLength);
}

public sealed class CustomerInvoiceDraftEvidenceLink : ICompanyOwnedEntity
{
    private CustomerInvoiceDraftEvidenceLink() { }

    public CustomerInvoiceDraftEvidenceLink(Guid id, Guid companyId, Guid draftId, Guid documentId,
        string contentHash, string title, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("Company is required.") : companyId;
        DraftId = draftId == Guid.Empty ? throw new ArgumentException("Draft is required.") : draftId;
        DocumentId = documentId == Guid.Empty ? throw new ArgumentException("Document is required.") : documentId;
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
    public CustomerInvoiceDraft Draft { get; private set; } = null!;
    public CompanyKnowledgeDocument Document { get; private set; } = null!;
}

public sealed class CustomerInvoiceDraftOperation : ICompanyOwnedEntity
{
    private CustomerInvoiceDraftOperation() { }

    public CustomerInvoiceDraftOperation(Guid id, Guid companyId, Guid draftId, string action,
        string idempotencyKey, string payloadHash, long resultVersion, Guid? approvalRequestId, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DraftId = draftId;
        Action = action.Trim().ToLowerInvariant();
        IdempotencyKey = idempotencyKey.Trim();
        PayloadHash = payloadHash.Trim().ToLowerInvariant();
        ResultVersion = resultVersion;
        ApprovalRequestId = approvalRequestId;
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
    public DateTime CreatedUtc { get; private set; }
    public CustomerInvoiceDraft Draft { get; private set; } = null!;
}
