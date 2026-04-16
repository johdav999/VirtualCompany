using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace VirtualCompany.Application.Activity;

public sealed record PersistActivityEventCommand(
    Guid TenantId,
    Guid? AgentId,
    string EventType,
    DateTime OccurredAt,
    string Status,
    string Summary,
    string? CorrelationId,
    [property: JsonPropertyName("source")] Dictionary<string, JsonNode?>? Source);

public sealed record ActivitySummaryDto(
    string EventType,
    string FormatterKey,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Actor,
    string Action,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Target,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Outcome,
    string SummaryText)
{
    public string Text => SummaryText;
}

public sealed record ActivityFeedQuery(
    string? Cursor = null,
    int? PageSize = null,
    Guid? AgentId = null,
    string? Department = null,
    Guid? TaskId = null,
    string? EventType = null,
    string? Status = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? Timeframe = null);

public sealed record ActivityEventDto(
    Guid EventId,
    Guid TenantId,
    Guid? AgentId,
    string EventType,
    DateTime OccurredAt,
    string Status,
    string Summary,
    string? CorrelationId,
    [property: JsonPropertyName("source")] Dictionary<string, JsonNode?> Source,
    Dictionary<string, JsonNode?> RawPayload,
    ActivitySummaryDto NormalizedSummary,
    string? Department,
    Guid? TaskId,
    ActivityAuditLinkDto? AuditLink);

public sealed record ActivityAuditLinkDto(Guid AuditEventId, string Href);

public sealed record ActivityEntityReferenceDto(
    string EntityType,
    Guid EntityId);

public sealed record ActivityLinkedEntityDto(
    string EntityType,
    Guid EntityId,
    string Availability,
    string DisplayText,
    string? CurrentStatus,
    DateTime? LastUpdatedAt,
    bool IsAvailable,
    string? UnavailableReason,
    Dictionary<string, JsonNode?> Metadata);

public sealed record ActivityTimelineItemDto(
    ActivityEventDto Activity,
    ActivityEntityReferenceDto? PrimaryTarget,
    IReadOnlyList<ActivityLinkedEntityDto> LinkedEntities);

public sealed record ActivityCorrelationTimelineQuery(
    string? CorrelationId = null,
    Guid? SelectedActivityEventId = null);

public sealed record ActivityCorrelationTimelineDto(
    Guid TenantId,
    string CorrelationId,
    IReadOnlyList<ActivityTimelineItemDto> Items,
    ActivityLinkedEntitiesDto? SelectedActivityLinks);

public sealed record ActivityLinkedEntitiesDto(
    Guid TenantId,
    Guid ActivityEventId,
    IReadOnlyList<ActivityLinkedEntityDto> LinkedEntities);

public static class ActivityLinkedEntityAvailability
{
    public const string Available = "available";
    public const string UnavailableMissing = "unavailable_missing";
    public const string UnavailableDeleted = "unavailable_deleted";
    public const string UnavailableForbidden = "unavailable_forbidden";
}

public sealed record ActivityFeedPageDto(
    IReadOnlyList<ActivityEventDto> Items,
    string? NextCursor);

public interface IActivityEventStore
{
    Task<ActivityEventDto> PersistAsync(Guid tenantId, PersistActivityEventCommand command, CancellationToken cancellationToken);
    Task<ActivityFeedPageDto> QueryFeedAsync(Guid tenantId, ActivityFeedQuery query, CancellationToken cancellationToken);
    Task<ActivityCorrelationTimelineDto> QueryCorrelationTimelineAsync(Guid tenantId, ActivityCorrelationTimelineQuery query, CancellationToken cancellationToken);
    Task<ActivityCorrelationTimelineDto> QueryCorrelationTimelineForActivityAsync(Guid tenantId, Guid activityEventId, CancellationToken cancellationToken);
    Task<ActivityLinkedEntitiesDto> QueryLinkedEntitiesAsync(Guid tenantId, Guid activityEventId, CancellationToken cancellationToken);
}

public interface IActivityEventSummaryFormatter
{
    ActivitySummaryDto Format(string eventType, string status, string? persistedSummary, IReadOnlyDictionary<string, JsonNode?>? rawPayload);
}

public interface IEntityLinkResolutionService
{
    Task<IReadOnlyDictionary<string, ActivityLinkedEntityDto>> ResolveAsync(Guid tenantId, IEnumerable<ActivityEntityReferenceDto> references, CancellationToken cancellationToken);
}

public interface IActivityEventPublisher
{
    Task PublishAsync(ActivityEventDto activityEvent, CancellationToken cancellationToken);
}

public sealed class ActivityFeedValidationException : Exception
{
    public ActivityFeedValidationException(IDictionary<string, string[]> errors) : base("Activity feed request validation failed.") =>
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
