using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Activity;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Activity;

public sealed class EfActivityEventStore : IActivityEventStore
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int MaxCorrelationEvents = 10_000;
    private static readonly JsonSerializerOptions CursorJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;
    private readonly IEntityLinkResolutionService _entityLinkResolver;
    private readonly IActivityEventSummaryFormatter _summaryFormatter;
    private readonly IActivityEventPublisher _publisher;
    private readonly ILogger<EfActivityEventStore> _logger;

    public EfActivityEventStore(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        IEntityLinkResolutionService entityLinkResolver,
        IActivityEventSummaryFormatter summaryFormatter,
        IActivityEventPublisher publisher,
        ILogger<EfActivityEventStore> logger)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _entityLinkResolver = entityLinkResolver;
        _summaryFormatter = summaryFormatter;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<ActivityEventDto> PersistAsync(Guid tenantId, PersistActivityEventCommand command, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(tenantId, cancellationToken);
        ValidateTenant(tenantId, command.TenantId);
        Validate(command);

        string? agentDepartment = null;
        if (command.AgentId.HasValue)
        {
            agentDepartment = await _dbContext.Agents.IgnoreQueryFilters()
                .Where(x => x.CompanyId == tenantId && x.Id == command.AgentId.Value)
                .Select(x => x.Department)
                .SingleOrDefaultAsync(cancellationToken);
            if (agentDepartment is null)
            {
                throw BuildValidationException(nameof(command.AgentId), "AgentId must reference an agent in the requested tenant.");
            }
        }

        var taskId = ExtractTaskId(command.Source);
        var auditEventId = await ResolveTenantAuditEventIdAsync(tenantId, ExtractAuditEventId(command.Source), cancellationToken);

        var activityEvent = new ActivityEvent(
            Guid.NewGuid(),
            tenantId,
            command.AgentId,
            command.EventType,
            command.OccurredAt,
            command.Status,
            command.Summary,
            command.CorrelationId,
            command.Source,
            ReadString(command.Source ?? new Dictionary<string, JsonNode?>(), "department", "agentDepartment") ?? agentDepartment,
            taskId,
            auditEventId);

        _dbContext.ActivityEvents.Add(activityEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = ToDto(activityEvent, new HashSet<Guid>());
        _logger.LogInformation(
            "Persisted activity event {ActivityEventId} for tenant {TenantId} with correlation {CorrelationId}.",
            dto.EventId,
            dto.TenantId,
            dto.CorrelationId);

        // Publish only after the database commit so subscribers never observe uncommitted activity.
        await _publisher.PublishAsync(dto, cancellationToken);
        return dto;
    }

    public async Task<ActivityFeedPageDto> QueryFeedAsync(Guid tenantId, ActivityFeedQuery query, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(tenantId, cancellationToken);
        ValidateQuery(query);
        var pageSize = Math.Clamp(query.PageSize ?? DefaultPageSize, 1, MaxPageSize);

        var events = _dbContext.ActivityEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == tenantId);

        events = ApplyFilters(events, query);
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            var cursor = DecodeCursor(query.Cursor);
            events = events.Where(x =>
                x.OccurredUtc < cursor.OccurredAt ||
                x.OccurredUtc == cursor.OccurredAt && x.Id.CompareTo(cursor.EventId) < 0);
        }

        var page = await events
            .OrderByDescending(x => x.OccurredUtc)
            .ThenByDescending(x => x.Id)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var auditEventIds = await ResolveLinkedAuditIdsAsync(tenantId, page.Take(pageSize), cancellationToken);
        var items = page.Take(pageSize).Select(x => ToDto(x, auditEventIds)).ToList();
        var nextCursor = page.Count > pageSize
            ? EncodeCursor(items[^1].OccurredAt, items[^1].EventId)
            : null;

        return new ActivityFeedPageDto(items, nextCursor);
    }

    public async Task<ActivityCorrelationTimelineDto> QueryCorrelationTimelineAsync(
        Guid tenantId,
        ActivityCorrelationTimelineQuery query,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(tenantId, cancellationToken);
        ValidateCorrelationQuery(query);

        var correlationId = query.CorrelationId?.Trim();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            var requestedSelectedActivityEventId = query.SelectedActivityEventId
                ?? throw BuildValidationException(nameof(query.SelectedActivityEventId), "SelectedActivityEventId is required when CorrelationId is not supplied.");

            var selectedCorrelation = await _dbContext.ActivityEvents.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == tenantId && x.Id == requestedSelectedActivityEventId)
                .Select(x => x.CorrelationId)
                .SingleOrDefaultAsync(cancellationToken);

            if (selectedCorrelation is null)
            {
                throw new KeyNotFoundException("Activity event was not found.");
            }

            correlationId = selectedCorrelation;
        }

        var events = await _dbContext.ActivityEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == tenantId && x.CorrelationId == correlationId)
            .OrderBy(x => x.OccurredUtc)
            .ThenBy(x => x.Id)
            .Take(MaxCorrelationEvents)
            .ToListAsync(cancellationToken);

        var referencesByEvent = events.ToDictionary(x => x.Id, x => ExtractReferences(x.SourceMetadata));
        var resolved = await _entityLinkResolver.ResolveAsync(
            tenantId,
            referencesByEvent.Values.SelectMany(x => x),
            cancellationToken);

        var items = events.Select(activityEvent =>
        {
            var references = referencesByEvent[activityEvent.Id];
            var links = references
                .Select(reference => resolved[ActivityEntityTypes.ToKey(reference.EntityType, reference.EntityId)])
                .ToList();

            return new ActivityTimelineItemDto(
                ToDto(activityEvent, new HashSet<Guid>()),
                references.FirstOrDefault(),
                links);
        }).ToList();

        ActivityLinkedEntitiesDto? selectedLinks = null;
        if (query.SelectedActivityEventId is Guid selectedActivityEventId)
        {
            var selected = events.FirstOrDefault(x => x.Id == selectedActivityEventId);
            if (selected is null)
            {
                throw new KeyNotFoundException("Activity event was not found for the requested correlation.");
            }

            selectedLinks = new ActivityLinkedEntitiesDto(
                tenantId,
                selectedActivityEventId,
                referencesByEvent[selectedActivityEventId]
                    .Select(reference => resolved[ActivityEntityTypes.ToKey(reference.EntityType, reference.EntityId)])
                    .ToList());
        }

        return new ActivityCorrelationTimelineDto(tenantId, correlationId, items, selectedLinks);
    }

    public Task<ActivityCorrelationTimelineDto> QueryCorrelationTimelineForActivityAsync(
        Guid tenantId,
        Guid activityEventId,
        CancellationToken cancellationToken) =>
        QueryCorrelationTimelineAsync(
            tenantId,
            new ActivityCorrelationTimelineQuery(SelectedActivityEventId: activityEventId),
            cancellationToken);

    public async Task<ActivityLinkedEntitiesDto> QueryLinkedEntitiesAsync(Guid tenantId, Guid activityEventId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(tenantId, cancellationToken);

        var activityEvent = await _dbContext.ActivityEvents.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == tenantId && x.Id == activityEventId, cancellationToken)
            ?? throw new KeyNotFoundException("Activity event was not found.");

        var references = ExtractReferences(activityEvent.SourceMetadata);
        var resolved = await _entityLinkResolver.ResolveAsync(tenantId, references, cancellationToken);
        return new ActivityLinkedEntitiesDto(tenantId, activityEventId, references.Select(x => resolved[ActivityEntityTypes.ToKey(x.EntityType, x.EntityId)]).ToList());
    }

    private async Task RequireMembershipAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty ||
            await _membershipContextResolver.ResolveAsync(tenantId, cancellationToken) is null)
        {
            throw new UnauthorizedAccessException();
        }
    }

    private static void ValidateTenant(Guid tenantId, Guid commandTenantId)
    {
        if (commandTenantId == Guid.Empty || commandTenantId != tenantId)
        {
            throw BuildValidationException(nameof(PersistActivityEventCommand.TenantId), "TenantId must match the route companyId.");
        }
    }

    private static void Validate(PersistActivityEventCommand command)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        AddRequired(errors, nameof(command.EventType), command.EventType, 100);
        AddRequired(errors, nameof(command.Status), command.Status, 64);
        AddRequired(errors, nameof(command.Summary), command.Summary, 500);
        AddOptional(errors, nameof(command.CorrelationId), command.CorrelationId, 128);
        if (command.OccurredAt == default)
        {
            errors[nameof(command.OccurredAt)] = ["OccurredAt is required."];
        }

        if (command.AgentId == Guid.Empty)
        {
            errors[nameof(command.AgentId)] = ["AgentId cannot be empty."];
        }

        if (errors.Count > 0)
        {
            throw new ActivityFeedValidationException(errors);
        }
    }

    private static void ValidateQuery(ActivityFeedQuery query)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        AddOptional(errors, nameof(query.Department), query.Department, 100);
        AddOptional(errors, nameof(query.EventType), query.EventType, 100);
        AddOptional(errors, nameof(query.Status), query.Status, 64);
        AddOptional(errors, nameof(query.Timeframe), query.Timeframe, 32);

        if (query.AgentId == Guid.Empty)
        {
            errors[nameof(query.AgentId)] = ["AgentId cannot be empty."];
        }

        if (query.TaskId == Guid.Empty)
        {
            errors[nameof(query.TaskId)] = ["TaskId cannot be empty."];
        }

        if (!IsSupportedTimeframe(query.Timeframe))
        {
            errors[nameof(query.Timeframe)] = ["Timeframe must be one of all, lastHour, last24Hours, last7Days, last30Days, or custom."];
        }

        if (query.FromUtc is DateTime fromUtc && query.ToUtc is DateTime toUtc && fromUtc > toUtc)
        {
            errors["dateRange"] = ["Activity feed from date must be on or before the to date."];
        }

        if (errors.Count > 0)
        {
            throw new ActivityFeedValidationException(errors);
        }
    }

    private static IQueryable<ActivityEvent> ApplyFilters(IQueryable<ActivityEvent> events, ActivityFeedQuery query)
    {
        // Activity feed filters are AND-combined. ToUtc is exclusive so adjacent ranges do not overlap.
        if (query.AgentId is Guid agentId)
        {
            events = events.Where(x => x.AgentId == agentId);
        }

        if (!string.IsNullOrWhiteSpace(query.Department))
        {
            var department = query.Department.Trim();
            events = events.Where(x => x.Department == department);
        }

        if (query.TaskId is Guid taskId)
        {
            events = events.Where(x => x.TaskId == taskId);
        }

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            var eventType = query.EventType.Trim();
            events = events.Where(x => x.EventType == eventType);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = query.Status.Trim();
            events = events.Where(x => x.Status == status);
        }

        var range = ResolveTimeframeRange(query);

        if (range.FromUtc is DateTime fromUtc)
        {
            events = events.Where(x => x.OccurredUtc >= fromUtc);
        }

        if (range.ToUtc is DateTime toUtc)
        {
            events = events.Where(x => x.OccurredUtc < toUtc);
        }

        return events;
    }

    private static void ValidateCorrelationQuery(ActivityCorrelationTimelineQuery query)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        AddOptional(errors, nameof(query.CorrelationId), query.CorrelationId, 128);

        if (string.IsNullOrWhiteSpace(query.CorrelationId) && !query.SelectedActivityEventId.HasValue)
        {
            errors[nameof(query.CorrelationId)] = ["CorrelationId or SelectedActivityEventId is required."];
        }

        if (!string.IsNullOrWhiteSpace(query.CorrelationId) && query.CorrelationId.Trim().Length > 128)
        {
            errors[nameof(query.CorrelationId)] = ["CorrelationId must be 128 characters or fewer."];
        }

        if (query.SelectedActivityEventId == Guid.Empty)
        {
            errors[nameof(query.SelectedActivityEventId)] = ["SelectedActivityEventId cannot be empty."];
        }

        if (errors.Count > 0)
        {
            throw new ActivityFeedValidationException(errors);
        }
    }

    private static void AddRequired(IDictionary<string, string[]> errors, string name, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[name] = [$"{name} is required."];
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[name] = [$"{name} must be {maxLength} characters or fewer."];
        }
    }

    private static void AddOptional(IDictionary<string, string[]> errors, string name, string? value, int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
        {
            errors[name] = [$"{name} must be {maxLength} characters or fewer."];
        }
    }

    private static ActivityFeedValidationException BuildValidationException(string key, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [key] = [message] });

    private ActivityEventDto ToDto(ActivityEvent activityEvent, IReadOnlySet<Guid> tenantAuditEventIds)
    {
        var source = CloneNodes(activityEvent.SourceMetadata);
        var rawPayload = CloneNodes(activityEvent.SourceMetadata);
        return new ActivityEventDto(
            activityEvent.Id,
            activityEvent.CompanyId,
            activityEvent.AgentId,
            activityEvent.EventType,
            activityEvent.OccurredUtc,
            activityEvent.Status,
            activityEvent.Summary,
            activityEvent.CorrelationId,
            source,
            rawPayload,
            _summaryFormatter.Format(activityEvent.EventType, activityEvent.Status, activityEvent.Summary, rawPayload),
            activityEvent.Department,
            activityEvent.TaskId,
            activityEvent.AuditEventId is Guid auditEventId && tenantAuditEventIds.Contains(auditEventId)
                ? new ActivityAuditLinkDto(auditEventId, $"/audit/{auditEventId}?companyId={activityEvent.CompanyId}")
                : null);
    }

    private async Task<Guid?> ResolveTenantAuditEventIdAsync(Guid tenantId, Guid? auditEventId, CancellationToken cancellationToken)
    {
        if (auditEventId is not Guid id)
        {
            return null;
        }

        return await _dbContext.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == tenantId && x.Id == id, cancellationToken)
            ? id
            : null;
    }

    private async Task<IReadOnlySet<Guid>> ResolveLinkedAuditIdsAsync(Guid tenantId, IEnumerable<ActivityEvent> activityEvents, CancellationToken cancellationToken)
    {
        var ids = activityEvents.Select(x => x.AuditEventId).OfType<Guid>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var tenantIds = await _dbContext.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == tenantId && ids.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        return tenantIds.ToHashSet();
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?> source) =>
        source.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<ActivityEntityReferenceDto> ExtractReferences(IReadOnlyDictionary<string, JsonNode?> source)
    {
        var references = new List<ActivityEntityReferenceDto>();

        AddTypedReference(references, ReadString(source, "targetEntityType", "entityType", "targetType"), ReadGuid(source, "targetEntityId", "entityId", "targetId"));
        AddKnownReference(references, ActivityEntityTypes.Task, ReadGuid(source, "taskId", "workTaskId"));
        AddKnownReference(references, ActivityEntityTypes.WorkflowInstance, ReadGuid(source, "workflowInstanceId", "workflowId"));
        AddKnownReference(references, ActivityEntityTypes.Approval, ReadGuid(source, "approvalRequestId", "approvalId"));
        AddKnownReference(references, ActivityEntityTypes.ToolExecution, ReadGuid(source, "toolExecutionAttemptId", "toolExecutionId"));

        if (source.TryGetValue("linkedEntities", out var linkedEntitiesNode) && linkedEntitiesNode is JsonArray linkedEntities)
        {
            foreach (var node in linkedEntities.OfType<JsonObject>())
            {
                AddTypedReference(
                    references,
                    ReadString(node, "entityType", "type", "targetEntityType"),
                    ReadGuid(node, "entityId", "id", "targetEntityId"));
            }
        }

        return references
            .Where(x => x.EntityId != Guid.Empty && ActivityEntityTypes.IsSupported(x.EntityType))
            .GroupBy(x => ActivityEntityTypes.ToKey(x.EntityType, x.EntityId), StringComparer.OrdinalIgnoreCase)
            .Select(x => new ActivityEntityReferenceDto(ActivityEntityTypes.Normalize(x.First().EntityType), x.First().EntityId))
            .ToList();
    }

    private static void AddKnownReference(List<ActivityEntityReferenceDto> references, string entityType, Guid? entityId)
    {
        if (entityId is Guid value && value != Guid.Empty)
        {
            references.Add(new ActivityEntityReferenceDto(entityType, value));
        }
    }

    private static void AddTypedReference(List<ActivityEntityReferenceDto> references, string? entityType, Guid? entityId)
    {
        if (!string.IsNullOrWhiteSpace(entityType) && entityId is Guid value && value != Guid.Empty)
        {
            references.Add(new ActivityEntityReferenceDto(ActivityEntityTypes.Normalize(entityType), value));
        }
    }

    private static string? ReadString(IReadOnlyDictionary<string, JsonNode?> source, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!source.TryGetValue(key, out var node) || node is null)
            {
                continue;
            }

            try
            {
                return node.GetValue<string>();
            }
            catch (InvalidOperationException)
            {
                return node.ToJsonString().Trim('"');
            }
        }

        return null;
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, JsonNode?> source, params string[] keys)
    {
        var value = ReadString(source, keys);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    private static string? ReadString(JsonObject source, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (source.TryGetPropertyValue(key, out var node) && node is not null)
            {
                return ReadString(new Dictionary<string, JsonNode?> { [key] = node }, key);
            }
        }

        return null;
    }

    private static Guid? ReadGuid(JsonObject source, params string[] keys) =>
        Guid.TryParse(ReadString(source, keys), out var id) ? id : null;

    private static Guid? ExtractAuditEventId(IReadOnlyDictionary<string, JsonNode?>? source) =>
        source is null ? null : ReadGuid(source, "auditEventId", "auditId");

    private static Guid? ExtractTaskId(IReadOnlyDictionary<string, JsonNode?>? source)
    {
        if (source is null)
        {
            return null;
        }

        var direct = ReadGuid(source, "taskId", "workTaskId");
        if (direct is not null)
        {
            return direct;
        }

        var sourceType = ReadString(source, "sourceType", "targetEntityType", "entityType");
        return sourceType is not null &&
               (sourceType.Equals("task", StringComparison.OrdinalIgnoreCase) ||
                sourceType.Equals(ActivityEntityTypes.Task, StringComparison.OrdinalIgnoreCase) ||
                sourceType.Equals("work_task", StringComparison.OrdinalIgnoreCase))
            ? ReadGuid(source, "sourceId", "targetEntityId", "entityId")
            : null;
    }

    private static DateTime NormalizeQueryUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static bool IsSupportedTimeframe(string? timeframe)
    {
        if (string.IsNullOrWhiteSpace(timeframe))
        {
            return true;
        }

        return NormalizeTimeframe(timeframe) is "all" or "lastHour" or "last24Hours" or "last7Days" or "last30Days" or "custom";
    }

    private static (DateTime? FromUtc, DateTime? ToUtc) ResolveTimeframeRange(ActivityFeedQuery query)
    {
        var fromUtc = query.FromUtc.HasValue ? NormalizeQueryUtc(query.FromUtc.Value) : (DateTime?)null;
        var toUtc = query.ToUtc.HasValue ? NormalizeQueryUtc(query.ToUtc.Value) : (DateTime?)null;
        var timeframe = NormalizeTimeframe(query.Timeframe);

        if (timeframe is null or "all" or "custom" || fromUtc.HasValue || toUtc.HasValue)
        {
            return (fromUtc, toUtc);
        }

        var now = DateTime.UtcNow;
        return timeframe switch
        {
            "lastHour" => (now.AddHours(-1), now),
            "last24Hours" => (now.AddHours(-24), now),
            "last7Days" => (now.AddDays(-7), now),
            "last30Days" => (now.AddDays(-30), now),
            _ => (fromUtc, toUtc)
        };
    }

    private static string? NormalizeTimeframe(string? timeframe)
    {
        if (string.IsNullOrWhiteSpace(timeframe))
        {
            return null;
        }

        return timeframe.Trim() switch
        {
            "last-hour" => "lastHour",
            "last-24-hours" => "last24Hours",
            "last-7-days" => "last7Days",
            "last-30-days" => "last30Days",
            var value => value
        };
    }

    private static string EncodeCursor(DateTime occurredAt, Guid eventId)
    {
        var json = JsonSerializer.Serialize(new ActivityFeedCursor(occurredAt, eventId), CursorJsonOptions);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static ActivityFeedCursor DecodeCursor(string cursor)
    {
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var decoded = JsonSerializer.Deserialize<ActivityFeedCursor>(json, CursorJsonOptions);
            if (decoded is null || decoded.EventId == Guid.Empty || decoded.OccurredAt == default)
            {
                throw BuildValidationException(nameof(ActivityFeedQuery.Cursor), "Cursor is invalid.");
            }

            return decoded with
            {
                OccurredAt = decoded.OccurredAt.Kind == DateTimeKind.Utc
                    ? decoded.OccurredAt
                    : DateTime.SpecifyKind(decoded.OccurredAt, DateTimeKind.Utc)
            };
        }
        catch (Exception ex) when (ex is FormatException or JsonException or NotSupportedException)
        {
            throw BuildValidationException(nameof(ActivityFeedQuery.Cursor), "Cursor is invalid.");
        }
    }

    private sealed record ActivityFeedCursor(DateTime OccurredAt, Guid EventId);
}
