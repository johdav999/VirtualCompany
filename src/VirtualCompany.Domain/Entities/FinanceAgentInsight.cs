using System.Text.Json;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceAgentInsight : ICompanyOwnedEntity
{
    private const int CheckCodeMaxLength = 128;
    private const int ConditionKeyMaxLength = 256;
    private const int MessageMaxLength = 4000;
    private const int RecommendationMaxLength = 4000;
    private const int EntityTypeMaxLength = 64;
    private const int EntityIdMaxLength = 128;
    private const int EntityNameMaxLength = 256;
    private const int MetadataMaxLength = 8000;

    private FinanceAgentInsight()
    {
    }

    public FinanceAgentInsight(
        Guid id,
        Guid companyId,
        string checkCode,
        string conditionKey,
        string entityType,
        string entityId,
        FinancialCheckSeverity severity,
        string message,
        string recommendation,
        decimal confidence,
        string? entityDisplayName,
        string affectedEntitiesJson,
        string? metadataJson,
        FinanceInsightStatus status,
        DateTime observedUtc,
        DateTime createdUtc,
        DateTime updatedUtc,
        DateTime? resolvedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CheckCode = NormalizeRequired(checkCode, nameof(checkCode), CheckCodeMaxLength);
        ConditionKey = NormalizeRequired(conditionKey, nameof(conditionKey), ConditionKeyMaxLength).ToLowerInvariant();
        EntityType = NormalizeEntityType(entityType);
        EntityId = NormalizeRequired(entityId, nameof(entityId), EntityIdMaxLength);
        Severity = severity;
        Message = NormalizeRequired(message, nameof(message), MessageMaxLength);
        Recommendation = NormalizeRequired(recommendation, nameof(recommendation), RecommendationMaxLength);
        Confidence = NormalizeConfidence(confidence);
        EntityDisplayName = NormalizeOptional(entityDisplayName, nameof(entityDisplayName), EntityNameMaxLength);
        AffectedEntitiesJson = NormalizeJson(affectedEntitiesJson, "[]");
        MetadataJson = NormalizeJson(metadataJson, "{}");
        Status = status;
        ObservedUtc = EntityTimestampNormalizer.NormalizeUtc(observedUtc, nameof(observedUtc));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        ResolvedUtc = resolvedUtc.HasValue
            ? EntityTimestampNormalizer.NormalizeUtc(resolvedUtc.Value, nameof(resolvedUtc))
            : status == FinanceInsightStatus.Resolved
                ? UpdatedUtc
                : null;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string CheckCode { get; private set; } = null!;
    public string ConditionKey { get; private set; } = null!;
    public string InsightKey => ConditionKey;
    public string EntityType { get; private set; } = null!;
    public string EntityId { get; private set; } = null!;
    public FinancialCheckSeverity Severity { get; private set; }
    public string Message { get; private set; } = null!;
    public string Recommendation { get; private set; } = null!;
    public decimal Confidence { get; private set; }
    public string? EntityDisplayName { get; private set; }
    public string AffectedEntitiesJson { get; private set; } = "[]";
    public string MetadataJson { get; private set; } = "{}";
    public FinanceInsightStatus Status { get; private set; }
    public DateTime ObservedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public void Refresh(
        string entityType,
        string entityId,
        FinancialCheckSeverity severity,
        string message,
        string recommendation,
        decimal confidence,
        string? entityDisplayName,
        string affectedEntitiesJson,
        string? metadataJson,
        DateTime observedUtc,
        DateTime updatedUtc)
    {
        EntityType = NormalizeEntityType(entityType);
        EntityId = NormalizeRequired(entityId, nameof(entityId), EntityIdMaxLength);
        Severity = severity;
        Message = NormalizeRequired(message, nameof(message), MessageMaxLength);
        Recommendation = NormalizeRequired(recommendation, nameof(recommendation), RecommendationMaxLength);
        Confidence = NormalizeConfidence(confidence);
        EntityDisplayName = NormalizeOptional(entityDisplayName, nameof(entityDisplayName), EntityNameMaxLength);
        AffectedEntitiesJson = NormalizeJson(affectedEntitiesJson, "[]");
        MetadataJson = NormalizeJson(metadataJson, "{}");
        Status = FinanceInsightStatus.Active;
        ObservedUtc = EntityTimestampNormalizer.NormalizeUtc(observedUtc, nameof(observedUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        ResolvedUtc = null;
    }

    public void MarkResolved(DateTime observedUtc, DateTime updatedUtc)
    {
        Status = FinanceInsightStatus.Resolved;
        ObservedUtc = EntityTimestampNormalizer.NormalizeUtc(observedUtc, nameof(observedUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        ResolvedUtc = UpdatedUtc;
    }

    private static decimal NormalizeConfidence(decimal value)
    {
        if (value is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Confidence must be between 0 and 1.");
        }

        return decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeEntityType(string value) =>
        NormalizeRequired(value, nameof(value), EntityTypeMaxLength).ToLowerInvariant();

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

    private static string NormalizeJson(string? value, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        using var document = JsonDocument.Parse(value);
        var normalized = document.RootElement.GetRawText();
        return string.IsNullOrWhiteSpace(normalized)
            ? defaultValue
            : normalized;
    }
}