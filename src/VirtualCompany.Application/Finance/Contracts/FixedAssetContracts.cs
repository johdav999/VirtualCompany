namespace VirtualCompany.Application.Finance;

public static class FixedAssetReasonCodes
{
    public const string NotFound = "fixed_asset_not_found";
    public const string ClassNotFound = "fixed_asset_class_not_found";
    public const string ClassInactive = "fixed_asset_class_inactive";
    public const string InvalidState = "fixed_asset_invalid_state";
    public const string InvalidAmount = "fixed_asset_invalid_amount";
    public const string PeriodUnavailable = "fixed_asset_period_unavailable";
    public const string SourceConflict = "fixed_asset_source_conflict";
    public const string IdempotencyConflict = "fixed_asset_idempotency_conflict";
    public const string CrossCompanyReference = "fixed_asset_cross_company_reference";
    public const string UnsupportedTaxDepreciation = "fixed_asset_tax_depreciation_unsupported";
}

public sealed record FixedAssetClassInput(string Code, string Name, string BookMethod, int UsefulLifeMonths,
    decimal DefaultResidualPercent, Guid CostAccountId, Guid AccumulatedDepreciationAccountId,
    Guid DepreciationExpenseAccountId, Guid AccumulatedImpairmentAccountId, Guid ImpairmentExpenseAccountId,
    Guid DisposalGainAccountId, Guid DisposalLossAccountId, string VoucherSeriesCode, bool RequiresApproval);
public sealed record SaveFixedAssetClassCommand(Guid CompanyId, Guid? ClassId, FixedAssetClassInput Class,
    long ExpectedVersion, Guid ActorUserId);

public sealed record FixedAssetComponentInput(string Code, string Name, decimal Cost, decimal ResidualValue,
    int UsefulLifeMonths, DateOnly PlacedInServiceDate);
public sealed record RegisterFixedAssetInput(Guid AssetClassId, string AssetNumber, string Name, string Currency,
    decimal AcquisitionCost, decimal? ResidualValue, int? UsefulLifeMonths, DateOnly AcquisitionDate,
    string SourceType, string SourceId, string SourceVersion, Guid? SourceDocumentId, Guid? LegacyFinanceAssetId,
    string? Custodian, string? Location, IReadOnlyDictionary<string, string>? DimensionFacts = null,
    IReadOnlyList<FixedAssetComponentInput>? Components = null);
public sealed record RegisterFixedAssetCommand(Guid CompanyId, RegisterFixedAssetInput Asset, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId = null);
public sealed record FixedAssetLifecycleCommand(Guid CompanyId, Guid AssetId, DateOnly EffectiveDate,
    Guid FiscalPeriodId, Guid OffsetAccountId, decimal Amount, long ExpectedVersion, string SourceVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record PlaceFixedAssetInServiceCommand(Guid CompanyId, Guid AssetId, DateOnly PlacedInServiceDate,
    long ExpectedVersion, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record TransferFixedAssetCommand(Guid CompanyId, Guid AssetId, string? Custodian, string? Location,
    IReadOnlyDictionary<string, string>? DimensionFacts, long ExpectedVersion, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId = null);
public sealed record DisposeFixedAssetCommand(Guid CompanyId, Guid AssetId, DateOnly DisposalDate,
    Guid FiscalPeriodId, Guid ProceedsAccountId, decimal Proceeds, long ExpectedVersion, string SourceVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record ReverseFixedAssetEventCommand(Guid CompanyId, Guid AssetId, Guid EventId,
    Guid FiscalPeriodId, DateOnly PostingDate, string Reason, string SourceVersion, string IdempotencyKey,
    long ExpectedVersion, Guid ActorUserId, string? CorrelationId = null);
public sealed record PreviewFixedAssetDepreciationQuery(Guid CompanyId, DateOnly PeriodStart, DateOnly PeriodEnd);
public sealed record PreviewFixedAssetRegistrationQuery(Guid CompanyId, RegisterFixedAssetInput Asset,
    Guid ActorUserId);
public sealed record PreviewFixedAssetDisposalQuery(Guid CompanyId, Guid AssetId, DateOnly DisposalDate,
    Guid FiscalPeriodId, Guid ProceedsAccountId, decimal Proceeds, long ExpectedVersion,
    string SourceVersion, Guid ActorUserId);
public sealed record RunFixedAssetDepreciationCommand(Guid CompanyId, Guid FiscalPeriodId, DateOnly PeriodStart,
    DateOnly PeriodEnd, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record GetFixedAssetQuery(Guid CompanyId, Guid AssetId);
public sealed record ListFixedAssetsQuery(Guid CompanyId, string? Status = null, Guid? AssetClassId = null,
    string? Search = null, int Skip = 0, int Take = 100);

public sealed record FixedAssetClassDto(Guid Id, string Code, string Name, string BookMethod,
    int UsefulLifeMonths, decimal DefaultResidualPercent, Guid CostAccountId,
    Guid AccumulatedDepreciationAccountId, Guid DepreciationExpenseAccountId,
    Guid AccumulatedImpairmentAccountId, Guid ImpairmentExpenseAccountId,
    Guid DisposalGainAccountId, Guid DisposalLossAccountId, string VoucherSeriesCode,
    bool RequiresApproval, bool IsActive, string DefinitionHash, long Version);
public sealed record FixedAssetEventDto(Guid Id, string EventType, DateOnly EffectiveDate, decimal Amount,
    decimal CostMovement, decimal DepreciationMovement, decimal ImpairmentMovement, decimal Proceeds,
    decimal GainLoss, string Status, Guid? LedgerEntryId, Guid? DepreciationRunId, Guid? OriginalEventId,
    string SourceType, string SourceId, string SourceVersion,
    IReadOnlyList<FixedAssetComponentAllocationDto> ComponentAllocations, DateTime CreatedUtc);
public sealed record FixedAssetComponentAllocationDto(Guid? ComponentId, decimal Amount,
    decimal DepreciableBasis, decimal RemainingDepreciableAmount, int EligibleDays, int DaysInPeriod,
    string Explanation);
public sealed record FixedAssetComponentDto(Guid Id, string Code, string Name, decimal Cost,
    decimal ResidualValue, decimal AccumulatedDepreciation, int UsefulLifeMonths,
    DateOnly PlacedInServiceDate);
public sealed record FixedAssetDto(Guid Id, Guid AssetClassId, string AssetClassCode, string AssetClassName,
    string AssetNumber, string Name, string Currency, decimal AcquisitionCost, decimal ImprovementCost,
    decimal GrossBookValue, decimal ResidualValue, decimal AccumulatedDepreciation,
    decimal AccumulatedImpairment, decimal NetBookValue, decimal DisposalProceeds, decimal DisposalGainLoss,
    int UsefulLifeMonths, string BookMethod, DateOnly AcquisitionDate, DateOnly? CapitalizationDate,
    DateOnly? PlacedInServiceDate, DateOnly? LastDepreciationThrough, DateOnly? DisposalDate, string Status,
    string SourceType, string SourceId, string SourceVersion, Guid? SourceDocumentId, Guid? LegacyFinanceAssetId,
    string? Custodian, string? Location, IReadOnlyDictionary<string, string> DimensionFacts,
    long Version, IReadOnlyList<FixedAssetComponentDto> Components, IReadOnlyList<FixedAssetEventDto> Events);
public sealed record FixedAssetListDto(IReadOnlyList<FixedAssetDto> Items, int TotalCount, int Skip, int Take,
    decimal AcquisitionCost, decimal AccumulatedDepreciation, decimal AccumulatedImpairment,
    decimal NetBookValue, int OpenMigrationConflictCount);
public sealed record FixedAssetDepreciationItemDto(Guid AssetId, string AssetNumber, string AssetName,
    long AssetVersion, decimal Amount, decimal DepreciableBasis, decimal RemainingDepreciableAmount,
    int EligibleDays, int DaysInPeriod, string Method, string Explanation, string Status,
    Guid? LedgerEntryId = null, string? FailureCode = null, string? FailureSummary = null);
public sealed record FixedAssetDepreciationPreviewDto(DateOnly PeriodStart, DateOnly PeriodEnd,
    decimal TotalAmount, string PopulationHash, IReadOnlyList<FixedAssetDepreciationItemDto> Items);
public sealed record FixedAssetRegistrationPreviewDto(RegisterFixedAssetInput Asset,
    FixedAssetClassDto AssetClass, decimal ResidualValue, int UsefulLifeMonths,
    string BookMethod, string ProposalChecksum, bool IsRegistered, bool RequiresApproval,
    Guid? ExistingAssetId = null);
public sealed record FixedAssetDisposalPreviewDto(FixedAssetDto Asset, DateOnly DisposalDate,
    Guid FiscalPeriodId, decimal NetBookValue, decimal Proceeds, decimal GainLoss,
    AccountingPostingPreview PostingPreview, string ProposalChecksum, bool IsPosted);
public sealed record FixedAssetDepreciationRunDto(Guid Id, Guid FiscalPeriodId, DateOnly PeriodStart,
    DateOnly PeriodEnd, string Status, decimal TotalAmount, int PostedItemCount, int ExceptionCount,
    string PopulationHash, long Version, IReadOnlyList<FixedAssetDepreciationItemDto> Items);
public sealed record FixedAssetReconciliationDto(decimal RegisterCost, decimal LedgerCost,
    decimal CostDifference, decimal RegisterAccumulatedDepreciation, decimal LedgerAccumulatedDepreciation,
    decimal DepreciationDifference, decimal RegisterAccumulatedImpairment, decimal LedgerAccumulatedImpairment,
    decimal ImpairmentDifference, decimal RegisterNetBookValue, bool IsReconciled,
    IReadOnlyList<string> Issues, int OpenMigrationConflictCount);

public interface IFixedAssetService
{
    Task<IReadOnlyList<FixedAssetClassDto>> ListClassesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<FixedAssetClassDto> SaveClassAsync(SaveFixedAssetClassCommand command, CancellationToken cancellationToken);
    Task<FixedAssetRegistrationPreviewDto> PreviewRegistrationAsync(PreviewFixedAssetRegistrationQuery query, CancellationToken cancellationToken);
    Task<FixedAssetDto> RegisterAsync(RegisterFixedAssetCommand command, CancellationToken cancellationToken);
    Task<FixedAssetDto> GetAsync(GetFixedAssetQuery query, CancellationToken cancellationToken);
    Task<FixedAssetListDto> ListAsync(ListFixedAssetsQuery query, CancellationToken cancellationToken);
    Task<FixedAssetDto> CapitalizeAsync(FixedAssetLifecycleCommand command, CancellationToken cancellationToken);
    Task<FixedAssetDto> PlaceInServiceAsync(PlaceFixedAssetInServiceCommand command, CancellationToken cancellationToken);
    Task<FixedAssetDto> ImproveAsync(FixedAssetLifecycleCommand command, CancellationToken cancellationToken);
    Task<FixedAssetDto> ImpairAsync(FixedAssetLifecycleCommand command, CancellationToken cancellationToken);
    Task<FixedAssetDto> TransferAsync(TransferFixedAssetCommand command, CancellationToken cancellationToken);
    Task<FixedAssetDisposalPreviewDto> PreviewDisposalAsync(PreviewFixedAssetDisposalQuery query, CancellationToken cancellationToken);
    Task<FixedAssetDto> DisposeAsync(DisposeFixedAssetCommand command, CancellationToken cancellationToken);
    Task<FixedAssetDto> ReverseEventAsync(ReverseFixedAssetEventCommand command, CancellationToken cancellationToken);
    Task<FixedAssetDepreciationPreviewDto> PreviewDepreciationAsync(PreviewFixedAssetDepreciationQuery query, CancellationToken cancellationToken);
    Task<FixedAssetDepreciationRunDto> RunDepreciationAsync(RunFixedAssetDepreciationCommand command, CancellationToken cancellationToken);
    Task<FixedAssetReconciliationDto> ReconcileAsync(Guid companyId, CancellationToken cancellationToken);
    Task<int> DiscoverLegacyConflictsAsync(Guid companyId, CancellationToken cancellationToken);
}

public sealed class FixedAssetException : Exception
{
    public FixedAssetException(string reasonCode, string message, bool conflict = false) : base(message)
    { ReasonCode = reasonCode; IsConflict = conflict; }
    public string ReasonCode { get; }
    public bool IsConflict { get; }
}
