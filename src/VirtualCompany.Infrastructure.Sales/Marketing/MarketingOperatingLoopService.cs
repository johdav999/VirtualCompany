using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Marketing;
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
            var objective = request.OperatingInitiativeId.HasValue
                ? $"Deliver the assigned company initiative '{assignment!.DesiredOutcome}' within its exact scope. Required completion evidence: {assignment.CompletionEvidence}. Company snapshot {companySnapshot!.SchemaVersion} has {companySnapshot.SourceCount} sources, {companySnapshot.DataGapCount} data gaps, and truncation={companySnapshot.IsTruncated}."
                : $"Run the governed Marketing {request.Cadence} review from company goals, product, customer, segment, commercial, and Marketing evidence. Use authoritative company snapshot {companySnapshot!.SchemaVersion} ({companySnapshot.Id:N}), containing {companySnapshot.SourceCount} sources and {companySnapshot.DataGapCount} declared data gaps; truncation={companySnapshot.IsTruncated}.";
            var result = await analysis.AnalyzeAsync(companyId, marketingAgentId, null,
                new RoleAgentAnalysisRequest(MarketingAgentAnalysisTypes.OperatingCadence, null, 90, objective, DateTime.UtcNow, request.Cadence), ct);
            var workerId = $"marketing-operating-loop:{Environment.ProcessId}";
            var executedAnalyses = new List<RoleAgentAnalysisResult>();
            var canOrganize = authority >= CompanyAutonomyLevel.Organize && !result.RequiresReview;
            var canOperateInternally = authority >= CompanyAutonomyLevel.OperateInternally && !result.RequiresReview;
            var taskLimit = Math.Clamp(config?.MaximumTasksPerCycle ?? 12, 0, 50);
            var remainingModelCalls = Math.Clamp((config?.MaximumModelCallsPerCycle ?? 3) - 1, 0, 10);
            var plannedActions = result.NextActions.Take(taskLimit).Select((action, index) =>
            {
                var decision = MarketingAuthorityPolicy.Evaluate(new MarketingAuthorityContext(
                    config?.AutonomyLevel ?? CompanyAutonomyLevel.Recommend,
                    AgentAutonomyLevelValues.Parse(agent.AutonomyLevel), CompanyAutonomyLevel.OperateInternally,
                    CompanyPaused: config?.IsPaused == true, GoalActive: assignment?.IsAccepted ?? true,
                    ApprovalSatisfied: !action.RequiresApproval,
                    WorkloadAvailable: index < taskLimit,
                    BudgetAvailable: !run.BudgetLimit.HasValue || run.BudgetUsed < run.BudgetLimit.Value));
                return new MarketingOperatingAction(Guid.NewGuid(), companyId, run.Id, index + 1,
                    action.ActionType, action.Title, InternalAnalysisType(action.ToolName), action.ToolName,
                    JsonSerializer.Serialize(new { assignment?.OperatingInitiativeId, assignment?.WorkTaskId }),
                    run.EvidenceVersion, assignment?.DesiredOutcome ?? objective,
                    JsonSerializer.Serialize(assignment?.Dependencies ?? []),
                    assignment?.CompletionEvidence ?? "A source-grounded draft artifact and its deterministic preflight result.",
                    decision.ReasonCode, action.RequiresApproval, $"{run.Id:N}:action:{index + 1}",
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
            run.Complete(JsonSerializer.Serialize(work), JsonSerializer.Serialize(evidence),
                JsonSerializer.Serialize(result.MissingEvidence), result.Summary);
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
                var artifact = await marketingOperations.CommitPlanAsync(companyId, agentId,
                    new CommitMarketingPlanRequest(action.IdempotencyKey, request), ct);
                return ("marketing_plan", artifact.Id, JsonSerializer.Serialize(new { proposal.ProposalKey, artifact.Version }));
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
