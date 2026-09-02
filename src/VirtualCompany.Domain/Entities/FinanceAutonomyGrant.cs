using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceAutonomyGrant : ICompanyOwnedEntity
{
    private FinanceAutonomyGrant() { }

    public FinanceAutonomyGrant(Guid id, Guid companyId, Guid agentId, string capabilityId, DateTime createdUtc)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty) throw new ArgumentException("Company and agent are required.");
        if (string.IsNullOrWhiteSpace(capabilityId)) throw new ArgumentException("Capability is required.", nameof(capabilityId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AgentId = agentId;
        CapabilityId = capabilityId.Trim().ToLowerInvariant();
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        UpdatedUtc = CreatedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AgentId { get; private set; }
    public string CapabilityId { get; private set; } = string.Empty;
    public int LatestVersionNumber { get; private set; }
    public Guid? ActiveVersionId { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Agent Agent { get; private set; } = null!;
    public ICollection<FinanceAutonomyGrantVersion> Versions { get; } = new List<FinanceAutonomyGrantVersion>();

    public int ReserveNextVersion(DateTime nowUtc)
    {
        LatestVersionNumber++;
        UpdatedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        Version++;
        return LatestVersionNumber;
    }

    public void Activate(Guid versionId, int expectedVersion, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion);
        ActiveVersionId = versionId;
        UpdatedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        Version++;
    }

    public void ClearActiveVersion(int expectedVersion, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion);
        ActiveVersionId = null;
        UpdatedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        Version++;
    }

    private void EnsureVersion(int expectedVersion)
    {
        if (expectedVersion > 0 && Version != expectedVersion)
            throw new InvalidOperationException("The Finance autonomy grant changed. Refresh and retry.");
    }
}

public sealed class FinanceAutonomyGrantVersion : ICompanyOwnedEntity
{
    private FinanceAutonomyGrantVersion() { }

    public FinanceAutonomyGrantVersion(
        Guid id, Guid companyId, Guid grantId, int versionNumber, FinanceAutonomyLevel level,
        IEnumerable<string> allowedTriggers, IEnumerable<string> allowedActionClasses, IEnumerable<string> allowedTools,
        int maximumRecordsPerRun, decimal? maximumAmountPerRun, int maximumActionsPerRun,
        string? scheduleExpression, string timezone, string windowStartLocal, string windowEndLocal,
        int evidenceFreshnessMinutes, string confirmationBehavior, string escalationRoute,
        DateTime effectiveFromUtc, DateTime? expiresUtc, string catalogueVersion, string capabilityPolicyHash,
        string authorityVersion, string authorityHash, Guid createdByUserId, DateTime createdUtc, bool elevated,
        IEnumerable<string>? allowedEventTypes = null, int minimumIntervalMinutes = 60,
        int maximumRunsPerWindow = 1, int debounceMinutes = 5, string catchUpBehavior = "latest",
        int maximumCatchUpWindows = 1, int lateEventToleranceMinutes = 1440)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        GrantId = grantId;
        VersionNumber = versionNumber;
        Level = level;
        AllowedTriggers = Normalize(allowedTriggers);
        AllowedActionClasses = Normalize(allowedActionClasses);
        AllowedTools = Normalize(allowedTools);
        AllowedEventTypes = Normalize(allowedEventTypes ?? []);
        MaximumRecordsPerRun = maximumRecordsPerRun;
        MaximumAmountPerRun = maximumAmountPerRun;
        MaximumActionsPerRun = maximumActionsPerRun;
        ScheduleExpression = string.IsNullOrWhiteSpace(scheduleExpression) ? null : scheduleExpression.Trim();
        Timezone = timezone.Trim();
        WindowStartLocal = windowStartLocal.Trim();
        WindowEndLocal = windowEndLocal.Trim();
        EvidenceFreshnessMinutes = evidenceFreshnessMinutes;
        MinimumIntervalMinutes = minimumIntervalMinutes;
        MaximumRunsPerWindow = maximumRunsPerWindow;
        DebounceMinutes = debounceMinutes;
        CatchUpBehavior = catchUpBehavior.Trim().ToLowerInvariant();
        MaximumCatchUpWindows = maximumCatchUpWindows;
        LateEventToleranceMinutes = lateEventToleranceMinutes;
        ConfirmationBehavior = confirmationBehavior.Trim().ToLowerInvariant();
        EscalationRoute = escalationRoute.Trim();
        EffectiveFromUtc = DateTime.SpecifyKind(effectiveFromUtc, DateTimeKind.Utc);
        ExpiresUtc = expiresUtc.HasValue ? DateTime.SpecifyKind(expiresUtc.Value, DateTimeKind.Utc) : null;
        CatalogueVersion = catalogueVersion;
        CapabilityPolicyHash = capabilityPolicyHash;
        AuthorityVersion = authorityVersion;
        AuthorityHash = authorityHash;
        CreatedByUserId = createdByUserId;
        CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc);
        Status = elevated ? FinanceAutonomyGrantVersionStatus.PendingReview : FinanceAutonomyGrantVersionStatus.Prospective;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid GrantId { get; private set; }
    public int VersionNumber { get; private set; }
    public FinanceAutonomyLevel Level { get; private set; }
    public List<string> AllowedTriggers { get; private set; } = [];
    public List<string> AllowedActionClasses { get; private set; } = [];
    public List<string> AllowedTools { get; private set; } = [];
    public List<string> AllowedEventTypes { get; private set; } = [];
    public int MaximumRecordsPerRun { get; private set; }
    public decimal? MaximumAmountPerRun { get; private set; }
    public int MaximumActionsPerRun { get; private set; }
    public string? ScheduleExpression { get; private set; }
    public string Timezone { get; private set; } = "UTC";
    public string WindowStartLocal { get; private set; } = "00:00";
    public string WindowEndLocal { get; private set; } = "23:59";
    public int EvidenceFreshnessMinutes { get; private set; }
    public int MinimumIntervalMinutes { get; private set; }
    public int MaximumRunsPerWindow { get; private set; }
    public int DebounceMinutes { get; private set; }
    public string CatchUpBehavior { get; private set; } = "latest";
    public int MaximumCatchUpWindows { get; private set; }
    public int LateEventToleranceMinutes { get; private set; }
    public string ConfirmationBehavior { get; private set; } = string.Empty;
    public string EscalationRoute { get; private set; } = string.Empty;
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? ExpiresUtc { get; private set; }
    public FinanceAutonomyGrantVersionStatus Status { get; private set; }
    public string CatalogueVersion { get; private set; } = string.Empty;
    public string CapabilityPolicyHash { get; private set; } = string.Empty;
    public string AuthorityVersion { get; private set; } = string.Empty;
    public string AuthorityHash { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public string? ReviewReason { get; private set; }
    public DateTime? ReviewedUtc { get; private set; }
    public DateTime? ActivatedUtc { get; private set; }
    public DateTime? RevokedUtc { get; private set; }
    public Guid? RevokedByUserId { get; private set; }
    public string? RevocationReason { get; private set; }
    public FinanceAutonomyGrant Grant { get; private set; } = null!;

    public void Activate(Guid reviewerUserId, string? reviewReason, DateTime nowUtc)
    {
        if (Status is not FinanceAutonomyGrantVersionStatus.Prospective and not FinanceAutonomyGrantVersionStatus.PendingReview)
            throw new InvalidOperationException("Only a prospective Finance autonomy version can be activated.");
        ReviewedByUserId = reviewerUserId;
        ReviewReason = string.IsNullOrWhiteSpace(reviewReason) ? null : reviewReason.Trim();
        ReviewedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        ActivatedUtc = ReviewedUtc;
        Status = FinanceAutonomyGrantVersionStatus.Active;
    }

    public void Supersede()
    {
        if (Status == FinanceAutonomyGrantVersionStatus.Active) Status = FinanceAutonomyGrantVersionStatus.Superseded;
    }

    public void Revoke(Guid actorUserId, string reason, DateTime nowUtc)
    {
        Status = FinanceAutonomyGrantVersionStatus.Revoked;
        RevokedByUserId = actorUserId;
        RevocationReason = reason.Trim();
        RevokedUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
    }

    private static List<string> Normalize(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim().ToLowerInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal).ToList();
}
