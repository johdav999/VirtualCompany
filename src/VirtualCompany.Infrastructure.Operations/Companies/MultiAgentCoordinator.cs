using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class MultiAgentCoordinator : IMultiAgentCoordinator
{
    private const int RationaleMaxLength = 2000;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyTaskCommandService _taskCommands;
    private readonly ICompanyTaskQueryService _taskQueries;
    private readonly IAgentAssignmentGuard _agentAssignmentGuard;
    private readonly ISingleAgentOrchestrationService _singleAgentOrchestrationService;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly MultiAgentCollaborationOptions _options;

    public MultiAgentCoordinator(
        VirtualCompanyDbContext dbContext,
        ICompanyTaskCommandService taskCommands,
        ICompanyTaskQueryService taskQueries,
        IAgentAssignmentGuard agentAssignmentGuard,
        ISingleAgentOrchestrationService singleAgentOrchestrationService,
        IAuditEventWriter auditEventWriter,
        IOptions<MultiAgentCollaborationOptions> options)
    {
        _dbContext = dbContext;
        _taskCommands = taskCommands;
        _taskQueries = taskQueries;
        _agentAssignmentGuard = agentAssignmentGuard;
        _singleAgentOrchestrationService = singleAgentOrchestrationService;
        _auditEventWriter = auditEventWriter;
        _options = options.Value;
    }

    public async Task<MultiAgentCollaborationResultDto> ExecuteAsync(
        StartMultiAgentCollaborationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var startedUtc = DateTime.UtcNow;
        var correlationId = EnsureCorrelationId(command.CorrelationId);
        var limits = ResolveLimits(command.Limits);
        IReadOnlyList<WorkerSubtaskRequest> workers;
        try
        {
            ValidateCoordinatorRequest(command, limits);
            workers = command.Workers ?? Array.Empty<WorkerSubtaskRequest>();
            ValidatePlan(command, workers, limits, startedUtc);
        }
        catch (MultiAgentCollaborationValidationException ex)
        {
            await WriteGuardrailDeniedAuditAsync(command, limits, correlationId, ex, cancellationToken);
            throw;
        }

        var boundedPolicy = BoundedCollaborationPolicy.FromLimits(limits);
        var deadlineUtc = startedUtc.AddSeconds(limits.MaxRuntimeSeconds);

        using var runtimeBudget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        runtimeBudget.CancelAfter(TimeSpan.FromSeconds(limits.MaxRuntimeSeconds));

        EnsureRuntimeBudget(startedUtc, limits);

        await _agentAssignmentGuard.EnsureAgentCanReceiveNewTasksAsync(
            command.CompanyId,
            command.CoordinatorAgentId,
            nameof(command.CoordinatorAgentId),
            runtimeBudget.Token);

        foreach (var worker in workers)
        {
            await _agentAssignmentGuard.EnsureAgentCanReceiveNewTasksAsync(
                command.CompanyId,
                worker.AgentId,
                $"{nameof(command.Workers)}.{worker.AgentId:N}",
                runtimeBudget.Token);
        }
        var workerProfiles = await LoadWorkerExecutionAgentsAsync(
            command.CompanyId,
            workers.Select(x => x.AgentId),
            runtimeBudget.Token);

        var planId = Guid.NewGuid();
        var parentTask = await _taskCommands.CreateTaskAsync(
            command.CompanyId,
            new CreateTaskCommand(
                MultiAgentCollaborationTaskTypes.Parent,
                BuildParentTitle(command.Objective),
                command.Objective,
                WorkTaskPriority.Normal.ToStorageValue(),
                null,
                command.CoordinatorAgentId,
                BuildParentInputPayload(command, planId, limits, correlationId, workers),
                null,
                command.WorkflowInstanceId,
                null,
                null,
                null,
                correlationId),
            cancellationToken);

        await WriteAuditAsync(
            command.CompanyId,
            AuditEventActions.MultiAgentCollaborationStarted,
            AuditEventOutcomes.Pending,
            AuditActorTypes.Agent,
            command.CoordinatorAgentId,
            parentTask.Id,
            "Manager-worker collaboration started with an explicit bounded plan.",
            correlationId,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["planId"] = planId.ToString("N"),
                ["workerCount"] = workers.Count.ToString(),
                ["maxWorkers"] = limits.MaxWorkers.ToString(),
                ["maxDepth"] = limits.MaxDepth.ToString(),
                ["maxTotalSteps"] = limits.MaxTotalSteps.ToString(),
                ["maxRuntimeSeconds"] = limits.MaxRuntimeSeconds.ToString()
            },
            cancellationToken);

        var plannedSteps = workers
            .Select((worker, index) => new CollaborationStepDto(
                Guid.NewGuid(),
                index + 1,
                parentTask.Id,
                null,
                worker.AgentId,
                NormalizeRequired(worker.Objective),
                NormalizeOptional(worker.Instructions),
                1,
                MultiAgentCollaborationStatusValues.Planned,
                null))
            .ToList();

        var initialPlan = new CollaborationPlanDto(
            planId,
            command.CompanyId,
            parentTask.Id,
            command.CoordinatorAgentId,
            NormalizeRequired(command.Objective),
            limits,
            plannedSteps,
            MultiAgentCollaborationStatusValues.Planned,
            correlationId);

        await WriteAuditAsync(
            command.CompanyId,
            AuditEventActions.MultiAgentCollaborationPlanCreated,
            AuditEventOutcomes.Succeeded,
            AuditActorTypes.Agent,
            command.CoordinatorAgentId,
            parentTask.Id,
            "Explicit manager-worker plan created before worker execution.",
            correlationId,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["planId"] = planId.ToString("N"),
                ["stepCount"] = plannedSteps.Count.ToString()
            },
            cancellationToken);

        var creationDecision = boundedPolicy.Evaluate(new BoundedCollaborationExecutionState(
            workers.Count,
            plannedSteps.Count == 0 ? 0 : plannedSteps.Max(x => x.DelegationDepth),
            plannedSteps.Count,
            startedUtc));
        if (!creationDecision.Allowed)
        {
            throw new InvalidOperationException(creationDecision.Rationale);
        }

        var executableSteps = new List<CollaborationStepDto>(plannedSteps.Count);
        foreach (var step in plannedSteps)
        {
            EnsureRuntimeBudget(startedUtc, limits);
            var childTask = await _taskCommands.CreateSubtaskAsync(
                command.CompanyId,
                parentTask.Id,
                new CreateSubtaskCommand(
                    MultiAgentCollaborationTaskTypes.WorkerSubtask,
                    BuildWorkerTitle(step.Sequence, step.Objective),
                    step.Instructions ?? step.Objective,
                    WorkTaskPriority.Normal.ToStorageValue(),
                    null,
                    step.AssignedAgentId,
                    BuildWorkerInputPayload(command, initialPlan, step),
                    command.WorkflowInstanceId,
                    null,
                    null,
                    null,
                    BuildWorkerCorrelationId(correlationId, step.Sequence)),
                runtimeBudget.Token);

            var executableStep = step with
            {
                SubtaskId = childTask.Id,
                Status = MultiAgentCollaborationStatusValues.InProgress
            };
            executableSteps.Add(executableStep);

            await WriteAuditAsync(
                command.CompanyId,
                AuditEventActions.MultiAgentWorkerSubtaskCreated,
                AuditEventOutcomes.Succeeded,
                AuditActorTypes.Agent,
                command.CoordinatorAgentId,
                childTask.Id,
                "Worker subtask created and linked to the manager parent task.",
                correlationId,
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["planId"] = planId.ToString("N"),
                    ["parentTaskId"] = parentTask.Id.ToString("N"),
                    ["assignedAgentId"] = step.AssignedAgentId.ToString("N"),
                    ["sequence"] = step.Sequence.ToString(),
                    ["delegationDepth"] = step.DelegationDepth.ToString()
                },
                runtimeBudget.Token);
            EnsureRuntimeBudget(startedUtc, limits);
        }

        var contributions = new List<AgentContributionDto>();
        var finalSteps = new List<CollaborationStepDto>(executableSteps.Count);
        var executedStepCount = 0;
        var terminationReason = MultiAgentCollaborationTerminationReasons.Completed;
        foreach (var step in executableSteps)
        {
            runtimeBudget.Token.ThrowIfCancellationRequested();
            if (executedStepCount >= limits.MaxTotalSteps)
            {
                var stepDecision = boundedPolicy.Evaluate(new BoundedCollaborationExecutionState(workers.Count, step.DelegationDepth, executedStepCount + 1, startedUtc));
                terminationReason = MultiAgentCollaborationTerminationReasons.StepLimitExceeded;
                await WriteBoundedDecisionAuditAsync(
                    command,
                    planId,
                    parentTask.Id,
                    step.SubtaskId ?? parentTask.Id,
                    stepDecision,
                    correlationId,
                    runtimeBudget.Token);
                finalSteps.Add(step with
                {
                    Status = MultiAgentCollaborationStatusValues.Blocked,
                    RationaleSummary = $"Collaboration step budget of {limits.MaxTotalSteps} was exhausted."
                });
                continue;
            }

            if (!step.SubtaskId.HasValue)
            {
                throw new InvalidOperationException("Worker execution requires persisted child subtasks.");
            }

            try
            {
                EnsureRuntimeBudget(startedUtc, limits);
                executedStepCount++;
                var workerResult = await _singleAgentOrchestrationService.ExecuteAsync(
                    new SingleAgentOrchestrationRequest(
                        command.CompanyId,
                        step.SubtaskId.Value,
                        step.AssignedAgentId,
                        command.InitiatingActorId,
                        command.InitiatingActorType ?? AuditActorTypes.User,
                        BuildWorkerCorrelationId(correlationId, step.Sequence),
                        MultiAgentCollaborationTaskTypes.WorkerSubtask),
                    runtimeBudget.Token);

                var workerProfile = workerProfiles[step.AssignedAgentId];
                var contribution = new AgentContributionDto(
                    step.AssignedAgentId,
                    workerProfile.DisplayName,
                    workerProfile.RoleName,
                    step.SubtaskId.Value,
                    step.SubtaskId.Value,
                    step.Sequence,
                    workerResult.Status,
                    workerResult.UserFacingOutput,
                    workerResult.RationaleSummary,
                    workerResult.ConfidenceScore,
                    workerResult.CorrelationId);
                contributions.Add(contribution);
                finalSteps.Add(step with
                {
                    Status = ResolveStepStatus(workerResult.Status),
                    RationaleSummary = workerResult.RationaleSummary
                });
                EnsureRuntimeBudget(startedUtc, limits);

                await WriteAuditAsync(
                    command.CompanyId,
                    AuditEventActions.MultiAgentWorkerCompleted,
                    AuditEventOutcomes.Succeeded,
                    AuditActorTypes.Agent,
                    step.AssignedAgentId,
                    step.SubtaskId.Value,
                    workerResult.RationaleSummary,
                    correlationId,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["planId"] = planId.ToString("N"),
                        ["parentTaskId"] = parentTask.Id.ToString("N"),
                        ["workerCorrelationId"] = workerResult.CorrelationId,
                        ["workerStatus"] = workerResult.Status
                    },
                    runtimeBudget.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                terminationReason = MultiAgentCollaborationTerminationReasons.RuntimeBudgetExceeded;
                var failureSummary = Trim($"Collaboration runtime budget of {limits.MaxRuntimeSeconds} second(s) was exhausted.", RationaleMaxLength);
                await _taskCommands.UpdateStatusAsync(
                    command.CompanyId,
                    step.SubtaskId.Value,
                    new UpdateTaskStatusCommand(
                        WorkTaskStatus.Failed.ToStorageValue(),
                        BuildFailurePayload(planId, parentTask.Id, step, failureSummary, correlationId),
                        failureSummary,
                        0m),
                    cancellationToken);

                var workerProfile = workerProfiles[step.AssignedAgentId];
                var contribution = new AgentContributionDto(
                    step.AssignedAgentId,
                    workerProfile.DisplayName,
                    workerProfile.RoleName,
                    step.SubtaskId.Value,
                    step.SubtaskId.Value,
                    step.Sequence,
                    OrchestrationStatusValues.Failed,
                    failureSummary,
                    failureSummary,
                    0m,
                    BuildWorkerCorrelationId(correlationId, step.Sequence));
                contributions.Add(contribution);
                finalSteps.Add(step with
                {
                    Status = MultiAgentCollaborationStatusValues.Failed,
                    RationaleSummary = failureSummary
                });

                await WriteAuditAsync(
                    command.CompanyId,
                    AuditEventActions.MultiAgentWorkerFailed,
                    AuditEventOutcomes.Failed,
                    AuditActorTypes.Agent,
                    step.AssignedAgentId,
                    step.SubtaskId.Value,
                    failureSummary,
                    correlationId,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["planId"] = planId.ToString("N"),
                        ["parentTaskId"] = parentTask.Id.ToString("N"),
                        ["terminationReason"] = terminationReason,
                        ["nonRetryable"] = "true"
                    },
                    cancellationToken);

                await WriteBoundedDecisionAuditAsync(
                    command,
                    planId,
                    parentTask.Id,
                    step.SubtaskId.Value,
                    BoundedCollaborationDecision.Denied(
                        MultiAgentCollaborationTerminationReasons.RuntimeBudgetExceeded,
                        "runtime",
                        limits.MaxRuntimeSeconds,
                        Math.Max(limits.MaxRuntimeSeconds, (int)Math.Ceiling((DateTime.UtcNow - startedUtc).TotalSeconds)),
                        failureSummary),
                    correlationId,
                    cancellationToken);
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (string.Equals(terminationReason, MultiAgentCollaborationTerminationReasons.Completed, StringComparison.OrdinalIgnoreCase))
                {
                    terminationReason = MultiAgentCollaborationTerminationReasons.WorkerExecutionFailed;
                }

                var failureSummary = Trim($"Worker subtask failed safely: {ex.Message}", RationaleMaxLength);
                await _taskCommands.UpdateStatusAsync(
                    command.CompanyId,
                    step.SubtaskId.Value,
                    new UpdateTaskStatusCommand(
                        WorkTaskStatus.Failed.ToStorageValue(),
                        BuildFailurePayload(planId, parentTask.Id, step, failureSummary, correlationId),
                        failureSummary,
                        0m),
                    runtimeBudget.Token);

                var workerProfile = workerProfiles[step.AssignedAgentId];
                var contribution = new AgentContributionDto(
                    step.AssignedAgentId,
                    workerProfile.DisplayName,
                    workerProfile.RoleName,
                    step.SubtaskId.Value,
                    step.SubtaskId.Value,
                    step.Sequence,
                    OrchestrationStatusValues.Failed,
                    failureSummary,
                    failureSummary,
                    0m,
                    BuildWorkerCorrelationId(correlationId, step.Sequence));
                contributions.Add(contribution);
                finalSteps.Add(step with
                {
                    Status = MultiAgentCollaborationStatusValues.Failed,
                    RationaleSummary = failureSummary
                });

                await WriteAuditAsync(
                    command.CompanyId,
                    AuditEventActions.MultiAgentWorkerFailed,
                    AuditEventOutcomes.Failed,
                    AuditActorTypes.Agent,
                    step.AssignedAgentId,
                    step.SubtaskId.Value,
                    failureSummary,
                    correlationId,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["planId"] = planId.ToString("N"),
                        ["parentTaskId"] = parentTask.Id.ToString("N"),
                        ["errorType"] = ex.GetType().Name
                    },
                    runtimeBudget.Token);
            }
        }

        var unprocessedSteps = executableSteps
            .Where(step => finalSteps.All(final => final.StepId != step.StepId))
            .ToList();
        foreach (var step in unprocessedSteps)
        {
            finalSteps.Add(step with
            {
                Status = MultiAgentCollaborationStatusValues.Blocked,
                RationaleSummary = "Collaboration execution stopped before this planned worker step could run."
            });
        }

        var status = string.Equals(terminationReason, MultiAgentCollaborationTerminationReasons.Completed, StringComparison.OrdinalIgnoreCase)
            ? ResolveCollaborationStatus(contributions)
            : MultiAgentCollaborationStatusValues.Failed;
        var finalResponse = BuildFinalResponse(command.Objective, contributions);
        var contributorRationaleSummaries = BuildContributorRationaleSummaries(contributions);
        var completedUtc = DateTime.UtcNow;
        var metrics = new CollaborationExecutionMetricsDto(
            plannedSteps.Count,
            executedStepCount,
            finalSteps.Count == 0 ? 0 : finalSteps.Max(x => x.DelegationDepth),
            startedUtc,
            completedUtc);
        var structuredOutput = BuildFinalOutput(
            command,
            planId,
            parentTask.Id,
            limits,
            finalSteps,
            contributions,
            status,
            contributorRationaleSummaries,
            finalResponse,
            terminationReason,
            IsRetryable(status, terminationReason),
            metrics,
            correlationId,
            startedUtc,
            deadlineUtc,
            completedUtc);

        await _taskCommands.UpdateStatusAsync(
            command.CompanyId,
            parentTask.Id,
            new UpdateTaskStatusCommand(
                string.Equals(status, MultiAgentCollaborationStatusValues.Failed, StringComparison.OrdinalIgnoreCase)
                    ? WorkTaskStatus.Failed.ToStorageValue()
                    : WorkTaskStatus.Completed.ToStorageValue(),
                structuredOutput,
                BuildParentRationale(contributions, status),
                contributions.Count == 0 ? 0m : contributions.Average(x => x.ConfidenceScore ?? 0.5m)),
            cancellationToken);

        await WriteAuditAsync(
            command.CompanyId,
            AuditEventActions.MultiAgentCollaborationConsolidated,
            string.Equals(status, MultiAgentCollaborationStatusValues.Failed, StringComparison.OrdinalIgnoreCase)
                ? AuditEventOutcomes.Failed
                : AuditEventOutcomes.Succeeded,
            AuditActorTypes.Agent,
            command.CoordinatorAgentId,
            parentTask.Id,
            BuildParentRationale(contributions, status),
            correlationId,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["planId"] = planId.ToString("N"),
                ["status"] = status,
                ["contributionCount"] = contributions.Count.ToString(),
                ["failedContributionCount"] = contributions.Count(IsFailed).ToString(),
                ["terminationReason"] = terminationReason,
                ["actualStepCount"] = executedStepCount.ToString(),
                ["contributorRationaleSummaryCount"] = contributorRationaleSummaries.Count.ToString()
            },
            cancellationToken);

        return new MultiAgentCollaborationResultDto(
            planId,
            command.CompanyId,
            parentTask.Id,
            command.CoordinatorAgentId,
            status,
            finalResponse,
            contributions.OrderBy(x => x.Sequence).ToList(),
            finalSteps.OrderBy(x => x.Sequence).ToList(),
            contributions.Where(IsFailed).Select(x => x.SubtaskId).ToList(),
            new ConsolidatedMultiAgentResponseDto(finalResponse, contributions.OrderBy(x => x.Sequence).ToList())
            {
                ContributorRationaleSummaries = contributorRationaleSummaries
            },
            terminationReason,
            IsRetryable(status, terminationReason),
            metrics,
            structuredOutput,
            correlationId)
        {
            ContributorRationaleSummaries = contributorRationaleSummaries
        };
    }

    private CollaborationLimitDto ResolveLimits(CollaborationLimitRequest? requested)
    {
        var configuredMaxWorkers = Math.Max(1, _options.MaxWorkers);
        var configuredMaxTotalSteps = Math.Max(configuredMaxWorkers, _options.MaxTotalSteps);
        var configuredMaxDepth = Math.Clamp(_options.MaxDepth, 1, 1);
        var configuredMaxRuntimeSeconds = Math.Max(1, _options.MaxRuntimeSeconds);

        return new CollaborationLimitDto(
            Math.Clamp(requested?.MaxWorkers ?? configuredMaxWorkers, 1, configuredMaxWorkers),
            Math.Clamp(requested?.MaxDepth ?? configuredMaxDepth, 1, configuredMaxDepth),
            Math.Clamp(requested?.MaxRuntimeSeconds ?? configuredMaxRuntimeSeconds, 1, configuredMaxRuntimeSeconds),
            Math.Clamp(requested?.MaxTotalSteps ?? configuredMaxTotalSteps, 1, configuredMaxTotalSteps));
    }

    private void ValidateCoordinatorRequest(
        StartMultiAgentCollaborationCommand command,
        CollaborationLimitDto limits)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        AddCoordinatorRequestErrors(command, limits, errors);
        AddNestedDelegationErrors(command, limits, errors);

        if (errors.Count > 0)
        {
            throw new MultiAgentCollaborationValidationException(errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
        }
    }

    private static void AddCoordinatorRequestErrors(
        StartMultiAgentCollaborationCommand command,
        CollaborationLimitDto limits,
        IDictionary<string, List<string>> errors)
    {
        if (command.CompanyId == Guid.Empty)
        {
            AddError(errors, nameof(command.CompanyId), "CompanyId is required.");
        }

        if (command.CoordinatorAgentId == Guid.Empty)
        {
            AddError(errors, nameof(command.CoordinatorAgentId), "CoordinatorAgentId is required.");
        }

        if (string.IsNullOrWhiteSpace(command.Objective))
        {
            AddError(errors, nameof(command.Objective), "Objective is required.");
        }

        if (command.Limits?.MaxDepth is not null && command.Limits.MaxDepth != 1)
        {
            AddError(errors, nameof(command.Limits), "Manager-worker collaboration depth must be exactly 1.");
        }

        if (limits.MaxDepth != 1)
        {
            AddError(errors, nameof(command.Limits), "Manager-worker collaboration depth must be exactly 1.");
        }

        if (command.Limits?.MaxTotalSteps is <= 0)
        {
            AddError(errors, nameof(command.Limits), "MaxTotalSteps must be greater than zero.");
        }

        if (command.Limits?.MaxRuntimeSeconds is <= 0)
        {
            AddError(errors, nameof(command.Limits), "MaxRuntimeSeconds must be greater than zero.");
        }
    }

    private static void AddNestedDelegationErrors(
        StartMultiAgentCollaborationCommand command,
        CollaborationLimitDto limits,
        IDictionary<string, List<string>> errors)
    {
        if (command.InputPayload is null || command.InputPayload.Count == 0)
        {
            return;
        }

        if (TryGetString(command.InputPayload, "collaborationRole", out var collaborationRole) &&
            string.Equals(collaborationRole, "worker_subtask", StringComparison.OrdinalIgnoreCase))
        {
            AddError(errors, nameof(command.InputPayload), "Worker subtasks cannot start nested manager-worker coordination.");
        }

        if (TryGetBoolean(command.InputPayload, "allowFurtherDelegation", out var allowFurtherDelegation) &&
            !allowFurtherDelegation)
        {
            AddError(errors, nameof(command.InputPayload), "Further delegation is disabled for this task context.");
        }

        if (TryGetInt32(command.InputPayload, "delegationDepth", out var delegationDepth) &&
            delegationDepth >= limits.MaxDepth)
        {
            AddError(errors, nameof(command.InputPayload), $"Delegation depth cannot exceed {limits.MaxDepth}.");
        }

        if (TryGetBoolean(command.InputPayload, "allowFurtherDelegation", out var allowedFurtherDelegation) &&
            allowedFurtherDelegation)
        {
            AddError(errors, nameof(command.InputPayload), "Unplanned further delegation is not allowed.");
        }

        if (TryGetString(command.InputPayload, "planId", out var upstreamPlanId) &&
            !string.IsNullOrWhiteSpace(upstreamPlanId))
        {
            AddError(errors, nameof(command.InputPayload), "Nested collaboration plans cannot be started from an existing collaboration plan.");
        }
    }

    private static void ValidatePlan(
        StartMultiAgentCollaborationCommand command,
        IReadOnlyList<WorkerSubtaskRequest> workers,
        CollaborationLimitDto limits,
        DateTime startedUtc)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (command.Workers is null)
        {
            AddError(errors, nameof(command.Workers), "An explicit manager-worker collaboration plan is required before delegation.");
        }

        if (workers.Count == 0)
        {
            AddError(errors, nameof(command.Workers), "A manager-worker coordination plan requires at least one assignable worker subtask.");
        }

        if (workers.Count > limits.MaxWorkers)
        {
            AddError(errors, nameof(command.Workers), $"Worker fan-out cannot exceed {limits.MaxWorkers}.");
        }

        if (workers.Count > limits.MaxTotalSteps)
        {
            AddError(errors, nameof(command.Workers), $"Collaboration step count cannot exceed {limits.MaxTotalSteps}.");
        }

        var boundedPolicy = BoundedCollaborationPolicy.FromLimits(limits);
        var decision = boundedPolicy.Evaluate(new BoundedCollaborationExecutionState(
            workers.Count,
            workers.Count == 0 ? 0 : 1,
            workers.Count,
            startedUtc));
        if (!decision.Allowed && !string.Equals(decision.TerminationReason, MultiAgentCollaborationTerminationReasons.RuntimeBudgetExceeded, StringComparison.OrdinalIgnoreCase))
        {
            AddError(errors, nameof(command.Workers), decision.Rationale ?? "The requested manager-worker collaboration exceeds its bounded execution policy.");
        }

        var seenAgentIds = new HashSet<Guid>();
        for (var i = 0; i < workers.Count; i++)
        {
            var worker = workers[i];
            if (worker.AgentId == Guid.Empty)
            {
                AddError(errors, $"{nameof(command.Workers)}[{i}].{nameof(worker.AgentId)}", "Worker AgentId is required.");
            }
            else if (!seenAgentIds.Add(worker.AgentId))
            {
                AddError(errors, nameof(command.Workers), "Each worker agent can be assigned at most one planned subtask.");
            }

            if (worker.AgentId == command.CoordinatorAgentId)
            {
                AddError(errors, $"{nameof(command.Workers)}[{i}].{nameof(worker.AgentId)}", "Coordinator agents cannot self-assign worker subtasks.");
            }

            if (string.IsNullOrWhiteSpace(worker.Objective))
            {
                AddError(errors, $"{nameof(command.Workers)}[{i}].{nameof(worker.Objective)}", "Worker objective is required.");
            }
        }

        AddDelegationChainCycleErrors(command, workers, errors);

        if (errors.Count > 0)
        {
            throw new MultiAgentCollaborationValidationException(errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
        }
    }

    private async Task<IReadOnlyList<WorkerSubtaskRequest>> GenerateWorkerPlanAsync(
        StartMultiAgentCollaborationCommand command,
        CollaborationLimitDto limits,
        CancellationToken cancellationToken)
    {
        var maxGeneratedWorkers = Math.Min(limits.MaxWorkers, 3);
        var agents = await _dbContext.Agents
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == command.CompanyId &&
                x.Id != command.CoordinatorAgentId &&
                x.Status != AgentStatus.Paused &&
                x.Status != AgentStatus.Archived)
            .OrderBy(x => x.Department)
            .ThenBy(x => x.DisplayName)
            .Select(x => new WorkerPlanAgent(x.Id, x.DisplayName, x.RoleName, x.Department, x.RoleBrief))
            .Take(maxGeneratedWorkers)
            .ToListAsync(cancellationToken);

        return agents
            .Select(agent => new WorkerSubtaskRequest(
                agent.AgentId,
                $"Assess {agent.Department} implications for: {NormalizeRequired(command.Objective)}",
                BuildGeneratedWorkerInstructions(agent, command.Objective)))
            .ToList();
    }

    private async Task<IReadOnlyDictionary<Guid, WorkerExecutionAgent>> LoadWorkerExecutionAgentsAsync(
        Guid companyId,
        IEnumerable<Guid> agentIds,
        CancellationToken cancellationToken)
    {
        var ids = agentIds.Distinct().ToList();
        var agents = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.Id))
            .Select(x => new WorkerExecutionAgent(x.Id, x.DisplayName, x.RoleName, x.Department))
            .ToListAsync(cancellationToken);

        return agents.ToDictionary(x => x.AgentId);
    }

    private static Dictionary<string, JsonNode?> BuildParentInputPayload(
        StartMultiAgentCollaborationCommand command,
        Guid planId,
        CollaborationLimitDto limits,
        string correlationId,
        IReadOnlyList<WorkerSubtaskRequest> workers)
    {
        var payload = CloneNodes(command.InputPayload);
        payload["schemaVersion"] = JsonValue.Create("2026-04-13");
        payload["collaborationRole"] = JsonValue.Create("manager");
        payload["planGenerationMode"] = JsonValue.Create("explicit_request");
        payload["planId"] = JsonValue.Create(planId.ToString("N"));
        payload["objective"] = JsonValue.Create(NormalizeRequired(command.Objective));
        payload["coordinatorAgentId"] = JsonValue.Create(command.CoordinatorAgentId.ToString("N"));
        payload["correlationId"] = JsonValue.Create(correlationId);
        payload["limits"] = JsonSerializer.SerializeToNode(limits);
        payload["plannedWorkers"] = JsonSerializer.SerializeToNode(workers.Select((worker, index) => new
        {
            sequence = index + 1,
            agentId = worker.AgentId,
            objective = NormalizeRequired(worker.Objective),
            instructions = NormalizeOptional(worker.Instructions),
            delegationDepth = 1
        }).ToList());
        return payload;
    }

    private static Dictionary<string, JsonNode?> BuildWorkerInputPayload(
        StartMultiAgentCollaborationCommand command,
        CollaborationPlanDto plan,
        CollaborationStepDto step)
    {
        return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create("2026-04-13"),
            ["collaborationRole"] = JsonValue.Create("worker_subtask"),
            ["planId"] = JsonValue.Create(plan.PlanId.ToString("N")),
            ["parentTaskId"] = JsonValue.Create(plan.ParentTaskId.ToString("N")),
            ["coordinatorAgentId"] = JsonValue.Create(plan.CoordinatorAgentId.ToString("N")),
            ["objective"] = JsonValue.Create(step.Objective),
            ["instructions"] = JsonValue.Create(step.Instructions ?? step.Objective),
            ["delegationDepth"] = JsonValue.Create(step.DelegationDepth),
            ["allowFurtherDelegation"] = JsonValue.Create(false),
            ["sourceObjective"] = JsonValue.Create(command.Objective),
            ["plan"] = JsonSerializer.SerializeToNode(plan)
        };
    }

    private static Dictionary<string, JsonNode?> BuildFailurePayload(
        Guid planId,
        Guid parentTaskId,
        CollaborationStepDto step,
        string failureSummary,
        string correlationId) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create("2026-04-13"),
            ["planId"] = JsonValue.Create(planId.ToString("N")),
            ["parentTaskId"] = JsonValue.Create(parentTaskId.ToString("N")),
            ["agentId"] = JsonValue.Create(step.AssignedAgentId.ToString("N")),
            ["sequence"] = JsonValue.Create(step.Sequence),
            ["status"] = JsonValue.Create(MultiAgentCollaborationStatusValues.Failed),
            ["rationaleSummary"] = JsonValue.Create(failureSummary),
            ["correlationId"] = JsonValue.Create(correlationId)
        };

    private static Dictionary<string, JsonNode?> BuildFinalOutput(
        StartMultiAgentCollaborationCommand command,
        Guid planId,
        Guid parentTaskId,
        CollaborationLimitDto limits,
        IReadOnlyList<CollaborationStepDto> steps,
        IReadOnlyList<AgentContributionDto> contributions,
        string status,
        IReadOnlyList<AgentRationaleSummaryDto> contributorRationaleSummaries,
        string finalResponse,
        string terminationReason,
        bool isRetryable,
        CollaborationExecutionMetricsDto metrics,
        string correlationId,
        DateTime startedUtc,
        DateTime deadlineUtc,
        DateTime completedUtc) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create("2026-04-13"),
            ["planId"] = JsonValue.Create(planId.ToString("N")),
            ["parentTaskId"] = JsonValue.Create(parentTaskId.ToString("N")),
            ["coordinatorAgentId"] = JsonValue.Create(command.CoordinatorAgentId.ToString("N")),
            ["objective"] = JsonValue.Create(command.Objective),
            ["status"] = JsonValue.Create(status),
            ["finalResponse"] = JsonValue.Create(finalResponse),
            ["terminationReason"] = JsonValue.Create(terminationReason),
            ["isRetryable"] = JsonValue.Create(isRetryable),
            ["metrics"] = JsonSerializer.SerializeToNode(metrics),
            ["limits"] = JsonSerializer.SerializeToNode(limits),
            ["steps"] = JsonSerializer.SerializeToNode(steps.OrderBy(x => x.Sequence).ToList()),
            ["contributions"] = JsonSerializer.SerializeToNode(contributions.OrderBy(x => x.Sequence).ToList()),
            ["sourceAttribution"] = JsonSerializer.SerializeToNode(contributions.OrderBy(x => x.Sequence).Select(contribution => new
            {
                contribution.Sequence,
                contribution.AgentId,
                contribution.AgentName,
                contribution.AgentRole,
                contribution.SubtaskId,
                contribution.SourceTaskId,
                contribution.Status,
                contribution.Output,
                contribution.RationaleSummary,
                contribution.ConfidenceScore,
                contribution.CorrelationId
            }).ToList()),
            ["contributorRationaleSummaries"] = JsonSerializer.SerializeToNode(contributorRationaleSummaries),
            ["consolidatedResponse"] = JsonSerializer.SerializeToNode(new ConsolidatedMultiAgentResponseDto(
                finalResponse,
                contributions.OrderBy(x => x.Sequence).ToList())
            {
                ContributorRationaleSummaries = contributorRationaleSummaries
            }),
            ["failedSubtaskIds"] = JsonSerializer.SerializeToNode(contributions.Where(IsFailed).Select(x => x.SubtaskId).ToList()),
            ["correlationId"] = JsonValue.Create(correlationId),
            ["startedAtUtc"] = JsonValue.Create(startedUtc),
            ["deadlineUtc"] = JsonValue.Create(deadlineUtc),
            ["completedAtUtc"] = JsonValue.Create(completedUtc),
            ["shared_engine"] = JsonValue.Create("multi_agent_coordinator")
        };

    private Task WriteAuditAsync(
        Guid companyId,
        string action,
        string outcome,
        string actorType,
        Guid? actorId,
        Guid taskId,
        string? rationale,
        string correlationId,
        IReadOnlyDictionary<string, string?> metadata,
        CancellationToken cancellationToken) =>
        _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                actorType,
                actorId,
                action,
                AuditTargetTypes.WorkTask,
                taskId.ToString("N"),
                outcome,
                rationale,
                ["multi_agent_coordinator", "single_agent_orchestration", "tasks"],
                metadata,
                correlationId,
                DateTime.UtcNow),
            cancellationToken);

    private async Task WriteGuardrailDeniedAuditAsync(
        StartMultiAgentCollaborationCommand command,
        CollaborationLimitDto limits,
        string correlationId,
        MultiAgentCollaborationValidationException exception,
        CancellationToken cancellationToken)
    {
        if (command.CompanyId == Guid.Empty)
        {
            return;
        }

        var decision = ClassifyValidationGuardrail(exception, limits, command.Workers?.Count ?? 0);
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["terminationReason"] = decision.TerminationReason,
            ["limitType"] = decision.LimitType,
            ["configuredThreshold"] = decision.ConfiguredThreshold?.ToString(),
            ["observedValue"] = decision.ObservedValue?.ToString(),
            ["maxWorkers"] = limits.MaxWorkers.ToString(),
            ["maxDepth"] = limits.MaxDepth.ToString(),
            ["maxRuntimeSeconds"] = limits.MaxRuntimeSeconds.ToString(),
            ["maxTotalSteps"] = limits.MaxTotalSteps.ToString(),
            ["validationErrors"] = string.Join(" | ", exception.Errors.SelectMany(x => x.Value))
        };

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                command.CompanyId,
                string.IsNullOrWhiteSpace(command.InitiatingActorType) ? AuditActorTypes.Agent : command.InitiatingActorType!,
                command.InitiatingActorId ?? command.CoordinatorAgentId,
                AuditEventActions.MultiAgentCollaborationGuardrailDenied,
                AuditTargetTypes.LinkedEntity,
                $"multi_agent_collaboration:{correlationId}",
                AuditEventOutcomes.Denied,
                decision.Rationale ?? "Manager-worker collaboration was denied by bounded execution policy.",
                ["multi_agent_coordinator", "bounded_collaboration_policy"],
                metadata,
                correlationId,
                DateTime.UtcNow),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private Task WriteBoundedDecisionAuditAsync(
        StartMultiAgentCollaborationCommand command,
        Guid planId,
        Guid parentTaskId,
        Guid targetTaskId,
        BoundedCollaborationDecision decision,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (decision.Allowed)
        {
            return Task.CompletedTask;
        }

        return WriteAuditAsync(
            command.CompanyId,
            AuditEventActions.MultiAgentCollaborationGuardrailDenied,
            AuditEventOutcomes.Denied,
            AuditActorTypes.Agent,
            command.CoordinatorAgentId,
            targetTaskId,
            decision.Rationale,
            correlationId,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["planId"] = planId.ToString("N"),
                ["parentTaskId"] = parentTaskId.ToString("N"),
                ["terminationReason"] = decision.TerminationReason,
                ["limitType"] = decision.LimitType,
                ["configuredThreshold"] = decision.ConfiguredThreshold?.ToString(),
                ["observedValue"] = decision.ObservedValue?.ToString()
            },
            cancellationToken);
    }

    private static BoundedCollaborationDecision ClassifyValidationGuardrail(
        MultiAgentCollaborationValidationException exception,
        CollaborationLimitDto limits,
        int workerCount)
    {
        var messages = exception.Errors.SelectMany(x => x.Value).ToList();
        if (messages.Any(x => x.Contains("fan-out", StringComparison.OrdinalIgnoreCase)))
        {
            return BoundedCollaborationDecision.Denied(MultiAgentCollaborationTerminationReasons.FanOutExceeded, "fan_out", limits.MaxWorkers, workerCount, $"Worker fan-out cannot exceed {limits.MaxWorkers}.");
        }

        if (messages.Any(x => x.Contains("depth", StringComparison.OrdinalIgnoreCase) || x.Contains("nested", StringComparison.OrdinalIgnoreCase) || x.Contains("Further delegation", StringComparison.OrdinalIgnoreCase)))
        {
            return BoundedCollaborationDecision.Denied(MultiAgentCollaborationTerminationReasons.DepthLimitExceeded, "depth", limits.MaxDepth, limits.MaxDepth + 1, $"Delegation depth cannot exceed {limits.MaxDepth}.");
        }

        if (messages.Any(x => x.Contains("step", StringComparison.OrdinalIgnoreCase)))
        {
            return BoundedCollaborationDecision.Denied(MultiAgentCollaborationTerminationReasons.StepLimitExceeded, "step_count", limits.MaxTotalSteps, workerCount, $"Collaboration step count cannot exceed {limits.MaxTotalSteps}.");
        }

        if (messages.Any(x => x.Contains("runtime", StringComparison.OrdinalIgnoreCase)))
        {
            return BoundedCollaborationDecision.Denied(MultiAgentCollaborationTerminationReasons.RuntimeBudgetExceeded, "runtime", limits.MaxRuntimeSeconds, 0, $"Collaboration runtime budget must be between 1 and {limits.MaxRuntimeSeconds} second(s).");
        }

        return BoundedCollaborationDecision.Denied(MultiAgentCollaborationTerminationReasons.PlanInvalid, "explicit_plan", 1, workerCount, "Manager-worker collaboration was denied because the explicit bounded plan was invalid.");
    }

    private static string BuildFinalResponse(string objective, IReadOnlyList<AgentContributionDto> contributions)
    {
        var completed = contributions.Where(x => !IsFailed(x)).OrderBy(x => x.Sequence).ToList();
        var failed = contributions.Where(IsFailed).OrderBy(x => x.Sequence).ToList();
        var builder = new StringBuilder();
        builder.Append("Manager-worker collaboration result for: ");
        builder.AppendLine(objective.Trim());
        builder.Append("Synthesis: ");

        if (completed.Count == 0)
        {
            builder.AppendLine("No worker completed successfully, so there is no attributed worker result to synthesize.");
        }
        else
        {
            builder.Append("Consolidated ");
            builder.Append(completed.Count);
            builder.AppendLine(" attributed worker result(s) into the final answer below.");
            builder.AppendLine("Attributed worker sources:");
            foreach (var contribution in completed)
            {
                builder.Append("- ");
                builder.Append(contribution.AgentName);
                if (!string.IsNullOrWhiteSpace(contribution.AgentRole))
                {
                    builder.Append(" (");
                    builder.Append(contribution.AgentRole);
                    builder.Append(')');
                }
                builder.Append("; agent ");
                builder.Append(contribution.AgentId.ToString("N"));
                builder.Append("; source task ");
                builder.Append(contribution.SourceTaskId.ToString("N"));
                builder.Append("): ");
                builder.AppendLine(Trim(contribution.Output, 700));
            }
        }

        if (failed.Count > 0)
        {
            builder.Append("Failed worker subtasks excluded from synthesis: ");
            builder.AppendLine(string.Join(", ", failed.Select(x => x.SubtaskId.ToString("N"))));
        }

        return Trim(builder.ToString(), 4000);
    }

    private static string BuildGeneratedWorkerInstructions(WorkerPlanAgent agent, string objective) =>
        Trim(
            $"Contribute from the {agent.RoleName} perspective in {agent.Department}. Do not create or request additional worker subtasks. Return concise findings, risks, and a recommended next action for: {objective}",
            1000);

    private static IReadOnlyList<AgentRationaleSummaryDto> BuildContributorRationaleSummaries(IReadOnlyList<AgentContributionDto> contributions) =>
        contributions
            .OrderBy(x => x.Sequence)
            .Select(contribution => new AgentRationaleSummaryDto(
                contribution.AgentId,
                contribution.AgentName,
                contribution.AgentRole,
                contribution.SubtaskId,
                contribution.SourceTaskId,
                contribution.Sequence,
                contribution.Status,
                string.IsNullOrWhiteSpace(contribution.RationaleSummary) ? null : Trim(contribution.RationaleSummary, RationaleMaxLength),
                contribution.ConfidenceScore,
                contribution.CorrelationId))
            .ToList();

    private static string BuildParentRationale(IReadOnlyList<AgentContributionDto> contributions, string status) =>
        Trim($"Consolidated {contributions.Count} bounded worker contribution(s) with status '{status}'. No recursive worker delegation was allowed.", RationaleMaxLength);

    private static string ResolveCollaborationStatus(IReadOnlyList<AgentContributionDto> contributions)
    {
        if (contributions.Count == 0 || contributions.All(IsFailed))
        {
            return MultiAgentCollaborationStatusValues.Failed;
        }

        return contributions.Any(IsFailed)
            ? MultiAgentCollaborationStatusValues.Partial
            : MultiAgentCollaborationStatusValues.Completed;
    }

    private static string ResolveStepStatus(string workerStatus) =>
        string.Equals(workerStatus, OrchestrationStatusValues.Failed, StringComparison.OrdinalIgnoreCase)
            ? MultiAgentCollaborationStatusValues.Failed
            : MultiAgentCollaborationStatusValues.Completed;

    private static bool IsRetryable(string status, string terminationReason) =>
        string.Equals(status, MultiAgentCollaborationStatusValues.Failed, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(terminationReason, MultiAgentCollaborationTerminationReasons.WorkerExecutionFailed, StringComparison.OrdinalIgnoreCase);

    private static void EnsureRuntimeBudget(DateTime startedUtc, CollaborationLimitDto limits)
    {
        if (DateTime.UtcNow - startedUtc > TimeSpan.FromSeconds(limits.MaxRuntimeSeconds))
        {
            throw new OperationCanceledException($"Collaboration runtime budget of {limits.MaxRuntimeSeconds} second(s) was exhausted.");
        }
    }

    private static bool IsFailed(AgentContributionDto contribution) =>
        string.Equals(contribution.Status, OrchestrationStatusValues.Failed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(contribution.Status, MultiAgentCollaborationStatusValues.Failed, StringComparison.OrdinalIgnoreCase);

    private static string BuildParentTitle(string objective) =>
        Trim($"Manager-worker collaboration: {NormalizeRequired(objective)}", 200);

    private static string BuildWorkerTitle(int sequence, string objective) =>
        Trim($"Worker {sequence}: {NormalizeRequired(objective)}", 200);

    private static string BuildWorkerCorrelationId(string correlationId, int sequence) =>
        $"{correlationId}:worker:{sequence}";

    private static string EnsureCorrelationId(string? requestedCorrelationId) =>
        string.IsNullOrWhiteSpace(requestedCorrelationId)
            ? System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString("N")
            : requestedCorrelationId.Trim();

    private static bool TryGetString(IReadOnlyDictionary<string, JsonNode?> payload, string key, out string value)
    {
        value = string.Empty;
        if (!payload.TryGetValue(key, out var node) || node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetBoolean(IReadOnlyDictionary<string, JsonNode?> payload, string key, out bool value)
    {
        value = false;
        if (!payload.TryGetValue(key, out var node) || node is not JsonValue jsonValue || !jsonValue.TryGetValue<bool>(out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryGetInt32(IReadOnlyDictionary<string, JsonNode?> payload, string key, out int value)
    {
        value = 0;
        if (!payload.TryGetValue(key, out var node) || node is not JsonValue jsonValue || !jsonValue.TryGetValue<int>(out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static void AddDelegationChainCycleErrors(
        StartMultiAgentCollaborationCommand command,
        IReadOnlyList<WorkerSubtaskRequest> workers,
        IDictionary<string, List<string>> errors)
    {
        if (command.InputPayload is null ||
            !command.InputPayload.TryGetValue("delegationChain", out var chainNode) ||
            chainNode is not JsonArray chain)
        {
            return;
        }

        var chainAgentIds = new HashSet<Guid>();
        foreach (var item in chain)
        {
            if (item is JsonValue value &&
                value.TryGetValue<string>(out var text) &&
                Guid.TryParse(text, out var agentId) &&
                !chainAgentIds.Add(agentId))
            {
                AddError(errors, nameof(command.InputPayload), "Delegation chain contains a repeated agent and would create a collaboration cycle.");
                return;
            }
        }

        if (chainAgentIds.Contains(command.CoordinatorAgentId) ||
            workers.Any(worker => chainAgentIds.Contains(worker.AgentId)))
        {
            AddError(errors, nameof(command.InputPayload), "Delegation chain cannot revisit the coordinator or planned worker agents.");
        }
    }

    private static string NormalizeRequired(string value) =>
        value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static string Trim(string value, int maxLength)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength ? normalized : string.Concat(normalized.AsSpan(0, maxLength - 3), "...");
    }

    private static void AddError(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }

    private sealed record WorkerPlanAgent(Guid AgentId, string DisplayName, string RoleName, string Department, string? RoleBrief);
    private sealed record WorkerExecutionAgent(Guid AgentId, string DisplayName, string RoleName, string Department);
}
