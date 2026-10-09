using System.Net.Http.Json;

namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<SupplierBillAccountingReferenceDataResponse> GetSupplierBillAccountingReferenceDataAsync(
        Guid companyId, Guid billId, CancellationToken cancellationToken = default) =>
        GetAsync<SupplierBillAccountingReferenceDataResponse>(companyId,
            $"internal/companies/{companyId}/finance/bills/{billId}/accounting/reference-data", false, cancellationToken)!;

    public Task<SupplierBillAccountingPreviewResponse> PreviewSupplierBillAccountingAsync(
        Guid companyId, Guid billId, SupplierBillAccountingApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<SupplierBillAccountingApiRequest, SupplierBillAccountingPreviewResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/accounting/preview", request, cancellationToken);

    public Task<SupplierBillAccountingSubmissionResponse> SubmitSupplierBillAccountingAsync(
        Guid companyId, Guid billId, SubmitSupplierBillAccountingApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<SubmitSupplierBillAccountingApiRequest, SupplierBillAccountingSubmissionResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/accounting/submit", request, cancellationToken);

    public Task<SupplierBillAccountingPostingResponse> PostSupplierBillAccountingAsync(
        Guid companyId, Guid billId, PostSupplierBillAccountingApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<PostSupplierBillAccountingApiRequest, SupplierBillAccountingPostingResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/accounting/post", request, cancellationToken);

    public Task<SupplierBillAccountingStateResponse?> GetSupplierBillAccountingAsync(
        Guid companyId, Guid billId, CancellationToken cancellationToken = default) =>
        GetAsync<SupplierBillAccountingStateResponse>(companyId,
            $"internal/companies/{companyId}/finance/bills/{billId}/accounting", true, cancellationToken);

    public Task<SupplierBillAccountingStateResponse> CreateNativeSupplierCreditNoteAsync(
        Guid companyId, Guid billId, CreateNativeSupplierCreditNoteApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<CreateNativeSupplierCreditNoteApiRequest, SupplierBillAccountingStateResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/native-credit-notes", request, cancellationToken);

    public Task<SupplierBillPayablesReconciliationResponse?> GetSupplierBillPayablesReconciliationAsync(
        Guid companyId, DateOnly? throughDate = null, CancellationToken cancellationToken = default)
    {
        var query = throughDate.HasValue ? $"?throughDate={throughDate:yyyy-MM-dd}" : string.Empty;
        return GetAsync<SupplierBillPayablesReconciliationResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/reconciliation/payables{query}", false, cancellationToken);
    }
}
