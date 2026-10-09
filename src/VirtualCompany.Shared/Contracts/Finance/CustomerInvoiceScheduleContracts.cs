using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Finance;

public sealed record CustomerInvoiceSchedulePreviewOccurrenceDto(DateOnly OccurrenceDate, DateOnly IssueDate,
    DateOnly DueDate, DateOnly SupplyDate, string RuleExplanation, decimal ExpectedNetAmount,
    decimal ExpectedTaxAmount, decimal ExpectedGrossAmount, string Currency,
    IReadOnlyList<CustomerInvoiceDraftIssue> Warnings, IReadOnlyList<CustomerInvoiceDraftIssue> Blockers);

public sealed record CustomerInvoiceScheduleDto(Guid Id, Guid CompanyId, Guid CustomerId, string CustomerName,
    string Name, string Status, DateOnly StartDate, DateOnly? EndDate, string Cadence, int BillingDay,
    string TimeZoneId, string BusinessDayConvention, string ProrationRule, int DueDateOffsetDays,
    string DocumentType, string Currency, string PaymentTermKind, int PaymentTermDays, string? BuyerReference,
    string? SellerReference, string? Notes, string DeliveryIntent, bool AutoIssueEnabled, string TemplateHash,
    long TemplateVersion, long Version,
    DateOnly NextOccurrenceDate, DateTime CreatedUtc, DateTime UpdatedUtc,
    IReadOnlyList<CustomerInvoiceScheduleLineDto> Lines, IReadOnlyList<Guid> EvidenceDocumentIds,
    IReadOnlyList<CustomerInvoiceScheduleOccurrenceDto> RecentOccurrences,
    CustomerInvoiceScheduleApprovalDto? Approval);

public sealed record CustomerInvoiceScheduleOccurrenceDto(Guid Id, DateOnly OccurrenceDate, DateOnly IssueDate,
    DateOnly DueDate, long ScheduleVersion, long TemplateVersion, string TemplateHash, long Version,
    string Status, Guid? DraftId, Guid? TaskId, int AttemptCount, string? FailureCode,
    string? FailureSummary, DateTime? LeaseExpiresUtc, DateTime? NextAttemptUtc,
    DateTime CreatedUtc, DateTime UpdatedUtc);

public sealed record CustomerInvoiceScheduleApprovalDto(Guid Id, string Status, long TemplateVersion,
    string TemplateHash, string? DecisionSummary, DateTime CreatedUtc, DateTime? DecidedUtc, bool IsCurrent);

public sealed record CustomerInvoiceSchedulePreviewDto(Guid ScheduleId, long ScheduleVersion,
    long TemplateVersion, string TemplateHash, IReadOnlyList<CustomerInvoiceSchedulePreviewOccurrenceDto> Occurrences);


public sealed record CustomerInvoiceScheduleLineDto(int Sequence, string Description, decimal Quantity, string Unit,
    decimal UnitPrice, decimal DiscountPercent, string TaxRuleKey, string TaxClassification,
    IReadOnlyList<CustomerInvoiceDraftTaxEvidenceInput> TaxEvidence, IReadOnlyDictionary<string, string> DimensionFacts,
    string? RevenueAccountRoleKey, string? SourceReference, string? OrderReference);

public sealed record CustomerInvoiceScheduleListResult(IReadOnlyList<CustomerInvoiceScheduleDto> Items,
    int TotalCount, int Skip, int Take);

public sealed record CustomerInvoiceScheduleSubmissionResult(CustomerInvoiceScheduleDto Schedule,
    Guid ApprovalRequestId, bool IsIdempotentReplay);
