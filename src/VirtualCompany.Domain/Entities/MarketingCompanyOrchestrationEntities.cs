using System.Text.Json;

namespace VirtualCompany.Domain.Entities;

public sealed class MarketingWorkEvidence : ICompanyOwnedEntity
{
    private MarketingWorkEvidence() { }

    public MarketingWorkEvidence(Guid id, Guid companyId, Guid marketingOperatingRunId,
        Guid operatingInitiativeId, Guid? workTaskId, string recordType, int version,
        string idempotencyKey, string evidenceVersion, string completedArtifactsJson,
        string expectedResultsJson, string actualResultsJson, decimal? confidence,
        string dataGapsJson, string blockersJson, string dependenciesJson,
        string changedForecastJson, string lessons, string requestedNextAction,
        string correlationId)
    {
        if (companyId == Guid.Empty || marketingOperatingRunId == Guid.Empty || operatingInitiativeId == Guid.Empty)
            throw new ArgumentException("Company, Marketing run, and initiative are required.");
        if (workTaskId == Guid.Empty) throw new ArgumentException("Work task cannot be empty.", nameof(workTaskId));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        if (confidence is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(confidence));
        var normalizedType = Required(recordType, nameof(recordType), 24).ToLowerInvariant();
        if (normalizedType is not ("progress" or "outcome")) throw new ArgumentException("Record type must be progress or outcome.", nameof(recordType));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MarketingOperatingRunId = marketingOperatingRunId;
        OperatingInitiativeId = operatingInitiativeId;
        WorkTaskId = workTaskId;
        RecordType = normalizedType;
        Version = version;
        IdempotencyKey = Required(idempotencyKey, nameof(idempotencyKey), 200);
        EvidenceVersion = Required(evidenceVersion, nameof(evidenceVersion), 100);
        CompletedArtifactsJson = Json(completedArtifactsJson, nameof(completedArtifactsJson));
        ExpectedResultsJson = Json(expectedResultsJson, nameof(expectedResultsJson));
        ActualResultsJson = Json(actualResultsJson, nameof(actualResultsJson));
        Confidence = confidence;
        DataGapsJson = Json(dataGapsJson, nameof(dataGapsJson));
        BlockersJson = Json(blockersJson, nameof(blockersJson));
        DependenciesJson = Json(dependenciesJson, nameof(dependenciesJson));
        ChangedForecastJson = Json(changedForecastJson, nameof(changedForecastJson));
        Lessons = Required(lessons, nameof(lessons), 4000);
        RequestedNextAction = Required(requestedNextAction, nameof(requestedNextAction), 2000);
        CorrelationId = Required(correlationId, nameof(correlationId), 128);
        CreatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid MarketingOperatingRunId { get; private set; }
    public Guid OperatingInitiativeId { get; private set; }
    public Guid? WorkTaskId { get; private set; }
    public string RecordType { get; private set; } = null!;
    public int Version { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string EvidenceVersion { get; private set; } = null!;
    public string CompletedArtifactsJson { get; private set; } = null!;
    public string ExpectedResultsJson { get; private set; } = null!;
    public string ActualResultsJson { get; private set; } = null!;
    public decimal? Confidence { get; private set; }
    public string DataGapsJson { get; private set; } = null!;
    public string BlockersJson { get; private set; } = null!;
    public string DependenciesJson { get; private set; } = null!;
    public string ChangedForecastJson { get; private set; } = null!;
    public string Lessons { get; private set; } = null!;
    public string RequestedNextAction { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }

    private static string Required(string value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= max ? normalized : throw new ArgumentOutOfRangeException(name);
    }

    private static string Json(string value, string name)
    {
        var normalized = Required(value, name, 32000);
        try { using var _ = JsonDocument.Parse(normalized); }
        catch (JsonException exception) { throw new ArgumentException($"{name} must contain valid JSON.", name, exception); }
        return normalized;
    }
}

public sealed class MarketingCompanySignal : ICompanyOwnedEntity
{
    private static readonly IReadOnlySet<string> SupportedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "opportunity", "risk", "segment_change", "provider_failure", "product_evidence",
        "customer_evidence", "budget_need", "cross_functional_dependency"
    };

    private MarketingCompanySignal() { }

    public MarketingCompanySignal(Guid id, Guid companyId, Guid? marketingOperatingRunId,
        string signalType, string severity, string summary, string evidenceJson,
        string idempotencyKey, string correlationId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company is required.", nameof(companyId));
        if (marketingOperatingRunId == Guid.Empty) throw new ArgumentException("Marketing run cannot be empty.", nameof(marketingOperatingRunId));
        var type = Required(signalType, nameof(signalType), 64).ToLowerInvariant();
        if (!SupportedTypes.Contains(type)) throw new ArgumentException("Unsupported Marketing company signal type.", nameof(signalType));
        var normalizedSeverity = Required(severity, nameof(severity), 24).ToLowerInvariant();
        if (normalizedSeverity is not ("info" or "warning" or "high" or "critical"))
            throw new ArgumentException("Unsupported signal severity.", nameof(severity));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        MarketingOperatingRunId = marketingOperatingRunId;
        SignalType = type;
        Severity = normalizedSeverity;
        Summary = Required(summary, nameof(summary), 2000);
        EvidenceJson = ValidateJson(evidenceJson, nameof(evidenceJson));
        IdempotencyKey = Required(idempotencyKey, nameof(idempotencyKey), 200);
        CorrelationId = Required(correlationId, nameof(correlationId), 128);
        Status = "pending";
        CycleEvaluationRequested = true;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? MarketingOperatingRunId { get; private set; }
    public string SignalType { get; private set; } = null!;
    public string Severity { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public string EvidenceJson { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public bool CycleEvaluationRequested { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public void MarkEvaluated() { Status = "evaluated"; CycleEvaluationRequested = false; UpdatedUtc = DateTime.UtcNow; }
    public void Dismiss() { Status = "dismissed"; CycleEvaluationRequested = false; UpdatedUtc = DateTime.UtcNow; }

    private static string Required(string value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= max ? normalized : throw new ArgumentOutOfRangeException(name);
    }

    private static string ValidateJson(string value, string name)
    {
        var normalized = Required(value, name, 32000);
        try { using var _ = JsonDocument.Parse(normalized); }
        catch (JsonException exception) { throw new ArgumentException($"{name} must contain valid JSON.", name, exception); }
        return normalized;
    }
}
