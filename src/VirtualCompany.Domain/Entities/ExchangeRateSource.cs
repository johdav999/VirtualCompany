using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class ExchangeRateSource : ICompanyOwnedEntity
{
    private ExchangeRateSource() { }

    public ExchangeRateSource(Guid id, Guid companyId, string sourceKey, string displayName,
        string sourceKind, string sourceVersion, int priority, bool requiresApproval,
        int maxStalenessDays, int refreshIntervalHours, string licenseSummary,
        bool isEnabled, DateTime createdUtc)
    {
        Id = ExchangeRateText.Id(id, nameof(id));
        CompanyId = ExchangeRateText.Id(companyId, nameof(companyId));
        SourceKey = ExchangeRateText.Token(sourceKey, 64, nameof(sourceKey));
        DisplayName = ExchangeRateText.Required(displayName, 160, nameof(displayName));
        SourceKind = NormalizeKind(sourceKind);
        SourceVersion = ExchangeRateText.Required(sourceVersion, 64, nameof(sourceVersion));
        ValidatePolicy(priority, maxStalenessDays, refreshIntervalHours);
        Priority = priority;
        RequiresApproval = requiresApproval;
        MaxStalenessDays = maxStalenessDays;
        RefreshIntervalHours = refreshIntervalHours;
        LicenseSummary = ExchangeRateText.Required(licenseSummary, 1000, nameof(licenseSummary));
        IsEnabled = isEnabled;
        CreatedUtc = UpdatedUtc = ExchangeRateText.Utc(createdUtc);
        NextRefreshUtc = SourceKind == ExchangeRateSourceKinds.Provider && isEnabled ? CreatedUtc : null;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string SourceKey { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string SourceKind { get; private set; } = null!;
    public string SourceVersion { get; private set; } = null!;
    public int Priority { get; private set; }
    public bool RequiresApproval { get; private set; }
    public int MaxStalenessDays { get; private set; }
    public int RefreshIntervalHours { get; private set; }
    public string LicenseSummary { get; private set; } = null!;
    public bool IsEnabled { get; private set; }
    public DateTime? LastAttemptUtc { get; private set; }
    public DateTime? LastSuccessfulRefreshUtc { get; private set; }
    public DateTime? NextRefreshUtc { get; private set; }
    public string? LastFailureReasonCode { get; private set; }
    public string? LastFailureSummary { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public ICollection<ExchangeRateSet> RateSets { get; } = new List<ExchangeRateSet>();

    public void Configure(int priority, bool requiresApproval, int maxStalenessDays,
        int refreshIntervalHours, bool isEnabled, long expectedVersion, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion);
        ValidatePolicy(priority, maxStalenessDays, refreshIntervalHours);
        Priority = priority;
        RequiresApproval = requiresApproval;
        MaxStalenessDays = maxStalenessDays;
        RefreshIntervalHours = refreshIntervalHours;
        IsEnabled = isEnabled;
        NextRefreshUtc = SourceKind == ExchangeRateSourceKinds.Provider && isEnabled
            ? NextRefreshUtc ?? ExchangeRateText.Utc(nowUtc)
            : null;
        Touch(nowUtc);
    }

    public void RecordAttempt(DateTime nowUtc)
    {
        LastAttemptUtc = ExchangeRateText.Utc(nowUtc);
        Touch(nowUtc);
    }

    public void RecordSuccess(DateTime nowUtc)
    {
        var now = ExchangeRateText.Utc(nowUtc);
        LastAttemptUtc = LastSuccessfulRefreshUtc = now;
        NextRefreshUtc = now.AddHours(RefreshIntervalHours);
        LastFailureReasonCode = LastFailureSummary = null;
        Touch(now);
    }

    public void RecordFailure(string reasonCode, string summary, DateTime nowUtc, TimeSpan retryDelay)
    {
        var now = ExchangeRateText.Utc(nowUtc);
        LastAttemptUtc = now;
        NextRefreshUtc = now.Add(retryDelay);
        LastFailureReasonCode = ExchangeRateText.Token(reasonCode, 96, nameof(reasonCode));
        LastFailureSummary = ExchangeRateText.Required(summary, 1000, nameof(summary));
        Touch(now);
    }

    public void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion) throw new InvalidOperationException("The exchange-rate source changed after it was loaded.");
    }

    private void Touch(DateTime nowUtc) { UpdatedUtc = ExchangeRateText.Utc(nowUtc); Version++; }

    private static string NormalizeKind(string value) => value switch
    {
        ExchangeRateSourceKinds.Manual => value,
        ExchangeRateSourceKinds.Provider => value,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Exchange-rate source kind is not supported.")
    };

    private static void ValidatePolicy(int priority, int maxStalenessDays, int refreshIntervalHours)
    {
        if (priority is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(priority));
        if (maxStalenessDays is < 0 or > 366) throw new ArgumentOutOfRangeException(nameof(maxStalenessDays));
        if (refreshIntervalHours is < 1 or > 8760) throw new ArgumentOutOfRangeException(nameof(refreshIntervalHours));
    }
}
