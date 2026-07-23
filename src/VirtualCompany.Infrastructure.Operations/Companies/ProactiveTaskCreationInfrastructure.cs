using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Events;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class DefaultTriggerToTaskMappingService : ITriggerToTaskMappingService
{
    public MappedTaskCreationRequest Map(ProactiveTaskTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var payload = CloneNodes(trigger.Payload);
        payload["triggerSource"] = JsonValue.Create(trigger.TriggerSource);
        payload["triggerEventKey"] = JsonValue.Create(trigger.TriggerEventId);
        payload["triggerEventId"] = JsonValue.Create(trigger.TriggerEventId);
        payload["correlationId"] = JsonValue.Create(trigger.CorrelationId);
        payload["sourceType"] = JsonValue.Create(WorkTaskSourceTypes.Agent);
        payload["creationReason"] = JsonValue.Create(trigger.CreationReason);
        payload["originatingAgentId"] = JsonValue.Create(trigger.AgentId.ToString("N"));

        var title = FirstNonBlank(
            trigger.TaskTitle,
            ReadString(payload, "title"),
            ReadString(payload, "taskTitle"),
            trigger.CreationReason);
        var description = FirstNonBlank(
            trigger.TaskDescription,
            ReadString(payload, "description"),
            ReadString(payload, "analysisSummary"),
            trigger.CreationReason);

        return new MappedTaskCreationRequest(
            trigger.CompanyId,
            trigger.AgentId,
            trigger.TriggerSource,
            trigger.TriggerEventId,
            trigger.CorrelationId,
            trigger.CreationReason,
            FirstNonBlank(trigger.TaskType, ReadString(payload, "taskType"), "proactive"),
            title,
            description,
            FirstNonBlank(trigger.TaskPriority, ReadString(payload, "priority"), WorkTaskPriority.Normal.ToStorageValue()),
            WorkTaskStatus.New.ToStorageValue(),
            trigger.DueAt,
            trigger.AssignedAgentId ?? trigger.AgentId,
            payload);
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string? ReadString(IReadOnlyDictionary<string, JsonNode?> payload, string key) =>
        payload.TryGetValue(key, out var node) &&
        node is JsonValue value &&
        value.TryGetValue<string>(out var text) &&
        !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class EfProactiveTaskDuplicateDetector : IProactiveTaskDuplicateDetector
{
    private readonly VirtualCompanyDbContext _dbContext;

    public EfProactiveTaskDuplicateDetector(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<WorkTaskDuplicateMatch?> FindDuplicateAsync(
        MappedTaskCreationRequest request,
        TimeSpan deduplicationWindow,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var dedupeKey = BuildDedupeKey(request);
        var receiptDuplicate = await (
            from receipt in _dbContext.AgentTaskCreationDedupeRecords.IgnoreQueryFilters().AsNoTracking()
            join task in _dbContext.WorkTasks.IgnoreQueryFilters().AsNoTracking()
                on receipt.TaskId equals task.Id
            where receipt.CompanyId == request.CompanyId &&
                receipt.DedupeKey == dedupeKey &&
                receipt.ExpiresUtc > nowUtc
            orderby receipt.CreatedUtc descending
            select new
            {
                task.Id,
                task.CompanyId,
                task.Status,
                CorrelationId = task.CorrelationId ?? receipt.CorrelationId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (receiptDuplicate is not null)
        {
            return new WorkTaskDuplicateMatch(
                receiptDuplicate.Id,
                receiptDuplicate.CompanyId,
                receiptDuplicate.Status.ToStorageValue(),
                receiptDuplicate.CorrelationId);
        }

        // Legacy fallback for tasks created before receipts existed, or if a receipt was pruned
        // while the task still falls inside the configured deduplication window.
        var windowStartUtc = nowUtc - deduplicationWindow;
        var duplicate = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == request.CompanyId &&
                x.SourceType == WorkTaskSourceTypes.Agent &&
                x.TriggerSource == request.TriggerSource &&
                x.TriggerEventId == request.TriggerEventId &&
                x.CorrelationId == request.CorrelationId &&
                x.CreatedUtc >= windowStartUtc)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return duplicate is null
            ? null
            : new WorkTaskDuplicateMatch(
                duplicate.Id,
                duplicate.CompanyId,
                duplicate.Status.ToStorageValue(),
                duplicate.CorrelationId ?? request.CorrelationId);
    }

    private static string BuildDedupeKey(MappedTaskCreationRequest request)
    {
        var material = string.Join(
            "|",
            request.CompanyId.ToString("N"),
            request.TriggerSource.Trim().ToUpperInvariant(),
            request.TriggerEventId.Trim().ToUpperInvariant(),
            request.CorrelationId.Trim().ToUpperInvariant());

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }
}

public sealed class ProactiveTaskCreationService : IProactiveTaskCreationService
{
    private const int TypeMaxLength = 100;
    private const int TitleMaxLength = 200;
    private const int DescriptionMaxLength = 4000;
    private const int RationaleSummaryMaxLength = 2000;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ITriggerToTaskMappingService _mapper;
    private readonly IProactiveTaskDuplicateDetector _duplicateDetector;
    private readonly IAgentAssignmentGuard _agentAssignmentGuard;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ICompanyOutboxEnqueuer _outboxEnqueuer;
    private readonly IExecutiveCockpitDashboardCache _dashboardCache;
    private readonly TimeProvider _timeProvider;
    private readonly ProactiveTaskCreationOptions _options;

    public ProactiveTaskCreationService(
        VirtualCompanyDbContext dbContext,
        ITriggerToTaskMappingService mapper,
        IProactiveTaskDuplicateDetector duplicateDetector,
        IAgentAssignmentGuard agentAssignmentGuard,
        IAuditEventWriter auditEventWriter,
        ICompanyOutboxEnqueuer outboxEnqueuer,
        IExecutiveCockpitDashboardCache dashboardCache,
        TimeProvider timeProvider,
        IOptions<ProactiveTaskCreationOptions> options)
    {
        _dbContext = dbContext;
        _mapper = mapper;
        _duplicateDetector = duplicateDetector;
        _agentAssignmentGuard = agentAssignmentGuard;
        _auditEventWriter = auditEventWriter;
        _outboxEnqueuer = outboxEnqueuer;
        _dashboardCache = dashboardCache;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    public async Task<CreateAgentInitiatedTaskResult> CreateAsync(
        CreateAgentInitiatedTaskCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Trigger);

        var request = _mapper.Map(command.Trigger);
        Validate(request);

        await EnsureAgentBelongsToTenantAsync(request.CompanyId, request.AgentId, nameof(request.AgentId), cancellationToken);
        await _agentAssignmentGuard.EnsureAgentCanReceiveNewTasksAsync(
            request.CompanyId,
            request.AssignedAgentId!.Value,
            nameof(request.AssignedAgentId),
            cancellationToken);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var deduplicationWindow = TimeSpan.FromSeconds(Math.Max(1, _options.DeduplicationWindowSeconds));
        var dedupeKey = BuildDedupeKey(request);
        var duplicate = await _duplicateDetector.FindDuplicateAsync(request, deduplicationWindow, nowUtc, cancellationToken);
        if (duplicate is not null)
        {
            return new CreateAgentInitiatedTaskResult(
                duplicate.TaskId,
                duplicate.CompanyId,
                Created: false,
                Duplicate: true,
                duplicate.Status,
                duplicate.CorrelationId);
        }

        var expiredDedupeRecords = await _dbContext.AgentTaskCreationDedupeRecords
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == request.CompanyId && x.DedupeKey == dedupeKey && x.ExpiresUtc <= nowUtc)
            .ToListAsync(cancellationToken);
        if (expiredDedupeRecords.Count > 0)
        {
            _dbContext.AgentTaskCreationDedupeRecords.RemoveRange(expiredDedupeRecords);
        }

        var task = new WorkTask(
            Guid.NewGuid(),
            request.CompanyId,
            request.Type,
            request.Title,
            request.Description,
            WorkTaskPriorityValues.Parse(request.Priority),
            request.AssignedAgentId,
            parentTaskId: null,
            AuditActorTypes.Agent,
            request.AgentId,
            request.InputPayload,
            workflowInstanceId: null,
            outputPayload: null,
            request.CreationReason,
            confidenceScore: null,
            request.CorrelationId,
            WorkTaskSourceTypes.Agent,
            request.AgentId,
            request.TriggerSource,
            request.CreationReason,
            request.TriggerEventId,
            WorkTaskStatusValues.Parse(request.Status));
        task.SetDueDate(request.DueAt);

        _dbContext.AgentTaskCreationDedupeRecords.Add(new AgentTaskCreationDedupeRecord(
            Guid.NewGuid(),
            request.CompanyId,
            dedupeKey,
            task.Id,
            request.AgentId,
            request.TriggerSource,
            request.TriggerEventId,
            request.CorrelationId,
            nowUtc,
            nowUtc.Add(deduplicationWindow)));
        _dbContext.WorkTasks.Add(task);
        await WriteAuditAsync(task, request, nowUtc, cancellationToken);
        EnqueueTaskPlatformEvent(task);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _dashboardCache.InvalidateAsync(request.CompanyId, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsAgentTaskDedupeUniqueViolation(ex))
        {
            _dbContext.ChangeTracker.Clear();
            duplicate = await _duplicateDetector.FindDuplicateAsync(request, deduplicationWindow, nowUtc, cancellationToken);
            if (duplicate is null)
            {
                throw;
            }

            return new CreateAgentInitiatedTaskResult(
                duplicate.TaskId,
                duplicate.CompanyId,
                Created: false,
                Duplicate: true,
                duplicate.Status,
                duplicate.CorrelationId);
        }

        return new CreateAgentInitiatedTaskResult(
            task.Id,
            task.CompanyId,
            Created: true,
            Duplicate: false,
            task.Status.ToStorageValue(),
            task.CorrelationId ?? request.CorrelationId);
    }

    private async Task EnsureAgentBelongsToTenantAsync(
        Guid companyId,
        Guid agentId,
        string fieldName,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken);
        if (!exists)
        {
            throw new TaskValidationException(
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [fieldName] = ["AgentId must reference an agent in the same company."]
                });
        }
    }

    private Task WriteAuditAsync(
        WorkTask task,
        MappedTaskCreationRequest request,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var diff = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["from"] = null,
            ["to"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["companyId"] = task.CompanyId.ToString("N"),
                ["agentId"] = request.AgentId.ToString("N"),
                ["taskId"] = task.Id.ToString("N"),
                ["title"] = task.Title,
                ["description"] = task.Description,
                ["priority"] = task.Priority.ToStorageValue(),
                ["status"] = task.Status.ToStorageValue(),
                ["assignedAgentId"] = task.AssignedAgentId?.ToString("N"),
                ["sourceType"] = task.SourceType,
                ["originatingAgentId"] = task.OriginatingAgentId?.ToString("N"),
                ["triggerSource"] = task.TriggerSource,
                ["triggerEventId"] = task.TriggerEventId,
                ["correlationId"] = task.CorrelationId,
                ["creationReason"] = task.CreationReason
            }
        });

        return _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                task.CompanyId,
                AuditActorTypes.Agent,
                request.AgentId,
                AuditEventActions.AgentInitiatedTaskCreated,
                AuditTargetTypes.WorkTask,
                task.Id.ToString("N"),
                AuditEventOutcomes.Succeeded,
                request.CreationReason,
                ["proactive_task_creation", request.TriggerSource],
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["companyId"] = task.CompanyId.ToString("N"),
                    ["agentId"] = request.AgentId.ToString("N"),
                    ["taskId"] = task.Id.ToString("N"),
                    ["triggerSource"] = request.TriggerSource,
                    ["triggerEventId"] = request.TriggerEventId,
                    ["correlationId"] = request.CorrelationId,
                    ["sourceType"] = WorkTaskSourceTypes.Agent
                },
                request.CorrelationId,
                nowUtc,
                PayloadDiffJson: diff),
            cancellationToken);
    }

    private void EnqueueTaskPlatformEvent(WorkTask task)
    {
        var eventId = $"{SupportedPlatformEventTypeRegistry.TaskCreated}:{task.Id:N}";
        _outboxEnqueuer.Enqueue(
            task.CompanyId,
            SupportedPlatformEventTypeRegistry.TaskCreated,
            new PlatformEventEnvelope(
                eventId,
                SupportedPlatformEventTypeRegistry.TaskCreated,
                task.CreatedUtc,
                task.CompanyId,
                task.CorrelationId ?? eventId,
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
                    ["sourceType"] = JsonValue.Create(task.SourceType),
                    ["triggerSource"] = JsonValue.Create(task.TriggerSource),
                    ["triggerEventId"] = JsonValue.Create(task.TriggerEventId)
                }),
            task.CorrelationId ?? eventId,
            idempotencyKey: $"platform-event:{task.CompanyId:N}:{eventId}",
            causationId: task.Id.ToString("N"));
    }

    private static void Validate(MappedTaskCreationRequest request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        AddRequired(errors, nameof(request.CompanyId), request.CompanyId);
        AddRequired(errors, nameof(request.AgentId), request.AgentId);
        AddRequired(errors, nameof(request.AssignedAgentId), request.AssignedAgentId);
        AddRequired(errors, nameof(request.TriggerSource), request.TriggerSource, 128);
        AddRequired(errors, nameof(request.TriggerEventId), request.TriggerEventId, 200);
        AddRequired(errors, nameof(request.CorrelationId), request.CorrelationId, 128);
        AddRequired(errors, nameof(request.CreationReason), request.CreationReason, RationaleSummaryMaxLength);
        AddRequired(errors, nameof(request.Type), request.Type, TypeMaxLength);
        AddRequired(errors, nameof(request.Title), request.Title, TitleMaxLength);
        AddRequired(errors, nameof(request.Description), request.Description, DescriptionMaxLength);

        if (!WorkTaskPriorityValues.TryParse(request.Priority, out _))
        {
            AddError(errors, nameof(request.Priority), WorkTaskPriorityValues.BuildValidationMessage(request.Priority));
        }

        if (!WorkTaskStatusValues.TryParse(request.Status, out _))
        {
            AddError(errors, nameof(request.Status), WorkTaskStatusValues.BuildValidationMessage(request.Status));
        }

        if (errors.Count > 0)
        {
            throw new TaskValidationException(errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
        }
    }

    private static void AddRequired(IDictionary<string, List<string>> errors, string key, Guid? value)
    {
        if (!value.HasValue || value.Value == Guid.Empty)
        {
            AddError(errors, key, $"{key} is required.");
        }
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

    private static string BuildDedupeKey(MappedTaskCreationRequest request)
    {
        var material = string.Join(
            "|",
            request.CompanyId.ToString("N"),
            request.TriggerSource.Trim().ToUpperInvariant(),
            request.TriggerEventId.Trim().ToUpperInvariant(),
            request.CorrelationId.Trim().ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static bool IsAgentTaskDedupeUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("agent_task_creation_dedupe", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("IX_agent_task_creation_dedupe_company_id_dedupe_key", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
