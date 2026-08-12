using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class MarketingOperationsService : IMarketingOperationsService
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IMarketingAgentAnalysisService? _analysis;
    private readonly IMarketingEventPublisher? _events;
    public MarketingOperationsService(VirtualCompanyDbContext db, IMarketingAgentAnalysisService? analysis = null,
        IMarketingEventPublisher? events = null)
    { _db = db; _analysis = analysis; _events = events; }

    public async Task<MarketingDashboardDto> GetDashboardAsync(Guid companyId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        RequireCompany(companyId);
        fromUtc = NormalizeUtc(fromUtc);
        toUtc = NormalizeUtc(toUtc);
        if (toUtc <= fromUtc) throw new ArgumentException("Dashboard period is invalid.");

        var objectives = await ListObjectivesAsync(companyId, ct);
        var plans = await ListPlansAsync(companyId, ct);
        var content = await ListContentAsync(companyId, ct);
        var handoffs = await ListHandoffsAsync(companyId, ct);
        var experiments = await ListExperimentsAsync(companyId, ct);
        var qualificationDefinitions = await ListQualificationDefinitionsAsync(companyId, ct);
        var qualificationEvaluations = await ListQualificationEvaluationsAsync(companyId, ct);
        var observations = await ListObservationsAsync(companyId, fromUtc, toUtc, ct);
        var campaigns = await _db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.CreatedUtc < toUtc)
            .Select(x => new { x.Id, x.Name, x.Status, x.CreatedUtc, x.ScheduledLaunchUtc, x.OwnerAgentId })
            .ToListAsync(ct);
        var activities = await _db.SalesCampaignActivities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DueUtc >= fromUtc && x.PlannedStartUtc < toUtc)
            .Select(x => new MarketingCalendarItemDto(x.Id, "activity", x.Name, x.PlannedStartUtc, x.DueUtc,
                x.Status, x.SalesCampaignId, x.OwnerAgentId))
            .ToListAsync(ct);
        var calendar = campaigns
            .Where(x => x.ScheduledLaunchUtc.HasValue && x.ScheduledLaunchUtc >= fromUtc && x.ScheduledLaunchUtc < toUtc)
            .Select(x => new MarketingCalendarItemDto(x.Id, "campaign", x.Name, x.ScheduledLaunchUtc!.Value,
                x.ScheduledLaunchUtc.Value, x.Status, x.Id, x.OwnerAgentId))
            .Concat(activities)
            .OrderBy(x => x.StartsUtc)
            .ToArray();

        decimal? Metric(string code)
        {
            var values = observations.Where(x => x.MetricCode == code).Select(x => x.Value).ToArray();
            return values.Length == 0 ? null : values.Sum();
        }
        var metrics = new[]
        {
            BuildMetric("Campaigns", campaigns.Count, "count", campaigns.Count == 0),
            BuildMetric("Qualified handoffs", handoffs.Count(x => x.Status == MarketingStatuses.Accepted), "count", handoffs.Count == 0),
            BuildObservedMetric("Engagement", Metric("engagement_rate"), "%"),
            BuildObservedMetric("Marketing-sourced pipeline", Metric("sourced_pipeline"), "currency"),
            BuildObservedMetric("Marketing cost", Metric("cost"), "currency")
        };
        return new MarketingDashboardDto(companyId, DateTime.UtcNow, metrics, objectives, plans, calendar,
            content, handoffs, experiments, qualificationDefinitions, qualificationEvaluations);
    }

    public async Task<IReadOnlyList<MarketingObjectiveDto>> ListObjectivesAsync(Guid companyId, CancellationToken ct) =>
        (await _db.MarketingObjectives.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.PeriodEndUtc).ToListAsync(ct)).Select(Map).ToArray();

    public async Task<MarketingObjectiveDto> CreateObjectiveAsync(Guid companyId, Guid userId, CreateMarketingObjectiveRequest r, CancellationToken ct)
    {
        var entity = new MarketingObjective(Guid.NewGuid(), companyId, r.Name, r.ObjectiveType, r.TargetValue,
            r.Unit, r.PeriodStartUtc, r.PeriodEndUtc, userId);
        entity.SetBaseline(r.BaselineValue);
        _db.MarketingObjectives.Add(entity);
        await _db.SaveChangesAsync(ct);
        return Map(entity);
    }

    public async Task<MarketingObjectiveDto?> ActivateObjectiveAsync(Guid companyId, Guid objectiveId, CancellationToken ct)
    {
        var objective = await _db.MarketingObjectives.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == objectiveId, ct);
        if (objective is null) return null;
        objective.Activate();
        await _db.SaveChangesAsync(ct);
        return Map(objective);
    }

    public async Task<IReadOnlyList<MarketingPlanDto>> ListPlansAsync(Guid companyId, CancellationToken ct) =>
        (await _db.MarketingPlans.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.StartsUtc).ToListAsync(ct)).Select(Map).ToArray();

    public async Task<MarketingPlanDto> CreatePlanAsync(Guid companyId, Guid userId, CreateMarketingPlanRequest r, CancellationToken ct)
        => await CreatePlanCoreAsync(companyId, userId, r, null, ct);

    private async Task<MarketingPlanDto> CreatePlanCoreAsync(Guid companyId, Guid userId, CreateMarketingPlanRequest r,
        string? idempotencyKey, CancellationToken ct)
    {
        var plan = new MarketingPlan(Guid.NewGuid(), companyId, r.Name, r.Summary, r.StartsUtc, r.EndsUtc,
            r.PlannedBudget, r.BudgetCurrency, userId, null, idempotencyKey);
        _db.MarketingPlans.Add(plan);
        foreach (var objectiveId in (r.ObjectiveIds ?? []).Distinct())
        {
            if (!await _db.MarketingObjectives.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == objectiveId, ct))
                throw new InvalidOperationException("A selected objective is not available to this company.");
            _db.MarketingPlanObjectives.Add(new MarketingPlanObjective(Guid.NewGuid(), companyId, plan.Id, objectiveId));
        }
        await _db.SaveChangesAsync(ct);
        return Map(plan);
    }

    public async Task<MarketingPlanDto?> ActivatePlanAsync(Guid companyId, Guid planId, CancellationToken ct)
    {
        var plan = await _db.MarketingPlans.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == planId, ct);
        if (plan is null) return null;
        plan.Activate();
        await _db.SaveChangesAsync(ct);
        return Map(plan);
    }

    public async Task<IReadOnlyList<MarketingContentBriefDto>> ListContentAsync(Guid companyId, CancellationToken ct)
    {
        var briefs = await _db.MarketingContentBriefs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.DueUtc).ToListAsync(ct);
        var ids = briefs.Select(x => x.Id).ToArray();
        var variants = await _db.MarketingContentVariants.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.MarketingContentBriefId))
            .OrderBy(x => x.CreatedUtc).ToListAsync(ct);
        return briefs.Select(x => Map(x, variants.Where(v => v.MarketingContentBriefId == x.Id).Select(Map).ToArray())).ToArray();
    }

    public async Task<MarketingContentBriefDto> CreateContentBriefAsync(Guid companyId, Guid userId, CreateMarketingContentBriefRequest r, CancellationToken ct)
    {
        await ValidateReferences(companyId, r.CampaignId, r.PlanId, ct);
        if (r.CampaignId.HasValue && !r.SegmentVersionId.HasValue)
            throw new InvalidOperationException("Campaign-linked content requires an approved target segment version.");
        MarketingCustomerSegmentVersion? segment = null;
        if (r.SegmentVersionId.HasValue)
            segment = await _db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == r.SegmentVersionId &&
                    (x.Status == MarketingStrategicStatuses.Approved || x.Status == MarketingStrategicStatuses.Active), ct)
                ?? throw new InvalidOperationException("The selected target segment version is not approved or is unavailable.");
        foreach (var (json, label) in new[] { (r.SupportingPointsJson, "Supporting points"),
            (r.RequiredClaimsJson, "Required claims"), (r.ProhibitedClaimsJson, "Prohibited claims"),
            (r.SeoRequirementsJson, "SEO requirements"), (r.DesiredFormatsJson, "Desired formats"),
            (r.VariantRequirementsJson, "Variant requirements"), (r.EvidenceRequirementsJson, "Evidence requirements"),
            (r.ApprovalPolicyJson, "Approval policy") }) ValidateMarketingJson(json, label);
        var customerInsight = !string.IsNullOrWhiteSpace(r.CustomerInsight) ? r.CustomerInsight : segment is null
            ? "Not specified"
            : JsonSerializer.Serialize(new { segmentVersionId = segment.Id, segment.NeedsJson,
                segment.BehaviorsJson, segment.ChannelsJson, segment.PricingJson, segment.SizeLow,
                segment.SizeHigh, segment.Confidence });
        var brief = new MarketingContentBrief(Guid.NewGuid(), companyId, r.Title, r.Purpose, r.Audience,
            r.Channel, r.Language, r.Tone, r.CallToAction, r.CampaignId, r.PlanId, r.DueUtc, userId, null,
            r.SegmentVersionId, ValueOr(r.MeasurableObjective), ValueOr(r.FunnelStage, "awareness"), customerInsight,
            ValueOr(r.KeyMessage), r.SupportingPointsJson, ValueOr(r.Offer), r.RequiredClaimsJson,
            r.ProhibitedClaimsJson, r.SeoRequirementsJson, ValueOr(r.VisualDirection), r.DesiredFormatsJson,
            r.VariantRequirementsJson, r.EvidenceRequirementsJson, r.ApprovalPolicyJson);
        _db.MarketingContentBriefs.Add(brief);
        await _db.SaveChangesAsync(ct);
        return Map(brief, []);
    }

    public async Task<MarketingContentVariantDto?> AddContentVariantAsync(Guid companyId, Guid briefId, CreateMarketingContentVariantRequest r, CancellationToken ct)
    {
        if (!await _db.MarketingContentBriefs.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == briefId, ct)) return null;
        var variant = new MarketingContentVariant(Guid.NewGuid(), companyId, briefId, r.Name, r.Body, r.SourceReferences, r.GeneratedByAi);
        _db.MarketingContentVariants.Add(variant);
        await _db.SaveChangesAsync(ct);
        return Map(variant);
    }

    public async Task<MarketingContentVariantDto?> CreateContentVariantVersionAsync(Guid companyId, Guid variantId,
        CreateMarketingContentVariantVersionRequest r, CancellationToken ct)
    {
        var source = await _db.MarketingContentVariants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == variantId, ct);
        if (source is null) return null;
        var nextVersion = await _db.MarketingContentVariants.IgnoreQueryFilters().Where(x =>
            x.CompanyId == companyId && x.VariantFamilyId == source.VariantFamilyId).MaxAsync(x => x.VersionNumber, ct) + 1;
        var variant = new MarketingContentVariant(Guid.NewGuid(), companyId, source.MarketingContentBriefId,
            r.Name, r.Body, r.SourceReferences, false, source.ContentFormat, null, "human-revision",
            "human-revision-v1", null, 0, source.VariantFamilyId, nextVersion);
        _db.MarketingContentVariants.Add(variant);
        await _db.SaveChangesAsync(ct);
        return Map(variant);
    }

    public async Task<bool> RetireContentVariantAsync(Guid companyId, Guid variantId, CancellationToken ct)
    {
        var variant = await _db.MarketingContentVariants.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == variantId, ct);
        if (variant is null) return false;
        variant.Retire();
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<GenerateMarketingContentVariantsResult> GenerateContentVariantsAsync(Guid companyId,
        Guid userId, Guid briefId, GenerateMarketingContentVariantsRequest r, CancellationToken ct)
    {
        if (_analysis is null) throw new InvalidOperationException("Marketing reasoning is unavailable.");
        if (r.VariantCount is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(r.VariantCount));
        var format = r.ContentFormat.Trim().ToLowerInvariant();
        if (format is not ("website" or "landing_page" or "article" or "social_post" or "email" or "ad" or
            "webinar" or "video_script" or "case_study" or "sales_enablement"))
            throw new ArgumentException("Unsupported Marketing content format.");
        var existing = await _db.MarketingContentVariants.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MarketingContentBriefId == briefId && x.IdempotencyKey == r.IdempotencyKey)
            .OrderBy(x => x.BatchIndex).ToListAsync(ct);
        if (existing.Count > 0)
            return new GenerateMarketingContentVariantsResult(existing[0].GenerationRunId ?? Guid.Empty,
                AgentAiRunStatuses.Completed, existing.Select(Map).ToArray(), [], false);
        var brief = await _db.MarketingContentBriefs.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == briefId, ct)
            ?? throw new InvalidOperationException("Content brief is unavailable.");
        if (brief.Status != MarketingStatuses.Draft)
            throw new InvalidOperationException("New variants can only be generated for a draft content brief.");
        var objective = $"Generate {r.VariantCount} distinct {format} draft variants. Brief purpose: {brief.Purpose}. " +
            $"Audience: {brief.Audience}. Channel: {brief.Channel}. Language: {brief.Language}. Tone: {brief.Tone}. " +
            $"Objective: {brief.MeasurableObjective}. Funnel stage: {brief.FunnelStage}. Customer insight: {brief.CustomerInsight}. " +
            $"Key message: {brief.KeyMessage}. Offer: {brief.Offer}. Required claims: {brief.RequiredClaimsJson}. " +
            $"Prohibited claims: {brief.ProhibitedClaimsJson}. SEO: {brief.SeoRequirementsJson}. " +
            $"Evidence requirements: {brief.EvidenceRequirementsJson}. Call to action: {brief.CallToAction}. Operator instructions: {r.Instructions}. " +
            "Omit any product fact, price, statistic, testimonial, competitor claim, or regulated claim without a supplied source.";
        var result = await _analysis.AnalyzeAsync(companyId, r.AgentId, userId,
            new RoleAgentAnalysisRequest(MarketingAgentAnalysisTypes.ContentAdvice, briefId, 30, objective), ct);
        var sourceIds = result.Sources.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalidClaims = result.Claims.Where(x => x.SourceIds.Count == 0 ||
            x.SourceIds.Any(id => !sourceIds.Contains(id))).ToArray();
        if (result.Status != AgentAiRunStatuses.Completed || invalidClaims.Length > 0 || result.Claims.Count == 0)
        {
            var missing = result.MissingEvidence.Concat(invalidClaims.Length > 0
                    ? ["Generated factual claims did not have accessible evidence."] : [])
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new GenerateMarketingContentVariantsResult(result.RunId, AgentAiRunStatuses.NeedsReview,
                [], missing, true);
        }
        var references = JsonSerializer.Serialize(result.Sources.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase));
        var variants = new List<MarketingContentVariant>();
        for (var index = 0; index < r.VariantCount; index++)
        {
            var claim = result.Claims[index % result.Claims.Count];
            var body = $"{result.Summary}\n\n{claim.Text}\n\n{brief.CallToAction}";
            var variant = new MarketingContentVariant(Guid.NewGuid(), companyId, briefId,
                $"Maya {format.Replace('_', ' ')} variant {index + 1}", body, references, true, format,
                result.RunId, "1.0.0", "marketing-content-v1", r.IdempotencyKey, index);
            variants.Add(variant); _db.MarketingContentVariants.Add(variant);
        }
        await _db.SaveChangesAsync(ct);
        return new GenerateMarketingContentVariantsResult(result.RunId, result.Status,
            variants.Select(Map).ToArray(), result.MissingEvidence, result.RequiresReview);
    }

    public async Task<bool> ReviewContentAsync(Guid companyId, Guid briefId, ReviewMarketingContentRequest r, CancellationToken ct)
    {
        var brief = await _db.MarketingContentBriefs.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == briefId, ct);
        if (brief is null) return false;
        brief.Review(r.Approved);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SubmitContentAsync(Guid companyId, Guid briefId, CancellationToken ct)
    {
        var brief = await _db.MarketingContentBriefs.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == briefId, ct);
        if (brief is null) return false;
        var preflight = await PreflightContentAsync(companyId, briefId, ct);
        if (preflight is null || !preflight.ReadyForReview)
            throw new InvalidOperationException("Content preflight found issues that must be resolved before review.");
        brief.Submit();
        await EnsureMarketingApprovalTaskAsync(companyId, brief, ct);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<MarketingSalesHandoffDto>> ListHandoffsAsync(Guid companyId, CancellationToken ct) =>
        (await _db.MarketingSalesHandoffs.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.UpdatedUtc).ToListAsync(ct)).Select(Map).ToArray();

    public async Task<MarketingSalesHandoffDto> CreateHandoffAsync(Guid companyId, CreateMarketingSalesHandoffRequest r, CancellationToken ct)
    {
        var existing = await _db.MarketingSalesHandoffs.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == r.IdempotencyKey, ct);
        if (existing is not null) return Map(existing);
        if (r.ContactId.HasValue && !await _db.Contacts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == r.ContactId, ct))
            throw new InvalidOperationException("The selected contact is not available to this company.");
        var handoff = new MarketingSalesHandoff(Guid.NewGuid(), companyId, r.CampaignId, r.ContactId,
            r.CustomerCompanyId, r.Reason, r.SuggestedAction, r.Urgency, r.ExpiresUtc,
            r.EvidenceReferences, r.IdempotencyKey);
        _db.MarketingSalesHandoffs.Add(handoff);
        await EnsureSalesHandoffTaskAsync(companyId, handoff, ct);
        await _db.SaveChangesAsync(ct);
        return Map(handoff);
    }

    public async Task<MarketingSalesHandoffDto?> DecideHandoffAsync(Guid companyId, Guid handoffId, DecideMarketingSalesHandoffRequest r, CancellationToken ct)
    {
        var handoff = await _db.MarketingSalesHandoffs.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == handoffId, ct);
        if (handoff is null) return null;
        handoff.Decide(r.Accepted, r.Reason, r.LeadId, r.DealId);
        var task = await _db.WorkTasks.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId &&
                x.CorrelationId == $"marketing-handoff:{handoff.Id:D}", ct);
        task?.UpdateStatus(WorkTaskStatus.Completed,
            new Dictionary<string, JsonNode?> { ["accepted"] = JsonValue.Create(r.Accepted), ["reason"] = JsonValue.Create(r.Reason) },
            r.Accepted ? "Sales accepted the marketing handoff." : "Sales declined the marketing handoff.");
        if (_events is not null) await _events.PublishAsync(companyId, new PublishMarketingEventCommand(
            MarketingEventTypes.SalesHandoffOutcome, "marketing_sales_handoff", handoff.Id.ToString("N"),
            1, JsonSerializer.Serialize(new { accepted = r.Accepted, r.Reason, r.LeadId, r.DealId }),
            $"marketing-handoff:{handoff.Id:N}", DateTime.UtcNow), ct);
        await _db.SaveChangesAsync(ct);
        return Map(handoff);
    }

    public async Task<IReadOnlyList<MarketingObservationDto>> ListObservationsAsync(Guid companyId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        (await _db.MarketingChannelObservations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PeriodEndUtc >= fromUtc && x.PeriodStartUtc < toUtc)
            .OrderByDescending(x => x.PeriodEndUtc).ToListAsync(ct)).Select(Map).ToArray();

    public async Task<MarketingObservationDto> RecordObservationAsync(Guid companyId, CreateMarketingObservationRequest r, CancellationToken ct)
    {
        var existing = await _db.MarketingChannelObservations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == r.IdempotencyKey, ct);
        if (existing is not null) return Map(existing);
        MarketingChannelObservation? corrected = null;
        if (r.CorrectionOfObservationId.HasValue)
        {
            corrected = await _db.MarketingChannelObservations.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == companyId && x.Id == r.CorrectionOfObservationId.Value, ct)
                ?? throw new InvalidOperationException("The observation being corrected is unavailable.");
            if (!corrected.MetricCode.Equals(r.MetricCode, StringComparison.OrdinalIgnoreCase) ||
                !corrected.Unit.Equals(r.Unit, StringComparison.OrdinalIgnoreCase) ||
                corrected.PeriodStartUtc != NormalizeUtc(r.PeriodStartUtc) || corrected.PeriodEndUtc != NormalizeUtc(r.PeriodEndUtc))
                throw new InvalidOperationException("A correction must retain the metric, unit, and observation period.");
        }
        var observation = new MarketingChannelObservation(Guid.NewGuid(), companyId, r.Provider, r.MetricCode,
            r.Value, r.Unit, r.PeriodStartUtc, r.PeriodEndUtc, r.CampaignId, r.ActivityId,
            r.SourceReference, r.IdempotencyKey, r.CorrectionOfObservationId);
        corrected?.Supersede();
        _db.MarketingChannelObservations.Add(observation);
        if (_events is not null && r.CorrectionOfObservationId.HasValue)
            await _events.PublishAsync(companyId, new PublishMarketingEventCommand(MarketingEventTypes.IntelligenceChange,
                "marketing_observation", observation.Id.ToString("N"), 1,
                JsonSerializer.Serialize(new { correctionOf = r.CorrectionOfObservationId, r.MetricCode, r.SourceReference }),
                $"marketing-observation:{observation.Id:N}", DateTime.UtcNow), ct);
        await _db.SaveChangesAsync(ct);
        return Map(observation);
    }

    public async Task<IReadOnlyList<MarketingExperimentDto>> ListExperimentsAsync(Guid companyId, CancellationToken ct) =>
        (await _db.MarketingExperiments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.StartsUtc).ToListAsync(ct)).Select(Map).ToArray();

    public async Task<MarketingExperimentDto> CreateExperimentAsync(Guid companyId, CreateMarketingExperimentRequest r, CancellationToken ct)
    {
        var experiment = new MarketingExperiment(Guid.NewGuid(), companyId, r.Name, r.Hypothesis,
            r.PrimaryMetric, r.GuardrailMetric, r.MinimumSampleSize, r.StartsUtc, r.EndsUtc, r.CampaignId);
        _db.MarketingExperiments.Add(experiment);
        await _db.SaveChangesAsync(ct);
        return Map(experiment);
    }

    public async Task<MarketingExperimentDto?> ActivateExperimentAsync(Guid companyId, Guid experimentId, CancellationToken ct)
    {
        var experiment = await _db.MarketingExperiments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == experimentId, ct);
        if (experiment is null) return null;
        experiment.Activate();
        await _db.SaveChangesAsync(ct);
        return Map(experiment);
    }

    public async Task<MarketingExperimentDto?> CompleteExperimentAsync(Guid companyId, Guid experimentId, CompleteMarketingExperimentRequest request, CancellationToken ct)
    {
        var experiment = await _db.MarketingExperiments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == experimentId, ct);
        if (experiment is null) return null;
        var evidence = await _db.MarketingChannelObservations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                x.SourceReference == $"experiment:{experiment.Id:D}" &&
                !x.IsSuperseded &&
                x.PeriodEndUtc >= experiment.StartsUtc && x.PeriodStartUtc <= experiment.EndsUtc)
            .Select(x => new { x.MetricCode, x.Value })
            .ToListAsync(ct);
        var sampleSize = evidence.Where(x => x.MetricCode == "sample_size").Sum(x => x.Value);
        if (sampleSize < experiment.MinimumSampleSize)
            throw new InvalidOperationException($"The experiment needs at least {experiment.MinimumSampleSize} observations before a decision. Current sample: {sampleSize:0}.");
        if (!evidence.Any(x => x.MetricCode == experiment.PrimaryMetric))
            throw new InvalidOperationException("Record the primary experiment metric before completing the experiment.");
        if (!evidence.Any(x => x.MetricCode == experiment.GuardrailMetric))
            throw new InvalidOperationException("Record the guardrail metric before completing the experiment.");
        experiment.Complete(request.Decision);
        if (_events is not null) await _events.PublishAsync(companyId, new PublishMarketingEventCommand(
            MarketingEventTypes.ExperimentThreshold, "marketing_experiment", experiment.Id.ToString("N"), 1,
            JsonSerializer.Serialize(new { sampleSize, experiment.PrimaryMetric, experiment.GuardrailMetric, request.Decision }),
            $"marketing-experiment:{experiment.Id:N}", DateTime.UtcNow), ct);
        await _db.SaveChangesAsync(ct);
        return Map(experiment);
    }

    private async Task ValidateReferences(Guid companyId, Guid? campaignId, Guid? planId, CancellationToken ct)
    {
        if (campaignId.HasValue && !await _db.SalesCampaigns.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == campaignId, ct))
            throw new InvalidOperationException("The selected campaign is not available to this company.");
        if (planId.HasValue && !await _db.MarketingPlans.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == planId, ct))
            throw new InvalidOperationException("The selected plan is not available to this company.");
    }

    private async Task EnsureMarketingApprovalTaskAsync(Guid companyId, MarketingContentBrief brief, CancellationToken ct)
    {
        var correlationId = $"marketing-content:{brief.Id:D}";
        if (await _db.WorkTasks.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.CorrelationId == correlationId, ct)) return;
        var agentId = await _db.Agents.IgnoreQueryFilters().Where(x => x.CompanyId == companyId &&
            x.Department == "Marketing" && x.Status != AgentStatus.Archived).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        var task = new WorkTask(Guid.NewGuid(), companyId, "marketing_content_approval",
            $"Review marketing content: {brief.Title}", "Review claims, sources, audience, channel, and call to action before publication.",
            WorkTaskPriority.Normal, agentId, null, "system", null,
            new Dictionary<string, JsonNode?> { ["briefId"] = JsonValue.Create(brief.Id), ["channel"] = JsonValue.Create(brief.Channel) },
            correlationId: correlationId, sourceType: WorkTaskSourceTypes.Agent, originatingAgentId: agentId,
            triggerSource: "marketing-content", creationReason: "Content passed deterministic preflight and requires human approval.",
            triggerEventId: brief.Id.ToString("D"), status: WorkTaskStatus.AwaitingApproval);
        task.SetDueDate(brief.DueUtc);
        _db.WorkTasks.Add(task);
    }

    private async Task EnsureSalesHandoffTaskAsync(Guid companyId, MarketingSalesHandoff handoff, CancellationToken ct)
    {
        var correlationId = $"marketing-handoff:{handoff.Id:D}";
        if (await _db.WorkTasks.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.CorrelationId == correlationId, ct)) return;
        var salesAgentId = await _db.Agents.IgnoreQueryFilters().Where(x => x.CompanyId == companyId &&
            x.Department == "Sales" && x.Status != AgentStatus.Archived).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        var task = new WorkTask(Guid.NewGuid(), companyId, "marketing_sales_handoff_review",
            "Review qualified marketing demand", handoff.SuggestedAction,
            handoff.Urgency == "high" ? WorkTaskPriority.High : WorkTaskPriority.Normal,
            salesAgentId, null, "system", null,
            new Dictionary<string, JsonNode?> { ["handoffId"] = JsonValue.Create(handoff.Id), ["reason"] = JsonValue.Create(handoff.Reason) },
            correlationId: correlationId, sourceType: WorkTaskSourceTypes.Agent, originatingAgentId: salesAgentId,
            triggerSource: "marketing-handoff", creationReason: "Marketing proposed qualified demand for Sales review.",
            triggerEventId: handoff.Id.ToString("D"), status: WorkTaskStatus.AwaitingApproval);
        task.SetDueDate(handoff.ExpiresUtc);
        _db.WorkTasks.Add(task);
    }

    private static MarketingMetricDto BuildMetric(string name, decimal value, string unit, bool empty) =>
        new(name, value, unit, empty ? "no_data" : "available", empty ? "No records are available for this period." : "Calculated from current company records.");
    private static MarketingMetricDto BuildObservedMetric(string name, decimal? value, string unit) =>
        new(name, value, unit, value.HasValue ? "available" : "not_connected",
            value.HasValue ? "Calculated from source-linked channel observations." : "Connect a channel or record a source observation to populate this metric.");
    private static void RequireCompany(Guid id) { if (id == Guid.Empty) throw new ArgumentException("Company is required."); }
    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static MarketingObjectiveDto Map(MarketingObjective x) => new(x.Id, x.Name, x.ObjectiveType, x.TargetValue, x.Unit, x.BaselineValue, x.PeriodStartUtc, x.PeriodEndUtc, x.Status, x.Version);
    private static MarketingPlanDto Map(MarketingPlan x) => new(x.Id, x.Name, x.Summary, x.StartsUtc, x.EndsUtc, x.PlannedBudget, x.BudgetCurrency, x.Status, x.Version);
    private static MarketingContentVariantDto Map(MarketingContentVariant x) => new(x.Id, x.VariantFamilyId,
        x.VersionNumber, x.Name, x.Body, x.ContentFormat, x.SourceReferences, x.GeneratedByAi,
        x.GenerationRunId, x.CapabilityVersion, x.PromptVersion, x.Status, x.CreatedUtc);
    private static MarketingContentBriefDto Map(MarketingContentBrief x, IReadOnlyList<MarketingContentVariantDto> variants) => new(x.Id, x.SalesCampaignId, x.MarketingPlanId, x.Title, x.Purpose, x.Audience, x.Channel, x.Language, x.Tone, x.CallToAction, x.DueUtc, x.Status, x.Version, variants, x.MarketingCustomerSegmentVersionId, x.MeasurableObjective, x.FunnelStage, x.CustomerInsight, x.KeyMessage, x.SupportingPointsJson, x.Offer, x.RequiredClaimsJson, x.ProhibitedClaimsJson, x.SeoRequirementsJson, x.VisualDirection, x.DesiredFormatsJson, x.VariantRequirementsJson, x.EvidenceRequirementsJson, x.ApprovalPolicyJson);
    private static void ValidateMarketingJson(string value, string label)
    { try { JsonNode.Parse(value); } catch (System.Text.Json.JsonException ex) { throw new ArgumentException($"{label} must be valid JSON.", ex); } }
    private static string ValueOr(string? value, string fallback = "Not specified") => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static MarketingSalesHandoffDto Map(MarketingSalesHandoff x) => new(x.Id, x.SalesCampaignId, x.ContactId, x.CustomerCompanyId, x.LinkedLeadId, x.LinkedDealId, x.Reason, x.SuggestedAction, x.Urgency, x.ExpiresUtc, x.EvidenceReferences, x.Status, x.DecisionReason, x.UpdatedUtc);
    private static MarketingObservationDto Map(MarketingChannelObservation x) => new(x.Id, x.SalesCampaignId, x.SalesCampaignActivityId, x.Provider, x.MetricCode, x.Value, x.Unit, x.PeriodStartUtc, x.PeriodEndUtc, x.SourceReference, x.RetrievedUtc, x.CorrectionOfObservationId, x.IsSuperseded);
    private static MarketingExperimentDto Map(MarketingExperiment x) => new(x.Id, x.SalesCampaignId, x.Name, x.Hypothesis, x.PrimaryMetric, x.GuardrailMetric, x.MinimumSampleSize, x.StartsUtc, x.EndsUtc, x.Status, x.Decision);
}
