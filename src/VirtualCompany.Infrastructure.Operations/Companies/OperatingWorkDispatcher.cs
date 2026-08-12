using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class OperatingWorkDispatcher : IOperatingWorkDispatcher, IOperatingDispatchQueryService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private readonly string _leaseOwner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyExecutionScopeFactory _executionScopes;
    private readonly ICompanyMembershipContextResolver _memberships;
    private readonly IAgentAssignmentGuard _assignmentGuard;
    private readonly ICompanyOperatingAutonomyPolicy _autonomyPolicy;
    private readonly ISingleAgentOrchestrationService _singleAgent;
    private readonly IMultiAgentCoordinator _multiAgent;

    public OperatingWorkDispatcher(VirtualCompanyDbContext db, ICompanyExecutionScopeFactory executionScopes,
        ICompanyMembershipContextResolver memberships, IAgentAssignmentGuard assignmentGuard,
        ICompanyOperatingAutonomyPolicy autonomyPolicy,
        ISingleAgentOrchestrationService singleAgent, IMultiAgentCoordinator multiAgent)
    {
        _db = db; _executionScopes = executionScopes; _memberships = memberships;
        _assignmentGuard = assignmentGuard; _autonomyPolicy = autonomyPolicy; _singleAgent = singleAgent; _multiAgent = multiAgent;
    }

    public async Task<OperatingDispatchRunResult> RunOnceAsync(int batchSize, CancellationToken ct)
    {
        batchSize = Math.Clamp(batchSize, 1, 25);
        var now = DateTime.UtcNow;
        var candidateIds = await _db.OperatingDispatches.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == OperatingDispatchStatus.Pending || x.Status == OperatingDispatchStatus.RetryScheduled ||
                         ((x.Status == OperatingDispatchStatus.Claimed || x.Status == OperatingDispatchStatus.Running) && x.LeaseExpiresUtc <= now)) &&
                        (x.NextAttemptUtc == null || x.NextAttemptUtc <= now))
            .OrderBy(x => x.NextAttemptUtc).ThenBy(x => x.CreatedUtc).Select(x => x.Id).Take(batchSize * 2).ToListAsync(ct);

        var claimed = new List<Guid>();
        foreach (var id in candidateIds)
        {
            var dispatch = await _db.OperatingDispatches.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (dispatch is null || !dispatch.TryClaim(_leaseOwner, now, LeaseDuration)) continue;
            try
            {
                await _db.SaveChangesAsync(ct);
                claimed.Add(id);
                if (claimed.Count == batchSize) break;
            }
            catch (DbUpdateConcurrencyException)
            {
                _db.Entry(dispatch).State = EntityState.Detached;
            }
        }

        var completed = 0; var awaiting = 0; var retried = 0; var blocked = 0; var dead = 0;
        foreach (var id in claimed)
        {
            var outcome = await ExecuteClaimedAsync(id, ct);
            if (outcome == OperatingDispatchStatus.Completed) completed++;
            else if (outcome == OperatingDispatchStatus.AwaitingApproval) awaiting++;
            else if (outcome == OperatingDispatchStatus.RetryScheduled) retried++;
            else if (outcome == OperatingDispatchStatus.DeadLettered) dead++;
            else blocked++;
        }
        return new OperatingDispatchRunResult(claimed.Count, completed, awaiting, retried, blocked, dead);
    }

    private async Task<OperatingDispatchStatus> ExecuteClaimedAsync(Guid dispatchId, CancellationToken ct)
    {
        var dispatch = await _db.OperatingDispatches.IgnoreQueryFilters()
            .Include(x => x.Initiative).ThenInclude(x => x.Plan).ThenInclude(x => x.Cycle)
            .Include(x => x.Task)
            .SingleAsync(x => x.Id == dispatchId, ct);
        using var tenantScope = _executionScopes.BeginScope(dispatch.CompanyId);
        var now = DateTime.UtcNow;
        try
        {
            var config = await _db.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == dispatch.CompanyId, ct);
            if (config is null || config.IsPaused || config.EmergencyStopped || config.AutonomyLevel < CompanyAutonomyLevel.OperateInternally)
            {
                dispatch.Start(_leaseOwner, now);
                dispatch.Block("autonomy_not_authorized", "Automatic execution is not enabled for this company.", now);
                await _db.SaveChangesAsync(ct);
                return dispatch.Status;
            }
            var autonomy = await _autonomyPolicy.EvaluateAsync(dispatch.CompanyId, dispatch.Initiative.PlanId,
                CompanyOperatingAutonomyPhase.Dispatch, ct);
            if (!autonomy.Allowed)
            {
                dispatch.Start(_leaseOwner, now);
                if (autonomy.ReviewRequired) dispatch.AwaitApproval(autonomy.Explanation, now);
                else dispatch.Block(autonomy.ReasonCode, autonomy.Explanation, now);
                await _db.SaveChangesAsync(ct);
                return dispatch.Status;
            }
            if (!dispatch.Initiative.OwnerAgentId.HasValue)
                throw new AgentAssignmentValidationException(new Dictionary<string, string[]> { ["ownerAgentId"] = ["An initiative owner is required."] });
            await _assignmentGuard.EnsureAgentCanReceiveNewTasksAsync(dispatch.CompanyId,
                dispatch.Initiative.OwnerAgentId.Value, "ownerAgentId", ct);
            var invalid = await _db.OperatingValidationResults.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == dispatch.CompanyId && x.PlanId == dispatch.Initiative.PlanId &&
                    x.ConfigurationVersion == config.Version && x.Outcome == OperatingValidationOutcome.Denied, ct);
            if (invalid)
            {
                dispatch.Start(_leaseOwner, now);
                dispatch.Block("validation_denied", "Current operating-plan validation does not allow this work to run.", now);
                await _db.SaveChangesAsync(ct);
                return dispatch.Status;
            }

            dispatch.Start(_leaseOwner, now);
            await _db.SaveChangesAsync(ct);
            var collaborators = await _db.OperatingInitiativeCollaborators.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == dispatch.CompanyId && x.InitiativeId == dispatch.InitiativeId)
                .OrderBy(x => x.Sequence).ToListAsync(ct);
            if (collaborators.Count == 0)
            {
                var result = await _singleAgent.ExecuteAsync(new SingleAgentOrchestrationRequest(
                    dispatch.CompanyId, dispatch.TaskId, dispatch.Initiative.OwnerAgentId,
                    dispatch.Initiative.Plan.Cycle.CoordinatorAgentId, "agent", dispatch.CorrelationId,
                    OrchestrationIntentValues.ExecuteTask), ct);
                if (result.Status == OrchestrationStatusValues.AwaitingApproval)
                    dispatch.AwaitApproval(result.FailureReason ?? "Execution is waiting for an approval.", DateTime.UtcNow);
                else if (result.Status == OrchestrationStatusValues.Completed)
                    dispatch.Complete(result.OrchestrationId, null, DateTime.UtcNow);
                else
                    dispatch.Retry("single_agent_failed", result.FailureReason ?? "The assigned agent could not complete the work.",
                        DateTime.UtcNow.AddMinutes(ComputeBackoff(dispatch.AttemptCount)), DateTime.UtcNow);
            }
            else
            {
                if (collaborators.Count > config.MaximumCollaborators)
                    throw new MultiAgentCollaborationValidationException(new Dictionary<string, string[]> { ["workers"] = ["The approved collaboration exceeds the company limit."] });
                foreach (var collaborator in collaborators)
                    await _assignmentGuard.EnsureAgentCanReceiveNewTasksAsync(dispatch.CompanyId, collaborator.AgentId, "collaboratorAgentId", ct);
                var workers = collaborators.Select(x => new WorkerSubtaskRequest(x.AgentId, x.Objective,
                    $"Act as {x.Role.ToStorageValue().Replace('_', ' ')}. Produce: {x.ExpectedArtifact}")).ToArray();
                var result = await _multiAgent.ExecuteAsync(new StartMultiAgentCollaborationCommand(
                    dispatch.CompanyId, dispatch.Initiative.DesiredOutcome, dispatch.Initiative.OwnerAgentId.Value,
                    workers, dispatch.Initiative.Plan.Cycle.CoordinatorAgentId, "agent", null,
                    dispatch.CorrelationId,
                    new CollaborationLimitRequest(config.MaximumCollaborators, 1, config.MaximumRuntimeSeconds,
                        Math.Max(config.MaximumCollaborators * 2, config.MaximumCollaborators)),
                    new Dictionary<string, JsonNode?>
                    {
                        ["operatingPlanId"] = JsonValue.Create(dispatch.Initiative.PlanId),
                        ["operatingInitiativeId"] = JsonValue.Create(dispatch.InitiativeId),
                        ["sourceTaskId"] = JsonValue.Create(dispatch.TaskId)
                    }), ct);
                if (result.Status == MultiAgentCollaborationStatusValues.Completed)
                    dispatch.Complete(null, result.PlanId, DateTime.UtcNow);
                else if (result.IsRetryable)
                    dispatch.Retry("collaboration_failed", result.TerminationReason,
                        DateTime.UtcNow.AddMinutes(ComputeBackoff(dispatch.AttemptCount)), DateTime.UtcNow);
                else
                    dispatch.Block("collaboration_blocked", result.TerminationReason, DateTime.UtcNow);
            }
        }
        catch (AgentAssignmentValidationException ex)
        {
            EnsureRunning(dispatch, now);
            dispatch.Block("assignment_denied", string.Join(" ", ex.Errors.SelectMany(x => x.Value)), DateTime.UtcNow);
        }
        catch (MultiAgentCollaborationValidationException ex)
        {
            EnsureRunning(dispatch, now);
            dispatch.Block("collaboration_plan_invalid", string.Join(" ", ex.Errors.SelectMany(x => x.Value)), DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            EnsureRunning(dispatch, now);
            dispatch.Retry("dispatch_failed", Safe(ex.Message), DateTime.UtcNow.AddMinutes(ComputeBackoff(dispatch.AttemptCount)), DateTime.UtcNow);
        }
        await _db.SaveChangesAsync(CancellationToken.None);
        return dispatch.Status;
    }

    private static void EnsureRunning(OperatingDispatch dispatch, DateTime now)
    {
        if (dispatch.Status == OperatingDispatchStatus.Claimed) dispatch.Start(dispatch.LeaseOwner!, now);
    }
    private static int ComputeBackoff(int attempt) => Math.Min(60, (int)Math.Pow(2, Math.Max(0, attempt - 1)));
    private static string Safe(string? message) => string.IsNullOrWhiteSpace(message) ? "Dispatch failed safely." : message.Trim()[..Math.Min(message.Trim().Length, 2000)];

    public async Task<IReadOnlyList<OperatingDispatchDto>> ListAsync(Guid companyId, int take, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, ct);
        take = Math.Clamp(take, 1, 100);
        var rows = await _db.OperatingDispatches.AsNoTracking().Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.UpdatedUtc).Take(take).ToListAsync(ct);
        return rows.Select(Map).ToArray();
    }

    public async Task<IReadOnlyList<OperatingCollaborationParticipantDto>> ListCollaborationAsync(
        Guid companyId, Guid initiativeId, CancellationToken ct)
    {
        await RequireMemberAsync(companyId, ct);
        return await _db.OperatingInitiativeCollaborators.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.InitiativeId == initiativeId)
            .OrderBy(x => x.Sequence)
            .Select(x => new OperatingCollaborationParticipantDto(x.Id, x.InitiativeId, x.AgentId,
                x.Role.ToStorageValue(), x.Pattern.ToStorageValue(), x.Sequence, x.Objective, x.ExpectedArtifact))
            .ToListAsync(ct);
    }

    private async Task RequireMemberAsync(Guid companyId, CancellationToken ct) =>
        _ = await _memberships.ResolveAsync(companyId, ct) ?? throw new UnauthorizedAccessException("Active company membership is required.");
    private static OperatingDispatchDto Map(OperatingDispatch x) => new(x.Id, x.CompanyId, x.InitiativeId,
        x.TaskId, x.Kind.ToStorageValue(), x.Status.ToStorageValue(), x.AttemptCount, x.MaxAttempts,
        x.NextAttemptUtc, x.LeaseExpiresUtc, x.OrchestrationRunId, x.CollaborationPlanId, x.FailureCode,
        x.FailureSummary, x.CreatedUtc, x.UpdatedUtc, x.CompletedUtc);
}
