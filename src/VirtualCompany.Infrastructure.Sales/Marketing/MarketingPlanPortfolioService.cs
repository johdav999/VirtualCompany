using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class MarketingOperationsService
{
    public async Task<MarketingPlanListItemDto[]> ListPlanPortfolioAsync(Guid companyId, CancellationToken ct)
    {
        RequireCompany(companyId);
        var plans = await _db.MarketingPlans.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.StartsUtc).Take(200).ToArrayAsync(ct);
        if (plans.Length == 0) return [];

        var planIds = plans.Select(x => x.Id).ToArray();
        var strategyIds = plans.Where(x => x.MarketingStrategyId.HasValue).Select(x => x.MarketingStrategyId!.Value).Distinct().ToArray();
        var strategies = await _db.MarketingStrategies.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && strategyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var objectiveLinks = await _db.MarketingPlanObjectives.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && planIds.Contains(x.MarketingPlanId)).ToArrayAsync(ct);
        var segmentLinks = await _db.MarketingPlanSegments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && planIds.Contains(x.MarketingPlanId)).ToArrayAsync(ct);
        var campaignLinks = await _db.MarketingPlanCampaigns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && planIds.Contains(x.MarketingPlanId)).ToArrayAsync(ct);
        var campaignLinkIds = campaignLinks.Select(x => x.Id).ToArray();
        var campaignSegments = await _db.MarketingPlanCampaignSegments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && campaignLinkIds.Contains(x.MarketingPlanCampaignId)).ToArrayAsync(ct);
        var segmentVersionIds = segmentLinks.Select(x => x.MarketingCustomerSegmentVersionId).Distinct().ToArray();
        var segmentVersions = await _db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && segmentVersionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);

        return plans.Select(plan =>
        {
            strategies.TryGetValue(plan.MarketingStrategyId ?? Guid.Empty, out var strategy);
            var objectives = objectiveLinks.Where(x => x.MarketingPlanId == plan.Id).Select(x => x.MarketingObjectiveId).ToArray();
            var segments = segmentLinks.Where(x => x.MarketingPlanId == plan.Id).ToArray();
            var campaigns = campaignLinks.Where(x => x.MarketingPlanId == plan.Id).ToArray();
            var linkedSegmentIds = campaignSegments.Where(x => campaigns.Any(c => c.Id == x.MarketingPlanCampaignId))
                .Select(x => x.MarketingPlanSegmentId).ToHashSet();
            var allocated = campaigns.Sum(x => x.AllocatedBudget ?? 0m);
            string? attention = null;
            if (strategy is not null && strategy.Version != plan.MarketingStrategyVersion) attention = "The linked strategy changed. Review this plan.";
            else if (segments.Any(x => segmentVersions.TryGetValue(x.MarketingCustomerSegmentVersionId, out var version) && version.Status == MarketingStrategicStatuses.Superseded)) attention = "A linked audience version was superseded.";
            else if (objectives.Any(id => campaigns.All(c => c.MarketingObjectiveId != id))) attention = "An objective has no campaign contribution yet.";
            else if (segments.Any(x => !linkedSegmentIds.Contains(x.Id))) attention = "A target segment has no campaign yet.";
            else if (plan.PlannedBudget.HasValue && allocated > plan.PlannedBudget) attention = "Campaign allocations exceed the plan budget.";
            return new MarketingPlanListItemDto(plan.Id, plan.Name, strategy?.Title, plan.MarketingStrategyVersion,
                plan.StartsUtc, plan.EndsUtc, plan.PlannedBudget, allocated,
                plan.PlannedBudget.HasValue ? plan.PlannedBudget - allocated : null, plan.BudgetCurrency,
                objectives.Length, segments.Length, campaigns.Length, attention is null ? "Ready" : "Needs attention",
                FriendlyStatus(plan.Status), plan.OwnerAgentId, plan.Version, attention);
        }).ToArray();
    }

    public async Task<MarketingPlanDetailDto?> GetPlanPortfolioAsync(Guid companyId, Guid planId, CancellationToken ct)
    {
        RequireCompany(companyId);
        var plan = await _db.MarketingPlans.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == planId, ct);
        if (plan is null) return null;
        var strategy = plan.MarketingStrategyId.HasValue
            ? await _db.MarketingStrategies.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == plan.MarketingStrategyId, ct) : null;
        var objectiveIds = await _db.MarketingPlanObjectives.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.MarketingPlanId == planId).Select(x => x.MarketingObjectiveId).ToArrayAsync(ct);
        var objectives = await _db.MarketingObjectives.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && objectiveIds.Contains(x.Id)).OrderBy(x => x.PeriodEndUtc)
            .Select(x => new MarketingPlanObjectiveSummaryDto(x.Id, x.Name, x.Status, x.PeriodStartUtc, x.PeriodEndUtc)).ToArrayAsync(ct);
        var planSegments = await _db.MarketingPlanSegments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.MarketingPlanId == planId).OrderBy(x => x.Priority).ToArrayAsync(ct);
        var versionIds = planSegments.Select(x => x.MarketingCustomerSegmentVersionId).ToArray();
        var versions = await _db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && versionIds.Contains(x.Id)).ToArrayAsync(ct);
        var segmentIds = versions.Select(x => x.MarketingCustomerSegmentId).ToArray();
        var names = await _db.MarketingCustomerSegments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && segmentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var segments = planSegments.Select(x => { var v = versions.Single(y => y.Id == x.MarketingCustomerSegmentVersionId); return new MarketingPlanSegmentDto(x.Id, v.Id, v.VersionNumber, names.GetValueOrDefault(v.MarketingCustomerSegmentId, "Audience segment"), x.Role, x.Priority, x.Rationale, x.ExpectedContribution, v.Status); }).ToArray();
        var links = await _db.MarketingPlanCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.MarketingPlanId == planId).OrderBy(x => x.Priority).ToArrayAsync(ct);
        var campaignIds = links.Select(x => x.SalesCampaignId).ToArray();
        var campaigns = await _db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && campaignIds.Contains(x.Id)).ToArrayAsync(ct);
        var linkIds = links.Select(x => x.Id).ToArray();
        var campaignSegments = await _db.MarketingPlanCampaignSegments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && linkIds.Contains(x.MarketingPlanCampaignId)).ToArrayAsync(ct);
        var campaignDtos = links.Select(x => { var c = campaigns.Single(y => y.Id == x.SalesCampaignId); var segIds = campaignSegments.Where(y => y.MarketingPlanCampaignId == x.Id).Join(planSegments, y => y.MarketingPlanSegmentId, y => y.Id, (_, p) => p.MarketingCustomerSegmentVersionId).ToArray(); return new MarketingPlanCampaignDto(x.Id, c.Id, c.Name, x.Purpose, x.MarketingObjectiveId, x.ExpectedContribution, segIds, x.AllocatedBudget, x.BudgetCurrency, x.Priority, x.Status, c.LifecycleStatus, c.PlanningStartsUtc, c.ScheduledLaunchUtc, c.ReviewDueUtc, c.EndsUtc, c.OwnerAgentId, c.ReadinessGaps()); }).ToArray();
        var findings = Coverage(objectiveIds, versionIds, campaignDtos, plan.PlannedBudget, plan.BudgetCurrency);
        if (strategy is not null && plan.MarketingStrategyVersion != strategy.Version)
            findings = findings.Append(new MarketingCoverageFindingDto("strategy_version_changed", "Strategy changed", "Review this plan against the current strategy version. Existing links were preserved.", "attention")).ToArray();
        foreach (var segment in segments.Where(x => x.Status == MarketingStrategicStatuses.Superseded))
            findings = findings.Append(new MarketingCoverageFindingDto("segment_superseded", "Audience version changed", $"{segment.SegmentName} version {segment.SegmentVersionNumber} was superseded. Review impact before activation.", "attention", SegmentVersionId: segment.SegmentVersionId)).ToArray();
        var allocated = links.Sum(x => x.AllocatedBudget ?? 0m);
        var attention = findings.FirstOrDefault(x => x.Severity != "info")?.Explanation;
        var allowed = new List<string>();
        if (plan.Status == MarketingStatuses.Draft) allowed.Add("Submit for review");
        if (plan.Status == MarketingStatuses.Approved) allowed.Add("Activate plan");
        if (campaignDtos.Length > 0) allowed.Add("Open linked campaigns");
        allowed.Add("Review impact");
        var summary = new MarketingPlanListItemDto(plan.Id, plan.Name, strategy?.Title, plan.MarketingStrategyVersion, plan.StartsUtc, plan.EndsUtc,
            plan.PlannedBudget, allocated, plan.PlannedBudget.HasValue ? plan.PlannedBudget - allocated : null, plan.BudgetCurrency,
            objectives.Length, segments.Length, campaignDtos.Length, findings.Count == 0 ? "Ready" : "Needs attention", FriendlyStatus(plan.Status),
            plan.OwnerAgentId, plan.Version, attention);
        return new MarketingPlanDetailDto(summary, plan.Summary, plan.Rationale, ParseList(plan.EvidenceReferencesJson), ParseList(plan.MissingEvidenceJson),
            objectives, segments, campaignDtos, findings, plan.ApprovalRequestId, allowed, strategy is not null);
    }

    public async Task<MarketingPolicyDecisionDto> AssessPlanReadinessAsync(Guid companyId, CreateGroundedMarketingPlanRequest r, CancellationToken ct)
    {
        RequireCompany(companyId);
        var strategy = await _db.MarketingStrategies.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == r.StrategyId, ct);
        if (strategy is null) return Deny(MarketingPlanReadinessReasons.StrategyMissing, "Choose an approved Marketing strategy.");
        if (strategy.Version != r.ExpectedStrategyVersion) return Deny(MarketingPlanReadinessReasons.StrategyStale, "The strategy changed. Refresh the proposal.");
        if (strategy.Status is not (MarketingStrategicStatuses.Approved or MarketingStrategicStatuses.Active) || strategy.ValidFromUtc > r.StartsUtc || strategy.ValidToUtc < r.EndsUtc)
            return Deny(MarketingPlanReadinessReasons.StrategyUnavailable, "The strategy is not approved and valid for the full plan period.");
        if (r.EndsUtc <= r.StartsUtc || r.PlannedBudget is < 0 || r.BudgetCurrency.Trim().Length != 3) return Deny("plan_values_invalid", "Check the plan period, budget, and currency.");
        if (r.Segments.Count == 0 || !r.Segments.Any(x => x.Role == MarketingPlanSegmentRoles.Primary)) return Deny(MarketingPlanReadinessReasons.PrimarySegmentMissing, "Choose at least one primary target segment.");
        if (r.Segments.Select(x => x.SegmentVersionId).Distinct().Count() != r.Segments.Count) return Deny(MarketingPlanReadinessReasons.SegmentUnavailable, "Each target segment can appear only once.");
        var linked = await _db.MarketingStrategySegments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.MarketingStrategyId == r.StrategyId).Select(x => x.MarketingCustomerSegmentVersionId).ToArrayAsync(ct);
        if (r.Segments.Any(x => !linked.Contains(x.SegmentVersionId))) return Deny(MarketingPlanReadinessReasons.SegmentUnavailable, "Every target segment must be linked to the selected strategy.");
        var validSegmentCount = await _db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking().CountAsync(x => x.CompanyId == companyId && r.Segments.Select(s => s.SegmentVersionId).Contains(x.Id) && (x.Status == MarketingStrategicStatuses.Approved || x.Status == MarketingStrategicStatuses.Active), ct);
        if (validSegmentCount != r.Segments.Count) return Deny(MarketingPlanReadinessReasons.SegmentUnavailable, "A target segment is no longer approved or active.");
        var objectives = await _db.MarketingObjectives.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && r.ObjectiveIds.Contains(x.Id)).ToArrayAsync(ct);
        if (objectives.Length != r.ObjectiveIds.Distinct().Count()) return Deny(MarketingPlanReadinessReasons.ObjectiveMissing, "A selected objective is unavailable.");
        if (objectives.Any(x => x.Status != MarketingStatuses.Active || x.PeriodStartUtc >= r.EndsUtc || x.PeriodEndUtc <= r.StartsUtc)) return Deny(MarketingPlanReadinessReasons.ObjectiveOutsidePeriod, "Objectives must be active and overlap the plan period.");
        if (r.EvidenceReferences.Count == 0 || r.MissingEvidence.Count > 0) return new(false, MarketingPlanReadinessReasons.EvidenceMissing, "Add the missing planning evidence before activation.", true, r.EvidenceReferences);
        return new(true, MarketingPlanReadinessReasons.Ready, "The plan is grounded and ready to be created as a draft.", true, r.EvidenceReferences);
    }

    public async Task<MarketingPlanDetailDto> CreateGroundedPlanAsync(Guid companyId, Guid userId, CreateGroundedMarketingPlanRequest r, CancellationToken ct)
    {
        var existingId = await _db.MarketingPlans.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.IdempotencyKey == r.IdempotencyKey).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (existingId.HasValue) return (await GetPlanPortfolioAsync(companyId, existingId.Value, ct))!;
        var decision = await AssessPlanReadinessAsync(companyId, r, ct);
        if (!decision.Allowed) throw new InvalidOperationException($"{decision.ReasonCode}: {decision.Explanation}");
        await using var tx = _db.Database.CurrentTransaction is null ? await _db.Database.BeginTransactionAsync(ct) : null;
        var plan = new MarketingPlan(Guid.NewGuid(), companyId, r.Name, r.Summary, r.StartsUtc, r.EndsUtc, r.PlannedBudget,
            r.BudgetCurrency, userId, r.OwnerAgentId, r.IdempotencyKey, r.StrategyId, r.ExpectedStrategyVersion, r.Rationale,
            JsonSerializer.Serialize(r.EvidenceReferences), JsonSerializer.Serialize(r.MissingEvidence));
        _db.MarketingPlans.Add(plan);
        foreach (var objectiveId in r.ObjectiveIds.Distinct()) _db.MarketingPlanObjectives.Add(new MarketingPlanObjective(Guid.NewGuid(), companyId, plan.Id, objectiveId));
        foreach (var segment in r.Segments) _db.MarketingPlanSegments.Add(new MarketingPlanSegment(Guid.NewGuid(), companyId, plan.Id, segment.SegmentVersionId, segment.Role, segment.Priority, segment.Rationale, segment.ExpectedContribution));
        AddPlanAudit(companyId, userId, "marketing.plan.created", plan.Id, "A strategy-grounded Marketing plan draft was created.", r.EvidenceReferences,
            new Dictionary<string, string?> { ["strategyId"] = r.StrategyId.ToString("D"), ["strategyVersion"] = r.ExpectedStrategyVersion.ToString(), ["planVersion"] = plan.Version.ToString(), ["idempotencyKey"] = r.IdempotencyKey },
            JsonSerializer.Serialize(new { objectives = r.ObjectiveIds, segments = r.Segments.Select(x => new { x.SegmentVersionId, x.Role, x.Priority }), r.PlannedBudget, r.BudgetCurrency }), r.OwnerAgentId == userId);
        await _db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return (await GetPlanPortfolioAsync(companyId, plan.Id, ct))!;
    }

    public async Task<MarketingCampaignPortfolioProposalDto> PrepareCampaignPortfolioAsync(Guid companyId, PrepareMarketingCampaignPortfolioRequest r, CancellationToken ct)
    {
        var plan = await GetPlanPortfolioAsync(companyId, r.PlanId, ct) ?? throw new KeyNotFoundException("Marketing plan not found.");
        var findings = new List<MarketingCoverageFindingDto>();
        if (plan.Summary.Version != r.ExpectedPlanVersion) findings.Add(Finding(MarketingPlanReadinessReasons.StaleVersion, "Plan changed", "Refresh before committing campaign drafts."));
        if (plan.Summary.StatusLabel is "Completed" or "Cancelled" or "Waiting for approval") findings.Add(Finding("plan_status_unavailable", "Plan is not editable", "Campaign drafts cannot be added in the plan's current state."));
        foreach (var c in r.Campaigns)
        {
            if (!plan.Objectives.Any(x => x.Id == c.ObjectiveId)) findings.Add(Finding(MarketingPlanReadinessReasons.ObjectiveMissing, "Objective unavailable", $"{c.Name} refers to an objective outside this plan."));
            if (c.SegmentVersionIds.Any(id => !plan.Segments.Any(x => x.SegmentVersionId == id))) findings.Add(Finding(MarketingPlanReadinessReasons.SegmentUnavailable, "Audience outside plan", $"{c.Name} targets a segment outside this plan."));
            if (c.PlanningStartsUtc < plan.Summary.StartsUtc || c.EndsUtc > plan.Summary.EndsUtc) findings.Add(Finding(MarketingPlanReadinessReasons.CampaignOutsidePlan, "Dates outside plan", $"{c.Name} must fit within the plan period."));
            if (!string.Equals(c.BudgetCurrency, plan.Summary.BudgetCurrency, StringComparison.OrdinalIgnoreCase)) findings.Add(Finding(MarketingPlanReadinessReasons.CurrencyMismatch, "Currency differs", $"{c.Name} must use the plan currency."));
            if (string.IsNullOrWhiteSpace(c.OfferBasis) || c.Activities.Count == 0 || c.ContentNeeds.Count == 0 || c.Channels.Count == 0 ||
                string.IsNullOrWhiteSpace(c.AudienceApproach) || string.IsNullOrWhiteSpace(c.MeasurementApproach) ||
                c.EvidenceReferences.Count == 0 || c.MissingEvidence.Count > 0)
                findings.Add(Finding(MarketingPlanReadinessReasons.EvidenceMissing, "Campaign details incomplete", $"{c.Name} needs offer, activity, content, audience, measurement, and evidence details."));
            if (plan.Campaigns.Any(existing => existing.Purpose.Equals(c.Purpose, StringComparison.OrdinalIgnoreCase) &&
                existing.ObjectiveId == c.ObjectiveId && existing.SegmentVersionIds.Order().SequenceEqual(c.SegmentVersionIds.Order()) &&
                existing.PlanningStartsUtc < c.EndsUtc && existing.EndsUtc > c.PlanningStartsUtc))
                findings.Add(Finding(MarketingPlanReadinessReasons.DuplicateCampaign, "Equivalent campaign exists", $"{c.Name} overlaps an equivalent campaign already in this plan."));
        }
        var allocation = plan.Summary.AllocatedBudget + r.Campaigns.Sum(x => x.AllocatedBudget ?? 0m);
        if (plan.Summary.PlannedBudget.HasValue && allocation > plan.Summary.PlannedBudget) findings.Add(Finding(MarketingPlanReadinessReasons.BudgetExceeded, "Budget overallocated", "Campaign allocations exceed the plan budget."));
        var duplicateKeys = r.Campaigns.GroupBy(x => $"{x.Purpose.Trim().ToLowerInvariant()}|{x.ObjectiveId}|{string.Join(',', x.SegmentVersionIds.Order())}").Where(x => x.Count() > 1).ToArray();
        if (duplicateKeys.Length > 0) findings.Add(Finding(MarketingPlanReadinessReasons.DuplicateCampaign, "Duplicate purpose", "Equivalent campaign purposes and audiences appear more than once."));
        for (var i = 0; i < r.Campaigns.Count; i++)
        for (var j = i + 1; j < r.Campaigns.Count; j++)
            if (r.Campaigns[i].Channels.Intersect(r.Campaigns[j].Channels, StringComparer.OrdinalIgnoreCase).Any() &&
                Math.Abs((r.Campaigns[i].LaunchUtc - r.Campaigns[j].LaunchUtc).TotalHours) < 24)
                findings.Add(Finding("channel_schedule_conflict", "Channel schedule conflict", $"{r.Campaigns[i].Name} and {r.Campaigns[j].Name} use the same channel within one day."));
        var decision = findings.Count == 0 ? new MarketingPolicyDecisionDto(true, MarketingPlanReadinessReasons.Ready, "The campaign portfolio is ready to create as drafts.", false, r.Campaigns.SelectMany(x => x.EvidenceReferences).Distinct().ToArray()) : Deny(findings[0].Code, findings[0].Explanation);
        return new MarketingCampaignPortfolioProposalDto(Fingerprint($"{companyId:N}|{r.PlanId:N}|{r.ExpectedPlanVersion}|{r.IdempotencyKey}"), r.PlanId, r.ExpectedPlanVersion, decision, findings, r.Campaigns);
    }

    public async Task<MarketingCampaignPortfolioResultDto> CommitCampaignPortfolioAsync(Guid companyId, Guid userId, CommitMarketingCampaignPortfolioRequest command, CancellationToken ct)
    {
        var r = command.Portfolio;
        var existing = await _db.MarketingPlanCampaigns.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.MarketingPlanId == r.PlanId && x.IdempotencyKey.StartsWith(r.IdempotencyKey), ct);
        if (existing) { var current = await GetPlanPortfolioAsync(companyId, r.PlanId, ct) ?? throw new KeyNotFoundException(); return new(r.PlanId, current.Summary.Version, current.Campaigns, true, "Campaign drafts already exist."); }
        var proposal = await PrepareCampaignPortfolioAsync(companyId, r, ct);
        if (!proposal.Decision.Allowed) throw new InvalidOperationException($"{proposal.Decision.ReasonCode}: {proposal.Decision.Explanation}");
        if (_campaignDrafts is null) throw new InvalidOperationException("Campaign draft creation is unavailable.");
        var planSegments = await _db.MarketingPlanSegments.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.MarketingPlanId == r.PlanId).ToArrayAsync(ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        for (var index = 0; index < r.Campaigns.Count; index++)
        {
            var c = r.Campaigns[index]; var key = $"{r.IdempotencyKey}:{index + 1}";
            var activityCommands = c.Activities.Select((name, i) => new SalesCampaignDraftActivityCommand(name,
                "marketing", c.Channels.FirstOrDefault() ?? "internal", c.PlanningStartsUtc.AddDays(i), c.LaunchUtc, c.TimeZoneId)).ToArray();
            var draft = await _campaignDrafts.CreateDraftAsync(new CreateSalesCampaignDraftCommand(companyId, userId, r.AgentId, c.Name, c.Purpose, c.CampaignType, c.AudienceType,
                "marketing_objective", c.ObjectiveTarget, c.ObjectiveUnit, c.ObjectiveTargetUtc, c.PlanningStartsUtc, c.LaunchUtc, c.ReviewUtc, c.EndsUtc, c.TimeZoneId,
                c.AllocatedBudget, c.BudgetCurrency, c.CommunicationLanguage, key, c.OfferBasis ?? "Planning basis", "marketing_plan",
                $"marketing-plan:{r.PlanId:D}:v{r.ExpectedPlanVersion}", Activities: activityCommands), ct);
            var link = new MarketingPlanCampaign(Guid.NewGuid(), companyId, r.PlanId, draft.CampaignId, c.Purpose, c.AllocatedBudget, c.BudgetCurrency, c.Priority, c.ObjectiveContribution, r.AgentId, key, c.ObjectiveId);
            _db.MarketingPlanCampaigns.Add(link);
            var actorType = r.AgentId == userId ? AuditActorTypes.Agent : AuditActorTypes.Human;
            _db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, actorType, userId, "marketing.campaign_draft.created",
                "sales_campaign", draft.CampaignId.ToString("D"), AuditEventOutcomes.Succeeded,
                "A governed campaign draft was created from the plan portfolio; launch remains blocked by Sales readiness.", c.EvidenceReferences,
                new Dictionary<string, string?> { ["planId"] = r.PlanId.ToString("D"), ["planVersion"] = r.ExpectedPlanVersion.ToString(), ["objectiveId"] = c.ObjectiveId.ToString("D"), ["idempotencyKey"] = key }));
            _db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, actorType, userId, "marketing.plan_campaign.linked",
                "marketing_plan_campaign", link.Id.ToString("D"), AuditEventOutcomes.Succeeded,
                "The campaign draft was linked to its owning plan and exact objective/segment context.", c.EvidenceReferences,
                new Dictionary<string, string?> { ["planId"] = r.PlanId.ToString("D"), ["campaignId"] = draft.CampaignId.ToString("D"), ["segmentVersionIds"] = string.Join(',', c.SegmentVersionIds) }));
            foreach (var segmentVersionId in c.SegmentVersionIds)
            {
                var planSegment = planSegments.Single(x => x.MarketingCustomerSegmentVersionId == segmentVersionId);
                _db.MarketingPlanCampaignSegments.Add(new MarketingPlanCampaignSegment(Guid.NewGuid(), companyId, link.Id, planSegment.Id, "Selected by the approved plan portfolio.", c.AudienceApproach));
            }
            foreach (var content in c.ContentNeeds.Select((name, i) => new { name, i }))
                _db.MarketingContentBriefs.Add(new MarketingContentBrief(Guid.NewGuid(), companyId, content.name, c.Purpose, c.AudienceApproach, c.Channels.FirstOrDefault() ?? "internal", c.CommunicationLanguage ?? "en", "professional", "Review campaign", draft.CampaignId, r.PlanId, c.LaunchUtc.AddDays(-Math.Max(1, content.i + 1)), userId, r.AgentId, c.SegmentVersionIds.FirstOrDefault(), c.ObjectiveContribution, evidenceRequirementsJson: JsonSerializer.Serialize(c.EvidenceReferences), approvalPolicyJson: JsonSerializer.Serialize(new { planId = r.PlanId, planVersion = r.ExpectedPlanVersion })));
            if (c.TaskNeeds is { Count: > 0 })
            {
                if (_tasks is null) throw new InvalidOperationException("Internal task creation is unavailable.");
                foreach (var taskNeed in c.TaskNeeds)
                    await _tasks.CreateTaskAsync(companyId, new CreateTaskCommand("marketing_campaign_preparation", taskNeed,
                        $"Prepare governed internal work for {c.Name}. No launch or external delivery is authorized.", "normal", c.LaunchUtc,
                        r.AgentId, new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
                        {
                            ["planId"] = System.Text.Json.Nodes.JsonValue.Create(r.PlanId),
                            ["campaignId"] = System.Text.Json.Nodes.JsonValue.Create(draft.CampaignId),
                            ["idempotencyKey"] = System.Text.Json.Nodes.JsonValue.Create($"{key}:task:{Fingerprint(taskNeed)}")
                        }, RationaleSummary: c.Purpose, CorrelationId: key), ct);
            }
        }
        AddPlanAudit(companyId, userId, "marketing.plan.campaign_portfolio_created", r.PlanId,
            $"{r.Campaigns.Count} governed Sales campaign draft(s) were created without launch or enrollment.", r.Campaigns.SelectMany(x => x.EvidenceReferences),
            new Dictionary<string, string?> { ["planVersion"] = r.ExpectedPlanVersion.ToString(), ["campaignCount"] = r.Campaigns.Count.ToString(), ["idempotencyKey"] = r.IdempotencyKey, ["agentId"] = r.AgentId?.ToString("D") },
            JsonSerializer.Serialize(r.Campaigns.Select(x => new { x.Name, x.ObjectiveId, x.SegmentVersionIds, x.AllocatedBudget, x.BudgetCurrency })), r.AgentId == userId);
        await _db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        var detail = await GetPlanPortfolioAsync(companyId, r.PlanId, ct) ?? throw new InvalidOperationException("Plan could not be reloaded.");
        return new(r.PlanId, detail.Summary.Version, detail.Campaigns, false, "Campaign drafts created. Launch controls remain unchanged.");
    }

    public async Task<MarketingDailyReviewDto?> GetDailyReviewAsync(Guid companyId, DateTime dateUtc, CancellationToken ct)
    {
        var start = DateTime.SpecifyKind(dateUtc.Date, DateTimeKind.Utc); var end = start.AddDays(1);
        var run = await _db.MarketingOperatingRuns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.CreatedUtc >= start && x.CreatedUtc < end)
            .OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(ct);
        if (run is null) return null;
        var needs = ReadDailyNeeds(run.SelectedWorkJson, run.EvidenceJson);
        var checkedEvidence = ReadCheckedEvidence(run.EvidenceJson);
        var actions = await _db.MarketingOperatingActions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.MarketingOperatingRunId == run.Id).OrderBy(x => x.Sequence).Select(x => x.Title).ToArrayAsync(ct);
        var noWork = run.OutcomeSummary?.Contains("no work", StringComparison.OrdinalIgnoreCase) == true;
        return new(run.Id, start, noWork ? "No work needed today" : run.Status == "blocked" ? "Needs attention" : "Marketing review complete",
            run.OutcomeSummary ?? "Maya completed the daily Marketing review.", checkedEvidence, needs, actions,
            run.RecoveryCode is null ? [] : [run.RecoveryCode], run.Status == "blocked" ? "Review the blocker and refresh the evidence." : null);
    }

    public async Task<MarketingPlanDetailDto?> SubmitPlanForReviewAsync(Guid companyId, Guid userId, Guid planId, int expectedVersion, CancellationToken ct)
    {
        var plan = await _db.MarketingPlans.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == planId, ct);
        if (plan is null) return null;
        if (plan.Version != expectedVersion) throw new InvalidOperationException("stale_version: The plan changed. Refresh and try again.");
        if (_approvals is null) throw new InvalidOperationException("Approval workflow is unavailable.");
        var detail = await GetPlanPortfolioAsync(companyId, planId, ct) ?? throw new InvalidOperationException("Plan could not be assessed.");
        if (detail.MissingEvidence.Count > 0 || detail.Objectives.Count == 0 || detail.Segments.Count == 0)
            throw new InvalidOperationException("evidence_missing: Resolve plan evidence, objective, and audience gaps before review.");
        var approval = await _approvals.CreateAsync(companyId, new CreateApprovalRequestCommand("marketing_plan", plan.Id,
            "user", userId, "marketing_plan_activation", null, "company_manager"), ct);
        plan.SubmitForReview(approval.Id);
        AddPlanAudit(companyId, userId, "marketing.plan.submitted_for_review", plan.Id, "The exact plan version was submitted for approval.", detail.EvidenceReferences,
            new Dictionary<string, string?> { ["planVersion"] = expectedVersion.ToString(), ["approvalRequestId"] = approval.Id.ToString("D") });
        await _db.SaveChangesAsync(ct);
        return await GetPlanPortfolioAsync(companyId, planId, ct);
    }

    public async Task<MarketingPlanDetailDto?> ActivateGroundedPlanAsync(Guid companyId, Guid userId, Guid planId, int expectedVersion, CancellationToken ct)
    {
        var plan = await _db.MarketingPlans.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == planId, ct);
        if (plan is null) return null;
        if (plan.Version != expectedVersion) throw new InvalidOperationException("stale_version: The plan changed. Refresh and try again.");
        if (!plan.ApprovalRequestId.HasValue || _approvals is null) throw new InvalidOperationException("approval_required: Plan approval is required.");
        var approval = await _approvals.GetAsync(companyId, plan.ApprovalRequestId.Value, ct);
        if (!string.Equals(approval.Status, "approved", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("approval_required: Plan approval is not complete.");
        if (plan.Status == MarketingStatuses.InReview) plan.MarkApproved();
        var strategy = await _db.MarketingStrategies.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == plan.MarketingStrategyId, ct);
        if (strategy is null || strategy.Version != plan.MarketingStrategyVersion || strategy.Status is not (MarketingStrategicStatuses.Approved or MarketingStrategicStatuses.Active))
            throw new InvalidOperationException("strategy_stale: Review the plan impact against the current strategy before activation.");
        plan.Activate();
        AddPlanAudit(companyId, userId, "marketing.plan.activated", plan.Id, "The approved plan was activated after strategy-version revalidation.", ParseList(plan.EvidenceReferencesJson),
            new Dictionary<string, string?> { ["approvedPlanVersion"] = expectedVersion.ToString(), ["strategyId"] = plan.MarketingStrategyId?.ToString("D"), ["strategyVersion"] = plan.MarketingStrategyVersion?.ToString(), ["approvalRequestId"] = plan.ApprovalRequestId?.ToString("D") });
        await _db.SaveChangesAsync(ct);
        return await GetPlanPortfolioAsync(companyId, planId, ct);
    }

    public async Task<MarketingPlanDetailDto?> CompletePlanAsync(Guid companyId, Guid planId, TransitionMarketingPlanRequest request, CancellationToken ct)
    {
        var plan = await _db.MarketingPlans.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == planId, ct);
        if (plan is null) return null; if (plan.Version != request.ExpectedVersion) throw new InvalidOperationException("stale_version: The plan changed.");
        if (string.IsNullOrWhiteSpace(request.Rationale)) throw new ArgumentException("A completion rationale is required.");
        plan.Complete();
        AddSystemPlanAudit(companyId, "marketing.plan.completed", plan.Id, request.Rationale, plan.Version);
        await _db.SaveChangesAsync(ct); return await GetPlanPortfolioAsync(companyId, planId, ct);
    }

    public async Task<MarketingPlanDetailDto?> CancelPlanAsync(Guid companyId, Guid planId, TransitionMarketingPlanRequest request, CancellationToken ct)
    {
        var plan = await _db.MarketingPlans.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == planId, ct);
        if (plan is null) return null; if (plan.Version != request.ExpectedVersion) throw new InvalidOperationException("stale_version: The plan changed.");
        if (string.IsNullOrWhiteSpace(request.Rationale)) throw new ArgumentException("A cancellation rationale is required.");
        plan.Cancel();
        AddSystemPlanAudit(companyId, "marketing.plan.cancelled", plan.Id, request.Rationale, plan.Version);
        await _db.SaveChangesAsync(ct); return await GetPlanPortfolioAsync(companyId, planId, ct);
    }

    private void AddPlanAudit(Guid companyId, Guid userId, string action, Guid planId, string rationale,
        IEnumerable<string>? sources = null, IReadOnlyDictionary<string, string?>? metadata = null, string? payloadDiffJson = null,
        bool actorIsAgent = false) =>
        _db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, actorIsAgent ? AuditActorTypes.Agent : AuditActorTypes.Human, userId, action,
            "marketing_plan", planId.ToString("D"), AuditEventOutcomes.Succeeded, rationale, sources, metadata,
            payloadDiffJson: payloadDiffJson));

    private void AddSystemPlanAudit(Guid companyId, string action, Guid planId, string rationale, int planVersion) =>
        _db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.System, null, action,
            "marketing_plan", planId.ToString("D"), AuditEventOutcomes.Succeeded, rationale,
            metadata: new Dictionary<string, string?> { ["planVersion"] = planVersion.ToString() }));

    private static MarketingPolicyDecisionDto Deny(string code, string explanation) => new(false, code, explanation, false, []);
    private static MarketingCoverageFindingDto Finding(string code, string label, string explanation) => new(code, label, explanation, "attention");
    private static IReadOnlyList<MarketingCoverageFindingDto> Coverage(Guid[] objectives, Guid[] segments, MarketingPlanCampaignDto[] campaigns, decimal? budget, string currency)
    {
        var result = new List<MarketingCoverageFindingDto>();
        foreach (var id in objectives.Where(id => campaigns.All(c => c.ObjectiveId != id))) result.Add(new("objective_without_campaign", "Objective not covered", "An objective has no campaign contribution yet.", "attention", ObjectiveId: id));
        foreach (var id in segments.Where(id => campaigns.All(c => !c.SegmentVersionIds.Contains(id)))) result.Add(new("segment_without_campaign", "Audience not covered", "A target segment has no campaign yet.", "attention", SegmentVersionId: id));
        if (budget.HasValue && campaigns.Sum(x => x.AllocatedBudget ?? 0m) > budget) result.Add(Finding(MarketingPlanReadinessReasons.BudgetExceeded, "Budget overallocated", $"Campaign allocations exceed the {currency} plan budget."));
        return result;
    }
    private static string FriendlyStatus(string status) => status switch { "in_review" => "Waiting for approval", "draft" => "Draft", "approved" => "Approved", "active" => "Active", "completed" => "Completed", "cancelled" => "Cancelled", _ => "Needs review" };
    private static IReadOnlyList<string> ParseList(string json) => TryDeserialize<string[]>(json) ?? [];
    private static IReadOnlyList<MarketingWorkNeedDto> ReadDailyNeeds(string selectedWorkJson, string evidenceJson)
    {
        var direct = TryDeserialize<MarketingWorkNeedDto[]>(selectedWorkJson);
        if (direct is { Length: > 0 }) return direct;
        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("workNeeds", out var needs)
                ? needs.Deserialize<MarketingWorkNeedDto[]>() ?? []
                : [];
        }
        catch (JsonException) { return []; }
    }
    private static IReadOnlyList<string> ReadCheckedEvidence(string evidenceJson)
    {
        var direct = TryDeserialize<string[]>(evidenceJson);
        if (direct is not null) return direct;
        try
        {
            using var document = JsonDocument.Parse(evidenceJson);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("checkedEvidence", out var checkedEvidence)
                ? checkedEvidence.Deserialize<string[]>() ?? []
                : [];
        }
        catch (JsonException) { return []; }
    }
    private static T? TryDeserialize<T>(string json) { try { return JsonSerializer.Deserialize<T>(json); } catch (JsonException) { return default; } }
    private static string Fingerprint(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
