using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class MonthlyManagementReviewProjector(VirtualCompanyDbContext db)
{
    public const string CalculationVersion = "monthly-management.v1";
    public async Task<MonthlyReviewDto> ProjectAsync(Guid companyId, MonthlyWorkspacePeriodDto period,
        IReadOnlyList<MonthlyWorkspaceMetricDto> results, IReadOnlyList<MonthlyWorkspaceSourceCoverageDto> coverage,
        CancellationToken ct)
    {
        // Company goals are member-readable through their existing owner. Never infer a monthly target
        // from an annual goal, mismatched currency/unit, baseline budget or a forecast.
        var targets = await db.CompanyGoals.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==companyId &&
            x.Status==CompanyGoalStatus.Active && x.StartUtc==period.StartUtc && x.TargetUtc==period.EndUtc &&
            x.TargetValue!=null).ToListAsync(ct);
        var measures=results.Select(x=>
        {
            var matching=targets.Where(t=>string.Equals(t.MetricKey,x.Key,StringComparison.Ordinal) &&
                string.Equals(t.MetricUnit,x.Unit,StringComparison.OrdinalIgnoreCase)).ToList();
            var target=matching.Count==1 ? matching[0] : null;
            var definition=x.Key switch
            {
                "finance.revenue"=>"Revenue returned by Finance monthly profit and loss; ledger accounting basis, company currency.",
                "finance.net_result"=>"Net result returned by Finance monthly profit and loss, revenue less expenses.",
                "sales.stage_movement"=>"Count of retained Sales activities labeled stage change in the company-local month; events, not distinct deals or pipeline value.",
                "sales.forecast"=>"Latest persisted 30-day revenue forecast before month end; projected revenue, not actual revenue.",
                "support.volume"=>"Cases created in the company-local month, including subsequently resolved cases.",
                "support.sla"=>"First responses meeting their stored deadline divided by cases created in the month whose deadline is due by the observation cutoff or which already received a response. Overdue unanswered cases count as missed; future unanswered deadlines are excluded.",
                "company.completed_work"=>"Count of currently completed, authorized Work tasks with a recorded completion in the selected company-local month, bounded to 20 included tasks. Protected collaboration ancestry is excluded; task completion is not company outcome attribution. No complete historical comparison is reconstructed.",
                _=>"Authoritative governed channel observation aggregate returned by Marketing for the selected month; unavailable observations remain unavailable."
            };
            var kind=x.Key=="sales.forecast" ? "forecast" : "actual";
            var change=x.IsAvailable && x.Value.HasValue && x.ComparisonValue.HasValue ? x.Value-x.ComparisonValue : null;
            var variance=x.IsAvailable && x.Value.HasValue && target!=null ? x.Value-target.TargetValue : null;
            var availability=target!=null ? "Matching active monthly goal" : matching.Count>1
                ? "Multiple matching goals; no single target chosen" : "No matching monthly target";
            var explanation=!x.IsAvailable ? x.UnavailableReason ?? "Source unavailable."
                : change.HasValue ? $"Change from prior month: {change:0.##} {x.Unit}. Review the linked owning source for the underlying records."
                : "No comparable retained prior-month value is available. Review the linked owning source.";
            return new MonthlyReviewMeasureDto(x.Key,x.Label,x.IsAvailable ? x.Value : null,x.ComparisonValue,x.Unit,definition,kind,
                x.EvidenceSourceType,x.DeepLink,target?.TargetValue,target?.Id,target?.Version,availability,change,variance,explanation);
        }).ToList();
        var partial=coverage.Any(x=>x.State is "partial" or "unavailable" or "stale");
        return new(CalculationVersion,measures,partial ? "Coverage incomplete — review the source gaps before relying on a complete review."
            : "Available sources captured; source coverage does not establish accounting close approval.",
            "Accounting close is verified in the Finance close workspace; this management review is not a statutory close or signed financial statement.",
            "Current risks, pending decisions and obligations are observed at review time. Deleted/reopened historical balances are not reconstructed. Source links open current authorized records and may no longer be available.");
    }
}
