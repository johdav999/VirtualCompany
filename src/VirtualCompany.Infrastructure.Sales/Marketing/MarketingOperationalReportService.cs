using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingOperationalReportService(VirtualCompanyDbContext db, ITodayWorkspaceLensResolver access,
    IMarketingOperationsService operations, IMarketingDeliveryService delivery, TimeProvider clock) : IMarketingOperationalReportService
{
    private async Task Authorize(Guid companyId, CancellationToken ct)
    {
        var scope = await access.ResolveAsync(companyId, TodayWorkspaceLenses.Marketing, ct);
        if (!scope.AvailableLenses.Any(x => x.Lens == TodayWorkspaceLenses.Marketing)) throw new UnauthorizedAccessException();
    }

    public async Task<MarketingOperationalReport> GetAsync(Guid companyId, MarketingOperationalFilter filter, CancellationToken ct)
    {
        await Authorize(companyId, ct);
        var from = Utc(filter.FromUtc); var to = Utc(filter.ToUtc);
        if (to <= from || to - from > TimeSpan.FromDays(366)) throw new ArgumentException("Choose a UTC period of up to 366 days.");
        var currency = string.IsNullOrWhiteSpace(filter.Currency) ? null : filter.Currency.Trim().ToUpperInvariant();
        if (currency is not null && (currency.Length != 3 || !currency.All(char.IsAsciiLetter))) throw new ArgumentException("Choose a three-letter currency.");
        var state = string.IsNullOrWhiteSpace(filter.State) ? null : filter.State.Trim().ToLowerInvariant();
        if (state is not null && !States.Contains(state)) throw new ArgumentException("Choose an available delivery state.");
        filter = filter with { FromUtc = from, ToUtc = to, Currency = currency, State = state };
        var now = clock.GetUtcNow().UtcDateTime;
        var campaigns = await db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.CreatedUtc < to && (!filter.CampaignId.HasValue || x.Id == filter.CampaignId)).OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
        var ids = campaigns.Select(x => x.Id).ToArray();
        var actions = await db.MarketingChannelActions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.UpdatedUtc >= from && x.UpdatedUtc < to && (!filter.CampaignId.HasValue || x.SalesCampaignId == filter.CampaignId)).ToListAsync(ct);
        var touches = await db.MarketingAttributionTouches.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.SubjectType == "campaign" && ids.Contains(x.SubjectId) && x.OccurredUtc >= from && x.OccurredUtc < to).ToListAsync(ct);
        // Period observations are included whole; never prorate or double count overlapping snapshots.
        var observations = await db.MarketingChannelObservations.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.SalesCampaignId.HasValue && ids.Contains(x.SalesCampaignId.Value) && !x.IsSuperseded &&
            x.PeriodStartUtc >= from && x.PeriodEndUtc <= to && x.RetrievedUtc <= now && x.MetricCode == "leads").ToListAsync(ct);
        var handoffs = await db.MarketingSalesHandoffs.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.SalesCampaignId.HasValue && ids.Contains(x.SalesCampaignId.Value) && x.UpdatedUtc >= from && x.UpdatedUtc < to).ToListAsync(ct);
        var attribution = await db.MarketingAttributionResults.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.PeriodStartUtc >= from && x.PeriodEndUtc <= to && x.CreatedUtc <= now).ToListAsync(ct);
        var spend = touches.Where(x => currency is null || string.IsNullOrWhiteSpace(x.Currency) || string.Equals(x.Currency, currency, StringComparison.OrdinalIgnoreCase))
            .Select(x => new MarketingSpendEvidence(x.Id, x.SubjectId,
            x.OccurredUtc, x.Cost, x.Currency?.Trim().ToUpperInvariant(), x.SourceReference)).OrderBy(x => x.OccurredUtc).ThenBy(x => x.Id).ToArray();
        var leads = new List<MarketingLeadEvidence>();
        var rows = campaigns.Where(x => currency is null || x.BudgetCurrency == currency || spend.Any(y => y.CampaignId == x.Id)).Select(c =>
        {
            var costs = spend.Where(x => x.CampaignId == c.Id).ToArray();
            var source = observations.Where(x => x.SalesCampaignId == c.Id).OrderBy(x => x.PeriodStartUtc).ToArray();
            var overlapping = source.Select((x, i) => source.Skip(i + 1).Any(y => y.PeriodStartUtc < x.PeriodEndUtc && y.PeriodEndUtc > x.PeriodStartUtc)).Any(x => x);
            var validLeads = !overlapping && source.All(x => x.Unit is "count" or "leads");
            foreach (var o in source) leads.Add(new(o.Id, c.Id, o.Value, o.Unit, overlapping ? "overlapping evidence" : "observed", o.SourceReference, o.RetrievedUtc));
            var outcomes = handoffs.Where(x => x.SalesCampaignId == c.Id).Select(x => x.ContactId).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
            var covered = outcomes.Count(id => attribution.Any(x => x.SubjectType == "contact" && x.SubjectId == id));
            var known = costs.Where(x => x.Cost.HasValue && x.Currency == c.BudgetCurrency).ToArray();
            var gaps = new List<string>();
            if (!c.PlannedBudget.HasValue) gaps.Add("No campaign budget recorded. Plan budgets are not campaign allocations.");
            if (costs.Length == 0 || costs.Any(x => !x.Cost.HasValue || string.IsNullOrEmpty(x.Currency))) gaps.Add("Spend coverage is incomplete; known cost is not total spend.");
            if (costs.Any(x => x.Currency != c.BudgetCurrency)) gaps.Add("Costs in other currencies are separate; no currency conversion.");
            if (source.Length == 0) gaps.Add("Lead outcomes unavailable.");
            if (!validLeads) gaps.Add("Lead observations overlap or have incompatible units; no additive total.");
            if (outcomes.Length == 0 || covered < outcomes.Length) gaps.Add("Attribution coverage is incomplete; campaign success is not causal evidence.");
            return new MarketingCampaignEvidence(c.Id, c.Name, c.LifecycleStatus, c.AudienceType, c.ScheduledLaunchUtc, c.UpdatedUtc,
                c.PlannedBudget, c.BudgetCurrency, known.Length == 0 ? null : known.Sum(x => x.Cost!.Value),
                costs.Count(x => !x.Cost.HasValue || string.IsNullOrEmpty(x.Currency)), source.Length == 0 || !validLeads ? null : source.Sum(x => x.Value), covered, outcomes.Length, gaps);
        }).ToArray();
        var rowIds = rows.Select(x => x.Id).ToHashSet();
        return new(companyId, now, filter, "marketing-operational-v1", rows,
            actions.Where(x => (x.SalesCampaignId is null && currency is null || x.SalesCampaignId.HasValue && rowIds.Contains(x.SalesCampaignId.Value)) &&
                (state is null || x.Status == state)).OrderBy(x => x.UpdatedUtc).ThenBy(x => x.Id).Select(x => new MarketingDeliveryEvidence(x.Id,
                    x.SalesCampaignId, x.MarketingContentBriefId, x.Status, x.ScheduledUtc, x.UpdatedUtc, x.AttemptCount, x.ApprovalRequestId, x.ProviderReference, x.FailureCode)).ToArray(),
            spend.Where(x => rowIds.Contains(x.CampaignId)).ToArray(), leads.Where(x => rowIds.Contains(x.CampaignId)).ToArray(),
            ["Delivery: current state of actions updated in the UTC period; prepared/approved/queued are not provider-confirmed outcomes.",
             "Spend: immutable campaign cost touches in the period. No touches means unknown; observations are not added to avoid duplicate costs. Budget is recorded campaign allocation for its full lifetime, not approval to spend.",
             "Leads: non-superseded whole-period lead observations; overlapping periods cannot be summed. Attribution coverage is distinct handoff contacts with any in-period attribution result; it does not prove causality."]);
    }

    public async Task<MarketingCampaignReview> GetReviewAsync(Guid companyId, Guid? campaignId, Guid? briefId, CancellationToken ct)
    {
        await Authorize(companyId, ct);
        if (!campaignId.HasValue && !briefId.HasValue) throw new ArgumentException("Choose a campaign or content item.");
        var content = (await operations.ListContentAsync(companyId, ct)).Where(x => (!campaignId.HasValue || x.CampaignId == campaignId) &&
            (!briefId.HasValue || x.Id == briefId)).ToArray();
        if (briefId.HasValue && content.Length == 0) throw new KeyNotFoundException();
        campaignId ??= content.FirstOrDefault()?.CampaignId;
        var now = clock.GetUtcNow().UtcDateTime;
        var report = await GetAsync(companyId, new(now.AddDays(-365), now.AddDays(1), campaignId), ct);
        var campaign = campaignId.HasValue ? report.Campaigns.SingleOrDefault(x => x.Id == campaignId) : null;
        if (campaignId.HasValue && campaign is null) throw new KeyNotFoundException();
        var briefs = content.Select(x => x.Id).ToArray();
        var assets = (await delivery.ListCreativeAssetsAsync(companyId, ct)).Where(x => briefs.Contains(x.BriefId)).ToArray();
        var segmentIds = content.Where(x => x.SegmentVersionId.HasValue).Select(x => x.SegmentVersionId!.Value).ToArray();
        var audiences = await db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId &&
            segmentIds.Contains(x.Id)).ToListAsync(ct);
        return new(companyId, report.AsOfUtc, campaign, content, assets, report.Deliveries.Where(x => campaignId.HasValue || x.BriefId.HasValue && briefs.Contains(x.BriefId.Value)).ToArray(),
            audiences.Select(x => new MarketingReviewAudience(x.Id, x.VersionNumber, x.Status, x.EvidenceJson,
                "Audience evidence has the recorded coverage only; no inferred consent or reach.")).ToArray());
    }
    public static readonly IReadOnlySet<string> States = new HashSet<string>(StringComparer.Ordinal)
    { "proposed", "awaiting_approval", "queued", "dispatching", "dispatched", "delivered", "failed", "retry_scheduled", "ambiguous", "cancelled" };
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
