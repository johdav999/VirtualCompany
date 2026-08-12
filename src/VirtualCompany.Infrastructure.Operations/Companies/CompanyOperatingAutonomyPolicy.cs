using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyOperatingAutonomyPolicy(VirtualCompanyDbContext db) : ICompanyOperatingAutonomyPolicy
{
    public async Task<CompanyOperatingAutonomyDecision> EvaluateAsync(Guid companyId, Guid planId,
        CompanyOperatingAutonomyPhase phase, CancellationToken cancellationToken)
    {
        var config = await db.CompanyOperatingConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        var plan = await db.OperatingPlans.AsNoTracking()
            .Include(x => x.Cycle).Include(x => x.Initiatives).Include(x => x.Decisions)
            .Include(x => x.ValidationResults)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == planId, cancellationToken);

        if (config is null || plan is null)
            return Deny("operating_state_missing", "The current operating configuration or plan is unavailable.",
                config?.AutonomyLevel ?? CompanyAutonomyLevel.Recommend, config?.Version ?? 0);
        if (config.IsPaused || config.EmergencyStopped)
            return Deny(config.EmergencyStopped ? "emergency_stop_active" : "operation_paused",
                config.EmergencyStopped ? "Company operation is emergency stopped." : "Company operation is paused.",
                config.AutonomyLevel, config.Version);
        if (plan.Cycle.ConfigurationVersion != config.Version)
            return Review("configuration_changed", "Operating settings changed after the plan was produced. Request a new review.", config);

        var requiredLevel = phase == CompanyOperatingAutonomyPhase.AutomaticCommit
            ? CompanyAutonomyLevel.Organize : CompanyAutonomyLevel.OperateInternally;
        if (config.AutonomyLevel < requiredLevel)
            return Review(phase == CompanyOperatingAutonomyPhase.AutomaticCommit
                    ? "organization_not_enabled" : "internal_operation_not_enabled",
                phase == CompanyOperatingAutonomyPhase.AutomaticCommit
                    ? "The company is configured to recommend work for review rather than organize it automatically."
                    : "The company is not configured to run internal work automatically.", config);

        var currentValidation = plan.ValidationResults.Where(x => x.ConfigurationVersion == config.Version).ToArray();
        if (currentValidation.Length == 0)
            return Review("validation_missing", "The plan has no validation for the current operating settings.", config);
        var denied = currentValidation.FirstOrDefault(x => x.Outcome == OperatingValidationOutcome.Denied);
        if (denied is not null) return Deny(denied.ReasonCode, denied.Explanation, config.AutonomyLevel, config.Version);
        var review = currentValidation.FirstOrDefault(x => x.Outcome == OperatingValidationOutcome.ReviewRequired || x.ApprovalRequired);
        if (review is not null) return Review(review.ReasonCode, review.Explanation, config);

        var guardedDecision = plan.Decisions.FirstOrDefault(x => x.ApprovalRequired ||
            x.ActionClass == OperatingActionClass.ExternalExecute ||
            (x.ActionClass == OperatingActionClass.InternalMutation &&
             !string.Equals(x.RiskLevel, "low", StringComparison.OrdinalIgnoreCase)));
        if (guardedDecision is not null)
            return Review(guardedDecision.ApprovalRequired ? "approval_required" : "action_outside_autonomy",
                "This plan contains an action that requires an authorized review or approval.", config);

        var ownerIds = plan.Initiatives.Where(x => x.OwnerAgentId.HasValue)
            .Select(x => x.OwnerAgentId!.Value).Distinct().ToArray();
        if (ownerIds.Length == 0 || plan.Initiatives.Any(x => !x.OwnerAgentId.HasValue))
            return Review("owner_missing", "Every initiative requires a current eligible owner before automatic operation.", config);
        var agents = await db.Agents.AsNoTracking().Where(x => x.CompanyId == companyId && ownerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Status, x.AutonomyLevel }).ToListAsync(cancellationToken);
        var requiredAgentLevel = phase == CompanyOperatingAutonomyPhase.Dispatch
            ? AgentAutonomyLevel.Level2 : AgentAutonomyLevel.Level1;
        if (agents.Count != ownerIds.Length || agents.Any(x => x.Status != AgentStatus.Active || x.AutonomyLevel < requiredAgentLevel))
            return Review("agent_autonomy_insufficient",
                "An assigned agent is inactive or has less authority than this automatic operation requires.", config);

        var day = DateTime.UtcNow.Date;
        var usage = await db.OperatingCycles.AsNoTracking().Where(x => x.CompanyId == companyId && x.RequestedUtc >= day)
            .GroupBy(_ => 1).Select(x => new
            {
                Tasks = x.Sum(y => y.TasksCreated), Model = x.Sum(y => y.ModelCallsUsed),
                Tools = x.Sum(y => y.ToolCallsUsed), Money = x.Sum(y => y.MonetaryBudgetUsed)
            }).SingleOrDefaultAsync(cancellationToken);
        var proposedTasks = phase == CompanyOperatingAutonomyPhase.AutomaticCommit
            ? plan.Initiatives.Count(x => !x.TaskId.HasValue) : 0;
        var plannedMoney = plan.Initiatives.Sum(x => x.Budget ?? 0m);
        if ((usage?.Tasks ?? 0) + proposedTasks > config.MaximumTasksPerDay ||
            (usage?.Model ?? 0) > config.MaximumModelCallsPerDay ||
            (usage?.Tools ?? 0) >= config.MaximumToolCallsPerDay ||
            (config.MaximumMonetaryBudgetPerDay.HasValue && (usage?.Money ?? 0m) + plannedMoney > config.MaximumMonetaryBudgetPerDay.Value))
            return Review("daily_budget_exhausted", "The configured daily operating budget has been reached.", config);

        return new(true, false, "within_autonomy",
            phase == CompanyOperatingAutonomyPhase.AutomaticCommit
                ? "The validated low-risk internal plan may be organized automatically."
                : "The validated low-risk internal work may be dispatched automatically.",
            config.AutonomyLevel, config.Version, Evidence(config, plan.Initiatives.Count, ownerIds.Length));
    }

    private static CompanyOperatingAutonomyDecision Review(string code, string explanation, CompanyOperatingConfiguration config) =>
        new(false, true, code, explanation, config.AutonomyLevel, config.Version, Evidence(config, 0, 0));

    private static CompanyOperatingAutonomyDecision Deny(string code, string explanation, CompanyAutonomyLevel level, int version) =>
        new(false, false, code, explanation, level, version,
            new Dictionary<string, string?> { ["companyAutonomy"] = level.ToStorageValue(), ["configurationVersion"] = version.ToString() });

    private static IReadOnlyDictionary<string, string?> Evidence(CompanyOperatingConfiguration config, int initiatives, int owners) =>
        new Dictionary<string, string?>
        {
            ["companyAutonomy"] = config.AutonomyLevel.ToStorageValue(), ["configurationVersion"] = config.Version.ToString(),
            ["initiativeCount"] = initiatives.ToString(), ["eligibleOwnerCount"] = owners.ToString(),
            ["paused"] = config.IsPaused.ToString(), ["emergencyStopped"] = config.EmergencyStopped.ToString()
        };
}
