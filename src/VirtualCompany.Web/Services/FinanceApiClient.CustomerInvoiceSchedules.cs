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
