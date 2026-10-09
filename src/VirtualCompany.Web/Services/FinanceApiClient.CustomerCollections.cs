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

public sealed record RecordCustomerPromiseApiRequest(
    decimal Amount,
    DateOnly DueDate,
    Guid? OwnerUserId,
    DateTime? FollowUpDueUtc,
    long? ExpectedVersion,
    string IdempotencyKey);
