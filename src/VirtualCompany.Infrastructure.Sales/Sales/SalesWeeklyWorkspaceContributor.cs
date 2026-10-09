using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesWeeklyWorkspaceContributor(VirtualCompanyDbContext db) : IWeeklyWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Sales;
    public async Task<WeeklyWorkspaceContribution> ContributeAsync(WeeklyWorkspaceContributorContext c, CancellationToken token)
    {
        var p = c.Period;
        var rows = await db.SalesActivities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == c.CompanyId && !x.IsDeleted && x.OccurredUtc >= p.ComparisonStartUtc && x.OccurredUtc < p.ActivityEndUtc &&
                (x.DealId == null || db.Deals.IgnoreQueryFilters().Any(d => d.CompanyId == c.CompanyId && d.Id == x.DealId && !d.IsDeleted)) &&
                (x.LeadId == null || db.Leads.IgnoreQueryFilters().Any(d => d.CompanyId == c.CompanyId && d.Id == x.LeadId && !d.IsDeleted)) &&
                (x.ContactId == null || db.Contacts.IgnoreQueryFilters().Any(d => d.CompanyId == c.CompanyId && d.Id == x.ContactId && !d.IsDeleted)) &&
                (x.CustomerCompanyId == null || db.CustomerCompanies.IgnoreQueryFilters().Any(d => d.CompanyId == c.CompanyId && d.Id == x.CustomerCompanyId && !d.IsDeleted)))
            .OrderBy(x => x.OccurredUtc).ThenBy(x => x.Id).Take(WeeklyWorkspaceMeasures.SourceLimit + 1).ToListAsync(token);
        var complete = rows.Count <= WeeklyWorkspaceMeasures.SourceLimit;
        rows = rows.Take(WeeklyWorkspaceMeasures.SourceLimit).ToList();
        string Route(SalesActivity x) => x.DealId.HasValue ? $"/app/sales/deals/{x.DealId:D}?companyId={c.CompanyId:D}"
            : x.LeadId.HasValue ? $"/app/sales/leads/{x.LeadId:D}?companyId={c.CompanyId:D}" : $"/app/sales/activities?companyId={c.CompanyId:D}";
        IReadOnlyList<WeeklyWorkspaceSourceDto> Sources(DateTime start, DateTime end, string? type) => rows
            .Where(x => x.OccurredUtc >= start && x.OccurredUtc < end && (type == null || x.ActivityType == type))
            .Select(x => new WeeklyWorkspaceSourceDto(x.Id.ToString("D"), x.Summary, x.OccurredUtc, Route(x))).ToList();
        WeeklyWorkspaceMetricDto Activity(string key, string label, string? type) => WeeklyWorkspaceMeasures.Activity(key, label,
            Sources(p.StartUtc, p.ActivityEndUtc, type), Sources(p.ComparisonStartUtc, p.ComparisonEndUtc, type),
            type == null ? "Count of recorded Sales activities by occurrence time; not a count of deals or revenue." : $"Count of recorded '{type}' activities by occurrence time; not pipeline value.",
            "Retained, non-deleted Sales activities only; absent older/imported events are not inferred. Combined prior/current source window is limited to 2,000 events.", complete);
        var deals = await db.Deals.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == c.CompanyId && !x.IsDeleted && x.Status == SalesStatuses.Open &&
                x.ExpectedCloseUtc < p.EndUtc.AddDays(7)).OrderBy(x => x.ExpectedCloseUtc).ThenBy(x => x.Id).Take(12).ToListAsync(token);
        var commitments = deals.Select(x => new WeeklyWorkspaceCommitmentDto(x.Id.ToString("D"), x.Title,
            "Current open opportunity: confirm the customer follow-up and expected close date. No historical status is inferred.", x.ExpectedCloseUtc,
            c.NowUtc, $"/app/sales/deals/{x.Id:D}?companyId={c.CompanyId:D}", c.Access.ResponsiblePerson, c.Access.WorkingAgent)).ToList();
        var followUps = await db.SalesAgentRecommendations.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == c.CompanyId && !x.IsDeleted &&
                x.Category == "follow_up" && x.ExecutedUtc >= p.ComparisonStartUtc && x.ExecutedUtc < p.ActivityEndUtc &&
                (x.ExecutionStatus == SalesStatuses.Completed || x.ExecutionStatus == SalesStatuses.DraftCreated) &&
                (x.DealId == null || db.Deals.IgnoreQueryFilters().Any(d => d.CompanyId == c.CompanyId && d.Id == x.DealId && !d.IsDeleted)) &&
                (x.LeadId == null || db.Leads.IgnoreQueryFilters().Any(d => d.CompanyId == c.CompanyId && d.Id == x.LeadId && !d.IsDeleted)))
            .OrderBy(x => x.ExecutedUtc).ThenBy(x => x.Id).Take(WeeklyWorkspaceMeasures.SourceLimit + 1).ToListAsync(token);
        IReadOnlyList<WeeklyWorkspaceSourceDto> FollowUps(DateTime start, DateTime end) => followUps.Take(WeeklyWorkspaceMeasures.SourceLimit)
            .Where(x => x.ExecutedUtc >= start && x.ExecutedUtc < end).Select(x => new WeeklyWorkspaceSourceDto(x.Id.ToString("D"),
                $"{x.Recommendation} · {(x.ExecutionStatus == SalesStatuses.DraftCreated ? "draft prepared" : "owning execution completed")}", x.ExecutedUtc!.Value,
                x.DealId.HasValue ? $"/app/sales/deals/{x.DealId:D}?companyId={c.CompanyId:D}" :
                    x.LeadId.HasValue ? $"/app/sales/leads/{x.LeadId:D}?companyId={c.CompanyId:D}" : $"/app/sales?companyId={c.CompanyId:D}")).ToList();
        var gaps = new List<string> { "Pipeline money balance movement is unavailable without complete dated deal-value/stage snapshots. Stage changes count recorded events, including repeated changes on the same deal." };
        if (!complete) gaps.Add("The combined activity window exceeds 2,000; counts and details cover only the included events.");
        return new(Lens, "Sales movement and follow-ups", [Activity("sales.stage_changes", "Recorded stage changes", "stage change"),
            Activity("sales.activities", "Recorded customer activities", null),
            WeeklyWorkspaceMeasures.Activity("sales.follow_ups", "Recorded follow-up executions", FollowUps(p.StartUtc, p.ActivityEndUtc),
                FollowUps(p.ComparisonStartUtc, p.ComparisonEndUtc), "Retained follow-up recommendations with an execution timestamp and completed or draft-prepared execution state. Draft preparation is not sending or customer receipt.",
                "Current successful execution cohort; retried, deleted or imported history is not reconstructed. Up to 2,000 executions.", followUps.Count <= WeeklyWorkspaceMeasures.SourceLimit),
            WeeklyWorkspaceMeasures.Unavailable("sales.pipeline_movement", "Pipeline balance movement", "balance", gaps[0])], commitments, gaps);
    }
}
