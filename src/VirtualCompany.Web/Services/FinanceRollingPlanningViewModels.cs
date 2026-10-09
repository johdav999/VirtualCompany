namespace VirtualCompany.Web.Services;

public sealed record FinancePlanningQuery(int Year, int Month, int Months = 1, string? BudgetVersion = null,
    string? ForecastVersion = null, string? Currency = null, Guid? FinanceAccountId = null, Guid? CostCenterId = null,
    Guid? FiscalPeriodId = null);
public sealed record FinancePlanningChoice(Guid Id, string Name);
public sealed record FinancePlanningPeriod(Guid Id, string Name, DateTime StartUtc, DateTime EndUtc, bool Closed);
public sealed record FinancePlanningSource(Guid Id, string Kind, Guid AccountId, string AccountCode, string AccountName,
    DateTime MonthUtc, Guid? CostCenterId, decimal Amount, string Currency, string? Version, DateTime UpdatedUtc,
    string Description, string DeepLink);
public sealed record FinancePlanningRow(DateTime MonthUtc, Guid AccountId, string AccountCode, string AccountName,
    Guid? CostCenterId, string Currency, decimal? Actual, decimal? Budget, decimal? Forecast, decimal? PriorYear,
    decimal? Variance, decimal? VariancePercent);
public sealed record FinanceVarianceExplanationDto(Guid Id, DateTime MonthUtc, Guid AccountId, Guid? CostCenterId,
    string Currency, string? BudgetVersion, string Text, DateTime SavedUtc, string SourceFingerprint);
public sealed record FinancePlanningReport(Guid CompanyId, FinancePlanningQuery Query, DateTime AsOfUtc,
    string CalculationVersion, string Fingerprint, IReadOnlyList<FinancePlanningRow> Rows,
    IReadOnlyList<FinancePlanningSource> Sources, IReadOnlyList<string> BudgetVersions, IReadOnlyList<string> ForecastVersions,
    IReadOnlyList<FinancePlanningChoice> Accounts, IReadOnlyList<FinancePlanningChoice> CostCenters,
    IReadOnlyList<FinancePlanningPeriod> FiscalPeriods, IReadOnlyList<string> Coverage,
    IReadOnlyList<FinanceVarianceExplanationDto> Explanations, IReadOnlyDictionary<string, string>? ForecastLabels = null);
public sealed record ExplainFinanceVariance(Guid RequestId, FinancePlanningQuery Query, DateTime MonthUtc,
    Guid AccountId, Guid? CostCenterId, string Currency, string SourceFingerprint, string Text);
public sealed record FinanceForecastAssumption(DateTime MonthUtc, Guid AccountId, Guid? CostCenterId,
    string Currency, decimal Amount, string Rationale);
public sealed record PreviewFinanceForecast(FinancePlanningQuery Query, DateTime ActualThroughUtc,
    IReadOnlyList<FinanceForecastAssumption> Assumptions, string Notes);
public sealed record FinanceForecastValue(DateTime MonthUtc, Guid AccountId, Guid? CostCenterId, string Currency,
    decimal? Actual, decimal? Budget, decimal? Forecast, string Explanation);
public sealed record FinanceForecastPreview(FinancePlanningReport Report, PreviewFinanceForecast Input,
    IReadOnlyList<FinanceForecastValue> Values, string Fingerprint);
public sealed record SaveFinanceForecast(Guid RequestId, string Name, PreviewFinanceForecast Input,
    string ExpectedFingerprint, Guid? PreviousId = null);
public sealed record FinanceForecastRevisionSummary(Guid Id, string Name, string NativeVersion, Guid? PreviousId,
    Guid AuthorId, DateTime SavedUtc, DateTime SourceAsOfUtc);
public sealed record FinanceForecastRevision(FinanceForecastRevisionSummary Summary, FinanceForecastPreview Preview,
    string Checksum);
public sealed record FinanceForecastComparisonRow(DateTime MonthUtc, Guid AccountId, Guid? CostCenterId, string Currency,
    decimal? EarlierActual, decimal? LaterActual, decimal? EarlierForecast, decimal? LaterForecast,
    decimal? Change, string Explanation);
public sealed record FinanceForecastComparison(FinanceForecastRevision Earlier, FinanceForecastRevision Later,
    IReadOnlyList<FinanceForecastComparisonRow> Rows);
public sealed record FinancePlanningExport(string FileName, string Content, string Fingerprint);

