using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AgentStatusAggregationService : IAgentStatusAggregationService
{
    private static readonly TimeSpan RecentSignalWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan StalledWorkThreshold = TimeSpan.FromMinutes(60);
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly TimeProvider _timeProvider;

    public AgentStatusAggregationService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _timeProvider = timeProvider;
    }

    public async Task<AgentStatusCardsResponseDto> GetStatusCardsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var generatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var stalledCutoffUtc = generatedUtc - StalledWorkThreshold;
        var recentSignalCutoffUtc = generatedUtc - RecentSignalWindow;

        var agents = await _dbContext.Agents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Department)
            .ThenBy(x => x.DisplayName)
            .Select(x => new AgentCardAgentProjection(
                x.Id,
                x.CompanyId,
                x.DisplayName,
                x.RoleName,
                x.Department,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        if (agents.Count == 0)
        {
            return new AgentStatusCardsResponseDto([], generatedUtc);
        }

        var agentIds = agents.Select(x => x.AgentId).ToArray();

        var taskAggregates = await _dbContext.WorkTasks
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AssignedAgentId.HasValue && agentIds.Contains(x.AssignedAgentId.Value))
            .GroupBy(x => x.AssignedAgentId!.Value)
            .Select(group => new AgentTaskAggregate(
                group.Key,
                group.Count(x => x.Status == WorkTaskStatus.New ||
                                 x.Status == WorkTaskStatus.InProgress ||
                                 x.Status == WorkTaskStatus.Blocked ||
                                 x.Status == WorkTaskStatus.AwaitingApproval),
                group.Count(x => x.Status == WorkTaskStatus.Blocked),
                group.Count(x => x.Status == WorkTaskStatus.AwaitingApproval),
                group.Count(x => x.Status == WorkTaskStatus.Failed && x.UpdatedUtc >= recentSignalCutoffUtc),
                group.Count(x =>
                    (x.Status == WorkTaskStatus.InProgress ||
                     x.Status == WorkTaskStatus.Blocked ||
                     x.Status == WorkTaskStatus.AwaitingApproval) &&
                    x.UpdatedUtc <= stalledCutoffUtc),
                group.Max(x => x.UpdatedUtc)))
            .ToListAsync(cancellationToken);

        var taskByAgentId = taskAggregates.ToDictionary(x => x.AgentId);

        var workflowRows = await _dbContext.WorkTasks
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.AssignedAgentId.HasValue &&
                agentIds.Contains(x.AssignedAgentId.Value) &&
                x.WorkflowInstanceId.HasValue &&
                x.WorkflowInstance != null &&
                (x.WorkflowInstance.State == WorkflowInstanceStatus.Started ||
                 x.WorkflowInstance.State == WorkflowInstanceStatus.Running ||
                 x.WorkflowInstance.State == WorkflowInstanceStatus.Blocked))
            .Select(x => new
            {
                AgentId = x.AssignedAgentId!.Value,
                WorkflowInstanceId = x.WorkflowInstanceId!.Value
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        var workflowCountByAgentId = workflowRows
            .GroupBy(x => x.AgentId)
            .ToDictionary(x => x.Key, x => x.Count());

        var workflowFailureRows = await _dbContext.WorkTasks
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.AssignedAgentId.HasValue &&
                agentIds.Contains(x.AssignedAgentId.Value) &&
                x.WorkflowInstanceId.HasValue &&
                x.WorkflowInstance != null &&
                x.WorkflowInstance.State == WorkflowInstanceStatus.Failed &&
                x.WorkflowInstance.UpdatedUtc >= recentSignalCutoffUtc)
            .Select(x => new
            {
                AgentId = x.AssignedAgentId!.Value,
                WorkflowInstanceId = x.WorkflowInstanceId!.Value,
                x.WorkflowInstance.UpdatedUtc
            })
            .ToListAsync(cancellationToken);

        var workflowFailuresByAgentId = workflowFailureRows
            .GroupBy(x => new { x.AgentId, x.WorkflowInstanceId })
            .Select(group => new
            {
                group.Key.AgentId,
                LastUpdatedUtc = group.Max(x => x.UpdatedUtc)
            })
            .GroupBy(x => x.AgentId)
            .ToDictionary(
                x => x.Key,
                x => new AgentWorkflowFailureAggregate(
                    x.Count(),
                    x.Max(row => row.LastUpdatedUtc)));

        var executionAggregates = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && agentIds.Contains(x.AgentId))
            .GroupBy(x => x.AgentId)
            .Select(group => new AgentExecutionStatusAggregate(
                group.Key,
                group.Count(x => x.Status == ToolExecutionStatus.Failed && x.UpdatedUtc >= recentSignalCutoffUtc),
                group.Count(x => x.Status == ToolExecutionStatus.Denied && x.UpdatedUtc >= recentSignalCutoffUtc),
                group.Count(x => x.Status == ToolExecutionStatus.AwaitingApproval),
                group.Max(x => x.UpdatedUtc)))
            .ToListAsync(cancellationToken);

        var executionByAgentId = executionAggregates.ToDictionary(x => x.AgentId);

        var approvalAggregates = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && agentIds.Contains(x.AgentId))
            .GroupBy(x => x.AgentId)
            .Select(group => new AgentApprovalStatusAggregate(
                group.Key,
                group.Count(x => x.Status == ApprovalRequestStatus.Pending),
                group.Max(x => x.UpdatedUtc)))
            .ToListAsync(cancellationToken);

        var approvalByAgentId = approvalAggregates.ToDictionary(x => x.AgentId);

        var alertAggregates = await _dbContext.Alerts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.SourceAgentId.HasValue &&
                agentIds.Contains(x.SourceAgentId.Value) &&
                (x.Status == AlertStatus.Open || x.Status == AlertStatus.Acknowledged))
            .GroupBy(x => x.SourceAgentId!.Value)
            .Select(group => new AgentAlertAggregate(
                group.Key,
                group.Count(),
                group.Max(x => x.UpdatedUtc)))
            .ToListAsync(cancellationToken);

        var alertByAgentId = alertAggregates.ToDictionary(x => x.AgentId);

        var recentActions = await LoadRecentActionsAsync(companyId, agentIds, cancellationToken);
        var recentActionsByAgentId = recentActions
            .GroupBy(x => x.AgentId)
            .ToDictionary(
                x => x.Key,
                x => (IReadOnlyList<AgentStatusRecentActionDto>)x
                    .OrderByDescending(action => action.OccurredUtc)
                    .ThenBy(action => action.ActionType, StringComparer.Ordinal)
                    .ThenBy(action => action.RelatedEntityId?.ToString("N") ?? string.Empty, StringComparer.Ordinal)
                    .Take(5)
                    .Select(action => action.Dto)
                    .ToList());

        var cards = agents.Select(agent =>
        {
            taskByAgentId.TryGetValue(agent.AgentId, out var task);
            executionByAgentId.TryGetValue(agent.AgentId, out var execution);
            approvalByAgentId.TryGetValue(agent.AgentId, out var approval);
            alertByAgentId.TryGetValue(agent.AgentId, out var alert);
            workflowCountByAgentId.TryGetValue(agent.AgentId, out var activeWorkflowCount);
            workflowFailuresByAgentId.TryGetValue(agent.AgentId, out var failedWorkflow);
            recentActionsByAgentId.TryGetValue(agent.AgentId, out var actions);

            var awaitingApprovalCount = Math.Max(approval?.PendingApprovalCount ?? 0, task?.AwaitingApprovalTaskCount ?? 0) +
                (execution?.AwaitingApprovalExecutionCount ?? 0);
            var activeTaskCount = task?.ActiveTaskCount ?? 0;
            var blockedTaskCount = task?.BlockedTaskCount ?? 0;
            var failedWorkflowCount = failedWorkflow?.FailedWorkflowCount ?? 0;
            var failedRunCount = (task?.FailedTaskCount ?? 0) + (execution?.FailedExecutionCount ?? 0) + failedWorkflowCount;
            var stalledWorkCount = task?.StalledWorkCount ?? 0;
            var policyViolationCount = execution?.DeniedExecutionCount ?? 0;
            var health = AgentStatusHealthCalculator.Calculate(new AgentStatusHealthMetrics(
                failedRunCount,
                stalledWorkCount,
                policyViolationCount));

            // Active alerts include first-class alerts plus actionable exception states until a dedicated alert exists for every source.
            var activeAlertsCount = (alert?.ActiveAlertCount ?? 0) +
                awaitingApprovalCount +
                blockedTaskCount +
                failedRunCount +
                policyViolationCount;
            var lastUpdatedUtc = MaxUtc(
                agent.LastUpdatedUtc,
                task?.LastUpdatedUtc,
                execution?.LastUpdatedUtc,
                approval?.LastUpdatedUtc,
                alert?.LastUpdatedUtc,
                failedWorkflow?.LastUpdatedUtc,
                actions?.FirstOrDefault()?.OccurredUtc);

            return new AgentStatusCardDto(
                agent.AgentId,
                agent.CompanyId,
                agent.DisplayName,
                agent.RoleName,
                agent.Department,
                new AgentStatusWorkloadDto(
                    activeTaskCount,
                    blockedTaskCount,
                    awaitingApprovalCount,
                    activeWorkflowCount,
                    ResolveWorkloadLevel(activeTaskCount, blockedTaskCount, awaitingApprovalCount, activeWorkflowCount)),
                health.Status,
                health.Reasons,
                activeAlertsCount,
                actions ?? [],
                lastUpdatedUtc,
                BuildDetailLink(agent.CompanyId, agent.AgentId));
        }).ToList();

        return new AgentStatusCardsResponseDto(cards, generatedUtc);
    }

    public async Task<AgentStatusDetailDto> GetStatusDetailAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var generatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var recentSignalCutoffUtc = generatedUtc - RecentSignalWindow;
        var stalledCutoffUtc = generatedUtc - StalledWorkThreshold;

        var agent = await _dbContext.Agents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == agentId)
            .Select(x => new AgentCardAgentProjection(
                x.Id,
                x.CompanyId,
                x.DisplayName,
                x.RoleName,
                x.Department,
                x.UpdatedUtc))
            .SingleOrDefaultAsync(cancellationToken);

        if (agent is null)
        {
            throw new KeyNotFoundException("Agent not found.");
        }

        var activeTasks = await _dbContext.WorkTasks
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.AssignedAgentId == agentId &&
                (x.Status == WorkTaskStatus.New ||
                 x.Status == WorkTaskStatus.InProgress ||
                 x.Status == WorkTaskStatus.Blocked ||
                 x.Status == WorkTaskStatus.AwaitingApproval))
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new AgentStatusDetailTaskDto(
                x.Id,
                x.Type,
                x.Title,
                x.Priority.ToStorageValue(),
                x.Status.ToStorageValue(),
                x.DueUtc,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        var activeWorkflows = await _dbContext.WorkTasks
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.AssignedAgentId == agentId &&
                x.WorkflowInstanceId.HasValue &&
                x.WorkflowInstance != null &&
                (x.WorkflowInstance.State == WorkflowInstanceStatus.Started ||
                 x.WorkflowInstance.State == WorkflowInstanceStatus.Running ||
                 x.WorkflowInstance.State == WorkflowInstanceStatus.Blocked))
            .Select(x => x.WorkflowInstance!)
            .Distinct()
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new AgentStatusDetailWorkflowDto(
                x.Id,
                x.Definition.Name,
                x.State.ToStorageValue(),
                x.CurrentStep,
                x.StartedUtc,
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        var activeAlerts = await _dbContext.Alerts
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.SourceAgentId == agentId &&
                (x.Status == AlertStatus.Open || x.Status == AlertStatus.Acknowledged))
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new AgentStatusDetailAlertDto(
                x.Id,
                x.Type.ToStorageValue(),
                x.Severity.ToStorageValue(),
                x.Title,
                x.Summary,
                x.Status.ToStorageValue(),
                x.UpdatedUtc))
            .ToListAsync(cancellationToken);

        var failedRunCount = await CountFailedRunsAsync(companyId, agentId, recentSignalCutoffUtc, cancellationToken);
        var stalledWorkCount = activeTasks.Count(x =>
            (string.Equals(x.Status, WorkTaskStatus.InProgress.ToStorageValue(), StringComparison.OrdinalIgnoreCase) ||
             string.Equals(x.Status, WorkTaskStatus.Blocked.ToStorageValue(), StringComparison.OrdinalIgnoreCase) ||
             string.Equals(x.Status, WorkTaskStatus.AwaitingApproval.ToStorageValue(), StringComparison.OrdinalIgnoreCase)) &&
            x.UpdatedUtc <= stalledCutoffUtc);
        var policyViolationCount = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .CountAsync(x =>
                x.CompanyId == companyId &&
                x.AgentId == agentId &&
                x.Status == ToolExecutionStatus.Denied &&
                x.UpdatedUtc >= recentSignalCutoffUtc,
                cancellationToken);

        var awaitingApprovalCount = activeTasks.Count(x => string.Equals(x.Status, WorkTaskStatus.AwaitingApproval.ToStorageValue(), StringComparison.OrdinalIgnoreCase)) +
            await _dbContext.ApprovalRequests.AsNoTracking().CountAsync(x => x.CompanyId == companyId && x.AgentId == agentId && x.Status == ApprovalRequestStatus.Pending, cancellationToken) +
            await _dbContext.ToolExecutionAttempts.AsNoTracking().CountAsync(x => x.CompanyId == companyId && x.AgentId == agentId && x.Status == ToolExecutionStatus.AwaitingApproval, cancellationToken);
        var blockedTaskCount = activeTasks.Count(x => string.Equals(x.Status, WorkTaskStatus.Blocked.ToStorageValue(), StringComparison.OrdinalIgnoreCase));
        var health = AgentStatusHealthCalculator.Calculate(new AgentStatusHealthMetrics(failedRunCount, stalledWorkCount, policyViolationCount));
        var recentActions = (await LoadRecentActionsAsync(companyId, [agentId], cancellationToken))
            .OrderByDescending(x => x.OccurredUtc)
            .ThenBy(x => x.ActionType, StringComparer.Ordinal)
            .ThenBy(x => x.RelatedEntityId?.ToString("N") ?? string.Empty, StringComparer.Ordinal)
            .Take(5)
            .Select(x => x.Dto)
            .ToList();

        return new AgentStatusDetailDto(
            agent.AgentId,
            agent.CompanyId,
            agent.DisplayName,
            agent.RoleName,
            agent.Department,
            new AgentStatusWorkloadDto(activeTasks.Count, blockedTaskCount, awaitingApprovalCount, activeWorkflows.Count, ResolveWorkloadLevel(activeTasks.Count, blockedTaskCount, awaitingApprovalCount, activeWorkflows.Count)),
            new AgentStatusHealthBreakdownDto(health.Status, health.Reasons, new AgentStatusHealthMetrics(failedRunCount, stalledWorkCount, policyViolationCount)),
            activeAlerts.Count + awaitingApprovalCount + blockedTaskCount + failedRunCount + policyViolationCount,
            activeTasks,
            activeWorkflows,
            activeAlerts,
            recentActions,
            MaxUtc(agent.LastUpdatedUtc, activeTasks.FirstOrDefault()?.UpdatedUtc, activeWorkflows.FirstOrDefault()?.UpdatedUtc, activeAlerts.FirstOrDefault()?.UpdatedUtc, recentActions.FirstOrDefault()?.OccurredUtc),
            generatedUtc);
    }

    private async Task RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _companyMembershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }
    }

    private async Task<int> CountFailedRunsAsync(Guid companyId, Guid agentId, DateTime recentSignalCutoffUtc, CancellationToken cancellationToken)
    {
        var failedTasks = await _dbContext.WorkTasks
            .AsNoTracking()
            .CountAsync(x =>
                x.CompanyId == companyId &&
                x.AssignedAgentId == agentId &&
                x.Status == WorkTaskStatus.Failed &&
                x.UpdatedUtc >= recentSignalCutoffUtc,
                cancellationToken);

        var failedExecutions = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .CountAsync(x =>
                x.CompanyId == companyId &&
                x.AgentId == agentId &&
                x.Status == ToolExecutionStatus.Failed &&
                x.UpdatedUtc >= recentSignalCutoffUtc,
                cancellationToken);

        var failedWorkflows = await _dbContext.WorkTasks
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.AssignedAgentId == agentId &&
                x.WorkflowInstanceId.HasValue &&
                x.WorkflowInstance != null &&
                x.WorkflowInstance.State == WorkflowInstanceStatus.Failed &&
                x.WorkflowInstance.UpdatedUtc >= recentSignalCutoffUtc)
            .Select(x => x.WorkflowInstanceId!.Value)
            .Distinct()
            .CountAsync(cancellationToken);

        return failedTasks + failedExecutions + failedWorkflows;
    }

    private async Task<IReadOnlyList<RecentActionProjection>> LoadRecentActionsAsync(
        Guid companyId,
        Guid[] agentIds,
        CancellationToken cancellationToken)
    {
        var taskActions = await _dbContext.WorkTasks
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AssignedAgentId.HasValue && agentIds.Contains(x.AssignedAgentId.Value))
            .Select(x => new RecentActionProjection(
                x.AssignedAgentId!.Value,
                x.UpdatedUtc,
                "task",
                x.Title,
                x.Status.ToStorageValue(),
                "task",
                x.Id))
            .ToListAsync(cancellationToken);

        var executionActions = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && agentIds.Contains(x.AgentId))
            .Select(x => new RecentActionProjection(
                x.AgentId,
                x.ExecutedUtc ?? x.CompletedUtc ?? x.UpdatedUtc,
                "tool_execution",
                x.ToolName,
                x.Status.ToStorageValue(),
                "tool_execution",
                x.Id))
            .ToListAsync(cancellationToken);

        var approvalActions = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && agentIds.Contains(x.AgentId))
            .Select(x => new RecentActionProjection(
                x.AgentId,
                x.UpdatedUtc,
                "approval",
                x.ToolName + " approval",
                x.Status.ToStorageValue(),
                "approval",
                x.Id))
            .ToListAsync(cancellationToken);

        var auditActions = await _dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.RelatedAgentId.HasValue && agentIds.Contains(x.RelatedAgentId.Value))
            .Select(x => new RecentActionProjection(
                x.RelatedAgentId!.Value,
                x.OccurredUtc,
                "audit_event",
                x.Action,
                x.Outcome,
                x.TargetType,
                x.RelatedTaskId ?? x.RelatedWorkflowInstanceId ?? x.RelatedApprovalRequestId ?? x.RelatedToolExecutionAttemptId ?? x.RelatedAgentId))
            .ToListAsync(cancellationToken);

        return taskActions
            .Concat(executionActions)
            .Concat(approvalActions)
            .Concat(auditActions)
            .ToList();
    }

    private static string ResolveWorkloadLevel(int activeTaskCount, int blockedTaskCount, int awaitingApprovalCount, int activeWorkflowCount)
    {
        if (blockedTaskCount > 0 || awaitingApprovalCount > 0)
        {
            return "Blocked";
        }

        var totalActiveWork = activeTaskCount + activeWorkflowCount;
        return totalActiveWork switch
        {
            0 => "Idle",
            <= 3 => "Normal",
            <= 7 => "High",
            _ => "Overloaded"
        };
    }

    private static AgentStatusDetailLinkDto BuildDetailLink(Guid companyId, Guid agentId) =>
        new(
            $"/agents/{agentId}",
            "work",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["companyId"] = companyId.ToString("D"),
                ["show"] = "active",
                ["include"] = "tasks,workflows,alerts"
            });

    private static DateTime MaxUtc(params DateTime?[] values)
    {
        var max = DateTime.MinValue;

        foreach (var value in values)
        {
            if (value.HasValue && value.Value > max)
            {
                max = value.Value;
            }
        }

        return max;
    }

    private sealed record AgentCardAgentProjection(Guid AgentId, Guid CompanyId, string DisplayName, string RoleName, string Department, DateTime LastUpdatedUtc);
    private sealed record AgentTaskAggregate(Guid AgentId, int ActiveTaskCount, int BlockedTaskCount, int AwaitingApprovalTaskCount, int FailedTaskCount, int StalledWorkCount, DateTime LastUpdatedUtc);
    private sealed record AgentWorkflowFailureAggregate(int FailedWorkflowCount, DateTime LastUpdatedUtc);
    private sealed record AgentExecutionStatusAggregate(
        Guid AgentId,
        int FailedExecutionCount,
        int DeniedExecutionCount,
        int AwaitingApprovalExecutionCount,
        DateTime LastUpdatedUtc);

    private sealed record AgentApprovalStatusAggregate(Guid AgentId, int PendingApprovalCount, DateTime LastUpdatedUtc);
    private sealed record AgentAlertAggregate(Guid AgentId, int ActiveAlertCount, DateTime LastUpdatedUtc);

    private sealed record RecentActionProjection(
        Guid AgentId,
        DateTime OccurredUtc,
        string ActionType,
        string Title,
        string Status,
        string RelatedEntityType,
        Guid? RelatedEntityId)
    {
        public AgentStatusRecentActionDto Dto => new(OccurredUtc, ActionType, Title, Status, RelatedEntityType, RelatedEntityId);
    }
}
