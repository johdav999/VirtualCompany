namespace VirtualCompany.Application.Finance;
public sealed record FixedAssetComponentDto(Guid Id, string Code, string Name, decimal Cost, decimal ResidualValue, decimal AccumulatedDepreciation, int UsefulLifeMonths, DateOnly PlacedInServiceDate)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public decimal Cost { get; set; } = Cost;
    public decimal ResidualValue { get; set; } = ResidualValue;
    public decimal AccumulatedDepreciation { get; set; } = AccumulatedDepreciation;
    public int UsefulLifeMonths { get; set; } = UsefulLifeMonths;
    public DateOnly PlacedInServiceDate { get; set; } = PlacedInServiceDate;

    public FixedAssetComponentDto() : this(default !, string.Empty, string.Empty, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record FixedAssetReconciliationDto(decimal RegisterCost, decimal LedgerCost, decimal CostDifference, decimal RegisterAccumulatedDepreciation, decimal LedgerAccumulatedDepreciation, decimal DepreciationDifference, decimal RegisterAccumulatedImpairment, decimal LedgerAccumulatedImpairment, decimal ImpairmentDifference, decimal RegisterNetBookValue, bool IsReconciled, IReadOnlyList<string> Issues, int OpenMigrationConflictCount)
{
    public decimal RegisterCost { get; set; } = RegisterCost;
    public decimal LedgerCost { get; set; } = LedgerCost;
    public decimal CostDifference { get; set; } = CostDifference;
    public decimal RegisterAccumulatedDepreciation { get; set; } = RegisterAccumulatedDepreciation;
    public decimal LedgerAccumulatedDepreciation { get; set; } = LedgerAccumulatedDepreciation;
    public decimal DepreciationDifference { get; set; } = DepreciationDifference;
    public decimal RegisterAccumulatedImpairment { get; set; } = RegisterAccumulatedImpairment;
    public decimal LedgerAccumulatedImpairment { get; set; } = LedgerAccumulatedImpairment;
    public decimal ImpairmentDifference { get; set; } = ImpairmentDifference;
    public decimal RegisterNetBookValue { get; set; } = RegisterNetBookValue;
    public bool IsReconciled { get; set; } = IsReconciled;
    public IReadOnlyList<string> Issues { get; set; } = Issues;
    public int OpenMigrationConflictCount { get; set; } = OpenMigrationConflictCount;

    public FixedAssetReconciliationDto() : this(default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, [], default !)
    {
    }
}

public sealed record FixedAssetDepreciationItemDto(Guid AssetId, string AssetNumber, string AssetName, long AssetVersion, decimal Amount, decimal DepreciableBasis, decimal RemainingDepreciableAmount, int EligibleDays, int DaysInPeriod, string Method, string Explanation, string Status, Guid? LedgerEntryId = null, string? FailureCode = null, string? FailureSummary = null)
{
    public Guid AssetId { get; set; } = AssetId;
    public string AssetNumber { get; set; } = AssetNumber;
    public string AssetName { get; set; } = AssetName;
    public long AssetVersion { get; set; } = AssetVersion;
    public decimal Amount { get; set; } = Amount;
    public decimal DepreciableBasis { get; set; } = DepreciableBasis;
    public decimal RemainingDepreciableAmount { get; set; } = RemainingDepreciableAmount;
    public int EligibleDays { get; set; } = EligibleDays;
    public int DaysInPeriod { get; set; } = DaysInPeriod;
    public string Method { get; set; } = Method;
    public string Explanation { get; set; } = Explanation;
    public string Status { get; set; } = Status;
    public Guid? LedgerEntryId { get; set; } = LedgerEntryId;
    public string? FailureCode { get; set; } = FailureCode;
    public string? FailureSummary { get; set; } = FailureSummary;

    public FixedAssetDepreciationItemDto() : this(default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, string.Empty, string.Empty, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record FixedAssetListDto(IReadOnlyList<FixedAssetDto> Items, int TotalCount, int Skip, int Take, decimal AcquisitionCost, decimal AccumulatedDepreciation, decimal AccumulatedImpairment, decimal NetBookValue, int OpenMigrationConflictCount)
{
    public IReadOnlyList<FixedAssetDto> Items { get; set; } = Items;
    public int TotalCount { get; set; } = TotalCount;
    public int Skip { get; set; } = Skip;
    public int Take { get; set; } = Take;
    public decimal AcquisitionCost { get; set; } = AcquisitionCost;
    public decimal AccumulatedDepreciation { get; set; } = AccumulatedDepreciation;
    public decimal AccumulatedImpairment { get; set; } = AccumulatedImpairment;
    public decimal NetBookValue { get; set; } = NetBookValue;
    public int OpenMigrationConflictCount { get; set; } = OpenMigrationConflictCount;

    public FixedAssetListDto() : this([], default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record FixedAssetDepreciationRunDto(Guid Id, Guid FiscalPeriodId, DateOnly PeriodStart, DateOnly PeriodEnd, string Status, decimal TotalAmount, int PostedItemCount, int ExceptionCount, string PopulationHash, long Version, IReadOnlyList<FixedAssetDepreciationItemDto> Items)
{
    public Guid Id { get; set; } = Id;
    public Guid FiscalPeriodId { get; set; } = FiscalPeriodId;
    public DateOnly PeriodStart { get; set; } = PeriodStart;
    public DateOnly PeriodEnd { get; set; } = PeriodEnd;
    public string Status { get; set; } = Status;
    public decimal TotalAmount { get; set; } = TotalAmount;
    public int PostedItemCount { get; set; } = PostedItemCount;
    public int ExceptionCount { get; set; } = ExceptionCount;
    public string PopulationHash { get; set; } = PopulationHash;
    public long Version { get; set; } = Version;
    public IReadOnlyList<FixedAssetDepreciationItemDto> Items { get; set; } = Items;

    public FixedAssetDepreciationRunDto() : this(default !, default !, default !, default !, string.Empty, default !, default !, default !, string.Empty, default !, [])
    {
    }
}

public sealed record FixedAssetClassDto(Guid Id, string Code, string Name, string BookMethod, int UsefulLifeMonths, decimal DefaultResidualPercent, Guid CostAccountId, Guid AccumulatedDepreciationAccountId, Guid DepreciationExpenseAccountId, Guid AccumulatedImpairmentAccountId, Guid ImpairmentExpenseAccountId, Guid DisposalGainAccountId, Guid DisposalLossAccountId, string VoucherSeriesCode, bool RequiresApproval, bool IsActive, string DefinitionHash, long Version)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public string BookMethod { get; set; } = BookMethod;
    public int UsefulLifeMonths { get; set; } = UsefulLifeMonths;
    public decimal DefaultResidualPercent { get; set; } = DefaultResidualPercent;
    public Guid CostAccountId { get; set; } = CostAccountId;
    public Guid AccumulatedDepreciationAccountId { get; set; } = AccumulatedDepreciationAccountId;
    public Guid DepreciationExpenseAccountId { get; set; } = DepreciationExpenseAccountId;
    public Guid AccumulatedImpairmentAccountId { get; set; } = AccumulatedImpairmentAccountId;
    public Guid ImpairmentExpenseAccountId { get; set; } = ImpairmentExpenseAccountId;
    public Guid DisposalGainAccountId { get; set; } = DisposalGainAccountId;
    public Guid DisposalLossAccountId { get; set; } = DisposalLossAccountId;
    public string VoucherSeriesCode { get; set; } = VoucherSeriesCode;
    public bool RequiresApproval { get; set; } = RequiresApproval;
    public bool IsActive { get; set; } = IsActive;
    public string DefinitionHash { get; set; } = DefinitionHash;
    public long Version { get; set; } = Version;

    public FixedAssetClassDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, string.Empty, default !, default !, string.Empty, default !)
    {
    }
}

public sealed record FixedAssetComponentAllocationDto(Guid? ComponentId, decimal Amount, decimal DepreciableBasis, decimal RemainingDepreciableAmount, int EligibleDays, int DaysInPeriod, string Explanation)
{
    public Guid? ComponentId { get; set; } = ComponentId;
    public decimal Amount { get; set; } = Amount;
    public decimal DepreciableBasis { get; set; } = DepreciableBasis;
    public decimal RemainingDepreciableAmount { get; set; } = RemainingDepreciableAmount;
    public int EligibleDays { get; set; } = EligibleDays;
    public int DaysInPeriod { get; set; } = DaysInPeriod;
    public string Explanation { get; set; } = Explanation;

    public FixedAssetComponentAllocationDto() : this(default !, default !, default !, default !, default !, default !, string.Empty)
    {
    }
}

public sealed record FixedAssetEventDto(Guid Id, string EventType, DateOnly EffectiveDate, decimal Amount, decimal CostMovement, decimal DepreciationMovement, decimal ImpairmentMovement, decimal Proceeds, decimal GainLoss, string Status, Guid? LedgerEntryId, Guid? DepreciationRunId, Guid? OriginalEventId, string SourceType, string SourceId, string SourceVersion, IReadOnlyList<FixedAssetComponentAllocationDto> ComponentAllocations, DateTime CreatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string EventType { get; set; } = EventType;
    public DateOnly EffectiveDate { get; set; } = EffectiveDate;
    public decimal Amount { get; set; } = Amount;
    public decimal CostMovement { get; set; } = CostMovement;
    public decimal DepreciationMovement { get; set; } = DepreciationMovement;
    public decimal ImpairmentMovement { get; set; } = ImpairmentMovement;
    public decimal Proceeds { get; set; } = Proceeds;
    public decimal GainLoss { get; set; } = GainLoss;
    public string Status { get; set; } = Status;
    public Guid? LedgerEntryId { get; set; } = LedgerEntryId;
    public Guid? DepreciationRunId { get; set; } = DepreciationRunId;
    public Guid? OriginalEventId { get; set; } = OriginalEventId;
    public string SourceType { get; set; } = SourceType;
    public string SourceId { get; set; } = SourceId;
    public string SourceVersion { get; set; } = SourceVersion;
    public IReadOnlyList<FixedAssetComponentAllocationDto> ComponentAllocations { get; set; } = ComponentAllocations;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;

    public FixedAssetEventDto() : this(default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, string.Empty, default !, default !, default !, string.Empty, string.Empty, string.Empty, [], default !)
    {
    }
}

public sealed record FixedAssetDepreciationPreviewDto(DateOnly PeriodStart, DateOnly PeriodEnd, decimal TotalAmount, string PopulationHash, IReadOnlyList<FixedAssetDepreciationItemDto> Items)
{
    public DateOnly PeriodStart { get; set; } = PeriodStart;
    public DateOnly PeriodEnd { get; set; } = PeriodEnd;
    public decimal TotalAmount { get; set; } = TotalAmount;
    public string PopulationHash { get; set; } = PopulationHash;
    public IReadOnlyList<FixedAssetDepreciationItemDto> Items { get; set; } = Items;

    public FixedAssetDepreciationPreviewDto() : this(default !, default !, default !, string.Empty, [])
    {
    }
}

public sealed record FixedAssetDto(Guid Id, Guid AssetClassId, string AssetClassCode, string AssetClassName, string AssetNumber, string Name, string Currency, decimal AcquisitionCost, decimal ImprovementCost, decimal GrossBookValue, decimal ResidualValue, decimal AccumulatedDepreciation, decimal AccumulatedImpairment, decimal NetBookValue, decimal DisposalProceeds, decimal DisposalGainLoss, int UsefulLifeMonths, string BookMethod, DateOnly AcquisitionDate, DateOnly? CapitalizationDate, DateOnly? PlacedInServiceDate, DateOnly? LastDepreciationThrough, DateOnly? DisposalDate, string Status, string SourceType, string SourceId, string SourceVersion, Guid? SourceDocumentId, Guid? LegacyFinanceAssetId, string? Custodian, string? Location, IReadOnlyDictionary<string, string> DimensionFacts, long Version, IReadOnlyList<FixedAssetComponentDto> Components, IReadOnlyList<FixedAssetEventDto> Events)
{
    public Guid Id { get; set; } = Id;
    public Guid AssetClassId { get; set; } = AssetClassId;
    public string AssetClassCode { get; set; } = AssetClassCode;
    public string AssetClassName { get; set; } = AssetClassName;
    public string AssetNumber { get; set; } = AssetNumber;
    public string Name { get; set; } = Name;
    public string Currency { get; set; } = Currency;
    public decimal AcquisitionCost { get; set; } = AcquisitionCost;
    public decimal ImprovementCost { get; set; } = ImprovementCost;
    public decimal GrossBookValue { get; set; } = GrossBookValue;
    public decimal ResidualValue { get; set; } = ResidualValue;
    public decimal AccumulatedDepreciation { get; set; } = AccumulatedDepreciation;
    public decimal AccumulatedImpairment { get; set; } = AccumulatedImpairment;
    public decimal NetBookValue { get; set; } = NetBookValue;
    public decimal DisposalProceeds { get; set; } = DisposalProceeds;
    public decimal DisposalGainLoss { get; set; } = DisposalGainLoss;
    public int UsefulLifeMonths { get; set; } = UsefulLifeMonths;
    public string BookMethod { get; set; } = BookMethod;
    public DateOnly AcquisitionDate { get; set; } = AcquisitionDate;
    public DateOnly? CapitalizationDate { get; set; } = CapitalizationDate;
    public DateOnly? PlacedInServiceDate { get; set; } = PlacedInServiceDate;
    public DateOnly? LastDepreciationThrough { get; set; } = LastDepreciationThrough;
    public DateOnly? DisposalDate { get; set; } = DisposalDate;
    public string Status { get; set; } = Status;
    public string SourceType { get; set; } = SourceType;
    public string SourceId { get; set; } = SourceId;
    public string SourceVersion { get; set; } = SourceVersion;
    public Guid? SourceDocumentId { get; set; } = SourceDocumentId;
    public Guid? LegacyFinanceAssetId { get; set; } = LegacyFinanceAssetId;
    public string? Custodian { get; set; } = Custodian;
    public string? Location { get; set; } = Location;
    public IReadOnlyDictionary<string, string> DimensionFacts { get; set; } = DimensionFacts;
    public long Version { get; set; } = Version;
    public IReadOnlyList<FixedAssetComponentDto> Components { get; set; } = Components;
    public IReadOnlyList<FixedAssetEventDto> Events { get; set; } = Events;

    public FixedAssetDto() : this(default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, new Dictionary<string, string>(), default !, [], [])
    {
    }
}
