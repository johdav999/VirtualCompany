using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using Microsoft.Extensions.Logging;
using VirtualCompany.Shared;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<FinancePaymentResponse> CreatePaymentAsync(
        Guid companyId,
        CreateFinancePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CreateFinancePaymentRequest, FinancePaymentResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/payments",
            request,
            cancellationToken);
    }

    public Task<IReadOnlyList<FinanceInvoiceResponse>> GetInvoicesAsync(Guid companyId, DateTime? startUtc = null, DateTime? endUtc = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceInvoiceResponse>>([]);
        }

        var uri = $"internal/companies/{companyId}/finance/invoices{BuildQuery(("startUtc", startUtc?.ToString("O")), ("endUtc", endUtc?.ToString("O")), ("limit", limit.ToString()), ("source", _financeDataSourceFilter))}";
        return GetListAsync<FinanceInvoiceResponse>(companyId, uri, cancellationToken);
    }

    public Task<IReadOnlyList<FinanceCounterpartyResponse>> GetCustomersAsync(Guid companyId, int limit = 200, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceCounterpartyResponse>>([]);
        }

        return GetListAsync<FinanceCounterpartyResponse>(companyId, $"internal/companies/{companyId}/finance/customers?limit={limit}", cancellationToken);
    }

    public Task<IReadOnlyList<FinanceCounterpartyResponse>> GetSuppliersAsync(Guid companyId, int limit = 200, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceCounterpartyResponse>>([]);
        }

        return GetListAsync<FinanceCounterpartyResponse>(companyId, $"internal/companies/{companyId}/finance/suppliers?limit={limit}", cancellationToken);
    }

    public Task<FinanceCounterpartyResponse?> GetCustomerAsync(Guid companyId, Guid counterpartyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode ? Task.FromResult<FinanceCounterpartyResponse?>(null) : GetAsync<FinanceCounterpartyResponse>(companyId, $"internal/companies/{companyId}/finance/customers/{counterpartyId}", allowNotFound: true, cancellationToken);

    public Task<FinanceCounterpartyResponse?> GetSupplierAsync(Guid companyId, Guid counterpartyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode ? Task.FromResult<FinanceCounterpartyResponse?>(null) : GetAsync<FinanceCounterpartyResponse>(companyId, $"internal/companies/{companyId}/finance/suppliers/{counterpartyId}", allowNotFound: true, cancellationToken);

    public Task<FinanceCounterpartyResponse> CreateCustomerAsync(Guid companyId, UpsertFinanceCounterpartyRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<UpsertFinanceCounterpartyRequest, FinanceCounterpartyResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/customers", request, cancellationToken);

    public Task<FinanceCounterpartyResponse> UpdateCustomerAsync(Guid companyId, Guid counterpartyId, UpsertFinanceCounterpartyRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<UpsertFinanceCounterpartyRequest, FinanceCounterpartyResponse>(companyId, HttpMethod.Put, $"internal/companies/{companyId}/finance/customers/{counterpartyId}", request, cancellationToken);

    public Task<FinanceCounterpartyResponse> CreateSupplierAsync(Guid companyId, UpsertFinanceCounterpartyRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<UpsertFinanceCounterpartyRequest, FinanceCounterpartyResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/suppliers", request, cancellationToken);

    public Task<FinanceCounterpartyResponse> UpdateSupplierAsync(Guid companyId, Guid counterpartyId, UpsertFinanceCounterpartyRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<UpsertFinanceCounterpartyRequest, FinanceCounterpartyResponse>(companyId, HttpMethod.Put, $"internal/companies/{companyId}/finance/suppliers/{counterpartyId}", request, cancellationToken);

    public Task<FinanceInvoiceDetailResponse?> GetInvoiceDetailAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceInvoiceDetailResponse?>(null)
            : GetAsync<FinanceInvoiceDetailResponse>(companyId, $"internal/companies/{companyId}/finance/invoices/{invoiceId}{BuildQuery(("source", _financeDataSourceFilter))}", allowNotFound: true, cancellationToken);

    public Task<CustomerInvoiceAccountingReferenceDataResponse> GetCustomerInvoiceAccountingReferenceDataAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken = default) =>
        GetAsync<CustomerInvoiceAccountingReferenceDataResponse>(companyId,
            $"internal/companies/{companyId}/finance/invoices/{invoiceId}/accounting/reference-data", allowNotFound: false, cancellationToken)!;

    public Task<CustomerInvoiceAccountingPreviewResponse> PreviewCustomerInvoiceAccountingAsync(Guid companyId, Guid invoiceId,
        CustomerInvoiceAccountingApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<CustomerInvoiceAccountingApiRequest, CustomerInvoiceAccountingPreviewResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/invoices/{invoiceId}/accounting/preview", request, cancellationToken);

    public Task<CustomerInvoiceAccountingSubmissionResponse> SubmitCustomerInvoiceAccountingAsync(Guid companyId, Guid invoiceId,
        SubmitCustomerInvoiceAccountingApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<SubmitCustomerInvoiceAccountingApiRequest, CustomerInvoiceAccountingSubmissionResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/invoices/{invoiceId}/accounting/submit", request, cancellationToken);

    public Task<CustomerInvoiceAccountingPostingResponse> PostCustomerInvoiceAccountingAsync(Guid companyId, Guid invoiceId,
        PostCustomerInvoiceAccountingApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<PostCustomerInvoiceAccountingApiRequest, CustomerInvoiceAccountingPostingResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/invoices/{invoiceId}/accounting/post", request, cancellationToken);

    public Task<CustomerInvoiceAccountingStateResponse> GetCustomerInvoiceAccountingAsync(Guid companyId, Guid invoiceId,
        CancellationToken cancellationToken = default) =>
        GetAsync<CustomerInvoiceAccountingStateResponse>(companyId,
            $"internal/companies/{companyId}/finance/invoices/{invoiceId}/accounting", allowNotFound: false, cancellationToken)!;

    public Task<CustomerInvoiceAccountingStateResponse> CreateCustomerCreditNoteAsync(Guid companyId, Guid invoiceId,
        CreateCustomerCreditNoteApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<CreateCustomerCreditNoteApiRequest, CustomerInvoiceAccountingStateResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/invoices/{invoiceId}/credit-notes", request, cancellationToken);

    public Task<CustomerInvoiceReceivableReconciliationResponse> GetCustomerInvoiceReceivableReconciliationAsync(Guid companyId,
        DateOnly? throughDate = null, CancellationToken cancellationToken = default)
    {
        var query = throughDate.HasValue ? $"?throughDate={throughDate:yyyy-MM-dd}" : string.Empty;
        return GetAsync<CustomerInvoiceReceivableReconciliationResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/reconciliation/receivables{query}", allowNotFound: false, cancellationToken)!;
    }

    public Task<CustomerInvoiceFortnoxActionResponse> RequestCustomerInvoiceFortnoxExportAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending customer invoice Fortnox export request. CompanyId: {CompanyId}. InvoiceId: {InvoiceId}.",
            companyId,
            invoiceId);

        return SendCompanyScopedAsync<object, CustomerInvoiceFortnoxActionResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/invoices/{invoiceId}/fortnox-export",
            new { },
            cancellationToken);
    }

    public Task<CustomerInvoiceFortnoxActionResponse> ExecuteCustomerInvoiceFortnoxExportAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending customer invoice Fortnox export execution request. CompanyId: {CompanyId}. InvoiceId: {InvoiceId}.",
            companyId,
            invoiceId);

        return SendCompanyScopedAsync<object, CustomerInvoiceFortnoxActionResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/invoices/{invoiceId}/fortnox-export/execute",
            new { },
            cancellationToken);
    }

    public Task<CustomerInvoiceFortnoxActionResponse> RequestCustomerInvoiceFortnoxBookkeepAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending customer invoice Fortnox bookkeeping request. CompanyId: {CompanyId}. InvoiceId: {InvoiceId}.",
            companyId,
            invoiceId);

        return SendCompanyScopedAsync<object, CustomerInvoiceFortnoxActionResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/invoices/{invoiceId}/fortnox-bookkeep",
            new { },
            cancellationToken);
    }

    public Task<CustomerInvoiceFortnoxActionResponse> ExecuteCustomerInvoiceFortnoxBookkeepAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending customer invoice Fortnox bookkeeping execution request. CompanyId: {CompanyId}. InvoiceId: {InvoiceId}.",
            companyId,
            invoiceId);

        return SendCompanyScopedAsync<object, CustomerInvoiceFortnoxActionResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/invoices/{invoiceId}/fortnox-bookkeep/execute",
            new { },
            cancellationToken);
    }

}

