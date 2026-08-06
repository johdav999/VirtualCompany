using System.Text.Json;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceIntegrationProviderConfigurationAudit
{
    private FinanceIntegrationProviderConfigurationAudit()
    {
    }

    public FinanceIntegrationProviderConfigurationAudit(
        Guid id,
        string providerKey,
        Guid actorUserId,
        string action,
        string outcome,
        string summary,
        IReadOnlyCollection<string> changedFields,
        string? correlationId,
        DateTime occurredUtc)
    {
        if (actorUserId == Guid.Empty)
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        ProviderKey = Normalize(providerKey, nameof(providerKey), 64).ToLowerInvariant();
        ActorUserId = actorUserId;
        Action = Normalize(action, nameof(action), 64);
        Outcome = Normalize(outcome, nameof(outcome), 32);
        Summary = Normalize(summary, nameof(summary), 1000);
        ChangedFieldsJson = JsonSerializer.Serialize(
            changedFields
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Normalize(value, nameof(changedFields), 128))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        CorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? null
            : Normalize(correlationId, nameof(correlationId), 128);
        OccurredUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc));
    }

    public Guid Id { get; private set; }
    public string ProviderKey { get; private set; } = string.Empty;
    public Guid ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string Outcome { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public string ChangedFieldsJson { get; private set; } = "[]";
    public string? CorrelationId { get; private set; }
    public DateTime OccurredUtc { get; private set; }

    public IReadOnlyCollection<string> GetChangedFields() =>
        JsonSerializer.Deserialize<string[]>(ChangedFieldsJson) ?? [];

    private static string Normalize(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return normalized;
    }
}
