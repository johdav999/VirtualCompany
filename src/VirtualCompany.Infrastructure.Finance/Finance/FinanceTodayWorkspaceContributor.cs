using VirtualCompany.Application.Cockpit;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceTodayWorkspaceContributor : ITodayWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Finance;

    public Task<TodayWorkspaceFeatureContribution> ContributeAsync(
        TodayWorkspaceContributorContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cockpit = context.ExecutiveCockpit
            ?? throw new InvalidOperationException("The Finance cockpit snapshot is unavailable.");
        var finance = cockpit.Finance;
        var cash = cockpit.CashPosition;
        if (finance is null && cash is null)
        {
            throw new InvalidOperationException("Finance has not been initialized for this company.");
        }

        var observed = finance?.CashPosition.LastRefreshedUtc ?? cash?.AsOfUtc ?? context.NowUtc;
        var currency = finance?.CashPosition.Currency ?? cash?.Currency;
        var balance = finance?.CashPosition.Amount ?? cash?.AvailableBalance;
        var runway = finance?.Runway.EstimatedRunwayDays ?? cash?.EstimatedRunwayDays;
        var health = finance?.FinancialHealth.Status ?? cash?.RiskLevel ?? "unknown";
        var insightCount = finance?.FinancialHealth.ActiveInsightCount ?? 0;
        var route = $"/finance?companyId={context.CompanyId:D}";

        var items = (finance?.InsightsFeed ?? [])
            .Take(5)
            .Select(item => new TodayWorkspaceFeatureItemDto(
                item.GroupKey,
                item.Title,
                item.Summary,
                item.Severity,
                item.LatestUpdatedUtc,
                AddCompany(item.Route, context.CompanyId, route)))
            .ToList();

        var priorities = (finance?.TopActions ?? [])
            .Select(item => new TodayWorkspacePriorityCandidate(
                $"finance:{item.GroupKey}",
                $"finance-insight:{item.GroupKey}",
                Lens,
                item.Title,
                string.IsNullOrWhiteSpace(item.Summary) ? item.EntitySummary : item.Summary,
                context.Access.ResponsiblePerson,
                context.Access.WorkingAgent,
                string.IsNullOrWhiteSpace(item.Recommendation) ? "Review the finance evidence." : item.Recommendation,
                item.LatestUpdatedUtc,
                "finance_insight",
                item.GroupKey,
                AddCompany(item.Route, context.CompanyId, route),
                Impact: Severity(item.Severity) * Math.Max(1, item.OccurrenceCount),
                DirectlyOwned: context.Access.IsPrimary,
                SeverityRank: Severity(item.Severity),
                Confidence: cash?.Confidence ?? 1m))
            .ToList();

        var metrics = new List<TodayWorkspaceMetricDto>();
        if (balance.HasValue)
        {
            metrics.Add(new(
                "finance.cash_balance",
                "Available cash",
                balance,
                finance?.CashPosition.DisplayValue ?? $"{balance:0.##} {currency}",
                currency,
                health,
                observed,
                "finance_cash_position",
                $"/finance/cash-position?companyId={context.CompanyId:D}"));
        }
        if (runway.HasValue)
        {
            metrics.Add(new(
                "finance.runway_days",
                "Runway",
                runway,
                $"{runway} days",
                "days",
                health,
                observed,
                "finance_cash_position",
                $"/finance/cash-position?companyId={context.CompanyId:D}"));
        }
        metrics.Add(new(
            "finance.open_insights",
            "Open finance insights",
            insightCount,
            insightCount.ToString(),
            "count",
            insightCount > 0 ? "attention" : "clear",
            observed,
            "finance_insight",
            $"/finance/issues?companyId={context.CompanyId:D}"));

        return Task.FromResult(new TodayWorkspaceFeatureContribution(
            Lens,
            priorities,
            metrics,
            [],
            Finance: new TodayWorkspaceFinanceSectionDto(
                true,
                "Finance data is current.",
                observed,
                balance,
                currency,
                runway,
                health,
                insightCount,
                items,
                route)));
    }

    private static int Severity(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "critical" => 100,
        "high" => 75,
        "medium" or "warning" => 50,
        _ => 20
    };

    private static string AddCompany(string? route, Guid companyId, string fallback)
    {
        if (string.IsNullOrWhiteSpace(route)) return fallback;
        return route.Contains("companyId=", StringComparison.OrdinalIgnoreCase)
            ? route
            : $"{route}{(route.Contains('?') ? '&' : '?')}companyId={companyId:D}";
    }
}
