using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;

public sealed class ActivityEvent : ICompanyOwnedEntity
{
    private const int EventTypeMaxLength = 100;
    private const int StatusMaxLength = 64;
    private const int SummaryMaxLength = 500;
    private const int CorrelationIdMaxLength = 128;
    private const int DepartmentMaxLength = 100;

    private ActivityEvent()
    {
        SourceMetadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
    }

    public ActivityEvent(
        Guid id,
        Guid companyId,
        Guid? agentId,
        string eventType,
        DateTime occurredUtc,
        string status,
        string summary,
        string? correlationId,
        IReadOnlyDictionary<string, JsonNode?>? sourceMetadata,
        string? department = null,
        Guid? taskId = null,
        Guid? auditEventId = null,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("AgentId cannot be empty.", nameof(agentId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AgentId = agentId;
        EventType = NormalizeRequired(eventType, nameof(eventType), EventTypeMaxLength);
        OccurredUtc = NormalizeUtc(occurredUtc, nameof(occurredUtc));
        Status = NormalizeRequired(status, nameof(status), StatusMaxLength);
        Summary = NormalizeRequired(summary, nameof(summary), SummaryMaxLength);
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        Department = NormalizeOptional(department, nameof(department), DepartmentMaxLength);
        TaskId = taskId == Guid.Empty ? throw new ArgumentException("TaskId cannot be empty.", nameof(taskId)) : taskId;
        AuditEventId = auditEventId == Guid.Empty ? throw new ArgumentException("AuditEventId cannot be empty.", nameof(auditEventId)) : auditEventId;
        SourceMetadata = CloneNodes(sourceMetadata);
        CreatedUtc = NormalizeUtc(createdUtc ?? OccurredUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? AgentId { get; private set; }
    public string EventType { get; private set; } = null!;
    public DateTime OccurredUtc { get; private set; }
    public string Status { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string? CorrelationId { get; private set; }
    public Dictionary<string, JsonNode?> SourceMetadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Department { get; private set; }
    public Guid? TaskId { get; private set; }
    public Guid? AuditEventId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Agent? Agent { get; private set; }

    private static DateTime NormalizeUtc(DateTime value, string name)
    {
        if (value == default)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes)
    {
        if (nodes is null || nodes.Count == 0)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        return nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
    }
}
