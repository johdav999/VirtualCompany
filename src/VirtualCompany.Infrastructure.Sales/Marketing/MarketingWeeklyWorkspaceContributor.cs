using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingWeeklyWorkspaceContributor(VirtualCompanyDbContext db) : IWeeklyWorkspaceContributor
{
    public string Lens => TodayWorkspaceLenses.Marketing;
    public async Task<WeeklyWorkspaceContribution> ContributeAsync(WeeklyWorkspaceContributorContext c, CancellationToken token)
    {
        var p = c.Period;
        var campaigns = await db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == c.CompanyId &&
                (x.StartedUtc >= p.ComparisonStartUtc && x.StartedUtc < p.ActivityEndUtc ||
                 x.ScheduledLaunchUtc >= p.StartUtc && x.ScheduledLaunchUtc < p.EndUtc.AddDays(7)))
            .OrderBy(x => x.Id).Take(WeeklyWorkspaceMeasures.SourceLimit + 1).ToListAsync(token);
        var activities = await db.SalesCampaignActivities.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == c.CompanyId &&
                x.SalesCampaign.CompanyId == c.CompanyId && x.CompletedUtc >= p.ComparisonStartUtc && x.CompletedUtc < p.ActivityEndUtc)
            .OrderBy(x => x.CompletedUtc).ThenBy(x => x.Id).Take(WeeklyWorkspaceMeasures.SourceLimit + 1).ToListAsync(token);
        var complete = campaigns.Count <= WeeklyWorkspaceMeasures.SourceLimit && activities.Count <= WeeklyWorkspaceMeasures.SourceLimit;
        campaigns = campaigns.Take(WeeklyWorkspaceMeasures.SourceLimit).ToList();
        activities = activities.Take(WeeklyWorkspaceMeasures.SourceLimit).ToList();
        string Route(Guid id) => $"/app/sales/campaigns?companyId={c.CompanyId:D}&campaignId={id:D}";
        IReadOnlyList<WeeklyWorkspaceSourceDto> Launches(DateTime start, DateTime end) => campaigns.Where(x => x.StartedUtc >= start && x.StartedUtc < end)
            .Select(x => new WeeklyWorkspaceSourceDto(x.Id.ToString("D"), x.Name, x.StartedUtc!.Value, Route(x.Id))).ToList();
        IReadOnlyList<WeeklyWorkspaceSourceDto> Delivered(DateTime start, DateTime end) => activities.Where(x => x.CompletedUtc >= start && x.CompletedUtc < end)
            .Select(x => new WeeklyWorkspaceSourceDto(x.Id.ToString("D"), x.Name, x.CompletedUtc!.Value, Route(x.SalesCampaignId))).ToList();
        var commitments = campaigns.Where(x => x.ScheduledLaunchUtc >= p.StartUtc && x.ScheduledLaunchUtc < p.EndUtc.AddDays(7) && x.StartedUtc == null)
            .OrderBy(x => x.ScheduledLaunchUtc).ThenBy(x => x.Id).Take(12).Select(x => new WeeklyWorkspaceCommitmentDto(x.Id.ToString("D"), x.Name,
                "Current scheduled launch; review campaign readiness and required approval. Scheduling is not launch or delivery.", x.ScheduledLaunchUtc,
                c.NowUtc, Route(x.Id), c.Access.ResponsiblePerson, c.Access.WorkingAgent)).ToList();
        var briefs = await db.MarketingContentBriefs.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == c.CompanyId &&
                x.DueUtc >= p.StartUtc && x.DueUtc < p.EndUtc &&
                (x.SalesCampaignId == null || db.SalesCampaigns.IgnoreQueryFilters().Any(s => s.CompanyId == c.CompanyId && s.Id == x.SalesCampaignId)))
            .OrderBy(x => x.DueUtc).ThenBy(x => x.Id).Take(WeeklyWorkspaceMeasures.SourceLimit + 1).ToListAsync(token);
        complete &= briefs.Count <= WeeklyWorkspaceMeasures.SourceLimit;
        var contentSources = briefs.Take(WeeklyWorkspaceMeasures.SourceLimit).Select(x => new WeeklyWorkspaceSourceDto(x.Id.ToString("D"), x.Title,
            x.DueUtc!.Value, $"/marketing?companyId={c.CompanyId:D}&section=Content&briefId={x.Id:D}")).ToList();
        commitments.AddRange(briefs.Take(8).Select(x => new WeeklyWorkspaceCommitmentDto(x.Id.ToString("D"), x.Title,
            "Current content deadline: open the exact brief and review readiness. Due content is not approved or delivered.", x.DueUtc, c.NowUtc,
            $"/marketing?companyId={c.CompanyId:D}&section=Content&briefId={x.Id:D}", c.Access.ResponsiblePerson, c.Access.WorkingAgent)));
        var gaps = new List<string> { "Launches use the retained first start timestamp. Completed campaign activities are internal delivery, not provider-confirmed publication or attributed revenue. Deleted/imported history and subsequent relaunches are not reconstructed." };
        if (!complete) gaps.Add("Campaign or activity window exceeds 2,000; counts describe only included source records.");
        return new(Lens, "Marketing delivery and launches", [WeeklyWorkspaceMeasures.Activity("marketing.launches", "Recorded campaign starts",
            Launches(p.StartUtc, p.ActivityEndUtc), Launches(p.ComparisonStartUtc, p.ComparisonEndUtc), "Campaigns with a retained first start timestamp in the interval; scheduled launches are excluded.", gaps[0], complete),
            WeeklyWorkspaceMeasures.Activity("marketing.delivery", "Completed campaign activities", Delivered(p.StartUtc, p.ActivityEndUtc),
                Delivered(p.ComparisonStartUtc, p.ComparisonEndUtc), "Internal campaign activity completion timestamps in the interval; not customer receipt or provider confirmation.", gaps[0], complete),
            new("marketing.content_due", "Content due in selected week", contentSources.Count, contentSources.Count.ToString(), null,
                "Historical due-date revisions unavailable", "current_due_schedule", "Current content brief deadlines in the full selected calendar week, including upcoming days; not completed delivery.",
                "Current due schedule; prior schedule versions are not reconstructed.", contentSources, complete, c.NowUtc)], commitments, gaps);
    }
}
