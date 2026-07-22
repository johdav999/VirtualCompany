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
    public Task<IReadOnlyList<FinanceBillResponse>> GetBillsAsync(
        Guid companyId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FinanceBillResponse>>([]);
        }

        var uri = $"internal/companies/{companyId}/finance/bills{BuildQuery(("limit", limit.ToString(CultureInfo.InvariantCulture)), ("source", _financeDataSourceFilter))}";
        return GetListAsync<FinanceBillResponse>(companyId, uri, cancellationToken);
    }

    public Task<FinanceBillDetailResponse?> GetBillDetailAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<FinanceBillDetailResponse?>(null)
            : GetAsync<FinanceBillDetailResponse>(companyId, $"internal/companies/{companyId}/finance/bills/{billId}", allowNotFound: true, cancellationToken);

    public Task<SupplierInvoicePaymentProposalResponse> RequestSupplierBillPaymentProposalAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending supplier bill payment proposal request. CompanyId: {CompanyId}. BillId: {BillId}.",
            companyId,
            billId);

        return SendCompanyScopedAsync<object, SupplierInvoicePaymentProposalResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/payment-proposal",
            new { },
            cancellationToken);
    }

    public Task<SupplierInvoicePaymentProposalResponse> ExportSupplierBillPaymentInstructionAsync(
        Guid companyId,
        Guid billId,
        string exportMode = "register_payment",
        CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending supplier bill payment export request. CompanyId: {CompanyId}. BillId: {BillId}. ExportMode: {ExportMode}.",
            companyId,
            billId,
            exportMode);

        return SendCompanyScopedAsync<ExportSupplierBillPaymentInstructionRequest, SupplierInvoicePaymentProposalResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/payment-proposal/export",
            new ExportSupplierBillPaymentInstructionRequest { ExportMode = exportMode },
            cancellationToken);
    }

    public Task<SupplierInvoiceSourceDocumentAttachmentResponse> AttachSupplierBillSourceDocumentAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending supplier bill source document attachment request. CompanyId: {CompanyId}. BillId: {BillId}.",
            companyId,
            billId);

        return SendCompanyScopedAsync<object, SupplierInvoiceSourceDocumentAttachmentResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/source-document-attachment",
            new { },
            cancellationToken);
    }

    public Task<SupplierInvoiceDraftActionResponse> UpdateSupplierBillFortnoxDraftAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending supplier bill Fortnox draft update request. CompanyId: {CompanyId}. BillId: {BillId}.",
            companyId,
            billId);

        return SendCompanyScopedAsync<object, SupplierInvoiceDraftActionResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/fortnox-draft/update",
            new { },
            cancellationToken);
    }

    public Task<SupplierInvoiceDraftActionResponse> BookkeepSupplierBillFortnoxDraftAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending supplier bill Fortnox bookkeeping request. CompanyId: {CompanyId}. BillId: {BillId}.",
            companyId,
            billId);

        return SendCompanyScopedAsync<object, SupplierInvoiceDraftActionResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/fortnox-draft/bookkeep",
            new { },
            cancellationToken);
    }

    public Task<PaidSupplierBillExpensePostingResponse> PostPaidSupplierBillExpenseAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending paid supplier bill expense posting request. CompanyId: {CompanyId}. BillId: {BillId}.",
            companyId,
            billId);

        return SendCompanyScopedAsync<object, PaidSupplierBillExpensePostingResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/paid-expense-posting",
            new { },
            cancellationToken);
    }

    public Task<SupplierInvoiceCorrectionActionResponse> CancelSupplierInvoiceAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending supplier bill cancellation request. CompanyId: {CompanyId}. BillId: {BillId}.",
            companyId,
            billId);

        return SendCompanyScopedAsync<object, SupplierInvoiceCorrectionActionResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/corrections/cancel",
            new { },
            cancellationToken);
    }

    public Task<SupplierInvoiceCorrectionActionResponse> CreateSupplierCreditNoteAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending supplier bill credit note request. CompanyId: {CompanyId}. BillId: {BillId}.",
            companyId,
            billId);

        return SendCompanyScopedAsync<object, SupplierInvoiceCorrectionActionResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/corrections/credit-note",
            new { },
            cancellationToken);
    }

    public Task<SupplierInvoiceEnrichmentActionResponse> SuggestSupplierInvoiceEnrichmentAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending supplier bill enrichment suggestion request. CompanyId: {CompanyId}. BillId: {BillId}.",
            companyId,
            billId);

        return SendCompanyScopedAsync<object, SupplierInvoiceEnrichmentActionResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/enrichment/suggest",
            new { },
            cancellationToken);
    }

    public Task<SupplierInvoiceEnrichmentActionResponse> SyncSupplierInvoiceEnrichmentAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending supplier bill enrichment sync request. CompanyId: {CompanyId}. BillId: {BillId}.",
            companyId,
            billId);

        return SendCompanyScopedAsync<object, SupplierInvoiceEnrichmentActionResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/enrichment/sync",
            new { },
            cancellationToken);
    }

    public Task<SupplierInvoiceEnrichmentActionResponse> ReconcileSupplierInvoiceAsync(Guid companyId, Guid billId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "Sending supplier bill reconciliation request. CompanyId: {CompanyId}. BillId: {BillId}.",
            companyId,
            billId);

        return SendCompanyScopedAsync<object, SupplierInvoiceEnrichmentActionResponse>(
            companyId,
            HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/enrichment/reconcile",
            new { },
            cancellationToken);
    }

}

