using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingCloseTelemetry(ILogger<AccountingCloseTelemetry> logger)
{
    internal const string MeterName = "VirtualCompany.AccountingClose";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> TemplateActions = Meter.CreateCounter<long>("accounting_close.template_actions");
    private static readonly Counter<long> CloseActions = Meter.CreateCounter<long>("accounting_close.instance_actions");
    private static readonly Counter<long> TaskActions = Meter.CreateCounter<long>("accounting_close.task_actions");
    private static readonly Histogram<long> GeneratedTasks = Meter.CreateHistogram<long>("accounting_close.generated_tasks");
    private static readonly Counter<long> GovernanceActions = Meter.CreateCounter<long>("accounting_close.governance_actions");

    public void Template(string action, string outcome)
    {
        TemplateActions.Add(1, new("action", action), new("outcome", outcome));
        logger.LogInformation("Accounting close template action {Action} completed with {Outcome}.", action, outcome);
    }

    public void Close(string action, string outcome, int taskCount)
    {
        CloseActions.Add(1, new("action", action), new("outcome", outcome));
        if (action == "started") GeneratedTasks.Record(taskCount);
        logger.LogInformation("Accounting close action {Action} completed with {Outcome}; TaskCount={TaskCount}.",
            action, outcome, taskCount);
    }

    public void Task(string action, string outcome, string? reasonCode)
    {
        TaskActions.Add(1, new("action", action), new("outcome", outcome), new("reason_code", reasonCode));
        logger.LogInformation("Accounting close task action {Action} completed with {Outcome}; ReasonCode={ReasonCode}.",
            action, outcome, reasonCode);
    }

    public void Governance(string action, string outcome, string? reasonCode)
    {
        GovernanceActions.Add(1, new("action", action), new("outcome", outcome), new("reason_code", reasonCode));
        logger.LogInformation("Accounting close governance action {Action} completed with {Outcome}; ReasonCode={ReasonCode}.",
            action, outcome, reasonCode);
    }
}
