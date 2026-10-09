namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<CustomerInvoiceDraftListResponse?> GetCustomerInvoiceDraftsAsync(Guid companyId,
        string? status = null, Guid? customerId = null, int skip = 0, int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"skip={Math.Max(0, skip)}",
            $"take={Math.Clamp(take, 1, 250)}"
        };
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        if (customerId.HasValue) query.Add($"customerId={customerId.Value:D}");
        return GetAsync<CustomerInvoiceDraftListResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts?{string.Join("&", query)}",
            allowNotFound: false, cancellationToken);
    }

    public Task<CustomerInvoiceDraftResponse?> GetCustomerInvoiceDraftAsync(Guid companyId, Guid draftId,
        CancellationToken cancellationToken = default) => GetAsync<CustomerInvoiceDraftResponse>(companyId,
        $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}",
        allowNotFound: true, cancellationToken);

    public Task<CustomerInvoiceDraftResponse> CreateCustomerInvoiceDraftAsync(Guid companyId,
        SaveCustomerInvoiceDraftApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveCustomerInvoiceDraftApiRequest, CustomerInvoiceDraftResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceDraftResponse> UpdateCustomerInvoiceDraftAsync(Guid companyId, Guid draftId,
        SaveCustomerInvoiceDraftApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveCustomerInvoiceDraftApiRequest, CustomerInvoiceDraftResponse>(companyId,
            HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceDraftResponse> CopyCustomerInvoiceDraftAsync(Guid companyId, Guid draftId,
        CopyCustomerInvoiceDraftApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CopyCustomerInvoiceDraftApiRequest, CustomerInvoiceDraftResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}/copy",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceDraftResponse> DiscardCustomerInvoiceDraftAsync(Guid companyId, Guid draftId,
        CustomerInvoiceDraftVersionedApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CustomerInvoiceDraftVersionedApiRequest, CustomerInvoiceDraftResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}/discard",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceDraftPreviewResponse> PreviewCustomerInvoiceDraftAsync(Guid companyId, Guid draftId,
        long expectedVersion, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<object, CustomerInvoiceDraftPreviewResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}/preview",
            new { expectedVersion }, cancellationToken);

    public Task<CustomerInvoiceDraftReadinessResponse?> GetCustomerInvoiceDraftReadinessAsync(Guid companyId,
        Guid draftId, long expectedVersion, CancellationToken cancellationToken = default) =>
        GetAsync<CustomerInvoiceDraftReadinessResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}/readiness?expectedVersion={expectedVersion}",
            allowNotFound: false, cancellationToken);

    public Task<CustomerInvoiceDraftSubmissionResponse> SubmitCustomerInvoiceDraftAsync(Guid companyId,
        Guid draftId, CustomerInvoiceDraftVersionedApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CustomerInvoiceDraftVersionedApiRequest, CustomerInvoiceDraftSubmissionResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}/submit",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceDraftIssueResponse> IssueCustomerInvoiceDraftAsync(Guid companyId, Guid draftId,
        IssueCustomerInvoiceDraftApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<IssueCustomerInvoiceDraftApiRequest, CustomerInvoiceDraftIssueResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}/issue",
            request, cancellationToken);
    }
}
