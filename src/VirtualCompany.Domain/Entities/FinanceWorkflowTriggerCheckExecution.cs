using System.Text.Json;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceWorkflowTriggerCheckExecution : ICompanyOwnedEntity
{
    private FinanceWorkflowTriggerCheckExecution()
    {
    }

    public FinanceWorkflowTriggerCheckExecution(
        Guid id,
        Guid companyId,
        Guid triggerExecutionId,
        string triggerType,
        string sourceEntityType,
        string sourceEntityId,
        string sourceEntityVersion,
        string checkType,
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

        if (triggerExecutionId == Guid.Empty)
        {
            throw new ArgumentException("TriggerExecutionId is required.", nameof(triggerExecutionId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        TriggerExecutionId = triggerExecutionId;
        TriggerType = NormalizeRequired(triggerType, nameof(triggerType), 64);
        SourceEntityType = NormalizeRequired(sourceEntityType, nameof(sourceEntityType), 128);
        SourceEntityId = NormalizeRequired(sourceEntityId, nameof(sourceEntityId), 128);
        SourceEntityVersion = NormalizeRequired(sourceEntityVersion, nameof(sourceEntityVersion), 256);
        CheckType = NormalizeRequired(checkType, nameof(checkType), 128);
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        EventId = NormalizeOptional(eventId, nameof(eventId), 200);
        CausationId = NormalizeOptional(causationId, nameof(causationId), 128);
        TriggerMessageId = NormalizeOptional(triggerMessageId, nameof(triggerMessageId), 64);
        StartedAtUtc = EntityTimestampNormalizer.NormalizeUtc(startedAtUtc, nameof(startedAtUtc));
        MetadataJson = NormalizeJson(metadataJson, "{}");
        Outcome = "pending";
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid TriggerExecutionId { get; private set; }
    public string TriggerType { get; private set; } = null!;
    public string SourceEntityType { get; private set; } = null!;
    public string SourceEntityId { get; private set; } = null!;
    public string SourceEntityVersion { get; private set; } = null!;
    public string CheckType { get; private set; } = null!;
    public string? CorrelationId { get; private set; }
    public string? EventId { get; private set; }
    public string? CausationId { get; private set; }
    public string? TriggerMessageId { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string Outcome { get; private set; } = null!;
    public string MetadataJson { get; private set; } = "{}";
    public string? ErrorDetails { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceWorkflowTriggerExecution TriggerExecution { get; private set; } = null!;

    public void Complete(
        string outcome,
        DateTime completedAtUtc,
        string? errorDetails = null)
    {
        Outcome = NormalizeRequired(outcome, nameof(outcome), 32);
        CompletedAtUtc = EntityTimestampNormalizer.NormalizeUtc(completedAtUtc, nameof(completedAtUtc));
        ErrorDetails = NormalizeOptional(errorDetails, nameof(errorDetails), 4000);
    }

    public void UpdateMetadataJson(string? metadataJson)
    {
        MetadataJson = NormalizeJson(metadataJson, "{}");
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
