using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingCompanyOrchestrationService(VirtualCompanyDbContext db)
    : IMarketingCompanyOrchestrationService
{
    public async Task<MarketingAssignmentContextDto> ResolveAssignmentAsync(Guid companyId, Guid marketingAgentId,
        RequestMarketingOperatingRun request, CancellationToken ct)
    {
        if (!request.OperatingInitiativeId.HasValue)
            throw new MarketingAssignmentException("initiative_required", "A company initiative is required for an assigned Marketing run.");

        var initiative = await db.OperatingInitiatives.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Id == request.OperatingInitiativeId.Value)
            .Select(x => new
            {
                x.Id, x.CompanyId, x.PlanId, x.GoalId, x.OwnerAgentId, x.Status, x.TaskId, x.Version,
                x.DesiredOutcome, x.Priority, x.CompletionEvidence, x.TargetUtc, x.Budget,
                PlanCompanyId = x.Plan.CompanyId, PlanVersion = x.Plan.Version, PlanStatus = x.Plan.Status,
                CycleId = x.Plan.CycleId, CycleCorrelationId = x.Plan.Cycle.CorrelationId,
                SnapshotId = x.Plan.Cycle.SnapshotId,
                GoalCompanyId = x.Goal.CompanyId, GoalVersion = x.Goal.Version, GoalStatus = x.Goal.Status,
                GoalStartUtc = x.Goal.StartUtc, GoalTargetUtc = x.Goal.TargetUtc,
                x.Goal.OwnerUserId, GoalConstraints = x.Goal.Constraints
            }).SingleOrDefaultAsync(ct);

        if (initiative is null)
            throw new MarketingAssignmentException("initiative_unavailable", "The assigned company initiative does not exist.");
        if (initiative.CompanyId != companyId || initiative.PlanCompanyId != companyId || initiative.GoalCompanyId != companyId)
            throw new MarketingAssignmentException(MarketingAssignmentReasonCodes.CrossCompanyLink,
                "The assignment contains a company link that does not match the current company.");

        var config = await db.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, ct);
        Reject(config?.IsPaused == true, MarketingAssignmentReasonCodes.CompanyPaused,
            $"Company operation is paused: {config?.PauseReason ?? "No reason supplied"}.");
        Reject(initiative.GoalStatus != CompanyGoalStatus.Active, MarketingAssignmentReasonCodes.GoalInactive,
            "The assigned company goal is not active.");
        Reject(initiative.PlanStatus is OperatingPlanStatus.Rejected or OperatingPlanStatus.Superseded or OperatingPlanStatus.Cancelled or OperatingPlanStatus.Draft or OperatingPlanStatus.AwaitingReview,
            MarketingAssignmentReasonCodes.PlanUnavailable, "The operating plan is not current and approved for work.");
        Reject(request.ExpectedGoalVersion.HasValue && request.ExpectedGoalVersion != initiative.GoalVersion,
            MarketingAssignmentReasonCodes.StaleGoalVersion, "The company goal changed after this assignment was issued.");
        Reject(request.ExpectedPlanVersion.HasValue && request.ExpectedPlanVersion != initiative.PlanVersion,
            MarketingAssignmentReasonCodes.StalePlanVersion, "The operating plan changed after this assignment was issued.");
        Reject(request.ExpectedInitiativeVersion.HasValue && request.ExpectedInitiativeVersion != initiative.Version,
            MarketingAssignmentReasonCodes.StaleInitiativeVersion, "The initiative changed after this assignment was issued.");
        Reject(initiative.OwnerAgentId != marketingAgentId, MarketingAssignmentReasonCodes.WrongOwner,
            "This initiative is not assigned to Maya.");
        Reject(initiative.Status is not (OperatingInitiativeStatus.Approved or OperatingInitiativeStatus.Active),
            "initiative_unavailable", "The initiative is not approved or active.");
        Reject(string.IsNullOrWhiteSpace(initiative.CompletionEvidence), MarketingAssignmentReasonCodes.CompletionEvidenceMissing,
            "The initiative does not define the evidence required for completion.");
        Reject(request.CompanyGoalId.HasValue && request.CompanyGoalId != initiative.GoalId,
            MarketingAssignmentReasonCodes.CrossCompanyLink, "The requested goal does not match the assigned initiative.");
        Reject(request.WorkTaskId.HasValue && request.WorkTaskId != initiative.TaskId,
            MarketingAssignmentReasonCodes.TaskUnavailable, "The requested task does not match the assigned initiative.");

        var dependencies = await (from link in db.OperatingPlanDependencies.IgnoreQueryFilters().AsNoTracking()
            join dependency in db.OperatingInitiatives.IgnoreQueryFilters().AsNoTracking()
                on new { link.CompanyId, Id = link.DependsOnInitiativeId } equals new { dependency.CompanyId, dependency.Id }
            where link.CompanyId == companyId && link.InitiativeId == initiative.Id
            select new MarketingAssignmentDependencyDto(dependency.Id, dependency.Title,
                dependency.Status.ToStorageValue(), true, dependency.Status == OperatingInitiativeStatus.Completed))
            .ToListAsync(ct);
        Reject(dependencies.Any(x => x.IsHard && !x.IsSatisfied), MarketingAssignmentReasonCodes.DependencyBlocked,
            "A required company initiative must be completed before Maya can accept this assignment.");

        var activeDuplicate = await db.MarketingOperatingRuns.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.CompanyId == companyId && x.AgentId == marketingAgentId && x.OperatingInitiativeId == initiative.Id &&
            (x.Status == "requested" || x.Status == "running") && x.IdempotencyKey != request.IdempotencyKey, ct);
        Reject(activeDuplicate, MarketingAssignmentReasonCodes.DuplicateActiveAssignment,
            "Maya already has an active run for this company initiative.");

        var budgetUsed = await db.MarketingOperatingRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.OperatingInitiativeId == initiative.Id)
            .SumAsync(x => x.BudgetUsed, ct);
        var budgetLimit = Minimum(initiative.Budget, config?.MaximumMonetaryBudgetPerCycle);
        Reject(budgetLimit.HasValue && budgetUsed >= budgetLimit.Value, MarketingAssignmentReasonCodes.BudgetExhausted,
            "The available budget for this assignment is exhausted.");

        var capacityLimit = config?.MaximumTasksPerCycle ?? 12;
        var capacityUsed = await db.WorkTasks.IgnoreQueryFilters().AsNoTracking().CountAsync(x =>
            x.CompanyId == companyId && x.AssignedAgentId == marketingAgentId &&
            x.Status != WorkTaskStatus.Completed && x.Status != WorkTaskStatus.Failed, ct);
        Reject(capacityUsed >= capacityLimit, MarketingAssignmentReasonCodes.CapacityExhausted,
            "Maya's configured work capacity is exhausted.");

        int? taskLifecycleVersion = null;
        if (initiative.TaskId.HasValue)
        {
            var task = await db.WorkTasks.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.Id == initiative.TaskId.Value)
                .Select(x => new { x.CompanyId, x.AssignedAgentId, x.Status, x.SourceLifecycleVersion }).SingleOrDefaultAsync(ct);
            Reject(task is null || task.CompanyId != companyId, MarketingAssignmentReasonCodes.CrossCompanyLink,
                "The linked task does not belong to the current company.");
            Reject(task!.AssignedAgentId != marketingAgentId || task.Status is WorkTaskStatus.Completed or WorkTaskStatus.Failed,
                MarketingAssignmentReasonCodes.TaskUnavailable, "The linked task is not available to Maya.");
            taskLifecycleVersion = task.SourceLifecycleVersion;
        }

        var snapshot = initiative.SnapshotId.HasValue
            ? await db.OperatingSnapshots.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Id == initiative.SnapshotId.Value)
                .Select(x => new { x.Id, x.SchemaVersion }).SingleOrDefaultAsync(ct)
            : null;
        var validation = await db.OperatingValidationResults.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PlanId == initiative.PlanId)
            .Select(x => x.Outcome).ToListAsync(ct);
        var validationState = validation.Any(x => x == OperatingValidationOutcome.Denied) ? "denied" :
            validation.Any(x => x == OperatingValidationOutcome.ReviewRequired) ? "review_required" : "allowed";
        Reject(validationState == "denied", MarketingAssignmentReasonCodes.PlanUnavailable,
            "The operating plan contains a denied validation result.");

        var restrictions = initiative.GoalConstraints
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Key}: {x.Value?.ToJsonString() ?? "null"}").ToArray();
        var contributorIds = ReadGuidArray(initiative.GoalConstraints, "contributorAgentIds");
        var agent = await db.Agents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == marketingAgentId)
            .Select(x => new { x.AutonomyLevel }).SingleAsync(ct);
        var authority = MarketingAuthorityPolicy.Evaluate(new MarketingAuthorityContext(
            config?.AutonomyLevel ?? CompanyAutonomyLevel.Recommend, agent.AutonomyLevel,
            CompanyAutonomyLevel.OperateInternally, ApprovalSatisfied: validationState != "review_required",
            WorkloadAvailable: capacityUsed < capacityLimit,
            BudgetAvailable: !budgetLimit.HasValue || budgetUsed < budgetLimit.Value));

        return new MarketingAssignmentContextDto(companyId, initiative.GoalId, initiative.GoalVersion,
            initiative.CycleId, snapshot?.Id, snapshot?.SchemaVersion, initiative.PlanId, initiative.PlanVersion,
            initiative.Id, initiative.Version, initiative.TaskId, taskLifecycleVersion,
            initiative.DesiredOutcome, initiative.Priority.ToStorageValue(), initiative.GoalStartUtc,
            initiative.TargetUtc ?? initiative.GoalTargetUtc, marketingAgentId, contributorIds,
            initiative.OwnerUserId, dependencies, budgetLimit, budgetUsed, capacityLimit, capacityUsed,
            initiative.CompletionEvidence, validationState, restrictions, request.CorrelationId,
            authority.EffectiveAuthority.ToStorageValue(), authority.Allowed, authority.ReasonCode, authority.Explanation);
    }

    public Task<MarketingWorkEvidenceDto> ReportProgressAsync(Guid companyId, ReportMarketingWorkCommand command, CancellationToken ct) =>
        ReportAsync(companyId, command, "progress", ct);

    public Task<MarketingWorkEvidenceDto> ReportOutcomeAsync(Guid companyId, ReportMarketingWorkCommand command, CancellationToken ct) =>
        ReportAsync(companyId, command, "outcome", ct);

    public async Task<MarketingCompanySignalDto> RaiseSignalAsync(Guid companyId,
        RaiseMarketingCompanySignalCommand command, CancellationToken ct)
    {
        var existing = await db.MarketingCompanySignals.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == command.IdempotencyKey, ct);
        if (existing is not null) return Map(existing);
        if (command.MarketingOperatingRunId.HasValue && !await db.MarketingOperatingRuns.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == command.MarketingOperatingRunId, ct))
            throw new MarketingAssignmentException(MarketingAssignmentReasonCodes.CrossCompanyLink,
                "The Marketing run does not belong to the current company.");
        var signal = new MarketingCompanySignal(Guid.NewGuid(), companyId, command.MarketingOperatingRunId,
            command.SignalType, command.Severity, command.Summary, command.EvidenceJson,
            command.IdempotencyKey, command.CorrelationId);
        db.MarketingCompanySignals.Add(signal);
        await db.SaveChangesAsync(ct);
        return Map(signal);
    }

    public async Task<IReadOnlyList<MarketingWorkEvidenceDto>> ListWorkEvidenceAsync(Guid companyId, Guid? runId, CancellationToken ct)
    {
        var items = await db.MarketingWorkEvidence.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && (!runId.HasValue || x.MarketingOperatingRunId == runId))
            .OrderByDescending(x => x.CreatedUtc).Take(100).ToListAsync(ct);
        return items.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<MarketingCompanySignalDto>> ListSignalsAsync(Guid companyId, CancellationToken ct)
    {
        var items = await db.MarketingCompanySignals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.CreatedUtc).Take(100).ToListAsync(ct);
        return items.Select(Map).ToArray();
    }

    private async Task<MarketingWorkEvidenceDto> ReportAsync(Guid companyId, ReportMarketingWorkCommand command,
        string recordType, CancellationToken ct)
    {
        var existing = await db.MarketingWorkEvidence.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == command.IdempotencyKey, ct);
        if (existing is not null) return Map(existing);
        var run = await db.MarketingOperatingRuns.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == command.MarketingOperatingRunId, ct);
        if (run is null || run.OperatingInitiativeId != command.OperatingInitiativeId ||
            (command.WorkTaskId.HasValue && run.WorkTaskId != command.WorkTaskId))
            throw new MarketingAssignmentException(MarketingAssignmentReasonCodes.CrossCompanyLink,
                "The work evidence does not match the current company's Marketing assignment.");
        var reviewContext = recordType == "outcome"
            ? await db.OperatingInitiatives.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Id == command.OperatingInitiativeId)
                .Select(x => new { x.PlanId, PlanVersion = x.Plan.Version, x.CompletionEvidence })
                .SingleOrDefaultAsync(ct)
            : null;
        if (recordType == "outcome" && reviewContext is null)
            throw new MarketingAssignmentException(MarketingAssignmentReasonCodes.CrossCompanyLink,
                "The outcome does not match an operating initiative in the current company.");
        if (recordType == "outcome" && IsEmptyJson(command.CompletedArtifactsJson))
            throw new MarketingAssignmentException(MarketingAssignmentReasonCodes.CompletionEvidenceMissing,
                "Completed artifacts are required before an outcome can be reported.");
        var version = await db.MarketingWorkEvidence.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MarketingOperatingRunId == command.MarketingOperatingRunId && x.RecordType == recordType)
            .MaxAsync(x => (int?)x.Version, ct) ?? 0;
        var item = new MarketingWorkEvidence(Guid.NewGuid(), companyId, command.MarketingOperatingRunId,
            command.OperatingInitiativeId, command.WorkTaskId, recordType, version + 1,
            command.IdempotencyKey, command.EvidenceVersion, command.CompletedArtifactsJson,
            command.ExpectedResultsJson, command.ActualResultsJson, command.Confidence,
            command.DataGapsJson, command.BlockersJson, command.DependenciesJson,
            command.ChangedForecastJson, command.Lessons, command.RequestedNextAction,
            command.CorrelationId);
        db.MarketingWorkEvidence.Add(item);
        if (recordType == "outcome")
        {
            db.OperatingReviews.Add(new OperatingReview(Guid.NewGuid(), companyId,
                reviewContext!.PlanId, reviewContext.PlanVersion, command.OperatingInitiativeId,
                IsEmptyJson(command.BlockersJson)
                    ? OperatingReviewOutcome.CloseSuccessful : OperatingReviewOutcome.Continue,
                command.Lessons, reviewContext.CompletionEvidence, command.ActualResultsJson,
                command.RequestedNextAction, command.EvidenceVersion, command.Confidence,
                evidence: new Dictionary<string, JsonNode?>
                {
                    ["marketingWorkEvidenceId"] = JsonValue.Create(item.Id),
                    ["actualResults"] = JsonNode.Parse(command.ActualResultsJson),
                    ["changedForecast"] = JsonNode.Parse(command.ChangedForecastJson),
                    ["requestedNextAction"] = JsonValue.Create(command.RequestedNextAction),
                    ["correlationId"] = JsonValue.Create(command.CorrelationId)
                }));
        }
        await db.SaveChangesAsync(ct);
        return Map(item);
    }

    private static bool IsEmptyJson(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => document.RootElement.GetArrayLength() == 0,
            JsonValueKind.Object => !document.RootElement.EnumerateObject().Any(),
            _ => false
        };
    }

    private static IReadOnlyList<Guid> ReadGuidArray(IReadOnlyDictionary<string, JsonNode?> source, string key)
    {
        if (!source.TryGetValue(key, out var node) || node is not JsonArray values) return [];
        return values.Select(x => Guid.TryParse(x?.GetValue<string>(), out var id) ? id : Guid.Empty)
            .Where(x => x != Guid.Empty).Distinct().ToArray();
    }

    private static decimal? Minimum(decimal? first, decimal? second) => first.HasValue && second.HasValue
        ? Math.Min(first.Value, second.Value) : first ?? second;
    private static void Reject(bool condition, string reasonCode, string explanation)
    { if (condition) throw new MarketingAssignmentException(reasonCode, explanation); }

    private static MarketingWorkEvidenceDto Map(MarketingWorkEvidence x) => new(x.Id, x.CompanyId,
        x.MarketingOperatingRunId, x.OperatingInitiativeId, x.WorkTaskId, x.RecordType, x.Version,
        x.IdempotencyKey, x.EvidenceVersion, x.CompletedArtifactsJson, x.ExpectedResultsJson,
        x.ActualResultsJson, x.Confidence, x.DataGapsJson, x.BlockersJson, x.DependenciesJson,
        x.ChangedForecastJson, x.Lessons, x.RequestedNextAction, x.CorrelationId, x.CreatedUtc);
    private static MarketingCompanySignalDto Map(MarketingCompanySignal x) => new(x.Id, x.CompanyId,
        x.MarketingOperatingRunId, x.SignalType, x.Severity, x.Summary, x.EvidenceJson, x.Status,
        x.CycleEvaluationRequested, x.IdempotencyKey, x.CorrelationId, x.CreatedUtc, x.UpdatedUtc);
}
