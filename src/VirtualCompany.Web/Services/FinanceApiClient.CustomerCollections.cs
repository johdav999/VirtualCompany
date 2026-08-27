namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<CustomerAgingResponse?> GetCustomerAgingAsync(
        Guid companyId,
        DateOnly cutoffDate,
        string timeZoneId,
        Guid? customerId = null,
        string? currency = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default) =>
        GetAsync<CustomerAgingResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/receivables/aging" +
            BuildQuery(
                ("cutoffDate", cutoffDate.ToString("yyyy-MM-dd")),
                ("timeZoneId", timeZoneId),
                ("customerId", customerId?.ToString("D")),
                ("currency", currency),
                ("skip", Math.Max(0, skip).ToString()),
                ("take", Math.Clamp(take, 1, 250).ToString())),
            allowNotFound: false,
            cancellationToken);

    public Task<CustomerCollectionMetricsResponse?> GetCustomerCollectionMetricsAsync(
        Guid companyId,
        DateOnly asOfDate,
        int lookbackDays = 90,
        string? currency = null,
        CancellationToken cancellationToken = default) =>
        GetAsync<CustomerCollectionMetricsResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/customer-collections/metrics" +
            BuildQuery(
                ("asOfDate", asOfDate.ToString("yyyy-MM-dd")),
                ("lookbackDays", Math.Clamp(lookbackDays, 1, 3650).ToString()),
                ("currency", currency)),
            allowNotFound: false,
            cancellationToken);

    public Task<CustomerCollectionCaseListResponse?> GetCustomerCollectionCasesAsync(
        Guid companyId,
        Guid? customerId = null,
        Guid? invoiceId = null,
        string? status = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default) =>
        GetAsync<CustomerCollectionCaseListResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/customer-collections/cases" +
            BuildQuery(
                ("customerId", customerId?.ToString("D")),
                ("invoiceId", invoiceId?.ToString("D")),
                ("status", status),
                ("skip", Math.Max(0, skip).ToString()),
                ("take", Math.Clamp(take, 1, 250).ToString())),
            allowNotFound: false,
            cancellationToken);

    public Task<CustomerStatementListResponse?> GetCustomerStatementsAsync(
        Guid companyId,
        Guid? customerId = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default) =>
        GetAsync<CustomerStatementListResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/customer-statements" +
            BuildQuery(
                ("customerId", customerId?.ToString("D")),
                ("skip", Math.Max(0, skip).ToString()),
                ("take", Math.Clamp(take, 1, 250).ToString())),
            allowNotFound: false,
            cancellationToken);

    public Task<CustomerStatementResponse> GenerateCustomerStatementAsync(
        Guid companyId,
        GenerateCustomerStatementApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<GenerateCustomerStatementApiRequest, CustomerStatementResponse>(companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-statements",
            request,
            cancellationToken);
    }

    public Task<CustomerCollectionCaseResponse> RecordCustomerDisputeAsync(
        Guid companyId,
        Guid invoiceId,
        RecordCustomerDisputeApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<RecordCustomerDisputeApiRequest, CustomerCollectionCaseResponse>(companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/collection-disputes",
            request,
            cancellationToken);
    }

    public Task<CustomerCollectionCaseResponse> RecordCustomerPromiseAsync(
        Guid companyId,
        Guid invoiceId,
        RecordCustomerPromiseApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<RecordCustomerPromiseApiRequest, CustomerCollectionCaseResponse>(companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/promises-to-pay",
            request,
            cancellationToken);
    }

    public Task<CustomerReminderDraftResponse> PrepareCustomerReminderAsync(
        Guid companyId,
        Guid invoiceId,
        PrepareCustomerReminderApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<PrepareCustomerReminderApiRequest, CustomerReminderDraftResponse>(companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/reminders",
            request,
            cancellationToken);
    }

    public Task<CustomerReminderDeliveryResponse> SendCustomerReminderAsync(
        Guid companyId,
        Guid reminderDraftId,
        SendCustomerReminderApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SendCustomerReminderApiRequest, CustomerReminderDeliveryResponse>(companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-reminders/{reminderDraftId:D}/send",
            request,
            cancellationToken);
    }
}

public sealed record CustomerAgingItemResponse(
    Guid InvoiceId,
    Guid CustomerId,
    string InvoiceNumber,
    string CustomerName,
    DateOnly IssuedDate,
    DateOnly DueDate,
    int DaysOverdue,
    string AgingBucket,
    string Currency,
    decimal OriginalAmount,
    decimal AllocatedAmount,
    decimal OpenAmount,
    bool IsDisputed,
    bool IsOnHold,
    string? PromiseStatus,
    DateOnly? PromiseDueDate,
    int ReminderStage,
    decimal? CreditLimit,
    decimal CustomerExposure,
    string RecommendedAction,
    IReadOnlyList<string> EvidenceCitations);

public sealed record CustomerAgingResponse(
    Guid CompanyId,
    DateOnly CutoffDate,
    DateTime CutoffExclusiveUtc,
    string TimeZoneId,
    string Currency,
    int TotalCount,
    decimal Current,
    decimal Days1To30,
    decimal Days31To60,
    decimal Days61To90,
    decimal DaysOver90,
    decimal TotalOpen,
    decimal ControlAccountDifference,
    bool IsControlAccountReconciled,
    IReadOnlyList<CustomerAgingItemResponse> Items);

public sealed record CustomerCollectionMetricsResponse(
    DateOnly AsOfDate,
    string Currency,
    decimal OverdueValue,
    decimal OpenReceivables,
    decimal CreditSalesInWindow,
    int LookbackDays,
    decimal DsoNumerator,
    decimal DsoDenominator,
    decimal? DaysSalesOutstanding,
    int RemindersAccepted,
    int ReminderPayments,
    decimal? ReminderToPaymentConversion,
    int PromisesKept,
    int PromisesBroken,
    decimal AverageDisputeAgeDays,
    int ManualOverrides,
    int CommunicationFailures);

public sealed record CustomerCollectionCaseResponse(
    Guid Id,
    Guid CustomerId,
    Guid InvoiceId,
    string Status,
    int ReminderStage,
    bool IsOnHold,
    string? HoldReason,
    string? DisputeStatus,
    string? DisputeReason,
    decimal? DisputedAmount,
    string? PromiseStatus,
    decimal? PromiseAmount,
    DateOnly? PromiseDueDate,
    Guid? OwnerUserId,
    DateTime? FollowUpDueUtc,
    Guid? WorkTaskId,
    long Version,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record CustomerCollectionCaseListResponse(int TotalCount, IReadOnlyList<CustomerCollectionCaseResponse> Items);

public sealed record CustomerStatementItemResponse(
    Guid Id,
    string ItemType,
    Guid? InvoiceId,
    Guid? PaymentAllocationId,
    DateOnly EffectiveDate,
    string Reference,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal RunningBalance,
    string SourceHash);

public sealed record CustomerStatementResponse(
    Guid Id,
    Guid CustomerId,
    string CustomerName,
    DateOnly FromDate,
    DateOnly CutoffDate,
    string TimeZoneId,
    string Locale,
    string Currency,
    decimal OpeningBalance,
    decimal InvoiceActivity,
    decimal AllocationActivity,
    decimal CreditActivity,
    decimal ClosingBalance,
    string Checksum,
    string SourceManifestHash,
    string MediaType,
    string FileName,
    string ContentHash,
    long ContentLength,
    DateTime CreatedUtc,
    IReadOnlyList<CustomerStatementItemResponse> Items,
    bool IsIdempotentReplay = false);

public sealed record CustomerStatementListResponse(int TotalCount, IReadOnlyList<CustomerStatementResponse> Items);

public sealed record CustomerReminderDraftResponse(
    Guid Id,
    Guid CaseId,
    Guid InvoiceId,
    Guid CustomerId,
    Guid? StatementId,
    int Stage,
    string RecipientEmail,
    string Subject,
    string Body,
    decimal PreparedOpenAmount,
    string Currency,
    string SourceHash,
    string Status,
    Guid? ApprovalRequestId,
    long Version,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    bool IsIdempotentReplay = false);

public sealed record CustomerReminderDeliveryResponse(
    Guid Id,
    Guid ReminderDraftId,
    string Status,
    int Attempts,
    string? ProviderReference,
    string? FailureCode,
    string? FailureSummary,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? AcceptedUtc,
    bool IsIdempotentReplay = false);

public sealed record GenerateCustomerStatementApiRequest(
    Guid CustomerId,
    DateOnly FromDate,
    DateOnly CutoffDate,
    string TimeZoneId,
    string Locale,
    string Currency,
    string IdempotencyKey);

public sealed record RecordCustomerDisputeApiRequest(
    decimal Amount,
    string Reason,
    Guid? OwnerUserId,
    DateTime? FollowUpDueUtc,
    long? ExpectedVersion,
    string IdempotencyKey);

public sealed record RecordCustomerPromiseApiRequest(
    decimal Amount,
    DateOnly DueDate,
    Guid? OwnerUserId,
    DateTime? FollowUpDueUtc,
    long? ExpectedVersion,
    string IdempotencyKey);

public sealed record PrepareCustomerReminderApiRequest(int? RequestedStage, Guid? StatementId, string IdempotencyKey);
public sealed record SendCustomerReminderApiRequest(long ExpectedDraftVersion, string ExpectedSourceHash, string IdempotencyKey);
