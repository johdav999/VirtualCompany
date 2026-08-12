using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingStrategyService(VirtualCompanyDbContext db, IApprovalRequestService approvals,
    IMarketingAgentAnalysisService analysis, IAgentReasoningGateway reasoning, ICompanyTaskCommandService tasks,
    IAuditEventWriter audit)
    : IMarketingStrategyService
{
    public async Task<IReadOnlyList<MarketingStrategyDto>> ListStrategiesAsync(Guid companyId, CancellationToken ct)
    {
        var items = await db.MarketingStrategies.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.UpdatedUtc).ToListAsync(ct);
        var links = await db.MarketingStrategySegments.AsNoTracking().Where(x => x.CompanyId == companyId).ToListAsync(ct);
        return items.Select(x => Map(x, links.Where(l => l.MarketingStrategyId == x.Id).Select(l => l.MarketingCustomerSegmentVersionId).ToArray())).ToArray();
    }

    public async Task<MarketingStrategyDto?> GetStrategyAsync(Guid companyId, Guid strategyId, CancellationToken ct)
    {
        var item = await db.MarketingStrategies.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == strategyId, ct);
        return item is null ? null : Map(item, await SegmentLinks(companyId, item.Id, ct));
    }

    public async Task<MarketingStrategyDto> CreateStrategyAsync(Guid companyId, Guid userId, SaveMarketingStrategyRequest request, CancellationToken ct)
    {
        var existing = await db.MarketingStrategies.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null) return Map(existing, await SegmentLinks(companyId, existing.Id, ct));
        await ValidateSegments(companyId, request.SegmentVersionIds, ct);
        var item = new MarketingStrategy(Guid.NewGuid(), companyId, request.Title, request.Summary, request.BusinessContext,
            request.ValidFromUtc, request.ValidToUtc, userId, request.SectionsJson, request.EvidenceReferencesJson,
            request.MissingEvidenceJson, request.IdempotencyKey);
        db.MarketingStrategies.Add(item);
        AddLinks(companyId, item.Id, request.SegmentVersionIds);
        await db.SaveChangesAsync(ct);
        await WriteStrategyAuditAsync(companyId, userId, "marketing.strategy.created", item, "succeeded", "Strategy draft created.", ct);
        return Map(item, request.SegmentVersionIds ?? []);
    }

    public async Task<MarketingStrategyDto?> UpdateStrategyAsync(Guid companyId, Guid userId, Guid strategyId, SaveMarketingStrategyRequest request, CancellationToken ct)
    {
        var item = await db.MarketingStrategies.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == strategyId, ct);
        if (item is null) return null;
        await ValidateSegments(companyId, request.SegmentVersionIds, ct);
        var before = JsonSerializer.Serialize(Map(item, await SegmentLinks(companyId, item.Id, ct)));
        item.Update(request.ExpectedVersion ?? throw new ArgumentException("ExpectedVersion is required."), request.Title, request.Summary,
            request.BusinessContext, request.ValidFromUtc, request.ValidToUtc, request.SectionsJson,
            request.EvidenceReferencesJson, request.MissingEvidenceJson);
        var old = await db.MarketingStrategySegments.Where(x => x.CompanyId == companyId && x.MarketingStrategyId == strategyId).ToListAsync(ct);
        db.MarketingStrategySegments.RemoveRange(old); AddLinks(companyId, item.Id, request.SegmentVersionIds);
        await db.SaveChangesAsync(ct);
        await WriteStrategyAuditAsync(companyId, userId, "marketing.strategy.updated", item, "succeeded", "Strategy draft updated.", ct, before, JsonSerializer.Serialize(Map(item, request.SegmentVersionIds ?? [])));
        return Map(item, request.SegmentVersionIds ?? []);
    }

    public async Task<MarketingStrategyDto?> SubmitStrategyAsync(Guid companyId, Guid userId, Guid strategyId, CancellationToken ct)
    {
        var item = await db.MarketingStrategies.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == strategyId, ct);
        if (item is null) return null;
        var approval = await approvals.CreateAsync(companyId, new CreateApprovalRequestCommand(
            "marketing_strategy", item.Id, "user", userId, "marketing_strategy_activation", null,
            "company_manager"), ct);
        item.Submit(approval.Id); await db.SaveChangesAsync(ct);
        await WriteStrategyAuditAsync(companyId, userId, "marketing.strategy.submitted", item, "succeeded", "Strategy submitted for approval.", ct);
        return Map(item, await SegmentLinks(companyId, item.Id, ct));
    }

    public async Task<MarketingStrategyDto?> ActivateStrategyAsync(Guid companyId, Guid userId, Guid strategyId, CancellationToken ct)
    {
        var item = await db.MarketingStrategies.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == strategyId, ct);
        if (item is null) return null;
        if (!item.ApprovalRequestId.HasValue) throw new InvalidOperationException("Strategy approval is required.");
        var approval = await approvals.GetAsync(companyId, item.ApprovalRequestId.Value, ct);
        if (string.Equals(approval.Status, "rejected", StringComparison.OrdinalIgnoreCase))
        {
            if (item.Status == MarketingStrategicStatuses.InReview) item.MarkRejected();
            await db.SaveChangesAsync(ct);
            await WriteStrategyAuditAsync(companyId, userId, "marketing.strategy.rejected", item, "denied", "Strategy approval was rejected.", ct);
            throw new InvalidOperationException("Strategy approval was rejected.");
        }
        if (!string.Equals(approval.Status, "approved", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Strategy approval is not complete.");
        await ValidateSegments(companyId, await SegmentLinks(companyId, item.Id, ct), ct);
        if (item.Status == MarketingStrategicStatuses.InReview) item.MarkApproved();
        var conflicts = await db.MarketingStrategies.Where(x => x.CompanyId == companyId && x.Id != item.Id && x.Status == MarketingStrategicStatuses.Active && x.ValidFromUtc < item.ValidToUtc && item.ValidFromUtc < x.ValidToUtc).ToListAsync(ct);
        foreach (var conflict in conflicts) conflict.Supersede();
        item.Activate(); await db.SaveChangesAsync(ct);
        await WriteStrategyAuditAsync(companyId, userId, "marketing.strategy.activated", item, "succeeded", "Approved strategy activated.", ct);
        return Map(item, await SegmentLinks(companyId, item.Id, ct));
    }

    public async Task<MarketingStrategyDto?> CancelStrategyAsync(Guid companyId, Guid userId, Guid strategyId,
        CancelMarketingStrategyRequest request, CancellationToken ct)
    {
        var item = await db.MarketingStrategies.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == strategyId, ct);
        if (item is null) return null;
        item.Cancel(request.ExpectedVersion);
        await db.SaveChangesAsync(ct);
        var rationale = request.Rationale?.Trim();
        if (string.IsNullOrWhiteSpace(rationale)) throw new ArgumentException("Cancellation rationale is required.");
        if (rationale.Length > 1000) throw new ArgumentException("Cancellation rationale cannot exceed 1000 characters.");
        await WriteStrategyAuditAsync(companyId, userId, "marketing.strategy.cancelled", item, "succeeded", rationale, ct);
        return Map(item, await SegmentLinks(companyId, item.Id, ct));
    }

    public async Task<MarketingStrategyProposalDto> PrepareStrategyProposalAsync(Guid companyId, Guid userId,
        PrepareMarketingStrategyProposalRequest request, CancellationToken ct)
    {
        if (request.ValidToUtc <= request.ValidFromUtc) throw new ArgumentException("Strategy validity is invalid.");
        if (request.TargetSegmentVersionIds.Count == 0) throw new ArgumentException("At least one approved target segment version is required.");
        await ValidateSegments(companyId, request.TargetSegmentVersionIds, ct);
        var result = await analysis.AnalyzeAsync(companyId, request.AgentId, userId,
            new RoleAgentAnalysisRequest(MarketingAgentAnalysisTypes.Planning, HorizonDays: 90,
                Objective: request.Objective), ct);
        return BuildProposal(request.AgentId, request.Title, request.Objective, request.ValidFromUtc,
            request.ValidToUtc, request.TargetSegmentVersionIds, result);
    }

    public async Task<MarketingStrategyDto> CommitStrategyProposalAsync(Guid companyId, Guid userId,
        CommitMarketingStrategyProposalRequest request, CancellationToken ct)
    {
        var existing = await db.MarketingStrategies.SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null) return Map(existing, await SegmentLinks(companyId, existing.Id, ct));
        if (request.ValidToUtc <= request.ValidFromUtc) throw new ArgumentException("Strategy validity is invalid.");
        await ValidateSegments(companyId, request.TargetSegmentVersionIds, ct);
        var run = await reasoning.GetRunAsync(companyId, request.AgentId, request.RunId, ct)
            ?? throw new InvalidOperationException("The Maya proposal run is unavailable.");
        ValidateGroundedRun(run);
        var recommendations = run.Claims.Select(x => new MarketingStrategyRecommendationDto(
            ClassifyArea(x.Text), x.Text, MarketingClaimClassification(x.Type), x.Confidence,
            request.TargetSegmentVersionIds, x.SourceIds)).ToArray();
        var sections = JsonSerializer.Serialize(new
        {
            proposalRunId = run.RunId,
            capabilityVersion = "1.0.0",
            promptVersion = "marketing-role-v1:on_demand",
            recommendations,
            assumptions = run.Uncertainty
        });
        var evidence = JsonSerializer.Serialize(new
        {
            proposalRunId = run.RunId,
            sourceIds = run.SourceIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            rationaleSummary = run.Summary,
            confidence = run.Confidence
        });
        var item = new MarketingStrategy(Guid.NewGuid(), companyId, request.Title, run.Summary,
            request.BusinessContext, request.ValidFromUtc, request.ValidToUtc, userId, sections, evidence,
            JsonSerializer.Serialize(run.MissingEvidence), request.IdempotencyKey);
        db.MarketingStrategies.Add(item);
        AddLinks(companyId, item.Id, request.TargetSegmentVersionIds);
        await db.SaveChangesAsync(ct);
        return Map(item, request.TargetSegmentVersionIds);
    }

    public async Task<MarketingDecompositionProposalDto> PrepareDecompositionAsync(Guid companyId,
        PrepareMarketingDecompositionRequest request, CancellationToken ct)
    {
        var gaps = await ValidateDecompositionAsync(companyId, request, ct);
        var normalized = JsonSerializer.Serialize(request);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return new MarketingDecompositionProposalDto(key, request.StrategyId, request.CampaignId,
            request.TargetSegmentVersionId, request.ObjectiveId, request.PlanName, request.PlanSummary,
            request.StartsUtc, request.EndsUtc, request.PlannedBudget, request.BudgetCurrency,
            request.Activities, gaps, gaps.Count == 0);
    }

    public async Task<MarketingDecompositionResultDto> CommitDecompositionAsync(Guid companyId, Guid userId,
        CommitMarketingDecompositionRequest request, CancellationToken ct)
    {
        var existing = await db.MarketingStrategyCampaignLinks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
        {
            var priorActivities = await db.SalesCampaignActivities.AsNoTracking().Where(x =>
                x.CompanyId == companyId && x.SalesCampaignId == existing.SalesCampaignId && x.CreatedUtc >= existing.CreatedUtc)
                .Select(x => x.Id).ToListAsync(ct);
            var priorCampaign = await db.SalesCampaigns.AsNoTracking().Include(x => x.Activities)
                .Include(x => x.Offers).Include(x => x.Contacts).SingleAsync(x =>
                x.CompanyId == companyId && x.Id == existing.SalesCampaignId, ct);
            return new MarketingDecompositionResultDto(existing.Id, existing.MarketingStrategyId,
                existing.MarketingPlanId, existing.SalesCampaignId, existing.MarketingCustomerSegmentVersionId,
                priorActivities, [], priorCampaign.ReadinessGaps(), existing.Status);
        }
        var gaps = await ValidateDecompositionAsync(companyId, request.Decomposition, ct);
        if (gaps.Count > 0) throw new InvalidOperationException(string.Join(" ", gaps));
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var r = request.Decomposition;
        var plan = new MarketingPlan(Guid.NewGuid(), companyId, r.PlanName, r.PlanSummary, r.StartsUtc,
            r.EndsUtc, r.PlannedBudget, r.BudgetCurrency, userId, null, $"decomposition:{request.IdempotencyKey}");
        db.MarketingPlans.Add(plan);
        db.MarketingPlanObjectives.Add(new MarketingPlanObjective(Guid.NewGuid(), companyId, plan.Id, r.ObjectiveId));
        var activities = new List<SalesCampaignActivity>();
        var byName = new Dictionary<string, SalesCampaignActivity>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in r.Activities.OrderBy(x => x.StartsUtc))
        {
            Guid? dependencyId = item.DependsOnName is null ? null : byName[item.DependsOnName].Id;
            var activity = new SalesCampaignActivity(Guid.NewGuid(), companyId, r.CampaignId, item.Name,
                item.ActivityType, item.Channel, "agent_assisted", item.StartsUtc, item.DueUtc, "UTC",
                userId, item.OwnerAgentId, dependencyId);
            db.SalesCampaignActivities.Add(activity); activities.Add(activity); byName[item.Name] = activity;
            if (item.ContentRequired)
                db.MarketingContentBriefs.Add(new MarketingContentBrief(Guid.NewGuid(), companyId,
                    $"{item.Name} content", $"Prepare content required for {item.Name}.", "Approved target segment",
                    item.Channel, "English", "Clear and helpful", "Use the approved campaign call to action.",
                    r.CampaignId, plan.Id, item.DueUtc, userId, item.OwnerAgentId, r.TargetSegmentVersionId,
                    "Support the linked campaign objective", "consideration", "Derived from the approved target segment",
                    "Use the approved campaign message", "[]", "Use the approved campaign offer", "[]", "[]", "{}",
                    "Follow the current brand profile", $"[\"{item.Channel}\"]", "{\"count\":3}",
                    "{\"citationsRequired\":true}", "{\"managerReviewRequired\":true}"));
        }
        var link = new MarketingStrategyCampaignLink(Guid.NewGuid(), companyId, r.StrategyId, plan.Id,
            r.CampaignId, r.TargetSegmentVersionId, request.IdempotencyKey);
        db.MarketingStrategyCampaignLinks.Add(link);
        await db.SaveChangesAsync(ct);
        var taskIds = new List<Guid>();
        foreach (var activity in activities)
        {
            var payload = new Dictionary<string, JsonNode?> { ["strategyId"] = JsonValue.Create(r.StrategyId),
                ["marketingPlanId"] = JsonValue.Create(plan.Id), ["salesCampaignId"] = JsonValue.Create(r.CampaignId),
                ["campaignActivityId"] = JsonValue.Create(activity.Id), ["targetSegmentVersionId"] = JsonValue.Create(r.TargetSegmentVersionId) };
            var task = await tasks.CreateTaskAsync(companyId, new CreateTaskCommand("marketing_campaign_activity",
                activity.Name, "Complete the linked campaign activity and attach reviewable evidence.", "normal",
                activity.DueUtc, activity.OwnerAgentId, payload, RationaleSummary: r.PlanSummary,
                CorrelationId: $"marketing-decomposition:{link.Id:N}"), ct);
            taskIds.Add(task.Id);
        }
        await transaction.CommitAsync(ct);
        var campaign = await db.SalesCampaigns.AsNoTracking().Include(x => x.Activities)
            .Include(x => x.Offers).Include(x => x.Contacts)
            .SingleAsync(x => x.CompanyId == companyId && x.Id == r.CampaignId, ct);
        return new MarketingDecompositionResultDto(link.Id, r.StrategyId, plan.Id, r.CampaignId,
            r.TargetSegmentVersionId, activities.Select(x => x.Id).ToArray(), taskIds, campaign.ReadinessGaps(), link.Status);
    }

    public async Task<IReadOnlyList<MarketingIntelligenceDto>> ListIntelligenceAsync(Guid companyId, bool freshnessQueue, CancellationToken ct) =>
        await db.MarketingIntelligenceRecords.AsNoTracking().Where(x => x.CompanyId == companyId && !x.IsArchived && (!freshnessQueue || x.ReviewDueUtc <= DateTime.UtcNow))
            .OrderBy(x => x.ReviewDueUtc).Select(x => new MarketingIntelligenceDto(x.Id, x.Kind, x.Subject, x.Summary, x.Classification, x.Confidence, x.SourceType, x.SourceReference, x.ObservedUtc, x.ReviewDueUtc, x.DimensionsJson, x.ReviewStatus, x.IsArchived, x.Version)).ToListAsync(ct);

    public async Task<MarketingIntelligenceDto?> GetIntelligenceAsync(Guid companyId, Guid intelligenceId, CancellationToken ct)
    {
        var item = await db.MarketingIntelligenceRecords.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == intelligenceId, ct);
        return item is null ? null : Map(item);
    }

    public async Task<MarketingIntelligenceDto> CreateIntelligenceAsync(Guid companyId, Guid userId, CreateMarketingIntelligenceRequest r, CancellationToken ct)
    {
        await ValidateIntelligenceSourceAsync(companyId, r.SourceType, r.SourceReference, ct);
        var x = new MarketingIntelligenceRecord(Guid.NewGuid(), companyId, r.Kind, r.Subject, r.Summary, r.Classification, r.Confidence, r.SourceType, r.SourceReference, r.ObservedUtc, r.ReviewDueUtc, r.DimensionsJson, userId);
        db.MarketingIntelligenceRecords.Add(x); await db.SaveChangesAsync(ct);
        await WriteAuditAsync(companyId, userId, "marketing.intelligence.created", x, AuditEventOutcomes.Succeeded,
            "A dated Marketing intelligence record was created with its source and uncertainty classification.", ct);
        return Map(x);
    }

    public async Task<MarketingIntelligenceDto?> UpdateIntelligenceAsync(Guid companyId, Guid userId, Guid intelligenceId,
        UpdateMarketingIntelligenceRequest r, CancellationToken ct)
    {
        var x = await db.MarketingIntelligenceRecords.SingleOrDefaultAsync(i => i.CompanyId == companyId && i.Id == intelligenceId, ct);
        if (x is null) return null;
        await ValidateIntelligenceSourceAsync(companyId, r.SourceType, r.SourceReference, ct);
        var before = JsonSerializer.Serialize(Map(x));
        x.Update(r.ExpectedVersion, r.Subject, r.Summary, r.Classification, r.Confidence, r.SourceType,
            r.SourceReference, r.ObservedUtc, r.ReviewDueUtc, r.DimensionsJson);
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync(companyId, userId, "marketing.intelligence.updated", x, AuditEventOutcomes.Succeeded,
            "The intelligence record was updated and returned to review.", ct, before, JsonSerializer.Serialize(Map(x)));
        return Map(x);
    }

    public async Task<MarketingIntelligenceDto?> ReviewIntelligenceAsync(Guid companyId, Guid userId,
        Guid intelligenceId, ReviewMarketingIntelligenceRequest r, CancellationToken ct)
    {
        var x = await db.MarketingIntelligenceRecords.SingleOrDefaultAsync(i => i.CompanyId == companyId && i.Id == intelligenceId, ct);
        if (x is null) return null;
        if (x.Version != r.ExpectedVersion) throw new InvalidOperationException("The intelligence record changed. Refresh and try again.");
        var before = JsonSerializer.Serialize(Map(x));
        x.Review(r.Verified);
        var after = JsonSerializer.Serialize(Map(x));
        var next = (await db.MarketingIntelligenceReviews.Where(review => review.CompanyId == companyId &&
            review.MarketingIntelligenceRecordId == intelligenceId).MaxAsync(review => (int?)review.ReviewNumber, ct) ?? 0) + 1;
        db.MarketingIntelligenceReviews.Add(new MarketingIntelligenceReview(Guid.NewGuid(), companyId,
            intelligenceId, next, userId, r.Verified ? "verified" : "needs_evidence", r.Rationale, before, after));
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync(companyId, userId, "marketing.intelligence.reviewed", x,
            r.Verified ? AuditEventOutcomes.Approved : AuditEventOutcomes.Rejected, r.Rationale, ct, before, after);
        return Map(x);
    }

    public async Task<MarketingIntelligenceDto?> ArchiveIntelligenceAsync(Guid companyId, Guid userId,
        Guid intelligenceId, ArchiveMarketingIntelligenceRequest r, CancellationToken ct)
    {
        var x = await db.MarketingIntelligenceRecords.SingleOrDefaultAsync(i => i.CompanyId == companyId && i.Id == intelligenceId, ct);
        if (x is null) return null;
        var before = JsonSerializer.Serialize(Map(x)); x.Archive(r.ExpectedVersion); await db.SaveChangesAsync(ct);
        await WriteAuditAsync(companyId, userId, "marketing.intelligence.archived", x, AuditEventOutcomes.Succeeded,
            "The intelligence record was archived without deleting its review history.", ct, before, JsonSerializer.Serialize(Map(x)));
        return Map(x);
    }

    public async Task<IReadOnlyList<MarketingIntelligenceReviewDto>> ListIntelligenceReviewsAsync(Guid companyId,
        Guid intelligenceId, CancellationToken ct)
    {
        if (!await db.MarketingIntelligenceRecords.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == intelligenceId, ct))
            return [];
        return await db.MarketingIntelligenceReviews.AsNoTracking().Where(x => x.CompanyId == companyId &&
                x.MarketingIntelligenceRecordId == intelligenceId).OrderByDescending(x => x.ReviewNumber)
            .Select(x => new MarketingIntelligenceReviewDto(x.Id, x.MarketingIntelligenceRecordId, x.ReviewNumber,
                x.ReviewerUserId, x.Outcome, x.Rationale, x.BeforeJson, x.AfterJson, x.CreatedUtc)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MarketingSegmentDto>> ListSegmentsAsync(Guid companyId, CancellationToken ct)
    {
        var segments = await db.MarketingCustomerSegments.AsNoTracking().Where(x => x.CompanyId == companyId && !x.IsArchived).OrderBy(x => x.Name).ToListAsync(ct);
        var versions = await db.MarketingCustomerSegmentVersions.AsNoTracking().Where(x => x.CompanyId == companyId).OrderByDescending(x => x.VersionNumber).ToListAsync(ct);
        return segments.Select(x => new MarketingSegmentDto(x.Id, x.Name, x.Description, x.IsArchived, versions.Where(v => v.MarketingCustomerSegmentId == x.Id).Select(Map).ToArray())).ToArray();
    }
    public async Task<MarketingSegmentProposalDto> PrepareSegmentProposalAsync(Guid companyId, Guid userId,
        PrepareMarketingSegmentProposalRequest request, CancellationToken ct)
    {
        var result = await analysis.AnalyzeAsync(companyId, request.AgentId, userId,
            new RoleAgentAnalysisRequest(MarketingAgentAnalysisTypes.AudienceIntelligence, HorizonDays: 180,
                Objective: request.Objective, IsBootstrap: true), ct);
        var grounding = MarketingSegmentProposalGrounding.Evaluate(result.Status, result.Claims,
            result.Sources.Select(x => x.Id).ToArray(), result.FailureCode);
        var claims = grounding.Claims.Select(x => new MarketingStrategyRecommendationDto(
            ClassifyArea(x.Text), x.Text, MarketingClaimClassification(x.Type), x.Confidence, [], x.SourceIds)).ToArray();
        var requiresReview = result.RequiresReview || grounding.RejectedClaimCount > 0 || grounding.UsedBootstrapFallback;
        var structured = JsonSerializer.Serialize(new
        {
            segmentationBasis = claims.Where(x => x.Area is "market" or "customer").ToArray(),
            definition = claims.Where(x => x.Area == "positioning").ToArray(),
            needsJobsBarriers = claims.Where(x => x.Recommendation.Contains("need", StringComparison.OrdinalIgnoreCase) || x.Recommendation.Contains("barrier", StringComparison.OrdinalIgnoreCase)).ToArray(),
            behaviours = claims.Where(x => x.Recommendation.Contains("behav", StringComparison.OrdinalIgnoreCase)).ToArray(),
            channelPresence = claims.Where(x => x.Area == "place").ToArray(),
            priceSensitivity = claims.Where(x => x.Area == "price").ToArray(),
            sizeAndEconomics = claims.Where(x => x.Recommendation.Contains("size", StringComparison.OrdinalIgnoreCase) || x.Recommendation.Contains("economic", StringComparison.OrdinalIgnoreCase)).ToArray(),
            alternatives = claims.Where(x => x.Area == "competition").ToArray(),
            risks = result.MissingEvidence.Select(x => new { risk = x, classification = "evidence_gap" }).ToArray(),
            missingEvidence = result.MissingEvidence,
            targetRecommendation = claims.Where(x => x.Recommendation.Contains("target", StringComparison.OrdinalIgnoreCase)).ToArray(),
            downstreamImplications = claims.Where(x => x.Area is "product" or "price" or "place" or "promotion").ToArray()
        });
        var summary = grounding.UsedBootstrapFallback
            ? "Maya created a bootstrap segment hypothesis. Add objective, customer, market, size, channel, pricing, and economics evidence before submitting it for approval."
            : result.Summary;
        return new MarketingSegmentProposalDto(result.RunId, request.AgentId, request.SegmentName, summary,
            claims, result.Sources, result.MissingEvidence.Concat(grounding.RejectedClaimCount > 0
                    ? [$"Maya returned {grounding.RejectedClaimCount} unsupported claim(s); they were excluded from the draft."] : [])
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), result.Confidence, requiresReview,
            grounding.CanCreateDraft, "2.0.0", "marketing-role-v2:audience_intelligence", structured);
    }
    public async Task<MarketingSegmentVersionDto> CommitSegmentProposalAsync(Guid companyId, Guid userId,
        CommitMarketingSegmentProposalRequest request, CancellationToken ct)
    {
        var existing = await db.MarketingCustomerSegmentVersions.SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null) return Map(existing);
        var run = await reasoning.GetRunAsync(companyId, request.AgentId, request.RunId, ct)
            ?? throw new InvalidOperationException("The Maya segment proposal run is unavailable.");
        var grounding = MarketingSegmentProposalGrounding.Evaluate(run.Status, run.Claims, run.SourceIds,
            run.FailureCode);
        if (!grounding.CanCreateDraft)
            throw new InvalidOperationException("The Maya segment proposal has no reviewable grounded content. Retry the proposal before creating a draft.");
        JsonNode? submittedEvidence;
        try { submittedEvidence = JsonNode.Parse(request.Version.EvidenceJson); }
        catch (JsonException exception) { throw new ArgumentException("Segment evidence must be valid JSON.", exception); }
        var evidence = JsonSerializer.Serialize(new { proposalRunId = run.RunId, sourceIds = run.SourceIds,
            claims = run.Claims, missingEvidence = run.MissingEvidence, submittedEvidence });
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var segment = await CreateSegmentAsync(companyId, userId,
            new CreateMarketingSegmentRequest(request.SegmentName, request.Description), ct);
        var versionRequest = request.Version with { EvidenceJson = evidence, IdempotencyKey = request.IdempotencyKey };
        var version = await CreateSegmentVersionAsync(companyId, userId, segment.Id, versionRequest, ct)
            ?? throw new InvalidOperationException("The segment proposal could not be committed.");
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, userId,
            "marketing.segment_proposal.committed", "marketing_segment_version", version.Id.ToString("N"),
            "succeeded", "A grounded Maya segment proposal was committed as a draft version.",
            DataSources: run.SourceIds, Metadata: new Dictionary<string, string?>
            { ["proposalRunId"] = run.RunId.ToString("N"), ["segmentId"] = segment.Id.ToString("N") }), ct);
        await transaction.CommitAsync(ct);
        return version;
    }
    public async Task<MarketingSegmentDto> CreateSegmentAsync(Guid companyId, Guid userId, CreateMarketingSegmentRequest r, CancellationToken ct)
    {
        var x = new MarketingCustomerSegment(Guid.NewGuid(), companyId, r.Name, r.Description, userId); db.MarketingCustomerSegments.Add(x); await db.SaveChangesAsync(ct);
        return new MarketingSegmentDto(x.Id, x.Name, x.Description, x.IsArchived, []);
    }
    public async Task<MarketingSegmentVersionDto?> CreateSegmentVersionAsync(Guid companyId, Guid userId, Guid segmentId, CreateMarketingSegmentVersionRequest r, CancellationToken ct)
    {
        if (!await db.MarketingCustomerSegments.AnyAsync(x => x.CompanyId == companyId && x.Id == segmentId && !x.IsArchived, ct)) return null;
        var existing = await db.MarketingCustomerSegmentVersions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == r.IdempotencyKey, ct); if (existing is not null) return Map(existing);
        var version = (await db.MarketingCustomerSegmentVersions.Where(x => x.CompanyId == companyId && x.MarketingCustomerSegmentId == segmentId).MaxAsync(x => (int?)x.VersionNumber, ct) ?? 0) + 1;
        var score = SegmentAttractivenessPolicy.Calculate(r.ScoreDimensions);
        var x = new MarketingCustomerSegmentVersion(Guid.NewGuid(), companyId, segmentId, version, r.CriteriaJson, r.NeedsJson, r.BehaviorsJson, r.ChannelsJson, r.PricingJson, r.SizeLow, r.SizeHigh, r.SizeMethod, r.Confidence, r.EconomicsJson, r.ScorecardJson, score, r.EvidenceJson, r.EvidenceObservedUtc, userId, r.IdempotencyKey);
        db.MarketingCustomerSegmentVersions.Add(x);
        db.MarketingSegmentDimensions.AddRange(BuildSegmentDimensions(companyId, x.Id, r));
        if (r.SizeEstimates is not null) db.MarketingSegmentSizeEstimates.AddRange(r.SizeEstimates.Select(e =>
            new MarketingSegmentSizeEstimate(Guid.NewGuid(), companyId, x.Id, e.Low, e.High, e.Unit, e.Period,
                e.Geography, e.Currency, e.Method, e.AssumptionsJson, e.SourceIdsJson, e.Confidence,
                e.ObservedUtc, e.AsOfUtc, e.Classification)));
        if (r.EconomicEstimates is not null) db.MarketingSegmentEconomicEstimates.AddRange(r.EconomicEstimates.Select(e =>
            new MarketingSegmentEconomicEstimate(Guid.NewGuid(), companyId, x.Id, e.MetricCode, e.Low, e.High,
                e.Unit, e.Currency, e.Method, e.Confidence, e.SourceIdsJson, e.ObservedUtc, e.Classification)));
        if (r.ScorePolicy is not null)
        {
            var policy = new MarketingSegmentScorePolicy(Guid.NewGuid(), companyId, x.Id, r.ScorePolicy.TargetThreshold,
                r.ScorePolicy.MissingEvidenceBehavior, r.ScorePolicy.ExclusionsJson, r.ScorePolicy.RiskJson);
            db.MarketingSegmentScorePolicies.Add(policy);
            db.MarketingSegmentScoreDimensions.AddRange(r.ScorePolicy.Dimensions.Select(d =>
                new MarketingSegmentScoreDimension(Guid.NewGuid(), companyId, policy.Id, d.Code, d.Weight, d.Score, d.EvidenceJson)));
        }
        await db.SaveChangesAsync(ct); return Map(x);
    }
    public async Task<MarketingSegmentVersionDto?> SubmitSegmentVersionAsync(Guid companyId, Guid userId, Guid versionId, CancellationToken ct)
    {
        var x = await db.MarketingCustomerSegmentVersions.SingleOrDefaultAsync(v => v.CompanyId == companyId && v.Id == versionId, ct); if (x is null) return null;
        var approval = await approvals.CreateAsync(companyId, new CreateApprovalRequestCommand("marketing_segment_version", x.Id, "user", userId, "marketing_target_selection", null, "company_manager"), ct);
        x.Submit(approval.Id); await db.SaveChangesAsync(ct); return Map(x);
    }
    public async Task<MarketingSegmentVersionDto?> ActivateTargetAsync(Guid companyId, Guid versionId, ActivateMarketingTargetRequest request, CancellationToken ct)
    {
        var x = await db.MarketingCustomerSegmentVersions.SingleOrDefaultAsync(v => v.CompanyId == companyId && v.Id == versionId, ct); if (x is null) return null;
        if (!x.ApprovalRequestId.HasValue) throw new InvalidOperationException("Target selection approval is required.");
        var approval = await approvals.GetAsync(companyId, x.ApprovalRequestId.Value, ct); if (!string.Equals(approval.Status, "approved", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Target selection approval is not complete.");
        if (x.Status == MarketingStrategicStatuses.InReview) x.MarkApproved();
        var active = await db.MarketingCustomerSegmentVersions.Where(v => v.CompanyId == companyId && v.MarketingCustomerSegmentId == x.MarketingCustomerSegmentId && v.Id != x.Id && v.Status == MarketingStrategicStatuses.Active).ToListAsync(ct);
        foreach (var prior in active) prior.Supersede(); x.ActivateTarget(request.TargetState, request.Rationale); await db.SaveChangesAsync(ct); return Map(x);
    }

    public async Task<MarketingSegmentImpactDto?> GetSegmentImpactAsync(Guid companyId, Guid versionId, CancellationToken ct)
    {
        var version = await db.MarketingCustomerSegmentVersions.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.Id == versionId, ct);
        if (version is null) return null;
        var latestVersion = await db.MarketingCustomerSegmentVersions.AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.MarketingCustomerSegmentId == version.MarketingCustomerSegmentId).MaxAsync(x => x.VersionNumber, ct);
        var isCurrent = version.VersionNumber == latestVersion;
        var reason = isCurrent ? "This artifact uses the current approved segment assumptions."
            : "This artifact references a superseded segment version and needs an explicit review; it was not rewritten automatically.";
        var artifacts = new List<MarketingSegmentImpactItemDto>();
        var strategies = await (from link in db.MarketingStrategySegments.AsNoTracking()
            join strategy in db.MarketingStrategies.AsNoTracking() on new { link.CompanyId, Id = link.MarketingStrategyId } equals new { strategy.CompanyId, strategy.Id }
            where link.CompanyId == companyId && link.MarketingCustomerSegmentVersionId == versionId
            select new { strategy.Id, strategy.Title, strategy.Status }).ToListAsync(ct);
        artifacts.AddRange(strategies.Select(x => new MarketingSegmentImpactItemDto("strategy", x.Id, x.Title, x.Status, reason)));
        var links = await db.MarketingStrategyCampaignLinks.AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.MarketingCustomerSegmentVersionId == versionId).ToListAsync(ct);
        var campaignIds = links.Select(x => x.SalesCampaignId).Distinct().ToArray();
        var planIds = links.Select(x => x.MarketingPlanId).Distinct().ToArray();
        var campaigns = await db.SalesCampaigns.AsNoTracking().Where(x => x.CompanyId == companyId && campaignIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Status }).ToListAsync(ct);
        artifacts.AddRange(campaigns.Select(x => new MarketingSegmentImpactItemDto("campaign", x.Id, x.Name, x.Status, reason)));
        var plans = await db.MarketingPlans.AsNoTracking().Where(x => x.CompanyId == companyId && planIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Status }).ToListAsync(ct);
        artifacts.AddRange(plans.Select(x => new MarketingSegmentImpactItemDto("plan", x.Id, x.Name, x.Status, reason)));
        var content = await db.MarketingContentBriefs.AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.MarketingCustomerSegmentVersionId == versionId).Select(x => new { x.Id, x.Title, x.Status }).ToListAsync(ct);
        artifacts.AddRange(content.Select(x => new MarketingSegmentImpactItemDto("content_brief", x.Id, x.Title, x.Status, reason)));
        var journeys = await db.MarketingLifecycleJourneys.AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.MarketingCustomerSegmentVersionId == versionId).Select(x => new { x.Id, x.Name, x.Status }).ToListAsync(ct);
        artifacts.AddRange(journeys.Select(x => new MarketingSegmentImpactItemDto("lifecycle_journey", x.Id, x.Name, x.Status, reason)));
        var experiments = await db.MarketingExperiments.AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.SalesCampaignId.HasValue && campaignIds.Contains(x.SalesCampaignId.Value)).Select(x => new { x.Id, x.Name, x.Status }).ToListAsync(ct);
        artifacts.AddRange(experiments.Select(x => new MarketingSegmentImpactItemDto("experiment", x.Id, x.Name, x.Status, reason)));
        var objectiveIds = await db.MarketingPlanObjectives.AsNoTracking().Where(x => x.CompanyId == companyId && planIds.Contains(x.MarketingPlanId))
            .Select(x => x.MarketingObjectiveId).Distinct().ToListAsync(ct);
        var objectives = await db.MarketingObjectives.AsNoTracking().Where(x => x.CompanyId == companyId && objectiveIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Status }).ToListAsync(ct);
        artifacts.AddRange(objectives.Select(x => new MarketingSegmentImpactItemDto("objective", x.Id, x.Name, x.Status, reason)));
        var explicitMappings = await db.MarketingSegmentArtifactMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MarketingCustomerSegmentVersionId == versionId).ToListAsync(ct);
        artifacts.AddRange(explicitMappings.Select(x => new MarketingSegmentImpactItemDto(x.MappingType, x.ArtifactId,
            x.Label, "mapped", reason)));
        return new MarketingSegmentImpactDto(versionId, isCurrent, !isCurrent && artifacts.Count > 0,
            artifacts.DistinctBy(x => new { x.ArtifactType, x.ArtifactId }).OrderBy(x => x.ArtifactType).ThenBy(x => x.Label).ToArray(), DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<MarketingSegmentDimensionDto>> ListSegmentDimensionsAsync(Guid companyId,
        Guid versionId, CancellationToken ct)
    {
        if (!await db.MarketingCustomerSegmentVersions.AsNoTracking().AnyAsync(x => x.CompanyId == companyId &&
                x.Id == versionId, ct)) return [];
        return await db.MarketingSegmentDimensions.AsNoTracking().Where(x => x.CompanyId == companyId &&
                x.MarketingCustomerSegmentVersionId == versionId).OrderBy(x => x.Category).ThenBy(x => x.Path)
            .Select(x => new MarketingSegmentDimensionDto(x.Id, x.MarketingCustomerSegmentVersionId, x.Category,
                x.Path, x.Value, x.Classification, x.NumericValue)).ToListAsync(ct);
    }

    public async Task<MarketingSegmentDecisionDataDto?> GetSegmentDecisionDataAsync(Guid companyId, Guid versionId, CancellationToken ct)
    {
        if (!await db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == versionId, ct)) return null;
        var sizes = await db.MarketingSegmentSizeEstimates.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MarketingCustomerSegmentVersionId == versionId).OrderByDescending(x => x.AsOfUtc).ToListAsync(ct);
        var economics = await db.MarketingSegmentEconomicEstimates.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MarketingCustomerSegmentVersionId == versionId).OrderBy(x => x.MetricCode).ToListAsync(ct);
        var policy = await db.MarketingSegmentScorePolicies.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.MarketingCustomerSegmentVersionId == versionId, ct);
        var dimensions = policy is null ? [] : await db.MarketingSegmentScoreDimensions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MarketingSegmentScorePolicyId == policy.Id).OrderBy(x => x.Code).ToListAsync(ct);
        var decisions = await db.MarketingSegmentTargetDecisions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MarketingCustomerSegmentVersionId == versionId).OrderByDescending(x => x.DecidedUtc).ToListAsync(ct);
        var mappings = await db.MarketingSegmentArtifactMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MarketingCustomerSegmentVersionId == versionId).OrderBy(x => x.MappingType).ToListAsync(ct);
        var score = CalculateScore(policy, dimensions);
        return new MarketingSegmentDecisionDataDto(versionId,
            sizes.Select(x => new MarketingSegmentSizeEstimateDto(x.Id, versionId, x.Low, x.High, x.Unit, x.Period, x.Geography, x.Currency, x.Method, x.AssumptionsJson, x.SourceIdsJson, x.Confidence, x.ObservedUtc, x.AsOfUtc, x.Classification)).ToArray(),
            economics.Select(x => new MarketingSegmentEconomicEstimateDto(x.Id, versionId, x.MetricCode, x.Low, x.High, x.Unit, x.Currency, x.Method, x.Confidence, x.SourceIdsJson, x.ObservedUtc, x.Classification)).ToArray(),
            policy is null ? null : new MarketingSegmentScorePolicyDto(policy.Id, versionId, policy.TargetThreshold, policy.MissingEvidenceBehavior, policy.ExclusionsJson, policy.RiskJson, score,
                score.HasValue ? score >= policy.TargetThreshold ? "meets_threshold" : "below_threshold" : "needs_review",
                dimensions.Select(x => new MarketingSegmentScoreDimensionDto(x.Id, x.Code, x.Weight, x.Score, x.EvidenceJson)).ToArray()),
            decisions.Select(MapDecision).ToArray(), mappings.Select(MapMapping).ToArray());
    }

    public async Task<MarketingSegmentTargetDecisionDto?> RecommendTargetAsync(Guid companyId, Guid actorId, Guid versionId,
        CreateMarketingSegmentTargetDecisionRequest request, CancellationToken ct)
    {
        if (!await db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == versionId, ct)) return null;
        var existing = await db.MarketingSegmentTargetDecisions.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null) return MapDecision(existing);
        var item = new MarketingSegmentTargetDecision(Guid.NewGuid(), companyId, versionId, request.TargetType,
            request.Rationale, request.ExpectedImpactJson, request.Confidence, request.RisksJson, request.ReviewUtc,
            "recommended", actorId, null, request.IdempotencyKey);
        db.MarketingSegmentTargetDecisions.Add(item); await db.SaveChangesAsync(ct); return MapDecision(item);
    }

    public async Task<MarketingSegmentArtifactMappingDto?> MapSegmentArtifactAsync(Guid companyId, Guid versionId,
        CreateMarketingSegmentArtifactMappingRequest request, CancellationToken ct)
    {
        if (!await db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == versionId, ct)) return null;
        var existing = await db.MarketingSegmentArtifactMappings.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null) return MapMapping(existing);
        var exists = request.MappingType.ToLowerInvariant() switch
        {
            "icp" => await db.IdealCustomerProfiles.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == request.ArtifactId, ct),
            "campaign_audience" => await db.SalesCampaignAudienceSegments.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == request.ArtifactId, ct),
            "qualification" => await db.MarketingQualificationDefinitions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == request.ArtifactId, ct),
            "sales_handoff" => await db.MarketingSalesHandoffs.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == request.ArtifactId, ct),
            _ => false
        };
        if (!exists) throw new InvalidOperationException("The mapped artifact is unavailable in this company or mapping type is unsupported.");
        var item = new MarketingSegmentArtifactMapping(Guid.NewGuid(), companyId, versionId,
            request.MappingType.ToLowerInvariant(), request.ArtifactId, request.Label, request.IdempotencyKey);
        db.MarketingSegmentArtifactMappings.Add(item); await db.SaveChangesAsync(ct); return MapMapping(item);
    }

    private static decimal? CalculateScore(MarketingSegmentScorePolicy? policy, IReadOnlyList<MarketingSegmentScoreDimension> dimensions)
    {
        if (policy is null || dimensions.Count == 0) return null;
        if (policy.MissingEvidenceBehavior == "needs_review" && dimensions.Any(x => !x.Score.HasValue)) return null;
        var included = policy.MissingEvidenceBehavior == "exclude" ? dimensions.Where(x => x.Score.HasValue).ToArray() : dimensions.ToArray();
        var weight = included.Sum(x => x.Weight); if (weight <= 0) return null;
        return decimal.Round(included.Sum(x => (x.Score ?? 0m) * x.Weight) / weight, 2, MidpointRounding.AwayFromZero);
    }
    private static MarketingSegmentTargetDecisionDto MapDecision(MarketingSegmentTargetDecision x) => new(x.Id,
        x.MarketingCustomerSegmentVersionId, x.TargetType, x.Rationale, x.ExpectedImpactJson, x.Confidence,
        x.RisksJson, x.ReviewUtc, x.ApprovalStatus, x.ActorId, x.ApprovalRequestId, x.IdempotencyKey, x.DecidedUtc);
    private static MarketingSegmentArtifactMappingDto MapMapping(MarketingSegmentArtifactMapping x) => new(x.Id,
        x.MarketingCustomerSegmentVersionId, x.MappingType, x.ArtifactId, x.Label, x.CreatedUtc);

    private async Task ValidateSegments(Guid companyId, IReadOnlyList<Guid>? ids, CancellationToken ct)
    {
        if (ids is null or { Count: 0 }) return;
        var approved = await db.MarketingCustomerSegmentVersions.CountAsync(x => x.CompanyId == companyId && ids.Contains(x.Id) && (x.Status == MarketingStrategicStatuses.Approved || x.Status == MarketingStrategicStatuses.Active), ct);
        if (approved != ids.Distinct().Count()) throw new InvalidOperationException("Every strategy segment must reference an approved company segment version.");
    }

    private static IReadOnlyList<MarketingSegmentDimension> BuildSegmentDimensions(Guid companyId, Guid versionId,
        CreateMarketingSegmentVersionRequest request)
    {
        var dimensions = new List<MarketingSegmentDimension>();
        AddJson("criteria", request.CriteriaJson, "submitted");
        AddJson("needs", request.NeedsJson, "submitted");
        AddJson("behavior", request.BehaviorsJson, "submitted");
        AddJson("channel_presence", request.ChannelsJson, "estimated");
        AddJson("price_sensitivity", request.PricingJson, "estimated");
        AddJson("economics", request.EconomicsJson, "estimated");
        AddJson("scorecard", request.ScorecardJson, "computed");
        AddJson("evidence", request.EvidenceJson, "submitted");
        foreach (var score in request.ScoreDimensions.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            Add("score_dimension", score.Key, score.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "computed", score.Value);
        return dimensions;

        void AddJson(string category, string json, string classification)
        {
            JsonNode node;
            try { node = JsonNode.Parse(json) ?? throw new JsonException("The JSON value is empty."); }
            catch (JsonException exception) { throw new ArgumentException($"Segment {category} must be valid JSON.", exception); }
            Flatten(category, "$", node, classification, 0);
        }
        void Flatten(string category, string path, JsonNode node, string classification, int depth)
        {
            if (depth > 12 || dimensions.Count >= 1000)
                throw new ArgumentException("Segment dimensions exceed the supported depth or item count.");
            if (node is JsonObject obj)
            {
                foreach (var property in obj.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                    if (property.Value is not null) Flatten(category, $"{path}.{property.Key}", property.Value, classification, depth + 1);
                return;
            }
            if (node is JsonArray array)
            {
                for (var index = 0; index < array.Count; index++)
                    if (array[index] is not null) Flatten(category, $"{path}[{index}]", array[index]!, classification, depth + 1);
                return;
            }
            var value = node.ToJsonString().Trim('"');
            decimal? numeric = decimal.TryParse(value, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            Add(category, path, value, classification, numeric);
        }
        void Add(string category, string path, string value, string classification, decimal? numeric)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            dimensions.Add(new MarketingSegmentDimension(Guid.NewGuid(), companyId, versionId, category, path,
                value.Length > 4000 ? value[..4000] : value, classification, numeric));
        }
    }
    private async Task<IReadOnlyList<string>> ValidateDecompositionAsync(Guid companyId,
        PrepareMarketingDecompositionRequest r, CancellationToken ct)
    {
        var gaps = new List<string>();
        var strategy = await db.MarketingStrategies.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == r.StrategyId, ct);
        if (strategy is null || strategy.Status is not (MarketingStrategicStatuses.Approved or MarketingStrategicStatuses.Active))
            gaps.Add("Choose an approved or active Marketing strategy.");
        var segmentLinked = await db.MarketingStrategySegments.AsNoTracking().AnyAsync(x => x.CompanyId == companyId &&
            x.MarketingStrategyId == r.StrategyId && x.MarketingCustomerSegmentVersionId == r.TargetSegmentVersionId, ct);
        var segmentApproved = await db.MarketingCustomerSegmentVersions.AsNoTracking().AnyAsync(x => x.CompanyId == companyId &&
            x.Id == r.TargetSegmentVersionId && (x.Status == MarketingStrategicStatuses.Approved || x.Status == MarketingStrategicStatuses.Active), ct);
        if (!segmentLinked || !segmentApproved) gaps.Add("The campaign must use an approved target segment version linked to the strategy.");
        var campaign = await db.SalesCampaigns.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == r.CampaignId, ct);
        if (campaign is null) gaps.Add("Choose a Sales campaign owned by this company.");
        else if (campaign.LifecycleStatus is not (CampaignLifecycleStatuses.Draft or CampaignLifecycleStatuses.Planning)) gaps.Add("Only a draft or planning Sales campaign can receive a decomposition.");
        if (!await db.MarketingObjectives.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == r.ObjectiveId, ct)) gaps.Add("Choose a company Marketing objective.");
        if (string.IsNullOrWhiteSpace(r.PlanName) || string.IsNullOrWhiteSpace(r.PlanSummary)) gaps.Add("Plan name and summary are required.");
        if (r.EndsUtc <= r.StartsUtc) gaps.Add("Plan end date must be after its start date.");
        if (r.PlannedBudget < 0 || (r.PlannedBudget.HasValue && r.BudgetCurrency.Trim().Length != 3)) gaps.Add("Budget must be non-negative and use a three-letter currency.");
        if (r.Activities.Count == 0) gaps.Add("Add at least one campaign activity.");
        if (r.Activities.GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) gaps.Add("Campaign activity names must be unique.");
        var names = r.Activities.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in r.Activities)
        {
            if (item.StartsUtc < r.StartsUtc || item.DueUtc > r.EndsUtc || item.DueUtc < item.StartsUtc) gaps.Add($"Activity '{item.Name}' must fit inside the plan date range.");
            if (item.DependsOnName is not null && (!names.Contains(item.DependsOnName) || item.DependsOnName.Equals(item.Name, StringComparison.OrdinalIgnoreCase))) gaps.Add($"Activity '{item.Name}' has an invalid dependency.");
            if (item.OwnerAgentId.HasValue && !await db.Agents.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == item.OwnerAgentId, ct)) gaps.Add($"Activity '{item.Name}' has an unavailable owner.");
        }
        return gaps.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static MarketingStrategyProposalDto BuildProposal(Guid agentId, string title, string objective,
        DateTime fromUtc, DateTime toUtc, IReadOnlyList<Guid> segments, RoleAgentAnalysisResult result)
    {
        var allowedSources = result.Sources.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalid = result.Claims.Any(x => x.SourceIds.Count == 0 || x.SourceIds.Any(id => !allowedSources.Contains(id)) ||
            x.Type is not ("confirmed_fact" or "inference" or "unknown"));
        var items = result.Claims.Where(x => !invalid).Select(x => new MarketingStrategyRecommendationDto(
            ClassifyArea(x.Text), x.Text, MarketingClaimClassification(x.Type), x.Confidence, segments, x.SourceIds)).ToArray();
        IReadOnlyList<MarketingStrategyRecommendationDto> Area(params string[] names) =>
            items.Where(x => names.Contains(x.Area, StringComparer.OrdinalIgnoreCase)).ToArray();
        var requiresReview = invalid || result.Status != AgentAiRunStatuses.Completed || items.Length == 0;
        var missing = result.MissingEvidence.Concat(invalid ? ["The generated proposal contained an invalid or inaccessible citation."] : [])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new MarketingStrategyProposalDto(result.RunId, agentId,
            requiresReview ? AgentAiRunStatuses.NeedsReview : result.Status, title, result.Summary, objective,
            fromUtc, toUtc, Area("market_customer"), Area("stp_positioning"),
            Area("product", "price", "place", "promotion"), Area("competitor"),
            Area("swot", "five_forces"), result.Sources, missing, requiresReview, "1.0.0",
            "marketing-role-v1:on_demand");
    }
    private static void ValidateGroundedRun(AgentReasoningResult run)
    {
        var sourceIds = run.SourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (run.Status != AgentAiRunStatuses.Completed || run.Claims.Count == 0 ||
            run.Claims.Any(x => x.SourceIds.Count == 0 || x.SourceIds.Any(id => !sourceIds.Contains(id)) ||
                x.Type is not ("confirmed_fact" or "inference" or "unknown")))
            throw new InvalidOperationException("The Maya proposal needs review and cannot be committed as a strategy draft.");
    }
    private static string MarketingClaimClassification(string value) => value switch
    {
        "confirmed_fact" => "observed",
        "inference" => "inferred",
        "unknown" => "assumption",
        _ => "assumption"
    };
    private static string ClassifyArea(string text)
    {
        var value = text.ToLowerInvariant();
        if (value.Contains("position") || value.Contains("segment") || value.Contains("target")) return "stp_positioning";
        if (value.Contains("competitor")) return "competitor";
        if (value.Contains("swot")) return "swot";
        if (value.Contains("five forces") || value.Contains("supplier") || value.Contains("substitute")) return "five_forces";
        if (value.Contains("price") || value.Contains("pricing")) return "price";
        if (value.Contains("channel") || value.Contains("distribution") || value.Contains("place")) return "place";
        if (value.Contains("promotion") || value.Contains("message") || value.Contains("content")) return "promotion";
        if (value.Contains("product") || value.Contains("offer")) return "product";
        return "market_customer";
    }
    private void AddLinks(Guid companyId, Guid strategyId, IReadOnlyList<Guid>? ids)
    {
        foreach (var id in ids?.Distinct() ?? [])
        {
            var segmentId = db.MarketingCustomerSegmentVersions.Local.SingleOrDefault(x => x.Id == id)?.MarketingCustomerSegmentId
                ?? db.MarketingCustomerSegmentVersions.AsNoTracking().Where(x => x.CompanyId == companyId && x.Id == id).Select(x => x.MarketingCustomerSegmentId).Single();
            db.MarketingStrategySegments.Add(new MarketingStrategySegment(Guid.NewGuid(), companyId, strategyId, segmentId, id));
        }
    }
    private async Task<IReadOnlyList<Guid>> SegmentLinks(Guid companyId, Guid strategyId, CancellationToken ct) => await db.MarketingStrategySegments.AsNoTracking().Where(x => x.CompanyId == companyId && x.MarketingStrategyId == strategyId).Select(x => x.MarketingCustomerSegmentVersionId).ToListAsync(ct);
    private static MarketingStrategyDto Map(MarketingStrategy x, IReadOnlyList<Guid> links) => new(x.Id, x.Title, x.Summary, x.BusinessContext, x.ValidFromUtc, x.ValidToUtc, x.SectionsJson, x.EvidenceReferencesJson, x.MissingEvidenceJson, x.Status, x.ApprovalRequestId, x.Version, x.UpdatedUtc, links);
    private static MarketingIntelligenceDto Map(MarketingIntelligenceRecord x) => new(x.Id, x.Kind, x.Subject, x.Summary, x.Classification, x.Confidence, x.SourceType, x.SourceReference, x.ObservedUtc, x.ReviewDueUtc, x.DimensionsJson, x.ReviewStatus, x.IsArchived, x.Version);

    private async Task ValidateIntelligenceSourceAsync(Guid companyId, string sourceType, string sourceReference,
        CancellationToken ct)
    {
        var type = sourceType.Trim().ToLowerInvariant();
        if (type is not ("knowledge_document" or "knowledge_chunk")) return;
        if (!Guid.TryParse(sourceReference, out var id))
            throw new InvalidOperationException("The knowledge source reference is invalid.");
        var available = type == "knowledge_document"
            ? await db.CompanyKnowledgeDocuments.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == id &&
                x.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed &&
                x.IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Indexed, ct)
            : await db.CompanyKnowledgeChunks.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == id && x.IsActive, ct);
        if (!available) throw new InvalidOperationException("The knowledge source is unavailable or outside this company.");
    }

    private Task WriteAuditAsync(Guid companyId, Guid userId, string action, MarketingIntelligenceRecord item,
        string outcome, string rationale, CancellationToken ct, string? before = null, string? after = null) =>
        audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, userId, action,
            "marketing_intelligence", item.Id.ToString("N"), outcome, rationale,
            DataSources: [item.SourceReference], Metadata: new Dictionary<string, string?>
            { ["classification"] = item.Classification, ["reviewStatus"] = item.ReviewStatus,
              ["version"] = item.Version.ToString() }, PayloadDiffJson: before is null ? null : JsonSerializer.Serialize(new { before, after })), ct);
    private Task WriteStrategyAuditAsync(Guid companyId, Guid userId, string action, MarketingStrategy item,
        string outcome, string rationale, CancellationToken ct, string? before = null, string? after = null) =>
        audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, userId, action,
            "marketing_strategy", item.Id.ToString("N"), outcome, rationale,
            Metadata: new Dictionary<string, string?> { ["status"] = item.Status, ["version"] = item.Version.ToString(),
                ["approvalRequestId"] = item.ApprovalRequestId?.ToString("N") },
            PayloadDiffJson: before is null ? null : JsonSerializer.Serialize(new { before, after })), ct);
    private static MarketingSegmentVersionDto Map(MarketingCustomerSegmentVersion x) => new(x.Id, x.MarketingCustomerSegmentId, x.VersionNumber, x.CriteriaJson, x.NeedsJson, x.BehaviorsJson, x.ChannelsJson, x.PricingJson, x.SizeLow, x.SizeHigh, x.SizeMethod, x.Confidence, x.EconomicsJson, x.ScorecardJson, x.AttractivenessScore, x.EvidenceJson, x.EvidenceObservedUtc, x.Status, x.TargetState, x.TargetRationale, x.ApprovalRequestId, x.ConcurrencyVersion);
}

internal sealed record MarketingSegmentProposalGroundingResult(
    IReadOnlyList<AgentAiClaim> Claims, bool CanCreateDraft, int RejectedClaimCount, bool UsedBootstrapFallback);

internal static class MarketingSegmentProposalGrounding
{
    private static readonly HashSet<string> AllowedClaimTypes = new(StringComparer.OrdinalIgnoreCase)
        { "confirmed_fact", "inference", "unknown" };

    public static MarketingSegmentProposalGroundingResult Evaluate(string status,
        IReadOnlyList<AgentAiClaim> claims, IReadOnlyList<string> allowedSourceIds, string? failureCode = null)
    {
        var allowed = allowedSourceIds.Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var valid = claims.Where(x => !string.IsNullOrWhiteSpace(x.Text) && AllowedClaimTypes.Contains(x.Type) &&
            x.SourceIds.Count > 0 && x.SourceIds.All(allowed.Contains)).ToArray();
        var rejected = claims.Count - valid.Length;
        var providerTimedOut = status == AgentAiRunStatuses.Failed &&
            string.Equals(failureCode, "provider_timeout", StringComparison.OrdinalIgnoreCase);
        var reviewableStatus = status is AgentAiRunStatuses.Completed or AgentAiRunStatuses.NeedsReview ||
            providerTimedOut;
        var usedFallback = false;
        if (reviewableStatus && valid.Length == 0 && allowed.Count > 0)
        {
            valid = [new AgentAiClaim(
                "This first segment is a hypothesis for human review; its size, needs, behaviours, channel presence, price sensitivity, and economics still require supporting evidence.",
                "unknown", .2m, [allowed.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).First()])];
            usedFallback = true;
        }
        return new MarketingSegmentProposalGroundingResult(valid, reviewableStatus && valid.Length > 0,
            rejected, usedFallback);
    }
}

public static class SegmentAttractivenessPolicy
{
    private static readonly string[] Required = ["sizeGrowth", "needIntensity", "productFit", "differentiation", "reachability", "priceValueFit", "economics", "evidenceQuality", "risk"];
    public static decimal Calculate(IReadOnlyDictionary<string, decimal> dimensions)
    {
        if (Required.Any(x => !dimensions.ContainsKey(x))) throw new ArgumentException("The complete segment scorecard is required.");
        if (dimensions.Values.Any(x => x is < 0 or > 100)) throw new ArgumentOutOfRangeException(nameof(dimensions));
        var positive = Required.Where(x => x != "risk").Average(x => dimensions[x]);
        return decimal.Round(positive * 0.9m + (100m - dimensions["risk"]) * 0.1m, 2, MidpointRounding.AwayFromZero);
    }
}
