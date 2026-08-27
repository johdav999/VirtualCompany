namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<CustomerInvoiceScheduleListResponse?> GetCustomerInvoiceSchedulesAsync(Guid companyId, string? status = null,
        Guid? customerId = null, int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        var query = $"skip={Math.Max(0, skip)}&take={Math.Clamp(take, 1, 200)}" +
            (string.IsNullOrWhiteSpace(status) ? string.Empty : $"&status={Uri.EscapeDataString(status)}") +
            (customerId.HasValue ? $"&customerId={customerId:D}" : string.Empty);
        return GetAsync<CustomerInvoiceScheduleListResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/customer-invoice-schedules?{query}", false, cancellationToken);
    }
    public Task<CustomerInvoiceScheduleResponse?> GetCustomerInvoiceScheduleAsync(Guid companyId, Guid scheduleId, CancellationToken cancellationToken = default) => GetAsync<CustomerInvoiceScheduleResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/customer-invoice-schedules/{scheduleId:D}", true, cancellationToken);
    public Task<CustomerInvoiceSchedulePreviewResponse?> PreviewCustomerInvoiceScheduleAsync(Guid companyId, Guid scheduleId, int count = 12, CancellationToken cancellationToken = default) => GetAsync<CustomerInvoiceSchedulePreviewResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/customer-invoice-schedules/{scheduleId:D}/preview?count={Math.Clamp(count, 1, 24)}", false, cancellationToken);
    public Task<CustomerInvoiceScheduleResponse> CreateCustomerInvoiceScheduleAsync(Guid companyId, SaveCustomerInvoiceScheduleApiRequest request, CancellationToken cancellationToken = default) { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveCustomerInvoiceScheduleApiRequest, CustomerInvoiceScheduleResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-schedules", request, cancellationToken); }
    public Task<CustomerInvoiceScheduleResponse> UpdateCustomerInvoiceScheduleAsync(Guid companyId, Guid scheduleId, SaveCustomerInvoiceScheduleApiRequest request, CancellationToken cancellationToken = default) { EnsureOnlineMutation(); return SendCompanyScopedAsync<SaveCustomerInvoiceScheduleApiRequest, CustomerInvoiceScheduleResponse>(companyId, HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/customer-invoice-schedules/{scheduleId:D}", request, cancellationToken); }
    public Task<CustomerInvoiceScheduleSubmissionResponse> SubmitCustomerInvoiceScheduleAsync(Guid companyId, Guid scheduleId, CustomerInvoiceScheduleActionApiRequest request, CancellationToken cancellationToken = default) { EnsureOnlineMutation(); return SendCompanyScopedAsync<CustomerInvoiceScheduleActionApiRequest, CustomerInvoiceScheduleSubmissionResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-schedules/{scheduleId:D}/submit", request, cancellationToken); }
    public Task<CustomerInvoiceScheduleResponse> ChangeCustomerInvoiceScheduleStatusAsync(Guid companyId, Guid scheduleId, string action, CustomerInvoiceScheduleActionApiRequest request, CancellationToken cancellationToken = default) { EnsureOnlineMutation(); return SendCompanyScopedAsync<CustomerInvoiceScheduleActionApiRequest, CustomerInvoiceScheduleResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-schedules/{scheduleId:D}/{action}", request, cancellationToken); }
}

public sealed record SaveCustomerInvoiceScheduleApiRequest(long ExpectedVersion, string IdempotencyKey, Guid CustomerId,
    string Name, DateOnly StartDate, DateOnly? EndDate, string Cadence, int BillingDay, string TimeZoneId,
    string BusinessDayConvention, string ProrationRule, int DueDateOffsetDays, string DocumentType, string Currency,
    string PaymentTermKind, int PaymentTermDays, string? BuyerReference, string? SellerReference, string? Notes,
    string DeliveryIntent, bool AutoIssueEnabled, IReadOnlyList<CustomerInvoiceScheduleLineApiRequest> Lines,
    IReadOnlyList<Guid> EvidenceDocumentIds);
public sealed record CustomerInvoiceScheduleLineApiRequest(int Sequence, string Description, decimal Quantity, string Unit,
    decimal UnitPrice, decimal DiscountPercent, string TaxRuleKey, string TaxClassification,
    IReadOnlyList<CustomerInvoiceDraftTaxEvidenceApiRequest> TaxEvidence, IReadOnlyDictionary<string, string>? DimensionFacts = null,
    string? RevenueAccountRoleKey = null, string? SourceReference = null, string? OrderReference = null);
public sealed record CustomerInvoiceScheduleActionApiRequest(long ExpectedVersion, string IdempotencyKey,
    bool AllowBackdatedGeneration = false, bool RetryBlockedOccurrence = false);
public sealed record CustomerInvoiceScheduleLineResponse(int Sequence, string Description, decimal Quantity, string Unit,
    decimal UnitPrice, decimal DiscountPercent, string TaxRuleKey, string TaxClassification,
    IReadOnlyList<CustomerInvoiceDraftTaxEvidenceResponse> TaxEvidence, IReadOnlyDictionary<string, string> DimensionFacts,
    string? RevenueAccountRoleKey, string? SourceReference, string? OrderReference);
public sealed record CustomerInvoiceScheduleOccurrenceResponse(Guid Id, DateOnly OccurrenceDate, DateOnly IssueDate,
    DateOnly DueDate, long ScheduleVersion, long TemplateVersion, string TemplateHash, long Version,
    string Status, Guid? DraftId, Guid? TaskId, int AttemptCount,
    string? FailureCode, string? FailureSummary, DateTime? LeaseExpiresUtc, DateTime? NextAttemptUtc,
    DateTime CreatedUtc, DateTime UpdatedUtc);
public sealed record CustomerInvoiceScheduleApprovalResponse(Guid Id, string Status, long TemplateVersion,
    string TemplateHash, string? DecisionSummary, DateTime CreatedUtc, DateTime? DecidedUtc, bool IsCurrent);
public sealed record CustomerInvoiceScheduleResponse(Guid Id, Guid CompanyId, Guid CustomerId, string CustomerName,
    string Name, string Status, DateOnly StartDate, DateOnly? EndDate, string Cadence, int BillingDay, string TimeZoneId,
    string BusinessDayConvention, string ProrationRule, int DueDateOffsetDays, string DocumentType, string Currency,
    string PaymentTermKind, int PaymentTermDays, string? BuyerReference, string? SellerReference, string? Notes,
    string DeliveryIntent, bool AutoIssueEnabled, string TemplateHash, long TemplateVersion, long Version,
    DateOnly NextOccurrenceDate, DateTime CreatedUtc,
    DateTime UpdatedUtc, IReadOnlyList<CustomerInvoiceScheduleLineResponse> Lines, IReadOnlyList<Guid> EvidenceDocumentIds,
    IReadOnlyList<CustomerInvoiceScheduleOccurrenceResponse> RecentOccurrences,
    CustomerInvoiceScheduleApprovalResponse? Approval);
public sealed record CustomerInvoiceSchedulePreviewOccurrenceResponse(DateOnly OccurrenceDate, DateOnly IssueDate,
    DateOnly DueDate, DateOnly SupplyDate, string RuleExplanation, decimal ExpectedNetAmount,
    decimal ExpectedTaxAmount, decimal ExpectedGrossAmount, string Currency,
    IReadOnlyList<CustomerInvoiceDraftIssueItemResponse> Warnings,
    IReadOnlyList<CustomerInvoiceDraftIssueItemResponse> Blockers);
public sealed record CustomerInvoiceSchedulePreviewResponse(Guid ScheduleId, long ScheduleVersion,
    long TemplateVersion, string TemplateHash,
    IReadOnlyList<CustomerInvoiceSchedulePreviewOccurrenceResponse> Occurrences);
public sealed record CustomerInvoiceScheduleSubmissionResponse(CustomerInvoiceScheduleResponse Schedule,
    Guid ApprovalRequestId, bool IsIdempotentReplay);
public sealed record CustomerInvoiceScheduleListResponse(IReadOnlyList<CustomerInvoiceScheduleResponse> Items,
    int TotalCount, int Skip, int Take);
