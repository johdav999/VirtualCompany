using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Support;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportTodayWorkspaceContributor(
    ISupportCaseService cases,
    ISupportAnalyticsService analytics, ISupportOperationalReportService? reports = null) : ITodayWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Customers;

    public async Task<TodayWorkspaceFeatureContribution> ContributeAsync(
        TodayWorkspaceContributorContext context,
        CancellationToken cancellationToken)
    {
        var dashboard = await analytics.GetDashboardAsync(context.CompanyId, cancellationToken);
        var openCases = await cases.ListCasesAsync(
            context.CompanyId,
            new SupportCaseListQuery(OpenOnly: true, SortBy: "attention", SortDirection: "desc", Take: 15),
            cancellationToken);
        var report = reports is null ? null : await reports.GetAsync(context.CompanyId, new("backlog"), cancellationToken);
        var operationalRows = report?.Cases.ToDictionary(x => x.Case.Id);
        if (report is not null)
            openCases = new(report.Cases.Select(x => x.Case).ToList(), report.Cases.Count,
                new(report.Cases.Count, report.AwaitingApproval, report.Escalated, report.AtRisk, report.Breached, dashboard.Summary.ResolvedToday));
        var route = $"/support?companyId={context.CompanyId:D}";
        var observed = report?.AsOfUtc ?? (openCases.Items.Count == 0 ? context.NowUtc : openCases.Items.Max(x => x.UpdatedUtc));

        var attention = openCases.Items
            .Where(item => item.IsSlaBreached || item.IsSlaRisk || item.IsVipRisk || item.IsChurnRisk ||
                           string.Equals(item.Status, "awaiting_approval", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(item.Status, "escalated", StringComparison.OrdinalIgnoreCase) ||
                           item.Status is "waiting_for_customer" or "waiting_internal")
            .ToList();

        var priorities = attention.Select(item => new TodayWorkspacePriorityCandidate(
            $"support-case:{item.Id:N}",
            $"support-case:{item.Id:N}",
            Lens,
            $"{item.CaseNumber}: {item.Subject}",
            ExplainImpact(item),
            item.AssignedUserId is not null && operationalRows is not null ? operationalRows[item.Id].Owner : context.Access.ResponsiblePerson,
            operationalRows is null ? context.Access.WorkingAgent : item.AssignedAgentId is not null ? operationalRows[item.Id].Owner : null,
            string.Equals(item.Status, "awaiting_approval", StringComparison.OrdinalIgnoreCase)
                ? "Review the pending customer decision."
                : "Open the case and confirm the next response.",
            item.UpdatedUtc,
            "support_case",
            item.Id.ToString("D"),
            $"/support/cases/{item.Id:D}?companyId={context.CompanyId:D}",
            DecisionRequired: string.Equals(item.Status, "awaiting_approval", StringComparison.OrdinalIgnoreCase),
            DueUtc: operationalRows is null ? item.ResolutionDueUtc ?? item.FirstResponseDueUtc : operationalRows[item.Id].NextDeadlineUtc,
            ProximityRank: item.IsSlaBreached ? 1000 : item.IsSlaRisk ? 800 : 0,
            Impact: item.IsVipRisk || item.IsChurnRisk ? 100 : 50,
            DirectlyOwned: context.Access.IsPrimary,
            Blocked: string.Equals(item.Status, "escalated", StringComparison.OrdinalIgnoreCase),
            SeverityRank: Priority(item.Priority),
            Confidence: 1m, SourceState: item.Status)).ToList();

        priorities.AddRange(dashboard.Insights.Take(3).Select((item, index) =>
            new TodayWorkspacePriorityCandidate(
                $"support-insight:{Slug(item.Category)}:{index}",
                $"support-insight:{Slug(item.Category)}:{Slug(item.Title)}",
                Lens,
                item.Title,
                item.Summary,
                context.Access.ResponsiblePerson,
                context.Access.WorkingAgent,
                item.SuggestedAction,
                observed,
                "support_root_cause",
                null,
                route,
                Impact: item.CaseCount,
                DirectlyOwned: context.Access.IsPrimary,
                SeverityRank: 35,
                Confidence: 0.8m)));

        var metrics = new TodayWorkspaceMetricDto[]
        {
            CountMetric("support.open_cases", "Open cases", openCases.Summary.Open, openCases.Summary.Open > 0 ? "active" : "clear", observed, route),
            CountMetric("support.sla_at_risk", "SLA at risk", openCases.Summary.SlaRisk, openCases.Summary.SlaRisk > 0 ? "attention" : "clear", observed, route),
            CountMetric("support.sla_breached", "SLA breached", openCases.Summary.SlaBreached, openCases.Summary.SlaBreached > 0 ? "critical" : "clear", observed, route),
            CountMetric("support.awaiting_approval", "Awaiting approval", openCases.Summary.AwaitingApproval, openCases.Summary.AwaitingApproval > 0 ? "decision" : "clear", observed, route)
        };

        var items = attention.Take(5).Select(item => new TodayWorkspaceFeatureItemDto(
            $"support-case:{item.Id:N}",
            $"{item.CaseNumber}: {item.Subject}",
            ExplainImpact(item) + (report is null ? "" : $" Owner: {report.Cases.First(x => x.Case.Id == item.Id).Owner}."),
            item.StatusLabel,
            item.UpdatedUtc,
            $"/support/cases/{item.Id:D}?companyId={context.CompanyId:D}")).ToList();

        return new TodayWorkspaceFeatureContribution(
            Lens,
            priorities,
            metrics,
            [],
            Support: new TodayWorkspaceSupportSectionDto(
                true,
                "Customer support data is current.",
                observed,
                openCases.Summary.Open,
                openCases.Summary.AwaitingApproval,
                openCases.Summary.Escalated,
                openCases.Summary.SlaRisk,
                openCases.Summary.SlaBreached,
                items,
                route, report?.Waiting ?? 0));
    }

    private static TodayWorkspaceMetricDto CountMetric(
        string key, string label, int value, string status, DateTime observed, string route) =>
        new(key, label, value, value.ToString(), "count", status, observed, "support_analytics", route);

    private static string ExplainImpact(SupportCaseListItem item)
    {
        var reasons = new List<string>();
        if (item.IsSlaBreached) reasons.Add("its service target is breached");
        else if (item.IsSlaRisk) reasons.Add("its service target is at risk");
        if (item.IsChurnRisk) reasons.Add("the customer may leave");
        if (item.IsVipRisk) reasons.Add("a priority customer is affected");
        return reasons.Count == 0
            ? "The customer is waiting for a response."
            : $"This matters because {string.Join(" and ", reasons)}.";
    }

    private static int Priority(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "urgent" => 100,
        "high" => 75,
        "normal" => 50,
        _ => 20
    };

    private static string Slug(string value) => string.Join('-', value.ToLowerInvariant()
        .Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries));
}
