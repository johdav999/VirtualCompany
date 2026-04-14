using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class Alert : ICompanyOwnedEntity
{
    private const int TitleMaxLength = 200;
    private const int SummaryMaxLength = 2000;
    private const int CorrelationIdMaxLength = 128;
    private const int FingerprintMaxLength = 256;

    private Alert()
    {
        Evidence = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        Metadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
    }

    public Alert(
        Guid id,
        Guid companyId,
        AlertType type,
        AlertSeverity severity,
        string title,
        string summary,
        IReadOnlyDictionary<string, JsonNode?> evidence,
        string correlationId,
        string fingerprint,
        Guid? sourceAgentId = null,
        IReadOnlyDictionary<string, JsonNode?>? metadata = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (sourceAgentId == Guid.Empty)
        {
            throw new ArgumentException("SourceAgentId cannot be empty.", nameof(sourceAgentId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Type = type;
        Severity = severity;
        Status = AlertStatus.Open;
        Title = NormalizeRequired(title, nameof(title), TitleMaxLength);
        Summary = NormalizeRequired(summary, nameof(summary), SummaryMaxLength);
        Evidence = CloneRequiredEvidence(evidence);
        CorrelationId = NormalizeRequired(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        Fingerprint = NormalizeRequired(fingerprint, nameof(fingerprint), FingerprintMaxLength);
        SourceAgentId = sourceAgentId;
        OccurrenceCount = 1;
        Metadata = CloneNodes(metadata);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
        LastDetectedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public AlertType Type { get; private set; }
    public AlertSeverity Severity { get; private set; }
    public string Title { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public Dictionary<string, JsonNode?> Evidence { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public AlertStatus Status { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string Fingerprint { get; private set; } = null!;
    public Guid? SourceAgentId { get; private set; }
    public int OccurrenceCount { get; private set; }
    public int SourceLifecycleVersion { get; private set; }
    public Dictionary<string, JsonNode?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? LastDetectedUtc { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public DateTime? ClosedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Agent? SourceAgent { get; private set; }

    public void UpdateDetails(AlertSeverity severity, string title, string summary, IReadOnlyDictionary<string, JsonNode?> evidence, IReadOnlyDictionary<string, JsonNode?>? metadata)
    {
        Severity = severity;
        Title = NormalizeRequired(title, nameof(title), TitleMaxLength);
        Summary = NormalizeRequired(summary, nameof(summary), SummaryMaxLength);
        Evidence = CloneRequiredEvidence(evidence);
        Metadata = CloneNodes(metadata);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void UpdateStatus(AlertStatus status)
    {
        var wasResolved = Status is AlertStatus.Resolved or AlertStatus.Closed;
        var isReopened = status is AlertStatus.Open or AlertStatus.Acknowledged;

        Status = status;
        UpdatedUtc = DateTime.UtcNow;
        if (wasResolved && isReopened)
        {
            SourceLifecycleVersion++;
            ResolvedUtc = null;
            ClosedUtc = null;
            return;
        }

        ResolvedUtc = status == AlertStatus.Resolved ? UpdatedUtc : status == AlertStatus.Open ? null : ResolvedUtc;
        ClosedUtc = status == AlertStatus.Closed ? UpdatedUtc : status == AlertStatus.Open ? null : ClosedUtc;
    }

    public void RefreshFromDuplicateDetection(AlertSeverity severity, string title, string summary, IReadOnlyDictionary<string, JsonNode?> evidence, string correlationId, Guid? sourceAgentId, IReadOnlyDictionary<string, JsonNode?>? metadata)
    {
        if (!Status.IsOpenForDeduplication())
        {
            return;
        }

        Severity = severity;
        Title = NormalizeRequired(title, nameof(title), TitleMaxLength);
        Summary = NormalizeRequired(summary, nameof(summary), SummaryMaxLength);
        Evidence = CloneRequiredEvidence(evidence);
        CorrelationId = NormalizeRequired(correlationId, nameof(correlationId), CorrelationIdMaxLength);
        SourceAgentId = sourceAgentId;
        Metadata = CloneNodes(metadata);
        OccurrenceCount++;
        UpdatedUtc = DateTime.UtcNow;
        LastDetectedUtc = UpdatedUtc;
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

    private static Dictionary<string, JsonNode?> CloneRequiredEvidence(IReadOnlyDictionary<string, JsonNode?>? evidence)
    {
        if (evidence is null || evidence.Count == 0)
        {
            throw new ArgumentException("Evidence is required.", nameof(evidence));
        }

        return CloneNodes(evidence);
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}
