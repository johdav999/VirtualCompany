namespace VirtualCompany.Domain.Entities;

public sealed class AgentTaskCreationDedupeRecord : ICompanyOwnedEntity
{
    private const int DedupeKeyMaxLength = 128;
    private const int TriggerSourceMaxLength = 128;
    private const int TriggerEventIdMaxLength = 200;
    private const int CorrelationIdMaxLength = 128;

    private AgentTaskCreationDedupeRecord()
    {
    }

    public AgentTaskCreationDedupeRecord(
        Guid id,
        Guid companyId,
        string dedupeKey,
        Guid taskId,
        Guid agentId,
        string triggerSource,
        string triggerEventId,
        string correlationId,
        DateTime createdUtc,
        DateTime expiresUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (taskId == Guid.Empty)
        {
            throw new ArgumentException("TaskId is required.", nameof(taskId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("AgentId is required.", nameof(agentId));
        }

        if (expiresUtc <= createdUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresUtc), "ExpiresUtc must be after CreatedUtc.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DedupeKey = NormalizeRequired(dedupeKey, nameof(dedupeKey), DedupeKeyMaxLength);
        TaskId = taskId;
        AgentId = agentId;
        TriggerSource = NormalizeRequired(triggerSource, nameof(triggerSource), TriggerSourceMaxLength);
        TriggerEventId = NormalizeRequired(triggerEventId, nameof(triggerEventId), TriggerEventIdMaxLength);
        CorrelationId = NormalizeRequired(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        CreatedUtc = EnsureUtc(createdUtc);
        ExpiresUtc = EnsureUtc(expiresUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string DedupeKey { get; private set; } = null!;
    public Guid TaskId { get; private set; }
    public Guid AgentId { get; private set; }
    public string TriggerSource { get; private set; } = null!;
    public string TriggerEventId { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public WorkTask Task { get; private set; } = null!;
    public Agent Agent { get; private set; } = null!;

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

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        if (value.Kind == DateTimeKind.Local)
        {
            return value.ToUniversalTime();
        }

        return DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}