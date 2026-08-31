using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinancialReportSuiteTelemetry
{
    public const string MeterName = "VirtualCompany.Finance.FinancialReports";
    private readonly ILogger<FinancialReportSuiteTelemetry> _logger;
    private readonly Counter<long> _generations;
    private readonly Counter<long> _snapshots;
    private readonly Counter<long> _blockers;
    private readonly Histogram<long> _durationMilliseconds;
    private readonly Histogram<long> _outputLines;

    public FinancialReportSuiteTelemetry(IMeterFactory meterFactory, ILogger<FinancialReportSuiteTelemetry> logger)
    {
        _logger = logger;
        var meter = meterFactory.Create(MeterName);
        _generations = meter.CreateCounter<long>("financial_report.generations");
        _snapshots = meter.CreateCounter<long>("financial_report.snapshots");
        _blockers = meter.CreateCounter<long>("financial_report.blockers");
        _durationMilliseconds = meter.CreateHistogram<long>("financial_report.duration_ms");
        _outputLines = meter.CreateHistogram<long>("financial_report.output_lines");
    }

    public void Generated(CompleteFinancialReportDto report)
    {
        var tags = new TagList
        {
            { "report_kind", report.ReportKind },
            { "used_snapshot", report.UsedSnapshot },
            { "within_budget", report.ObservedDurationMilliseconds <= report.ReproducibilityBudgetMilliseconds }
        };
        _generations.Add(1, tags);
        _durationMilliseconds.Record(report.ObservedDurationMilliseconds, tags);
        _outputLines.Record(report.TotalLineCount, tags);
        if (report.Blockers.Count > 0)
            _blockers.Add(report.Blockers.Count, new KeyValuePair<string, object?>("report_kind", report.ReportKind));
        _logger.LogInformation(
            "Financial report {ReportKind} generated; Snapshot={UsedSnapshot}; OutputLines={OutputLineCount}; DurationMs={DurationMs}; BudgetMs={BudgetMs}; Blockers={BlockerCount}.",
            report.ReportKind, report.UsedSnapshot, report.TotalLineCount, report.ObservedDurationMilliseconds,
            report.ReproducibilityBudgetMilliseconds, report.Blockers.Count);
    }

    public void SnapshotCaptured(string reportKind, bool replayed)
    {
        _snapshots.Add(1, new("report_kind", reportKind), new("replayed", replayed));
        _logger.LogInformation("Financial report snapshot captured; ReportKind={ReportKind}; Replayed={Replayed}.",
            reportKind, replayed);
    }
}
