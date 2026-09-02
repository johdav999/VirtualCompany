using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesTodayWorkspaceContributor(ISalesOperationsService sales) : ITodayWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Sales;

    public async Task<TodayWorkspaceFeatureContribution> ContributeAsync(
        TodayWorkspaceContributorContext context,
        CancellationToken cancellationToken)
    {
        var dashboard = await sales.GetDashboardAsync(context.CompanyId, cancellationToken);
        var route = $"/app/sales?companyId={context.CompanyId:D}";
        var pipelineRoute = $"/app/sales/pipeline?companyId={context.CompanyId:D}";

        var priorities = dashboard.DealsRequiringAction.Select(deal =>
            new TodayWorkspacePriorityCandidate(
                $"sales-deal:{deal.Id:N}",
                $"deal:{deal.Id:N}",
                Lens,
                $"{deal.Title} needs attention",
                $"This {deal.StageName} opportunity is worth {deal.Amount:0.##} {deal.Currency}.",
                context.Access.ResponsiblePerson,
                context.Access.WorkingAgent,
                "Review the opportunity and confirm its next step.",
                deal.UpdatedUtc,
                "sales_deal",
                deal.Id.ToString("D"),
                $"/app/sales/deals/{deal.Id:D}?companyId={context.CompanyId:D}",
                DueUtc: deal.ExpectedCloseUtc,
                Impact: Math.Abs(deal.Amount),
                DirectlyOwned: context.Access.IsPrimary,
                SeverityRank: 60,
                Confidence: 1m)).ToList();

        priorities.AddRange(dashboard.AgentRecommendations
            .Where(item => !string.Equals(item.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(item.Status, "dismissed", StringComparison.OrdinalIgnoreCase))
            .Select(item =>
            {
                var sourceType = item.DealId.HasValue ? "deal" : item.LeadId.HasValue ? "lead" : "sales_recommendation";
                var sourceId = item.DealId ?? item.LeadId ?? item.Id;
                var detail = item.DealId.HasValue
                    ? $"/app/sales/deals/{item.DealId:D}?companyId={context.CompanyId:D}"
                    : item.LeadId.HasValue
                        ? $"/app/sales/prospects?companyId={context.CompanyId:D}&view=leads&leadId={item.LeadId:D}"
                        : route;
                return new TodayWorkspacePriorityCandidate(
                    $"sales-recommendation:{item.Id:N}",
                    $"{sourceType}:{sourceId:N}",
                    Lens,
                    item.Recommendation,
                    item.Rationale,
                    context.Access.ResponsiblePerson,
                    context.Access.WorkingAgent,
                    item.RequiresApproval ? "Review and decide whether to approve this recommendation." : "Review the recommended sales action.",
                    item.CreatedUtc,
                    "sales_recommendation",
                    item.Id.ToString("D"),
                    detail,
                    DecisionRequired: item.RequiresApproval && !string.Equals(item.ApprovalStatus, "approved", StringComparison.OrdinalIgnoreCase),
                    Impact: Risk(item.RiskLevel),
                    DirectlyOwned: context.Access.IsPrimary,
                    Blocked: !string.IsNullOrWhiteSpace(item.FailureSummary),
                    SeverityRank: Risk(item.RiskLevel),
                    Confidence: 0.8m);
            }));

        var metrics = new TodayWorkspaceMetricDto[]
        {
            new("sales.pipeline_value", "Pipeline value", dashboard.PipelineValue,
                $"{dashboard.PipelineValue:0.##} {dashboard.Currency}", dashboard.Currency, "current", context.NowUtc,
                "sales_dashboard", pipelineRoute),
            new("sales.hot_leads", "Hot leads", dashboard.HotLeads, dashboard.HotLeads.ToString(), "count",
                dashboard.HotLeads > 0 ? "opportunity" : "clear", context.NowUtc, "sales_dashboard",
                $"/app/sales/prospects?companyId={context.CompanyId:D}"),
            new("sales.deals_needing_attention", "Deals needing attention", dashboard.DealsNeedingAttention,
                dashboard.DealsNeedingAttention.ToString(), "count", dashboard.DealsNeedingAttention > 0 ? "attention" : "clear",
                context.NowUtc, "sales_dashboard", pipelineRoute),
            new("sales.forecast_revenue", "Forecast revenue", dashboard.ForecastRevenue,
                $"{dashboard.ForecastRevenue:0.##} {dashboard.Currency}", dashboard.Currency, "forecast", context.NowUtc,
                "sales_dashboard", pipelineRoute)
        };

        var items = dashboard.DealsRequiringAction.Take(5).Select(deal => new TodayWorkspaceFeatureItemDto(
            $"deal:{deal.Id:N}", deal.Title, $"{deal.StageName} · {deal.Amount:0.##} {deal.Currency}", deal.Status,
            deal.UpdatedUtc, $"/app/sales/deals/{deal.Id:D}?companyId={context.CompanyId:D}")).ToList();
        var updates = dashboard.RecentActivity.Take(5).Select(item => new TodayWorkspaceAgentUpdateDto(
            $"sales-activity:{item.Id:N}", "Sales activity", item.Summary, context.Access.WorkingAgent,
            item.OccurredUtc, "sales_activity", item.DealId.HasValue
                ? $"/app/sales/deals/{item.DealId:D}?companyId={context.CompanyId:D}"
                : route,
            "Sales agent",
            TodayAgentStates.Completed,
            RationaleSummary: "This update is backed by persisted sales activity.",
            VisibilityReason: context.Access.IsPrimary
                ? "Shown because you own the Sales responsibility."
                : "Shown because you have executive oversight of Sales.",
            UpdatedUtc: item.OccurredUtc)).ToList();

        return new TodayWorkspaceFeatureContribution(
            Lens,
            priorities,
            metrics,
            updates,
            Sales: new TodayWorkspaceSalesSectionDto(
                true,
                "Sales pipeline data is current.",
                context.NowUtc,
                dashboard.PipelineValue,
                dashboard.Currency,
                dashboard.NewLeads,
                dashboard.HotLeads,
                dashboard.DealsNeedingAttention,
                dashboard.ForecastRevenue,
                items,
                route));
    }

    private static int Risk(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "critical" => 100,
        "high" => 75,
        "medium" => 50,
        _ => 20
    };
}
