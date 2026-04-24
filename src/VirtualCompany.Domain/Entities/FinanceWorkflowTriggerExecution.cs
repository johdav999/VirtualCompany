using System.Text.Json;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceWorkflowTriggerExecution : ICompanyOwnedEntity
{
    private FinanceWorkflowTriggerExecution()
    {
    }

    public FinanceWorkflowTriggerExecution(
        Guid id,
        Guid companyId,
        string triggerType,
        string sourceEntityType,
        string sourceEntityId,
        string sourceEntityVersion,
        DateTime occurredAtUtc,
        DateTime startedAtUtc,
        string? correlationId = null,
        string? eventId = null,
        string? causationId = null,
        string? triggerMessageId = null,
        string? metadataJson = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        TriggerType = NormalizeRequired(triggerType, nameof(triggerType), 64);
        SourceEntityType = NormalizeRequired(sourceEntityType, nameof(sourceEntityType), 128);
        SourceEntityId = NormalizeRequired(sourceEntityId, nameof(sourceEntityId), 128);
        SourceEntityVersion = NormalizeRequired(sourceEntityVersion, nameof(sourceEntityVersion), 256);
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        EventId = NormalizeOptional(eventId, nameof(eventId), 200);
        CausationId = NormalizeOptional(causationId, nameof(causationId), 128);
        TriggerMessageId = NormalizeOptional(triggerMessageId, nameof(triggerMessageId), 64);
        OccurredAtUtc = EntityTimestampNormalizer.NormalizeUtc(occurredAtUtc, nameof(occurredAtUtc));
        StartedAtUtc = EntityTimestampNormalizer.NormalizeUtc(startedAtUtc, nameof(startedAtUtc));
        ExecutedChecksJson = "[]";
        MetadataJson = NormalizeJson(metadataJson, "{}");
        Outcome = "pending";
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string TriggerType { get; private set; } = null!;
    public string SourceEntityType { get; private set; } = null!;
    public string SourceEntityId { get; private set; } = null!;
    public string SourceEntityVersion { get; private set; } = null!;
    public string? CorrelationId { get; private set; }
    public string? EventId { get; private set; }
    public string? CausationId { get; private set; }
    public string? TriggerMessageId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string ExecutedChecksJson { get; private set; } = "[]";
    public string Outcome { get; private set; } = null!;
    public string MetadataJson { get; private set; } = "{}";
    public string? ErrorDetails { get; private set; }
    public Company Company { get; private set; } = null!;

    public void Complete(
        IReadOnlyCollection<string> executedChecks,
        string outcome,
        DateTime completedAtUtc,
        string? errorDetails = null)
    {
        ExecutedChecksJson = JsonSerializer.Serialize(
            executedChecks
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        Outcome = NormalizeRequired(outcome, nameof(outcome), 32);
        CompletedAtUtc = EntityTimestampNormalizer.NormalizeUtc(completedAtUtc, nameof(completedAtUtc));
        ErrorDetails = NormalizeOptional(errorDetails, nameof(errorDetails), 4000);
    }

    public void UpdateMetadataJson(string? metadataJson)
    {
        MetadataJson = NormalizeJson(metadataJson, "{}");
    }

    public IReadOnlyList<string> GetExecutedChecks()
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(ExecutedChecksJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
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