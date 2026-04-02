using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Policies;

namespace VirtualCompany.Domain.Entities;

public sealed class MemoryItem : ICompanyOwnedEntity
{
    private const int SummaryMaxLength = 4000;
    private const int SourceEntityTypeMaxLength = 100;
    private const int ActorTypeMaxLength = 64;
    private const int LifecycleReasonMaxLength = 512;

    private MemoryItem()
    {
    }

    public MemoryItem(
        Guid id,
        Guid companyId,
        Guid? agentId,
        MemoryType memoryType,
        string summary,
        string? sourceEntityType,
        Guid? sourceEntityId,
        decimal salience,
        DateTime validFromUtc,
        DateTime? validToUtc,
        IDictionary<string, JsonNode?>? metadata = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (agentId.HasValue && agentId.Value == Guid.Empty)
        {
            throw new ArgumentException("AgentId cannot be empty.", nameof(agentId));
        }

        if (sourceEntityId.HasValue && sourceEntityId.Value == Guid.Empty)
        {
            throw new ArgumentException("SourceEntityId cannot be empty.", nameof(sourceEntityId));
        }

        MemoryTypeValues.EnsureSupported(memoryType, nameof(memoryType));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AgentId = agentId;
        MemoryType = memoryType;

        var normalizedSummary = NormalizeRequired(summary, nameof(summary), SummaryMaxLength);
        if (!MemoryContentSafetyPolicy.TryValidateSummary(normalizedSummary, out var summaryError))
        {
            throw new ArgumentException(summaryError, nameof(summary));
        }

        Summary = normalizedSummary;
        SourceEntityType = NormalizeOptional(sourceEntityType, nameof(sourceEntityType), SourceEntityTypeMaxLength);
        SourceEntityId = sourceEntityId;
        Salience = NormalizeSalience(salience, nameof(salience));
        ValidFromUtc = EnsureUtc(validFromUtc);
        ValidToUtc = validToUtc.HasValue ? EnsureUtc(validToUtc.Value) : null;

        if (ValidToUtc.HasValue && ValidToUtc.Value < ValidFromUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(validToUtc), "ValidToUtc must be greater than or equal to ValidFromUtc.");
        }

        Metadata = NormalizeMetadata(metadata);
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? AgentId { get; private set; }
    public MemoryType MemoryType { get; private set; }
    public string Summary { get; private set; } = null!;
    public string? SourceEntityType { get; private set; }
    public Guid? SourceEntityId { get; private set; }
    public decimal Salience { get; private set; }
    public Dictionary<string, JsonNode?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime ValidFromUtc { get; private set; }
    public DateTime? ValidToUtc { get; private set; }
    public string? Embedding { get; private set; }
    public string? EmbeddingProvider { get; private set; }
    public string? EmbeddingModel { get; private set; }
    public string? EmbeddingModelVersion { get; private set; }
    public int? EmbeddingDimensions { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? DeletedUtc { get; private set; }
    public string? DeletedByActorType { get; private set; }
    public Guid? DeletedByActorId { get; private set; }
    public string? DeletionReason { get; private set; }
    public string? ExpiredByActorType { get; private set; }
    public Guid? ExpiredByActorId { get; private set; }
    public string? ExpirationReason { get; private set; }
    public Company Company { get; private set; } = null!;
    public Agent? Agent { get; private set; }
    public bool IsCompanyWide => AgentId is null;
    public bool IsAgentSpecific => AgentId is not null;
    public bool IsDeleted => DeletedUtc.HasValue;
    public bool IsNotYetValid(DateTime asOfUtc) => ValidFromUtc > EnsureUtc(asOfUtc);

    public bool IsExpired(DateTime asOfUtc) =>
        ValidToUtc.HasValue &&
        EnsureUtc(ValidToUtc.Value) <= EnsureUtc(asOfUtc);

    public bool IsActive(DateTime asOfUtc)
    {
        var normalizedAsOfUtc = EnsureUtc(asOfUtc);
        return !IsDeleted &&
               ValidFromUtc <= normalizedAsOfUtc &&
               (!ValidToUtc.HasValue || ValidToUtc.Value > normalizedAsOfUtc);
    }

    public void AttachEmbedding(
        string embedding,
        string provider,
        string model,
        string? modelVersion,
        int dimensions)
    {
        if (dimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions));
        }

        Embedding = NormalizeRequired(embedding, nameof(embedding), 1024 * 1024);
        EmbeddingProvider = NormalizeRequired(provider, nameof(provider), 100);
        EmbeddingModel = NormalizeRequired(model, nameof(model), 200);
        EmbeddingModelVersion = NormalizeOptional(modelVersion, nameof(modelVersion), 100);
        EmbeddingDimensions = dimensions;
    }

    public bool Expire(DateTime validToUtc, string actorType, Guid? actorId, string? reason)
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("Deleted memory items cannot be expired.");
        }

        var normalizedValidToUtc = EnsureUtc(validToUtc);
        if (normalizedValidToUtc < ValidFromUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(validToUtc), "ValidToUtc must be greater than or equal to ValidFromUtc.");
        }

        if (ValidToUtc.HasValue && ValidToUtc.Value == normalizedValidToUtc)
        {
            ExpiredByActorType = NormalizeOptional(actorType, nameof(actorType), ActorTypeMaxLength);
            ExpiredByActorId = NormalizeActorId(actorId, nameof(actorId));
            ExpirationReason = NormalizeOptional(reason, nameof(reason), LifecycleReasonMaxLength);
            return false;
        }

        ValidToUtc = normalizedValidToUtc;
        ExpiredByActorType = NormalizeRequired(actorType, nameof(actorType), ActorTypeMaxLength);
        ExpiredByActorId = NormalizeActorId(actorId, nameof(actorId));
        ExpirationReason = NormalizeOptional(reason, nameof(reason), LifecycleReasonMaxLength);
        return true;
    }

    public bool Delete(string actorType, Guid? actorId, string? reason)
    {
        if (IsDeleted)
        {
            DeletedByActorType = NormalizeOptional(actorType, nameof(actorType), ActorTypeMaxLength);
            DeletedByActorId = NormalizeActorId(actorId, nameof(actorId));
            DeletionReason = NormalizeOptional(reason, nameof(reason), LifecycleReasonMaxLength);
            return false;
        }

        DeletedUtc = DateTime.UtcNow;
        DeletedByActorType = NormalizeRequired(actorType, nameof(actorType), ActorTypeMaxLength);
        DeletedByActorId = NormalizeActorId(actorId, nameof(actorId));
        DeletionReason = NormalizeOptional(reason, nameof(reason), LifecycleReasonMaxLength);
        return true;
    }

    private static Guid? NormalizeActorId(Guid? value, string name)
    {
        if (value.HasValue && value.Value == Guid.Empty)
        {
            throw new ArgumentException($"{name} cannot be empty.", name);
        }

        return value;
    }

    private static decimal NormalizeSalience(decimal value, string name)
    {
        if (value < 0m || value > 1m)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be between 0 and 1.");
        }

        return decimal.Round(value, 3, MidpointRounding.AwayFromZero);
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

        return NormalizeRequired(value, name, maxLength);
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private static Dictionary<string, JsonNode?> NormalizeMetadata(IDictionary<string, JsonNode?>? nodes)
    {
        var cloned = CloneNodes(nodes);
        if (!MemoryContentSafetyPolicy.TryValidateMetadata(cloned, out var metadataError))
        {
            throw new ArgumentException(metadataError, nameof(nodes));
        }

        return cloned;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes)
    {
        if (nodes is null || nodes.Count == 0)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        var cloned = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in nodes)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            cloned[pair.Key.Trim()] = pair.Value?.DeepClone();
        }

        return cloned;
    }
}