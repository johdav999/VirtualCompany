using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Events;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyTaskService : ICompanyTaskService, ICompanyTaskQueryService
{
    private const int TypeMaxLength = 100;
    private const int TitleMaxLength = 200;
    private const int DescriptionMaxLength = 4000;
    private const int RationaleSummaryMaxLength = 2000;
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly IAgentAssignmentGuard _agentAssignmentGuard;
    private readonly IExecutiveCockpitDashboardCache _dashboardCache;
    private readonly ICompanyOutboxEnqueuer _outboxEnqueuer;

    public CompanyTaskService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        IAgentAssignmentGuard agentAssignmentGuard,
        IExecutiveCockpitDashboardCache dashboardCache,
        ICompanyOutboxEnqueuer outboxEnqueuer)
    {
        _dbContext = dbContext;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _outboxEnqueuer = outboxEnqueuer;
        _agentAssignmentGuard = agentAssignmentGuard;
        _dashboardCache = dashboardCache;
    }

    public async Task<TaskDetailDto> CreateTaskAsync(
        Guid companyId,
        CreateTaskCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        Validate(command);

        var priority = ResolvePriority(command.Priority);
        if (command.AssignedAgentId.HasValue)
        {
            await _agentAssignmentGuard.EnsureAgentCanReceiveNewTasksAsync(
                companyId,
                command.AssignedAgentId.Value,
                nameof(command.AssignedAgentId),
                cancellationToken);
        }

        var parentTask = command.ParentTaskId.HasValue
            ? await GetParentTaskAsync(companyId, command.ParentTaskId.Value, cancellationToken)
            : null;
        var workflowInstanceId = command.WorkflowInstanceId ?? parentTask?.WorkflowInstanceId;
        if (parentTask is not null &&
            string.Equals(parentTask.Type, MultiAgentCollaborationTaskTypes.WorkerSubtask, StringComparison.OrdinalIgnoreCase))
        {
            throw new TaskValidationException(
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(command.ParentTaskId)] = ["Worker subtasks cannot create additional subtasks outside an approved manager-worker plan."]
                });
        }

        if (workflowInstanceId.HasValue)
        {
            await EnsureWorkflowInstanceExistsAsync(companyId, workflowInstanceId.Value, cancellationToken);
        }

        var task = new WorkTask(
            Guid.NewGuid(),
            companyId,
            command.Type,
            command.Title,
            command.Description,
            priority,
            command.AssignedAgentId,
            command.ParentTaskId,
            "user",
            membership.UserId,
            command.InputPayload,
            workflowInstanceId,
            command.OutputPayload,
            command.RationaleSummary,
            command.ConfidenceScore,
            command.CorrelationId);
        task.SetDueDate(command.DueAt);

        _dbContext.WorkTasks.Add(task);
        await _dbContext.SaveChangesAsync(cancellationToken);
        EnqueueTaskPlatformEvent(task, SupportedPlatformEventTypeRegistry.TaskCreated, task.CreatedUtc, task.CorrelationId);
        await _dashboardCache.InvalidateAsync(companyId, cancellationToken);

        return await GetByIdAsync(companyId, task.Id, cancellationToken);
    }

    public async Task<TaskDetailDto> CreateSubtaskAsync(
        Guid companyId,
        Guid parentTaskId,
        CreateSubtaskCommand command,
        CancellationToken cancellationToken)
    {
        var createCommand = new CreateTaskCommand(
            command.Type,
            command.Title,
            command.Description,
            command.Priority,
            command.DueAt,
            command.AssignedAgentId,
            command.InputPayload,
            parentTaskId,
            command.WorkflowInstanceId,
            command.OutputPayload,
            command.RationaleSummary,
            command.ConfidenceScore,
            command.CorrelationId);

        return await CreateTaskAsync(companyId, createCommand, cancellationToken);
    }

    public async Task<TaskDetailDto> UpdateStatusAsync(
        Guid companyId,
        Guid taskId,
        UpdateTaskStatusCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        Validate(command);

        var task = await _dbContext.WorkTasks
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == taskId, cancellationToken);

        if (task is null)
        {
            throw new KeyNotFoundException("Task not found.");
        }

        var status = WorkTaskStatusValues.Parse(command.Status);
        task.UpdateStatus(
            status,
            command.OutputPayload,
            command.RationaleSummary,
            command.ConfidenceScore);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _dashboardCache.InvalidateAsync(companyId, cancellationToken);
        EnqueueTaskPlatformEvent(task, SupportedPlatformEventTypeRegistry.TaskUpdated, task.UpdatedUtc, task.CorrelationId);
        return await GetByIdAsync(companyId, task.Id, cancellationToken);
    }

    public async Task<TaskDetailDto> ReassignAsync(
        Guid companyId,
        Guid taskId,
        ReassignTaskCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        if (command.AssignedAgentId.HasValue)
        {
            await _agentAssignmentGuard.EnsureAgentCanReceiveNewTasksAsync(
                companyId,
                command.AssignedAgentId.Value,
                nameof(command.AssignedAgentId),
                cancellationToken);
        }

        var task = await _dbContext.WorkTasks
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == taskId, cancellationToken);

        if (task is null)
        {
            throw new KeyNotFoundException("Task not found.");
        }

        task.AssignTo(command.AssignedAgentId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _dashboardCache.InvalidateAsync(companyId, cancellationToken);

        return await GetByIdAsync(companyId, task.Id, cancellationToken);
    }

    public Task<TaskDetailDto> GetByIdAsync(
        Guid companyId,
        GetTaskByIdQuery query,
        CancellationToken cancellationToken) =>
        GetByIdAsync(companyId, query.TaskId, cancellationToken);

    public Task<TaskListResultDto> ListAsync(
        Guid companyId,
        ListTasksQuery query,
        CancellationToken cancellationToken) =>
        ListAsync(
            companyId,
            new TaskListFilterDto(
                query.Status, query.AssignedAgentId, query.ParentTaskId,
                query.DueBefore, query.DueAfter, query.Skip, query.Take),
            cancellationToken);

    public async Task<TaskDetailDto> GetByIdAsync(
        Guid companyId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var task = await _dbContext.WorkTasks
            .AsNoTracking()
            .Include(x => x.AssignedAgent)
            .Include(x => x.ParentTask)
            .Include(x => x.Subtasks)
                .ThenInclude(x => x.AssignedAgent)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == taskId, cancellationToken);

        return task is null
            ? throw new KeyNotFoundException("Task not found.")
            : ToDetailDto(task);
    }

    public async Task<TaskListResultDto> ListAsync(
        Guid companyId,
        TaskListFilterDto filter,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        Validate(filter);

        var query = _dbContext.WorkTasks
            .AsNoTracking()
            .Include(x => x.AssignedAgent)
            .Where(x => x.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            var status = WorkTaskStatusValues.Parse(filter.Status);
            query = query.Where(x => x.Status == status);
        }

        if (filter.AssignedAgentId.HasValue)
        {
            query = query.Where(x => x.AssignedAgentId == filter.AssignedAgentId.Value);
        }

        if (filter.ParentTaskId.HasValue)
        {
            query = query.Where(x => x.ParentTaskId == filter.ParentTaskId.Value);
        }

        if (filter.DueBefore.HasValue)
        {
            query = query.Where(x => x.DueUtc <= filter.DueBefore.Value);
        }

        if (filter.DueAfter.HasValue)
        {
            query = query.Where(x => x.DueUtc >= filter.DueAfter.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var skip = Math.Max(0, filter.Skip ?? 0);
        var take = Math.Clamp(filter.Take ?? DefaultPageSize, 1, MaxPageSize);
        var tasks = await query
            .OrderByDescending(x => x.CreatedUtc)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new TaskListResultDto(
            tasks.Select(ToListItemDto).ToList(),
            totalCount,
            skip,
            take);
    }

    private async Task<ResolvedCompanyMembershipContext> RequireMembershipAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var membership = await _companyMembershipContextResolver.ResolveAsync(companyId, cancellationToken);
        return membership ?? throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
    }

    private async Task<WorkTask> GetParentTaskAsync(
        Guid companyId,
        Guid parentTaskId,
        CancellationToken cancellationToken)
    {
        var parentTask = await _dbContext.WorkTasks
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == parentTaskId, cancellationToken);

        if (parentTask is not null)
        {
            return parentTask;
        }

        throw new TaskValidationException(
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(CreateTaskCommand.ParentTaskId)] = ["ParentTaskId must reference a task in the same company."]
            });
    }

    private async Task EnsureWorkflowInstanceExistsAsync(
        Guid companyId,
        Guid workflowInstanceId,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.WorkflowInstances
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == workflowInstanceId, cancellationToken);

        if (!exists)
        {
            throw new TaskValidationException(
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(CreateTaskCommand.WorkflowInstanceId)] = ["WorkflowInstanceId must reference a workflow instance in the same company."]
                });
        }
    }

    private void EnqueueTaskPlatformEvent(
        WorkTask task,
        string eventType,
        DateTime occurredAtUtc,
        string? correlationId)
    {
        var eventId = eventType == SupportedPlatformEventTypeRegistry.TaskCreated
            ? $"{eventType}:{task.Id:N}"
            : $"{eventType}:{task.Id:N}:{occurredAtUtc:yyyyMMddHHmmssfffffff}";
        var effectiveCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? eventId
            : correlationId.Trim();

        _outboxEnqueuer.Enqueue(
            task.CompanyId,
            eventType,
            new PlatformEventEnvelope(
                eventId,
                eventType,
                occurredAtUtc.Kind == DateTimeKind.Utc ? occurredAtUtc : occurredAtUtc.ToUniversalTime(),
                task.CompanyId,
                effectiveCorrelationId,
                "work_task",
                task.Id.ToString("N"),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["taskId"] = JsonValue.Create(task.Id.ToString("N")),
                    ["title"] = JsonValue.Create(task.Title),
                    ["type"] = JsonValue.Create(task.Type),
                    ["status"] = JsonValue.Create(task.Status.ToStorageValue()),
                    ["priority"] = JsonValue.Create(task.Priority.ToStorageValue()),
                    ["assignedAgentId"] = task.AssignedAgentId.HasValue ? JsonValue.Create(task.AssignedAgentId.Value.ToString("N")) : null,
                    ["parentTaskId"] = task.ParentTaskId.HasValue ? JsonValue.Create(task.ParentTaskId.Value.ToString("N")) : null,
                    ["workflowInstanceId"] = task.WorkflowInstanceId.HasValue ? JsonValue.Create(task.WorkflowInstanceId.Value.ToString("N")) : null
                }),
            effectiveCorrelationId,
            idempotencyKey: $"platform-event:{task.CompanyId:N}:{eventId}",
            causationId: task.Id.ToString("N"));
    }

    private static WorkTaskPriority ResolvePriority(string? priority) =>
        string.IsNullOrWhiteSpace(priority)
            ? WorkTaskPriorityValues.DefaultPriority
            : WorkTaskPriorityValues.Parse(priority);

    private static TaskDetailDto ToDetailDto(WorkTask task) =>
        new(
            task.Id,
            task.CompanyId,
            task.Type,
            task.Title,
            task.Description,
            task.Priority.ToStorageValue(),
            task.Status.ToStorageValue(),
            task.DueUtc,
            task.AssignedAgentId,
            task.ParentTaskId,
            task.WorkflowInstanceId,
            task.CreatedByActorType,
            task.SourceType,
            task.OriginatingAgentId,
            task.TriggerSource,
            task.CreationReason,
            task.TriggerEventId,
            task.CreatedByActorId,
            CloneNodes(task.InputPayload),
            CloneNodes(task.OutputPayload),
            task.RationaleSummary,
            task.ConfidenceScore,
            task.CreatedUtc,
            task.UpdatedUtc,
            task.CompletedUtc,
            task.AssignedAgent is null ? null : ToAgentSummaryDto(task.AssignedAgent),
            task.ParentTask is null ? null : new TaskParentSummaryDto(task.ParentTask.Id, task.ParentTask.Title, task.ParentTask.Status.ToStorageValue()),
            task.CorrelationId,
            task.Subtasks
                .OrderBy(x => x.CreatedUtc)
                .Select(ToSubtaskSummaryDto)
                .ToList());

    private static TaskSubtaskSummaryDto ToSubtaskSummaryDto(WorkTask task) =>
        new(
            task.Id,
            task.CompanyId,
            task.Type,
            task.Title,
            task.Priority.ToStorageValue(),
            task.Status.ToStorageValue(),
            task.DueUtc,
            task.AssignedAgentId,
            task.ParentTaskId,
            task.WorkflowInstanceId,
            task.CreatedUtc,
            task.UpdatedUtc,
            task.CompletedUtc,
            task.AssignedAgent is null ? null : ToAgentSummaryDto(task.AssignedAgent));

    private static TaskListItemDto ToListItemDto(WorkTask task) =>
        new(
            task.Id,
            task.CompanyId,
            task.Type,
            task.Title,
            task.Priority.ToStorageValue(),
            task.Status.ToStorageValue(),
            task.DueUtc,
            task.AssignedAgentId,
            task.ParentTaskId,
            task.CreatedUtc,
            task.UpdatedUtc,
            task.AssignedAgent is null ? null : ToAgentSummaryDto(task.AssignedAgent));

    private static TaskAgentSummaryDto ToAgentSummaryDto(Agent agent) =>
        new(agent.Id, agent.DisplayName, agent.Status.ToStorageValue());

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?> nodes) =>
        nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static void Validate(CreateTaskCommand command)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        AddRequired(errors, nameof(command.Type), command.Type, TypeMaxLength);
        AddRequired(errors, nameof(command.Title), command.Title, TitleMaxLength);
        AddOptional(errors, nameof(command.Description), command.Description, DescriptionMaxLength);
        AddOptional(errors, nameof(command.RationaleSummary), command.RationaleSummary, RationaleSummaryMaxLength);

        if (command.ConfidenceScore is < 0 or > 1)
        {
            AddError(errors, nameof(command.ConfidenceScore), "ConfidenceScore must be between 0 and 1.");
        }

        if (!string.IsNullOrWhiteSpace(command.Priority) && !WorkTaskPriorityValues.TryParse(command.Priority, out _))
        {
            AddError(errors, nameof(command.Priority), WorkTaskPriorityValues.BuildValidationMessage(command.Priority));
        }

        ThrowIfInvalid(errors);
    }

    private static void Validate(UpdateTaskStatusCommand command)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (!WorkTaskStatusValues.TryParse(command.Status, out _))
        {
            AddError(errors, nameof(command.Status), WorkTaskStatusValues.BuildValidationMessage(command.Status));
        }

        AddOptional(errors, nameof(command.RationaleSummary), command.RationaleSummary, RationaleSummaryMaxLength);

        if (command.ConfidenceScore is < 0 or > 1)
        {
            AddError(errors, nameof(command.ConfidenceScore), "ConfidenceScore must be between 0 and 1.");
        }

        ThrowIfInvalid(errors);
    }

    private static void Validate(TaskListFilterDto filter)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(filter.Status) && !WorkTaskStatusValues.TryParse(filter.Status, out _))
        {
            AddError(errors, nameof(filter.Status), WorkTaskStatusValues.BuildValidationMessage(filter.Status));
        }

        if (filter.Skip is < 0)
        {
            AddError(errors, nameof(filter.Skip), "Skip must be zero or greater.");
        }

        if (filter.Take is <= 0 or > MaxPageSize)
        {
            AddError(errors, nameof(filter.Take), $"Take must be between 1 and {MaxPageSize}.");
        }

        ThrowIfInvalid(errors);
    }

    private static void AddRequired(IDictionary<string, List<string>> errors, string key, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, key, $"{key} is required.");
            return;
        }

        AddOptional(errors, key, value, maxLength);
    }

    private static void AddOptional(IDictionary<string, List<string>> errors, string key, string? value, int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
        {
            AddError(errors, key, $"{key} must be {maxLength} characters or fewer.");
        }
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

    private static void ThrowIfInvalid(IDictionary<string, List<string>> errors)
    {
        if (errors.Count > 0)
        {
            throw new TaskValidationException(errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
        }
    }
}
