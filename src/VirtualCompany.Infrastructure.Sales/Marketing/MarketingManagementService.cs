using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class MarketingManagementService(VirtualCompanyDbContext db, ITodayWorkspaceLensResolver access,
    TimeProvider clock) : IMarketingManagementService
{
    public const string Version = "marketing-management.v1";
    private const int Bound = 2000;
    private async Task<TodayWorkspaceLensResolution> Authorize(Guid company, CancellationToken ct)
    {
        if (company == Guid.Empty) throw new ArgumentException("Choose a company.");
        var scope = await access.ResolveAsync(company, TodayWorkspaceLenses.Marketing, ct);
        if (!scope.AvailableLenses.Any(x => x.Lens == TodayWorkspaceLenses.Marketing)) throw new UnauthorizedAccessException();
        return scope;
    }
    private static List<T> Bounded<T>(List<T> rows, string source)
    {
        if (rows.Count > Bound) throw new ArgumentException($"{source} exceeds {Bound} records. Select a campaign, segment or narrower source cohort.");
        return rows;
    }
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static string? Currency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim().ToUpperInvariant();
        if (value.Length != 3 || !value.All(char.IsAsciiLetterUpper)) throw new ArgumentException("Choose a three-letter currency.");
        return value;
    }
    private static TimeZoneInfo Timezone(string? value)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(value ?? "UTC"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    public async Task<MarketingManagementReport> ReportAsync(Guid company, MarketingManagementQuery query, CancellationToken ct)
    {
        await Authorize(company, ct);
        query = query with { Currency = Currency(query.Currency) };
        var now = clock.GetUtcNow().UtcDateTime;
        var zone = Timezone(await db.Companies.IgnoreQueryFilters().Where(x => x.Id == company).Select(x => x.Timezone).SingleAsync(ct));
        var period = MonthlyWorkspacePeriod.Resolve(now, zone, query.Year, query.Month);
        var cutoff = now < period.EndUtc ? now : period.EndUtc;
        var allLinks = Bounded(await db.MarketingPlanCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company &&
            (!query.CampaignId.HasValue || x.SalesCampaignId == query.CampaignId) &&
            (!query.SegmentVersionId.HasValue || db.MarketingPlanCampaignSegments.IgnoreQueryFilters().Any(s => s.CompanyId == company &&
                s.MarketingPlanCampaignId == x.Id && db.MarketingPlanSegments.IgnoreQueryFilters().Any(p => p.CompanyId == company &&
                    p.Id == s.MarketingPlanSegmentId && p.MarketingCustomerSegmentVersionId == query.SegmentVersionId))))
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Portfolio campaign links");
        var linkIds = allLinks.Select(x => x.Id).ToArray();
        var relatedPlanIds = allLinks.Select(x => x.MarketingPlanId).Distinct().ToArray();
        var planSegments = Bounded(await db.MarketingPlanSegments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company &&
            (query.CampaignId == null || relatedPlanIds.Contains(x.MarketingPlanId)) &&
            (query.SegmentVersionId == null || x.MarketingCustomerSegmentVersionId == query.SegmentVersionId))
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Portfolio segments");
        var segmentLinks = Bounded(await db.MarketingPlanCampaignSegments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == company && linkIds.Contains(x.MarketingPlanCampaignId))
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Campaign segment links");
        if (query.SegmentVersionId.HasValue && !planSegments.Any(x => x.MarketingCustomerSegmentVersionId == query.SegmentVersionId))
            throw new KeyNotFoundException("This segment is unavailable in the current company portfolio.");
        var campaignIdsForSegment = segmentLinks.Where(x => planSegments.Any(s => s.Id == x.MarketingPlanSegmentId &&
            s.MarketingCustomerSegmentVersionId == query.SegmentVersionId)).Select(x => x.MarketingPlanCampaignId).ToHashSet();
        var selectedCampaignIds = allLinks.Where(x => campaignIdsForSegment.Contains(x.Id)).Select(x => x.SalesCampaignId).ToArray();
        var campaigns = Bounded(await db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company &&
            x.CreatedUtc < period.EndUtc && x.CreatedUtc <= now && (!query.CampaignId.HasValue || x.Id == query.CampaignId) &&
            (!query.SegmentVersionId.HasValue || selectedCampaignIds.Contains(x.Id)))
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Campaigns");
        if (query.CampaignId.HasValue && campaigns.Count == 0) throw new KeyNotFoundException("This campaign is unavailable in the selected cohort.");
        var ids = campaigns.Select(x => x.Id).ToArray();
        var touches = Bounded(await db.MarketingAttributionTouches.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company &&
            x.OccurredUtc >= period.StartUtc && x.OccurredUtc < cutoff &&
            ((!query.CampaignId.HasValue && !query.SegmentVersionId.HasValue) || x.SubjectType == "campaign" && ids.Contains(x.SubjectId)))
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Cost touches");
        var costs = new List<MarketingCostSource>();
        foreach (var group in touches.GroupBy(x => new { x.SubjectType, x.SubjectId, x.TouchType, x.Channel, x.SourceReference, x.OccurredUtc }))
        {
            var latest = group.OrderByDescending(x => x.SourceVersion).ThenBy(x => x.Id).First();
            var conflict = group.Where(x => x.SourceVersion == latest.SourceVersion).Select(x => (x.Cost, x.Currency)).Distinct().Count() > 1;
            foreach (var touch in group)
            {
                var currency = NormalizeSourceCurrency(touch.Currency);
                if (query.Currency != null && currency != null && currency != query.Currency) continue;
                costs.Add(new(touch.Id, touch.SubjectType, touch.SubjectId, touch.Channel, Utc(touch.OccurredUtc),
                    conflict ? null : touch.Cost, currency, touch.SourceReference, touch.SourceVersion, touch.Id == latest.Id,
                    conflict ? "Conflicting costs at the same source version; amount unavailable." : touch.Id == latest.Id ?
                    "Latest recorded source version; unknown costs remain unavailable." : "Duplicate or earlier source version; excluded from totals."));
            }
        }
        var models = Bounded(await db.MarketingAttributionModels.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company && x.CreatedUtc <= now)
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Attribution models");
        if (query.ModelId.HasValue && !models.Any(x => x.Id == query.ModelId)) throw new KeyNotFoundException("This model version is unavailable.");
        var results = Bounded(await db.MarketingAttributionResults.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company &&
            x.PeriodStartUtc >= period.StartUtc && x.PeriodEndUtc <= cutoff && x.CreatedUtc <= now &&
            ((!query.CampaignId.HasValue && !query.SegmentVersionId.HasValue) || x.SubjectType == "campaign" && ids.Contains(x.SubjectId)))
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Attribution runs");
        var resultIds = results.Select(x => x.Id).ToArray();
        var allocations = Bounded(await db.MarketingAttributionAllocations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == company && resultIds.Contains(x.MarketingAttributionResultId))
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Attribution allocations");
        var attribution = results.Select(r => Attribution(r, models, allocations, costs, query.ModelId)).ToArray();
        // Repeated runs of one subject/window/model/unit do not represent new outcomes.
        var deduplicated = attribution.GroupBy(x => new { x.SubjectType, x.SubjectId, x.ModelId, x.ModelVersion, x.Unit, x.StartUtc, x.EndUtc })
            .SelectMany(g =>
            {
                var conflict = g.Select(x => JsonSerializer.Serialize(new { x.Value, Allocations = x.Allocations.OrderBy(a => a.TouchId) })).Distinct().Count() > 1;
                return g.OrderBy(x => x.Id).Select((x, index) => conflict ? x with { Included = false, Coverage = "Conflicting repeated outcome cohorts; outcomes unavailable." } :
                    index == 0 ? x : x with { Included = false, Coverage = "Repeated outcome cohort; excluded from totals." });
            }).ToArray();
        attribution = deduplicated.Select(x => deduplicated.Any(y => y.Id != x.Id && y.Included && x.Included &&
            y.SubjectType == x.SubjectType && y.SubjectId == x.SubjectId && y.Unit == x.Unit && y.ModelId == x.ModelId &&
            y.StartUtc < x.EndUtc && y.EndUtc > x.StartUtc) ? x with { Included = false, Coverage = "Overlapping outcome windows; additive outcomes unavailable." } : x).ToArray();
        var channels = costs.Where(x => x.Included).GroupBy(x => (x.Channel, x.Currency)).Select(g =>
        {
            var channelCosts = g.ToArray(); var touchIds = channelCosts.Select(x => x.Id).ToHashSet();
            var credited = attribution.Where(x => x.Included).SelectMany(x => x.Allocations.Where(a => touchIds.Contains(a.TouchId))
                .Select(a => (x.Unit, a.Value, a.TouchId))).ToArray();
            var unknown = channelCosts.Count(x => !x.Cost.HasValue || x.Currency == null) +
                costs.Count(x => x.Included && x.Channel == g.Key.Channel && x.Currency == null && g.Key.Currency != null);
            var known = channelCosts.Where(x => x.Cost.HasValue && x.Currency != null).ToArray();
            decimal? total = known.Length == 0 ? null : known.Sum(x => x.Cost!.Value);
            var attributedTouchCount = credited.Select(x => x.TouchId).Distinct().Count();
            var complete = unknown == 0 && channelCosts.Length > 0 && attributedTouchCount == channelCosts.Length;
            var economics = credited.GroupBy(x => x.Unit).Select(x => new MarketingAcquisitionMeasure(x.Key, x.Sum(a => a.Value),
                complete && x.Select(a => a.TouchId).Distinct().Count() == channelCosts.Length && total.HasValue && x.Sum(a => a.Value) > 0 ? total / x.Sum(a => a.Value) : null)).ToArray();
            return new MarketingChannelPerformance(g.Key.Channel, g.Key.Currency, total, channelCosts.Length, unknown,
                attributedTouchCount, economics, complete ? "Included cost touches have reconciled attribution; recorded cohort only, not causal evidence." :
                "Costs, currency or reconciled attribution are incomplete. Acquisition economics unavailable.");
        }).OrderBy(x => x.Channel).ThenBy(x => x.Currency).ToArray();
        var versionIds = planSegments.Select(x => x.MarketingCustomerSegmentVersionId).ToArray();
        var versions = Bounded(await db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company && versionIds.Contains(x.Id))
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Segment versions");
        var campaignRows = campaigns.Select(c => new MarketingManagementCampaign(c.Id, c.Name, c.BudgetCurrency, c.PlannedBudget,
            segmentLinks.Where(x => allLinks.Any(l => l.Id == x.MarketingPlanCampaignId && l.SalesCampaignId == c.Id))
                .Select(x => (Link: x, Segment: planSegments.SingleOrDefault(s => s.Id == x.MarketingPlanSegmentId)))
                .Where(x => x.Segment != null && versions.Any(v => v.Id == x.Segment.MarketingCustomerSegmentVersionId))
                .Select(x => new MarketingSegmentEvidence(x.Link.Id, x.Segment!.MarketingCustomerSegmentVersionId,
                    versions.Single(v => v.Id == x.Segment.MarketingCustomerSegmentVersionId).VersionNumber, x.Link.Rationale)).ToArray())).ToArray();
        var planIds = allLinks.Where(x => ids.Contains(x.SalesCampaignId)).Select(x => x.MarketingPlanId).Distinct().ToArray();
        var completePlanLinks = Bounded(await db.MarketingPlanCampaigns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == company && planIds.Contains(x.MarketingPlanId)).OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Applicable portfolio allocations");
        var plans = Bounded(await db.MarketingPlans.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company && planIds.Contains(x.Id))
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Portfolio plans");
        var planRows = plans.Select(p => new MarketingPlanBudgetEvidence(p.Id, p.Name, p.BudgetCurrency, p.PlannedBudget,
            completePlanLinks.Where(x => x.MarketingPlanId == p.Id).Select(x => new MarketingPlanAllocationEvidence(x.SalesCampaignId, x.AllocatedBudget, x.BudgetCurrency)).ToArray())).ToArray();
        var experiments = Bounded(await db.MarketingExperiments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company &&
            x.StartsUtc < cutoff && x.EndsUtc > period.StartUtc &&
            ((!query.CampaignId.HasValue && !query.SegmentVersionId.HasValue) || x.SalesCampaignId.HasValue && ids.Contains(x.SalesCampaignId.Value)))
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Experiments");
        var experimentIds = experiments.Select(x => x.Id).ToArray();
        var decisions = Bounded(await db.MarketingExperimentDecisions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == company && experimentIds.Contains(x.MarketingExperimentId) && x.CreatedUtc <= now)
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Experiment decisions");
        var exposureCounts = await db.MarketingExperimentExposures.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == company && experimentIds.Contains(x.MarketingExperimentId) && x.ExposedUtc <= now)
            .GroupBy(x => x.MarketingExperimentId).Select(x => new { Id = x.Key, Count = x.Count() }).ToListAsync(ct);
        var experimentRows = experiments.Select(e =>
        {
            var decision = decisions.SingleOrDefault(x => x.MarketingExperimentId == e.Id);
            return new MarketingExperimentEvidence(e.Id, e.Name, e.SalesCampaignId, e.Hypothesis, e.PrimaryMetric, e.GuardrailMetric,
                e.MinimumSampleSize, Utc(e.StartsUtc), Utc(e.EndsUtc), decision == null ? null : new MarketingExperimentDecisionDto(decision.Id,
                e.Id, decision.Decision, decision.SampleSize, decision.ContaminationRate, decision.GuardrailBreached,
                decision.CausalEligible && decision.Decision == "ready_for_decision" && !decision.GuardrailBreached && decision.SampleSize >= e.MinimumSampleSize && decision.ContaminationRate <= .05m,
                decision.EvidenceJson, decision.Limitations), exposureCounts.SingleOrDefault(x => x.Id == e.Id)?.Count ?? 0,
                "Recorded experiment decision, sample and guardrail evidence only. Causal eligibility does not establish uplift or channel-wide causality.");
        }).ToArray();
        var connections = Bounded(await db.MarketingChannelConnections.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company)
            .OrderBy(x => x.Id).Take(Bound + 1).ToListAsync(ct), "Channel source health");
        var health = connections.Select(x => new MarketingMeasurementHealth(x.Provider, x.DisplayName, x.Status, x.HealthStatus,
            x.LastCheckedUtc.HasValue ? Utc(x.LastCheckedUtc.Value) : null,
            x.HealthStatus == "healthy" ? "Connection health recorded; complete cost/outcome ingestion is not established." :
            "Source health is unavailable or needs reconnection; missing measurements must not be treated as zero.")).ToArray();
        return new(company, query, zone.Id, period.StartUtc, period.EndUtc, now, Version, channels, costs.OrderBy(x => x.Id).ToArray(),
            attribution, models.Select(x => new MarketingAttributionModelDto(x.Id, x.Name, x.ModelType, x.Version, x.RulesJson, x.Limitations, x.LookbackDays)).ToArray(),
            experimentRows, campaignRows, planRows,
            ["Cost cohort: immutable touches in [company-local month start, month end), capped at current as-of. Duplicate source versions count once. Known cost is not total spend when costs are missing.",
             "Outcomes: whole recorded attribution windows within the month; explicit model/version and reconciled allocations required. Repeated and overlapping outcome cohorts are excluded. Configured model lookback is disclosed; the captured run window determines actual inclusion.",
             "Economics = known channel/currency cost divided by credited outcome units only with complete included cost and attribution coverage and a positive denominator. Units/currencies are never added together. This is attributed cost per unit, not causal acquisition cost or return on investment.",
             "Campaign budgets, plan ceilings and segment associations are current recorded context, not reconstructed historical actuals. Segment filtering selects linked campaigns; segment-specific outcomes are not inferred. Unmapped contact touches are included only in unfiltered reports.",
             "Missing or failed ingestion has no guaranteed observation instrumentation: absent touches/outcomes remain unavailable, not zero. No provider ingestion or outbound action is triggered by this report."], health);
    }
    private static string? NormalizeSourceCurrency(string? value)
    {
        try { return Currency(value); } catch (ArgumentException) { return null; }
    }
    private static MarketingAttributionEvidence Attribution(MarketingAttributionResult r,
        IReadOnlyList<MarketingAttributionModelDefinition> models, IReadOnlyList<MarketingAttributionAllocation> allocations,
        IReadOnlyList<MarketingCostSource> costs, Guid? selectedModel)
    {
        MarketingAttributionModelDefinition? model = null;
        try
        {
            using var evidence = JsonDocument.Parse(r.EvidenceJson);
            if (evidence.RootElement.TryGetProperty("Id", out var id) && id.TryGetGuid(out var modelId) &&
                evidence.RootElement.TryGetProperty("Version", out var version) && version.TryGetInt32(out var number))
                model = models.SingleOrDefault(x => x.Id == modelId && x.Version == number && r.Model == $"{x.ModelType}:v{x.Version}");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        var parts = allocations.Where(x => x.MarketingAttributionResultId == r.Id).Select(x =>
            new MarketingAttributedSource(x.MarketingAttributionTouchId, x.Weight, x.AttributedValue, x.EvidenceVersion)).ToArray();
        var reconciles = r.AttributedValue >= 0 && parts.Length > 0 && parts.Select(x => x.TouchId).Distinct().Count() == parts.Length &&
            parts.All(x => costs.Any(c => c.Id == x.TouchId && c.Included && c.SubjectType == r.SubjectType && c.SubjectId == r.SubjectId &&
                c.OccurredUtc >= Utc(r.PeriodStartUtc) && c.OccurredUtc <= Utc(r.PeriodEndUtc)) && x.Weight is >= 0 and <= 1 && x.Value >= 0 &&
                Math.Abs(x.Value - decimal.Round(r.AttributedValue * x.Weight, 4)) <= .0001m) &&
            Math.Abs(parts.Sum(x => x.Weight) - 1m) <= .0000001m && Math.Abs(parts.Sum(x => x.Value) - r.AttributedValue) <= .0001m * parts.Length;
        var included = model != null && selectedModel == model.Id && reconciles && r.AttributedValue >= 0;
        return new(r.Id, r.SubjectType, r.SubjectId, r.Model, model?.Id, model?.Version, model?.LookbackDays,
            model?.Limitations ?? "Captured model identity/version unavailable.", r.Classification, r.AttributedValue, r.Unit,
            Utc(r.PeriodStartUtc), Utc(r.PeriodEndUtc), included, model == null ? "Model identity/version unavailable; excluded." :
            selectedModel != model.Id ? "Choose this explicit model version to include its outcomes." : !reconciles ?
            "Allocation/source coverage fails reconciliation; excluded." : "Recorded attribution; not causal experiment evidence.", parts);
    }
}
