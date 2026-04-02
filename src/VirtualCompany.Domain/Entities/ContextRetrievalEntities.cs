namespace VirtualCompany.Domain.Entities;

public sealed class ContextRetrieval : ICompanyOwnedEntity
{
    private ContextRetrieval()
    {
    }

    public ContextRetrieval(
        Guid id,
        Guid companyId,
        Guid agentId,
        Guid? actorUserId,
        Guid? taskId,
        string queryText,
        string queryHash,
        string? correlationId,
        string? retrievalPurpose,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (agentId == Guid.Empty)
        {
            throw new ArgumentException("AgentId is required.", nameof(agentId));
        }

        if (actorUserId.HasValue && actorUserId.Value == Guid.Empty)
        {
            throw new ArgumentException("ActorUserId cannot be empty.", nameof(actorUserId));
        }

        if (taskId.HasValue && taskId.Value == Guid.Empty)
        {
            throw new ArgumentException("TaskId cannot be empty.", nameof(taskId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AgentId = agentId;
        ActorUserId = actorUserId;
        TaskId = taskId;
        QueryText = NormalizeRequired(queryText, nameof(queryText), 4000);
        QueryHash = NormalizeRequired(queryHash, nameof(queryHash), 128);
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        RetrievalPurpose = NormalizeOptional(retrievalPurpose, nameof(retrievalPurpose), 256);
        CreatedUtc = createdUtc ?? DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public Guid? TaskId { get; private set; }
    public string QueryText { get; private set; } = null!;
    public string QueryHash { get; private set; } = null!;
    public string? CorrelationId { get; private set; }
    public string? RetrievalPurpose { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<ContextRetrievalSource> Sources { get; } = new List<ContextRetrievalSource>();

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
}

public sealed class ContextRetrievalSource : ICompanyOwnedEntity
{
    private ContextRetrievalSource()
    {
    }

    public ContextRetrievalSource(
        Guid id,
        Guid retrievalId,
        Guid companyId,
        string sourceType,
        string sourceEntityId,
        string? parentSourceType,
        string? parentSourceEntityId,
        string? parentTitle,
        string title,
        string snippet,
        string sectionId,
        string sectionTitle,
        int sectionRank,
        string? locator,
        int rank,
        double? score,
        DateTime? timestampUtc,
        IReadOnlyDictionary<string, string?>? metadata = null)
    {
        if (retrievalId == Guid.Empty)
        {
            throw new ArgumentException("RetrievalId is required.", nameof(retrievalId));
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (sectionRank <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sectionRank), "SectionRank must be greater than zero.");
        }

        if (rank <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rank), "Rank must be greater than zero.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        RetrievalId = retrievalId;
        CompanyId = companyId;
        SourceType = NormalizeRequired(sourceType, nameof(sourceType), 64);
        SourceEntityId = NormalizeRequired(sourceEntityId, nameof(sourceEntityId), 128);
        ParentSourceType = NormalizeOptional(parentSourceType, nameof(parentSourceType), 64);
        ParentSourceEntityId = NormalizeOptional(parentSourceEntityId, nameof(parentSourceEntityId), 128);
        ParentTitle = NormalizeOptional(parentTitle, nameof(parentTitle), 256);
        Title = NormalizeRequired(title, nameof(title), 256);
        Snippet = NormalizeRequired(snippet, nameof(snippet), 4000);
        SectionId = NormalizeRequired(sectionId, nameof(sectionId), 64);
        SectionTitle = NormalizeRequired(sectionTitle, nameof(sectionTitle), 128);
        SectionRank = sectionRank;
        Locator = NormalizeOptional(locator, nameof(locator), 512);
        Rank = rank;
        Score = score;
        TimestampUtc = timestampUtc;
        Metadata = NormalizeMetadata(metadata);
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid RetrievalId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string SourceType { get; private set; } = null!;
    public string SourceEntityId { get; private set; } = null!;
    public string? ParentSourceType { get; private set; }
    public string? ParentSourceEntityId { get; private set; }
    public string? ParentTitle { get; private set; }
    public string Title { get; private set; } = null!;
    public string Snippet { get; private set; } = null!;
    public string SectionId { get; private set; } = null!;
    public string SectionTitle { get; private set; } = null!;
    public int SectionRank { get; private set; }
    public string? Locator { get; private set; }
    public int Rank { get; private set; }
    public double? Score { get; private set; }
    public DateTime? TimestampUtc { get; private set; }
    public Dictionary<string, string?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ContextRetrieval Retrieval { get; private set; } = null!;

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
}