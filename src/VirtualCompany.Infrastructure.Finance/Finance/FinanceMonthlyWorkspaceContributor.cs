using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceMonthlyWorkspaceContributor(
    IFinanceReadService finance,
    IFinanceSummaryQueryService summary) : IMonthlyWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Finance;

    public async Task<MonthlyWorkspaceFeatureContribution> ContributeAsync(
        MonthlyWorkspaceContributorContext context,
        CancellationToken cancellationToken)
    {
        var period = context.Period;
        var current = await finance.GetMonthlyProfitAndLossAsync(
            new(context.CompanyId, period.Year, period.Month), cancellationToken);
        var comparisonLocal = new DateTime(period.Year, period.Month, 1).AddMonths(-1);
        var comparison = await finance.GetMonthlyProfitAndLossAsync(
            new(context.CompanyId, comparisonLocal.Year, comparisonLocal.Month), cancellationToken);
        var cash = await finance.GetCashPositionAsync(
            new(context.CompanyId, period.EndUtc.AddTicks(-1)), cancellationToken);
        var balances = await summary.GetAsync(
            new(context.CompanyId, period.EndUtc.AddTicks(-1)), cancellationToken);
        var route = $"/finance/monthly-summary?companyId={context.CompanyId:D}&year={period.Year}&month={period.Month}";

        var results = new List<MonthlyWorkspaceMetricDto>
        {
            Metric("finance.revenue", "Revenue", current.Revenue, comparison.Revenue, current.Currency,
                current.EndUtc, "finance_monthly_profit_and_loss", route),
            Metric("finance.net_result", "Net result", current.NetResult, comparison.NetResult, current.Currency,
                current.EndUtc, "finance_monthly_profit_and_loss", route)
        };

        var priorities = new List<MonthlyWorkspacePriorityCandidate>();
        if (balances.OverdueReceivables > 0)
        {
            priorities.Add(Priority(
                "finance.overdue_receivables", "finance:overdue-receivables",
                $"{balances.OverdueReceivables:0.##} {balances.Currency} remains overdue",
                "Unresolved receivables can constrain next month's cash position.",
                "Review collection work and confirm the next customer follow-up.",
                balances.OverdueReceivables, "/finance/invoices?companyId=" + context.CompanyId.ToString("D"),
                context, true));
        }
        if (balances.OverduePayables > 0)
        {
            priorities.Add(Priority(
                "finance.overdue_payables", "finance:overdue-payables",
                $"{balances.OverduePayables:0.##} {balances.Currency} remains overdue",
                "Overdue supplier obligations need an explicit payment decision.",
                "Review supplier bills and the approval state before scheduling payment.",
                balances.OverduePayables, "/finance/bills?companyId=" + context.CompanyId.ToString("D"),
                context, true));
        }

        var facts = new List<MonthlyWorkspaceFactDto>
        {
            new("Revenue", Money(current.Revenue, current.Currency), DeltaStatus(current.Revenue, comparison.Revenue)),
            new("Expenses", Money(current.Expenses, current.Currency), DeltaStatus(comparison.Expenses, current.Expenses)),
            new("Net result", Money(current.NetResult, current.Currency), current.NetResult >= 0 ? "positive" : "attention"),
            new("Cash", Money(cash.AvailableBalance, cash.Currency), cash.RiskLevel),
            new("Runway", cash.EstimatedRunwayDays.HasValue ? $"{cash.EstimatedRunwayDays} days" : "Unavailable",
                cash.EstimatedRunwayDays.HasValue ? cash.RiskLevel : "unavailable"),
            new("Receivables", Money(balances.AccountsReceivable, balances.Currency), balances.OverdueReceivables > 0 ? "attention" : "current"),
            new("Payables", Money(balances.AccountsPayable, balances.Currency), balances.OverduePayables > 0 ? "attention" : "current")
        };
        var section = new MonthlyWorkspaceSectionDto(
            Lens,
            "Finance",
            current.NetResult >= 0
                ? "The month closed with a positive operating result; cash obligations still need active review."
                : "The month closed with a negative operating result and needs a next-period response.",
            priorities.Count > 0 || current.NetResult < 0 ? "attention" : "healthy",
            current.EndUtc,
            facts,
            [],
            route,
            "Profit and loss, cash, receivables, and payables are available from Finance.");

        return new MonthlyWorkspaceFeatureContribution(
            Lens,
            section,
            priorities,
            results,
            [],
            [new("finance", "Finance", "current", current.EndUtc,
                "Monthly ledger results and period-end balance obligations are available.")]);
    }

    private static MonthlyWorkspacePriorityCandidate Priority(
        string key, string dedupe, string changed, string matters, string action,
        decimal impact, string route, MonthlyWorkspaceContributorContext context, bool unresolved) =>
        new(key, dedupe, TodayWorkspaceLenses.Finance, changed, matters,
            context.Access.ResponsiblePerson, context.Access.WorkingAgent, action,
            context.Period.EndUtc, "finance_summary", null, route,
            MaterialChange: Math.Abs(impact), UnresolvedRisk: unresolved,
            DirectlyOwned: context.Access.IsPrimary, Confidence: 1m);

    private static MonthlyWorkspaceMetricDto Metric(
        string key, string label, decimal value, decimal comparison, string currency,
        DateTime observed, string source, string route) =>
        new(key, label, value, Money(value, currency), comparison, Money(comparison, currency), currency,
            DeltaStatus(value, comparison), observed, source, route);

    private static string Money(decimal value, string currency) => $"{value:0.##} {currency}";
    private static string DeltaStatus(decimal value, decimal comparison) => value > comparison ? "positive" : value < comparison ? "attention" : "current";
}
