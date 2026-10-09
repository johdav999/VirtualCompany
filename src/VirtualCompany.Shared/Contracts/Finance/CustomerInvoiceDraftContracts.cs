using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Finance;


public sealed record CustomerInvoiceDraftTaxEvidenceInput(string Classification, string? SourceReference = null);

public sealed record CustomerInvoiceDraftLineDto(Guid Id, int Sequence, string Description, decimal Quantity,
    string Unit, decimal UnitPrice, decimal DiscountPercent, decimal DiscountAmount, decimal NetAmount,
    string TaxRuleKey, string TaxRuleVersion, string TaxClassification, decimal TaxRate, decimal TaxAmount,
    decimal GrossAmount, string? RevenueAccountRoleKey, string? TaxAccountRoleKey,
    IReadOnlyList<string> VatBoxMappings, IReadOnlyList<CustomerInvoiceDraftTaxEvidenceInput> TaxEvidence,
    IReadOnlyDictionary<string, string> DimensionFacts, string? SourceReference, string? OrderReference);

public sealed record CustomerInvoiceDraftDto(Guid Id, Guid CompanyId, Guid CustomerId, string CustomerName,
    string Status, string DocumentType, DateOnly IssueDate, DateOnly SupplyDate, DateOnly DueDate,
    string Currency, string PaymentTermKind, int PaymentTermDays, string? BuyerReference,
    string? SellerReference, string? Notes, string DeliveryIntent, string SourceKind, string? SourceReference,
    long Version, string InputHash, string ResultHash, string PolicyPackKey, string PolicyPackVersion,
    string PolicyDefinitionHash, Guid CreatedByUserId, Guid UpdatedByUserId, DateTime CreatedUtc,
    DateTime UpdatedUtc, DateTime? DiscardedUtc, CustomerInvoiceDraftTotalsDto Totals,
    IReadOnlyList<CustomerInvoiceDraftLineDto> Lines, IReadOnlyList<CustomerInvoiceDraftEvidenceDto> Evidence,
    IReadOnlyList<CustomerInvoiceDraftIssue> Warnings, IReadOnlyList<CustomerInvoiceDraftIssue> Blockers,
    CustomerInvoiceDraftApprovalDto? Approval, Guid? OriginalInvoiceId = null);

public sealed record CustomerInvoiceDraftTotalsDto(decimal NetTotal, decimal DiscountTotal, decimal TaxTotal,
    decimal GrossTotal, decimal RoundingAmount, int RoundingPrecision, string RoundingMode);


public sealed record CustomerInvoiceDraftIssue(string ReasonCode, string Explanation, Guid? RelatedEntityId = null);

public sealed record CustomerInvoiceDraftEvidenceDto(Guid DocumentId, string Title, string ContentHash, string OriginalFileName);

public sealed record CustomerInvoiceDraftReadinessDto(bool IsAllowed, string ReasonCode, string Explanation,
    bool RequiresApproval, decimal ApprovalThreshold, string ApprovalCurrency,
    IReadOnlyList<CustomerInvoiceDraftIssue> Blockers, IReadOnlyList<CustomerInvoiceDraftIssue> Warnings,
    IReadOnlyDictionary<string, string> Evidence);

public sealed record CustomerInvoiceDraftPreviewDto(CustomerInvoiceDraftDto Draft, bool IsDeterministicReplay);

public sealed record CustomerInvoiceDraftApprovalDto(Guid Id, string Status, string? DecisionSummary,
    long DraftVersion, string ResultHash, DateTime CreatedUtc, DateTime? DecidedUtc, bool IsCurrent);

public sealed record CustomerInvoiceDraftIssueResult(Guid InvoiceId, Guid IssuedDocumentId,
    Guid LedgerEntryId, string DocumentNumber, string DeliveryState, string SnapshotHash,
    decimal NetTotal, decimal TaxTotal, decimal GrossTotal, string Currency,
    IReadOnlyList<string> AllowedNextActions, bool IsIdempotentReplay);

public sealed record CustomerInvoiceDraftListResult(IReadOnlyList<CustomerInvoiceDraftDto> Items,
    int TotalCount, int Skip, int Take);

public sealed record CustomerInvoiceDraftSubmissionResult(CustomerInvoiceDraftDto Draft,
    CustomerInvoiceDraftReadinessDto Readiness, Guid ApprovalRequestId, bool IsIdempotentReplay);
