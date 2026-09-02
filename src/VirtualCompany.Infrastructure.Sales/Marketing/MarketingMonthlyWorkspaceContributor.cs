using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Marketing;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingMonthlyWorkspaceContributor(IMarketingOperationsService marketing) : IMonthlyWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Marketing;

    public async Task<MonthlyWorkspaceFeatureContribution> ContributeAsync(
        MonthlyWorkspaceContributorContext context,
        CancellationToken cancellationToken)
    {
        var period = context.Period;
        var current = await marketing.GetDashboardAsync(context.CompanyId, period.StartUtc, period.EndUtc, cancellationToken);
        var comparison = await marketing.GetDashboardAsync(context.CompanyId, period.ComparisonStartUtc, period.ComparisonEndUtc, cancellationToken);
        var route = $"/marketing?companyId={context.CompanyId:D}";
        var authoritative = current.Metrics
            .Where(x => x.Value.HasValue && !string.Equals(x.State, "unavailable", StringComparison.OrdinalIgnoreCase))
            .Where(x => x.Name is not "Campaigns" and not "Qualified handoffs")
            .ToList();
        var comparisonByName = comparison.Metrics.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var results = authoritative.Take(2).Select((metric, index) =>
        {
            comparisonByName.TryGetValue(metric.Name, out var previous);
            return new MonthlyWorkspaceMetricDto(
                $"marketing.{Slug(metric.Name)}.{index}", metric.Name, metric.Value,
                $"{metric.Value:0.##} {metric.Unit}".Trim(), previous?.Value,
                previous?.Value is null ? "No comparison" : $"{previous.Value:0.##} {previous.Unit}".Trim(),
                metric.Unit, metric.State, current.GeneratedUtc, "marketing_channel_observation", route);
        }).ToList();

        var unavailable = authoritative.Count == 0;
        if (unavailable)
        {
            results.Add(new("marketing.outcomes", "Marketing outcomes", null, "Unavailable", null,
                "No comparison", null, "unavailable", current.GeneratedUtc, "marketing_channel_observation", route,
                false, "No authoritative channel observations exist for this month."));
        }

        var dueItems = current.Calendar.Where(x => x.StartsUtc >= period.StartUtc && x.StartsUtc < period.EndUtc)
            .OrderByDescending(x => x.StartsUtc).Take(5)
            .Select(x => new TodayWorkspaceFeatureItemDto(
                $"marketing:{x.Id:N}", x.Name, x.Kind, x.AttentionState, x.StartsUtc,
                AddCompany(x.NavigationTarget, context.CompanyId, route))).ToList();
        var priorities = current.Handoffs
            .Where(x => !string.Equals(x.Status, "accepted", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(x.Status, "rejected", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(x => new MonthlyWorkspacePriorityCandidate(
                $"marketing-handoff:{x.Id:N}", $"marketing-handoff:{x.Id:N}", Lens, x.Reason,
                "An unresolved Marketing-to-Sales handoff can weaken next month's pipeline plan.",
                context.Access.ResponsiblePerson, context.Access.WorkingAgent, x.SuggestedAction,
                x.UpdatedUtc, "marketing_sales_handoff", x.Id.ToString("D"), route,
                DecisionRequired: true, DueUtc: x.ExpiresUtc, MaterialChange: Urgency(x.Urgency),
                UnresolvedRisk: true, DirectlyOwned: context.Access.IsPrimary, Confidence: 1m)).ToList();
        if (unavailable)
        {
            priorities.Add(new("marketing.setup_outcomes", "marketing:setup-outcomes", Lens,
                "Marketing outcome data is unavailable for the month",
                "Without governed channel observations, campaign outcomes cannot be evaluated honestly.",
                context.Access.ResponsiblePerson, context.Access.WorkingAgent,
                "Connect a data source or record an authoritative monthly observation.", current.GeneratedUtc,
                "marketing_channel_observation", null, route, MaterialChange: 1, UnresolvedRisk: true,
                DirectlyOwned: context.Access.IsPrimary, Confidence: 1m));
        }

        var section = new MonthlyWorkspaceSectionDto(Lens, "Marketing",
            unavailable ? "Campaign activity is visible, but authoritative monthly outcomes are unavailable."
                : "Authoritative campaign observations are available for the reporting month.",
            unavailable ? "unavailable" : priorities.Count > 0 ? "attention" : "current",
            current.GeneratedUtc,
            unavailable
                ? [new("Monthly outcomes", "Unavailable", "unavailable"), new("Scheduled activity", dueItems.Count.ToString(), "current")]
                : authoritative.Take(4).Select(x => new MonthlyWorkspaceFactDto(x.Name, $"{x.Value:0.##} {x.Unit}".Trim(), x.State)).ToList(),
            dueItems, route,
            unavailable ? "Activity is available; outcome coverage is missing." : "Authoritative channel observations are available.",
            true, unavailable ? route : null);
        return new(Lens, section, priorities, results, [],
            [new("marketing", "Marketing", unavailable ? "unavailable" : "current", current.GeneratedUtc,
                unavailable ? "No authoritative monthly outcome observations were found." : "Monthly channel observations are available.",
                unavailable ? route : null)]);
    }

    private static int Urgency(string? value) => value?.Trim().ToLowerInvariant() switch
    { "critical" or "urgent" => 100, "high" => 75, "medium" => 50, _ => 20 };
    private static string Slug(string value) => string.Join('-', value.ToLowerInvariant().Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries));
    private static string AddCompany(string? route, Guid companyId, string fallback) => string.IsNullOrWhiteSpace(route)
        ? fallback : route.Contains("companyId=", StringComparison.OrdinalIgnoreCase)
            ? route : $"{route}{(route.Contains('?') ? '&' : '?')}companyId={companyId:D}";
}
