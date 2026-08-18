using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingWorkNeedAssessment(VirtualCompanyDbContext db) : IMarketingWorkNeedAssessment
{
    public async Task<MarketingWorkNeedAssessmentDto> AssessAsync(Guid companyId, DateTime asOfUtc, CancellationToken ct)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.");
        asOfUtc = asOfUtc.Kind == DateTimeKind.Utc ? asOfUtc : asOfUtc.ToUniversalTime();
        var horizon = asOfUtc.AddDays(90); var needs = new List<MarketingWorkNeedDto>();
        var strategies = await db.MarketingStrategies.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && (x.Status == MarketingStrategicStatuses.Active || x.Status == MarketingStrategicStatuses.Approved)).ToArrayAsync(ct);
        var strategy = strategies.Where(x => x.ValidFromUtc <= asOfUtc && x.ValidToUtc >= horizon).OrderByDescending(x => x.Version).FirstOrDefault();
        if (strategy is null) Add("strategy_missing_or_expired", "Strategy needed", "high", true, [], "No approved Marketing strategy covers today.", MarketingToolIds.PreparePlan, true);
        Guid[] segmentVersionIds = [];
        if (strategy is not null)
        {
            segmentVersionIds = await db.MarketingStrategySegments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.MarketingStrategyId == strategy.Id).Select(x => x.MarketingCustomerSegmentVersionId).ToArrayAsync(ct);
            var approvedCount = await db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.CompanyId == companyId && segmentVersionIds.Contains(x.Id) && (x.Status == MarketingStrategicStatuses.Approved || x.Status == MarketingStrategicStatuses.Active), ct);
            if (segmentVersionIds.Length == 0 || approvedCount != segmentVersionIds.Length) Add("approved_segments_missing", "Approved audiences needed", "high", true, [strategy.Id], "The active strategy needs approved linked target segments.", MarketingToolIds.PrepareSegmentation, true);
        }
        var objectives = await db.MarketingObjectives.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == MarketingStatuses.Active && x.PeriodEndUtc > asOfUtc && x.PeriodStartUtc < horizon).ToArrayAsync(ct);
        var plans = await db.MarketingPlans.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.Status != MarketingStatuses.Cancelled && x.Status != MarketingStatuses.Completed && x.EndsUtc > asOfUtc && x.StartsUtc < horizon).ToArrayAsync(ct);
        var planIds = plans.Select(x => x.Id).ToArray();
        var planObjectives = await db.MarketingPlanObjectives.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && planIds.Contains(x.MarketingPlanId)).ToArrayAsync(ct);
        var planSegments = await db.MarketingPlanSegments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && planIds.Contains(x.MarketingPlanId)).ToArrayAsync(ct);
        var planCampaigns = await db.MarketingPlanCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && planIds.Contains(x.MarketingPlanId)).ToArrayAsync(ct);
        var planCampaignIds = planCampaigns.Select(x => x.Id).ToArray();
        var campaignSegmentLinks = await db.MarketingPlanCampaignSegments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && planCampaignIds.Contains(x.MarketingPlanCampaignId)).ToArrayAsync(ct);
        foreach (var objective in objectives.Where(o => planObjectives.All(p => p.MarketingObjectiveId != o.Id))) Add("objective_without_plan", "Objective needs a plan", "high", true, [objective.Id], $"{objective.Name} is active but has no covering Marketing plan.", MarketingToolIds.PreparePlan, false);
        if (strategy is not null && objectives.Length > 0 && plans.Length == 0 && needs.All(x => x.ReasonCode != "objective_without_plan")) Add("plan_missing_for_horizon", "Plan needed", "high", true, [strategy.Id], "No Marketing plan covers the next 90 days.", MarketingToolIds.PreparePlan, false);
        foreach (var plan in plans)
        {
            var links = planCampaigns.Where(x => x.MarketingPlanId == plan.Id).ToArray();
            if (plan.EndsUtc <= asOfUtc.AddDays(21)) Add("plan_ending_soon", "Plan ending soon", "medium", true, [plan.Id], $"{plan.Name} ends within three weeks.", MarketingToolIds.PreparePlan, false);
            if (links.Length == 0) Add("plan_has_no_campaigns", "Plan needs campaigns", "high", true, [plan.Id], $"{plan.Name} has no campaign portfolio.", MarketingToolIds.PrepareCampaignPortfolio, false);
            foreach (var objectiveId in planObjectives.Where(x => x.MarketingPlanId == plan.Id).Select(x => x.MarketingObjectiveId).Where(id => links.All(x => x.MarketingObjectiveId != id))) Add("objective_without_campaign_coverage", "Objective not covered", "medium", true, [plan.Id, objectiveId], "A plan objective has no campaign contribution.", MarketingToolIds.PrepareCampaignPortfolio, false);
            var portfolioSegmentIds = campaignSegmentLinks.Where(x => links.Select(l => l.Id).Contains(x.MarketingPlanCampaignId)).Select(x => x.MarketingPlanSegmentId).ToHashSet();
            foreach (var segmentLink in planSegments.Where(x => x.MarketingPlanId == plan.Id && !portfolioSegmentIds.Contains(x.Id))) Add("target_segment_without_campaign", "Audience not covered", "medium", true, [plan.Id, segmentLink.MarketingCustomerSegmentVersionId], "A plan audience has no campaign coverage.", MarketingToolIds.PrepareCampaignPortfolio, false);
            var allocated = links.Sum(x => x.AllocatedBudget ?? 0m);
            if (plan.PlannedBudget.HasValue && allocated > plan.PlannedBudget) Add("budget_overallocated", "Budget overallocated", "high", true, [plan.Id], "Campaign allocations exceed the plan budget.", MarketingToolIds.AssessPlanCoverage, false);
        }
        var campaignIds = planCampaigns.Select(x => x.SalesCampaignId).ToArray();
        var campaigns = await db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && campaignIds.Contains(x.Id)).ToArrayAsync(ct);
        var campaignContactCounts = await db.SalesCampaignContacts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && campaignIds.Contains(x.SalesCampaignId))
            .GroupBy(x => x.SalesCampaignId).Select(x => new { CampaignId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.CampaignId, x => x.Count, ct);
        var campaignActivityCounts = await db.SalesCampaignActivities.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && campaignIds.Contains(x.SalesCampaignId))
            .GroupBy(x => x.SalesCampaignId).Select(x => new { CampaignId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.CampaignId, x => x.Count, ct);
        var campaignOfferCounts = await db.SalesCampaignOffers.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && campaignIds.Contains(x.SalesCampaignId))
            .GroupBy(x => x.SalesCampaignId).Select(x => new { CampaignId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.CampaignId, x => x.Count, ct);
        var sequenceIds = campaigns.Select(x => x.SalesSequenceId).ToArray();
        var sequenceStepCounts = await db.SalesSequenceSteps.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && sequenceIds.Contains(x.SalesSequenceId))
            .GroupBy(x => x.SalesSequenceId).Select(x => new { SequenceId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.SequenceId, x => x.Count, ct);
        foreach (var campaign in campaigns.Where(x => x.LifecycleStatus is CampaignLifecycleStatuses.Draft or CampaignLifecycleStatuses.Planning)) Add("incomplete_campaign_draft", "Campaign draft incomplete", "medium", true, [campaign.Id], $"{campaign.Name} still needs readiness work.", MarketingToolIds.PopulateCampaignDraft, false);
        foreach (var campaign in campaigns.Where(x => x.ScheduledLaunchUtc <= asOfUtc.AddDays(14) && x.LifecycleStatus == CampaignLifecycleStatuses.Planning)) Add("campaign_readiness_due", "Campaign readiness due", "high", true, [campaign.Id], $"{campaign.Name} is approaching launch and is not ready.", MarketingToolIds.SubmitCampaignForReadiness, false);
        for (var i = 0; i < campaigns.Length; i++) for (var j = i + 1; j < campaigns.Length; j++)
            if (campaigns[i].ScheduledLaunchUtc.HasValue && campaigns[j].ScheduledLaunchUtc.HasValue && Math.Abs((campaigns[i].ScheduledLaunchUtc.Value - campaigns[j].ScheduledLaunchUtc.Value).TotalHours) < 24)
                Add("campaign_schedule_conflict", "Campaign dates overlap", "medium", true, [campaigns[i].Id, campaigns[j].Id], "Two campaigns launch within the same day. Review channel capacity and audience overlap.", MarketingToolIds.AssessPlanCoverage, false);
        var superseded = await db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && planSegments.Select(s => s.MarketingCustomerSegmentVersionId).Contains(x.Id) && x.Status == MarketingStrategicStatuses.Superseded).Select(x => x.Id).ToArrayAsync(ct);
        foreach (var id in superseded) Add("segment_superseded", "Audience version changed", "medium", true, [id], "A plan uses a superseded audience version. Review impact without rewriting history.", MarketingToolIds.AssessPlanCoverage, true);
        var underperforming = await db.MarketingChannelObservations.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.MetricCode == "performance_variance" && x.Value < 0 && x.PeriodEndUtc >= asOfUtc.AddDays(-30) && x.SalesCampaignId != null).Select(x => x.SalesCampaignId!.Value).Distinct().Take(25).ToArrayAsync(ct);
        foreach (var id in underperforming.Where(campaignIds.Contains)) Add("performance_below_plan", "Performance below plan", "medium", true, [id], "Recent observed performance is below the plan baseline.", MarketingToolIds.PreparePerformanceReview, false);
        foreach (var plan in plans.Where(x => x.Status == MarketingStatuses.InReview)) Add("waiting_approval", "Waiting for approval", "low", false, [plan.Id], $"{plan.Name} is waiting for approval; no replacement will be created.", MarketingToolIds.ReadPlans, true);
        var overdueContent = await db.MarketingContentBriefs.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.DueUtc < asOfUtc && x.Status == MarketingStatuses.Draft).Select(x => x.Id).Take(25).ToArrayAsync(ct);
        var overdueActivities = await db.SalesCampaignActivities.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.DueUtc < asOfUtc && x.Status != "completed" && x.Status != "cancelled").Select(x => x.Id).Take(25).ToArrayAsync(ct);
        var overdueWork = overdueContent.Concat(overdueActivities).Take(25).ToArray();
        if (overdueWork.Length > 0) Add("overdue_content_or_activity", "Marketing work overdue", "high", true, overdueWork, "Content or campaign activity is overdue.", MarketingToolIds.ReadContentCalendar, false);
        var segmentStates = await db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && (segmentVersionIds.Contains(x.Id) || planSegments.Select(p => p.MarketingCustomerSegmentVersionId).Contains(x.Id)))
            .Select(x => new { x.Id, x.VersionNumber, x.Status }).ToArrayAsync(ct);
        var stateToken = string.Join('|', strategies.OrderBy(x => x.Id).Select(x => $"strategy:{x.Id:N}:{x.Version}:{x.Status}")) + "|" +
            string.Join('|', segmentStates.OrderBy(x => x.Id).Select(x => $"segment:{x.Id:N}:{x.VersionNumber}:{x.Status}")) + "|" +
            string.Join('|', objectives.OrderBy(x => x.Id).Select(x => $"objective:{x.Id:N}:{x.Version}:{x.Status}")) + "|" +
            string.Join('|', plans.OrderBy(x => x.Id).Select(x => $"plan:{x.Id:N}:{x.Version}:{x.Status}")) + "|" +
            string.Join('|', campaigns.OrderBy(x => x.Id).Select(x => $"campaign:{x.Id:N}:{x.ConcurrencyVersion}:{x.LifecycleStatus}:contacts={campaignContactCounts.GetValueOrDefault(x.Id)}:activities={campaignActivityCounts.GetValueOrDefault(x.Id)}:offers={campaignOfferCounts.GetValueOrDefault(x.Id)}:steps={sequenceStepCounts.GetValueOrDefault(x.SalesSequenceId)}"));
        var ordered = needs.Select(x => x with { Fingerprint = Fingerprint($"{companyId:N}|{x.ReasonCode}|{string.Join(',', x.AffectedIds.Order())}|{stateToken}") })
            .OrderBy(x => x.Urgency == "high" ? 0 : x.Urgency == "medium" ? 1 : 2).ThenBy(x => x.ReasonCode).ToArray();
        return new(asOfUtc, ordered, ["approved strategies and validity", "exact strategy-linked segment versions", "active objectives and plan coverage", "campaign readiness and budget allocation", "content and activity deadlines", "waiting approvals"], ordered.Any(x => x.Actionable));

        void Add(string code, string label, string urgency, bool actionable, IReadOnlyList<Guid> ids, string explanation, string tool, bool approval) =>
            needs.Add(new(code, label, urgency, actionable, ids, ids.Select(x => $"record:{x:N}").ToArray(), explanation, tool, approval, Fingerprint($"{companyId:N}|{code}|{string.Join(',', ids.Order())}")));
    }

    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
