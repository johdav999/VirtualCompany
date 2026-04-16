namespace VirtualCompany.Application.Cockpit;

public sealed record GetExecutiveCockpitKpiDashboardQuery(
    Guid CompanyId,
    string? Department,
    DateTime? StartUtc,
    DateTime? EndUtc);

public sealed record ExecutiveCockpitKpiDashboardDto(
    Guid CompanyId,
    DateTime GeneratedAtUtc,
    DateTime StartUtc,
    DateTime EndUtc,
    string? Department,
    IReadOnlyList<string> Departments,
    IReadOnlyList<ExecutiveCockpitKpiTileDto> Kpis,
    IReadOnlyList<ExecutiveCockpitAnomalyDto> Anomalies);

public sealed record ExecutiveCockpitKpiTileDto(
    string Key,
    string Label,
    string Department,
    decimal CurrentValue,
    decimal? BaselineValue,
    decimal? DeltaValue,
    decimal? DeltaPercentage,
    string TrendDirection,
    string ComparisonLabel,
    DateTime AsOfUtc,
    string? Unit,
    string? Route);

public sealed record ExecutiveCockpitAnomalyDto(
    string KpiKey,
    string Label,
    string Department,
    string Severity,
    DateTime OccurredUtc,
    string Reason,
    decimal CurrentValue,
    decimal? BaselineValue,
    decimal? ThresholdValue,
    decimal? DeviationPercentage,
    string Summary,
    string? Route);

public sealed record ExecutiveCockpitKpiMetricDefinition(
    string Key,
    string Label,
    string? Unit,
    bool HigherIsRisk,
    decimal? WarningThreshold,
    decimal? CriticalThreshold,
    decimal WarningDeviationPercentage,
    decimal CriticalDeviationPercentage);

public sealed record ExecutiveCockpitKpiMetricValue(
    string Key,
    string Label,
    string Department,
    decimal CurrentValue,
    decimal? BaselineValue,
    DateTime AsOfUtc,
    string? Route,
    ExecutiveCockpitKpiMetricDefinition Definition);

public sealed record ExecutiveCockpitBaselineComparison(
    decimal? DeltaValue,
    decimal? DeltaPercentage,
    string TrendDirection);

public sealed record ExecutiveCockpitAnomalyEvaluation(
    string Reason,
    string Severity,
    decimal? ThresholdValue,
    decimal? DeviationPercentage,
    string Summary);

public static class ExecutiveCockpitKpiAnomalyReasons
{
    public const string ThresholdBreach = "threshold_breach";
    public const string BaselineDeviation = "baseline_deviation";
}

public static class ExecutiveCockpitKpiSeverity
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";

    public static string Max(string left, string right)
    {
        return Rank(right) > Rank(left) ? right : left;

        static int Rank(string severity) =>
            severity.Trim().ToLowerInvariant() switch
            {
                Critical => 3,
                Warning => 2,
                Info => 1,
                _ => 0
            };
    }
}

public static class ExecutiveCockpitKpiMetricCatalog
{
    public static IReadOnlyList<ExecutiveCockpitKpiMetricDefinition> Defaults { get; } =
    [
        new(
            "open_tasks",
            "Open tasks",
            null,
            HigherIsRisk: true,
            WarningThreshold: 5,
            CriticalThreshold: 10,
            WarningDeviationPercentage: 50,
            CriticalDeviationPercentage: 100),
        new(
            "blocked_tasks",
            "Blocked tasks",
            null,
            HigherIsRisk: true,
            WarningThreshold: 1,
            CriticalThreshold: 3,
            WarningDeviationPercentage: 50,
            CriticalDeviationPercentage: 100),
        new(
            "pending_approvals",
            "Pending approvals",
            null,
            HigherIsRisk: true,
            WarningThreshold: 3,
            CriticalThreshold: 8,
            WarningDeviationPercentage: 50,
            CriticalDeviationPercentage: 100),
        new(
            "completed_tasks",
            "Completed tasks",
            null,
            HigherIsRisk: false,
            WarningThreshold: null,
            CriticalThreshold: null,
            WarningDeviationPercentage: 50,
            CriticalDeviationPercentage: 80),
        new(
            "active_workflows",
            "Active workflows",
            null,
            HigherIsRisk: false,
            WarningThreshold: null,
            CriticalThreshold: null,
            WarningDeviationPercentage: 50,
            CriticalDeviationPercentage: 80),
        new(
            "workflow_exceptions",
            "Workflow exceptions",
            null,
            HigherIsRisk: true,
            WarningThreshold: 1,
            CriticalThreshold: 3,
            WarningDeviationPercentage: 50,
            CriticalDeviationPercentage: 100)
    ];

    public static ExecutiveCockpitKpiMetricDefinition Find(string key) =>
        Defaults.First(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
}

public static class ExecutiveCockpitKpiCalculator
{
    private const decimal FlatTolerance = 0.0001m;

    public static ExecutiveCockpitBaselineComparison Compare(decimal currentValue, decimal? baselineValue)
    {
        if (baselineValue is null)
        {
            return new ExecutiveCockpitBaselineComparison(
                null,
                null,
                ExecutiveCockpitTrendDirections.Unknown);
        }

        var delta = currentValue - baselineValue.Value;
        var direction = Math.Abs(delta) <= FlatTolerance
            ? ExecutiveCockpitTrendDirections.Flat
            : delta > 0
                ? ExecutiveCockpitTrendDirections.Up
                : ExecutiveCockpitTrendDirections.Down;

        decimal? percentage = baselineValue.Value == 0
            ? null
            : Math.Round(delta / Math.Abs(baselineValue.Value) * 100m, 1, MidpointRounding.AwayFromZero);

        return new ExecutiveCockpitBaselineComparison(delta, percentage, direction);
    }

    public static ExecutiveCockpitKpiTileDto CreateTile(
        ExecutiveCockpitKpiMetricValue metric,
        string comparisonLabel)
    {
        var comparison = Compare(metric.CurrentValue, metric.BaselineValue);
        return new ExecutiveCockpitKpiTileDto(
            metric.Key,
            metric.Label,
            metric.Department,
            metric.CurrentValue,
            metric.BaselineValue,
            comparison.DeltaValue,
            comparison.DeltaPercentage,
            comparison.TrendDirection,
            comparisonLabel,
            metric.AsOfUtc,
            metric.Definition.Unit,
            metric.Route);
    }

    public static ExecutiveCockpitAnomalyEvaluation? DetectAnomaly(
        ExecutiveCockpitKpiMetricValue metric)
    {
        var comparison = Compare(metric.CurrentValue, metric.BaselineValue);
        var threshold = EvaluateThreshold(metric.CurrentValue, metric.Definition);
        var deviation = EvaluateDeviation(comparison.DeltaPercentage, metric.Definition);

        if (threshold is null && deviation is null)
        {
            return null;
        }

        var reason = threshold is not null && deviation is not null
            ? $"{ExecutiveCockpitKpiAnomalyReasons.ThresholdBreach},{ExecutiveCockpitKpiAnomalyReasons.BaselineDeviation}"
            : threshold is not null
                ? ExecutiveCockpitKpiAnomalyReasons.ThresholdBreach
                : ExecutiveCockpitKpiAnomalyReasons.BaselineDeviation;

        var severity = threshold?.Severity ?? ExecutiveCockpitKpiSeverity.Info;
        if (deviation is not null)
        {
            severity = ExecutiveCockpitKpiSeverity.Max(severity, deviation.Value.Severity);
        }

        var summary = threshold is not null && deviation is not null
            ? $"{metric.Label} breached its configured threshold and moved {Math.Abs(deviation.Value.DeviationPercentage):0.#}% from baseline."
            : threshold is not null
                ? $"{metric.Label} breached the configured {threshold.Value.Severity} threshold."
                : $"{metric.Label} moved {Math.Abs(deviation!.Value.DeviationPercentage):0.#}% from baseline.";

        return new ExecutiveCockpitAnomalyEvaluation(
            reason,
            severity,
            threshold?.ThresholdValue,
            deviation?.DeviationPercentage,
            summary);
    }

    private static (string Severity, decimal ThresholdValue)? EvaluateThreshold(
        decimal currentValue,
        ExecutiveCockpitKpiMetricDefinition definition)
    {
        if (!definition.HigherIsRisk)
        {
            return null;
        }

        if (definition.CriticalThreshold is decimal critical && currentValue >= critical)
        {
            return (ExecutiveCockpitKpiSeverity.Critical, critical);
        }

        if (definition.WarningThreshold is decimal warning && currentValue >= warning)
        {
            return (ExecutiveCockpitKpiSeverity.Warning, warning);
        }

        return null;
    }

    private static (string Severity, decimal DeviationPercentage)? EvaluateDeviation(
        decimal? deltaPercentage,
        ExecutiveCockpitKpiMetricDefinition definition)
    {
        if (deltaPercentage is null)
        {
            return null;
        }

        var riskDeviation = definition.HigherIsRisk
            ? Math.Max(0, deltaPercentage.Value)
            : Math.Max(0, -deltaPercentage.Value);

        if (riskDeviation >= definition.CriticalDeviationPercentage)
        {
            return (ExecutiveCockpitKpiSeverity.Critical, deltaPercentage.Value);
        }

        if (riskDeviation >= definition.WarningDeviationPercentage)
        {
            return (ExecutiveCockpitKpiSeverity.Warning, deltaPercentage.Value);
        }

        return null;
    }
}

public interface IExecutiveCockpitKpiQueryService
{
    Task<ExecutiveCockpitKpiDashboardDto> GetAsync(
        GetExecutiveCockpitKpiDashboardQuery query,
        CancellationToken cancellationToken);
}
