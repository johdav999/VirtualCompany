namespace VirtualCompany.Application.Finance;

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
public interface IFinanceRollingPlanningService
{
    Task<FinancePlanningReport> ReportAsync(Guid company, FinancePlanningQuery query, CancellationToken ct);
    Task<FinancePlanningExport> ExportAsync(Guid company, FinancePlanningQuery query, CancellationToken ct);
    Task<FinanceVarianceExplanationDto> ExplainAsync(Guid company, ExplainFinanceVariance command, CancellationToken ct);
    Task<FinanceForecastPreview> PreviewAsync(Guid company, PreviewFinanceForecast command, CancellationToken ct);
    Task<FinanceForecastRevision> SaveAsync(Guid company, SaveFinanceForecast command, CancellationToken ct);
    Task<FinanceForecastRevision> OpenAsync(Guid company, Guid id, CancellationToken ct);
    Task<IReadOnlyList<FinanceForecastRevisionSummary>> HistoryAsync(Guid company, int skip, CancellationToken ct);
    Task<FinanceForecastComparison> CompareAsync(Guid company, Guid earlier, Guid later, string? currency, Guid? costCenter, CancellationToken ct);
    Task<FinancePlanningExport> ComparisonExportAsync(Guid company, Guid earlier, Guid later, string? currency, Guid? costCenter, CancellationToken ct);
}

public static class FinanceRollingPlanningCalculation
{
    public const string Version = "finance-rolling-planning.v1";
    public static decimal? Difference(decimal? actual, decimal? comparison) =>
        actual.HasValue && comparison.HasValue ? decimal.Round(actual.Value - comparison.Value, 2, MidpointRounding.AwayFromZero) : null;
    public static decimal? Percentage(decimal? difference, decimal? comparison) =>
        difference.HasValue && comparison is not null and not 0 ? decimal.Round(difference.Value / comparison.Value * 100m, 2, MidpointRounding.AwayFromZero) : null;
    public static IReadOnlyList<FinanceForecastValue> Calculate(FinancePlanningReport report, PreviewFinanceForecast input)
    {
        if (input.Assumptions is null || input.Assumptions.Count is < 1 or > 500 || input.Notes is null ||
            input.Notes.Trim().Length is < 1 or > 2000 || input.ActualThroughUtc.Day != 1 || input.ActualThroughUtc.TimeOfDay != TimeSpan.Zero)
            throw new ArgumentException("Provide future assumptions, a month boundary and an explanation of the changes.");
        var start = new DateTime(report.Query.Year, report.Query.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(report.Query.Months);
        if (input.ActualThroughUtc < start || input.ActualThroughUtc >= end) throw new ArgumentException("Actual cutoff must fall within the selected forecast horizon.");
        if (input.Assumptions.GroupBy(x => (x.MonthUtc, x.AccountId, x.CostCenterId)).Any(x => x.Count() > 1))
            throw new ArgumentException("An account and dimension may have only one assumption per month.");
        foreach (var a in input.Assumptions)
            if (a.MonthUtc <= input.ActualThroughUtc || a.MonthUtc >= end || a.MonthUtc.Day != 1 || a.MonthUtc.TimeOfDay != TimeSpan.Zero ||
                a.AccountId == Guid.Empty || a.CostCenterId == Guid.Empty || a.Currency.Length != 3 || !a.Currency.All(char.IsAsciiLetterUpper) ||
                decimal.Round(a.Amount, 2) != a.Amount || Math.Abs(a.Amount) > 9999999999999999.99m || string.IsNullOrWhiteSpace(a.Rationale) || a.Rationale.Length > 1000)
                throw new ArgumentException("Use future months, valid accounts/currencies, two-decimal amounts and explicit rationales.");
        var values = report.Rows.Where(x => x.MonthUtc <= input.ActualThroughUtc).Select(x => new FinanceForecastValue(
            x.MonthUtc, x.AccountId, x.CostCenterId, x.Currency, x.Actual, x.Budget, null, "Posted actual retained; no forecast writes.")).ToList();
        foreach (var a in input.Assumptions.OrderBy(x => x.MonthUtc).ThenBy(x => x.AccountId).ThenBy(x => x.CostCenterId))
        {
            var row = report.Rows.SingleOrDefault(x => x.MonthUtc == a.MonthUtc && x.AccountId == a.AccountId && x.CostCenterId == a.CostCenterId && x.Currency == a.Currency);
            values.Add(new(a.MonthUtc, a.AccountId, a.CostCenterId, a.Currency, null, row?.Budget, a.Amount, a.Rationale.Trim()));
        }
        return values;
    }
}
