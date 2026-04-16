using VirtualCompany.Application.Cockpit;

namespace VirtualCompany.Api.Tests;

public sealed class ExecutiveCockpitKpiCalculatorTests
{
    [Theory]
    [InlineData(12, 10, "up", 2, 20)]
    [InlineData(8, 10, "down", -2, -20)]
    [InlineData(10, 10, "flat", 0, 0)]
    public void Compare_derives_trend_and_delta(decimal current, decimal baseline, string direction, decimal delta, decimal percentage)
    {
        var comparison = ExecutiveCockpitKpiCalculator.Compare(current, baseline);

        Assert.Equal(direction, comparison.TrendDirection);
        Assert.Equal(delta, comparison.DeltaValue);
        Assert.Equal(percentage, comparison.DeltaPercentage);
    }

    [Fact]
    public void Compare_handles_missing_baseline()
    {
        var comparison = ExecutiveCockpitKpiCalculator.Compare(4, null);

        Assert.Equal("unknown", comparison.TrendDirection);
        Assert.Null(comparison.DeltaValue);
        Assert.Null(comparison.DeltaPercentage);
    }

    [Fact]
    public void Compare_handles_zero_baseline_without_dividing_by_zero()
    {
        var comparison = ExecutiveCockpitKpiCalculator.Compare(4, 0);

        Assert.Equal("up", comparison.TrendDirection);
        Assert.Equal(4, comparison.DeltaValue);
        Assert.Null(comparison.DeltaPercentage);
    }

    [Fact]
    public void DetectAnomaly_returns_threshold_breach()
    {
        var metric = Metric(
            "blocked_tasks",
            3,
            2,
            ExecutiveCockpitKpiMetricCatalog.Find("blocked_tasks"));

        var anomaly = ExecutiveCockpitKpiCalculator.DetectAnomaly(metric);

        Assert.NotNull(anomaly);
        Assert.Equal("threshold_breach", anomaly!.Reason);
        Assert.Equal("critical", anomaly.Severity);
        Assert.Equal(3, anomaly.ThresholdValue);
    }

    [Fact]
    public void DetectAnomaly_returns_baseline_deviation_for_higher_is_risk_metric()
    {
        var definition = ExecutiveCockpitKpiMetricCatalog.Find("open_tasks") with
        {
            WarningThreshold = null,
            CriticalThreshold = null,
            WarningDeviationPercentage = 25,
            CriticalDeviationPercentage = 75
        };
        var metric = Metric("open_tasks", 15, 10, definition);

        var anomaly = ExecutiveCockpitKpiCalculator.DetectAnomaly(metric);

        Assert.NotNull(anomaly);
        Assert.Equal("baseline_deviation", anomaly!.Reason);
        Assert.Equal("warning", anomaly.Severity);
        Assert.Equal(50, anomaly.DeviationPercentage);
    }

    [Fact]
    public void DetectAnomaly_returns_baseline_deviation_for_lower_is_risk_metric()
    {
        var definition = ExecutiveCockpitKpiMetricCatalog.Find("completed_tasks") with
        {
            WarningDeviationPercentage = 25,
            CriticalDeviationPercentage = 75
        };
        var metric = Metric("completed_tasks", 5, 10, definition);

        var anomaly = ExecutiveCockpitKpiCalculator.DetectAnomaly(metric);

        Assert.NotNull(anomaly);
        Assert.Equal("baseline_deviation", anomaly!.Reason);
        Assert.Equal("warning", anomaly.Severity);
        Assert.Equal(-50, anomaly.DeviationPercentage);
    }

    [Fact]
    public void DetectAnomaly_returns_highest_severity_when_multiple_conditions_match()
    {
        var definition = ExecutiveCockpitKpiMetricCatalog.Find("open_tasks") with
        {
            WarningThreshold = 5,
            CriticalThreshold = 20,
            WarningDeviationPercentage = 25,
            CriticalDeviationPercentage = 75
        };
        var metric = Metric("open_tasks", 15, 5, definition);

        var anomaly = ExecutiveCockpitKpiCalculator.DetectAnomaly(metric);

        Assert.NotNull(anomaly);
        Assert.Equal("threshold_breach,baseline_deviation", anomaly!.Reason);
        Assert.Equal("critical", anomaly.Severity);
        Assert.Equal(5, anomaly.ThresholdValue);
        Assert.Equal(200, anomaly.DeviationPercentage);
    }

    [Fact]
    public void DetectAnomaly_returns_null_when_metric_is_within_baseline_and_thresholds()
    {
        var metric = Metric(
            "open_tasks",
            4,
            4,
            ExecutiveCockpitKpiMetricCatalog.Find("open_tasks"));

        Assert.Null(ExecutiveCockpitKpiCalculator.DetectAnomaly(metric));
    }

    [Theory]
    [InlineData(4, 4, null)]
    [InlineData(5, 4, "warning")]
    [InlineData(9, 4, "warning")]
    [InlineData(10, 4, "critical")]
    public void Severity_mapping_respects_threshold_boundaries(decimal current, decimal baseline, string? expectedSeverity)
    {
        var definition = ExecutiveCockpitKpiMetricCatalog.Find("open_tasks") with
        {
            WarningDeviationPercentage = 500,
            CriticalDeviationPercentage = 1000
        };
        var metric = Metric("open_tasks", current, baseline, definition);

        var anomaly = ExecutiveCockpitKpiCalculator.DetectAnomaly(metric);

        Assert.Equal(expectedSeverity, anomaly?.Severity);
    }

    [Theory]
    [InlineData(14, 10, "warning")]
    [InlineData(18, 10, "critical")]
    public void Severity_mapping_respects_baseline_deviation_boundaries(
        decimal current,
        decimal baseline,
        string expectedSeverity)
    {
        var definition = ExecutiveCockpitKpiMetricCatalog.Find("open_tasks") with
        {
            WarningThreshold = null,
            CriticalThreshold = null,
            WarningDeviationPercentage = 40,
            CriticalDeviationPercentage = 80
        };
        var metric = Metric("open_tasks", current, baseline, definition);

        var anomaly = ExecutiveCockpitKpiCalculator.DetectAnomaly(metric);

        Assert.Equal(expectedSeverity, anomaly?.Severity);
    }

    private static ExecutiveCockpitKpiMetricValue Metric(
        string key,
        decimal current,
        decimal? baseline,
        ExecutiveCockpitKpiMetricDefinition definition) =>
        new(
            key,
            definition.Label,
            "Operations",
            current,
            baseline,
            new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc),
            "/tasks",
            definition);
}