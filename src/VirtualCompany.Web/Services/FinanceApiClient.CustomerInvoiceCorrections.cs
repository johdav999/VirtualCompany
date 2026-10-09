namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<CustomerInvoiceCorrectionPolicyResponse?> EvaluateCustomerInvoiceCorrectionAsync(Guid companyId,
        Guid invoiceId, string correctionType, decimal amount, string currency, string? providerKey = null,
        CancellationToken cancellationToken = default)
    {
        var path = $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/corrections/policy" +
            $"?correctionType={Uri.EscapeDataString(correctionType)}&amount={Uri.EscapeDataString(amount.ToString(System.Globalization.CultureInfo.InvariantCulture))}" +
            $"&currency={Uri.EscapeDataString(currency)}" +
            (string.IsNullOrWhiteSpace(providerKey) ? string.Empty : $"&providerKey={Uri.EscapeDataString(providerKey)}");
        return GetAsync<CustomerInvoiceCorrectionPolicyResponse>(companyId, path, false, cancellationToken);
    }

    public Task<CustomerInvoiceCorrectionResponse> ProposeCustomerInvoiceCorrectionAsync(Guid companyId,
        Guid invoiceId, ProposeCustomerInvoiceCorrectionApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ProposeCustomerInvoiceCorrectionApiRequest, CustomerInvoiceCorrectionResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/corrections",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceCorrectionListResponse?> GetCustomerInvoiceCorrectionsAsync(Guid companyId,
        Guid? invoiceId = null, string? status = null, int skip = 0, int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"skip={Math.Max(0, skip)}", $"take={Math.Clamp(take, 1, 250)}" };
        if (invoiceId.HasValue) query.Add($"invoiceId={invoiceId:D}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        return GetAsync<CustomerInvoiceCorrectionListResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-corrections?{string.Join("&", query)}",
            false, cancellationToken);
    }

    public Task<CustomerInvoiceCorrectionResponse?> GetCustomerInvoiceCorrectionAsync(Guid companyId,
        Guid correctionId, CancellationToken cancellationToken = default) =>
        GetAsync<CustomerInvoiceCorrectionResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-corrections/{correctionId:D}",
            true, cancellationToken);

    public Task<CustomerInvoiceCorrectionResponse> ExecuteCustomerInvoiceCorrectionAsync(Guid companyId,
        Guid correctionId, ExecuteCustomerInvoiceCorrectionApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ExecuteCustomerInvoiceCorrectionApiRequest, CustomerInvoiceCorrectionResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-corrections/{correctionId:D}/execute",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceCorrectionResponse> ReconcileCustomerInvoiceRefundAsync(Guid companyId,
        Guid correctionId, ReconcileCustomerInvoiceRefundApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ReconcileCustomerInvoiceRefundApiRequest, CustomerInvoiceCorrectionResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-corrections/{correctionId:D}/refund-reconciliation",
            request, cancellationToken);
    }
}
