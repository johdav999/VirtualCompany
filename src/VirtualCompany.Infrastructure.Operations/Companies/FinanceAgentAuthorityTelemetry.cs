using System.Diagnostics.Metrics;
using VirtualCompany.Application.Agents;

namespace VirtualCompany.Infrastructure.Companies;

public static class FinanceAgentAuthorityTelemetry
{
    public const string MeterName = "VirtualCompany.Finance.AgentAuthority";
    public const string AuthorizationMetricName = "finance.agent.authority.decisions";
    public const string ApprovalMetricName = "finance.agent.authority.approval_decisions";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> AuthorizationDecisions = Meter.CreateCounter<long>(AuthorizationMetricName);
    private static readonly Counter<long> ApprovalDecisions = Meter.CreateCounter<long>(ApprovalMetricName);

    public static void RecordAuthorization(FinanceAgentAuthorizationDecisionDto decision) =>
        AuthorizationDecisions.Add(1,
            new KeyValuePair<string, object?>("tool.name", Normalize(decision.ToolName)),
            new KeyValuePair<string, object?>("action.type", Normalize(decision.ActionType)),
            new KeyValuePair<string, object?>("decision.outcome", Normalize(decision.Outcome)),
            new KeyValuePair<string, object?>("reason.code", Normalize(decision.ReasonCode)));

    public static void RecordApproval(string toolName, string outcome, string reasonCode) =>
        ApprovalDecisions.Add(1,
            new KeyValuePair<string, object?>("tool.name", Normalize(toolName)),
            new KeyValuePair<string, object?>("decision.outcome", Normalize(outcome)),
            new KeyValuePair<string, object?>("reason.code", Normalize(reasonCode)));

    private static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim().ToLowerInvariant();
}
