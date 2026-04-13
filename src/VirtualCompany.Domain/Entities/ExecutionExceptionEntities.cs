using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class ExecutionExceptionRecord : ICompanyOwnedEntity
{
    private const int TitleMaxLength = 200;
    private const int SummaryMaxLength = 2000;
    private const int SourceIdMaxLength = 128;
    private const int RelatedEntityTypeMaxLength = 100;
    private const int RelatedEntityIdMaxLength = 128;
    private const int IncidentKeyMaxLength = 300;
    private const int FailureCodeMaxLength = 200;

    private ExecutionExceptionRecord()
    {
        Details = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }

    public ExecutionExceptionRecord(
        Guid id,
        Guid companyId,
        ExecutionExceptionKind kind,
        ExecutionExceptionSeverity severity,
        string title,
        string summary,
        ExecutionExceptionSourceType sourceType,
        string sourceId,
        Guid? backgroundExecutionId,
        string? relatedEntityType,
        string? relatedEntityId,
        string incidentKey,
        string? failureCode,
        IReadOnlyDictionary<string, string?>? details = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (backgroundExecutionId == Guid.Empty)
        {
            throw new ArgumentException("BackgroundExecutionId cannot be empty.", nameof(backgroundExecutionId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Kind = kind;
        Severity = severity;
        Status = ExecutionExceptionStatus.Open;
        Title = NormalizeRequired(title, nameof(title), TitleMaxLength);
        Summary = NormalizeRequired(summary, nameof(summary), SummaryMaxLength);
        SourceType = sourceType;
        SourceId = NormalizeRequired(sourceId, nameof(sourceId), SourceIdMaxLength);
        BackgroundExecutionId = backgroundExecutionId;
        RelatedEntityType = NormalizeOptional(relatedEntityType, nameof(relatedEntityType), RelatedEntityTypeMaxLength);
        RelatedEntityId = NormalizeOptional(relatedEntityId, nameof(relatedEntityId), RelatedEntityIdMaxLength);
        IncidentKey = NormalizeRequired(incidentKey, nameof(incidentKey), IncidentKeyMaxLength);
        FailureCode = NormalizeOptional(failureCode, nameof(failureCode), FailureCodeMaxLength);
        Details = Clone(details);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public ExecutionExceptionKind Kind { get; private set; }
    public ExecutionExceptionSeverity Severity { get; private set; }
    public ExecutionExceptionStatus Status { get; private set; }
    public string Title { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public ExecutionExceptionSourceType SourceType { get; private set; }
    public string SourceId { get; private set; } = null!;
    public Guid? BackgroundExecutionId { get; private set; }
    public string? RelatedEntityType { get; private set; }
    public string? RelatedEntityId { get; private set; }
    public string IncidentKey { get; private set; } = null!;
    public string? FailureCode { get; private set; }
    public Dictionary<string, string?> Details { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public BackgroundExecution? BackgroundExecution { get; private set; }

    public void RefreshOpen(
        ExecutionExceptionSeverity severity,
        string title,
        string summary,
        string? failureCode,
        IReadOnlyDictionary<string, string?>? details)
    {
        if (Status != ExecutionExceptionStatus.Open)
        {
            return;
        }

        Severity = severity;
        Title = NormalizeRequired(title, nameof(title), TitleMaxLength);
        Summary = NormalizeRequired(summary, nameof(summary), SummaryMaxLength);
        FailureCode = NormalizeOptional(failureCode, nameof(failureCode), FailureCodeMaxLength);
        Details = Clone(details);
        UpdatedUtc = DateTime.UtcNow;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return NormalizeOptional(value, name, maxLength)!;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static Dictionary<string, string?> Clone(IReadOnlyDictionary<string, string?>? details) =>
        details is null || details.Count == 0
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : details.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
}