using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingOperatingLoopService(
    VirtualCompanyDbContext db,
    IMarketingAgentAccessGuard accessGuard,
    IMarketingAgentAnalysisService analysis,
    IMarketingCompanyOrchestrationService companyOrchestration,
    IMarketingOperationsService marketingOperations,
    IMarketingStrategyService marketingStrategy,
    IMarketingWorkNeedAssessment workNeedAssessment,
    ISalesCampaignDraftService salesCampaignDrafts,
    ICampaignPlanningService campaignPlanning,
    ICompanyTaskCommandService tasks,
    ICompanyOperatingSnapshotService snapshots) : IMarketingOperatingLoopService
{
    public async Task<MarketingOperatingRunDto> RunAsync(Guid companyId, Guid marketingAgentId,
        RequestMarketingOperatingRun request, CancellationToken ct)
    {
        var existing = await db.MarketingOperatingRuns.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null) return Map(existing);

        var agent = await accessGuard.RequireActiveMarketingAgentAsync(companyId, marketingAgentId, ct);
        var config = await db.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, ct);
        var assignment = request.OperatingInitiativeId.HasValue
            ? await companyOrchestration.ResolveAssignmentAsync(companyId, marketingAgentId, request, ct)
            : null;
        var authorityDecision = MarketingAuthorityPolicy.Evaluate(new MarketingAuthorityContext(
            config?.AutonomyLevel ?? CompanyAutonomyLevel.Recommend,
            AgentAutonomyLevelValues.Parse(agent.AutonomyLevel), CompanyAutonomyLevel.OperateInternally,
            CompanyPaused: config?.IsPaused == true,
            GoalActive: assignment?.IsAccepted ?? true,
            WorkloadAvailable: assignment is null || assignment.CapacityUsed < assignment.CapacityLimit,
            BudgetAvailable: assignment is null || !assignment.BudgetLimit.HasValue || assignment.BudgetUsed < assignment.BudgetLimit));
        var authority = authorityDecision.EffectiveAuthority;
        var snapshotCycleId = assignment?.OperatingCycleId ?? await db.OperatingCycles.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status != OperatingCycleStatus.Cancelled &&
                x.Status != OperatingCycleStatus.Failed).OrderByDescending(x => x.CreatedUtc)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        OperatingSnapshotDto? companySnapshot = null;
        string? snapshotFailure = null;
        if (snapshotCycleId.HasValue)
        {
            try { companySnapshot = await snapshots.CaptureAsync(companyId, snapshotCycleId.Value, ct); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { snapshotFailure = exception.GetType().Name; }
        }
        else snapshotFailure = "operating_cycle_unavailable";
        var evidenceVersion = companySnapshot is null
            ? $"marketing-operating-snapshot.v1:{DateTime.UtcNow:yyyyMMddHHmm}"
            : $"{companySnapshot.SchemaVersion}:{companySnapshot.Id:N}:{companySnapshot.CreatedUtc:O}";
        var run = new MarketingOperatingRun(Guid.NewGuid(), companyId, marketingAgentId, request.TriggerType,
            request.TriggerReference, request.IdempotencyKey, request.CorrelationId,
            assignment?.CompanyGoalId ?? request.CompanyGoalId,
            assignment?.OperatingInitiativeId ?? request.OperatingInitiativeId,
            assignment?.WorkTaskId ?? request.WorkTaskId, authority.ToStorageValue(), config?.Version ?? 0,
            evidenceVersion, assignment?.BudgetLimit ?? config?.MaximumMonetaryBudgetPerCycle);
        if (assignment is not null) run.SetAssignmentContext(JsonSerializer.Serialize(assignment));
        db.MarketingOperatingRuns.Add(run);

        if (config?.IsPaused == true)
        {
            run.Block("company_paused", $"Company operation is paused: {config.PauseReason ?? "No reason supplied"}.");
            await db.SaveChangesAsync(ct); return Map(run);
        }
        if (companySnapshot is null)
        {
            run.Block("company_snapshot_unavailable", "The authoritative company snapshot could not be refreshed, so Maya did not plan or execute Marketing work.",
                JsonSerializer.Serialize(new[] { snapshotFailure ?? "snapshot_unavailable" }));
            await db.SaveChangesAsync(ct); return Map(run);
        }
        var recentCutoff = DateTime.UtcNow.AddMinutes(-(config?.MinimumCycleIntervalMinutes ?? 60));
        var duplicate = await db.MarketingOperatingRuns.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == companyId && x.AgentId == marketingAgentId && x.Id != run.Id &&
            x.TriggerType == request.TriggerType && x.CreatedUtc >= recentCutoff &&
            (x.Status == "running" || x.Status == "completed"), ct);
        if (duplicate)
        {
            run.Block("cooldown_active", "A materially equivalent Marketing run already occurred inside the configured minimum interval.");
            await db.SaveChangesAsync(ct); return Map(run);
        }

        run.Claim(TimeSpan.FromMinutes(5)); await db.SaveChangesAsync(ct);
        try
        {
            MarketingWorkNeedAssessmentDto? dailyAssessment = null;
            if (string.Equals(request.Cadence, "daily", StringComparison.OrdinalIgnoreCase))
            {
                dailyAssessment = await workNeedAssessment.AssessAsync(companyId, DateTime.UtcNow, ct);
                if (!dailyAssessment.HasActionableWork)
                {
                    run.Complete(JsonSerializer.Serialize(dailyAssessment.Needs), JsonSerializer.Serialize(dailyAssessment.CheckedEvidence), "[]",
                        "no_work_required: Maya checked the Marketing workspace and found no actionable planning or campaign gaps.");
                    db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.Agent, marketingAgentId,
                        "marketing.daily_work_assessed", "marketing_operating_run", run.Id.ToString("D"), AuditEventOutcomes.Succeeded,
                        "No actionable Marketing plan or campaign work was required.", dailyAssessment.CheckedEvidence,
                        new Dictionary<string, string?> { ["outcome"] = "no_work_required", ["needCount"] = dailyAssessment.Needs.Count.ToString() }));
                    await db.SaveChangesAsync(ct);
                    return Map(run);
                }
            }
            var objective = request.OperatingInitiativeId.HasValue
                ? $"Deliver the assigned company initiative '{assignment!.DesiredOutcome}' within its exact scope. Required completion evidence: {assignment.CompletionEvidence}. Company snapshot {companySnapshot!.SchemaVersion} has {companySnapshot.SourceCount} sources, {companySnapshot.DataGapCount} data gaps, and truncation={companySnapshot.IsTruncated}."
                : $"Run the governed Marketing {request.Cadence} review from company goals, product, customer, segment, commercial, and Marketing evidence. Use authoritative company snapshot {companySnapshot!.SchemaVersion} ({companySnapshot.Id:N}), containing {companySnapshot.SourceCount} sources and {companySnapshot.DataGapCount} declared data gaps; truncation={companySnapshot.IsTruncated}. Deterministic work needs: {JsonSerializer.Serialize(dailyAssessment?.Needs.Where(x => x.Actionable).Take(10) ?? [])}";
            var result = await analysis.AnalyzeAsync(companyId, marketingAgentId, null,
                new RoleAgentAnalysisRequest(MarketingAgentAnalysisTypes.OperatingCadence, null, 90, objective, DateTime.UtcNow, request.Cadence), ct);
            var workerId = $"marketing-operating-loop:{Environment.ProcessId}";
            var executedAnalyses = new List<RoleAgentAnalysisResult>();
            var canOrganize = authority >= CompanyAutonomyLevel.Organize && !result.RequiresReview;
            var canOperateInternally = authority >= CompanyAutonomyLevel.OperateInternally && !result.RequiresReview;
            var taskLimit = Math.Clamp(config?.MaximumTasksPerCycle ?? 12, 0, 50);
            var remainingModelCalls = Math.Clamp((config?.MaximumModelCallsPerCycle ?? 3) - 1, 0, 10);
            var selectedNeeds = dailyAssessment?.Needs.Where(x => x.Actionable)
                .GroupBy(x => x.RecommendedTool == MarketingToolIds.PreparePlan
                    ? "plan-draft"
                    : x.RecommendedTool == MarketingToolIds.PrepareCampaignPortfolio && x.AffectedIds.Count > 0
                        ? $"campaign-portfolio:{x.AffectedIds[0]:N}"
                        : x.Fingerprint)
                .Select(x => x.First()).Take(taskLimit).ToArray() ?? [];
            var selectedActions = dailyAssessment is null
                ? result.NextActions
                : selectedNeeds.Select(need =>
                {
                    var tool = authority >= CompanyAutonomyLevel.OperateInternally
                        ? need.ReasonCode switch
                        {
                            "objective_without_plan" or "plan_missing_for_horizon" => MarketingToolIds.CreatePlanDraft,
                            "plan_has_no_campaigns" or "objective_without_campaign_coverage" or "target_segment_without_campaign" => MarketingToolIds.CreateCampaignDrafts,
                            _ => need.RecommendedTool
                        }
                        : need.ReasonCode is "objective_without_plan" or "plan_missing_for_horizon"
                            ? MarketingToolIds.PreparePlan
                            : need.ReasonCode is "plan_has_no_campaigns" or "objective_without_campaign_coverage" or "target_segment_without_campaign"
                                ? MarketingToolIds.PrepareCampaignPortfolio
                                : need.RecommendedTool;
                    return new AgentAiNextAction(need.Label, authority >= CompanyAutonomyLevel.OperateInternally ? "execute" : "recommend", tool, need.RequiresApproval);
                }).ToArray();
            var proposedActions = selectedActions.Take(taskLimit).Select((action, index) => new
            {
                Action = action,
                Index = index,
                Need = dailyAssessment is null ? null : selectedNeeds[index],
                IdempotencyKey = dailyAssessment is null
                    ? $"{run.Id:N}:action:{index + 1}"
                    : $"marketing-need:{selectedNeeds[index].Fingerprint}:{action.ToolName}"
            }).ToArray();
            var proposedKeys = proposedActions.Select(x => x.IdempotencyKey).ToArray();
            var completedNeedKeys = dailyAssessment is null ? [] : await db.MarketingOperatingActions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && proposedKeys.Contains(x.IdempotencyKey) && (x.Status == "completed" || x.Status == "blocked"))
                .Select(x => x.IdempotencyKey).ToArrayAsync(ct);
            var plannedActions = proposedActions.Where(x => !completedNeedKeys.Contains(x.IdempotencyKey)).Select(candidate =>
            {
                var action = candidate.Action;
                var index = candidate.Index;
                var decision = MarketingAuthorityPolicy.Evaluate(new MarketingAuthorityContext(
                    config?.AutonomyLevel ?? CompanyAutonomyLevel.Recommend,
                    AgentAutonomyLevelValues.Parse(agent.AutonomyLevel), CompanyAutonomyLevel.OperateInternally,
                    CompanyPaused: config?.IsPaused == true, GoalActive: assignment?.IsAccepted ?? true,
                    ApprovalSatisfied: !action.RequiresApproval,
                    WorkloadAvailable: index < taskLimit,
                    BudgetAvailable: !run.BudgetLimit.HasValue || run.BudgetUsed < run.BudgetLimit.Value));
                return new MarketingOperatingAction(Guid.NewGuid(), companyId, run.Id, index + 1,
                    action.ActionType, action.Title, InternalAnalysisType(action.ToolName), action.ToolName,
                    JsonSerializer.Serialize(new { assignment?.OperatingInitiativeId, assignment?.WorkTaskId, needFingerprint = candidate.Need?.Fingerprint, affectedIds = candidate.Need?.AffectedIds }),
                    run.EvidenceVersion, assignment?.DesiredOutcome ?? objective,
                    JsonSerializer.Serialize(assignment?.Dependencies ?? []),
                    assignment?.CompletionEvidence ?? "A source-grounded draft artifact and its deterministic preflight result.",
                    decision.ReasonCode, action.RequiresApproval, candidate.IdempotencyKey,
                    InternalAnalysisType(action.ToolName) is null ? 0m : 0.01m, maximumAttempts: 3);
            }).ToList();
            db.MarketingOperatingActions.AddRange(plannedActions);
            await db.SaveChangesAsync(ct);

            foreach (var plannedAction in plannedActions)
            {
                Guid? taskId = null;
                RoleAgentAnalysisResult? executed = null;
                plannedAction.Claim(workerId, TimeSpan.FromMinutes(5));
                run.RenewLease(TimeSpan.FromMinutes(5));
                await db.SaveChangesAsync(ct);
                try
                {
                    var currentDecision = MarketingAuthorityPolicy.Evaluate(new MarketingAuthorityContext(
                        config?.AutonomyLevel ?? CompanyAutonomyLevel.Recommend,
                        AgentAutonomyLevelValues.Parse(agent.AutonomyLevel), CompanyAutonomyLevel.OperateInternally,
                        CompanyPaused: config?.IsPaused == true, GoalActive: assignment?.IsAccepted ?? true,
                        ApprovalSatisfied: !plannedAction.RequiresApproval,
                        BudgetAvailable: !run.BudgetLimit.HasValue || run.BudgetUsed + plannedAction.EstimatedCost <= run.BudgetLimit.Value));
                    if (!currentDecision.Allowed || plannedAction.RequiresApproval)
                    {
                        plannedAction.Block(workerId, currentDecision.ReasonCode,
                            currentDecision.RequiresApproval ? "Obtain exact-version approval, then retry this action." : currentDecision.Explanation,
                            retryable: false);
                        await db.SaveChangesAsync(ct); continue;
                    }
                    if (!canOperateInternally)
                    {
                        plannedAction.Complete(workerId, "marketing_recommendation", null,
                            JsonSerializer.Serialize(new { disposition = "recommendation_only", result.Summary }), 0m);
                        await db.SaveChangesAsync(ct); continue;
                    }
                    var internalAnalysisType = InternalAnalysisType(plannedAction.Tool);
                    if (remainingModelCalls > 0 && internalAnalysisType is not null)
                    {
                        executed = await analysis.AnalyzeAsync(companyId, marketingAgentId, null,
                            new RoleAgentAnalysisRequest(internalAnalysisType, null, 90,
                                $"Execute this permitted internal Marketing analysis within the assigned company scope: {plannedAction.Title}",
                                DateTime.UtcNow, "operating_action"), ct);
                        executedAnalyses.Add(executed); remainingModelCalls--;
                    }
                    var artifact = await ExecuteInternalArtifactAsync(companyId, marketingAgentId, run,
                        plannedAction, result, executed, assignment, ct);
                    if (artifact is null && canOrganize && IsInternalRecommendation(plannedAction.Tool, plannedAction.RequiresApproval))
                    {
                        var payload = new Dictionary<string, JsonNode?>
                        {
                            ["marketingOperatingRunId"] = JsonValue.Create(run.Id),
                            ["companyGoalId"] = JsonValue.Create(run.CompanyGoalId),
                            ["operatingInitiativeId"] = JsonValue.Create(run.OperatingInitiativeId),
                            ["recommendedTool"] = JsonValue.Create(plannedAction.Tool),
                            ["completionEvidence"] = JsonValue.Create(plannedAction.ExpectedCompletionEvidence)
                        };
                        var command = run.WorkTaskId.HasValue
                            ? await tasks.CreateSubtaskAsync(companyId, run.WorkTaskId.Value,
                                new CreateSubtaskCommand("marketing_internal_work", plannedAction.Title,
                                    "Prepare and validate the recommended Marketing artifact. External execution remains separately governed.",
                                    "normal", DateTime.UtcNow.AddDays(7), marketingAgentId, payload,
                                    RationaleSummary: result.Summary, ConfidenceScore: result.Confidence,
                                    CorrelationId: request.CorrelationId), ct)
                            : await tasks.CreateTaskAsync(companyId,
                                new CreateTaskCommand("marketing_internal_work", plannedAction.Title,
                                    "Prepare and validate the recommended Marketing artifact. External execution remains separately governed.",
                                    "normal", DateTime.UtcNow.AddDays(7), marketingAgentId, payload,
                                    RationaleSummary: result.Summary, ConfidenceScore: result.Confidence,
                                    CorrelationId: request.CorrelationId), ct);
                        taskId = command.Id;
                        artifact = ("company_task", taskId, JsonSerializer.Serialize(new { taskId, disposition = "organized" }));
                    }
                    artifact ??= (executed is null ? "marketing_recommendation" : "marketing_analysis_run",
                        executed?.RunId, JsonSerializer.Serialize(new { executed?.Summary, disposition = executed is null ? "recommended" : "executed_internal_analysis" }));
                    run.AddBudgetUsage(plannedAction.EstimatedCost);
                    plannedAction.Complete(workerId, artifact.Value.ArtifactType, artifact.Value.ArtifactId,
                        artifact.Value.EvidenceJson, plannedAction.EstimatedCost);
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception actionException) when (actionException is InvalidOperationException or ArgumentException or KeyNotFoundException)
                {
                    plannedAction.Block(workerId, "policy_or_prerequisite_blocked",
                        $"The guarded command was blocked by a prerequisite or policy check ({actionException.GetType().Name}). Refresh the evidence before retrying.",
                        retryable: false);
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception actionException) when (actionException is not OperationCanceledException)
                {
                    plannedAction.Block(workerId, "internal_action_failed",
                        $"The guarded command failed with {actionException.GetType().Name}. Review evidence and retry safely.",
                        retryable: true, TimeSpan.FromMinutes(5));
                    await db.SaveChangesAsync(ct);
                }
            }
            var work = plannedActions.Select(x => new { x.Id, x.Sequence, x.ActionType, x.Title, x.Tool,
                x.RequiresApproval, x.Status, x.ArtifactType, x.ArtifactId, x.RecoveryCode }).ToArray();
            var evidence = result.Sources.Concat(executedAnalyses.SelectMany(x => x.Sources))
                .DistinctBy(x => x.Id).Select(x => new { sourceId = x.Id, sourceType = x.Type, x.Title, observedUtc = x.UpdatedUtc }).ToArray();
            var evidencePayload = dailyAssessment is null
                ? JsonSerializer.Serialize(evidence)
                : JsonSerializer.Serialize(new { sources = evidence, checkedEvidence = dailyAssessment.CheckedEvidence, workNeeds = dailyAssessment.Needs });
            run.Complete(JsonSerializer.Serialize(work), evidencePayload,
                JsonSerializer.Serialize(result.MissingEvidence), result.Summary);
            if (dailyAssessment is not null)
                db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.Agent, marketingAgentId,
                    "marketing.daily_work_assessed", "marketing_operating_run", run.Id.ToString("D"), AuditEventOutcomes.Succeeded,
                    "Maya completed the governed daily Marketing work assessment.", dailyAssessment.CheckedEvidence,
                    new Dictionary<string, string?> { ["outcome"] = "work_assessed", ["actionableNeedCount"] = dailyAssessment.Needs.Count(x => x.Actionable).ToString(), ["plannedActionCount"] = plannedActions.Count.ToString() }));
            await db.SaveChangesAsync(ct); return Map(run);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Block("analysis_failed", "Maya could not complete this operating review. The run is safe to retry after the underlying evidence or AI service is available.", JsonSerializer.Serialize(new[] { ex.GetType().Name }));
            await db.SaveChangesAsync(ct); return Map(run);
        }
    }

    public async Task<IReadOnlyList<MarketingOperatingRunDto>> ListAsync(Guid companyId, int take, CancellationToken ct) =>
        await db.MarketingOperatingRuns.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedUtc).Take(Math.Clamp(take, 1, 100)).Select(x => new MarketingOperatingRunDto(
                x.Id, x.CompanyId, x.AgentId, x.CompanyGoalId, x.OperatingInitiativeId, x.WorkTaskId, x.TriggerType,
                x.TriggerReference, x.EffectiveAuthority, x.Status, x.SelectedWorkJson, x.EvidenceJson,
                x.MissingEvidenceJson, x.OutcomeSummary, x.RecoveryCode, x.BudgetLimit, x.BudgetUsed,
                x.AttemptCount, x.CreatedUtc, x.CompletedUtc, x.AssignmentContextJson, 0, 0)).ToListAsync(ct);

    public async Task<IReadOnlyList<MarketingOperatingActionDto>> ListActionsAsync(Guid companyId, Guid runId, CancellationToken ct)
    {
        var items = await db.MarketingOperatingActions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MarketingOperatingRunId == runId)
            .OrderBy(x => x.Sequence).ToListAsync(ct);
        return items.Select(MapAction).ToArray();
    }

    public async Task<MarketingOperatingActionDto?> RetryActionAsync(Guid companyId, Guid runId, Guid actionId,
        RetryMarketingOperatingActionRequest request, CancellationToken ct)
    {
        var item = await db.MarketingOperatingActions.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.MarketingOperatingRunId == runId && x.Id == actionId, ct);
        if (item is null) return null;
        item.Retry(request.RecoveryRationale); await db.SaveChangesAsync(ct); return MapAction(item);
    }

    public async Task<MarketingOperatingActionDto?> CancelActionAsync(Guid companyId, Guid runId, Guid actionId,
        CancelMarketingOperatingActionRequest request, CancellationToken ct)
    {
        var item = await db.MarketingOperatingActions.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.MarketingOperatingRunId == runId && x.Id == actionId, ct);
        if (item is null) return null;
        item.Cancel(request.Rationale); await db.SaveChangesAsync(ct); return MapAction(item);
    }

    private async Task<(string ArtifactType, Guid? ArtifactId, string EvidenceJson)?> ExecuteInternalArtifactAsync(
        Guid companyId, Guid agentId, MarketingOperatingRun run, MarketingOperatingAction action,
        RoleAgentAnalysisResult operatingResult, RoleAgentAnalysisResult? actionResult,
        MarketingAssignmentContextDto? assignment, CancellationToken ct)
    {
        var starts = DateTime.UtcNow.Date;
        switch (action.Tool)
        {
            case MarketingToolIds.PreparePlan:
            {
                var request = new CreateMarketingPlanRequest(action.Title, actionResult?.Summary ?? operatingResult.Summary,
                    starts, starts.AddDays(90), run.BudgetLimit, "SEK");
                var proposal = await marketingOperations.PreparePlanProposalAsync(companyId, request, ct);
                return ("marketing_plan_proposal", null, JsonSerializer.Serialize(new { proposal.ProposalKey, disposition = "recommendation_only" }));
            }
            case MarketingToolIds.CreatePlanDraft:
            {
                var strategy = await db.MarketingStrategies.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId &&
                    (x.Status == MarketingStrategicStatuses.Active || x.Status == MarketingStrategicStatuses.Approved) && x.ValidFromUtc <= starts && x.ValidToUtc >= starts.AddDays(90))
                    .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct) ?? throw new InvalidOperationException("strategy_missing: No approved strategy covers the plan horizon.");
                var segmentIds = await db.MarketingStrategySegments.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.MarketingStrategyId == strategy.Id)
                    .Select(x => x.MarketingCustomerSegmentVersionId).ToArrayAsync(ct);
                var segments = await db.MarketingCustomerSegmentVersions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && segmentIds.Contains(x.Id) &&
                    (x.Status == MarketingStrategicStatuses.Active || x.Status == MarketingStrategicStatuses.Approved)).ToArrayAsync(ct);
                var objectives = await db.MarketingObjectives.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == MarketingStatuses.Active && x.PeriodStartUtc < starts.AddDays(90) && x.PeriodEndUtc > starts).ToArrayAsync(ct);
                if (segments.Length == 0 || objectives.Length == 0) throw new InvalidOperationException("evidence_missing: Approved audiences and active objectives are required.");
                var evidence = new[] { $"marketing-strategy:{strategy.Id:N}:v{strategy.Version}" }.Concat(segments.Select(x => $"marketing-segment-version:{x.Id:N}:v{x.VersionNumber}")).ToArray();
                var request = new CreateGroundedMarketingPlanRequest(action.Title, actionResult?.Summary ?? operatingResult.Summary,
                    strategy.Id, strategy.Version, starts, starts.AddDays(90), run.BudgetLimit, "SEK", objectives.Select(x => x.Id).ToArray(),
                    segments.Select((x, i) => new MarketingPlanSegmentSelection(x.Id, i == 0 ? MarketingPlanSegmentRoles.Primary : MarketingPlanSegmentRoles.Secondary,
                        i + 1, "Selected from the approved strategy.", "Contribute to the active Marketing objectives.")).ToArray(),
                    operatingResult.Summary, evidence, [], [], [], action.IdempotencyKey, agentId);
                var artifact = await marketingOperations.CreateGroundedPlanAsync(companyId, agentId, request, ct);
                return ("marketing_plan", artifact.Summary.Id, JsonSerializer.Serialize(new { artifact.Summary.Version, evidence }));
            }
            case MarketingToolIds.CreateCampaignDrafts:
            {
                var planId = await db.MarketingPlans.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId &&
                    (x.Status == MarketingStatuses.Draft || x.Status == MarketingStatuses.Active) &&
                    !db.MarketingPlanCampaigns.IgnoreQueryFilters().Any(c => c.CompanyId == companyId && c.MarketingPlanId == x.Id))
                    .OrderBy(x => x.EndsUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct)
                    ?? throw new InvalidOperationException("plan_missing: No plan needs a campaign portfolio.");
                var plan = await marketingOperations.GetPlanPortfolioAsync(companyId, planId, ct) ?? throw new InvalidOperationException("plan_missing: The plan is unavailable.");
                var objective = plan.Objectives.FirstOrDefault() ?? throw new InvalidOperationException("objective_missing: The plan has no objective.");
                if (plan.Segments.Count == 0) throw new InvalidOperationException("segment_missing: The plan has no approved target segment.");
                var launch = starts.AddDays(14); if (launch >= plan.Summary.EndsUtc) launch = starts.AddDays(1);
                var item = new MarketingCampaignPortfolioItemRequest($"{plan.Summary.Name} · {objective.Name}", actionResult?.Summary ?? action.Title,
                    objective.Id, $"Contribute to {objective.Name}.", plan.Segments.Select(x => x.SegmentVersionId).ToArray(), plan.Summary.RemainingBudget,
                    plan.Summary.BudgetCurrency, plan.Campaigns.Count + 1, CampaignTypes.LeadGeneration, "marketing_segment", 1, "result",
                    plan.Summary.EndsUtc, starts, launch, plan.Summary.EndsUtc.AddDays(-1), plan.Summary.EndsUtc, "UTC", "en", ["internal"],
                    "Grounded in the approved plan evidence", ["Prepare campaign assets", "Review campaign readiness"], ["Campaign message brief"],
                    string.Join("; ", plan.Segments.Select(x => x.SegmentName)), "Measure the linked objective before and after launch.", plan.EvidenceReferences, plan.MissingEvidence);
                var request = new PrepareMarketingCampaignPortfolioRequest(planId, plan.Summary.Version, [item], action.IdempotencyKey, agentId);
                var result = await marketingOperations.CommitCampaignPortfolioAsync(companyId, agentId, new CommitMarketingCampaignPortfolioRequest(request), ct);
                return ("marketing_campaign_portfolio", planId, JsonSerializer.Serialize(new { result.Outcome, campaignIds = result.Campaigns.Select(x => x.CampaignId) }));
            }
            case MarketingToolIds.PopulateCampaignDraft:
            {
                var campaignId = AffectedId(action) ?? throw new InvalidOperationException("campaign_missing: The work need has no campaign reference.");
                var summary = actionResult?.Summary ?? operatingResult.Summary;
                var steps = Enumerable.Range(1, 4).Select(index => new SalesCampaignDraftStepCommand(index, index == 1 ? 0 : (index - 1) * 3,
                    $"Draft touch {index}", $"Internal draft for review — {summary}")).ToArray();
                var result = await salesCampaignDrafts.PopulateDraftAsync(new PopulateSalesCampaignDraftCommand(companyId,
                    campaignId, agentId, agentId, steps, action.IdempotencyKey), ct);
                return ("sales_campaign", result.CampaignId, JsonSerializer.Serialize(new { result.SequenceId, stepCount = steps.Length, disposition = "draft_only" }));
            }
            case MarketingToolIds.SubmitCampaignForReadiness:
            {
                var campaignId = AffectedId(action) ?? throw new InvalidOperationException("campaign_missing: The work need has no campaign reference.");
                var readiness = await campaignPlanning.GetReadinessAsync(companyId, campaignId, ct)
                    ?? throw new InvalidOperationException("campaign_missing: The campaign is unavailable.");
                var result = await campaignPlanning.RequestReadinessAsync(companyId, agentId, campaignId, readiness.Version, ct)
                    ?? throw new InvalidOperationException("campaign_missing: The campaign is unavailable.");
                return ("sales_campaign_readiness", result.Id, JsonSerializer.Serialize(new { result.LifecycleStatus, result.Version, result.MissingRequirements }));
            }
            case MarketingToolIds.PrepareContentBrief:
            {
                var artifact = await marketingOperations.CreateContentBriefAsync(companyId, agentId,
                    new CreateMarketingContentBriefRequest(null, null, action.Title,
                        actionResult?.Summary ?? operatingResult.Summary, "Approved company target audience",
                        "internal_draft", "en", "company_brand", "Review and approve the next step",
                        DateTime.UtcNow.AddDays(7), MeasurableObjective: assignment?.DesiredOutcome ?? "Support the assigned company outcome",
                        CustomerInsight: operatingResult.Summary, KeyMessage: actionResult?.Summary ?? action.Title,
                        EvidenceRequirementsJson: JsonSerializer.Serialize(new { sourceVersion = action.SourceVersion }),
                        ApprovalPolicyJson: JsonSerializer.Serialize(new { externalPublicationRequiresApproval = true })), ct);
                return ("marketing_content_brief", artifact.Id, JsonSerializer.Serialize(new { artifact.Version, artifact.Status }));
            }
            case MarketingToolIds.PrepareExperiment:
            {
                var artifact = await marketingOperations.CreateExperimentAsync(companyId,
                    new CreateMarketingExperimentRequest(null, action.Title,
                        actionResult?.Summary ?? operatingResult.Summary, "conversion_rate", "complaint_rate",
                        100, starts, starts.AddDays(30)), ct);
                return ("marketing_experiment", artifact.Id, JsonSerializer.Serialize(new { artifact.Status, artifact.MinimumSampleSize }));
            }
            case MarketingToolIds.PrepareSegmentation:
            case MarketingToolIds.AnalyzeAudience:
            {
                var proposal = await marketingStrategy.PrepareSegmentProposalAsync(companyId, agentId,
                    new PrepareMarketingSegmentProposalRequest(agentId, action.Title,
                        assignment?.DesiredOutcome ?? actionResult?.Summary ?? operatingResult.Summary), ct);
                return ("marketing_segment_proposal", proposal.RunId,
                    JsonSerializer.Serialize(new { proposal.RequiresReview, proposal.Confidence, proposal.MissingEvidence }));
            }
            case MarketingToolIds.PrepareOperatingReview when assignment is not null:
            {
                var evidence = await companyOrchestration.ReportProgressAsync(companyId,
                    new ReportMarketingWorkCommand(run.Id, assignment.OperatingInitiativeId, assignment.WorkTaskId,
                        action.IdempotencyKey, action.SourceVersion, "[]",
                        JsonSerializer.Serialize(new { assignment.DesiredOutcome }), "{}", actionResult?.Confidence,
                        JsonSerializer.Serialize(actionResult?.MissingEvidence ?? operatingResult.MissingEvidence), "[]",
                        JsonSerializer.Serialize(assignment.Dependencies), "{}", actionResult?.Summary ?? operatingResult.Summary,
                        "continue", assignment.CorrelationId), ct);
                return ("marketing_work_progress", evidence.Id, JsonSerializer.Serialize(new { evidence.Version }));
            }
            default:
                return null;
        }
    }

    private static bool IsInternalRecommendation(string? tool, bool requiresApproval) =>
        !requiresApproval && (tool is null || MarketingToolIds.ReadTools.Contains(tool) ||
            MarketingToolIds.RecommendTools.Contains(tool));
    private static Guid? AffectedId(MarketingOperatingAction action)
    {
        try
        {
            using var document = JsonDocument.Parse(action.TargetJson);
            if (!document.RootElement.TryGetProperty("affectedIds", out var ids) || ids.ValueKind != JsonValueKind.Array) return null;
            return ids.EnumerateArray().Select(x => x.GetGuid()).FirstOrDefault() is var id && id != Guid.Empty ? id : null;
        }
        catch (JsonException) { return null; }
    }
    private static string? InternalAnalysisType(string? toolName) => toolName switch
    {
        MarketingToolIds.AnalyzeAudience or MarketingToolIds.PrepareSegmentation or MarketingToolIds.RecommendTargetSegments
            or MarketingToolIds.AssessSegmentStrategyImpact => MarketingAgentAnalysisTypes.AudienceIntelligence,
        MarketingToolIds.PrepareContentBrief => MarketingAgentAnalysisTypes.ContentAdvice,
        MarketingToolIds.RecommendCampaignChange => MarketingAgentAnalysisTypes.CampaignCoordination,
        MarketingToolIds.PreparePerformanceReview => MarketingAgentAnalysisTypes.PerformanceAnalysis,
        MarketingToolIds.PrepareExperiment => MarketingAgentAnalysisTypes.ExperimentAdvice,
        MarketingToolIds.PreparePlan => MarketingAgentAnalysisTypes.Planning,
        MarketingToolIds.PrepareOperatingReview => MarketingAgentAnalysisTypes.OperatingCadence,
        _ => null
    };
    private static MarketingOperatingRunDto Map(MarketingOperatingRun x) => new(x.Id, x.CompanyId, x.AgentId,
        x.CompanyGoalId, x.OperatingInitiativeId, x.WorkTaskId, x.TriggerType, x.TriggerReference,
        x.EffectiveAuthority, x.Status, x.SelectedWorkJson, x.EvidenceJson, x.MissingEvidenceJson,
        x.OutcomeSummary, x.RecoveryCode, x.BudgetLimit, x.BudgetUsed, x.AttemptCount, x.CreatedUtc, x.CompletedUtc,
        x.AssignmentContextJson);
    private static MarketingOperatingActionDto MapAction(MarketingOperatingAction x) => new(x.Id,
        x.MarketingOperatingRunId, x.Sequence, x.Version, x.ActionType, x.Title, x.Capability, x.Tool,
        x.TargetJson, x.SourceVersion, x.GoalRelevance, x.DependenciesJson, x.ExpectedCompletionEvidence,
        x.AuthorityDecision, x.RequiresApproval, x.IdempotencyKey, x.EstimatedCost, x.ActualCost, x.Status,
        x.AttemptCount, x.MaximumAttempts, x.LeaseExpiresUtc, x.ArtifactType, x.ArtifactId,
        x.ActualEvidenceJson, x.RecoveryCode, x.RecoveryGuidance, x.NextAttemptUtc, x.CreatedUtc, x.CompletedUtc);
}
