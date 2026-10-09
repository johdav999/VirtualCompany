namespace VirtualCompany.Web.Services;
public sealed record ScenarioCheckpointInput(int Year, string Title, Guid OwnerId, Guid InitiativeId);
public sealed record ScenarioCheckpointView(int Year,string Title,Guid OwnerId,string Owner,Guid InitiativeId);
public sealed record StrategicScenarioInput(string Name, Guid OwnerId, Guid AnnualPlanId, Guid ForecastRevisionId,
    string Currency, ScenarioDrivers Drivers, IReadOnlyList<ScenarioCashInput> Cash,
    IReadOnlyList<ScenarioCheckpointInput> Checkpoints, string Notes);
public sealed record ScenarioSource(Guid AnnualPlanId, int AnnualVersion, string AnnualFingerprint, string AnnualStatus,
    int FiscalYear, DateTime StartUtc, DateTime EndUtc, string Timezone, string Currency, Guid ForecastRevisionId,
    string ForecastName, string NativeForecastVersion, string ForecastChecksum, DateTime SourceAsOfUtc,
    int SourceYear, int SourceMonth, int SourceMonths, decimal Revenue, decimal Expense,
    IReadOnlyList<string> Provenance, IReadOnlyList<PlanningInitiative> Dependencies);
public sealed record StrategicScenarioOptions(Guid CompanyId, int FiscalYear, IReadOnlyList<AnnualPlanSummary> AnnualPlans,
    IReadOnlyList<FinanceForecastRevisionSummary> Forecasts, IReadOnlyList<PlanningOwner> Owners);
public sealed record StrategicScenarioPreview(Guid CompanyId, StrategicScenarioInput Input, string Owner,
    ScenarioSource Source, string CalculationVersion, IReadOnlyList<ScenarioYearResult> Years, IReadOnlyList<ScenarioCheckpointView> Checkpoints,
    IReadOnlyList<string> Warnings, string Fingerprint, string Formulas, string Limitations);
public sealed record StrategicScenarioSummary(Guid Id, Guid CompanyId, Guid SeriesId, int Revision, Guid? PreviousId,
    Guid? DerivedFromId, string Name, Guid OwnerId, string Owner, DateTime SavedUtc, int FiscalYear, string Currency);
public sealed record StrategicScenarioDocument(StrategicScenarioSummary Summary, StrategicScenarioPreview Scenario);
public sealed record StrategicScenarioHistoryPage(Guid CompanyId,int Skip,bool HasMore,IReadOnlyList<StrategicScenarioSummary> Items);
public sealed record SaveStrategicScenario(Guid RequestId, StrategicScenarioInput Input, string ExpectedFingerprint,
    Guid? PreviousId = null, int ExpectedRevision = 0);
public sealed record DuplicateStrategicScenario(Guid RequestId, string Name);
public sealed record StrategicScenarioDelta(int Year, decimal Revenue, decimal Cost, decimal ClosingCash, decimal FundingGap, decimal CapacityShortfall);
public sealed record StrategicScenarioComparison(Guid CompanyId, StrategicScenarioDocument Baseline,
    StrategicScenarioDocument Alternative, IReadOnlyList<StrategicScenarioDelta> Deltas, IReadOnlyList<string> AssumptionChanges);
public interface IStrategicScenarioService
{
    Task<StrategicScenarioOptions> OptionsAsync(Guid company,int fiscalYear,CancellationToken ct);
    Task<StrategicScenarioHistoryPage> HistoryAsync(Guid company,int skip,CancellationToken ct);
    Task<StrategicScenarioPreview> PreviewAsync(Guid company,StrategicScenarioInput input,CancellationToken ct);
    Task<StrategicScenarioDocument> SaveAsync(Guid company,SaveStrategicScenario command,CancellationToken ct);
    Task<StrategicScenarioDocument> OpenAsync(Guid company,Guid id,CancellationToken ct);
    Task<StrategicScenarioDocument> DuplicateAsync(Guid company,Guid id,DuplicateStrategicScenario command,CancellationToken ct);
    Task<StrategicScenarioComparison> CompareAsync(Guid company,Guid baseline,Guid alternative,CancellationToken ct);
}

public sealed record ScenarioDrivers(int Years, string CapacityUnit, decimal SourceToAnnualScale,
    decimal DemandUnits, decimal CapacityUnits, decimal DemandGrowthPercent, decimal PriceGrowthPercent,
    decimal CostGrowthPercent, decimal CapacityGrowthPercent, decimal VariableCostShare,
    decimal OpeningCash, decimal OpeningReceivables, decimal OpeningPayables,
    decimal CollectionShare, decimal PaymentShare, decimal CashFloor);
public sealed record ScenarioCashInput(int Year, decimal Investment, decimal Funding, string Rationale);
public sealed record ScenarioYearResult(int Year, decimal Demand, decimal Capacity, decimal Fulfilled,
    decimal CapacityShortfall, decimal Revenue, decimal OperatingCost, decimal OpeningCash,
    decimal Collections, decimal Payments, decimal Investment, decimal Funding, decimal ClosingCash,
    decimal Receivables, decimal Payables, decimal FundingGap);
