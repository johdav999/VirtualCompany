using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using Microsoft.Extensions.Logging;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceTodayWorkspaceContributor(IFinanceReadService financeRead, ILogger<FinanceTodayWorkspaceContributor> logger,
    IFinanceOperationalReportService? operationalReports = null) : ITodayWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Finance;

    public async Task<TodayWorkspaceFeatureContribution> ContributeAsync(
        TodayWorkspaceContributorContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cockpit = context.ExecutiveCockpit
            ?? throw new InvalidOperationException("The Finance cockpit snapshot is unavailable.");
        var finance = cockpit.Finance;
        var cash = cockpit.CashPosition;
        if (finance is null && cash is null && operationalReports is null)
        {
            throw new InvalidOperationException("Finance has not been initialized for this company.");
        }

        var observed = finance?.CashPosition.LastRefreshedUtc ?? cash?.AsOfUtc ?? DateTime.MinValue;
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
                Confidence: cash?.Confidence ?? 1m, SourceState: item.Severity))
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

        var plan = await ReadPlanAsync(context, cancellationToken);
        FinanceOperationalReportDto? operational = null;
        var gaps = new List<string>();
        if (operationalReports is not null)
        {
            try { operational = await operationalReports.GetAsync(new(context.CompanyId, DateOnly.FromDateTime(context.NowUtc)), cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException and not UnauthorizedAccessException)
            {
                logger.LogWarning("Finance daily obligations unavailable for {CompanyId}: {ErrorType}", context.CompanyId, ex.GetType().Name);
                gaps.Add("Due obligations are unavailable. Open Finance reports to retry.");
            }
        }
        if (operational is not null)
        {
            gaps.AddRange(operational.CoverageGaps);
            // Use only currency-separated owning cash evidence; never display a mixed aggregate as one currency.
            var cashTotals = operational.Totals.Where(x => x.StartingCash.HasValue).ToArray();
            balance = cashTotals.Length == 1 ? cashTotals[0].StartingCash : null;
            currency = cashTotals.Length == 1 ? cashTotals[0].Currency : null;
            observed = operational.ObservedAtUtc;
            metrics.RemoveAll(x => x.Key == "finance.cash_balance");
            if (balance.HasValue) metrics.Add(new("finance.cash_balance", "Available cash", balance, $"{balance:0.##} {currency}", currency,
                health, observed, "finance_cash_position", $"/finance/cash-forecast?companyId={context.CompanyId:D}"));
            var due = operational.Receivables.Where(x => x.DaysOverdue > 0).Concat(operational.Payables.Where(x => DateOnly.FromDateTime(x.DueUtc) <= operational.ThroughDate))
                .OrderByDescending(x => x.DaysOverdue).ThenBy(x => x.DueUtc).Take(6);
            foreach (var item in due)
            {
                var title = $"{item.Number} · {item.Counterparty}";
                var summary = $"{item.RemainingAmount:0.00} {item.Currency} remaining; due {item.DueUtc:yyyy-MM-dd} UTC.";
                items.Add(new($"finance:{item.Kind}:{item.Id:D}", title, summary, item.DaysOverdue > 0 ? "high" : "medium", item.SourceUpdatedUtc, item.DeepLink));
                priorities.Add(new($"finance:{item.Kind}:{item.Id:D}", $"finance:{item.Kind}:{item.Id:D}", Lens, title, summary,
                    context.Access.ResponsiblePerson, context.Access.WorkingAgent, "Review the recorded obligation and permitted next action.", item.SourceUpdatedUtc,
                    $"finance_{item.Kind}", item.Id.ToString("D"), item.DeepLink, Impact: item.DaysOverdue > 0 ? 75 : 50,
                    DueUtc: item.DueUtc, DirectlyOwned: context.Access.IsPrimary, SeverityRank: item.DaysOverdue > 0 ? 75 : 50, SourceState: item.Status));
            }
            if (operational.ReconciliationExceptions > 0)
                items.Add(new("finance:reconciliation", "Bank reconciliation needs review", $"{operational.ReconciliationExceptions} transactions need reconciliation.", "high", operational.ObservedAtUtc,
                    $"/finance/accounting/reconciliation?companyId={context.CompanyId:D}"));
        }
        return new TodayWorkspaceFeatureContribution(
            Lens,
            priorities,
            metrics,
            [],
            Finance: new TodayWorkspaceFinanceSectionDto(
                operational is not null || finance is not null || cash is not null,
                gaps.Count > 0 ? "Finance has incomplete source coverage; review the available obligations." : "Review dated cash evidence and due obligations.",
                observed,
                balance,
                currency,
                runway,
                health,
                insightCount,
                items,
                route, plan, operational?.Receivables.Count(x => x.DaysOverdue > 0),
                operational?.Payables.Count(x => DateOnly.FromDateTime(x.DueUtc) <= operational.ThroughDate), operational?.ReconciliationExceptions, gaps, context.Access.WorkingAgent));
    }

    private async Task<TodayWorkspacePlanComparisonDto> ReadPlanAsync(TodayWorkspaceContributorContext context, CancellationToken token)
    {
        // Finance variance owns ledger actuals and comparison math. Never merge different budget versions.
        var start = new DateTime(context.NowUtc.Year, context.NowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = start.AddMonths(1).AddTicks(-1);
        try
        {
            var budgets = await financeRead.GetBudgetsAsync(new(context.CompanyId, start, end), token);
            var versions = budgets.Select(x => x.Version).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (versions.Count == 0) return new("no_baseline", start, end, null, [], null);
            var updated = budgets.Max(x => x.UpdatedUtc);
            if (versions.Count != 1) return new("selection_required", start, end, updated, versions, null);
            var comparison = await financeRead.GetVarianceAsync(new(context.CompanyId, start, FinanceVarianceComparisonTypes.ActualVsBudget, end, versions[0]), token);
            return new("recorded_unapproved", start, end, updated, versions, comparison);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not UnauthorizedAccessException)
        {
            logger.LogError("Today plan projection failed for company {CompanyId}; error type {ErrorType}.", context.CompanyId, ex.GetType().Name);
            return new("unavailable", start, end, null, [], null);
        }
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
