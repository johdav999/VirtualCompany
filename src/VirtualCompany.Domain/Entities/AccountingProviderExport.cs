namespace VirtualCompany.Domain.Entities;

public static class AccountingProviderExportStatuses
{
    public const string AwaitingApproval = "awaiting_approval";
    public const string Approved = "approved";
    public const string Executing = "executing";
    public const string Exported = "exported";
    public const string Failed = "failed";
    public const string ReconciliationRequired = "reconciliation_required";
    public const string Cancelled = "cancelled";
}

public static class AccountingProviderExportFailureCategories
{
    public const string UnknownOutcome = "unknown_outcome";
    public const string ProviderSuccessLocalFailure = "provider_success_local_failure";
    public const string StaleCredentials = "stale_credentials";
    public const string MissingScope = "missing_scope";
    public const string RateLimited = "rate_limited";
    public const string Validation = "validation";
    public const string Permanent = "permanent";
}

public sealed class AccountingProviderExport : ICompanyOwnedEntity
{
    private AccountingProviderExport()
    {
    }

    public AccountingProviderExport(
        Guid id,
        Guid companyId,
        Guid authorityPeriodId,
        Guid ledgerEntryId,
        string providerKey,
        string sourceType,
        string sourceId,
        string sourceVersion,
        string action,
        string stableIdentity,
        Guid writeRequestId,
        Guid requestedByUserId,
        DateTime requestedUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (authorityPeriodId == Guid.Empty) throw new ArgumentException("AuthorityPeriodId is required.", nameof(authorityPeriodId));
        if (ledgerEntryId == Guid.Empty) throw new ArgumentException("LedgerEntryId is required.", nameof(ledgerEntryId));
        if (writeRequestId == Guid.Empty) throw new ArgumentException("WriteRequestId is required.", nameof(writeRequestId));
        if (requestedByUserId == Guid.Empty) throw new ArgumentException("RequestedByUserId is required.", nameof(requestedByUserId));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AuthorityPeriodId = authorityPeriodId;
        LedgerEntryId = ledgerEntryId;
        ProviderKey = Normalize(providerKey, nameof(providerKey), 64).ToLowerInvariant();
        SourceType = Normalize(sourceType, nameof(sourceType), 64).ToLowerInvariant();
        SourceId = Normalize(sourceId, nameof(sourceId), 128);
        SourceVersion = Normalize(sourceVersion, nameof(sourceVersion), 128);
        Action = Normalize(action, nameof(action), 64).Replace('-', '_').ToLowerInvariant();
        StableIdentity = Normalize(stableIdentity, nameof(stableIdentity), 256).ToLowerInvariant();
        WriteRequestId = writeRequestId;
        RequestedByUserId = requestedByUserId;
        Status = AccountingProviderExportStatuses.AwaitingApproval;
        RequestedUtc = EntityTimestampNormalizer.NormalizeUtc(requestedUtc, nameof(requestedUtc));
        UpdatedUtc = RequestedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AuthorityPeriodId { get; private set; }
    public Guid LedgerEntryId { get; private set; }
    public string ProviderKey { get; private set; } = null!;
    public string SourceType { get; private set; } = null!;
    public string SourceId { get; private set; } = null!;
    public string SourceVersion { get; private set; } = null!;
    public string Action { get; private set; } = null!;
    public string StableIdentity { get; private set; } = null!;
    public Guid WriteRequestId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public string Status { get; private set; } = null!;
    public string? FailureCategory { get; private set; }
    public string? SafeSummary { get; private set; }
    public string? ProviderExternalId { get; private set; }
    public int AttemptCount { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public Guid? ReconciledByUserId { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime? ReconciledUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingAuthorityPeriod AuthorityPeriod { get; private set; } = null!;
    public LedgerEntry LedgerEntry { get; private set; } = null!;

    public void AttachApproval(Guid approvalRequestId, DateTime updatedUtc)
    {
        if (approvalRequestId == Guid.Empty) throw new ArgumentException("ApprovalRequestId is required.", nameof(approvalRequestId));
        ApprovalRequestId = approvalRequestId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        Version++;
    }

    public void MarkExecuting(DateTime updatedUtc)
    {
        if (Status == AccountingProviderExportStatuses.Exported) return;
        Status = AccountingProviderExportStatuses.Executing;
        AttemptCount++;
        FailureCategory = null;
        SafeSummary = null;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        Version++;
    }

    public void MarkExported(string? providerExternalId, string summary, DateTime completedUtc)
    {
        Status = AccountingProviderExportStatuses.Exported;
        ProviderExternalId = NormalizeOptional(providerExternalId, nameof(providerExternalId), 256);
        SafeSummary = Normalize(summary, nameof(summary), 1000);
        FailureCategory = null;
        CompletedUtc = EntityTimestampNormalizer.NormalizeUtc(completedUtc, nameof(completedUtc));
        UpdatedUtc = CompletedUtc.Value;
        Version++;
    }

    public void MarkFailed(string failureCategory, string summary, bool outcomeIsAmbiguous, DateTime failedUtc)
    {
        FailureCategory = Normalize(failureCategory, nameof(failureCategory), 64).ToLowerInvariant();
        SafeSummary = Normalize(summary, nameof(summary), 1000);
        Status = outcomeIsAmbiguous
            ? AccountingProviderExportStatuses.ReconciliationRequired
            : AccountingProviderExportStatuses.Failed;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(failedUtc, nameof(failedUtc));
        Version++;
    }

    public void ReconcileAsExported(string? providerExternalId, string summary, Guid actorUserId, DateTime reconciledUtc)
    {
        if (Status != AccountingProviderExportStatuses.ReconciliationRequired)
            throw new InvalidOperationException("Only an export with an unknown outcome can be reconciled.");
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));

        MarkExported(providerExternalId, summary, reconciledUtc);
        ReconciledByUserId = actorUserId;
        ReconciledUtc = UpdatedUtc;
    }

    public void ReconcileAsNotSent(string summary, Guid actorUserId, DateTime reconciledUtc)
    {
        if (Status != AccountingProviderExportStatuses.ReconciliationRequired)
            throw new InvalidOperationException("Only an export with an unknown outcome can be reconciled.");
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));

        Status = AccountingProviderExportStatuses.Failed;
        FailureCategory = AccountingProviderExportFailureCategories.UnknownOutcome;
        SafeSummary = Normalize(summary, nameof(summary), 1000);
        ReconciledByUserId = actorUserId;
        ReconciledUtc = EntityTimestampNormalizer.NormalizeUtc(reconciledUtc, nameof(reconciledUtc));
        UpdatedUtc = ReconciledUtc.Value;
        Version++;
    }

    private static string Normalize(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(name);
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value, name, maxLength);
}
