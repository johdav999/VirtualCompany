using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMonthlyWorkspaceContributor(VirtualCompanyDbContext db) : IMonthlyWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Sales;

    public async Task<MonthlyWorkspaceFeatureContribution> ContributeAsync(
        MonthlyWorkspaceContributorContext context,
        CancellationToken cancellationToken)
    {
        var period = context.Period;
        var activities = await db.SalesActivities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId && x.OccurredUtc >= period.StartUtc && x.OccurredUtc < period.EndUtc)
            .OrderByDescending(x => x.OccurredUtc).ToListAsync(cancellationToken);
        var previousActivities = await db.SalesActivities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId && x.OccurredUtc >= period.ComparisonStartUtc && x.OccurredUtc < period.ComparisonEndUtc)
            .ToListAsync(cancellationToken);
        var openDeals = await db.Deals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId && !x.IsDeleted && x.Status == SalesStatuses.Open)
            .OrderBy(x => x.ExpectedCloseUtc).ToListAsync(cancellationToken);
        var latestForecast = await db.RevenueForecastSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId && x.AsOfUtc < period.EndUtc)
            .OrderByDescending(x => x.AsOfUtc).FirstOrDefaultAsync(cancellationToken);
        var comparisonForecast = await db.RevenueForecastSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId && x.AsOfUtc < period.StartUtc)
            .OrderByDescending(x => x.AsOfUtc).FirstOrDefaultAsync(cancellationToken);
        var route = $"/app/sales/pipeline?companyId={context.CompanyId:D}";
        var stageMoves = activities.Count(x => x.ActivityType == "stage change");
        var previousStageMoves = previousActivities.Count(x => x.ActivityType == "stage change");
        var conversions = activities.Count(x => x.ActivityType == "conversion");
        var previousConversions = previousActivities.Count(x => x.ActivityType == "conversion");
        var dealsAtRisk = latestForecast?.HighRiskDeals ?? 0;

        var results = new List<MonthlyWorkspaceMetricDto>
        {
            new("sales.stage_movement", "Pipeline movement", stageMoves, stageMoves.ToString(), previousStageMoves,
                previousStageMoves.ToString(), "stage changes", stageMoves >= previousStageMoves ? "positive" : "attention",
                activities.FirstOrDefault()?.OccurredUtc ?? period.EndUtc, "sales_activity", route)
        };
        if (latestForecast is not null)
        {
            results.Add(new("sales.forecast", "30-day forecast", latestForecast.ExpectedRevenue30Days,
                $"{latestForecast.ExpectedRevenue30Days:0.##} {latestForecast.Currency}",
                comparisonForecast?.ExpectedRevenue30Days,
                comparisonForecast is null ? "No comparison" : $"{comparisonForecast.ExpectedRevenue30Days:0.##} {comparisonForecast.Currency}",
                latestForecast.Currency, dealsAtRisk > 0 ? "attention" : "current", latestForecast.AsOfUtc,
                "revenue_forecast_snapshot", route));
        }

        var priorities = new List<MonthlyWorkspacePriorityCandidate>();
        if (dealsAtRisk > 0)
        {
            priorities.Add(new("sales.high_risk_deals", "sales:high-risk-deals", Lens,
                $"{dealsAtRisk} forecast deal{(dealsAtRisk == 1 ? " is" : "s are")} high risk",
                "Unresolved forecast risk can carry into next month's revenue plan.", context.Access.ResponsiblePerson,
                context.Access.WorkingAgent, "Review the risky deals and commit each next follow-up.",
                latestForecast!.AsOfUtc, "revenue_forecast_snapshot", latestForecast.Id.ToString("D"), route,
                MaterialChange: dealsAtRisk, UnresolvedRisk: true, SustainedTrend: true,
                DirectlyOwned: context.Access.IsPrimary, Confidence: 1m));
        }
        foreach (var deal in openDeals.Where(x => x.ExpectedCloseUtc < period.EndUtc).Take(3))
        {
            priorities.Add(new($"sales.overdue_close:{deal.Id:N}", $"deal:{deal.Id:N}", Lens,
                $"{deal.Title} did not close in the reporting month",
                $"The {deal.Amount:0.##} {deal.Currency} opportunity still needs a forecast decision.",
                context.Access.ResponsiblePerson, context.Access.WorkingAgent,
                "Update the stage, close date, and next customer action.", deal.UpdatedUtc, "sales_deal", deal.Id.ToString("D"),
                $"/app/sales/deals/{deal.Id:D}?companyId={context.CompanyId:D}", DecisionRequired: true,
                DueUtc: deal.ExpectedCloseUtc, MaterialChange: deal.Amount, UnresolvedRisk: true,
                DirectlyOwned: context.Access.IsPrimary, Confidence: 1m));
        }

        var items = activities.Take(5).Select(x => new TodayWorkspaceFeatureItemDto(
            $"sales-activity:{x.Id:N}", x.Summary, x.ActivityType, x.Status, x.OccurredUtc,
            x.DealId.HasValue ? $"/app/sales/deals/{x.DealId:D}?companyId={context.CompanyId:D}" : route)).ToList();
        var section = new MonthlyWorkspaceSectionDto(Lens, "Sales",
            stageMoves > 0 ? "The pipeline moved during the month; next-period follow-up should focus on unresolved risk."
                : "No recorded stage movement was available for this reporting month.",
            dealsAtRisk > 0 ? "attention" : "current", activities.FirstOrDefault()?.OccurredUtc ?? period.EndUtc,
            [new("Current pipeline", $"{openDeals.Sum(x => x.Amount):0.##} {openDeals.FirstOrDefault()?.Currency ?? latestForecast?.Currency ?? string.Empty}".Trim(), "current"),
             new("Stage changes", stageMoves.ToString(), stageMoves >= previousStageMoves ? "positive" : "attention"),
             new("Lead conversions", conversions.ToString(), conversions >= previousConversions ? "positive" : "attention"),
             new("High-risk forecast deals", dealsAtRisk.ToString(), dealsAtRisk > 0 ? "attention" : "current")],
            items, route, "Sales activity and persisted forecast snapshots are available.");
        var outcomes = activities.Take(5).Select(x => new TodayWorkspaceAgentUpdateDto(
            $"sales-month:{x.Id:N}", "Sales outcome", x.Summary, context.Access.WorkingAgent,
            x.OccurredUtc, "sales_activity", route, "Sales agent", TodayAgentStates.Completed,
            RationaleSummary: "Backed by a persisted sales activity in this reporting month.", UpdatedUtc: x.UpdatedUtc)).ToList();

        return new(Lens, section, priorities, results, outcomes,
            [new("sales", "Sales", "current", activities.FirstOrDefault()?.OccurredUtc ?? latestForecast?.AsOfUtc,
                "Period activity and forecast snapshots are available.")]);
    }
}
