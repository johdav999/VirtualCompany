using Microsoft.EntityFrameworkCore;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed partial class MarketingOperationsService : IMarketingOperationsService
{
    private readonly VirtualCompanyDbContext _db;
    public MarketingOperationsService(VirtualCompanyDbContext db) => _db = db;

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
        var brief = new MarketingContentBrief(Guid.NewGuid(), companyId, r.Title, r.Purpose, r.Audience,
            r.Channel, r.Language, r.Tone, r.CallToAction, r.CampaignId, r.PlanId, r.DueUtc, userId, null);
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
        var observation = new MarketingChannelObservation(Guid.NewGuid(), companyId, r.Provider, r.MetricCode,
            r.Value, r.Unit, r.PeriodStartUtc, r.PeriodEndUtc, r.CampaignId, r.ActivityId,
            r.SourceReference, r.IdempotencyKey);
        _db.MarketingChannelObservations.Add(observation);
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
    private static MarketingContentVariantDto Map(MarketingContentVariant x) => new(x.Id, x.Name, x.Body, x.SourceReferences, x.GeneratedByAi, x.Status, x.CreatedUtc);
    private static MarketingContentBriefDto Map(MarketingContentBrief x, IReadOnlyList<MarketingContentVariantDto> variants) => new(x.Id, x.SalesCampaignId, x.MarketingPlanId, x.Title, x.Purpose, x.Audience, x.Channel, x.Language, x.Tone, x.CallToAction, x.DueUtc, x.Status, x.Version, variants);
    private static MarketingSalesHandoffDto Map(MarketingSalesHandoff x) => new(x.Id, x.SalesCampaignId, x.ContactId, x.CustomerCompanyId, x.LinkedLeadId, x.LinkedDealId, x.Reason, x.SuggestedAction, x.Urgency, x.ExpiresUtc, x.EvidenceReferences, x.Status, x.DecisionReason, x.UpdatedUtc);
    private static MarketingObservationDto Map(MarketingChannelObservation x) => new(x.Id, x.SalesCampaignId, x.SalesCampaignActivityId, x.Provider, x.MetricCode, x.Value, x.Unit, x.PeriodStartUtc, x.PeriodEndUtc, x.SourceReference, x.RetrievedUtc);
    private static MarketingExperimentDto Map(MarketingExperiment x) => new(x.Id, x.SalesCampaignId, x.Name, x.Hypothesis, x.PrimaryMetric, x.GuardrailMetric, x.MinimumSampleSize, x.StartsUtc, x.EndsUtc, x.Status, x.Decision);
}
