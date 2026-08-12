using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class OperatingPlanValidationService : IOperatingPlanValidationService
{
    private const string ValidatorPrefix = "company-operating-policy/";
    private const string ValidatorVersion = "2.0";
    private readonly VirtualCompanyDbContext _db;
    private readonly IAgentAssignmentGuard _assignmentGuard;
    private readonly IAgentCapabilityCatalog _capabilities;

    public OperatingPlanValidationService(
        VirtualCompanyDbContext db,
        IAgentAssignmentGuard assignmentGuard,
        IAgentCapabilityCatalog capabilities)
    {
        _db = db;
        _assignmentGuard = assignmentGuard;
        _capabilities = capabilities;
    }

    public async Task<IReadOnlyList<OperatingValidationResultDto>> ValidateAsync(
        Guid companyId,
        Guid planId,
        CancellationToken ct)
    {
        var plan = await _db.OperatingPlans
            .Include(x => x.Cycle)
            .Include(x => x.Initiatives)
            .Include(x => x.Decisions)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == planId, ct)
            ?? throw new KeyNotFoundException("Operating plan not found.");
        var config = await _db.CompanyOperatingConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, ct)
            ?? new CompanyOperatingConfiguration(Guid.NewGuid(), companyId);
        var dependencies = await _db.OperatingPlanDependencies.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PlanId == planId)
            .ToListAsync(ct);
        var goals = await _db.CompanyGoals.AsNoTracking()
            .Where(x => x.CompanyId == companyId && plan.Initiatives.Select(i => i.GoalId).Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var existing = await _db.OperatingValidationResults
            .Where(x => x.CompanyId == companyId && x.PlanId == planId && x.Validator.StartsWith(ValidatorPrefix))
            .ToListAsync(ct);
        _db.OperatingValidationResults.RemoveRange(existing);

        var results = new List<OperatingValidationResult>();
        ValidatePlanLimits(plan, config, results);
        ValidateGoalRelevance(plan, goals, config, results);
        ValidateDependencies(plan, dependencies, config, results);
        ValidateDatesEvidenceAndBudgets(plan, config, results);
        await ValidateOwnersCapacityAndCapabilitiesAsync(plan, config, results, ct);
        await ValidateDuplicateWorkAsync(plan, goals, config, results, ct);
        ValidateActionsAndAutonomy(plan, config, results);

        _db.OperatingValidationResults.AddRange(results);
        await _db.SaveChangesAsync(ct);
        return results.Select(Map).ToArray();
    }

    private static void ValidatePlanLimits(OperatingPlan plan, CompanyOperatingConfiguration config,
        List<OperatingValidationResult> results)
    {
        Add(results, plan, null, "plan_limits",
            plan.Initiatives.Count == 0 || plan.Initiatives.Count > config.MaximumInitiativesPerCycle
                ? OperatingValidationOutcome.Denied
                : OperatingValidationOutcome.Allowed,
            "initiative_limit",
            plan.Initiatives.Count == 0
                ? "A plan must contain at least one initiative."
                : plan.Initiatives.Count > config.MaximumInitiativesPerCycle
                    ? "The plan exceeds the configured initiative limit."
                    : "The initiative count is within the configured limit.",
            false, config.Version,
            new() { ["count"] = JsonValue.Create(plan.Initiatives.Count), ["limit"] = JsonValue.Create(config.MaximumInitiativesPerCycle) });

        Add(results, plan, null, "task_budget",
            plan.Initiatives.Count <= config.MaximumTasksPerCycle ? OperatingValidationOutcome.Allowed : OperatingValidationOutcome.Denied,
            "task_limit", plan.Initiatives.Count <= config.MaximumTasksPerCycle
                ? "The proposed work fits within the configured task limit."
                : "The proposed work exceeds the configured task limit.",
            false, config.Version,
            new() { ["proposedTasks"] = JsonValue.Create(plan.Initiatives.Count), ["limit"] = JsonValue.Create(config.MaximumTasksPerCycle) });
    }

    private static void ValidateGoalRelevance(OperatingPlan plan, IReadOnlyDictionary<Guid, CompanyGoal> goals,
        CompanyOperatingConfiguration config, List<OperatingValidationResult> results)
    {
        foreach (var initiative in plan.Initiatives)
        {
            var valid = goals.TryGetValue(initiative.GoalId, out var goal) && goal.Status == CompanyGoalStatus.Active;
            Add(results, plan, DecisionId(plan, initiative.Id), "goal_relevance",
                valid ? OperatingValidationOutcome.Allowed : OperatingValidationOutcome.Denied,
                valid ? "goal_active" : "goal_not_active",
                valid ? $"The initiative advances the active goal '{goal!.Name}'." : "The initiative does not reference an active goal owned by this company.",
                false, config.Version,
                new() { ["initiativeId"] = JsonValue.Create(initiative.Id), ["goalId"] = JsonValue.Create(initiative.GoalId), ["goalVersion"] = JsonValue.Create(goal?.Version) });
        }
    }

    private static void ValidateDependencies(OperatingPlan plan, IReadOnlyCollection<OperatingPlanDependency> dependencies,
        CompanyOperatingConfiguration config, List<OperatingValidationResult> results)
    {
        var initiativeIds = plan.Initiatives.Select(x => x.Id).ToHashSet();
        var invalid = dependencies.Count(x => !initiativeIds.Contains(x.InitiativeId) || !initiativeIds.Contains(x.DependsOnInitiativeId) || x.InitiativeId == x.DependsOnInitiativeId);
        var cycle = invalid == 0 && HasDependencyCycle(initiativeIds, dependencies);
        var outcome = invalid > 0 || cycle ? OperatingValidationOutcome.Denied : OperatingValidationOutcome.Allowed;
        Add(results, plan, null, "dependencies", outcome,
            invalid > 0 ? "dependency_outside_plan" : cycle ? "dependency_cycle" : "dependencies_valid",
            invalid > 0 ? "One or more dependencies reference work outside this plan."
                : cycle ? "The initiative dependency graph contains a cycle."
                : "The initiative dependency graph is valid and acyclic.",
            false, config.Version,
            new() { ["dependencyCount"] = JsonValue.Create(dependencies.Count), ["invalidCount"] = JsonValue.Create(invalid), ["cycleDetected"] = JsonValue.Create(cycle) });
    }

    private static void ValidateDatesEvidenceAndBudgets(OperatingPlan plan, CompanyOperatingConfiguration config,
        List<OperatingValidationResult> results)
    {
        var missingEvidence = plan.Initiatives.Count(x => string.IsNullOrWhiteSpace(x.CompletionEvidence));
        var pastTargets = plan.Initiatives.Count(x => x.TargetUtc.HasValue && x.TargetUtc.Value.Date < DateTime.UtcNow.Date);
        Add(results, plan, null, "completion_evidence",
            missingEvidence == 0 ? OperatingValidationOutcome.Allowed : OperatingValidationOutcome.Denied,
            missingEvidence == 0 ? "evidence_defined" : "evidence_missing",
            missingEvidence == 0 ? "Every initiative defines observable completion evidence." : "Every initiative must define observable completion evidence.",
            false, config.Version, new() { ["missingEvidenceCount"] = JsonValue.Create(missingEvidence) });
        Add(results, plan, null, "target_dates",
            pastTargets == 0 ? OperatingValidationOutcome.Allowed : OperatingValidationOutcome.Denied,
            pastTargets == 0 ? "target_dates_valid" : "target_date_in_past",
            pastTargets == 0 ? "Initiative target dates are valid." : "One or more initiative target dates are already in the past.",
            false, config.Version, new() { ["pastTargetCount"] = JsonValue.Create(pastTargets) });

        var budget = plan.Initiatives.Sum(x => x.Budget ?? 0m);
        var withinBudget = !config.MaximumMonetaryBudgetPerCycle.HasValue || budget <= config.MaximumMonetaryBudgetPerCycle.Value;
        Add(results, plan, null, "monetary_budget", withinBudget ? OperatingValidationOutcome.Allowed : OperatingValidationOutcome.Denied,
            withinBudget ? "budget_within_limit" : "budget_exceeded",
            withinBudget ? "The planned monetary budget is within the configured limit." : "The planned monetary budget exceeds the configured limit.",
            false, config.Version,
            new() { ["plannedBudget"] = JsonValue.Create(budget), ["limit"] = JsonValue.Create(config.MaximumMonetaryBudgetPerCycle) });
    }

    private async Task ValidateOwnersCapacityAndCapabilitiesAsync(OperatingPlan plan, CompanyOperatingConfiguration config,
        List<OperatingValidationResult> results, CancellationToken ct)
    {
        var activeAssignments = await _db.WorkTasks.AsNoTracking()
            .Where(x => x.CompanyId == plan.CompanyId && x.AssignedAgentId != null &&
                x.Status != WorkTaskStatus.Completed && x.Status != WorkTaskStatus.Failed)
            .GroupBy(x => x.AssignedAgentId!.Value)
            .Select(x => new { AgentId = x.Key, Count = x.Count() })
            .ToDictionaryAsync(x => x.AgentId, x => x.Count, ct);
        var workloadLimit = Math.Max(3, config.MaximumTasksPerCycle);

        foreach (var initiative in plan.Initiatives)
        {
            var decisionId = DecisionId(plan, initiative.Id);
            if (!initiative.OwnerAgentId.HasValue)
            {
                Add(results, plan, decisionId, "owner_eligibility", OperatingValidationOutcome.ReviewRequired,
                    "owner_required", "Choose an eligible owner before this initiative can be committed.", true, config.Version,
                    new() { ["initiativeId"] = JsonValue.Create(initiative.Id) });
                continue;
            }

            try
            {
                await _assignmentGuard.EnsureAgentCanReceiveNewTasksAsync(plan.CompanyId, initiative.OwnerAgentId.Value,
                    "ownerAgentId", ct);
                Add(results, plan, decisionId, "owner_eligibility", OperatingValidationOutcome.Allowed,
                    "owner_assignable", "The proposed owner is active and can receive new work.", false, config.Version,
                    new() { ["agentId"] = JsonValue.Create(initiative.OwnerAgentId.Value) });
            }
            catch (AgentAssignmentValidationException exception)
            {
                Add(results, plan, decisionId, "owner_eligibility", OperatingValidationOutcome.Denied,
                    "owner_not_assignable", string.Join(" ", exception.Errors.SelectMany(x => x.Value)), false, config.Version,
                    new() { ["agentId"] = JsonValue.Create(initiative.OwnerAgentId.Value) });
                continue;
            }
            catch (KeyNotFoundException)
            {
                Add(results, plan, decisionId, "owner_eligibility", OperatingValidationOutcome.Denied,
                    "owner_not_found", "The proposed owner does not belong to this company.", false, config.Version,
                    new() { ["agentId"] = JsonValue.Create(initiative.OwnerAgentId.Value) });
                continue;
            }

            var assigned = activeAssignments.GetValueOrDefault(initiative.OwnerAgentId.Value);
            Add(results, plan, decisionId, "owner_capacity",
                assigned >= workloadLimit ? OperatingValidationOutcome.ReviewRequired : OperatingValidationOutcome.Allowed,
                assigned >= workloadLimit ? "owner_capacity_temporarily_exceeded" : "owner_capacity_available",
                assigned >= workloadLimit ? "The proposed owner is at the configured workload threshold; review or reassign this initiative."
                    : "The proposed owner has capacity within the configured threshold.",
                assigned >= workloadLimit, config.Version,
                new() { ["activeAssignments"] = JsonValue.Create(assigned), ["threshold"] = JsonValue.Create(workloadLimit) });

            var catalog = await _capabilities.GetEffectiveCatalogAsync(plan.CompanyId, initiative.OwnerAgentId.Value, ct);
            var planning = catalog.Capabilities.FirstOrDefault(x => x.Id == AgentCapabilityIds.Planning) ??
                catalog.Capabilities.FirstOrDefault(x => x.Id == AgentCapabilityIds.WorkPrioritization);
            var capabilityOutcome = planning is null || planning.State == AgentCapabilityStates.NotImplemented || planning.State == AgentCapabilityStates.PermissionDenied
                ? OperatingValidationOutcome.Denied
                : planning.State == AgentCapabilityStates.Available ? OperatingValidationOutcome.Allowed : OperatingValidationOutcome.ReviewRequired;
            Add(results, plan, decisionId, "owner_capability", capabilityOutcome,
                planning?.ReasonCode ?? "planning_capability_missing",
                planning?.Explanation ?? "The proposed owner has no implemented planning capability.",
                capabilityOutcome == OperatingValidationOutcome.ReviewRequired, config.Version,
                new() { ["capabilityId"] = JsonValue.Create(planning?.Id), ["capabilityState"] = JsonValue.Create(planning?.State) });
        }
    }

    private async Task ValidateDuplicateWorkAsync(OperatingPlan plan, IReadOnlyDictionary<Guid, CompanyGoal> goals,
        CompanyOperatingConfiguration config, List<OperatingValidationResult> results, CancellationToken ct)
    {
        var candidateIds = plan.Initiatives.Select(x => x.Id).ToHashSet();
        var comparable = await _db.OperatingInitiatives.AsNoTracking()
            .Where(x => x.CompanyId == plan.CompanyId && !candidateIds.Contains(x.Id) &&
                (x.Status == OperatingInitiativeStatus.Proposed || x.Status == OperatingInitiativeStatus.Approved ||
                 x.Status == OperatingInitiativeStatus.Active || x.Status == OperatingInitiativeStatus.Completed))
            .OrderByDescending(x => x.UpdatedUtc).Take(500).ToListAsync(ct);
        var recentTasks = await _db.WorkTasks.AsNoTracking()
            .Where(x => x.CompanyId == plan.CompanyId && x.Type == "operating_initiative" &&
                x.UpdatedUtc >= DateTime.UtcNow.AddDays(-90) && x.Status != WorkTaskStatus.Failed)
            .OrderByDescending(x => x.UpdatedUtc).Take(500).ToListAsync(ct);

        foreach (var initiative in plan.Initiatives)
        {
            var goalVersion = goals.GetValueOrDefault(initiative.GoalId)?.Version ?? 0;
            var identity = BusinessIdentity(plan.CompanyId, initiative.GoalId, goalVersion, initiative.Title,
                initiative.DesiredOutcome, initiative.TargetUtc);
            var duplicateInitiatives = comparable.Count(x => BusinessIdentity(plan.CompanyId, x.GoalId,
                goals.GetValueOrDefault(x.GoalId)?.Version ?? goalVersion, x.Title, x.DesiredOutcome, x.TargetUtc) == identity);
            var normalizedTitle = Normalize(initiative.Title);
            var duplicateTasks = recentTasks.Count(x => Normalize(x.Title) == normalizedTitle && Normalize(x.Description) == Normalize(initiative.DesiredOutcome));
            var duplicate = duplicateInitiatives + duplicateTasks;
            Add(results, plan, DecisionId(plan, initiative.Id), "duplicate_work",
                duplicate == 0 ? OperatingValidationOutcome.Allowed : OperatingValidationOutcome.ReviewRequired,
                duplicate == 0 ? "work_identity_unique" : "possible_duplicate_work",
                duplicate == 0 ? "No active or recently completed work has the same business identity."
                    : "Similar active or recently completed work already exists; review before creating another task.",
                duplicate > 0, config.Version,
                new() { ["businessIdentity"] = JsonValue.Create(identity), ["matchingRecords"] = JsonValue.Create(duplicate) });
        }
    }

    private static void ValidateActionsAndAutonomy(OperatingPlan plan, CompanyOperatingConfiguration config,
        List<OperatingValidationResult> results)
    {
        foreach (var decision in plan.Decisions)
        {
            var known = decision.ActionType is "initiative" or "operator_notification";
            if (!known)
            {
                Add(results, plan, decision.Id, "action_classification", OperatingValidationOutcome.Denied,
                    "unknown_action_type", "The proposed action type is not registered and is denied safely.", false,
                    config.Version, new() { ["actionType"] = JsonValue.Create(decision.ActionType) });
                continue;
            }

            var outcome = decision.ActionClass switch
            {
                OperatingActionClass.Recommend => OperatingValidationOutcome.Allowed,
                OperatingActionClass.Read when config.AutonomyLevel >= CompanyAutonomyLevel.OperateInternally && !decision.ApprovalRequired => OperatingValidationOutcome.Allowed,
                OperatingActionClass.Read => OperatingValidationOutcome.ReviewRequired,
                OperatingActionClass.InternalMutation when config.AutonomyLevel >= CompanyAutonomyLevel.OperateInternally &&
                    !decision.ApprovalRequired && string.Equals(decision.RiskLevel, "low", StringComparison.OrdinalIgnoreCase) => OperatingValidationOutcome.Allowed,
                OperatingActionClass.InternalMutation => OperatingValidationOutcome.ReviewRequired,
                OperatingActionClass.ExternalExecute when decision.ActionType == "operator_notification" && decision.ApprovalRequired => OperatingValidationOutcome.ReviewRequired,
                _ => OperatingValidationOutcome.Denied
            };
            var reason = outcome switch
            {
                OperatingValidationOutcome.Allowed => "action_within_autonomy",
                OperatingValidationOutcome.ReviewRequired => decision.ApprovalRequired ? "approval_required" : "autonomy_review_required",
                _ => "action_not_authorized"
            };
            Add(results, plan, decision.Id, "action_classification", outcome, reason,
                outcome == OperatingValidationOutcome.Allowed ? "The action is within the current operating boundary."
                    : outcome == OperatingValidationOutcome.ReviewRequired ? "The action requires an authorized review before it can proceed."
                    : "The action is not authorized by the current company operating boundary.",
                outcome == OperatingValidationOutcome.ReviewRequired, config.Version,
                new() { ["actionClass"] = JsonValue.Create(decision.ActionClass.ToStorageValue()), ["actionType"] = JsonValue.Create(decision.ActionType), ["autonomyLevel"] = JsonValue.Create(config.AutonomyLevel.ToStorageValue()), ["riskLevel"] = JsonValue.Create(decision.RiskLevel) });
        }
    }

    private static bool HasDependencyCycle(IReadOnlyCollection<Guid> initiativeIds,
        IReadOnlyCollection<OperatingPlanDependency> dependencies)
    {
        var edges = dependencies.GroupBy(x => x.InitiativeId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.DependsOnInitiativeId).ToArray());
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();
        bool Visit(Guid id)
        {
            if (!visiting.Add(id)) return true;
            if (visited.Contains(id)) { visiting.Remove(id); return false; }
            foreach (var next in edges.GetValueOrDefault(id) ?? [])
                if (Visit(next)) return true;
            visiting.Remove(id); visited.Add(id); return false;
        }
        return initiativeIds.Any(Visit);
    }

    private static Guid? DecisionId(OperatingPlan plan, Guid initiativeId) =>
        plan.Decisions.FirstOrDefault(x => x.InitiativeId == initiativeId)?.Id;

    private static string BusinessIdentity(Guid companyId, Guid goalId, int goalVersion, string title,
        string outcome, DateTime? targetUtc)
    {
        var input = $"{companyId:N}|{goalId:N}|{goalVersion}|{Normalize(title)}|{Normalize(outcome)}|{targetUtc:yyyy-MM-dd}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static string Normalize(string? value) => string.Join(' ', (value ?? string.Empty).Trim()
        .ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void Add(List<OperatingValidationResult> results, OperatingPlan plan, Guid? decisionId,
        string validator, OperatingValidationOutcome outcome, string code, string explanation,
        bool approvalRequired, int configurationVersion, Dictionary<string, JsonNode?> evidence) =>
        results.Add(new OperatingValidationResult(Guid.NewGuid(), plan.CompanyId, plan.Id, decisionId,
            ValidatorPrefix + validator, ValidatorVersion, outcome, code, explanation, approvalRequired,
            configurationVersion, evidence));

    internal static OperatingValidationResultDto Map(OperatingValidationResult x) =>
        new(x.Id, x.DecisionId, x.Validator, x.ValidatorVersion, x.Outcome.ToStorageValue(), x.ReasonCode,
            x.Explanation, x.ApprovalRequired, x.Evidence, x.EvaluatedUtc);
}
