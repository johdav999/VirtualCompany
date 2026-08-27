using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Finance;

public static class CustomerInvoiceScheduleReasonCodes
{
    public const string NotFound = "customer_invoice_schedule_not_found";
    public const string VersionConflict = "customer_invoice_schedule_version_conflict";
    public const string IdempotencyConflict = "customer_invoice_schedule_idempotency_conflict";
    public const string NotEditable = "customer_invoice_schedule_not_editable";
    public const string InvalidTemplate = "customer_invoice_schedule_invalid_template";
    public const string InvalidOccurrence = "customer_invoice_schedule_invalid_occurrence";
    public const string AutoIssueNotPermitted = "customer_invoice_schedule_auto_issue_not_permitted";
    public const string ApprovalRequired = "customer_invoice_schedule_approval_required";
    public const string ApprovalPending = "customer_invoice_schedule_approval_pending";
    public const string ApprovalRejected = "customer_invoice_schedule_approval_rejected";
    public const string ApprovalStale = "customer_invoice_schedule_approval_stale";
    public const string OccurrenceBlocked = "customer_invoice_schedule_occurrence_blocked";
}

public sealed record CustomerInvoiceScheduleLineInput(
    int Sequence, string Description, decimal Quantity, string Unit, decimal UnitPrice,
    decimal DiscountPercent, string TaxRuleKey, string TaxClassification,
    IReadOnlyList<CustomerInvoiceDraftTaxEvidenceInput> TaxEvidence,
    IReadOnlyDictionary<string, string>? DimensionFacts = null, string? RevenueAccountRoleKey = null,
    string? SourceReference = null, string? OrderReference = null);

public sealed record CustomerInvoiceScheduleInput(
    Guid CustomerId, string Name, DateOnly StartDate, DateOnly? EndDate, string Cadence,
    int BillingDay, string TimeZoneId, string BusinessDayConvention, string ProrationRule,
    int DueDateOffsetDays, string DocumentType, string Currency, string PaymentTermKind,
    int PaymentTermDays, string? BuyerReference, string? SellerReference, string? Notes,
    string DeliveryIntent, bool AutoIssueEnabled, IReadOnlyList<CustomerInvoiceScheduleLineInput> Lines,
    IReadOnlyList<Guid> EvidenceDocumentIds);

public sealed record CreateCustomerInvoiceScheduleCommand(Guid CompanyId, CustomerInvoiceScheduleInput Schedule,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record UpdateCustomerInvoiceScheduleCommand(Guid CompanyId, Guid ScheduleId, long ExpectedVersion,
    CustomerInvoiceScheduleInput Schedule, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record CustomerInvoiceScheduleActionCommand(Guid CompanyId, Guid ScheduleId, long ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null,
    bool AllowBackdatedGeneration = false, bool RetryBlockedOccurrence = false);
public sealed record SubmitCustomerInvoiceScheduleForApprovalCommand(Guid CompanyId, Guid ScheduleId,
    long ExpectedVersion, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record GetCustomerInvoiceScheduleQuery(Guid CompanyId, Guid ScheduleId);
public sealed record ListCustomerInvoiceSchedulesQuery(Guid CompanyId, string? Status = null, Guid? CustomerId = null,
    int Skip = 0, int Take = 100);
public sealed record PreviewCustomerInvoiceScheduleQuery(Guid CompanyId, Guid ScheduleId, int Count = 12);

public sealed record CustomerInvoiceScheduleLineDto(int Sequence, string Description, decimal Quantity, string Unit,
    decimal UnitPrice, decimal DiscountPercent, string TaxRuleKey, string TaxClassification,
    IReadOnlyList<CustomerInvoiceDraftTaxEvidenceInput> TaxEvidence, IReadOnlyDictionary<string, string> DimensionFacts,
    string? RevenueAccountRoleKey, string? SourceReference, string? OrderReference);
public sealed record CustomerInvoiceScheduleOccurrenceDto(Guid Id, DateOnly OccurrenceDate, DateOnly IssueDate,
    DateOnly DueDate, long ScheduleVersion, long TemplateVersion, string TemplateHash, long Version,
    string Status, Guid? DraftId, Guid? TaskId, int AttemptCount, string? FailureCode,
    string? FailureSummary, DateTime? LeaseExpiresUtc, DateTime? NextAttemptUtc,
    DateTime CreatedUtc, DateTime UpdatedUtc);
public sealed record CustomerInvoiceScheduleApprovalDto(Guid Id, string Status, long TemplateVersion,
    string TemplateHash, string? DecisionSummary, DateTime CreatedUtc, DateTime? DecidedUtc, bool IsCurrent);
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
public sealed record CustomerInvoiceSchedulePreviewOccurrenceDto(DateOnly OccurrenceDate, DateOnly IssueDate,
    DateOnly DueDate, DateOnly SupplyDate, string RuleExplanation, decimal ExpectedNetAmount,
    decimal ExpectedTaxAmount, decimal ExpectedGrossAmount, string Currency,
    IReadOnlyList<CustomerInvoiceDraftIssue> Warnings, IReadOnlyList<CustomerInvoiceDraftIssue> Blockers);
public sealed record CustomerInvoiceSchedulePreviewDto(Guid ScheduleId, long ScheduleVersion,
    long TemplateVersion, string TemplateHash, IReadOnlyList<CustomerInvoiceSchedulePreviewOccurrenceDto> Occurrences);
public sealed record CustomerInvoiceScheduleSubmissionResult(CustomerInvoiceScheduleDto Schedule,
    Guid ApprovalRequestId, bool IsIdempotentReplay);
public sealed record CustomerInvoiceScheduleListResult(IReadOnlyList<CustomerInvoiceScheduleDto> Items,
    int TotalCount, int Skip, int Take);

public interface ICustomerInvoiceScheduleService
{
    Task<CustomerInvoiceScheduleDto> CreateAsync(CreateCustomerInvoiceScheduleCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceScheduleDto> UpdateAsync(UpdateCustomerInvoiceScheduleCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceScheduleDto> ActivateAsync(CustomerInvoiceScheduleActionCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceScheduleDto> PauseAsync(CustomerInvoiceScheduleActionCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceScheduleDto> ResumeAsync(CustomerInvoiceScheduleActionCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceScheduleDto> EndAsync(CustomerInvoiceScheduleActionCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceScheduleSubmissionResult> SubmitAsync(SubmitCustomerInvoiceScheduleForApprovalCommand command, CancellationToken cancellationToken);
    Task<CustomerInvoiceScheduleDto> GetAsync(GetCustomerInvoiceScheduleQuery query, CancellationToken cancellationToken);
    Task<CustomerInvoiceScheduleListResult> ListAsync(ListCustomerInvoiceSchedulesQuery query, CancellationToken cancellationToken);
    Task<CustomerInvoiceSchedulePreviewDto> PreviewAsync(PreviewCustomerInvoiceScheduleQuery query, CancellationToken cancellationToken);
}

public interface ICustomerInvoiceScheduleGenerationRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public sealed record CustomerInvoiceScheduleOccurrenceDecision(bool IsAllowed, string ReasonCode,
    string Explanation, decimal NetTotal, decimal TaxTotal, decimal GrossTotal, string Currency,
    IReadOnlyList<CustomerInvoiceDraftIssue> Warnings, IReadOnlyList<CustomerInvoiceDraftIssue> Blockers);

public interface ICustomerInvoiceScheduleOccurrencePolicy
{
    Task<CustomerInvoiceScheduleOccurrenceDecision> EvaluateAsync(Guid companyId,
        CustomerInvoiceDraftInput input, CancellationToken cancellationToken);
}

public sealed class CustomerInvoiceScheduleException : Exception
{
    public CustomerInvoiceScheduleException(string reasonCode, string message, bool isConflict = false,
        long? currentVersion = null) : base(message)
    { ReasonCode = reasonCode; IsConflict = isConflict; CurrentVersion = currentVersion; }
    public string ReasonCode { get; }
    public bool IsConflict { get; }
    public long? CurrentVersion { get; }
}
