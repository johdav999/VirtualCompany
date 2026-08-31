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

public sealed class RunFixedAssetDepreciationApiRequest
{ public Guid FiscalPeriodId { get; set; } public DateOnly PeriodStart { get; set; } public DateOnly PeriodEnd { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class FixedAssetClassResponse
{ public Guid Id { get; set; } public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string BookMethod { get; set; } = string.Empty; public int UsefulLifeMonths { get; set; } public decimal DefaultResidualPercent { get; set; } public string VoucherSeriesCode { get; set; } = string.Empty; public bool RequiresApproval { get; set; } public bool IsActive { get; set; } public string DefinitionHash { get; set; } = string.Empty; public long Version { get; set; } }
public sealed class FixedAssetListResponse
{ public List<FixedAssetResponse> Items { get; set; } = []; public int TotalCount { get; set; } public int Skip { get; set; } public int Take { get; set; } public decimal AcquisitionCost { get; set; } public decimal AccumulatedDepreciation { get; set; } public decimal AccumulatedImpairment { get; set; } public decimal NetBookValue { get; set; } public int OpenMigrationConflictCount { get; set; } }
public sealed class FixedAssetResponse
{
    public Guid Id { get; set; } public Guid AssetClassId { get; set; } public string AssetClassCode { get; set; } = string.Empty; public string AssetClassName { get; set; } = string.Empty;
    public string AssetNumber { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public string Currency { get; set; } = string.Empty;
    public decimal AcquisitionCost { get; set; } public decimal ImprovementCost { get; set; } public decimal GrossBookValue { get; set; } public decimal ResidualValue { get; set; }
    public decimal AccumulatedDepreciation { get; set; } public decimal AccumulatedImpairment { get; set; } public decimal NetBookValue { get; set; }
    public decimal DisposalProceeds { get; set; } public decimal DisposalGainLoss { get; set; } public int UsefulLifeMonths { get; set; }
    public string BookMethod { get; set; } = string.Empty; public DateOnly AcquisitionDate { get; set; } public DateOnly? CapitalizationDate { get; set; }
    public DateOnly? PlacedInServiceDate { get; set; } public DateOnly? LastDepreciationThrough { get; set; } public DateOnly? DisposalDate { get; set; }
    public string Status { get; set; } = string.Empty; public string SourceType { get; set; } = string.Empty; public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty; public Guid? SourceDocumentId { get; set; } public Guid? LegacyFinanceAssetId { get; set; }
    public string? Custodian { get; set; } public string? Location { get; set; } public Dictionary<string, string> DimensionFacts { get; set; } = [];
    public long Version { get; set; } public List<FixedAssetComponentResponse> Components { get; set; } = [];
    public List<FixedAssetEventResponse> Events { get; set; } = [];
}
public sealed class FixedAssetComponentResponse
{ public Guid Id { get; set; } public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public decimal Cost { get; set; } public decimal ResidualValue { get; set; } public decimal AccumulatedDepreciation { get; set; } public int UsefulLifeMonths { get; set; } public DateOnly PlacedInServiceDate { get; set; } }
public sealed class FixedAssetEventResponse
{ public Guid Id { get; set; } public string EventType { get; set; } = string.Empty; public DateOnly EffectiveDate { get; set; } public decimal Amount { get; set; } public decimal CostMovement { get; set; } public decimal DepreciationMovement { get; set; } public decimal ImpairmentMovement { get; set; } public decimal Proceeds { get; set; } public decimal GainLoss { get; set; } public string Status { get; set; } = string.Empty; public Guid? LedgerEntryId { get; set; } public Guid? DepreciationRunId { get; set; } public Guid? OriginalEventId { get; set; } public string SourceType { get; set; } = string.Empty; public string SourceId { get; set; } = string.Empty; public string SourceVersion { get; set; } = string.Empty; public List<FixedAssetComponentAllocationResponse> ComponentAllocations { get; set; } = []; public DateTime CreatedUtc { get; set; } }
public sealed class FixedAssetComponentAllocationResponse
{ public Guid? ComponentId { get; set; } public decimal Amount { get; set; } public decimal DepreciableBasis { get; set; } public decimal RemainingDepreciableAmount { get; set; } public int EligibleDays { get; set; } public int DaysInPeriod { get; set; } public string Explanation { get; set; } = string.Empty; }
public sealed class FixedAssetReconciliationResponse
{ public decimal RegisterCost { get; set; } public decimal LedgerCost { get; set; } public decimal CostDifference { get; set; } public decimal RegisterAccumulatedDepreciation { get; set; } public decimal LedgerAccumulatedDepreciation { get; set; } public decimal DepreciationDifference { get; set; } public decimal RegisterAccumulatedImpairment { get; set; } public decimal LedgerAccumulatedImpairment { get; set; } public decimal ImpairmentDifference { get; set; } public decimal RegisterNetBookValue { get; set; } public bool IsReconciled { get; set; } public List<string> Issues { get; set; } = []; public int OpenMigrationConflictCount { get; set; } }
public sealed class FixedAssetDepreciationItemResponse
{ public Guid AssetId { get; set; } public string AssetNumber { get; set; } = string.Empty; public string AssetName { get; set; } = string.Empty; public long AssetVersion { get; set; } public decimal Amount { get; set; } public decimal DepreciableBasis { get; set; } public decimal RemainingDepreciableAmount { get; set; } public int EligibleDays { get; set; } public int DaysInPeriod { get; set; } public string Method { get; set; } = string.Empty; public string Explanation { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public Guid? LedgerEntryId { get; set; } public string? FailureCode { get; set; } public string? FailureSummary { get; set; } }
public sealed class FixedAssetDepreciationPreviewResponse
{ public DateOnly PeriodStart { get; set; } public DateOnly PeriodEnd { get; set; } public decimal TotalAmount { get; set; } public string PopulationHash { get; set; } = string.Empty; public List<FixedAssetDepreciationItemResponse> Items { get; set; } = []; }
public sealed class FixedAssetDepreciationRunResponse
{ public Guid Id { get; set; } public Guid FiscalPeriodId { get; set; } public DateOnly PeriodStart { get; set; } public DateOnly PeriodEnd { get; set; } public string Status { get; set; } = string.Empty; public decimal TotalAmount { get; set; } public int PostedItemCount { get; set; } public int ExceptionCount { get; set; } public string PopulationHash { get; set; } = string.Empty; public long Version { get; set; } public List<FixedAssetDepreciationItemResponse> Items { get; set; } = []; }
