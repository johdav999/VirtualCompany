using System.Security.Cryptography;
using System.Text;

namespace VirtualCompany.Domain.Entities;

public static class FixedAssetBookMethods
{
    public const string StraightLine = "straight_line";
    public static string Normalize(string value) => Required(value, nameof(value), 32).Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
    public static bool IsSupported(string value) => Normalize(value) == StraightLine;
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
}

public static class FixedAssetStatuses
{
    public const string Draft = "draft";
    public const string Capitalized = "capitalized";
    public const string InService = "in_service";
    public const string Impaired = "impaired";
    public const string Disposed = "disposed";
}

public static class FixedAssetEventTypes
{
    public const string Capitalization = "capitalization";
    public const string PlacedInService = "placed_in_service";
    public const string Depreciation = "depreciation";
    public const string Improvement = "improvement";
    public const string Transfer = "transfer";
    public const string Impairment = "impairment";
    public const string Disposal = "disposal";
    public const string Reversal = "reversal";
}

public sealed class FixedAssetClass : ICompanyOwnedEntity
{
    private FixedAssetClass() { }

    public FixedAssetClass(Guid id, Guid companyId, string code, string name, string bookMethod,
        int usefulLifeMonths, decimal defaultResidualPercent, Guid costAccountId,
        Guid accumulatedDepreciationAccountId, Guid depreciationExpenseAccountId,
        Guid accumulatedImpairmentAccountId, Guid impairmentExpenseAccountId,
        Guid disposalGainAccountId, Guid disposalLossAccountId, string voucherSeriesCode,
        bool requiresApproval, Guid actorUserId, DateTime now)
    {
        Id = IdValue(id, nameof(id)); CompanyId = IdValue(companyId, nameof(companyId));
        Apply(code, name, bookMethod, usefulLifeMonths, defaultResidualPercent, costAccountId,
            accumulatedDepreciationAccountId, depreciationExpenseAccountId, accumulatedImpairmentAccountId,
            impairmentExpenseAccountId, disposalGainAccountId, disposalLossAccountId, voucherSeriesCode,
            requiresApproval, actorUserId, now, false);
        CreatedUtc = Utc(now); Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string BookMethod { get; private set; } = null!;
    public int UsefulLifeMonths { get; private set; }
    public decimal DefaultResidualPercent { get; private set; }
    public Guid CostAccountId { get; private set; }
    public Guid AccumulatedDepreciationAccountId { get; private set; }
    public Guid DepreciationExpenseAccountId { get; private set; }
    public Guid AccumulatedImpairmentAccountId { get; private set; }
    public Guid ImpairmentExpenseAccountId { get; private set; }
    public Guid DisposalGainAccountId { get; private set; }
    public Guid DisposalLossAccountId { get; private set; }
    public string VoucherSeriesCode { get; private set; } = null!;
    public bool RequiresApproval { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string DefinitionHash { get; private set; } = null!;
    public long Version { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<FixedAssetRegisterItem> Assets { get; } = new List<FixedAssetRegisterItem>();

    public void Update(string code, string name, string bookMethod, int usefulLifeMonths,
        decimal defaultResidualPercent, Guid costAccountId, Guid accumulatedDepreciationAccountId,
        Guid depreciationExpenseAccountId, Guid accumulatedImpairmentAccountId, Guid impairmentExpenseAccountId,
        Guid disposalGainAccountId, Guid disposalLossAccountId, string voucherSeriesCode,
        bool requiresApproval, Guid actorUserId, DateTime now) => Apply(code, name, bookMethod,
        usefulLifeMonths, defaultResidualPercent, costAccountId, accumulatedDepreciationAccountId,
        depreciationExpenseAccountId, accumulatedImpairmentAccountId, impairmentExpenseAccountId,
        disposalGainAccountId, disposalLossAccountId, voucherSeriesCode, requiresApproval, actorUserId, now, true);

    public void Deactivate(Guid actorUserId, DateTime now)
    { IsActive = false; UpdatedByUserId = IdValue(actorUserId, nameof(actorUserId)); UpdatedUtc = Utc(now); Version++; }

    private void Apply(string code, string name, string bookMethod, int usefulLifeMonths,
        decimal defaultResidualPercent, Guid costAccountId, Guid accumulatedDepreciationAccountId,
        Guid depreciationExpenseAccountId, Guid accumulatedImpairmentAccountId, Guid impairmentExpenseAccountId,
        Guid disposalGainAccountId, Guid disposalLossAccountId, string voucherSeriesCode,
        bool requiresApproval, Guid actorUserId, DateTime now, bool increment)
    {
        if (usefulLifeMonths is < 1 or > 1200) throw new ArgumentOutOfRangeException(nameof(usefulLifeMonths));
        if (defaultResidualPercent is < 0m or > 100m) throw new ArgumentOutOfRangeException(nameof(defaultResidualPercent));
        var method = FixedAssetBookMethods.Normalize(bookMethod);
        if (!FixedAssetBookMethods.IsSupported(method)) throw new ArgumentOutOfRangeException(nameof(bookMethod), "Unsupported book depreciation method.");
        Code = Text(code, nameof(code), 64).ToUpperInvariant(); Name = Text(name, nameof(name), 200);
        BookMethod = method; UsefulLifeMonths = usefulLifeMonths;
        DefaultResidualPercent = decimal.Round(defaultResidualPercent, 4, MidpointRounding.AwayFromZero);
        CostAccountId = IdValue(costAccountId, nameof(costAccountId));
        AccumulatedDepreciationAccountId = IdValue(accumulatedDepreciationAccountId, nameof(accumulatedDepreciationAccountId));
        DepreciationExpenseAccountId = IdValue(depreciationExpenseAccountId, nameof(depreciationExpenseAccountId));
        AccumulatedImpairmentAccountId = IdValue(accumulatedImpairmentAccountId, nameof(accumulatedImpairmentAccountId));
        ImpairmentExpenseAccountId = IdValue(impairmentExpenseAccountId, nameof(impairmentExpenseAccountId));
        DisposalGainAccountId = IdValue(disposalGainAccountId, nameof(disposalGainAccountId));
        DisposalLossAccountId = IdValue(disposalLossAccountId, nameof(disposalLossAccountId));
        VoucherSeriesCode = Text(voucherSeriesCode, nameof(voucherSeriesCode), 32).ToUpperInvariant();
        RequiresApproval = requiresApproval; UpdatedByUserId = IdValue(actorUserId, nameof(actorUserId)); UpdatedUtc = Utc(now);
        DefinitionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', Code, Name,
            BookMethod, UsefulLifeMonths, DefaultResidualPercent, CostAccountId, AccumulatedDepreciationAccountId,
            DepreciationExpenseAccountId, AccumulatedImpairmentAccountId, ImpairmentExpenseAccountId,
            DisposalGainAccountId, DisposalLossAccountId, VoucherSeriesCode, RequiresApproval)))).ToLowerInvariant();
        if (increment) Version++;
    }

    internal static Guid IdValue(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    internal static string Text(string value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    internal static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value));
    internal static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    internal static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed class FixedAssetRegisterItem : ICompanyOwnedEntity
{
    private FixedAssetRegisterItem() { }

    public FixedAssetRegisterItem(Guid id, Guid companyId, Guid assetClassId, long assetClassVersion,
        string assetClassHash, string assetNumber, string name, string currency, decimal acquisitionCost,
        decimal residualValue, int usefulLifeMonths, string bookMethod, DateOnly acquisitionDate,
        string sourceType, string sourceId, string sourceVersion, Guid? sourceDocumentId,
        Guid? legacyFinanceAssetId, string? custodian, string? location, string dimensionSnapshotJson,
        Guid actorUserId, DateTime now)
    {
        Id = FixedAssetClass.IdValue(id, nameof(id)); CompanyId = FixedAssetClass.IdValue(companyId, nameof(companyId));
        AssetClassId = FixedAssetClass.IdValue(assetClassId, nameof(assetClassId));
        if (assetClassVersion < 1) throw new ArgumentOutOfRangeException(nameof(assetClassVersion));
        AssetClassVersion = assetClassVersion; AssetClassHash = Hash(assetClassHash);
        AssetNumber = FixedAssetClass.Text(assetNumber, nameof(assetNumber), 64).ToUpperInvariant();
        Name = FixedAssetClass.Text(name, nameof(name), 200); Currency = FixedAssetClass.Text(currency, nameof(currency), 3).ToUpperInvariant();
        if (acquisitionCost <= 0m) throw new ArgumentOutOfRangeException(nameof(acquisitionCost));
        AcquisitionCost = FixedAssetClass.Money(acquisitionCost); ResidualValue = FixedAssetClass.Money(residualValue);
        if (ResidualValue < 0m || ResidualValue >= AcquisitionCost) throw new ArgumentOutOfRangeException(nameof(residualValue));
        if (usefulLifeMonths is < 1 or > 1200) throw new ArgumentOutOfRangeException(nameof(usefulLifeMonths));
        UsefulLifeMonths = usefulLifeMonths; BookMethod = FixedAssetBookMethods.Normalize(bookMethod);
        AcquisitionDate = acquisitionDate; SourceType = FixedAssetClass.Text(sourceType, nameof(sourceType), 64).ToLowerInvariant();
        SourceId = FixedAssetClass.Text(sourceId, nameof(sourceId), 200); SourceVersion = FixedAssetClass.Text(sourceVersion, nameof(sourceVersion), 100);
        SourceDocumentId = EmptyToNull(sourceDocumentId); LegacyFinanceAssetId = EmptyToNull(legacyFinanceAssetId);
        Custodian = FixedAssetClass.Optional(custodian, 160); Location = FixedAssetClass.Optional(location, 160);
        DimensionSnapshotJson = FixedAssetClass.Text(dimensionSnapshotJson, nameof(dimensionSnapshotJson), 8000);
        Status = FixedAssetStatuses.Draft; CreatedByUserId = FixedAssetClass.IdValue(actorUserId, nameof(actorUserId));
        CreatedUtc = UpdatedUtc = FixedAssetClass.Utc(now); Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AssetClassId { get; private set; }
    public long AssetClassVersion { get; private set; }
    public string AssetClassHash { get; private set; } = null!;
    public string AssetNumber { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Currency { get; private set; } = null!;
    public decimal AcquisitionCost { get; private set; }
    public decimal ImprovementCost { get; private set; }
    public decimal ResidualValue { get; private set; }
    public decimal AccumulatedDepreciation { get; private set; }
    public decimal AccumulatedImpairment { get; private set; }
    public decimal DisposalProceeds { get; private set; }
    public decimal DisposalGainLoss { get; private set; }
    public int UsefulLifeMonths { get; private set; }
    public string BookMethod { get; private set; } = null!;
    public DateOnly AcquisitionDate { get; private set; }
    public DateOnly? CapitalizationDate { get; private set; }
    public DateOnly? PlacedInServiceDate { get; private set; }
    public DateOnly? LastDepreciationThrough { get; private set; }
    public DateOnly? DisposalDate { get; private set; }
    public string Status { get; private set; } = null!;
    public string SourceType { get; private set; } = null!;
    public string SourceId { get; private set; } = null!;
    public string SourceVersion { get; private set; } = null!;
    public Guid? SourceDocumentId { get; private set; }
    public Guid? LegacyFinanceAssetId { get; private set; }
    public string? Custodian { get; private set; }
    public string? Location { get; private set; }
    public string DimensionSnapshotJson { get; private set; } = null!;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public FixedAssetClass AssetClass { get; private set; } = null!;
    public Company Company { get; private set; } = null!;
    public ICollection<FixedAssetBookEvent> Events { get; } = new List<FixedAssetBookEvent>();
    public ICollection<FixedAssetComponent> Components { get; } = new List<FixedAssetComponent>();
    public decimal GrossBookValue => FixedAssetClass.Money(AcquisitionCost + ImprovementCost);
    public decimal NetBookValue => FixedAssetClass.Money(Math.Max(0m, GrossBookValue - AccumulatedDepreciation - AccumulatedImpairment));

    public void EnsureCanCapitalize(DateOnly date)
    {
        Require(FixedAssetStatuses.Draft);
        if (date < AcquisitionDate) throw new ArgumentOutOfRangeException(nameof(date), "Capitalization cannot precede acquisition.");
    }
    public void MarkCapitalized(DateOnly date, DateTime now) { EnsureCanCapitalize(date); CapitalizationDate = date; Status = FixedAssetStatuses.Capitalized; Touch(now); }
    public void PlaceInService(DateOnly date, DateTime now)
    { if (Status is not (FixedAssetStatuses.Capitalized or FixedAssetStatuses.Impaired)) throw new InvalidOperationException("Only a capitalized asset can be placed in service."); if (date < (CapitalizationDate ?? AcquisitionDate)) throw new ArgumentOutOfRangeException(nameof(date), "Placed-in-service date cannot precede capitalization."); PlacedInServiceDate = date; Status = AccumulatedImpairment > 0m ? FixedAssetStatuses.Impaired : FixedAssetStatuses.InService; Touch(now); }
    public void ApplyDepreciation(decimal amount, DateOnly through, DateTime now)
    { amount = FixedAssetClass.Money(amount); EnsureCanDepreciate(amount, through); ApplyDepreciationBalance(amount, through, now); }
    public void ApplyDepreciation(decimal amount, DateOnly through,
        IReadOnlyList<FixedAssetComponentDepreciationAllocation> allocations, DateTime now)
    {
        amount = FixedAssetClass.Money(amount);
        EnsureCanDepreciate(amount, through);
        if (FixedAssetClass.Money(allocations.Sum(x => x.Amount)) > amount)
            throw new InvalidOperationException("Component allocations exceed total depreciation.");
        var componentAllocations = allocations.Where(x => x.ComponentId.HasValue && x.Amount > 0m)
            .GroupBy(x => x.ComponentId!.Value)
            .Select(x => (ComponentId: x.Key, Amount: FixedAssetClass.Money(x.Sum(a => a.Amount))))
            .ToArray();
        foreach (var allocation in componentAllocations)
        {
            var component = Components.SingleOrDefault(x => x.Id == allocation.ComponentId)
                ?? throw new InvalidOperationException("A retained component allocation is unavailable.");
            component.EnsureCanApplyDepreciation(allocation.Amount);
        }
        foreach (var allocation in componentAllocations)
            Components.Single(x => x.Id == allocation.ComponentId).ApplyDepreciation(allocation.Amount);
        ApplyDepreciationBalance(amount, through, now);
    }
    public void EnsureCanImprove(decimal amount)
    { if (Status is not (FixedAssetStatuses.Capitalized or FixedAssetStatuses.InService or FixedAssetStatuses.Impaired)) throw new InvalidOperationException("Only a capitalized asset can be improved."); if (FixedAssetClass.Money(amount) <= 0m) throw new ArgumentOutOfRangeException(nameof(amount)); }
    public void ApplyImprovement(decimal amount, DateTime now) { EnsureCanImprove(amount); ImprovementCost += FixedAssetClass.Money(amount); Touch(now); }
    public void EnsureCanImpair(decimal amount)
    { if (Status is not (FixedAssetStatuses.Capitalized or FixedAssetStatuses.InService or FixedAssetStatuses.Impaired)) throw new InvalidOperationException("Only a capitalized asset can be impaired."); amount = FixedAssetClass.Money(amount); if (amount <= 0m || amount > NetBookValue - ResidualValue) throw new InvalidOperationException("Impairment exceeds the available carrying amount."); }
    public void ApplyImpairment(decimal amount, DateTime now) { EnsureCanImpair(amount); amount = FixedAssetClass.Money(amount); AccumulatedImpairment += amount; Status = FixedAssetStatuses.Impaired; Touch(now); }
    public void Transfer(string? custodian, string? location, string dimensionSnapshotJson, DateTime now)
    { if (Status == FixedAssetStatuses.Disposed) throw new InvalidOperationException("A disposed asset cannot be transferred."); Custodian = FixedAssetClass.Optional(custodian, 160); Location = FixedAssetClass.Optional(location, 160); DimensionSnapshotJson = FixedAssetClass.Text(dimensionSnapshotJson, nameof(dimensionSnapshotJson), 8000); Touch(now); }
    public void EnsureCanDispose(DateOnly date, decimal proceeds)
    { if (Status is not (FixedAssetStatuses.Capitalized or FixedAssetStatuses.InService or FixedAssetStatuses.Impaired)) throw new InvalidOperationException("Only a capitalized asset can be disposed."); if (date < (CapitalizationDate ?? AcquisitionDate)) throw new ArgumentOutOfRangeException(nameof(date), "Disposal cannot precede capitalization."); if (FixedAssetClass.Money(proceeds) < 0m) throw new ArgumentOutOfRangeException(nameof(proceeds), "Disposal proceeds cannot be negative."); }
    public void Dispose(DateOnly date, decimal proceeds, decimal gainLoss, DateTime now)
    { EnsureCanDispose(date, proceeds); DisposalDate = date; DisposalProceeds = FixedAssetClass.Money(proceeds); DisposalGainLoss = FixedAssetClass.Money(gainLoss); Status = FixedAssetStatuses.Disposed; Touch(now); }
    public void ReverseDepreciation(decimal amount,
        IReadOnlyList<FixedAssetComponentDepreciationAllocation> allocations,
        DateOnly? previousDepreciationThrough, DateTime now)
    {
        amount = FixedAssetClass.Money(amount);
        if (amount <= 0m || amount > AccumulatedDepreciation)
            throw new InvalidOperationException("Depreciation reversal exceeds the retained balance.");
        if (FixedAssetClass.Money(allocations.Sum(x => x.Amount)) > amount)
            throw new InvalidOperationException("Component reversal allocations exceed total depreciation.");
        var componentAllocations = allocations.Where(x => x.ComponentId.HasValue && x.Amount > 0m)
            .GroupBy(x => x.ComponentId!.Value)
            .Select(x => (ComponentId: x.Key, Amount: FixedAssetClass.Money(x.Sum(a => a.Amount))))
            .ToArray();
        foreach (var allocation in componentAllocations)
        {
            var component = Components.SingleOrDefault(x => x.Id == allocation.ComponentId)
                ?? throw new InvalidOperationException("A retained component reversal allocation is unavailable.");
            component.EnsureCanReverseDepreciation(allocation.Amount);
        }
        foreach (var allocation in componentAllocations)
            Components.Single(x => x.Id == allocation.ComponentId).ReverseDepreciation(allocation.Amount);
        AccumulatedDepreciation = FixedAssetClass.Money(AccumulatedDepreciation - amount);
        LastDepreciationThrough = previousDepreciationThrough;
        Touch(now);
    }
    public void ReverseImpairment(decimal amount, DateTime now) { amount = FixedAssetClass.Money(amount); if (amount <= 0m || amount > AccumulatedImpairment) throw new InvalidOperationException("Impairment reversal exceeds the retained balance."); AccumulatedImpairment = FixedAssetClass.Money(AccumulatedImpairment - amount); Status = PlacedInServiceDate.HasValue ? (AccumulatedImpairment > 0m ? FixedAssetStatuses.Impaired : FixedAssetStatuses.InService) : FixedAssetStatuses.Capitalized; Touch(now); }
    public void ReverseDisposal(DateTime now) { if (Status != FixedAssetStatuses.Disposed) throw new InvalidOperationException("Only a disposed asset can have its disposal reversed."); DisposalDate = null; DisposalProceeds = DisposalGainLoss = 0m; Status = PlacedInServiceDate.HasValue ? (AccumulatedImpairment > 0m ? FixedAssetStatuses.Impaired : FixedAssetStatuses.InService) : FixedAssetStatuses.Capitalized; Touch(now); }
    private void Require(string status) { if (Status != status) throw new InvalidOperationException($"Asset must be {status}."); }
    private void EnsureCanDepreciate(decimal amount, DateOnly through)
    { if (Status is not (FixedAssetStatuses.InService or FixedAssetStatuses.Impaired)) throw new InvalidOperationException("Asset is not depreciable."); if (through < PlacedInServiceDate || (LastDepreciationThrough.HasValue && through <= LastDepreciationThrough.Value)) throw new InvalidOperationException("Depreciation must advance the retained through date."); if (amount <= 0m || amount > NetBookValue - ResidualValue) throw new InvalidOperationException("Depreciation exceeds the depreciable carrying amount."); }
    private void ApplyDepreciationBalance(decimal amount, DateOnly through, DateTime now)
    { AccumulatedDepreciation += amount; LastDepreciationThrough = through; Touch(now); }
    private void Touch(DateTime now) { UpdatedUtc = FixedAssetClass.Utc(now); Version++; }
    private static Guid? EmptyToNull(Guid? value) => value == Guid.Empty ? throw new ArgumentException("Identifier cannot be empty.") : value;
    private static string Hash(string value) { value = FixedAssetClass.Text(value, nameof(value), 64).ToLowerInvariant(); return value.Length == 64 ? value : throw new ArgumentOutOfRangeException(nameof(value)); }
}

public sealed class FixedAssetComponent : ICompanyOwnedEntity
{
    private FixedAssetComponent() { }
    public FixedAssetComponent(Guid id, Guid companyId, Guid assetId, string code, string name,
        decimal cost, decimal residualValue, int usefulLifeMonths, DateOnly placedInServiceDate)
    { Id = FixedAssetClass.IdValue(id, nameof(id)); CompanyId = FixedAssetClass.IdValue(companyId, nameof(companyId)); AssetId = FixedAssetClass.IdValue(assetId, nameof(assetId)); Code = FixedAssetClass.Text(code, nameof(code), 64).ToUpperInvariant(); Name = FixedAssetClass.Text(name, nameof(name), 200); Cost = FixedAssetClass.Money(cost); ResidualValue = FixedAssetClass.Money(residualValue); if (Cost <= 0m || ResidualValue < 0m || ResidualValue >= Cost || usefulLifeMonths is < 1 or > 1200) throw new ArgumentOutOfRangeException(nameof(cost)); UsefulLifeMonths = usefulLifeMonths; PlacedInServiceDate = placedInServiceDate; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid AssetId { get; private set; }
    public string Code { get; private set; } = null!; public string Name { get; private set; } = null!;
    public decimal Cost { get; private set; } public decimal ResidualValue { get; private set; }
    public decimal AccumulatedDepreciation { get; private set; } public int UsefulLifeMonths { get; private set; }
    public DateOnly PlacedInServiceDate { get; private set; } public FixedAssetRegisterItem Asset { get; private set; } = null!;
    public void EnsureCanApplyDepreciation(decimal amount) { amount = FixedAssetClass.Money(amount); if (amount <= 0m || AccumulatedDepreciation + amount > Cost - ResidualValue) throw new InvalidOperationException("Component depreciation exceeds its depreciable amount."); }
    public void ApplyDepreciation(decimal amount) { amount = FixedAssetClass.Money(amount); EnsureCanApplyDepreciation(amount); AccumulatedDepreciation += amount; }
    public void EnsureCanReverseDepreciation(decimal amount) { amount = FixedAssetClass.Money(amount); if (amount <= 0m || amount > AccumulatedDepreciation) throw new InvalidOperationException("Component depreciation reversal exceeds its retained balance."); }
    public void ReverseDepreciation(decimal amount) { amount = FixedAssetClass.Money(amount); EnsureCanReverseDepreciation(amount); AccumulatedDepreciation -= amount; }
}

public sealed record FixedAssetComponentDepreciationAllocation(Guid? ComponentId, decimal Amount,
    decimal DepreciableBasis, decimal RemainingDepreciableAmount, int EligibleDays, int DaysInPeriod,
    string Explanation);

public sealed class FixedAssetBookEvent : ICompanyOwnedEntity
{
    private FixedAssetBookEvent() { }
    public FixedAssetBookEvent(Guid id, Guid companyId, Guid assetId, string eventType, DateOnly effectiveDate,
        decimal amount, decimal costMovement, decimal depreciationMovement, decimal impairmentMovement,
        decimal proceeds, decimal gainLoss, string sourceType, string sourceId, string sourceVersion,
        string idempotencyKey, string snapshotHash, Guid? fiscalPeriodId, Guid? ledgerEntryId,
        Guid? depreciationRunId, Guid? originalEventId, string componentAllocationJson,
        Guid actorUserId, DateTime now)
    { Id = FixedAssetClass.IdValue(id, nameof(id)); CompanyId = FixedAssetClass.IdValue(companyId, nameof(companyId)); AssetId = FixedAssetClass.IdValue(assetId, nameof(assetId)); EventType = FixedAssetClass.Text(eventType, nameof(eventType), 32).ToLowerInvariant(); EffectiveDate = effectiveDate; Amount = FixedAssetClass.Money(amount); CostMovement = FixedAssetClass.Money(costMovement); DepreciationMovement = FixedAssetClass.Money(depreciationMovement); ImpairmentMovement = FixedAssetClass.Money(impairmentMovement); Proceeds = FixedAssetClass.Money(proceeds); GainLoss = FixedAssetClass.Money(gainLoss); SourceType = FixedAssetClass.Text(sourceType, nameof(sourceType), 64).ToLowerInvariant(); SourceId = FixedAssetClass.Text(sourceId, nameof(sourceId), 200); SourceVersion = FixedAssetClass.Text(sourceVersion, nameof(sourceVersion), 100); IdempotencyKey = FixedAssetClass.Text(idempotencyKey, nameof(idempotencyKey), 200); SnapshotHash = FixedAssetClass.Text(snapshotHash, nameof(snapshotHash), 64).ToLowerInvariant(); FiscalPeriodId = fiscalPeriodId; LedgerEntryId = ledgerEntryId; DepreciationRunId = depreciationRunId; OriginalEventId = originalEventId; ComponentAllocationJson = FixedAssetClass.Text(componentAllocationJson, nameof(componentAllocationJson), 8000); ActorUserId = FixedAssetClass.IdValue(actorUserId, nameof(actorUserId)); CreatedUtc = FixedAssetClass.Utc(now); Status = "posted"; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid AssetId { get; private set; }
    public string EventType { get; private set; } = null!; public DateOnly EffectiveDate { get; private set; }
    public decimal Amount { get; private set; } public decimal CostMovement { get; private set; }
    public decimal DepreciationMovement { get; private set; } public decimal ImpairmentMovement { get; private set; }
    public decimal Proceeds { get; private set; } public decimal GainLoss { get; private set; }
    public string SourceType { get; private set; } = null!; public string SourceId { get; private set; } = null!;
    public string SourceVersion { get; private set; } = null!; public string IdempotencyKey { get; private set; } = null!;
    public string SnapshotHash { get; private set; } = null!; public string Status { get; private set; } = null!;
    public Guid? FiscalPeriodId { get; private set; } public Guid? LedgerEntryId { get; private set; }
    public Guid? DepreciationRunId { get; private set; } public Guid? OriginalEventId { get; private set; }
    public string ComponentAllocationJson { get; private set; } = "[]";
    public Guid ActorUserId { get; private set; } public DateTime CreatedUtc { get; private set; }
    public FixedAssetRegisterItem Asset { get; private set; } = null!;
    public void MarkReversed() { if (Status != "posted") throw new InvalidOperationException("Event is already reversed."); Status = "reversed"; }
}

public sealed class FixedAssetMigrationConflict : ICompanyOwnedEntity
{
    private FixedAssetMigrationConflict() { }
    public FixedAssetMigrationConflict(Guid id, Guid companyId, Guid legacyFinanceAssetId, string reasonCode,
        string explanation, string legacySnapshotJson, DateTime now)
    { Id = FixedAssetClass.IdValue(id, nameof(id)); CompanyId = FixedAssetClass.IdValue(companyId, nameof(companyId)); LegacyFinanceAssetId = FixedAssetClass.IdValue(legacyFinanceAssetId, nameof(legacyFinanceAssetId)); ReasonCode = FixedAssetClass.Text(reasonCode, nameof(reasonCode), 100); Explanation = FixedAssetClass.Text(explanation, nameof(explanation), 1000); LegacySnapshotJson = FixedAssetClass.Text(legacySnapshotJson, nameof(legacySnapshotJson), 8000); Status = "open"; CreatedUtc = FixedAssetClass.Utc(now); }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid LegacyFinanceAssetId { get; private set; }
    public string ReasonCode { get; private set; } = null!; public string Explanation { get; private set; } = null!;
    public string LegacySnapshotJson { get; private set; } = null!; public string Status { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; } public DateTime? ResolvedUtc { get; private set; }
    public FinanceAsset LegacyFinanceAsset { get; private set; } = null!;
}

public sealed class FixedAssetDepreciationRun : ICompanyOwnedEntity
{
    private FixedAssetDepreciationRun() { }
    public FixedAssetDepreciationRun(Guid id, Guid companyId, Guid fiscalPeriodId, DateOnly periodStart,
        DateOnly periodEnd, string idempotencyKey, string populationHash, Guid actorUserId, DateTime now)
    { Id = FixedAssetClass.IdValue(id, nameof(id)); CompanyId = FixedAssetClass.IdValue(companyId, nameof(companyId)); FiscalPeriodId = FixedAssetClass.IdValue(fiscalPeriodId, nameof(fiscalPeriodId)); if (periodEnd < periodStart) throw new ArgumentOutOfRangeException(nameof(periodEnd)); PeriodStart = periodStart; PeriodEnd = periodEnd; IdempotencyKey = FixedAssetClass.Text(idempotencyKey, nameof(idempotencyKey), 200); PopulationHash = FixedAssetClass.Text(populationHash, nameof(populationHash), 64).ToLowerInvariant(); ActorUserId = FixedAssetClass.IdValue(actorUserId, nameof(actorUserId)); Status = "previewed"; CreatedUtc = UpdatedUtc = FixedAssetClass.Utc(now); Version = 1; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid FiscalPeriodId { get; private set; }
    public DateOnly PeriodStart { get; private set; } public DateOnly PeriodEnd { get; private set; }
    public string IdempotencyKey { get; private set; } = null!; public string PopulationHash { get; private set; } = null!;
    public string Status { get; private set; } = null!; public decimal TotalAmount { get; private set; }
    public int PostedItemCount { get; private set; } public int ExceptionCount { get; private set; }
    public Guid ActorUserId { get; private set; } public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; } public long Version { get; private set; }
    public Company Company { get; private set; } = null!; public ICollection<FixedAssetDepreciationRunItem> Items { get; } = new List<FixedAssetDepreciationRunItem>();
    public void Complete(decimal total, int posted, int exceptions, DateTime now) { Status = exceptions == 0 ? "posted" : posted > 0 ? "posted_with_exceptions" : "blocked"; TotalAmount = FixedAssetClass.Money(total); PostedItemCount = posted; ExceptionCount = exceptions; UpdatedUtc = FixedAssetClass.Utc(now); Version++; }
}

public sealed class FixedAssetDepreciationRunItem : ICompanyOwnedEntity
{
    private FixedAssetDepreciationRunItem() { }
    public FixedAssetDepreciationRunItem(Guid id, Guid companyId, Guid runId, Guid assetId, long assetVersion,
        string assetClassHash, decimal openingCost, decimal openingAccumulatedDepreciation,
        decimal openingAccumulatedImpairment, decimal residualValue, decimal amount, string calculationExplanation)
    { Id = FixedAssetClass.IdValue(id, nameof(id)); CompanyId = FixedAssetClass.IdValue(companyId, nameof(companyId)); RunId = FixedAssetClass.IdValue(runId, nameof(runId)); AssetId = FixedAssetClass.IdValue(assetId, nameof(assetId)); if (assetVersion < 1) throw new ArgumentOutOfRangeException(nameof(assetVersion)); AssetVersion = assetVersion; AssetClassHash = FixedAssetClass.Text(assetClassHash, nameof(assetClassHash), 64).ToLowerInvariant(); OpeningCost = FixedAssetClass.Money(openingCost); OpeningAccumulatedDepreciation = FixedAssetClass.Money(openingAccumulatedDepreciation); OpeningAccumulatedImpairment = FixedAssetClass.Money(openingAccumulatedImpairment); ResidualValue = FixedAssetClass.Money(residualValue); Amount = FixedAssetClass.Money(amount); CalculationExplanation = FixedAssetClass.Text(calculationExplanation, nameof(calculationExplanation), 1000); Status = Amount > 0m ? "pending" : "skipped"; }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid RunId { get; private set; }
    public Guid AssetId { get; private set; } public long AssetVersion { get; private set; } public string AssetClassHash { get; private set; } = null!;
    public decimal OpeningCost { get; private set; } public decimal OpeningAccumulatedDepreciation { get; private set; }
    public decimal OpeningAccumulatedImpairment { get; private set; } public decimal ResidualValue { get; private set; }
    public decimal Amount { get; private set; } public string CalculationExplanation { get; private set; } = null!;
    public string Status { get; private set; } = null!; public Guid? LedgerEntryId { get; private set; }
    public string? FailureCode { get; private set; } public string? FailureSummary { get; private set; }
    public FixedAssetDepreciationRun Run { get; private set; } = null!; public FixedAssetRegisterItem Asset { get; private set; } = null!;
    public void MarkPosted(Guid ledgerEntryId) { LedgerEntryId = FixedAssetClass.IdValue(ledgerEntryId, nameof(ledgerEntryId)); Status = "posted"; }
    public void MarkFailed(string code, string summary) { FailureCode = FixedAssetClass.Text(code, nameof(code), 100); FailureSummary = FixedAssetClass.Text(summary, nameof(summary), 1000); Status = "failed"; }
}
