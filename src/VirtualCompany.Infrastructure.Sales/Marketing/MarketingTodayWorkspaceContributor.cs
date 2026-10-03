using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Marketing;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingTodayWorkspaceContributor(IMarketingOperationsService marketing, IMarketingOperationalReportService? reports = null,
    IMarketingDeliveryService? delivery = null) : ITodayWorkspaceContributor
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
                $"{route}&section=Handoffs#marketing-record-{item.Id:D}",
                DecisionRequired: true,
                DueUtc: item.ExpiresUtc,
                Impact: Urgency(item.Urgency),
                DirectlyOwned: context.Access.IsPrimary,
                SeverityRank: Urgency(item.Urgency),
                Confidence: 1m, SourceState: item.Status))
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
                item.UpdatedUtc ?? DateTime.MinValue,
                "marketing_content_brief",
                item.Id.ToString("D"),
                $"/marketing/review?companyId={context.CompanyId:D}&briefId={item.Id:D}&campaignId={item.CampaignId}",
                DecisionRequired: string.Equals(item.Status, "submitted", StringComparison.OrdinalIgnoreCase),
                DueUtc: item.DueUtc,
                Impact: 50,
                DirectlyOwned: context.Access.IsPrimary,
                SeverityRank: 50,
                Confidence: 1m, SourceState: item.Status)));

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
                item.UpdatedUtc ?? DateTime.MinValue,
                "marketing_experiment",
                item.Id.ToString("D"),
                $"{route}&section=Experiments#marketing-record-{item.Id:D}",
                DecisionRequired: item.EndsUtc <= context.NowUtc,
                DueUtc: item.EndsUtc,
                Impact: item.MinimumSampleSize,
                DirectlyOwned: context.Access.IsPrimary,
                SeverityRank: 40,
                Confidence: 0.8m, SourceState: item.Status)));

        var operational = reports is null ? null : await reports.GetAsync(context.CompanyId,
            new(context.NowUtc.AddDays(-30), context.NowUtc.AddDays(3)), cancellationToken);
        var launches = operational?.Campaigns.Where(x => x.LaunchUtc <= context.NowUtc.AddDays(1) && x.State is "planning" or "scheduled" or "waiting_for_approval").ToArray() ?? [];
        var spendExceptions = operational?.Campaigns.Where(x => !x.Budget.HasValue || x.KnownSpend > x.Budget).ToArray() ?? [];
        var attributionGaps = operational?.Campaigns.Where(x => x.OutcomeSubjects == 0 || x.AttributedSubjects < x.OutcomeSubjects).ToArray() ?? [];
        foreach (var campaign in launches)
            priorities.Add(new($"marketing-launch:{campaign.Id:N}", $"marketing-campaign:{campaign.Id:N}", Lens,
                $"{campaign.Name}: launch review", "Inspect exact content, audience and launch authority before delivery.",
                context.Access.ResponsiblePerson, context.Access.WorkingAgent, "Review the campaign and its governed next step.", campaign.UpdatedUtc,
                "marketing_campaign", campaign.Id.ToString("D"), $"/marketing/review?companyId={context.CompanyId:D}&campaignId={campaign.Id:D}",
                DecisionRequired: true, DueUtc: campaign.LaunchUtc, Impact: 70, DirectlyOwned: context.Access.IsPrimary, SeverityRank: 70, Confidence: 1m, SourceState: campaign.State));
        foreach (var campaign in spendExceptions)
            priorities.Add(new($"marketing-spend:{campaign.Id:N}", $"marketing-spend:{campaign.Id:N}", Lens,
                $"{campaign.Name}: budget review", campaign.Budget.HasValue ? "Known cost exceeds the recorded campaign budget." : "No campaign budget is recorded; spend is not assumed to be zero.",
                context.Access.ResponsiblePerson, context.Access.WorkingAgent, "Review spend evidence and campaign allocation.", campaign.UpdatedUtc,
                "marketing_campaign", campaign.Id.ToString("D"), $"/marketing/reports/spend?companyId={context.CompanyId:D}&campaignId={campaign.Id:D}",
                Impact: 65, DirectlyOwned: context.Access.IsPrimary, SeverityRank: 65, Confidence: 1m, SourceState: "needs_review"));
        foreach (var campaign in attributionGaps)
            priorities.Add(new($"marketing-attribution:{campaign.Id:N}", $"marketing-attribution:{campaign.Id:N}", Lens,
                $"{campaign.Name}: attribution gap", "Available outcomes do not have complete attribution. Success is not inferred.",
                context.Access.ResponsiblePerson, context.Access.WorkingAgent, "Inspect lead results and coverage.", campaign.UpdatedUtc,
                "marketing_campaign", campaign.Id.ToString("D"), $"/marketing/reports/leads?companyId={context.CompanyId:D}&campaignId={campaign.Id:D}",
                Impact: 35, DirectlyOwned: context.Access.IsPrimary, SeverityRank: 35, Confidence: 1m, SourceState: "partial"));
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
        if (operational is not null)
        {
            var counts = new[] { ("Due launches", launches.Length), ("Content reviews", dashboard.Content.Count(x => x.Status == "submitted")),
                ("Spend exceptions", spendExceptions.Length), ("Attribution gaps", attributionGaps.Length) };
            metrics = counts.Select((x, index) => new TodayWorkspaceMetricDto($"marketing.daily.{index}", x.Item1, x.Item2,
                x.Item2.ToString(), "count", "current", operational.AsOfUtc, "marketing_operational", route)).ToList();
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
                item.CampaignId.HasValue ? $"/marketing/review?companyId={context.CompanyId:D}&campaignId={item.CampaignId:D}" : AddCompany(item.NavigationTarget, context.CompanyId, route)))
            .ToList();
        items.InsertRange(0, (operational?.Deliveries ?? []).Where(x => x.State is "failed" or "ambiguous" or "retry_scheduled").Select(x =>
            new TodayWorkspaceFeatureItemDto($"marketing-delivery:{x.Id:N}", "Delivery needs attention", "Inspect the recorded provider state before retry or reconciliation.",
                x.State, x.UpdatedUtc, $"/marketing?companyId={context.CompanyId:D}&section=Channels&actionId={x.Id:D}")));
        if (delivery is not null)
        {
            var assets = await delivery.ListCreativeAssetsAsync(context.CompanyId, cancellationToken);
            items.InsertRange(0, assets.Where(x => x.Status is "rejected" or "changes_requested").Select(x =>
                new TodayWorkspaceFeatureItemDto($"marketing-asset:{x.Id:N}", x.Name, "Creative revision is required before external use.",
                    x.Status, x.UpdatedUtc, $"/marketing/review?companyId={context.CompanyId:D}&briefId={x.BriefId:D}&assetId={x.Id:D}")));
        }

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
                dashboard.Content.Count(x => x.DueUtc.HasValue && x.DueUtc <= context.NowUtc.AddDays(3) && x.Status is not ("approved" or "published")),
                dashboard.Experiments.Count(x => !string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase)),
                items,
                route, launches.Length, spendExceptions.Length, attributionGaps.Length));
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
