using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Marketing;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingTodayWorkspaceContributor(IMarketingOperationsService marketing) : ITodayWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Marketing;

    public async Task<TodayWorkspaceFeatureContribution> ContributeAsync(
        TodayWorkspaceContributorContext context,
        CancellationToken cancellationToken)
    {
        var dashboard = await marketing.GetDashboardAsync(
            context.CompanyId,
            context.NowUtc.Date.AddDays(-30),
            context.NowUtc.Date.AddDays(1),
            cancellationToken);
        var route = $"/marketing?companyId={context.CompanyId:D}";

        var priorities = dashboard.Handoffs
            .Where(x => !string.Equals(x.Status, "accepted", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(x.Status, "rejected", StringComparison.OrdinalIgnoreCase))
            .Select(item => new TodayWorkspacePriorityCandidate(
                $"marketing-handoff:{item.Id:N}",
                item.LinkedDealId.HasValue ? $"deal:{item.LinkedDealId:N}" : $"marketing-handoff:{item.Id:N}",
                Lens,
                item.Reason,
                $"A Marketing-to-Sales handoff is {item.Urgency} and expires {item.ExpiresUtc:yyyy-MM-dd HH:mm} UTC.",
                context.Access.ResponsiblePerson,
                context.Access.WorkingAgent,
                item.SuggestedAction,
                item.UpdatedUtc,
                "marketing_sales_handoff",
                item.Id.ToString("D"),
                route,
                DecisionRequired: true,
                DueUtc: item.ExpiresUtc,
                Impact: Urgency(item.Urgency),
                DirectlyOwned: context.Access.IsPrimary,
                SeverityRank: Urgency(item.Urgency),
                Confidence: 1m))
            .ToList();

        priorities.AddRange(dashboard.Content
            .Where(item => item.DueUtc.HasValue && item.DueUtc.Value <= context.NowUtc.AddDays(3) &&
                           !string.Equals(item.Status, "approved", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(item.Status, "published", StringComparison.OrdinalIgnoreCase))
            .Select(item => new TodayWorkspacePriorityCandidate(
                $"marketing-content:{item.Id:N}",
                $"marketing-content:{item.Id:N}",
                Lens,
                $"{item.Title} is due soon",
                $"The {item.Channel} content for {item.Audience} is not complete.",
                context.Access.ResponsiblePerson,
                context.Access.WorkingAgent,
                "Review the brief and complete its next approval or delivery step.",
                item.DueUtc ?? context.NowUtc,
                "marketing_content_brief",
                item.Id.ToString("D"),
                route,
                DecisionRequired: string.Equals(item.Status, "submitted", StringComparison.OrdinalIgnoreCase),
                DueUtc: item.DueUtc,
                Impact: 50,
                DirectlyOwned: context.Access.IsPrimary,
                SeverityRank: 50,
                Confidence: 1m)));

        priorities.AddRange(dashboard.Experiments
            .Where(item => item.EndsUtc <= context.NowUtc.AddDays(3) &&
                           !string.Equals(item.Status, "completed", StringComparison.OrdinalIgnoreCase))
            .Select(item => new TodayWorkspacePriorityCandidate(
                $"marketing-experiment:{item.Id:N}",
                $"marketing-experiment:{item.Id:N}",
                Lens,
                $"{item.Name} is ready for review",
                $"The {item.PrimaryMetric} experiment reaches its review date soon.",
                context.Access.ResponsiblePerson,
                context.Access.WorkingAgent,
                "Review the evidence and record the experiment decision.",
                item.EndsUtc,
                "marketing_experiment",
                item.Id.ToString("D"),
                route,
                DecisionRequired: item.EndsUtc <= context.NowUtc,
                DueUtc: item.EndsUtc,
                Impact: item.MinimumSampleSize,
                DirectlyOwned: context.Access.IsPrimary,
                SeverityRank: 40,
                Confidence: 0.8m)));

        var metrics = dashboard.Metrics.Take(4).Select((metric, index) => new TodayWorkspaceMetricDto(
            $"marketing.{Slug(metric.Name)}.{index}",
            metric.Name,
            metric.Value,
            metric.Value.HasValue ? $"{metric.Value:0.##} {metric.Unit}".Trim() : "Unavailable",
            metric.Unit,
            metric.State,
            dashboard.GeneratedUtc,
            "marketing_dashboard",
            route)).ToList();

        if (metrics.Count == 0)
        {
            metrics.Add(new(
                "marketing.active_objectives",
                "Active objectives",
                dashboard.Objectives.Count(x => string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase)),
                dashboard.Objectives.Count(x => string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase)).ToString(),
                "count",
                "current",
                dashboard.GeneratedUtc,
                "marketing_dashboard",
                route));
        }

        var items = dashboard.Calendar
            .Where(item => item.EndsUtc >= context.NowUtc.AddDays(-1))
            .OrderBy(item => item.StartsUtc)
            .Take(5)
            .Select(item => new TodayWorkspaceFeatureItemDto(
                $"marketing-calendar:{item.Id:N}",
                item.Name,
                $"{item.Kind} scheduled for {item.StartsUtc:yyyy-MM-dd HH:mm} UTC.",
                item.AttentionState,
                dashboard.GeneratedUtc,
                AddCompany(item.NavigationTarget, context.CompanyId, route)))
            .ToList();

        return new TodayWorkspaceFeatureContribution(
            Lens,
            priorities,
            metrics,
            [],
            Marketing: new TodayWorkspaceMarketingSectionDto(
                true,
                "Marketing plan and performance data is current.",
                dashboard.GeneratedUtc,
                dashboard.Objectives.Count(x => string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase)),
                dashboard.Plans.Count(x => string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase)),
                dashboard.Content.Count(x => x.DueUtc.HasValue && x.DueUtc <= context.NowUtc.AddDays(3)),
                dashboard.Experiments.Count(x => !string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase)),
                items,
                route));
    }

    private static int Urgency(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "critical" or "urgent" => 100,
        "high" => 75,
        "medium" => 50,
        _ => 20
    };

    private static string Slug(string value) => string.Join('_', value.ToLowerInvariant()
        .Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries));

    private static string AddCompany(string? detail, Guid companyId, string fallback)
    {
        if (string.IsNullOrWhiteSpace(detail)) return fallback;
        return detail.Contains("companyId=", StringComparison.OrdinalIgnoreCase)
            ? detail
            : $"{detail}{(detail.Contains('?') ? '&' : '?')}companyId={companyId:D}";
    }
}
