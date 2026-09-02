using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportMonthlyWorkspaceContributor(VirtualCompanyDbContext db) : IMonthlyWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Customers;

    public async Task<MonthlyWorkspaceFeatureContribution> ContributeAsync(
        MonthlyWorkspaceContributorContext context,
        CancellationToken cancellationToken)
    {
        var period = context.Period;
        var created = await CasesCreated(period.StartUtc, period.EndUtc);
        var previousCreated = await CasesCreated(period.ComparisonStartUtc, period.ComparisonEndUtc);
        var resolved = await db.SupportCases.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId && x.ResolvedUtc >= period.StartUtc && x.ResolvedUtc < period.EndUtc)
            .ToListAsync(cancellationToken);
        var openRisk = await db.SupportCases.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId && x.Status != SupportCaseStatuses.Resolved && x.Status != SupportCaseStatuses.Closed &&
                        (x.IsSlaRisk || x.IsSlaBreached || x.IsChurnRisk || x.IsVipRisk))
            .OrderByDescending(x => x.IsSlaBreached).ThenBy(x => x.ResolutionDueUtc).Take(10).ToListAsync(cancellationToken);
        var gaps = await db.SupportKnowledgeGaps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId && x.Status != SupportKnowledgeGapStatuses.Resolved)
            .OrderByDescending(x => x.FrequencyCount).ThenByDescending(x => x.UpdatedUtc).Take(5).ToListAsync(cancellationToken);
        var route = $"/support?companyId={context.CompanyId:D}";
        var eligibleFirstResponses = created.Where(x => x.FirstResponseDueUtc.HasValue && x.FirstResponseSentUtc.HasValue).ToList();
        var met = eligibleFirstResponses.Count(x => x.FirstResponseSentUtc <= x.FirstResponseDueUtc);
        decimal? slaRate = eligibleFirstResponses.Count == 0 ? null : decimal.Round(100m * met / eligibleFirstResponses.Count, 1);

        var results = new List<MonthlyWorkspaceMetricDto>
        {
            new("support.volume", "Support volume", created.Count, created.Count.ToString(), previousCreated.Count,
                previousCreated.Count.ToString(), "cases", created.Count <= previousCreated.Count ? "positive" : "attention",
                created.FirstOrDefault()?.UpdatedUtc ?? period.EndUtc, "support_case", route),
            new("support.sla", "SLA performance", slaRate, slaRate.HasValue ? $"{slaRate:0.#}%" : "Unavailable", null,
                "No comparable governed sample", "%", slaRate.HasValue ? (slaRate >= 90 ? "positive" : "attention") : "unavailable",
                created.FirstOrDefault()?.UpdatedUtc ?? period.EndUtc, "support_case", route, slaRate.HasValue,
                slaRate.HasValue ? null : "No cases with both stored SLA targets and response timestamps occurred this month.")
        };

        var priorities = openRisk.Select(x => new MonthlyWorkspacePriorityCandidate(
            $"support:{x.Id:N}", $"support-case:{x.Id:N}", Lens, $"{x.CaseNumber}: {x.Subject}",
            x.IsSlaBreached ? "The service target is breached and remains unresolved at period end."
                : x.IsChurnRisk ? "The unresolved customer risk can carry into next month." : "The customer risk remains unresolved.",
            context.Access.ResponsiblePerson, context.Access.WorkingAgent, "Open the case and commit the next response or escalation.",
            x.UpdatedUtc, "support_case", x.Id.ToString("D"), $"/support/cases/{x.Id:D}?companyId={context.CompanyId:D}",
            DueUtc: x.ResolutionDueUtc, MaterialChange: x.IsChurnRisk || x.IsVipRisk ? 100 : 50,
            UnresolvedRisk: true, SustainedTrend: true, DirectlyOwned: context.Access.IsPrimary, Confidence: 1m)).ToList();
        priorities.AddRange(gaps.Take(2).Select(x => new MonthlyWorkspacePriorityCandidate(
            $"support-gap:{x.Id:N}", $"support-gap:{x.Id:N}", Lens, $"Knowledge gap repeated {x.FrequencyCount} times",
            x.MissingInformationSummary, context.Access.ResponsiblePerson, context.Access.WorkingAgent,
            "Review the knowledge gap and complete its linked Work task.", x.UpdatedUtc, "support_knowledge_gap",
            x.Id.ToString("D"), route, MaterialChange: x.FrequencyCount, UnresolvedRisk: true,
            SustainedTrend: x.FrequencyCount >= 3, DirectlyOwned: context.Access.IsPrimary, Confidence: 1m)));

        var items = gaps.Select(x => new TodayWorkspaceFeatureItemDto(
            $"support-gap:{x.Id:N}", x.QuestionSummary, x.MissingInformationSummary,
            x.Status, x.UpdatedUtc, x.LinkedTaskId.HasValue
                ? $"/tasks?companyId={context.CompanyId:D}&taskId={x.LinkedTaskId:D}&view=detail&source=dashboard"
                : route)).ToList();
        var section = new MonthlyWorkspaceSectionDto(Lens, "Customer Support",
            openRisk.Count > 0 ? "Service activity is recorded, with unresolved customer risk carrying into next month."
                : "Service activity is recorded with no high-risk case left open at period end.",
            openRisk.Count > 0 ? "attention" : "healthy", created.FirstOrDefault()?.UpdatedUtc ?? period.EndUtc,
            [new("Cases opened", created.Count.ToString()), new("Cases resolved", resolved.Count.ToString()),
             new("SLA performance", slaRate.HasValue ? $"{slaRate:0.#}%" : "Unavailable", slaRate.HasValue ? "current" : "unavailable"),
             new("Open customer risks", openRisk.Count.ToString(), openRisk.Count > 0 ? "attention" : "current"),
             new("Knowledge gaps", gaps.Count.ToString(), gaps.Count > 0 ? "attention" : "current")],
            items, route, "Case volume, stored SLA timestamps, unresolved risk, and knowledge gaps are available.");
        var outcomes = resolved.Take(5).Select(x => new TodayWorkspaceAgentUpdateDto(
            $"support-resolved:{x.Id:N}", "Customer case resolved", $"{x.CaseNumber}: {x.Subject}", context.Access.WorkingAgent,
            x.ResolvedUtc!.Value, "support_case", $"/support/cases/{x.Id:D}?companyId={context.CompanyId:D}",
            "Support agent", TodayAgentStates.Completed, RationaleSummary: "Backed by the case resolution timestamp.",
            UpdatedUtc: x.UpdatedUtc)).ToList();
        return new(Lens, section, priorities, results, outcomes,
            [new("support", "Customer Support", "current", created.FirstOrDefault()?.UpdatedUtc,
                "Period case activity and governed SLA fields are available.")]);

        Task<List<SupportCase>> CasesCreated(DateTime start, DateTime end) => db.SupportCases.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == context.CompanyId && x.CreatedUtc >= start && x.CreatedUtc < end)
            .OrderByDescending(x => x.CreatedUtc).ToListAsync(cancellationToken);
    }
}
