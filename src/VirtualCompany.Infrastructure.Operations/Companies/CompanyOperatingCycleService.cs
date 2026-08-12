using System.Text.Json;
using System.Text.Json.Nodes;
using System.Data;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyOperatingCycleService : ICompanyOperatingCycleService, ICompanyOperatingCycleAutomationService, ICompanyOperatingReviewAutomationService
{
    private const string CapabilityId = "company-operating-recommendation";
    private const string CapabilityVersion = "1.0";
    private const string PromptVersion = "1.0";
    private const string PlanSchemaVersion = "company-operating-plan.v1";
    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyMembershipContextResolver _memberships;
    private readonly ICompanyOperatingSnapshotService _snapshots;
    private readonly IOperatingPlanValidationService _validator;
    private readonly IAgentReasoningGateway _reasoning;
    private readonly IAuditEventWriter _audit;
    private readonly IApprovalRequestService _approvals;
    private readonly ICompanyTaskCommandService _tasks;
    private readonly ICompanyOutboxEnqueuer _outbox;
    private readonly ICompanyOperatingAutonomyPolicy _autonomy;
    private readonly ICompanyExternalActionReadinessRegistry _externalActions;
    private readonly ICorrelationContextAccessor _correlation;

    public CompanyOperatingCycleService(VirtualCompanyDbContext db, ICompanyMembershipContextResolver memberships,
        ICompanyOperatingSnapshotService snapshots, IOperatingPlanValidationService validator,
        IAgentReasoningGateway reasoning, IAuditEventWriter audit, ICompanyTaskCommandService tasks,
        IApprovalRequestService approvals, ICompanyOutboxEnqueuer outbox,
        ICompanyOperatingAutonomyPolicy autonomy, ICompanyExternalActionReadinessRegistry externalActions,
        ICorrelationContextAccessor correlation)
    { _db = db; _memberships = memberships; _snapshots = snapshots; _validator = validator; _reasoning = reasoning; _audit = audit; _tasks = tasks; _approvals = approvals; _outbox = outbox; _autonomy = autonomy; _externalActions = externalActions; _correlation = correlation; }

    public async Task<OperatingCycleDto> RunRecommendationCycleAsync(Guid companyId, RequestOperatingCycleCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var member = await RequireManagerAsync(companyId, ct);
        return await RunCoreAsync(companyId, command, member.UserId, ct);
    }

    public Task<OperatingCycleDto> RunScheduledCycleAsync(Guid companyId, RequestOperatingCycleCommand command, CancellationToken ct) =>
        RunCoreAsync(companyId, command, null, ct);

    private async Task<OperatingCycleDto> RunCoreAsync(Guid companyId, RequestOperatingCycleCommand command, Guid? actorUserId, CancellationToken ct)
    {
        var config = await _db.CompanyOperatingConfigurations.SingleOrDefaultAsync(x => x.CompanyId == companyId, ct)
            ?? new CompanyOperatingConfiguration(Guid.NewGuid(), companyId);
        if (config.IsPaused || config.EmergencyStopped) throw Validation("operation", config.EmergencyStopped ? "Company operation is emergency stopped." : "Company operation is paused.");
        if (!config.CoordinatorAgentId.HasValue) throw Validation("coordinatorAgentId", "Assign an active coordinator agent before running the company cycle.");
        if (!await _db.Agents.AnyAsync(x => x.CompanyId == companyId && x.Id == config.CoordinatorAgentId && x.Status == AgentStatus.Active, ct))
            throw Validation("coordinatorAgentId", "The configured coordinator agent is not active.");

        var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId) ? _correlation.CorrelationId ?? Guid.NewGuid().ToString("N") : command.CorrelationId.Trim();
        var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey) ? $"manual:{Guid.NewGuid():N}" : command.IdempotencyKey.Trim();
        var duplicate = await _db.OperatingCycles.Include(x => x.Plans).ThenInclude(x => x.Initiatives).Include(x => x.Plans).ThenInclude(x => x.ValidationResults)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == idempotencyKey, ct);
        if (duplicate is not null) return Map(duplicate);
        await EnforceCycleLimitsAsync(companyId, config, ct);

        var cycle = new OperatingCycle(Guid.NewGuid(), companyId, command.TriggerType, command.TriggerReference,
            config.CoordinatorAgentId.Value, correlationId, idempotencyKey, config.Version);
        _db.OperatingCycles.Add(cycle);
        await SaveAsync(ct);

        try
        {
            cycle.MarkObserving(); await SaveAsync(ct);
            var snapshot = await _snapshots.CaptureAsync(companyId, cycle.Id, ct);
            if (snapshot.DataGapCount > 0) throw new CompanyOperatingValidationException(new Dictionary<string, string[]> { ["snapshot"] = ["The operating snapshot is missing active goals or active agents."] });
            cycle.MarkPlanning(snapshot.Id); await SaveAsync(ct);

            var sources = SnapshotSources(snapshot);
            var result = await _reasoning.ReasonAsync(new AgentReasoningRequest(companyId, cycle.CoordinatorAgentId,
                CapabilityId, CapabilityVersion, PromptVersion, PlanSchemaVersion,
                "Recommend a bounded set of initiatives that advances the active company goals. Each next action must use actionType 'initiative'. Cite supplied evidence for factual claims. Do not execute tools, create tasks, contact people, spend money, or change business records.",
                sources, ["initiative"], [], actorUserId, CorrelationId: correlationId), ct);
            cycle.RecordUsage(1, 0, 0, 0);
            if (result.Status is AgentAiRunStatuses.Failed or AgentAiRunStatuses.Cancelled || result.NextActions.Count == 0)
                throw new InvalidOperationException(result.FailureMessage ?? "The coordinator produced no valid initiatives.");

            var goals = await _db.CompanyGoals.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == CompanyGoalStatus.Active)
                .OrderByDescending(x => x.Priority).ThenBy(x => x.TargetUtc).Take(config.MaximumInitiativesPerCycle).ToListAsync(ct);
            var activeAgentIds = await _db.Agents.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == AgentStatus.Active)
                .Select(x => x.Id).ToHashSetAsync(ct);
            var actions = result.NextActions.Take(config.MaximumInitiativesPerCycle).ToArray();
            var objective = string.Join("; ", goals.Select(x => x.Name));
            var uncertainty = new Dictionary<string, JsonNode?>
            {
                ["confidence"] = JsonValue.Create(result.Confidence),
                ["uncertainty"] = JsonSerializer.SerializeToNode(result.Uncertainty),
                ["missingEvidence"] = JsonSerializer.SerializeToNode(result.MissingEvidence),
                ["reasoningRunId"] = JsonValue.Create(result.RunId)
            };
            var plan = new OperatingPlan(Guid.NewGuid(), companyId, cycle.Id, 1, objective, result.Summary, uncertainty: uncertainty);
            _db.OperatingPlans.Add(plan);
            for (var index = 0; index < actions.Length; index++)
            {
                var action = actions[index]; var goal = goals[index % goals.Count];
                var ownerAgentId = goal.OwnerAgentId.HasValue && activeAgentIds.Contains(goal.OwnerAgentId.Value)
                    ? goal.OwnerAgentId.Value : cycle.CoordinatorAgentId;
                var initiative = new OperatingInitiative(Guid.NewGuid(), companyId, plan.Id, goal.Id, action.Title,
                    $"Advance the goal '{goal.Name}' through the recommended work.", goal.Priority,
                    $"Evidence is recorded showing measurable progress toward '{goal.Outcome}'.",
                    ownerAgentId, goal.TargetUtc, null);
                _db.OperatingInitiatives.Add(initiative);
                if (ownerAgentId != cycle.CoordinatorAgentId)
                    _db.OperatingInitiativeCollaborators.Add(new OperatingInitiativeCollaborator(Guid.NewGuid(), companyId,
                        initiative.Id, cycle.CoordinatorAgentId, OperatingCollaborationRole.Reviewer,
                        OperatingCollaborationPattern.SequentialHandoff, 1,
                        $"Review the completed work for '{initiative.Title}' against the company goal and operating constraints.",
                        "A concise review identifying evidence gaps, risks, and the recommended next action."));
                _db.OperatingDecisions.Add(new OperatingDecision(Guid.NewGuid(), companyId, plan.Id, initiative.Id,
                    OperatingActionClass.Recommend, "initiative", "company_goal", goal.Id.ToString("N"), ownerAgentId,
                    result.Summary, result.Confidence, result.Status == AgentAiRunStatuses.NeedsReview ? "medium" : "low",
                    result.Status == AgentAiRunStatuses.NeedsReview,
                    $"{cycle.Id:N}:initiative:{index}"));
            }
            cycle.MarkValidating(); await SaveAsync(ct);
            var validation = await _validator.ValidateAsync(companyId, plan.Id, ct);
            if (validation.Any(x => x.Outcome == OperatingValidationOutcome.Denied.ToStorageValue()))
                throw new InvalidOperationException("The proposed plan did not pass company operating validation.");
            plan.SubmitForReview(); cycle.MarkAwaitingReview(); await SaveAsync(ct);
            var autonomy = await _autonomy.EvaluateAsync(companyId, plan.Id,
                CompanyOperatingAutonomyPhase.AutomaticCommit, ct);
            if (autonomy.Allowed)
                await CommitAutomaticallyAsync(companyId, plan, config.Version, actorUserId,
                    sources.Select(x => x.Id).ToArray(), autonomy, ct);
            else
            {
                var actorType = actorUserId.HasValue ? AuditActorTypes.User : AuditActorTypes.System;
                await _audit.WriteAsync(new AuditEventWriteRequest(companyId, actorType, actorUserId,
                    "company.operating_cycle.recommendation_created", "operating_cycle", cycle.Id.ToString("N"), AuditEventOutcomes.Succeeded,
                    "The coordinator created a validated recommendation plan. No work was executed.", sources.Select(x => x.Id).ToArray(),
                    new Dictionary<string, string?> { ["planId"] = plan.Id.ToString("N"), ["initiativeCount"] = actions.Length.ToString(), ["autonomyLevel"] = config.AutonomyLevel.ToStorageValue(), ["autonomyDecision"] = autonomy.ReasonCode, ["reviewRequired"] = autonomy.ReviewRequired.ToString() }, correlationId), ct);
            }
            await SaveAsync(ct);
            return await LoadAsync(companyId, cycle.Id, ct);
        }
        catch (Exception ex)
        {
            _db.ChangeTracker.Clear();
            cycle = await _db.OperatingCycles.SingleAsync(x => x.CompanyId == companyId && x.Id == cycle.Id, CancellationToken.None);
            cycle.Fail(ex is CompanyOperatingValidationException ? "snapshot_incomplete" : "recommendation_failed", SafeSummary(ex));
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, actorUserId.HasValue ? AuditActorTypes.User : AuditActorTypes.System, actorUserId,
                "company.operating_cycle.failed", "operating_cycle", cycle.Id.ToString("N"), AuditEventOutcomes.Failed,
                cycle.FailureSummary ?? "The operating cycle failed safely.", CorrelationId: correlationId), ct);
            await SaveAsync(CancellationToken.None);
            return await LoadAsync(companyId, cycle.Id, ct);
        }
    }

    public async Task<OperatingCycleDto> GetAsync(Guid companyId, Guid cycleId, CancellationToken ct)
    { await RequireMemberAsync(companyId, ct); return await LoadAsync(companyId, cycleId, ct); }

    public async Task<IReadOnlyList<OperatingCycleDto>> ListAsync(Guid companyId, int take, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, ct); take = Math.Clamp(take, 1, 100);
        var rows = await CycleQuery(companyId).OrderByDescending(x => x.RequestedUtc).Take(take).ToListAsync(ct);
        return rows.Select(Map).ToArray();
    }

    public async Task<OperatingCycleDto> ReviewPlanAsync(Guid companyId, Guid planId, ReviewOperatingPlanCommand command, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct);
        var plan = await _db.OperatingPlans.Include(x => x.Cycle).Include(x => x.Initiatives)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == planId, ct) ?? throw new KeyNotFoundException("Operating plan not found.");
        var decision = command.Decision.Trim().ToLowerInvariant();
        if (decision is not ("approve" or "reject" or "request_changes"))
            throw Validation("decision", "Decision must be approve, reject, or request changes.");

        if ((decision == "approve" && plan.Status == OperatingPlanStatus.Approved) ||
            (decision == "reject" && plan.Status == OperatingPlanStatus.Rejected) ||
            (decision == "request_changes" && plan.Status == OperatingPlanStatus.ChangesRequested))
            return await LoadAsync(companyId, plan.CycleId, ct);
        if (plan.Status != OperatingPlanStatus.AwaitingReview)
            throw Validation("plan", $"The plan is {plan.Status.ToStorageValue().Replace('_', ' ')} and cannot be reviewed again.");

        var approval = await GetOrCreatePlanApprovalAsync(companyId, plan, member.UserId, ct);
        var approvalDecision = decision == "approve" ? "approve" : "reject";
        if (approval.Status == ApprovalRequestStatus.Pending.ToStorageValue())
        {
            var result = await _approvals.DecideAsync(companyId,
                new ApprovalDecisionCommand(approval.Id, approvalDecision, approval.CurrentStep?.Id,
                    command.Comment, Guid.NewGuid()), ct);
            approval = result.Approval;
        }

        if (decision == "approve")
        {
            if (approval.Status != ApprovalRequestStatus.Approved.ToStorageValue())
                throw Validation("approval", "The operating plan still requires approval before it can be committed.");
            foreach (var initiative in plan.Initiatives.Where(x => x.Status == OperatingInitiativeStatus.Proposed))
                initiative.Approve();
        }
        else if (decision == "request_changes")
        {
            if (plan.Status == OperatingPlanStatus.Rejected)
                plan.RequestChanges();
        }
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, member.UserId,
            $"company.operating_plan.{decision}", "operating_plan", plan.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            string.IsNullOrWhiteSpace(command.Comment) ? $"Operating plan {decision.Replace('_', ' ')}." : Truncate(command.Comment, 2000),
            Metadata: new Dictionary<string, string?> { ["cycleId"] = plan.CycleId.ToString("N"), ["approvalRequestId"] = approval.Id.ToString("N") }, CorrelationId: command.CorrelationId ?? plan.Cycle.CorrelationId), ct);
        await SaveAsync(ct); return await LoadAsync(companyId, plan.CycleId, ct);
    }

    public async Task<OperatingCycleDto> CommitPlanAsync(Guid companyId, Guid planId, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct);
        var plan = await _db.OperatingPlans.Include(x => x.Cycle).Include(x => x.Initiatives).Include(x => x.ValidationResults)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == planId, ct) ?? throw new KeyNotFoundException("Operating plan not found.");
        if (plan.Status == OperatingPlanStatus.Committed) return await LoadAsync(companyId, plan.CycleId, ct);
        if (plan.Status != OperatingPlanStatus.Approved) throw Validation("plan", "Only an approved operating plan can be committed.");
        var approved = await _db.ApprovalRequests.AsNoTracking().AnyAsync(x => x.CompanyId == companyId &&
            x.TargetEntityType == ApprovalTargetEntityType.OperatingPlan.ToStorageValue() && x.TargetEntityId == plan.Id &&
            x.Status == ApprovalRequestStatus.Approved, ct);
        if (!approved) throw Validation("approval", "The operating plan does not have a current authoritative approval.");

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var created = 0;
        try
        {
            var config = await _db.CompanyOperatingConfigurations.SingleOrDefaultAsync(x => x.CompanyId == companyId, ct)
                ?? throw Validation("configuration", "Company operating configuration is required.");
            if (config.IsPaused || config.EmergencyStopped) throw Validation("operation", config.EmergencyStopped ? "Company operation is emergency stopped." : "Company operation is paused.");
            if (plan.Cycle.ConfigurationVersion != config.Version)
                throw Validation("configuration", "Operating settings changed after this plan was created. Request a new plan.");
            if (plan.Initiatives.Count > config.MaximumTasksPerCycle)
                throw Validation("tasks", "The plan exceeds the configured task limit.");
            var plannedBudget = plan.Initiatives.Sum(x => x.Budget ?? 0m);
            if (config.MaximumMonetaryBudgetPerCycle.HasValue && plannedBudget > config.MaximumMonetaryBudgetPerCycle.Value)
                throw Validation("budget", "The plan exceeds the configured monetary planning budget.");
            var today = DateTime.UtcNow.Date;
            var daily = await _db.OperatingCycles.Where(x => x.CompanyId == companyId && x.RequestedUtc >= today)
                .GroupBy(_ => 1).Select(x => new { Tasks = x.Sum(y => y.TasksCreated), Model = x.Sum(y => y.ModelCallsUsed), Tools = x.Sum(y => y.ToolCallsUsed), Money = x.Sum(y => y.MonetaryBudgetUsed) }).SingleOrDefaultAsync(ct);
            if ((daily?.Tasks ?? 0) + plan.Initiatives.Count > config.MaximumTasksPerDay)
                throw Validation("budget", "The daily task-creation budget would be exceeded.");
            if (config.MaximumMonetaryBudgetPerDay.HasValue && (daily?.Money ?? 0m) + plannedBudget > config.MaximumMonetaryBudgetPerDay.Value)
                throw Validation("budget", "The daily monetary planning budget would be exceeded.");

            var validation = await _validator.ValidateAsync(companyId, plan.Id, ct);
            if (validation.Any(x => x.Outcome == OperatingValidationOutcome.Denied.ToStorageValue()))
                throw Validation("validation", "The approved plan is no longer eligible to commit. Review the current validation results.");

            plan.BeginCommit();
            foreach (var initiative in plan.Initiatives.OrderByDescending(x => x.Priority).ThenBy(x => x.TargetUtc))
            {
                if (initiative.TaskId.HasValue) continue;
                var dedupeKey = $"operating:{initiative.Id:N}:plan-v{plan.Version}";
                var existing = await _db.AgentTaskCreationDedupeRecords.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DedupeKey == dedupeKey, ct);
                if (existing is not null)
                {
                    initiative.LinkWork(existing.TaskId, null);
                    if (!await _db.OperatingDispatches.AnyAsync(x => x.CompanyId == companyId && x.InitiativeId == initiative.Id, ct))
                    {
                        var collaborativeExisting = await _db.OperatingInitiativeCollaborators
                            .AnyAsync(x => x.CompanyId == companyId && x.InitiativeId == initiative.Id, ct);
                        _db.OperatingDispatches.Add(new OperatingDispatch(Guid.NewGuid(), companyId, initiative.Id,
                            existing.TaskId, collaborativeExisting ? OperatingDispatchKind.MultiAgent : OperatingDispatchKind.SingleAgent,
                            plan.Cycle.CorrelationId));
                    }
                    continue;
                }
                var payload = new Dictionary<string, JsonNode?> { ["operatingPlanId"] = JsonValue.Create(plan.Id), ["operatingInitiativeId"] = JsonValue.Create(initiative.Id), ["companyGoalId"] = JsonValue.Create(initiative.GoalId), ["completionEvidence"] = JsonValue.Create(initiative.CompletionEvidence), ["businessIdempotencyKey"] = JsonValue.Create(dedupeKey) };
                var task = await _tasks.CreateTaskAsync(companyId, new CreateTaskCommand("operating_initiative", initiative.Title,
                    initiative.DesiredOutcome, initiative.Priority.ToStorageValue(), initiative.TargetUtc, initiative.OwnerAgentId, payload,
                    RationaleSummary: plan.RationaleSummary, CorrelationId: plan.Cycle.CorrelationId), ct);
                initiative.LinkWork(task.Id, null);
                _db.AgentTaskCreationDedupeRecords.Add(new AgentTaskCreationDedupeRecord(Guid.NewGuid(), companyId,
                    dedupeKey, task.Id, initiative.OwnerAgentId ?? plan.Cycle.CoordinatorAgentId, "company_operating_plan",
                    initiative.Id.ToString("N"), plan.Cycle.CorrelationId, DateTime.UtcNow, DateTime.UtcNow.AddYears(1)));
                var collaborative = await _db.OperatingInitiativeCollaborators
                    .AnyAsync(x => x.CompanyId == companyId && x.InitiativeId == initiative.Id, ct);
                if (!await _db.OperatingDispatches.AnyAsync(x => x.CompanyId == companyId && x.InitiativeId == initiative.Id, ct))
                    _db.OperatingDispatches.Add(new OperatingDispatch(Guid.NewGuid(), companyId, initiative.Id, task.Id,
                        collaborative ? OperatingDispatchKind.MultiAgent : OperatingDispatchKind.SingleAgent,
                        plan.Cycle.CorrelationId));
                created++;
            }
            plan.Cycle.RecordUsage(0, 0, created, 0); plan.MarkCommitted(); plan.Cycle.Complete();
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, member.UserId,
                "company.operating_plan.committed", "operating_plan", plan.Id.ToString("N"), AuditEventOutcomes.Succeeded,
                $"The approved plan was committed as {created} new task(s).", Metadata: new Dictionary<string, string?> { ["cycleId"] = plan.CycleId.ToString("N"), ["tasksCreated"] = created.ToString() }, CorrelationId: plan.Cycle.CorrelationId), ct);
            await SaveAsync(ct);
            await transaction.CommitAsync(ct);
            return await LoadAsync(companyId, plan.CycleId, ct);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, member.UserId,
                "company.operating_plan.commit_failed", "operating_plan", plan.Id.ToString("N"), AuditEventOutcomes.Failed,
                $"Plan commit was rolled back without creating partial work: {Truncate(ex.Message, 1000)}", CorrelationId: plan.Cycle.CorrelationId), CancellationToken.None);
            await SaveAsync(CancellationToken.None); throw;
        }
    }

    private async Task CommitAutomaticallyAsync(Guid companyId, OperatingPlan plan, int expectedConfigurationVersion,
        Guid? actorUserId, IReadOnlyList<string> sourceIds, CompanyOperatingAutonomyDecision autonomy,
        CancellationToken ct)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var config = await _db.CompanyOperatingConfigurations.SingleAsync(x => x.CompanyId == companyId, ct);
            if (config.IsPaused || config.EmergencyStopped || config.Version != expectedConfigurationVersion)
                throw Validation("configuration", "Company operating settings changed or operation was paused before automatic work could be committed.");
            var policy = await _autonomy.EvaluateAsync(companyId, plan.Id,
                CompanyOperatingAutonomyPhase.AutomaticCommit, ct);
            if (!policy.Allowed) throw Validation("autonomy", policy.Explanation);
            var validation = await _validator.ValidateAsync(companyId, plan.Id, ct);
            if (validation.Any(x => x.Outcome != OperatingValidationOutcome.Allowed.ToStorageValue()))
                throw Validation("validation", "Automatic commit stopped because the current plan requires review or is no longer allowed.");

            plan.Approve();
            foreach (var initiative in plan.Initiatives.Where(x => x.Status == OperatingInitiativeStatus.Proposed))
                initiative.Approve();
            plan.BeginCommit();
            var created = 0;
            foreach (var initiative in plan.Initiatives.OrderByDescending(x => x.Priority).ThenBy(x => x.TargetUtc))
            {
                if (initiative.TaskId.HasValue) continue;
                var dedupeKey = $"operating:{initiative.Id:N}:plan-v{plan.Version}";
                var existing = await _db.AgentTaskCreationDedupeRecords.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DedupeKey == dedupeKey, ct);
                Guid taskId;
                if (existing is not null)
                {
                    taskId = existing.TaskId;
                }
                else
                {
                    var payload = new Dictionary<string, JsonNode?>
                    {
                        ["operatingPlanId"] = JsonValue.Create(plan.Id),
                        ["operatingInitiativeId"] = JsonValue.Create(initiative.Id),
                        ["companyGoalId"] = JsonValue.Create(initiative.GoalId),
                        ["completionEvidence"] = JsonValue.Create(initiative.CompletionEvidence),
                        ["businessIdempotencyKey"] = JsonValue.Create(dedupeKey)
                    };
                    var task = await _tasks.CreateTaskAsync(companyId, new CreateTaskCommand("operating_initiative",
                        initiative.Title, initiative.DesiredOutcome, initiative.Priority.ToStorageValue(),
                        initiative.TargetUtc, initiative.OwnerAgentId, payload, RationaleSummary: plan.RationaleSummary,
                        CorrelationId: plan.Cycle.CorrelationId), ct);
                    taskId = task.Id;
                    _db.AgentTaskCreationDedupeRecords.Add(new AgentTaskCreationDedupeRecord(Guid.NewGuid(), companyId,
                        dedupeKey, taskId, initiative.OwnerAgentId ?? plan.Cycle.CoordinatorAgentId,
                        "company_operating_plan", initiative.Id.ToString("N"), plan.Cycle.CorrelationId,
                        DateTime.UtcNow, DateTime.UtcNow.AddYears(1)));
                    created++;
                }
                initiative.LinkWork(taskId, null);
                if (!await _db.OperatingDispatches.AnyAsync(x => x.CompanyId == companyId && x.InitiativeId == initiative.Id, ct))
                {
                    var collaborative = await _db.OperatingInitiativeCollaborators
                        .AnyAsync(x => x.CompanyId == companyId && x.InitiativeId == initiative.Id, ct);
                    _db.OperatingDispatches.Add(new OperatingDispatch(Guid.NewGuid(), companyId, initiative.Id,
                        taskId, collaborative ? OperatingDispatchKind.MultiAgent : OperatingDispatchKind.SingleAgent,
                        plan.Cycle.CorrelationId));
                }
            }
            plan.Cycle.RecordUsage(0, 0, created, 0);
            plan.MarkCommitted();
            plan.Cycle.Complete();
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId,
                actorUserId.HasValue ? AuditActorTypes.User : AuditActorTypes.System, actorUserId,
                "company.operating_cycle.recommendation_created", "operating_cycle", plan.CycleId.ToString("N"),
                AuditEventOutcomes.Succeeded,
                "The coordinator created a validated plan and atomically organized eligible internal work within the configured autonomy boundary.",
                sourceIds, new Dictionary<string, string?>
                {
                    ["planId"] = plan.Id.ToString("N"), ["initiativeCount"] = plan.Initiatives.Count.ToString(),
                    ["tasksCreated"] = created.ToString(), ["autonomyLevel"] = config.AutonomyLevel.ToStorageValue(),
                    ["autonomyDecision"] = autonomy.ReasonCode, ["reviewRequired"] = autonomy.ReviewRequired.ToString()
                }, plan.Cycle.CorrelationId), ct);
            await SaveAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<ApprovalRequestDto> GetOrCreatePlanApprovalAsync(Guid companyId, OperatingPlan plan,
        Guid requesterUserId, CancellationToken ct)
    {
        var existingId = await _db.ApprovalRequests.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TargetEntityType == ApprovalTargetEntityType.OperatingPlan.ToStorageValue() && x.TargetEntityId == plan.Id)
            .OrderByDescending(x => x.CreatedUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (existingId.HasValue) return await _approvals.GetAsync(companyId, existingId.Value, ct);
        return await _approvals.CreateAsync(companyId, new CreateApprovalRequestCommand(
            ApprovalTargetEntityType.OperatingPlan.ToStorageValue(), plan.Id, AuditActorTypes.User, requesterUserId,
            "company_operating_plan", new Dictionary<string, JsonNode?>
            {
                ["planVersion"] = JsonValue.Create(plan.Version),
                ["cycleId"] = JsonValue.Create(plan.CycleId),
                ["configurationVersion"] = JsonValue.Create(plan.Cycle.ConfigurationVersion),
                ["rationaleSummary"] = JsonValue.Create(plan.RationaleSummary)
            }, RequiredRole: "manager"), ct);
    }

    public async Task<IReadOnlyList<OperatingReviewDto>> ReviewCommittedWorkAsync(Guid companyId, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct);
        return await ReviewCommittedWorkCoreAsync(companyId, AuditActorTypes.User, member.UserId, ct);
    }

    public Task<IReadOnlyList<OperatingReviewDto>> ReviewCommittedWorkAutomaticallyAsync(Guid companyId, CancellationToken ct) =>
        ReviewCommittedWorkCoreAsync(companyId, AuditActorTypes.System, null, ct);

    private async Task<IReadOnlyList<OperatingReviewDto>> ReviewCommittedWorkCoreAsync(Guid companyId,
        string actorType, Guid? actorId, CancellationToken ct)
    {
        var initiatives = await _db.OperatingInitiatives.Include(x => x.Task).Include(x => x.Plan)
            .Where(x => x.CompanyId == companyId && x.Status == OperatingInitiativeStatus.Active && x.TaskId != null).ToListAsync(ct);
        var reviews = new List<OperatingReview>();
        foreach (var initiative in initiatives)
        {
            var task = initiative.Task!; var version = $"task:{task.Id:N}:{task.UpdatedUtc.Ticks}";
            if (await _db.OperatingReviews.AnyAsync(x => x.CompanyId == companyId && x.InitiativeId == initiative.Id && x.EvidenceVersion == version, ct)) continue;
            var actualEvidence = ExtractActualEvidence(task);
            var hasEvidence = !string.IsNullOrWhiteSpace(actualEvidence);
            var outcome = task.Status switch { WorkTaskStatus.Completed when hasEvidence => OperatingReviewOutcome.CloseSuccessful, WorkTaskStatus.Completed => OperatingReviewOutcome.RequestEvidence, WorkTaskStatus.Failed => OperatingReviewOutcome.Revise, WorkTaskStatus.Blocked => OperatingReviewOutcome.Escalate, WorkTaskStatus.AwaitingApproval => OperatingReviewOutcome.Escalate, _ => OperatingReviewOutcome.Continue };
            var summary = outcome switch { OperatingReviewOutcome.CloseSuccessful => "The linked work completed and supplied the expected evidence.", OperatingReviewOutcome.RequestEvidence => "The task is marked complete, but the expected evidence is missing.", OperatingReviewOutcome.Revise => "The linked work failed; a new governed plan version is required.", OperatingReviewOutcome.Escalate => "The linked work is blocked or awaiting approval and needs attention.", _ => "The linked work is still progressing." };
            var nextAction = outcome switch { OperatingReviewOutcome.CloseSuccessful => "Close the initiative and retain the linked evidence.", OperatingReviewOutcome.RequestEvidence => "Provide the missing completion evidence before closing the initiative.", OperatingReviewOutcome.Revise => "Review and approve the newly proposed plan revision.", OperatingReviewOutcome.Escalate => "Resolve the block or approval before work continues.", _ => "Continue the current work and review again when evidence changes." };
            if (outcome == OperatingReviewOutcome.CloseSuccessful) initiative.Complete();
            else if (outcome is OperatingReviewOutcome.Revise or OperatingReviewOutcome.Escalate) initiative.Block();
            if (outcome == OperatingReviewOutcome.Revise)
            {
                var revisionSubmitted = await CreatePlanRevisionAsync(initiative.Plan, initiative, ct);
                if (!revisionSubmitted)
                    nextAction = "Resolve the recorded revision validation failures before submitting replacement work for review.";
            }
            var workflow = task.WorkflowInstanceId.HasValue
                ? await _db.WorkflowInstances.AsNoTracking().Where(x => x.CompanyId == companyId && x.Id == task.WorkflowInstanceId)
                    .Select(x => new { x.Id, status = x.Status.ToString(), x.CurrentStep, x.UpdatedUtc, x.CompletedUtc }).SingleOrDefaultAsync(ct)
                : null;
            var review = new OperatingReview(Guid.NewGuid(), companyId, initiative.PlanId, initiative.Plan.Version,
                initiative.Id, outcome, summary, initiative.CompletionEvidence, actualEvidence, nextAction,
                version, task.ConfidenceScore, evidence: new Dictionary<string, JsonNode?>
                {
                    ["taskId"] = JsonValue.Create(task.Id), ["taskStatus"] = JsonValue.Create(task.Status.ToStorageValue()),
                    ["taskUpdatedUtc"] = JsonValue.Create(task.UpdatedUtc), ["taskSourceType"] = JsonValue.Create(task.SourceType),
                    ["originatingAgentId"] = JsonValue.Create(task.OriginatingAgentId), ["triggerSource"] = JsonValue.Create(task.TriggerSource),
                    ["triggerEventId"] = JsonValue.Create(task.TriggerEventId), ["outputPayload"] = JsonSerializer.SerializeToNode(task.OutputPayload),
                    ["rationaleSummary"] = JsonValue.Create(task.RationaleSummary), ["workflow"] = JsonSerializer.SerializeToNode(workflow),
                    ["beforeSnapshotId"] = JsonValue.Create(initiative.Plan.Cycle.SnapshotId), ["operatingPlanVersion"] = JsonValue.Create(initiative.Plan.Version)
                });
            _db.OperatingReviews.Add(review); reviews.Add(review);
        }
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, actorType, actorId,
            "company.operating_work.reviewed", "company", companyId.ToString("N"), AuditEventOutcomes.Succeeded,
            $"Reviewed {reviews.Count} changed initiative(s) against current task evidence.", Metadata: new Dictionary<string, string?> { ["reviewCount"] = reviews.Count.ToString(), ["replanCount"] = reviews.Count(x => x.Outcome == OperatingReviewOutcome.Revise).ToString() }), ct);
        await SaveAsync(ct);
        return reviews.Select(MapReview).ToArray();
    }

    private async Task<bool> CreatePlanRevisionAsync(OperatingPlan source, OperatingInitiative failed, CancellationToken ct)
    {
        var existing = await _db.OperatingPlans.AsNoTracking()
            .Where(x => x.CompanyId == source.CompanyId && x.SupersedesPlanId == source.Id)
            .Select(x => (OperatingPlanStatus?)x.Status).SingleOrDefaultAsync(ct);
        if (existing.HasValue) return existing == OperatingPlanStatus.AwaitingReview;
        var version = await _db.OperatingPlans.Where(x => x.CompanyId == source.CompanyId && x.CycleId == source.CycleId).MaxAsync(x => x.Version, ct) + 1;
        var revision = new OperatingPlan(Guid.NewGuid(), source.CompanyId, source.CycleId, version,
            source.Objective, $"Revision requested because '{failed.Title}' did not produce the expected outcome.", source.Id,
            new Dictionary<string, JsonNode?> { ["reason"] = JsonValue.Create("failed_work"), ["sourceInitiativeId"] = JsonValue.Create(failed.Id) });
        _db.OperatingPlans.Add(revision);
        var replacement = new OperatingInitiative(Guid.NewGuid(), source.CompanyId, revision.Id, failed.GoalId,
            $"Revise: {failed.Title}", failed.DesiredOutcome, failed.Priority, failed.CompletionEvidence,
            failed.OwnerAgentId, failed.TargetUtc?.AddDays(7), failed.Budget);
        _db.OperatingInitiatives.Add(replacement);
        _db.OperatingDecisions.Add(new OperatingDecision(Guid.NewGuid(), source.CompanyId, revision.Id, replacement.Id,
            OperatingActionClass.Recommend, "initiative", "company_goal", failed.GoalId.ToString("N"), failed.OwnerAgentId,
            revision.RationaleSummary, taskConfidence(failed.Task), "medium", true, $"review-revision:{failed.Id:N}:{version}"));
        await SaveAsync(ct);
        var validation = await _validator.ValidateAsync(source.CompanyId, revision.Id, ct);
        if (validation.Any(x => x.Outcome == OperatingValidationOutcome.Denied.ToStorageValue())) return false;
        revision.SubmitForReview();
        await SaveAsync(ct);
        return true;
    }

    private static decimal taskConfidence(WorkTask? task) => task?.ConfidenceScore ?? .5m;
    private static string? ExtractActualEvidence(WorkTask task)
    {
        if (task.OutputPayload.Count > 0) return Truncate(JsonSerializer.Serialize(task.OutputPayload), 4000);
        if (!string.IsNullOrWhiteSpace(task.RationaleSummary)) return Truncate(task.RationaleSummary, 4000);
        return null;
    }
    private static OperatingReviewDto MapReview(OperatingReview x) => new(x.Id, x.PlanId, x.PlanVersion,
        x.InitiativeId, x.Outcome.ToStorageValue(), x.Summary, x.ExpectedEvidence, x.ActualEvidence,
        x.NextAction, x.EvidenceVersion, x.Confidence, x.CreatedUtc);

    public async Task<OperatingDecisionDto> ProposeControlledNotificationAsync(Guid companyId, ProposeControlledNotificationCommand command, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct);
        var config = await _db.CompanyOperatingConfigurations.SingleOrDefaultAsync(x => x.CompanyId == companyId, ct) ?? new CompanyOperatingConfiguration(Guid.NewGuid(), companyId);
        if (config.AutonomyLevel != CompanyAutonomyLevel.ControlledExecution) throw Validation("autonomyLevel", "Controlled execution must be enabled before proposing an external notification.");
        var readiness = _externalActions.Find("operator_notification");
        if (readiness is null || !readiness.Ready) throw Validation("actionType", "This external action is not registered as ready for controlled execution.");
        var plan = await _db.OperatingPlans.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == command.PlanId, ct) ?? throw new KeyNotFoundException("Operating plan not found.");
        if (plan.Status is not (OperatingPlanStatus.AwaitingReview or OperatingPlanStatus.Approved or OperatingPlanStatus.Committed)) throw Validation("plan", "The plan is not available for controlled actions.");
        if (!await _db.CompanyMemberships.AnyAsync(x => x.CompanyId == companyId && x.UserId == command.RecipientUserId && x.Status == CompanyMembershipStatus.Active, ct)) throw Validation("recipientUserId", "The notification recipient must be an active company member.");
        if (string.IsNullOrWhiteSpace(command.Title) || command.Title.Length > 200 || string.IsNullOrWhiteSpace(command.Body) || command.Body.Length > 4000) throw Validation("notification", "A title and bounded message are required.");
        var key = $"operating-notification:{plan.Id:N}:{command.RecipientUserId:N}:{command.Title.Trim().ToLowerInvariant()}";
        var existing = await _db.OperatingDecisions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            await EnsureControlledActionApprovalAsync(companyId, existing, member.UserId, config.Version, readiness, ct);
            return MapDecision(existing);
        }
        var payload = new Dictionary<string, JsonNode?> { ["title"] = JsonValue.Create(command.Title.Trim()), ["body"] = JsonValue.Create(command.Body.Trim()), ["actionUrl"] = JsonValue.Create(command.ActionUrl?.Trim()) };
        var decision = new OperatingDecision(Guid.NewGuid(), companyId, plan.Id, null, OperatingActionClass.ExternalExecute,
            "operator_notification", "company_member", command.RecipientUserId.ToString("N"), null,
            "Send the approved operating update to the selected company member.", 1m, "low", true, key, payload);
        _db.OperatingDecisions.Add(decision);
        _db.OperatingValidationResults.Add(new OperatingValidationResult(Guid.NewGuid(), companyId, plan.Id, decision.Id,
            "controlled-execution-policy", "1.0", OperatingValidationOutcome.ReviewRequired, "explicit_manager_approval",
            "A company manager must explicitly execute this notification. It will be delivered through the durable outbox.", true, config.Version));
        await SaveAsync(ct);
        await EnsureControlledActionApprovalAsync(companyId, decision, member.UserId, config.Version, readiness, ct);
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, member.UserId,
            "company.controlled_action.proposed", "operating_decision", decision.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "A controlled notification was proposed and is waiting for explicit execution approval.", CorrelationId: command.CorrelationId), ct);
        await SaveAsync(ct); return MapDecision(decision);
    }

    private async Task EnsureControlledActionApprovalAsync(Guid companyId, OperatingDecision decision,
        Guid requesterUserId, int configurationVersion, CompanyExternalActionReadiness readiness, CancellationToken ct)
    {
        if (await _db.ApprovalRequests.AsNoTracking().AnyAsync(x => x.CompanyId == companyId &&
            x.TargetEntityType == ApprovalTargetEntityType.OperatingDecision.ToStorageValue() &&
            x.TargetEntityId == decision.Id, ct)) return;
        await _approvals.CreateAsync(companyId, new CreateApprovalRequestCommand(
            ApprovalTargetEntityType.OperatingDecision.ToStorageValue(), decision.Id, AuditActorTypes.User, requesterUserId,
            "company_controlled_action", new Dictionary<string, JsonNode?>
            {
                ["planId"] = JsonValue.Create(decision.PlanId), ["actionType"] = JsonValue.Create(decision.ActionType),
                ["targetType"] = JsonValue.Create(decision.TargetType), ["targetId"] = JsonValue.Create(decision.TargetId),
                ["outboxTopic"] = JsonValue.Create(readiness.OutboxTopic), ["configurationVersion"] = JsonValue.Create(configurationVersion)
            }, RequiredRole: "manager"), ct);
    }

    public async Task<OperatingDecisionDto> ExecuteControlledActionAsync(Guid companyId, Guid decisionId, CancellationToken ct)
    {
        var member = await RequireManagerAsync(companyId, ct);
        var config = await _db.CompanyOperatingConfigurations.SingleOrDefaultAsync(x => x.CompanyId == companyId, ct) ?? throw Validation("configuration", "Company operating configuration is required.");
        if (config.IsPaused || config.EmergencyStopped || config.AutonomyLevel != CompanyAutonomyLevel.ControlledExecution) throw Validation("operation", "Controlled execution is unavailable while paused, stopped, or disabled.");
        var decision = await _db.OperatingDecisions.Include(x => x.Plan).ThenInclude(x => x.Cycle).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == decisionId, ct) ?? throw new KeyNotFoundException("Operating decision not found.");
        if (decision.ActionClass != OperatingActionClass.ExternalExecute || decision.ActionType != "operator_notification" || !decision.ApprovalRequired) throw Validation("decision", "This decision is not a supported controlled action.");
        var readiness = _externalActions.Find(decision.ActionType);
        if (readiness is null || !readiness.Ready || !readiness.RequiresApproval || readiness.OutboxTopic != CompanyOutboxTopics.NotificationDeliveryRequested)
            throw Validation("decision", "This external action is not currently registered as ready for controlled execution.");
        if (decision.Plan.Status is not (OperatingPlanStatus.Approved or OperatingPlanStatus.Committed)) throw Validation("plan", "Approve the operating plan before executing its controlled action.");
        if (decision.Plan.Cycle.ConfigurationVersion != config.Version) throw Validation("configuration", "Operating settings changed after this action was proposed. Request a new controlled action.");
        var approved = await _db.ApprovalRequests.AsNoTracking().AnyAsync(x => x.CompanyId == companyId &&
            x.TargetEntityType == ApprovalTargetEntityType.OperatingDecision.ToStorageValue() &&
            x.TargetEntityId == decision.Id && x.Status == ApprovalRequestStatus.Approved, ct);
        if (!approved) throw Validation("approval", "This controlled action requires a completed manager approval before it can be queued.");
        var recipientId = Guid.Parse(decision.TargetId); var title = decision.Payload["title"]?.GetValue<string>() ?? "Company operation update"; var body = decision.Payload["body"]?.GetValue<string>() ?? decision.RationaleSummary; var actionUrl = decision.Payload["actionUrl"]?.GetValue<string>();
        if (!await _db.CompanyMemberships.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.UserId == recipientId && x.Status == CompanyMembershipStatus.Active, ct))
            throw Validation("recipient", "The notification recipient is no longer an active company member.");
        _outbox.Enqueue(companyId, CompanyOutboxTopics.NotificationDeliveryRequested,
            new NotificationDeliveryRequestedMessage(companyId, "company_operation", "normal", title, body,
                "operating_decision", decision.Id, actionUrl, recipientId, null, null, null,
                $"controlled-action:{decision.Id:N}", decision.Plan.Cycle.CorrelationId), decision.Plan.Cycle.CorrelationId,
            idempotencyKey: $"controlled-action:{decision.Id:N}");
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, member.UserId,
            "company.controlled_action.approved_and_queued", "operating_decision", decision.Id.ToString("N"), AuditEventOutcomes.Succeeded,
            "The manager approved the controlled notification. Delivery was queued durably for retry and reconciliation.", CorrelationId: decision.Plan.Cycle.CorrelationId), ct);
        await SaveAsync(ct); return MapDecision(decision);
    }

    private async Task EnforceCycleLimitsAsync(Guid companyId, CompanyOperatingConfiguration config, CancellationToken ct)
    {
        var since = DateTime.UtcNow.Date;
        if (await _db.OperatingCycles.CountAsync(x => x.CompanyId == companyId && x.RequestedUtc >= since, ct) >= config.MaximumCyclesPerDay)
            throw Validation("cycle", "The configured daily operating-cycle limit has been reached.");
        var latest = await _db.OperatingCycles.Where(x => x.CompanyId == companyId).MaxAsync(x => (DateTime?)x.RequestedUtc, ct);
        if (latest.HasValue && latest.Value.AddMinutes(config.MinimumCycleIntervalMinutes) > DateTime.UtcNow)
            throw Validation("cycle", "The minimum interval between operating cycles has not elapsed.");
        var usage = await _db.OperatingCycles.Where(x => x.CompanyId == companyId && x.RequestedUtc >= since)
            .GroupBy(_ => 1).Select(x => new { Model = x.Sum(y => y.ModelCallsUsed), Tools = x.Sum(y => y.ToolCallsUsed), Tasks = x.Sum(y => y.TasksCreated), Money = x.Sum(y => y.MonetaryBudgetUsed) }).SingleOrDefaultAsync(ct);
        if ((usage?.Model ?? 0) >= config.MaximumModelCallsPerDay) throw Validation("budget", "The daily model-call budget has been reached.");
        if ((usage?.Tools ?? 0) >= config.MaximumToolCallsPerDay) throw Validation("budget", "The daily tool-call budget has been reached.");
        if ((usage?.Tasks ?? 0) >= config.MaximumTasksPerDay) throw Validation("budget", "The daily task budget has been reached.");
        if (config.MaximumMonetaryBudgetPerDay.HasValue && (usage?.Money ?? 0m) >= config.MaximumMonetaryBudgetPerDay.Value) throw Validation("budget", "The daily monetary budget has been reached.");
    }

    private static IReadOnlyList<AgentAiSource> SnapshotSources(OperatingSnapshotDto snapshot) => snapshot.Payload
        .Where(x => x.Key != "observedAtUtc").Select(x => new AgentAiSource($"snapshot:{snapshot.Id:N}:{x.Key}", "operating_snapshot", x.Key,
            Truncate(x.Value?.ToJsonString() ?? "null", 5000), snapshot.CreatedUtc)).ToArray();
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    private static string SafeSummary(Exception ex) => ex is CompanyOperatingValidationException ? ex.Message : Truncate(string.IsNullOrWhiteSpace(ex.Message) ? "The operating cycle failed safely." : ex.Message, 2000);
    private IQueryable<OperatingCycle> CycleQuery(Guid companyId) => _db.OperatingCycles.AsNoTracking().Where(x => x.CompanyId == companyId)
        .Include(x => x.Plans).ThenInclude(x => x.Initiatives).Include(x => x.Plans).ThenInclude(x => x.ValidationResults).AsSplitQuery();
    private async Task<OperatingCycleDto> LoadAsync(Guid companyId, Guid cycleId, CancellationToken ct) => Map(await CycleQuery(companyId).SingleOrDefaultAsync(x => x.Id == cycleId, ct) ?? throw new KeyNotFoundException("Operating cycle not found."));
    private async Task SaveAsync(CancellationToken ct) => await _db.SaveChangesAsync(ct);
    private async Task<ResolvedCompanyMembershipContext> RequireMemberAsync(Guid companyId, CancellationToken ct) => await _memberships.ResolveAsync(companyId, ct) ?? throw new UnauthorizedAccessException("Active company membership is required.");
    private async Task<ResolvedCompanyMembershipContext> RequireManagerAsync(Guid companyId, CancellationToken ct) { var m = await RequireMemberAsync(companyId, ct); if (m.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager)) throw new UnauthorizedAccessException("Company manager access is required."); return m; }
    private static CompanyOperatingValidationException Validation(string key, string message) => new(new Dictionary<string, string[]> { [key] = [message] });
    private static OperatingCycleDto Map(OperatingCycle x) => new(x.Id, x.CompanyId, x.TriggerType, x.TriggerReference, x.CoordinatorAgentId, x.Status.ToStorageValue(), x.ConfigurationVersion, x.CorrelationId, x.IdempotencyKey, x.SnapshotId, x.ModelCallsUsed, x.ToolCallsUsed, x.TasksCreated, x.MonetaryBudgetUsed, x.FailureCode, x.FailureSummary, x.RequestedUtc, x.StartedUtc, x.CompletedUtc, x.Plans.OrderByDescending(p => p.Version).Select(Map).ToArray());
    private static OperatingPlanDto Map(OperatingPlan x) => new(x.Id, x.CompanyId, x.CycleId, x.Version, x.Status.ToStorageValue(), x.Objective, x.RationaleSummary, x.Uncertainty, x.Initiatives.Select(i => new OperatingInitiativeDto(i.Id, i.GoalId, i.Title, i.DesiredOutcome, i.Priority.ToStorageValue(), i.Status.ToStorageValue(), i.CompletionEvidence, i.OwnerAgentId, i.TargetUtc, i.Budget, i.TaskId, i.WorkflowInstanceId, i.Version)).ToArray(), x.ValidationResults.Select(OperatingPlanValidationService.Map).ToArray(), x.CreatedUtc, x.UpdatedUtc);
    private static OperatingDecisionDto MapDecision(OperatingDecision x) => new(x.Id, x.PlanId, x.ActionClass.ToStorageValue(), x.ActionType, x.TargetType, x.TargetId, x.RationaleSummary, x.RiskLevel, x.ApprovalRequired, x.CreatedUtc);
}
