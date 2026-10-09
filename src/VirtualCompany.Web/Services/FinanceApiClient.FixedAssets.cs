namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<FixedAssetListResponse?> ListFixedAssetsAsync(Guid companyId, string? status = null,
        CancellationToken cancellationToken = default)
    {
        var suffix = string.IsNullOrWhiteSpace(status) ? string.Empty : $"?status={Uri.EscapeDataString(status)}";
        return GetAsync<FixedAssetListResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/fixed-assets{suffix}", false, cancellationToken);
    }

    public Task<FixedAssetResponse?> GetFixedAssetAsync(Guid companyId, Guid assetId,
        CancellationToken cancellationToken = default) => GetAsync<FixedAssetResponse>(companyId,
        $"internal/companies/{companyId}/finance/accounting/fixed-assets/{assetId:D}", true, cancellationToken);

    public async Task<IReadOnlyList<FixedAssetClassResponse>> ListFixedAssetClassesAsync(Guid companyId,
        CancellationToken cancellationToken = default) => await GetListAsync<FixedAssetClassResponse>(companyId,
        $"internal/companies/{companyId}/finance/accounting/fixed-assets/classes", cancellationToken);

    public Task<FixedAssetReconciliationResponse?> ReconcileFixedAssetsAsync(Guid companyId,
        CancellationToken cancellationToken = default) => GetAsync<FixedAssetReconciliationResponse>(companyId,
        $"internal/companies/{companyId}/finance/accounting/fixed-assets/reconciliation", false, cancellationToken);

    public Task<FixedAssetDepreciationPreviewResponse?> PreviewFixedAssetDepreciationAsync(Guid companyId,
        DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken = default) =>
        GetAsync<FixedAssetDepreciationPreviewResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/fixed-assets/depreciation/preview?periodStart={periodStart:yyyy-MM-dd}&periodEnd={periodEnd:yyyy-MM-dd}",
            false, cancellationToken);

    public Task<FixedAssetDepreciationRunResponse> RunFixedAssetDepreciationAsync(Guid companyId,
        Guid fiscalPeriodId, DateOnly periodStart, DateOnly periodEnd, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<RunFixedAssetDepreciationApiRequest, FixedAssetDepreciationRunResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/fixed-assets/depreciation/runs",
            new() { FiscalPeriodId = fiscalPeriodId, PeriodStart = periodStart, PeriodEnd = periodEnd,
                IdempotencyKey = idempotencyKey }, cancellationToken);
    }
}
