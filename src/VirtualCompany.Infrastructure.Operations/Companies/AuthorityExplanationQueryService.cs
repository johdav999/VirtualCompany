using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AuthorityExplanationQueryService(VirtualCompanyDbContext db, CompanyWorkVisibility visibility,
    IAgentWorkQueryService work, ICompanyOperatingConfigurationService configurations,
    ICompanyOperatingAutonomyPolicy operatingPolicy, IAgentToolPolicyPreviewService tools,
    IFinanceAutonomyGrantService grants, IFinanceAutonomyPolicyEvaluator financePolicy,
    TimeProvider clock, ILogger<AuthorityExplanationQueryService> logger, IAgentExecutionControlGate controls, ITaskTypePolicyEvaluator taskPolicies) : IAuthorityExplanationQueryService
{
    public async Task<AuthorityExplanationDto> GetAsync(Guid companyId, Guid? agentId, string? workKind, Guid? workId, CancellationToken token)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty || workId == Guid.Empty ||
            (workKind is null) != (workId is null)) throw new ArgumentException("Choose a company and a complete work identity.");
        var scope = await visibility.ResolveAsync(companyId, token);
        var agents = await scope.Agents(db.Agents.IgnoreQueryFilters().AsNoTracking()).OrderBy(x => x.DisplayName).ToListAsync(token);
        var choices = agents.Select(x => new AuthorityAgentChoiceDto(x.Id, x.DisplayName)).ToArray();
        var taskType = "Agent capability checks";
        string? workDependency = null;
        Guid? planId = null;
        if (workId.HasValue)
        {
            // Reuse the Work boundary, including transitive contribution visibility, before reading identities.
            var item = await work.GetAsync(companyId, workKind!, workId.Value, token);
            workDependency = item.Dependency;
            agents = agents.Where(x => item.Agents.Any(a => a.Id == x.Id)).ToList();
            taskType = workKind == "task" ? await db.WorkTasks.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Id == workId).Select(x => x.Type).SingleAsync(token) : item.Title;
            planId = await db.OperatingInitiatives.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && (workKind == "initiative" ? x.Id == workId : workKind == "task" && x.TaskId == workId))
                .Select(x => (Guid?)x.PlanId).FirstOrDefaultAsync(token);
        }
        else if (!agentId.HasValue) agents = agents.Take(1).ToList();
        if (agentId.HasValue)
        {
            agents = agents.Where(x => x.Id == agentId).ToList();
            if (agents.Count == 0) throw new KeyNotFoundException("Agent is unavailable in the current company and access scope.");
        }
        var config = await configurations.GetAsync(companyId, token);
        var diagnostics = new List<string>();
        var policy = new AuthorityCheckDto("Task-type policy", "not_evaluated", "task_policy_not_bound",
            "No separately approved task-type policy or company operating plan is bound to this view. Tool checks below describe capabilities without a proposed business payload. The owning workflow must check the actual action before execution.", false);
        if (planId.HasValue)
        {
            try
            {
                var decision = await operatingPolicy.EvaluateAsync(companyId, planId.Value, CompanyOperatingAutonomyPhase.Dispatch, token);
                policy = new("Company operating dispatch", decision.Allowed ? "available" : decision.ReviewRequired ? "approval_required" : "permission_denied",
                    decision.ReasonCode, decision.Explanation, decision.ReviewRequired);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or UnauthorizedAccessException))
            { policy = Failed("Company operating dispatch"); Record(ex, diagnostics); }
        }
        var projected = new List<AuthorityAgentDto>();
        foreach (var agent in agents)
        {
            try
            {
                var preview = await tools.PreviewAsync(companyId, agent.Id, token);
                var actions = preview.Authority.Tools.Select(x => new AuthorityActionDto(x.ToolName, x.ActionType, x.Scope,
                    x.State, x.IntegrationState, x.ActorPermission, preview.Checks[x.ToolName]))
                    .Concat(preview.Authority.ExcludedConfiguredTools.Select(name => new AuthorityActionDto(name, "not_offered", agent.Department,
                        "not_implemented", "not_available", "", new("Supported capability catalogue", "not_implemented", "configured_tool_outside_catalogue",
                            "This configured tool is outside the supported capability catalogue for this agent and is not offered for execution.", false))))
                    .ToArray();
                for(var i=0;i<actions.Length;i++)
                {
                    if(actions[i].Check.State is not ("available" or "approval_required")) continue;
                    if(await controls.IsPausedAsync(companyId,agent.Id,token))
                        actions[i]=actions[i] with {Check=new("Execution controls","permission_denied","execution_paused","New controlled steps are paused for this agent or company. Already admitted steps may complete or need reconciliation.",false)};
                    else if(workKind=="task" && await taskPolicies.CheckAsync(companyId,agent.Id,actions[i].ToolName,workId,false,token) is { } check)
                        actions[i]=actions[i] with {Check=check};
                }
                var bounded = new List<AuthorityGrantDto>();
                if (agent.Department.Equals("Finance", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var grant in await grants.ListAsync(companyId, agent.Id, token))
                    {
                        var version = grant.Versions.FirstOrDefault(x => x.Id == grant.ActiveVersionId)
                            ?? grant.Versions.OrderByDescending(x => x.VersionNumber).FirstOrDefault();
                        if (version is null) continue;
                        var toolName = version.AllowedTools.FirstOrDefault() ?? "";
                        var action = preview.Authority.Tools.FirstOrDefault(x => x.ToolName.Equals(toolName, StringComparison.OrdinalIgnoreCase))?.ActionType
                            ?? version.AllowedActionClasses.FirstOrDefault() ?? "read";
                        // Actual expiry, control, policy hash, authority and evidence checks belong to the Finance evaluator.
                        var check = await financePolicy.EvaluateAsync(new(companyId, agent.Id, grant.CapabilityId,
                            FinanceAutonomyTriggers.ManualReview, action, toolName, EvidenceObservedUtc: null), token);
                        bounded.Add(new(grant.CapabilityId, version.Level, version.VersionNumber, version.EffectiveFromUtc,
                            version.ExpiresUtc, [Limit("Records per run", version.MaximumRecordsPerRun),
                                Limit("Actions per run", version.MaximumActionsPerRun), Limit("Amount per run", version.MaximumAmountPerRun),
                                new("Local time window", $"{version.WindowStartLocal}–{version.WindowEndLocal} ({version.Timezone})"),
                                Limit("Evidence freshness (minutes)", version.EvidenceFreshnessMinutes),
                                new("Allowed triggers", string.Join(", ", version.AllowedTriggers)),
                                Limit("Runs per window", version.MaximumRunsPerWindow)],
                            new("Proactive Finance: manual review, one action, no business evidence supplied",
                                check.IsAllowed ? check.RequiresApproval || check.RequiresConfirmation ? "approval_required" : "available" : "permission_denied",
                                check.ReasonCode, check.Explanation, check.RequiresApproval || check.RequiresConfirmation)));
                    }
                }
                projected.Add(new(agent.Id, agent.DisplayName, preview.Authority.AutonomyLevel, preview.Authority.AuthorityVersion,
                    preview.Authority.AuthorityHash, actions, bounded));
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or UnauthorizedAccessException))
            {
                Record(ex, diagnostics);
                projected.Add(new(agent.Id, agent.DisplayName, "unknown", "unavailable", "unavailable",
                    [new("Capability evaluation", "unknown", "", "unknown", "unknown", "", Failed("Authority evaluation"))], []));
            }
        }
        var limits = new List<AuthorityLimitDto> {
            Limit("Tasks per cycle", config.MaximumTasksPerCycle), Limit("Tasks per day", config.MaximumTasksPerDay),
            Limit("Model calls per cycle", config.MaximumModelCallsPerCycle), Limit("Tool calls per cycle", config.MaximumToolCallsPerCycle),
            Limit("Model calls per day", config.MaximumModelCallsPerDay), Limit("Tool calls per day", config.MaximumToolCallsPerDay),
            Limit("Monetary budget per cycle", config.MaximumMonetaryBudgetPerCycle), Limit("Monetary budget per day", config.MaximumMonetaryBudgetPerDay),
            Limit("Runtime (seconds)", config.MaximumRuntimeSeconds), Limit("Cycles per day", config.MaximumCyclesPerDay),
            Limit("Minimum cycle interval (minutes)", config.MinimumCycleIntervalMinutes), new("Timezone", config.Timezone),
            new("Company operation", config.EmergencyStopped ? "Emergency stopped" : config.IsPaused ? "Paused" : "Running"),
            new("Mandatory review", "Company operating external actions always require human review, including Act within limits."),
            new("Expiry", "Company configuration has no grant expiry. Separate grants retain their own expiry.") };
        if (config.EmergencyStopped || config.IsPaused) limits.Add(new("Restriction reason", config.EmergencyStopReason ?? config.PauseReason ?? "Company operations are stopped."));
        if (workDependency is not null) limits.Add(new("Recorded work dependency", workDependency));
        var currentConfig = await configurations.GetAsync(companyId, token);
        if (currentConfig.Version != config.Version)
        {
            diagnostics.Add("Company settings changed during this read. Refresh to obtain a consistent observation.");
            policy = new("Current observation", "evaluation_failed", "authority_snapshot_changed", "The company configuration changed while authority was being checked. Refresh before relying on these receipts.", false);
        }
        var result = new AuthorityExplanationDto(companyId, workKind, workId, taskType, config.AutonomyLevel, config.Version,
            limits, policy, projected, workId.HasValue ? [] : choices, diagnostics, "authority-explanation-v1", "", clock.GetUtcNow().UtcDateTime);
        // No cache: every read evaluates current owners. Fingerprint all displayed inputs/decisions,
        // including company version, grant limits/expiry and actor outcomes; time is not part of identity.
        var canonical = JsonSerializer.Serialize(result with { EvaluatedUtc = default });
        return result with { ProjectionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))) };
    }
    private static AuthorityLimitDto Limit(string label, object? value) => new(label, value is null ? "Not configured" : Convert.ToString(value, CultureInfo.InvariantCulture)!);
    private static AuthorityCheckDto Failed(string source) => new(source, "evaluation_failed", "authority_evaluation_failed", "Current authority could not be evaluated. Refresh to retry; no permission is established by this result.", false);
    private void Record(Exception ex, List<string> diagnostics)
    {
        logger.LogWarning("Authority explanation evaluation failed ({FailureType})", ex.GetType().Name);
        diagnostics.Add("A policy evaluation failed. Refresh or ask the accountable owner to check configuration.");
    }
}
