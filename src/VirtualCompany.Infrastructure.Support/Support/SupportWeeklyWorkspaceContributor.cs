using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportWeeklyWorkspaceContributor(VirtualCompanyDbContext db) : IWeeklyWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Customers;
    public async Task<WeeklyWorkspaceContribution> ContributeAsync(WeeklyWorkspaceContributorContext c, CancellationToken token)
    {
        var p = c.Period;
        var events = await db.SupportCaseEvents.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == c.CompanyId &&
                x.SupportCase.CompanyId == c.CompanyId && x.OccurredUtc >= p.ComparisonStartUtc && x.OccurredUtc < p.ActivityEndUtc &&
                (x.EventType == SupportCaseEventTypes.Resolved || x.EventType == SupportCaseEventTypes.Reopened))
            .OrderBy(x => x.OccurredUtc).ThenBy(x => x.Id).Take(WeeklyWorkspaceMeasures.SourceLimit + 1).ToListAsync(token);
        var cases = await db.SupportCases.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == c.CompanyId &&
                (x.CreatedUtc >= p.ComparisonStartUtc && x.CreatedUtc < p.ActivityEndUtc ||
                 x.FirstResponseDueUtc >= p.ComparisonStartUtc && x.FirstResponseDueUtc < p.ActivityEndUtc))
            .OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id).Take(WeeklyWorkspaceMeasures.SourceLimit + 1).ToListAsync(token);
        var complete = events.Count <= WeeklyWorkspaceMeasures.SourceLimit && cases.Count <= WeeklyWorkspaceMeasures.SourceLimit;
        events = events.Take(WeeklyWorkspaceMeasures.SourceLimit).ToList(); cases = cases.Take(WeeklyWorkspaceMeasures.SourceLimit).ToList();
        string Route(Guid id) => $"/support/cases/{id:D}?companyId={c.CompanyId:D}";
        IReadOnlyList<WeeklyWorkspaceSourceDto> Opened(DateTime start, DateTime end) => cases.Where(x => x.CreatedUtc >= start && x.CreatedUtc < end)
            .Select(x => new WeeklyWorkspaceSourceDto(x.Id.ToString("D"), $"{x.CaseNumber}: {x.Subject}", x.CreatedUtc, Route(x.Id))).ToList();
        IReadOnlyList<WeeklyWorkspaceSourceDto> Events(DateTime start, DateTime end, string type) => events.Where(x => x.OccurredUtc >= start && x.OccurredUtc < end && x.EventType == type)
            .Select(x => new WeeklyWorkspaceSourceDto(x.Id.ToString("D"), x.Summary, x.OccurredUtc, Route(x.SupportCaseId))).ToList();
        const string coverage = "Recorded case creations and lifecycle events only; imported cases may lack full lifecycle history. Up to 2,000 cases/events per source window.";
        WeeklyWorkspaceMetricDto Activity(string key, string label, string type) => WeeklyWorkspaceMeasures.Activity(key, label,
            Events(p.StartUtc, p.ActivityEndUtc, type), Events(p.ComparisonStartUtc, p.ComparisonEndUtc, type),
            $"Count of stored {type} events in the interval, including repeated events for one case. A current status does not establish historical movement.", coverage, complete);
        List<SupportCase> Eligible(DateTime start, DateTime end) => cases.Where(x => x.FirstResponseDueUtc >= start && x.FirstResponseDueUtc < end).ToList();
        decimal? Rate(List<SupportCase> eligible, DateTime cutoff) => eligible.Count == 0 ? null : decimal.Round(100m * eligible.Count(x =>
            x.FirstResponseSentUtc.HasValue && x.FirstResponseSentUtc <= x.FirstResponseDueUtc && x.FirstResponseSentUtc < cutoff) / eligible.Count, 1);
        var eligible = Eligible(p.StartUtc, p.ActivityEndUtc); var prior = Eligible(p.ComparisonStartUtc, p.ComparisonEndUtc);
        var rate = Rate(eligible, p.ActivityEndUtc); var priorRate = Rate(prior, p.ComparisonEndUtc);
        var sla = new WeeklyWorkspaceMetricDto("support.sla", "First response SLA", rate, rate.HasValue ? $"{rate:0.#}%" : "Unavailable",
            priorRate, priorRate.HasValue ? $"{priorRate:0.#}%" : "Unavailable", "ratio",
            "Cases whose stored first-response deadline falls in the interval. On-time recorded replies / all due targets; unanswered overdue cases remain in the denominator.",
            $"{eligible.Count} eligible due targets; prior {prior.Count}. Stored business-calendar deadlines are reused; target/calendar changes are not reconstructed.",
            eligible.Select(x => new WeeklyWorkspaceSourceDto(x.Id.ToString("D"), $"{x.CaseNumber}: {x.Subject}", x.FirstResponseDueUtc!.Value, Route(x.Id))).ToList(), complete && rate.HasValue,
            ComparisonSources: prior.Select(x => new WeeklyWorkspaceSourceDto(x.Id.ToString("D"), $"{x.CaseNumber}: {x.Subject}", x.FirstResponseDueUtc!.Value, Route(x.Id))).ToList());
        var risks = await db.SupportCases.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == c.CompanyId &&
                x.Status != SupportCaseStatuses.Resolved && x.Status != SupportCaseStatuses.Closed &&
                (x.IsSlaRisk || x.IsSlaBreached || x.IsChurnRisk || x.IsVipRisk || x.ResolutionDueUtc < p.EndUtc.AddDays(7)))
            .OrderByDescending(x => x.IsSlaBreached).ThenBy(x => x.ResolutionDueUtc).ThenBy(x => x.Id).Take(12).ToListAsync(token);
        return new(Lens, "Support movement and service targets", [WeeklyWorkspaceMeasures.Activity("support.opened", "Cases opened", Opened(p.StartUtc, p.ActivityEndUtc),
            Opened(p.ComparisonStartUtc, p.ComparisonEndUtc), "Cases created in the interval, irrespective of current resolution state.", coverage, complete),
            Activity("support.resolved", "Recorded resolutions", SupportCaseEventTypes.Resolved), Activity("support.reopened", "Recorded reopenings", SupportCaseEventTypes.Reopened), sla,
            WeeklyWorkspaceMeasures.Unavailable("support.backlog_movement", "Backlog balance movement", "balance", "Complete historic lifecycle coverage is not certified; current case statuses cannot reconstruct prior backlog balances.")],
            risks.Select(x => new WeeklyWorkspaceCommitmentDto(x.Id.ToString("D"), $"{x.CaseNumber}: {x.Subject}", "Current unresolved case: review response or escalation; risk observed now.",
                x.ResolutionDueUtc, c.NowUtc, Route(x.Id), c.Access.ResponsiblePerson, c.Access.WorkingAgent)).ToList(),
            ["Historical backlog balances and calendar/target revisions are unavailable; recorded resolutions/reopenings are event counts, not net backlog movement.",
             complete ? "Sparse or missing SLA samples are unavailable, not zero performance." : "The source window exceeds 2,000 records; measures cover only included records."]);
    }
}
