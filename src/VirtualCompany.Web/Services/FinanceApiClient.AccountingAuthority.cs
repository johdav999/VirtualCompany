namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AccountingAuthorityReadModelResponse?> GetAccountingAuthorityAsync(
        Guid companyId,
        int exportLimit = 50,
        CancellationToken cancellationToken = default) =>
        GetAsync<AccountingAuthorityReadModelResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/authority?exportLimit={exportLimit}",
            allowNotFound: false, cancellationToken);

    public Task<AccountingAuthorityChangePreviewResponse> PreviewAccountingAuthorityChangeAsync(
        Guid companyId,
        PreviewAccountingAuthorityChangeApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<PreviewAccountingAuthorityChangeApiRequest, AccountingAuthorityChangePreviewResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/authority/preview", request, cancellationToken);
    }

    public Task<AccountingAuthorityReadModelResponse> StartAccountingAuthorityChangeAsync(
        Guid companyId,
        StartAccountingAuthorityChangeApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StartAccountingAuthorityChangeApiRequest, AccountingAuthorityReadModelResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/authority/change", request, cancellationToken);
    }

    public Task<AccountingAuthorityReadModelResponse> RecordAccountingCutoverValidationAsync(
        Guid companyId,
        Guid authorityPeriodId,
        RecordAccountingCutoverValidationApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<RecordAccountingCutoverValidationApiRequest, AccountingAuthorityReadModelResponse>(
            companyId, HttpMethod.Put,
            $"internal/companies/{companyId}/finance/accounting/authority/{authorityPeriodId}/cutover-validation",
            request, cancellationToken);
    }

    public Task<AccountingAuthorityReadModelResponse> CompleteAccountingAuthorityCutoverAsync(
        Guid companyId,
        Guid authorityPeriodId,
        CompleteAccountingAuthorityCutoverApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CompleteAccountingAuthorityCutoverApiRequest, AccountingAuthorityReadModelResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/authority/{authorityPeriodId}/complete",
            request, cancellationToken);
    }

    public Task<AccountingProviderExportResponse> QueueAccountingProviderExportAsync(
        Guid companyId,
        QueueAccountingProviderExportApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<QueueAccountingProviderExportApiRequest, AccountingProviderExportResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/provider-exports", request, cancellationToken);
    }

    public Task<AccountingProviderExportResponse> ReconcileAccountingProviderExportAsync(
        Guid companyId,
        Guid exportId,
        ReconcileAccountingProviderExportApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ReconcileAccountingProviderExportApiRequest, AccountingProviderExportResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/provider-exports/{exportId}/reconcile",
            request, cancellationToken);
    }
}
