namespace VirtualCompany.Domain.Entities;

public static class AccountingProviderSwitchAssessmentStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class AccountingProviderSwitchCapabilityLevels
{
    public const string Supported = "supported";
    public const string Partial = "partial";
    public const string Unsupported = "unsupported";
    public const string Unknown = "unknown";

    public static string Normalize(string value) => value?.Trim().ToLowerInvariant() switch
    {
        Supported => Supported,
        Partial => Partial,
        Unsupported => Unsupported,
        Unknown => Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Capability level is not supported.")
    };
}

public static class AccountingProviderSwitchDatasetAvailability
{
    public const string Available = "available";
    public const string ConfirmedAbsent = "confirmed_absent";
    public const string NotReturned = "not_returned";
    public const string NotAuthorized = "not_authorized";
    public const string Unsupported = "unsupported";
    public const string Unknown = "unknown";

    public static string Normalize(string value) => value?.Trim().ToLowerInvariant() switch
    {
        Available => Available,
        ConfirmedAbsent => ConfirmedAbsent,
        NotReturned => NotReturned,
        NotAuthorized => NotAuthorized,
        Unsupported => Unsupported,
        Unknown => Unknown,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Dataset availability is not supported.")
    };
}

public static class AccountingProviderSwitchGapSeverities
{
    public const string Information = "information";
    public const string Warning = "warning";
    public const string Blocking = "blocking";
}

public sealed class AccountingProviderSwitchAssessment : ICompanyOwnedEntity
{
    private AccountingProviderSwitchAssessment() { }

    public AccountingProviderSwitchAssessment(
        Guid id,
        Guid companyId,
        Guid switchId,
        Guid requestedByUserId,
        string idempotencyKey,
        string correlationId,
        int totalWorkItems,
        DateTime requestedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = Required(companyId, nameof(companyId));
        SwitchId = Required(switchId, nameof(switchId));
        RequestedByUserId = Required(requestedByUserId, nameof(requestedByUserId));
        IdempotencyKey = Text(idempotencyKey, nameof(idempotencyKey), 128);
        CorrelationId = Text(correlationId, nameof(correlationId), 128);
        TotalWorkItems = totalWorkItems > 0 ? totalWorkItems : throw new ArgumentOutOfRangeException(nameof(totalWorkItems));
        Status = AccountingProviderSwitchAssessmentStatuses.Queued;
        RequestedUtc = Utc(requestedUtc, nameof(requestedUtc));
        UpdatedUtc = RequestedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public int WorkIndex { get; private set; }
    public int TotalWorkItems { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? NextAttemptUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingProviderSwitch Switch { get; private set; } = null!;
    public ICollection<AccountingProviderSwitchCapability> Capabilities { get; } = new List<AccountingProviderSwitchCapability>();
    public ICollection<AccountingProviderSwitchDataset> Datasets { get; } = new List<AccountingProviderSwitchDataset>();
    public ICollection<AccountingProviderSwitchGap> Gaps { get; } = new List<AccountingProviderSwitchGap>();

    public void Claim(string leaseOwner, DateTime leaseExpiresUtc, DateTime nowUtc)
    {
        LeaseOwner = Text(leaseOwner, nameof(leaseOwner), 200);
        LeaseExpiresUtc = Utc(leaseExpiresUtc, nameof(leaseExpiresUtc));
        Status = AccountingProviderSwitchAssessmentStatuses.Running;
        StartedUtc ??= Utc(nowUtc, nameof(nowUtc));
        UpdatedUtc = StartedUtc.Value;
        Version++;
    }

    public void Continue(bool workItemCompleted, DateTime nowUtc)
    {
        if (workItemCompleted) WorkIndex = Math.Min(TotalWorkItems, WorkIndex + 1);
        Status = AccountingProviderSwitchAssessmentStatuses.Queued;
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        NextAttemptUtc = null;
        AttemptCount = 0;
        FailureCode = null;
        FailureSummary = null;
        UpdatedUtc = Utc(nowUtc, nameof(nowUtc));
        Version++;
    }

    public void Complete(DateTime nowUtc)
    {
        var utc = Utc(nowUtc, nameof(nowUtc));
        Status = AccountingProviderSwitchAssessmentStatuses.Completed;
        WorkIndex = TotalWorkItems;
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        NextAttemptUtc = null;
        CompletedUtc = utc;
        UpdatedUtc = utc;
        Version++;
    }

    public void Retry(string code, string summary, int attempt, DateTime nextAttemptUtc, DateTime nowUtc)
    {
        Status = AccountingProviderSwitchAssessmentStatuses.Queued;
        AttemptCount = attempt;
        FailureCode = Text(code, nameof(code), 100).ToLowerInvariant();
        FailureSummary = Text(summary, nameof(summary), 1000);
        NextAttemptUtc = Utc(nextAttemptUtc, nameof(nextAttemptUtc));
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        UpdatedUtc = Utc(nowUtc, nameof(nowUtc));
        Version++;
    }

    public void Fail(string code, string summary, int attempt, DateTime nowUtc)
    {
        var utc = Utc(nowUtc, nameof(nowUtc));
        Status = AccountingProviderSwitchAssessmentStatuses.Failed;
        AttemptCount = attempt;
        FailureCode = Text(code, nameof(code), 100).ToLowerInvariant();
        FailureSummary = Text(summary, nameof(summary), 1000);
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        NextAttemptUtc = null;
        CompletedUtc = utc;
        UpdatedUtc = utc;
        Version++;
    }

    internal static string Text(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(name);
    }

    internal static string? Optional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Text(value, name, maxLength);

    internal static Guid Required(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;

    internal static DateTime Utc(DateTime value, string name) =>
        value == default ? throw new ArgumentException($"{name} is required.", name) :
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public sealed class AccountingProviderSwitchCapability : ICompanyOwnedEntity
{
    private AccountingProviderSwitchCapability() { }

    public AccountingProviderSwitchCapability(Guid companyId, Guid switchId, Guid assessmentId, string endpointRole,
        string capabilityKey, string level, string explanation, string? requiredScope, DateTime observedUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = AccountingProviderSwitchAssessment.Required(companyId, nameof(companyId));
        SwitchId = AccountingProviderSwitchAssessment.Required(switchId, nameof(switchId));
        AssessmentId = AccountingProviderSwitchAssessment.Required(assessmentId, nameof(assessmentId));
        EndpointRole = NormalizeRole(endpointRole);
        CapabilityKey = AccountingProviderSwitchAssessment.Text(capabilityKey, nameof(capabilityKey), 64).ToLowerInvariant();
        Level = AccountingProviderSwitchCapabilityLevels.Normalize(level);
        Explanation = AccountingProviderSwitchAssessment.Text(explanation, nameof(explanation), 1000);
        RequiredScope = AccountingProviderSwitchAssessment.Optional(requiredScope, nameof(requiredScope), 128)?.ToLowerInvariant();
        ObservedUtc = AccountingProviderSwitchAssessment.Utc(observedUtc, nameof(observedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid AssessmentId { get; private set; }
    public string EndpointRole { get; private set; } = null!;
    public string CapabilityKey { get; private set; } = null!;
    public string Level { get; private set; } = null!;
    public string Explanation { get; private set; } = null!;
    public string? RequiredScope { get; private set; }
    public DateTime ObservedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingProviderSwitchAssessment Assessment { get; private set; } = null!;

    public void Replace(string level, string explanation, string? requiredScope, DateTime observedUtc)
    {
        Level = AccountingProviderSwitchCapabilityLevels.Normalize(level);
        Explanation = AccountingProviderSwitchAssessment.Text(explanation, nameof(explanation), 1000);
        RequiredScope = AccountingProviderSwitchAssessment.Optional(requiredScope, nameof(requiredScope), 128)?.ToLowerInvariant();
        ObservedUtc = AccountingProviderSwitchAssessment.Utc(observedUtc, nameof(observedUtc));
    }

    internal static string NormalizeRole(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "source" => "source",
        "target" => "target",
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Endpoint role must be source or target.")
    };
}

public sealed class AccountingProviderSwitchDataset : ICompanyOwnedEntity
{
    private AccountingProviderSwitchDataset() { }

    public AccountingProviderSwitchDataset(Guid companyId, Guid switchId, Guid assessmentId, string endpointRole,
        string datasetKey, DateTime createdUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = AccountingProviderSwitchAssessment.Required(companyId, nameof(companyId));
        SwitchId = AccountingProviderSwitchAssessment.Required(switchId, nameof(switchId));
        AssessmentId = AccountingProviderSwitchAssessment.Required(assessmentId, nameof(assessmentId));
        EndpointRole = AccountingProviderSwitchCapability.NormalizeRole(endpointRole);
        DatasetKey = AccountingProviderSwitchAssessment.Text(datasetKey, nameof(datasetKey), 64).ToLowerInvariant();
        Availability = AccountingProviderSwitchDatasetAvailability.Unknown;
        CapabilityLevel = AccountingProviderSwitchCapabilityLevels.Unknown;
        EvidenceJson = "{}";
        ExtractedUtc = AccountingProviderSwitchAssessment.Utc(createdUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid AssessmentId { get; private set; }
    public string EndpointRole { get; private set; } = null!;
    public string DatasetKey { get; private set; } = null!;
    public string Availability { get; private set; } = null!;
    public string CapabilityLevel { get; private set; } = null!;
    public long RecordCount { get; private set; }
    public decimal FinancialTotal { get; private set; }
    public string? Currency { get; private set; }
    public string? SourceCursor { get; private set; }
    public string? SourceVersion { get; private set; }
    public string IntegrityHash { get; private set; } = string.Empty;
    public string EvidenceJson { get; private set; } = null!;
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime ExtractedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingProviderSwitchAssessment Assessment { get; private set; } = null!;

    public void Record(string availability, string capabilityLevel, long recordCount, decimal financialTotal,
        string? currency, string? sourceCursor, string? sourceVersion, string integrityHash, string evidenceJson,
        string? failureCode, string? failureSummary, DateTime extractedUtc)
    {
        if (recordCount < 0) throw new ArgumentOutOfRangeException(nameof(recordCount));
        Availability = AccountingProviderSwitchDatasetAvailability.Normalize(availability);
        CapabilityLevel = AccountingProviderSwitchCapabilityLevels.Normalize(capabilityLevel);
        RecordCount = recordCount;
        FinancialTotal = financialTotal;
        Currency = AccountingProviderSwitchAssessment.Optional(currency, nameof(currency), 16)?.ToUpperInvariant();
        SourceCursor = AccountingProviderSwitchAssessment.Optional(sourceCursor, nameof(sourceCursor), 256);
        SourceVersion = AccountingProviderSwitchAssessment.Optional(sourceVersion, nameof(sourceVersion), 128);
        IntegrityHash = AccountingProviderSwitchAssessment.Text(integrityHash, nameof(integrityHash), 64).ToLowerInvariant();
        EvidenceJson = AccountingProviderSwitchAssessment.Text(evidenceJson, nameof(evidenceJson), 16000);
        FailureCode = AccountingProviderSwitchAssessment.Optional(failureCode, nameof(failureCode), 100)?.ToLowerInvariant();
        FailureSummary = AccountingProviderSwitchAssessment.Optional(failureSummary, nameof(failureSummary), 1000);
        ExtractedUtc = AccountingProviderSwitchAssessment.Utc(extractedUtc, nameof(extractedUtc));
    }
}

public sealed class AccountingProviderSwitchGap : ICompanyOwnedEntity
{
    private AccountingProviderSwitchGap() { }

    public AccountingProviderSwitchGap(Guid companyId, Guid switchId, Guid assessmentId, string category,
        string? datasetKey, string severity, bool isBlocking, string reasonCode, string explanation,
        string evidenceJson, string operatorAction, DateTime createdUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = AccountingProviderSwitchAssessment.Required(companyId, nameof(companyId));
        SwitchId = AccountingProviderSwitchAssessment.Required(switchId, nameof(switchId));
        AssessmentId = AccountingProviderSwitchAssessment.Required(assessmentId, nameof(assessmentId));
        Category = AccountingProviderSwitchAssessment.Text(category, nameof(category), 64).ToLowerInvariant();
        DatasetKey = AccountingProviderSwitchAssessment.Optional(datasetKey, nameof(datasetKey), 64)?.ToLowerInvariant();
        Severity = severity?.Trim().ToLowerInvariant() switch
        {
            AccountingProviderSwitchGapSeverities.Information => AccountingProviderSwitchGapSeverities.Information,
            AccountingProviderSwitchGapSeverities.Warning => AccountingProviderSwitchGapSeverities.Warning,
            AccountingProviderSwitchGapSeverities.Blocking => AccountingProviderSwitchGapSeverities.Blocking,
            _ => throw new ArgumentOutOfRangeException(nameof(severity), "Gap severity is not supported.")
        };
        IsBlocking = isBlocking;
        ReasonCode = AccountingProviderSwitchAssessment.Text(reasonCode, nameof(reasonCode), 100).ToLowerInvariant();
        Explanation = AccountingProviderSwitchAssessment.Text(explanation, nameof(explanation), 1000);
        EvidenceJson = AccountingProviderSwitchAssessment.Text(evidenceJson, nameof(evidenceJson), 16000);
        OperatorAction = AccountingProviderSwitchAssessment.Text(operatorAction, nameof(operatorAction), 1000);
        CreatedUtc = AccountingProviderSwitchAssessment.Utc(createdUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid AssessmentId { get; private set; }
    public string Category { get; private set; } = null!;
    public string? DatasetKey { get; private set; }
    public string Severity { get; private set; } = null!;
    public bool IsBlocking { get; private set; }
    public string ReasonCode { get; private set; } = null!;
    public string Explanation { get; private set; } = null!;
    public string EvidenceJson { get; private set; } = null!;
    public string OperatorAction { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingProviderSwitchAssessment Assessment { get; private set; } = null!;
}
