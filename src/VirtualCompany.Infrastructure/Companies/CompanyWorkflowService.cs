using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Events;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyWorkflowService : ICompanyWorkflowService, IWorkflowScheduleTriggerService, IInternalWorkflowEventTriggerService
{
    private const int CodeMaxLength = 100;
    private const int NameMaxLength = 200;
    private const int DepartmentMaxLength = 100;
    private const int EventNameMaxLength = 200;
    private const int TriggerRefMaxLength = 200;
    private const int CurrentStepMaxLength = 200;
    private const int ResolutionNotesMaxLength = 2000;
    private const string InstanceStepKey = "instance";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ILogger<CompanyWorkflowService> _logger;
    private readonly ICompanyOutboxEnqueuer _outboxEnqueuer;
    private readonly ISupportedPlatformEventTypeRegistry _supportedEventTypes;

    public CompanyWorkflowService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        IAuditEventWriter auditEventWriter,
        ICompanyOutboxEnqueuer outboxEnqueuer,
        ISupportedPlatformEventTypeRegistry supportedEventTypes,
        ILogger<CompanyWorkflowService> logger)
    {
        _dbContext = dbContext;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _auditEventWriter = auditEventWriter;
        _outboxEnqueuer = outboxEnqueuer;
        _supportedEventTypes = supportedEventTypes;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WorkflowCatalogItemDto>> ListCatalogAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        return PredefinedWorkflowCatalog.All
            .OrderBy(x => x.Name)
            .Select(x => new WorkflowCatalogItemDto(
                x.Code,
                x.Name,
                x.Description,
                x.Department,
                x.Version,
                x.TriggerType,
                x.SupportedStepHandlers,
                PredefinedWorkflowCatalog.CloneDefinitionJson(x.Code)))
            .ToList();
    }

    public async Task<WorkflowDefinitionDto> CreateDefinitionAsync(
        Guid companyId,
        CreateWorkflowDefinitionCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        Validate(command);

        var code = NormalizeCode(command.Code);
        var exists = await _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Code == code, cancellationToken);

        if (exists)
        {
            throw new WorkflowValidationException(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(command.Code)] = ["Code already has a workflow definition in this company. Create a new version instead."]
            });
        }

        var definition = new WorkflowDefinition(
            Guid.NewGuid(),
            companyId,
            code,
            command.Name,
            command.Department,
            WorkflowTriggerTypeValues.Parse(command.TriggerType),
            version: 1,
            command.DefinitionJson!,
            command.Active);

        _dbContext.WorkflowDefinitions.Add(definition);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetDefinitionAsync(companyId, definition.Id, cancellationToken);
    }

    public async Task<WorkflowDefinitionDto> CreateDefinitionVersionAsync(
        Guid companyId,
        Guid definitionId,
        CreateWorkflowDefinitionVersionCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var source = await _dbContext.WorkflowDefinitions
            .Include(x => x.Triggers)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == definitionId, cancellationToken);

        if (source is null)
        {
            throw new KeyNotFoundException("Workflow definition not found.");
        }

        Validate(command, source.Code);

        var latestVersion = await _dbContext.WorkflowDefinitions
            .Where(x => x.CompanyId == companyId && x.Code == source.Code)
            .MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0;

        if (command.Active)
        {
            var activeDefinitions = await _dbContext.WorkflowDefinitions
                .Where(x => x.CompanyId == companyId && x.Code == source.Code && x.Active)
                .ToListAsync(cancellationToken);

            foreach (var activeDefinition in activeDefinitions)
            {
                activeDefinition.SetActive(false);
            }
        }

        var definition = new WorkflowDefinition(
            Guid.NewGuid(),
            companyId,
            source.Code,
            string.IsNullOrWhiteSpace(command.Name) ? source.Name : command.Name,
            string.IsNullOrWhiteSpace(command.Department) ? source.Department : command.Department,
            WorkflowTriggerTypeValues.Parse(command.TriggerType),
            latestVersion + 1,
            command.DefinitionJson!,
            command.Active);

        foreach (var trigger in source.Triggers)
        {
            _dbContext.WorkflowTriggers.Add(new WorkflowTrigger(
                Guid.NewGuid(),
                companyId,
                definition.Id,
                trigger.EventName,
                trigger.CriteriaJson,
                trigger.IsEnabled));
        }

        _dbContext.WorkflowDefinitions.Add(definition);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetDefinitionAsync(companyId, definition.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowDefinitionDto>> ListDefinitionsAsync(
        Guid companyId,
        bool activeOnly,
        bool latestOnly,
        bool includeSystem,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var query = _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .Include(x => x.Triggers)
            .Where(x => x.CompanyId == companyId || (includeSystem && x.CompanyId == null));

        if (activeOnly)
        {
            query = query.Where(x => x.Active);
        }

        var definitions = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .ThenByDescending(x => x.Version)
            .ToListAsync(cancellationToken);

        if (latestOnly)
        {
            definitions = definitions
                .GroupBy(x => new { x.CompanyId, x.Code })
                .Select(group => group.OrderByDescending(x => x.Version).First())
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Code)
                .ToList();
        }

        return definitions.Select(ToDefinitionDto).ToList();
    }

    public async Task<WorkflowDefinitionDto> GetDefinitionAsync(Guid companyId, Guid definitionId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var definition = await _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .Include(x => x.Triggers)
            .SingleOrDefaultAsync(x => x.Id == definitionId && (x.CompanyId == companyId || x.CompanyId == null), cancellationToken);

        return definition is null
            ? throw new KeyNotFoundException("Workflow definition not found.")
            : ToDefinitionDto(definition);
    }

    public async Task<WorkflowTriggerDto> CreateTriggerAsync(
        Guid companyId,
        Guid definitionId,
        CreateWorkflowTriggerCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        Validate(command);

        var definitionExists = await _dbContext.WorkflowDefinitions
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == definitionId, cancellationToken);

        if (!definitionExists)
        {
            throw new KeyNotFoundException("Workflow definition not found.");
        }

        var trigger = new WorkflowTrigger(
            Guid.NewGuid(),
            companyId,
            definitionId,
            _supportedEventTypes.Normalize(command.EventName),
            command.CriteriaJson,
            command.IsEnabled);

        _dbContext.WorkflowTriggers.Add(trigger);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToTriggerDto(trigger);
    }

    public Task<WorkflowInstanceDto> StartManualInstanceAsync(
        Guid companyId,
        StartManualWorkflowInstanceCommand command,
        CancellationToken cancellationToken) =>
        StartInstanceAsync(
            companyId,
            new StartWorkflowInstanceCommand(command.DefinitionId, null, command.InputPayload, WorkflowTriggerType.Manual.ToStorageValue(), command.TriggerRef),
            cancellationToken);

    public async Task<WorkflowInstanceDto> StartManualInstanceByCodeAsync(
        Guid companyId,
        StartManualWorkflowByCodeCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        Validate(command);

        var code = NormalizeCode(command.Code);
        var definition = await ResolveLatestActiveDefinitionByCodeAsync(
            companyId,
            code,
            WorkflowTriggerType.Manual,
            cancellationToken);

        return await StartInstanceCoreAsync(
            companyId,
            new StartWorkflowInstanceCommand(
                definition.Id,
                null,
                command.InputPayload,
                WorkflowTriggerType.Manual.ToStorageValue(),
                command.TriggerRef),
            requireMembership: false,
            cancellationToken);
    }

    public async Task<WorkflowInstanceDto> StartInstanceAsync(
        Guid companyId,
        StartWorkflowInstanceCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        Validate(command);
        return await StartInstanceCoreAsync(companyId, command, requireMembership: false, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowInstanceDto>> StartDueScheduledWorkflowsAsync(
        Guid companyId,
        TriggerScheduledWorkflowsCommand command,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new WorkflowValidationException(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(companyId)] = ["CompanyId is required."]
            });
        }

        var definitions = await _dbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                (x.CompanyId == companyId &&
                x.Active &&
                x.TriggerType == WorkflowTriggerType.Schedule ||
                x.CompanyId == null && x.Active && x.TriggerType == WorkflowTriggerType.Schedule))
            .OrderBy(x => x.Code)
            .ThenByDescending(x => x.Version)
            .ToListAsync(cancellationToken);

        var started = new List<WorkflowInstanceDto>();
        var scheduledAtUtc = command.ScheduledAtUtc.Kind == DateTimeKind.Utc
            ? command.ScheduledAtUtc
            : command.ScheduledAtUtc.ToUniversalTime();
        foreach (var definition in ResolveLatestDefinitionsByCode(
            definitions.Where(x => MatchesSchedule(x, command.ScheduleKey)),
            companyId))
        {
            var scheduleKey = string.IsNullOrWhiteSpace(command.ScheduleKey)
                ? ResolveScheduleKey(definition) ?? definition.Code
                : command.ScheduleKey.Trim();
            var scheduleRef = $"{scheduleKey}:{scheduledAtUtc:yyyyMMddHHmm}";

            if (await HasExistingInstanceAsync(companyId, definition.Id, WorkflowTriggerType.Schedule, scheduleRef, cancellationToken))
            {
                _logger.LogInformation(
                    "Skipped duplicate scheduled workflow start for definition {DefinitionId} and schedule ref {ScheduleRef}.",
                    definition.Id,
                    scheduleRef);
                continue;
            }

            var input = CloneNodes(command.ContextJson);
            input["scheduledAtUtc"] = JsonValue.Create(scheduledAtUtc);
            input["scheduleKey"] = JsonValue.Create(scheduleKey);
            input["scheduledOccurrenceRef"] = JsonValue.Create(scheduleRef);

            try
            {
                started.Add(await StartInstanceCoreAsync(
                    companyId,
                    new StartWorkflowInstanceCommand(definition.Id, null, input, WorkflowTriggerType.Schedule.ToStorageValue(), scheduleRef),
                    requireMembership: false,
                    cancellationToken));
            }
            catch (DbUpdateException ex) when (IsDuplicateWorkflowInstanceStart(ex))
            {
                _dbContext.ChangeTracker.Clear();
                _logger.LogInformation(
                    ex,
                    "Skipped duplicate scheduled workflow occurrence for company {CompanyId}, definition {DefinitionId}, and schedule ref {ScheduleRef}.",
                    companyId,
                    definition.Id,
                    scheduleRef);
            }
        }

        return started;
    }

    public async Task<InternalWorkflowEventTriggerResult> HandleAsync(InternalWorkflowEvent workflowEvent, CancellationToken cancellationToken)
    {
        var eventType = string.IsNullOrWhiteSpace(workflowEvent.EventName) ? string.Empty : workflowEvent.EventName.Trim();
        var eventId = string.IsNullOrWhiteSpace(workflowEvent.EventRef)
            ? $"{eventType}:{Guid.NewGuid():N}"
            : workflowEvent.EventRef.Trim();
        var result = await HandleAsync(
            new PlatformEventEnvelope(
                eventId,
                eventType,
                DateTime.UtcNow,
                workflowEvent.CompanyId,
                eventId,
                "unknown",
                eventId,
                CloneNodes(workflowEvent.Payload)),
            cancellationToken);

        return new InternalWorkflowEventTriggerResult(result.Event.EventType, result.StartedInstances);
    }

    public async Task<PlatformEventTriggerResult> HandleAsync(PlatformEventEnvelope workflowEvent, CancellationToken cancellationToken)
    {
        if (workflowEvent.CompanyId == Guid.Empty)
        {
            throw new WorkflowValidationException(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(workflowEvent.CompanyId)] = ["CompanyId is required."]
            });
        }

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        AddRequired(errors, nameof(workflowEvent.EventId), workflowEvent.EventId, TriggerRefMaxLength);
        AddRequired(errors, nameof(workflowEvent.EventType), workflowEvent.EventType, EventNameMaxLength);
        AddRequired(errors, nameof(workflowEvent.CorrelationId), workflowEvent.CorrelationId, TriggerRefMaxLength);
        AddRequired(errors, nameof(workflowEvent.SourceEntityType), workflowEvent.SourceEntityType, 100);
        AddRequired(errors, nameof(workflowEvent.SourceEntityId), workflowEvent.SourceEntityId, 200);
        if (workflowEvent.OccurredAtUtc == default)
        {
            AddError(errors, nameof(workflowEvent.OccurredAtUtc), "OccurredAtUtc is required.");
        }

        if (!_supportedEventTypes.IsSupported(workflowEvent.EventType))
        {
            AddError(errors, nameof(workflowEvent.EventType), SupportedPlatformEventTypeRegistry.BuildValidationMessage(workflowEvent.EventType));
        }

        ThrowIfInvalid(errors);

        var eventId = workflowEvent.EventId.Trim();
        var eventType = _supportedEventTypes.Normalize(workflowEvent.EventType);
        var occurredAtUtc = workflowEvent.OccurredAtUtc.Kind == DateTimeKind.Utc
            ? workflowEvent.OccurredAtUtc
            : workflowEvent.OccurredAtUtc.ToUniversalTime();
        var normalizedEvent = workflowEvent with
        {
            EventId = eventId,
            EventType = eventType,
            OccurredAtUtc = occurredAtUtc,
            CorrelationId = workflowEvent.CorrelationId.Trim(),
            SourceEntityType = workflowEvent.SourceEntityType.Trim(),
            SourceEntityId = workflowEvent.SourceEntityId.Trim(),
            Metadata = CloneNodes(workflowEvent.Metadata)
        };

        var triggeredDefinitions = await _dbContext.WorkflowTriggers
            .IgnoreQueryFilters()
            .Include(x => x.Definition)
            .Where(x =>
                x.CompanyId == workflowEvent.CompanyId &&
                x.IsEnabled &&
                x.EventName == eventType &&
                x.Definition.CompanyId == workflowEvent.CompanyId &&
                x.Definition.Active &&
                x.Definition.TriggerType == WorkflowTriggerType.Event)
            .OrderBy(x => x.Definition.Code)
            .ThenByDescending(x => x.Definition.Version)
            .ToListAsync(cancellationToken);

        var started = new List<WorkflowInstanceDto>();
        foreach (var trigger in triggeredDefinitions
            .GroupBy(x => x.Definition.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(x => x.Definition.Version).First()))
        {
            var processedEvent = await TryReserveProcessedTriggerEventAsync(
                normalizedEvent.CompanyId,
                trigger.Id,
                eventId,
                cancellationToken);
            if (processedEvent is null)
            {
                _logger.LogInformation(
                    "Skipped duplicate event workflow start for company {CompanyId}, trigger {TriggerId}, event {EventType}, id {EventId}, and correlation {CorrelationId}.",
                    normalizedEvent.CompanyId,
                    trigger.Id,
                    eventType,
                    eventId,
                    normalizedEvent.CorrelationId);
                continue;
            }

            var input = BuildEventInput(normalizedEvent);

            try
            {
                var instance = await StartInstanceCoreAsync(
                    normalizedEvent.CompanyId,
                    new StartWorkflowInstanceCommand(trigger.DefinitionId, trigger.Id, input, WorkflowTriggerType.Event.ToStorageValue(), eventId),
                    requireMembership: false,
                    cancellationToken);
                processedEvent.MarkExecutionCreated(instance.Id);
                await _dbContext.SaveChangesAsync(cancellationToken);
                started.Add(instance);

                _logger.LogInformation(
                    "Created event workflow start for company {CompanyId}, trigger {TriggerId}, event {EventType}, id {EventId}, correlation {CorrelationId}, and workflow instance {WorkflowInstanceId}.",
                    normalizedEvent.CompanyId,
                    trigger.Id,
                    eventType,
                    eventId,
                    normalizedEvent.CorrelationId,
                    instance.Id);
            }
            catch (DbUpdateException ex) when (IsDuplicateWorkflowInstanceStart(ex))
            {
                _dbContext.ChangeTracker.Clear();
                _logger.LogInformation(
                    ex,
                    "Skipped duplicate event workflow start for company {CompanyId}, trigger {TriggerId}, event {EventType}, id {EventId}, and correlation {CorrelationId}.",
                    normalizedEvent.CompanyId,
                    trigger.Id,
                    eventType,
                    eventId,
                    normalizedEvent.CorrelationId);
            }
        }

        return new PlatformEventTriggerResult(normalizedEvent, started);
    }

    private async Task<WorkflowInstanceDto> StartInstanceCoreAsync(
        Guid companyId,
        StartWorkflowInstanceCommand command,
        bool requireMembership,
        CancellationToken cancellationToken)
    {
        if (requireMembership)
        {
            await RequireMembershipAsync(companyId, cancellationToken);
        }

        var triggerSource = WorkflowTriggerTypeValues.Parse(command.TriggerSource);
        var definition = await _dbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == command.DefinitionId && (x.CompanyId == companyId || x.CompanyId == null), cancellationToken);

        if (definition is null)
        {
            throw new KeyNotFoundException("Workflow definition not found.");
        }

        if (!definition.Active)
        {
            throw new WorkflowValidationException(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(command.DefinitionId)] = ["Workflow definition must be active before an instance can be started."]
            });
        }

        if (definition.TriggerType != triggerSource)
        {
            throw new WorkflowValidationException(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(command.TriggerSource)] = [$"Workflow definition only supports '{definition.TriggerType.ToStorageValue()}' triggers."]
            });
        }

        if (command.TriggerId.HasValue)
        {
            var triggerExists = await _dbContext.WorkflowTriggers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x =>
                    x.CompanyId == companyId &&
                    x.DefinitionId == command.DefinitionId &&
                    x.Id == command.TriggerId.Value &&
                    x.IsEnabled,
                    cancellationToken);

            if (!triggerExists)
            {
                throw new WorkflowValidationException(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(command.TriggerId)] = ["TriggerId must reference an enabled trigger on the workflow definition."]
                });
            }
        }

        var instance = new WorkflowInstance(
            Guid.NewGuid(),
            companyId,
            command.DefinitionId,
            command.TriggerId,
            command.InputPayload,
            triggerSource,
            command.TriggerRef,
            ResolveInitialStep(definition),
            command.InputPayload);

        _dbContext.WorkflowInstances.Add(instance);
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                triggerSource == WorkflowTriggerType.Manual ? AuditActorTypes.User : AuditActorTypes.System,
                null,
                AuditEventActions.WorkflowInstanceStarted,
                AuditTargetTypes.WorkflowInstance,
                instance.Id.ToString("N"),
                AuditEventOutcomes.Succeeded,
                $"Started workflow '{definition.Code}' from {triggerSource.ToStorageValue()} trigger.",
                Metadata: new Dictionary<string, string?>
                {
                    ["workflowDefinitionId"] = definition.Id.ToString("N"),
                    ["workflowDefinitionCode"] = definition.Code,
                    ["workflowDefinitionVersion"] = definition.Version.ToString(),
                    ["triggerSource"] = triggerSource.ToStorageValue(),
                    ["triggerRef"] = command.TriggerRef
                },
                CorrelationId: ResolveCorrelationId(command)),
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetInstanceCoreAsync(companyId, instance.Id, requireMembership, cancellationToken);
    }

    private async Task<WorkflowDefinition> ResolveLatestActiveDefinitionByCodeAsync(
        Guid companyId,
        string code,
        WorkflowTriggerType triggerType,
        CancellationToken cancellationToken)
    {
        var definitions = await _dbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                (x.CompanyId == companyId || x.CompanyId == null) &&
                x.Code == code &&
                x.Active &&
                x.TriggerType == triggerType)
            .ToListAsync(cancellationToken);

        return ResolveLatestDefinitionsByCode(definitions, companyId).SingleOrDefault()
            ?? throw new KeyNotFoundException("Workflow definition not found.");
    }

    private static IEnumerable<WorkflowDefinition> ResolveLatestDefinitionsByCode(
        IEnumerable<WorkflowDefinition> definitions,
        Guid companyId) =>
        definitions
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => x.CompanyId == companyId)
                .ThenByDescending(x => x.Version)
                .First());

    private Task<bool> HasExistingInstanceAsync(
        Guid companyId,
        Guid definitionId,
        WorkflowTriggerType triggerSource,
        string? triggerRef,
        CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(triggerRef)
            ? Task.FromResult(false)
            : _dbContext.WorkflowInstances
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(x =>
                    x.CompanyId == companyId &&
                    x.DefinitionId == definitionId &&
                    x.TriggerSource == triggerSource &&
                    x.TriggerRef == triggerRef,
                    cancellationToken);

    public async Task<IReadOnlyList<WorkflowInstanceDto>> ListInstancesAsync(
        Guid companyId,
        Guid? definitionId,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var query = _dbContext.WorkflowInstances
            .AsNoTracking()
            .Include(x => x.Definition)
            .Where(x => x.CompanyId == companyId);

        if (definitionId.HasValue)
        {
            query = query.Where(x => x.DefinitionId == definitionId.Value);
        }

        var instances = await query
            .OrderByDescending(x => x.StartedUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        return instances.Select(ToInstanceDto).ToList();
    }

    public async Task<WorkflowInstanceDto> GetInstanceAsync(Guid companyId, Guid instanceId, CancellationToken cancellationToken)
        => await GetInstanceCoreAsync(companyId, instanceId, requireMembership: true, cancellationToken);

    private async Task<WorkflowInstanceDto> GetInstanceCoreAsync(
        Guid companyId,
        Guid instanceId,
        bool requireMembership,
        CancellationToken cancellationToken)
    {
        if (requireMembership)
        {
            await RequireMembershipAsync(companyId, cancellationToken);
        }

        IQueryable<WorkflowInstance> query = _dbContext.WorkflowInstances
            .AsNoTracking()
            .Include(x => x.Definition);

        if (!requireMembership)
        {
            query = query.IgnoreQueryFilters();
        }

        var instance = await query
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == instanceId, cancellationToken);

        return instance is null
            ? throw new KeyNotFoundException("Workflow instance not found.")
            : ToInstanceDto(instance);
    }

    public async Task<WorkflowInstanceDto> UpdateInstanceStateAsync(
        Guid companyId,
        Guid instanceId,
        UpdateWorkflowInstanceStateCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        Validate(command);

        var instance = await _dbContext.WorkflowInstances
            .Include(x => x.Definition)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == instanceId, cancellationToken);

        if (instance is null)
        {
            throw new KeyNotFoundException("Workflow instance not found.");
        }

        var state = WorkflowInstanceStatusValues.Parse(command.State);
        instance.UpdateState(
            state,
            command.CurrentStep,
            command.OutputPayload);

        if (state is WorkflowInstanceStatus.Failed or WorkflowInstanceStatus.Blocked)
        {
            await EnsureOpenWorkflowExceptionAsync(instance, state, command.OutputPayload, cancellationToken);
        }
        EnqueueWorkflowStateChangedEvent(instance, state, command.OutputPayload);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToInstanceDto(instance);
    }

    public async Task<IReadOnlyList<WorkflowExceptionDto>> ListExceptionsAsync(
        Guid companyId,
        string? status,
        Guid? workflowInstanceId,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var query = _dbContext.WorkflowExceptions
            .AsNoTracking()
            .Include(x => x.WorkflowInstance)
            .Include(x => x.Definition)
            .Where(x => x.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == WorkflowExceptionStatusValues.Parse(status));
        }

        if (workflowInstanceId.HasValue)
        {
            query = query.Where(x => x.WorkflowInstanceId == workflowInstanceId.Value);
        }

        var exceptions = await query
            .OrderByDescending(x => x.OccurredUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        return exceptions.Select(ToExceptionDto).ToList();
    }

    public async Task<WorkflowExceptionDto> GetExceptionAsync(Guid companyId, Guid exceptionId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var workflowException = await _dbContext.WorkflowExceptions
            .AsNoTracking()
            .Include(x => x.WorkflowInstance)
            .Include(x => x.Definition)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == exceptionId, cancellationToken);

        return workflowException is null
            ? throw new KeyNotFoundException("Workflow exception not found.")
            : ToExceptionDto(workflowException);
    }

    public async Task<WorkflowExceptionDto> ReviewExceptionAsync(
        Guid companyId,
        Guid exceptionId,
        ReviewWorkflowExceptionCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        Validate(command);

        var workflowException = await _dbContext.WorkflowExceptions
            .Include(x => x.WorkflowInstance)
            .Include(x => x.Definition)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == exceptionId, cancellationToken);

        if (workflowException is null)
        {
            throw new KeyNotFoundException("Workflow exception not found.");
        }

        workflowException.MarkReviewed(membership.UserId, command.ResolutionNotes);
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                membership.UserId,
                AuditEventActions.WorkflowExceptionReviewed,
                AuditTargetTypes.WorkflowException,
                workflowException.Id.ToString("N"),
                AuditEventOutcomes.Succeeded,
                $"Reviewed workflow exception for step '{workflowException.StepKey}'.",
                Metadata: new Dictionary<string, string?>
                {
                    ["workflowInstanceId"] = workflowException.WorkflowInstanceId.ToString("N"),
                    ["workflowDefinitionId"] = workflowException.WorkflowDefinitionId.ToString("N"),
                    ["stepKey"] = workflowException.StepKey,
                    ["exceptionType"] = workflowException.ExceptionType.ToStorageValue()
                }),
            cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return ToExceptionDto(workflowException);
    }

    private async Task EnsureOpenWorkflowExceptionAsync(
        WorkflowInstance instance,
        WorkflowInstanceStatus state,
        Dictionary<string, JsonNode?>? outputPayload,
        CancellationToken cancellationToken)
    {
        var exceptionType = state == WorkflowInstanceStatus.Failed ? WorkflowExceptionType.Failed : WorkflowExceptionType.Blocked;
        var stepKey = string.IsNullOrWhiteSpace(instance.CurrentStep) ? InstanceStepKey : instance.CurrentStep;

        var exists = await _dbContext.WorkflowExceptions.AnyAsync(
            x => x.CompanyId == instance.CompanyId &&
                x.WorkflowInstanceId == instance.Id &&
                x.StepKey == stepKey &&
                x.ExceptionType == exceptionType &&
                x.Status == WorkflowExceptionStatus.Open,
            cancellationToken);

        if (exists)
        {
            return;
        }

        var workflowException = new WorkflowException(
            Guid.NewGuid(),
            instance.CompanyId,
            instance.Id,
            instance.DefinitionId,
            stepKey,
            exceptionType,
            state == WorkflowInstanceStatus.Failed ? "Workflow step failed" : "Workflow step blocked",
            ResolveExceptionDetails(outputPayload, state),
            TryGetString(outputPayload, "errorCode") ?? TryGetString(outputPayload, "code"),
            outputPayload);

        _dbContext.WorkflowExceptions.Add(workflowException);
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                instance.CompanyId,
                AuditActorTypes.System,
                null,
                AuditEventActions.WorkflowExceptionCreated,
                AuditTargetTypes.WorkflowException,
                workflowException.Id.ToString("N"),
                AuditEventOutcomes.Pending,
                workflowException.Title,
                Metadata: new Dictionary<string, string?>
                {
                    ["workflowInstanceId"] = instance.Id.ToString("N"),
                    ["workflowDefinitionId"] = instance.DefinitionId.ToString("N"),
                    ["stepKey"] = workflowException.StepKey,
                    ["exceptionType"] = workflowException.ExceptionType.ToStorageValue()
                }),
            cancellationToken);

        _outboxEnqueuer.Enqueue(
            instance.CompanyId,
            CompanyOutboxTopics.NotificationDeliveryRequested,
            new NotificationDeliveryRequestedMessage(
                instance.CompanyId,
                state == WorkflowInstanceStatus.Failed
                    ? CompanyNotificationType.WorkflowFailure.ToStorageValue()
                    : CompanyNotificationType.Escalation.ToStorageValue(),
                CompanyNotificationPriority.High.ToStorageValue(),
                workflowException.Title,
                workflowException.Details,
                AuditTargetTypes.WorkflowException,
                workflowException.Id,
                $"/workflows?companyId={instance.CompanyId}",
                null,
                CompanyMembershipRole.Owner.ToStorageValue(),
                null,
                null,
                $"workflow-exception:{workflowException.Id:N}",
                null),
            idempotencyKey: $"notification:workflow-exception:{workflowException.Id:N}",
            causationId: workflowException.Id.ToString("N"));
    }

    private void EnqueueWorkflowStateChangedEvent(
        WorkflowInstance instance,
        WorkflowInstanceStatus state,
        Dictionary<string, JsonNode?>? outputPayload)
    {
        var eventType = SupportedPlatformEventTypeRegistry.WorkflowStateChanged;
        var occurredAtUtc = instance.UpdatedUtc.Kind == DateTimeKind.Utc
            ? instance.UpdatedUtc
            : instance.UpdatedUtc.ToUniversalTime();
        var eventId = $"{eventType}:{instance.Id:N}:{occurredAtUtc:yyyyMMddHHmmssfffffff}";
        var correlationId = ResolveCorrelationId(new StartWorkflowInstanceCommand(
            instance.DefinitionId,
            instance.TriggerId,
            instance.InputPayload,
            instance.TriggerSource.ToStorageValue(),
            instance.TriggerRef)) ?? eventId;

        _outboxEnqueuer.Enqueue(
            instance.CompanyId,
            eventType,
            new PlatformEventEnvelope(
                eventId,
                eventType,
                occurredAtUtc,
                instance.CompanyId,
                correlationId,
                "workflow_instance",
                instance.Id.ToString("N"),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["workflowInstanceId"] = JsonValue.Create(instance.Id.ToString("N")),
                    ["workflowDefinitionId"] = JsonValue.Create(instance.DefinitionId.ToString("N")),
                    ["workflowTriggerId"] = instance.TriggerId.HasValue ? JsonValue.Create(instance.TriggerId.Value.ToString("N")) : null,
                    ["state"] = JsonValue.Create(state.ToStorageValue()),
                    ["currentStep"] = JsonValue.Create(instance.CurrentStep),
                    ["triggerSource"] = JsonValue.Create(instance.TriggerSource.ToStorageValue()),
                    ["triggerRef"] = JsonValue.Create(instance.TriggerRef),
                    ["output"] = new JsonObject(CloneNodes(outputPayload).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase))
                }),
            correlationId,
            idempotencyKey: $"platform-event:{instance.CompanyId:N}:{eventId}",
            causationId: instance.Id.ToString("N"));
    }

    private async Task<ResolvedCompanyMembershipContext> RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _companyMembershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }

        return membership;
    }

    private static WorkflowDefinitionDto ToDefinitionDto(WorkflowDefinition definition) =>
        new(
            definition.Id,
            definition.CompanyId,
            definition.Code,
            definition.Name,
            definition.Department,
            definition.Version,
            definition.TriggerType.ToStorageValue(),
            CloneNodes(definition.DefinitionJson),
            definition.Active,
            definition.CreatedUtc,
            definition.UpdatedUtc,
            definition.Triggers.Select(ToTriggerDto).OrderBy(x => x.EventName).ToList());

    private static WorkflowTriggerDto ToTriggerDto(WorkflowTrigger trigger) =>
        new(
            trigger.Id,
            trigger.CompanyId,
            trigger.DefinitionId,
            trigger.EventName,
            CloneNodes(trigger.CriteriaJson),
            trigger.IsEnabled,
            trigger.CreatedUtc,
            trigger.UpdatedUtc);

    private static WorkflowInstanceDto ToInstanceDto(WorkflowInstance instance) =>
        new(
            instance.Id,
            instance.CompanyId,
            instance.DefinitionId,
            instance.TriggerId,
            instance.TriggerSource.ToStorageValue(),
            instance.TriggerRef,
            instance.State.ToStorageValue(),
            instance.State.ToStorageValue(),
            instance.CurrentStep,
            CloneNodes(instance.InputPayload),
            CloneNodes(instance.ContextJson),
            CloneNodes(instance.OutputPayload),
            instance.StartedUtc,
            instance.UpdatedUtc,
            instance.CompletedUtc,
            instance.Definition.Code,
            instance.Definition.Name,
            instance.Definition.Version);

    private static WorkflowExceptionDto ToExceptionDto(WorkflowException workflowException) =>
        new(
            workflowException.Id,
            workflowException.CompanyId,
            workflowException.WorkflowInstanceId,
            workflowException.WorkflowDefinitionId,
            workflowException.Definition.Code,
            workflowException.Definition.Name,
            workflowException.StepKey,
            workflowException.ExceptionType.ToStorageValue(),
            workflowException.Status.ToStorageValue(),
            workflowException.Title,
            workflowException.Details,
            workflowException.ErrorCode,
            CloneNodes(workflowException.TechnicalDetailsJson),
            workflowException.OccurredUtc,
            workflowException.ReviewedUtc,
            workflowException.ReviewedByUserId,
            workflowException.ResolutionNotes,
            workflowException.WorkflowInstance.State.ToStorageValue(),
            workflowException.WorkflowInstance.CurrentStep);

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static void Validate(CreateWorkflowDefinitionCommand command)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        AddRequired(errors, nameof(command.Code), command.Code, CodeMaxLength);
        AddRequired(errors, nameof(command.Name), command.Name, NameMaxLength);
        AddOptional(errors, nameof(command.Department), command.Department, DepartmentMaxLength);
        AddTriggerType(errors, nameof(command.TriggerType), command.TriggerType);
        AddDefinitionJson(errors, nameof(command.DefinitionJson), command.DefinitionJson);
        PredefinedWorkflowCatalog.ValidateDefinition(errors, nameof(command.Code), command.Code, nameof(command.TriggerType), command.TriggerType, nameof(command.DefinitionJson), command.DefinitionJson);
        ThrowIfInvalid(errors);
    }

    private static void Validate(CreateWorkflowDefinitionVersionCommand command, string code)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        AddOptional(errors, nameof(command.Name), command.Name, NameMaxLength);
        AddOptional(errors, nameof(command.Department), command.Department, DepartmentMaxLength);
        AddTriggerType(errors, nameof(command.TriggerType), command.TriggerType);
        AddDefinitionJson(errors, nameof(command.DefinitionJson), command.DefinitionJson);
        PredefinedWorkflowCatalog.ValidateDefinition(errors, nameof(CreateWorkflowDefinitionCommand.Code), code, nameof(command.TriggerType), command.TriggerType, nameof(command.DefinitionJson), command.DefinitionJson);
        ThrowIfInvalid(errors);
    }

    private void Validate(CreateWorkflowTriggerCommand command)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        AddRequired(errors, nameof(command.EventName), command.EventName, EventNameMaxLength);
        if (!string.IsNullOrWhiteSpace(command.EventName) &&
            !_supportedEventTypes.IsSupported(command.EventName))
        {
            AddError(errors, nameof(command.EventName), SupportedPlatformEventTypeRegistry.BuildValidationMessage(command.EventName));
        }
        ThrowIfInvalid(errors);
    }

    private static void Validate(StartManualWorkflowByCodeCommand command)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        AddRequired(errors, nameof(command.Code), command.Code, CodeMaxLength);
        AddOptional(errors, nameof(command.TriggerRef), command.TriggerRef, TriggerRefMaxLength);
        if (command.InputPayload is null)
        {
            return;
        }

        ThrowIfInvalid(errors);
    }

    private static void Validate(StartWorkflowInstanceCommand command)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (command.DefinitionId == Guid.Empty)
        {
            AddError(errors, nameof(command.DefinitionId), "DefinitionId is required.");
        }

        if (command.TriggerId == Guid.Empty)
        {
            AddError(errors, nameof(command.TriggerId), "TriggerId cannot be empty.");
        }

        AddSupportedTriggerSource(errors, nameof(command.TriggerSource), command.TriggerSource);
        AddOptional(errors, nameof(command.TriggerRef), command.TriggerRef, TriggerRefMaxLength);

        ThrowIfInvalid(errors);
    }

    private static void Validate(UpdateWorkflowInstanceStateCommand command)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        AddWorkflowInstanceState(errors, nameof(command.State), command.State);
        AddOptional(errors, nameof(command.CurrentStep), command.CurrentStep, CurrentStepMaxLength);

        ThrowIfInvalid(errors);
    }

    private static void Validate(ReviewWorkflowExceptionCommand command)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        AddOptional(errors, nameof(command.ResolutionNotes), command.ResolutionNotes, ResolutionNotesMaxLength);
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

    private static void AddTriggerType(IDictionary<string, List<string>> errors, string key, string? value)
    {
        if (!WorkflowTriggerTypeValues.TryParse(value, out _))
        {
            AddError(errors, key, WorkflowTriggerTypeValues.BuildValidationMessage(value));
        }
    }

    private static void AddSupportedTriggerSource(IDictionary<string, List<string>> errors, string key, string? value)
    {
        if (!WorkflowTriggerTypeValues.TryParse(value, out var triggerType))
        {
            AddError(errors, key, WorkflowTriggerTypeValues.BuildValidationMessage(value));
            return;
        }

        if (triggerType is not (WorkflowTriggerType.Manual or WorkflowTriggerType.Schedule or WorkflowTriggerType.Event))
        {
            AddError(errors, key, "Workflow instances can only be started from manual, schedule, or event triggers.");
        }
    }

    private static void AddWorkflowInstanceState(IDictionary<string, List<string>> errors, string key, string? value)
    {
        if (!WorkflowInstanceStatusValues.TryParse(value, out _))
        {
            AddError(errors, key, WorkflowInstanceStatusValues.BuildValidationMessage(value));
        }
    }

    private static void AddDefinitionJson(
        IDictionary<string, List<string>> errors,
        string key,
        Dictionary<string, JsonNode?>? definitionJson)
    {
        if (definitionJson is null || definitionJson.Count == 0)
        {
            AddError(errors, key, "DefinitionJson must be a non-empty JSON object.");
            return;
        }

        if (!definitionJson.TryGetValue("steps", out var steps) || steps is not JsonArray)
        {
            AddError(errors, key, "DefinitionJson must include a steps array.");
        }
    }

    private static string NormalizeCode(string code) =>
        code.Trim().ToUpperInvariant();

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
            throw new WorkflowValidationException(errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
        }
    }

    private static string? ResolveInitialStep(WorkflowDefinition definition)
    {
        if (!definition.DefinitionJson.TryGetValue("steps", out var stepsNode) || stepsNode is not JsonArray steps)
        {
            return null;
        }

        var firstStep = steps.OfType<JsonObject>().FirstOrDefault();
        return TryGetString(firstStep, "id") ?? TryGetString(firstStep, "code") ?? TryGetString(firstStep, "name");
    }

    private static string? ResolveScheduleKey(WorkflowDefinition definition)
    {
        if (!definition.DefinitionJson.TryGetValue("schedule", out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value &&
            value.TryGetValue<string>(out var scalar) &&
            !string.IsNullOrWhiteSpace(scalar))
        {
            return scalar.Trim();
        }

        if (node is JsonObject jsonObject)
        {
            return TryGetString(jsonObject, "key") ??
                TryGetString(jsonObject, "scheduleKey") ??
                TryGetString(jsonObject, "id") ??
                TryGetString(jsonObject, "name");
        }

        return null;
    }

    private static bool MatchesSchedule(WorkflowDefinition definition, string? scheduleKey)
    {
        if (string.IsNullOrWhiteSpace(scheduleKey))
        {
            return true;
        }

        return MatchesConfiguredValue(definition.DefinitionJson, "schedule", scheduleKey);
    }

    private static bool MatchesEvent(WorkflowDefinition definition, string eventName) =>
        MatchesConfiguredValue(definition.DefinitionJson, "event", eventName) ||
        MatchesConfiguredValue(definition.DefinitionJson, "trigger", eventName);

    private static bool MatchesConfiguredValue(Dictionary<string, JsonNode?> definitionJson, string sectionName, string value)
    {
        if (!definitionJson.TryGetValue(sectionName, out var node) || node is null)
        {
            return false;
        }

        if (node is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out var scalar) &&
            string.Equals(scalar, value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (node is JsonObject jsonObject)
        {
            return new[] { "key", "id", "name", "eventName", "scheduleKey" }
                .Select(key => TryGetString(jsonObject, key))
                .Any(configured => string.Equals(configured, value, StringComparison.OrdinalIgnoreCase));
        }

        if (node is JsonArray jsonArray)
        {
            return jsonArray.Any(item =>
                item is JsonValue arrayValue &&
                arrayValue.TryGetValue<string>(out var configured) &&
                string.Equals(configured, value, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private async Task<ProcessedWorkflowTriggerEvent?> TryReserveProcessedTriggerEventAsync(
        Guid companyId,
        Guid triggerId,
        string eventId,
        CancellationToken cancellationToken)
    {
        try
        {
            var processedEvent = new ProcessedWorkflowTriggerEvent(
                Guid.NewGuid(),
                companyId,
                triggerId,
                eventId,
                DateTime.UtcNow);
            await _dbContext.ProcessedWorkflowTriggerEvents.AddAsync(processedEvent, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
            return processedEvent;
        }
        catch (DbUpdateException ex) when (IsDuplicateProcessedTriggerEvent(ex))
        {
            _dbContext.ChangeTracker.Clear();
            return null;
        }
    }

    private static bool IsDuplicateProcessedTriggerEvent(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("processed_workflow_trigger_events", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("IX_processed_workflow_trigger_events_company_id_workflow_trigger_id_event_id", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, JsonNode?> BuildEventInput(PlatformEventEnvelope workflowEvent) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["eventId"] = JsonValue.Create(workflowEvent.EventId),
            ["eventType"] = JsonValue.Create(workflowEvent.EventType),
            ["occurredAtUtc"] = JsonValue.Create(workflowEvent.OccurredAtUtc),
            ["companyId"] = JsonValue.Create(workflowEvent.CompanyId),
            ["correlationId"] = JsonValue.Create(workflowEvent.CorrelationId),
            ["sourceEntityType"] = JsonValue.Create(workflowEvent.SourceEntityType),
            ["sourceEntityId"] = JsonValue.Create(workflowEvent.SourceEntityId),
            ["metadata"] = new JsonObject(CloneNodes(workflowEvent.Metadata).ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase))
        };

    private static string? ResolveCorrelationId(StartWorkflowInstanceCommand command) =>
        TryGetString(command.InputPayload, "correlationId") ?? command.TriggerRef;

    private static string? TryGetString(JsonObject? jsonObject, string key)
    {
        if (jsonObject is null ||
            !jsonObject.TryGetPropertyValue(key, out var node) ||
            node is not JsonValue value ||
            !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool IsDuplicateWorkflowInstanceStart(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("workflow_instances", StringComparison.OrdinalIgnoreCase) &&
                current.Message.Contains("trigger_ref", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveExceptionDetails(
        IReadOnlyDictionary<string, JsonNode?>? payload,
        WorkflowInstanceStatus state) =>
        TryGetString(payload, "reason") ??
        TryGetString(payload, "details") ??
        TryGetString(payload, "message") ??
        TryGetString(payload, "error") ??
        (state == WorkflowInstanceStatus.Failed
            ? "Workflow step execution failed and needs review."
            : "Workflow step execution is blocked and needs review.");

    private static string? TryGetString(IReadOnlyDictionary<string, JsonNode?>? payload, string key)
    {
        if (payload is null ||
            !payload.TryGetValue(key, out var node) ||
            node is not JsonValue value ||
            !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
