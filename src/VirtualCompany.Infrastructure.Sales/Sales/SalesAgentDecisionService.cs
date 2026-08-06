using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesAgentDecisionService(
    VirtualCompanyDbContext db,
    ISalesAgentAnalysisService analysis,
    IConversionAnalyticsService conversionAnalytics,
    ICampaignPlanningService campaignPlanning,
    ICompanyKnowledgeSearchService knowledge) : ISalesAgentDecisionService
{
    public async Task<SalesIntelligenceBriefResult> BuildIntelligenceBriefAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, SalesIntelligenceBriefRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        if (request.LeadId.HasValue == request.DealId.HasValue)
            throw new ArgumentException("Exactly one lead or deal is required.", nameof(request));

        var now = Utc(request.AsOfUtc ?? DateTime.UtcNow);
        var facts = new List<SalesIntelligenceFactDto>();
        var gaps = new List<string>();
        var buyingSignals = new List<string>();
        var riskSignals = new List<string>();
        Guid subjectId;
        string subjectType;
        string title;

        if (request.LeadId is { } leadId)
        {
            var lead = await db.Leads.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == leadId && !x.IsDeleted, ct)
                ?? throw new KeyNotFoundException("Sales lead not found.");
            subjectId = lead.Id;
            subjectType = "lead";
            title = lead.Title;
            AddFact(facts, "Status", lead.Status, $"sales-lead:{lead.Id:N}", lead.UpdatedUtc);
            AddFact(facts, "Source", lead.Source ?? "unknown", $"sales-lead:{lead.Id:N}", lead.UpdatedUtc);
            AddFact(facts, "Estimated value", lead.EstimatedValue?.ToString(CultureInfo.InvariantCulture) ?? "unknown",
                $"sales-lead:{lead.Id:N}", lead.UpdatedUtc);
            if (lead.PrimaryContactId is null) gaps.Add("Primary contact and buying role are not confirmed.");
            if (lead.CustomerCompanyId is null) gaps.Add("Account identity is not confirmed.");
            if (string.IsNullOrWhiteSpace(lead.Fit)) gaps.Add("ICP fit has not been reviewed.");
            if (string.IsNullOrWhiteSpace(lead.Temperature)) gaps.Add("Buying timing is unknown.");
            if (lead.QualifiedUtc.HasValue) buyingSignals.Add("Lead was explicitly qualified.");
            if (lead.UpdatedUtc < now.AddDays(-7)) riskSignals.Add("Lead evidence is older than seven days.");
        }
        else
        {
            var dealId = request.DealId!.Value;
            var deal = await db.Deals.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == dealId && !x.IsDeleted, ct)
                ?? throw new KeyNotFoundException("Sales deal not found.");
            subjectId = deal.Id;
            subjectType = "deal";
            title = deal.Title;
            AddFact(facts, "Status", deal.Status, $"sales-deal:{deal.Id:N}", deal.UpdatedUtc);
            AddFact(facts, "Amount", $"{deal.Amount.ToString(CultureInfo.InvariantCulture)} {deal.Currency}",
                $"sales-deal:{deal.Id:N}", deal.UpdatedUtc);
            AddFact(facts, "Expected close", deal.ExpectedCloseUtc?.ToString("O") ?? "unknown",
                $"sales-deal:{deal.Id:N}", deal.UpdatedUtc);
            if (deal.PrimaryContactId is null) gaps.Add("Primary contact and authority are not confirmed.");
            if (deal.CustomerCompanyId is null) gaps.Add("Account identity is not confirmed.");
            if (deal.ExpectedCloseUtc is null) gaps.Add("Expected close date is missing.");
            if (deal.ExpectedCloseUtc < now) riskSignals.Add("Expected close date has passed.");
        }

        var activities = await db.SalesActivities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted &&
                        (x.LeadId == request.LeadId || x.DealId == request.DealId))
            .OrderByDescending(x => x.OccurredUtc).Take(10).ToListAsync(ct);
        foreach (var activity in activities)
            AddFact(facts, $"Activity: {activity.ActivityType}", activity.Summary,
                $"sales-activity:{activity.Id:N}", activity.OccurredUtc);
        if (activities.Count == 0) gaps.Add("No recorded activity supports engagement or timing.");
        else if (activities[0].OccurredUtc >= now.AddDays(-7)) buyingSignals.Add("Recent recorded sales activity exists.");

        var advice = await Analyze(companyId, agentId, actorUserId, SalesAgentAnalysisTypes.LeadIntelligence,
            subjectId, request.Objective, now, ct);
        return new SalesIntelligenceBriefResult(advice, subjectType, subjectId, title, facts, gaps, buyingSignals,
            riskSignals, facts.Select(x => x.SourceId).Distinct().ToArray(),
            advice.RequiresReview || gaps.Count > 0 || riskSignals.Count > 0);
    }

    public async Task<SalesNextBestActionResult> RecommendNextActionsAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, SalesNextBestActionRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var now = Utc(request.AsOfUtc ?? DateTime.UtcNow);
        var limit = Math.Clamp(request.Limit, 1, 100);
        var policy = await db.SalesAutomationPolicies.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, ct);
        var leadsQuery = db.Leads.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.Status != SalesStatuses.Rejected && x.Status != SalesStatuses.Converted);
        if (request.LeadId.HasValue) leadsQuery = leadsQuery.Where(x => x.Id == request.LeadId.Value);
        else if (request.DealId.HasValue) leadsQuery = leadsQuery.Where(_ => false);
        var dealsQuery = db.Deals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.Status != SalesStatuses.Won && x.Status != SalesStatuses.Lost);
        if (request.DealId.HasValue) dealsQuery = dealsQuery.Where(x => x.Id == request.DealId.Value);
        else if (request.LeadId.HasValue) dealsQuery = dealsQuery.Where(_ => false);
        var leads = await leadsQuery.OrderByDescending(x => x.UpdatedUtc).Take(100).ToListAsync(ct);
        var deals = await dealsQuery.OrderByDescending(x => x.UpdatedUtc).Take(100).ToListAsync(ct);
        if ((request.LeadId.HasValue || request.DealId.HasValue) && leads.Count + deals.Count == 0)
            throw new KeyNotFoundException("Sales subject not found.");

        var contactIds = leads.Select(x => x.PrimaryContactId).Concat(deals.Select(x => x.PrimaryContactId))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
        var permissions = await db.SalesContactPermissions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && contactIds.Contains(x.ContactId) && x.Channel == "email")
            .OrderByDescending(x => x.ObservedUtc).ToListAsync(ct);
        var allowedContacts = permissions.GroupBy(x => x.ContactId)
            .Where(x => IsAllowed(x.First().Status)).Select(x => x.Key).ToHashSet();
        var actions = new List<SalesNextBestActionItemDto>();

        foreach (var lead in leads)
        {
            var age = Math.Max(0, (now.Date - lead.UpdatedUtc.Date).Days);
            var qualified = lead.Status == SalesStatuses.Qualified;
            var communicationAllowed = lead.PrimaryContactId is { } contactId && allowedContacts.Contains(contactId);
            var reasons = new List<string> { qualified ? "qualified_lead" : "qualification_required" };
            if (age > 7) reasons.Add("stale_activity");
            if (!communicationAllowed) reasons.Add("email_permission_missing");
            var score = Math.Clamp(40 + (lead.Priority == "high" ? 25 : 0) + (lead.Temperature == "hot" ? 20 : 0) + Math.Min(age, 15), 0, 100);
            actions.Add(new SalesNextBestActionItemDto("lead", lead.Id, lead.Title, score,
                qualified ? "Review and prepare the next qualification follow-up" : "Complete qualification research",
                age > 7 ? "overdue" : "this_week", communicationAllowed ? "email_draft" : "internal_research",
                communicationAllowed && (policy?.RequireApprovalFollowUps ?? true), communicationAllowed, reasons,
                [$"sales-lead:{lead.Id:N}"]));
        }

        foreach (var deal in deals)
        {
            var age = Math.Max(0, (now.Date - deal.UpdatedUtc.Date).Days);
            var communicationAllowed = deal.PrimaryContactId is { } contactId && allowedContacts.Contains(contactId);
            var overdue = deal.ExpectedCloseUtc.HasValue && deal.ExpectedCloseUtc.Value < now;
            var reasons = new List<string> { overdue ? "expected_close_overdue" : "deal_review_due" };
            if (age > 7) reasons.Add("stale_activity");
            if (!communicationAllowed) reasons.Add("email_permission_missing");
            var score = Math.Clamp(45 + (overdue ? 30 : 0) + Math.Min(age, 20), 0, 100);
            actions.Add(new SalesNextBestActionItemDto("deal", deal.Id, deal.Title, score,
                overdue ? "Review close plan and expected close date" : "Review stakeholders and next milestone",
                overdue ? "overdue" : "this_week", "internal_review", false, communicationAllowed, reasons,
                [$"sales-deal:{deal.Id:N}"]));
        }

        actions = actions.OrderByDescending(x => x.PriorityScore).ThenBy(x => x.SubjectType, StringComparer.Ordinal)
            .ThenBy(x => x.SubjectId).Take(limit).ToList();
        var missing = new List<string>();
        if (policy is null) missing.Add("Sales automation policy");
        if (permissions.Count == 0 && contactIds.Length > 0) missing.Add("Current email communication permission");
        var advice = await Analyze(companyId, agentId, actorUserId, SalesAgentAnalysisTypes.NextBestAction,
            request.LeadId ?? request.DealId, request.Objective, now, ct);
        return new SalesNextBestActionResult(advice, actions, missing,
            true); // Recommendations always require explicit review before durable work or communication.
    }

    public async Task<SalesDealStrategyResult> AnalyzeDealStrategyAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, SalesDealStrategyRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var now = Utc(request.AsOfUtc ?? DateTime.UtcNow);
        var deal = await db.Deals.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.DealId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Sales deal not found.");
        var latestRisk = await db.DealRiskScoreSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DealId == deal.Id)
            .OrderByDescending(x => x.CalculatedUtc).FirstOrDefaultAsync(ct);
        var activities = await db.SalesActivities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DealId == deal.Id && !x.IsDeleted)
            .OrderByDescending(x => x.OccurredUtc).Take(20).ToListAsync(ct);
        var factors = new List<string>();
        var unknowns = new List<string>();
        if (latestRisk is not null) factors.Add(latestRisk.FactorsSummary);
        else unknowns.Add("Authoritative deal risk snapshot is missing.");
        if (deal.PrimaryContactId is null) unknowns.Add("Primary contact, champion, and authority are not confirmed.");
        if (deal.ExpectedCloseUtc is null) unknowns.Add("Expected close date is missing.");
        else if (deal.ExpectedCloseUtc < now) factors.Add("Expected close date has passed.");
        if (activities.Count == 0) factors.Add("No deal activity is recorded.");
        else if (activities[0].OccurredUtc < now.AddDays(-14)) factors.Add("No deal activity is recorded in the last 14 days.");

        var sources = new[] { $"sales-deal:{deal.Id:N}" };
        var plan = new List<SalesMutualActionPlanItemDto>
        {
            new(1, "Confirm customer outcome, decision process, and success criteria", "sales_owner", now.AddDays(3), "draft", [], sources),
            new(2, "Map champion, decision maker, procurement, and blockers", "sales_owner", now.AddDays(7), "draft", ["1"], sources),
            new(3, "Review product scope, commercial assumptions, and approvals", "sales_owner", now.AddDays(10), "draft", ["1", "2"], sources),
            new(4, "Agree a customer-reviewed close plan and next milestone", "sales_owner", deal.ExpectedCloseUtc ?? now.AddDays(14), "draft", ["2", "3"], sources)
        };
        var advice = await Analyze(companyId, agentId, actorUserId, SalesAgentAnalysisTypes.DealRisk,
            deal.Id, request.Objective, now, ct);
        return new SalesDealStrategyResult(advice, deal.Id, latestRisk?.Score, latestRisk?.Band ?? "unknown",
            factors, unknowns, plan, true);
    }

    public async Task<SalesForecastScenarioResult> AnalyzeForecastAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, SalesForecastScenarioRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var horizon = Math.Clamp(request.HorizonDays, 1, 365);
        EnsureProbabilityAdjustment(request.UpsideProbabilityAdjustment, nameof(request.UpsideProbabilityAdjustment));
        EnsureProbabilityAdjustment(request.DownsideProbabilityAdjustment, nameof(request.DownsideProbabilityAdjustment));
        var now = Utc(request.AsOfUtc ?? DateTime.UtcNow);
        var end = now.AddDays(horizon);
        var deals = await db.Deals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.Status != SalesStatuses.Lost &&
                        x.ExpectedCloseUtc.HasValue && x.ExpectedCloseUtc.Value >= now && x.ExpectedCloseUtc.Value <= end)
            .OrderBy(x => x.Currency).ThenBy(x => x.Id).ToListAsync(ct);
        var ids = deals.Select(x => x.Id).ToArray();
        var riskRows = await db.DealRiskScoreSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.DealId))
            .OrderByDescending(x => x.CalculatedUtc).ToListAsync(ct);
        var latestRisk = riskRows.GroupBy(x => x.DealId).ToDictionary(x => x.Key, x => x.First());
        var scenarios = new List<SalesForecastScenarioDto>();
        var concentration = new List<string>();
        foreach (var group in deals.GroupBy(x => x.Currency).OrderBy(x => x.Key))
        {
            var gross = group.Sum(x => x.Amount);
            decimal Expected(decimal adjustment) => group.Sum(x => x.Amount * Math.Clamp(StageProbability(x) + adjustment, 0m, 1m));
            var baseline = Expected(0m);
            var high = group.Count(x => latestRisk.TryGetValue(x.Id, out var risk) && risk.Band == "high");
            var unknown = group.Count(x => !latestRisk.ContainsKey(x.Id));
            var sourceId = $"sales-forecast-input:{now:yyyyMMddHH}:{horizon}:{group.Key}";
            scenarios.Add(Scenario("commit", gross, baseline, baseline, group.Key, group.Count(), high, unknown,
                ["Stage probabilities are deterministic and deal amounts are authoritative."], sourceId));
            var best = Expected(request.UpsideProbabilityAdjustment);
            scenarios.Add(Scenario("best_case", gross, best, baseline, group.Key, group.Count(), high, unknown,
                [$"Probability adjustment {request.UpsideProbabilityAdjustment:P0} was supplied by the reviewer."], sourceId));
            var downside = Expected(request.DownsideProbabilityAdjustment);
            scenarios.Add(Scenario("downside", gross, downside, baseline, group.Key, group.Count(), high, unknown,
                [$"Probability adjustment {request.DownsideProbabilityAdjustment:P0} was supplied by the reviewer."], sourceId));
            var largest = group.OrderByDescending(x => x.Amount).FirstOrDefault();
            if (largest is not null && gross > 0m && largest.Amount / gross >= .5m)
                concentration.Add($"{group.Key} pipeline is concentrated: deal {largest.Id:D} represents {largest.Amount / gross:P0} of gross pipeline.");
        }
        var missing = new List<string>();
        if (deals.Count == 0) missing.Add("Open deals with expected close dates in the selected horizon");
        if (deals.Count > 0 && latestRisk.Count < deals.Count) missing.Add("Current risk snapshots for every included deal");
        var advice = await Analyze(companyId, agentId, actorUserId, SalesAgentAnalysisTypes.ForecastAnalysis,
            null, request.Objective, now, ct, horizon);
        return new SalesForecastScenarioResult(advice, scenarios, concentration, missing,
            advice.RequiresReview || missing.Count > 0 || concentration.Count > 0);
    }

    public async Task<SalesCampaignOptimizationResult> OptimizeCampaignsAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, SalesCampaignOptimizationRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var now = Utc(request.AsOfUtc ?? DateTime.UtcNow);
        var query = db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId);
        if (request.CampaignId.HasValue) query = query.Where(x => x.Id == request.CampaignId.Value);
        var campaigns = await query.OrderByDescending(x => x.UpdatedUtc).Take(50).ToListAsync(ct);
        if (request.CampaignId.HasValue && campaigns.Count == 0) throw new KeyNotFoundException("Sales campaign not found.");
        var experiments = new List<SalesCampaignExperimentDto>();
        var missing = new List<string>();
        var evidence = new List<string>();
        foreach (var campaign in campaigns)
        {
            var performance = await conversionAnalytics.GetCampaignPerformanceAsync(companyId, campaign.Id, ct);
            var governedPerformance = await campaignPlanning.GetPerformanceAsync(companyId, campaign.Id, ct);
            var readiness = await campaignPlanning.GetReadinessAsync(companyId, campaign.Id, ct);
            var sourceVersion = $"sales-campaign:{campaign.Id:N}:version:{campaign.ConcurrencyVersion}";
            evidence.Add($"Campaign '{campaign.Name}' ({campaign.Id:D}) uses version {campaign.ConcurrencyVersion}; " +
                         $"lifecycle={campaign.LifecycleStatus}; status={campaign.Status}; " +
                         $"readiness={(readiness?.IsReady == true ? "ready" : "not ready")}; " +
                         $"readiness gaps={string.Join(", ", readiness?.MissingRequirements ?? [])}.");
            if (governedPerformance is not null)
            {
                evidence.Add($"Campaign '{campaign.Name}' evidence at {governedPerformance.ObservedUtc:O}: " +
                             $"audience={governedPerformance.Audience}; sent={governedPerformance.Sent}; " +
                             $"delivered={governedPerformance.Delivered}; replied={governedPerformance.Replied}; " +
                             $"opportunities={governedPerformance.Opportunities}; won deals={governedPerformance.WonDeals}; " +
                             $"objective progress={governedPerformance.ObjectiveProgress?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}%; " +
                             $"costs={FormatAmounts(governedPerformance.Costs)}; " +
                             $"direct revenue={FormatAmounts(governedPerformance.DirectRevenue)}; " +
                             $"attribution={FormatAttribution(governedPerformance.Attribution)}.");
                if (governedPerformance.Costs.Count == 0)
                    missing.Add($"Recorded campaign costs for {campaign.Name}");
                if (governedPerformance.Attribution.Count == 0)
                    missing.Add($"Stable campaign attribution evidence for {campaign.Name}");
                if (governedPerformance.Audience == 0)
                    missing.Add($"Captured eligible audience for {campaign.Name}");
            }
            if (readiness is { IsReady: false })
                missing.AddRange(readiness.MissingRequirements.Select(x => $"{campaign.Name}: {x}"));
            if (performance is null)
            {
                missing.Add($"Campaign performance for {campaign.Name}");
                experiments.Add(new SalesCampaignExperimentDto(campaign.Id, campaign.Name, 0, 0, 0, 0, 0m, 0m, 0m,
                    "Insufficient evidence: collect at least 30 delivered messages before choosing a variant or audience change.",
                    false, campaign.ApprovalRequired, [sourceVersion]));
                continue;
            }
            var sufficient = performance.Counts.Delivered >= 30;
            var recommendation = !sufficient
                ? "Insufficient sample: continue measurement without declaring a winner."
                : performance.Rates.ReplyRate < .03m
                    ? "Draft one controlled message experiment with reply rate as the primary measure."
                    : "Retain the current control and test one bounded audience or timing hypothesis.";
            var approved = !campaign.ApprovalRequired || campaign.ApprovalStatus == SalesStatuses.Approved;
            experiments.Add(new SalesCampaignExperimentDto(campaign.Id, campaign.Name, performance.Counts.Sent,
                performance.Counts.Delivered, performance.Counts.Replied, performance.Counts.Converted,
                performance.Rates.DeliveryRate, performance.Rates.ReplyRate, performance.Rates.ConversionRate,
                recommendation, sufficient && campaign.OutboundEnabled && approved && campaign.Status == SalesStatuses.Draft,
                campaign.ApprovalRequired, [sourceVersion, $"sales-campaign-performance:{campaign.Id:N}"]));
        }
        var analysisObjective = string.Join("\n", new[]
        {
            request.Objective ?? "Recommend the next bounded campaign decision.",
            "Use only the following campaign evidence. Treat unavailable values as unknown, preserve currency boundaries, " +
            "do not infer causation from influenced attribution, and recommend human review for consequential changes.",
            string.Join("\n", evidence)
        });
        var advice = await Analyze(companyId, agentId, actorUserId, SalesAgentAnalysisTypes.CampaignOptimization,
            request.CampaignId, analysisObjective, now, ct);
        return new SalesCampaignOptimizationResult(advice, experiments, missing.Distinct().ToList(), true);
    }

    public async Task<SalesProposalAdviceResult> AdviseProposalAsync(Guid companyId, Guid agentId,
        Guid? actorUserId, SalesProposalAdviceRequest request, CancellationToken ct)
    {
        ValidateIds(companyId, agentId);
        var now = Utc(request.AsOfUtc ?? DateTime.UtcNow);
        var deal = await db.Deals.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.DealId && !x.IsDeleted, ct)
            ?? throw new KeyNotFoundException("Sales deal not found.");
        var policy = await db.SalesAutomationPolicies.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, ct);
        var queryText = $"product catalog pricing commercial policy proposal terms {request.RequestedProduct} {request.RequestedTerms}";
        var results = await knowledge.SearchAsync(new CompanyKnowledgeSemanticSearchQuery(companyId, queryText, 12,
            new CompanyKnowledgeAccessContext(companyId, DataScopes: ["sales", "knowledge"])), ct);
        var authoritative = results.Where(x => x.Score >= .25d).Take(12).ToArray();
        var sourceIds = authoritative.Select(x => $"knowledge-chunk:{x.ChunkId:N}").ToArray();
        var combined = string.Join("\n", authoritative.Select(x => x.Content));
        var validations = new List<SalesProposalValidationDto>();
        var approvedClaims = new List<string>();
        var unknowns = new List<string>();
        if (string.IsNullOrWhiteSpace(request.RequestedProduct))
            unknowns.Add("Requested product or package is missing.");
        else if (combined.Contains(request.RequestedProduct, StringComparison.OrdinalIgnoreCase))
        {
            validations.Add(new SalesProposalValidationDto("product", "grounded", "The requested product appears in accessible approved company knowledge.", sourceIds));
            approvedClaims.Add($"Product reference: {request.RequestedProduct.Trim()}");
        }
        else
        {
            validations.Add(new SalesProposalValidationDto("product", "unsupported", "The requested product is not supported by accessible approved company knowledge.", sourceIds));
            unknowns.Add("Product capability and availability require authoritative catalog evidence.");
        }
        if (request.RequestedPrice.HasValue)
            validations.Add(new SalesProposalValidationDto("price", "review_required",
                $"Requested price {request.RequestedPrice.Value.ToString(CultureInfo.InvariantCulture)} {request.Currency ?? deal.Currency} requires comparison with an authoritative price/version.", sourceIds));
        else unknowns.Add("Requested price and currency are missing.");
        if (!string.IsNullOrWhiteSpace(request.RequestedTerms))
            validations.Add(new SalesProposalValidationDto("terms", "review_required", "Requested terms require explicit commercial-policy review.", sourceIds));
        if (authoritative.Length == 0) unknowns.Add("Processed product catalog and commercial policy evidence is unavailable.");
        if (deal.PrimaryContactId is null) unknowns.Add("Customer requirements and authorized reviewer are not confirmed on the deal.");
        var pricingApproval = request.RequestedPrice.HasValue && (policy?.RequireApprovalPricingDiscussion ?? true);
        var termsApproval = !string.IsNullOrWhiteSpace(request.RequestedTerms);
        var advice = await Analyze(companyId, agentId, actorUserId, SalesAgentAnalysisTypes.ProposalAdvice,
            deal.Id, request.Objective, now, ct);
        return new SalesProposalAdviceResult(advice, deal.Id, validations, approvedClaims, unknowns,
            pricingApproval, termsApproval, true);
    }

    private Task<RoleAgentAnalysisResult> Analyze(Guid companyId, Guid agentId, Guid? actorUserId, string type,
        Guid? subjectId, string? objective, DateTime now, CancellationToken ct, int horizon = 30) =>
        analysis.AnalyzeAsync(companyId, agentId, actorUserId,
            new RoleAgentAnalysisRequest(type, subjectId, horizon, objective, now), ct);

    private static SalesForecastScenarioDto Scenario(string name, decimal gross, decimal expected, decimal baseline,
        string currency, int deals, int highRisk, int unknownRisk, IReadOnlyList<string> assumptions, string sourceId) =>
        new(name, gross, expected, expected - baseline, currency, deals, highRisk, unknownRisk, assumptions, sourceId);

    private static decimal StageProbability(Deal deal) => deal.Status switch
    {
        SalesStatuses.Won => 1m,
        SalesStatuses.Lost => 0m,
        _ when deal.PipelineStageId == SalesPipelineStage.ProposalStageId => .65m,
        _ when deal.PipelineStageId == SalesPipelineStage.QualifiedStageId => .35m,
        _ => .10m
    };

    private static bool IsAllowed(string status) => status is "allowed" or "granted" or "consented" or "opted_in";
    private static string FormatAmounts(IReadOnlyList<CampaignCurrencyAmountResponse> amounts) =>
        amounts.Count == 0
            ? "unavailable"
            : string.Join("; ", amounts.Select(x =>
                $"{x.Amount.ToString(CultureInfo.InvariantCulture)} {x.Currency} ({x.Classification})"));
    private static string FormatAttribution(IReadOnlyList<CampaignAttributionEvidenceResponse> attribution) =>
        attribution.Count == 0
            ? "unavailable"
            : string.Join("; ", attribution.GroupBy(x => x.Classification)
                .Select(x => $"{x.Count()} {x.Key}, confidence range {x.Min(v => v.Confidence):0.00}-{x.Max(v => v.Confidence):0.00}"));
    private static void AddFact(List<SalesIntelligenceFactDto> facts, string label, string value, string sourceId, DateTime asOf) =>
        facts.Add(new SalesIntelligenceFactDto(label, value, sourceId, Utc(asOf)));
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static void ValidateIds(Guid companyId, Guid agentId)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty) throw new ArgumentException("Company and agent are required.");
    }
    private static void EnsureProbabilityAdjustment(decimal value, string name)
    {
        if (value is < -1m or > 1m) throw new ArgumentOutOfRangeException(name, "Probability adjustment must be between -1 and 1.");
    }
}
