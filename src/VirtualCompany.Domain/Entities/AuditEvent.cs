namespace VirtualCompany.Domain.Entities;

public sealed class AuditEvent : ICompanyOwnedEntity
{
    private AuditEvent()
    {
    }

    public AuditEvent(
        Guid id,
        Guid companyId,
        string actorType,
        Guid? actorId,
        string action,
        string targetType,
        string targetId,
        string outcome,
        string? rationaleSummary = null,
        IEnumerable<string>? dataSources = null,
        IReadOnlyDictionary<string, string?>? metadata = null,
        string? correlationId = null,
        DateTime? occurredUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (actorId.HasValue && actorId.Value == Guid.Empty)
        {
            throw new ArgumentException("ActorId cannot be empty.", nameof(actorId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ActorType = NormalizeRequired(actorType, nameof(actorType), 64);
        ActorId = actorId;
        Action = NormalizeRequired(action, nameof(action), 128);
        TargetType = NormalizeRequired(targetType, nameof(targetType), 128);
        TargetId = NormalizeRequired(targetId, nameof(targetId), 128);
        Outcome = NormalizeRequired(outcome, nameof(outcome), 64);
        RationaleSummary = NormalizeOptional(rationaleSummary, nameof(rationaleSummary), 512);
        DataSources = NormalizeDataSources(dataSources);
        Metadata = NormalizeMetadata(metadata);
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        OccurredUtc = occurredUtc ?? DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ActorType { get; private set; } = null!;
    public Guid? ActorId { get; private set; }
    public string Action { get; private set; } = null!;
    public string TargetType { get; private set; } = null!;
    public string TargetId { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public string? RationaleSummary { get; private set; }
    public List<string> DataSources { get; private set; } = [];
    public Dictionary<string, string?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? CorrelationId { get; private set; }
    public DateTime OccurredUtc { get; private set; }
    public Company Company { get; private set; } = null!;

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

    private static List<string> NormalizeDataSources(IEnumerable<string>? dataSources)
    {
        if (dataSources is null)
        {
            return [];
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataSource in dataSources)
        {
            if (string.IsNullOrWhiteSpace(dataSource))
            {
                continue;
            }

            normalized.Add(NormalizeRequired(dataSource, nameof(dataSources), 64));
        }

        return normalized.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Dictionary<string, string?> NormalizeMetadata(IReadOnlyDictionary<string, string?>? metadata)
    {
        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (metadata is null)
        {
            return normalized;
        }

        foreach (var pair in metadata)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            normalized[NormalizeRequired(pair.Key, nameof(metadata), 100)] =
                NormalizeOptional(pair.Value, nameof(metadata), 512);
        }

        return normalized;
    }
}
