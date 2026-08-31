using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class ReportDefinitionTelemetry
{
    public const string MeterName = "VirtualCompany.Finance.ReportDefinitions";
    private readonly Counter<long> _actions;
    private readonly Counter<long> _validationIssues;
    private readonly ILogger<ReportDefinitionTelemetry> _logger;

    public ReportDefinitionTelemetry(IMeterFactory meterFactory, ILogger<ReportDefinitionTelemetry> logger)
    {
        _logger = logger;
        var meter = meterFactory.Create(MeterName);
        _actions = meter.CreateCounter<long>("report_definition.actions");
        _validationIssues = meter.CreateCounter<long>("report_definition.validation_issues");
    }

    public void Action(string action, string outcome, string reportKind, int issueCount = 0)
    {
        _actions.Add(1, new("action", action), new("outcome", outcome), new("report_kind", reportKind));
        if (issueCount > 0)
            _validationIssues.Add(issueCount, new KeyValuePair<string, object?>("report_kind", reportKind));
        _logger.LogInformation("Report definition action {Action} completed with {Outcome}; ReportKind={ReportKind}; Issues={IssueCount}.",
            action, outcome, reportKind, issueCount);
    }
}
