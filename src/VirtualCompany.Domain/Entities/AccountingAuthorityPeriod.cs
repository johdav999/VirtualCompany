namespace VirtualCompany.Domain.Entities;

public static class AccountingAuthorityOperationValues
{
    public const string NativeAuthoritativePosting = "native_authoritative_posting";
    public const string ProviderAuthoritativeWrite = "provider_authoritative_write";
    public const string DownstreamExport = "downstream_export";
    public const string MigrationReconciliation = "migration_reconciliation";
    public const string ImportProjection = "import_projection";

    public static string Normalize(string value) =>
        NormalizeToken(value, nameof(value)) switch
        {
            NativeAuthoritativePosting => NativeAuthoritativePosting,
            ProviderAuthoritativeWrite => ProviderAuthoritativeWrite,
            DownstreamExport => DownstreamExport,
            MigrationReconciliation => MigrationReconciliation,
            ImportProjection => ImportProjection,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Accounting authority operation is not supported.")
        };

    private static string NormalizeToken(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Accounting authority operation is required.", name)
            : value.Trim().Replace('-', '_').ToLowerInvariant();
}

public sealed class AccountingAuthorityPeriod : ICompanyOwnedEntity
{
    private AccountingAuthorityPeriod()
    {
    }

    public AccountingAuthorityPeriod(
        Guid id,
        Guid companyId,
        DateOnly effectiveFrom,
        DateOnly? effectiveTo,
        string authority,
        string? providerKey,
        Guid changedByUserId,
        string changeReason,
        DateTime createdUtc,
        string? targetAuthority = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (changedByUserId == Guid.Empty) throw new ArgumentException("ChangedByUserId is required.", nameof(changedByUserId));
        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
            throw new ArgumentOutOfRangeException(nameof(effectiveTo), "The authority period cannot end before it starts.");

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        Authority = AccountingAuthorityValues.Normalize(authority);
        TargetAuthority = string.IsNullOrWhiteSpace(targetAuthority) ? null : AccountingAuthorityValues.Normalize(targetAuthority);
        ProviderKey = NormalizeOptional(providerKey, nameof(providerKey), 64)?.ToLowerInvariant();
        ValidateProvider();
        ChangeReason = NormalizeRequired(changeReason, nameof(changeReason), 1000);
        ChangedByUserId = changedByUserId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public string Authority { get; private set; } = null!;
    public string? TargetAuthority { get; private set; }
    public string? ProviderKey { get; private set; }
    public string ChangeReason { get; private set; } = null!;
    public bool OpeningBalancesReconciled { get; private set; }
    public bool TrialBalanceReconciled { get; private set; }
    public bool SourceMappingsReconciled { get; private set; }
    public int ConflictCount { get; private set; }
    public string? ValidationSummary { get; private set; }
    public Guid ChangedByUserId { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;

    public bool IsCutoverReady =>
        Authority == AccountingAuthorityValues.Migration &&
        OpeningBalancesReconciled &&
        TrialBalanceReconciled &&
        SourceMappingsReconciled &&
        ConflictCount == 0;

    public void EndBefore(DateOnly nextEffectiveFrom, Guid actorUserId, DateTime updatedUtc)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        if (nextEffectiveFrom <= EffectiveFrom)
            throw new ArgumentOutOfRangeException(nameof(nextEffectiveFrom), "A later authority boundary is required.");

        EffectiveTo = nextEffectiveFrom.AddDays(-1);
        ChangedByUserId = actorUserId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        Version++;
    }

    public void RecordCutoverValidation(
        bool openingBalancesReconciled,
        bool trialBalanceReconciled,
        bool sourceMappingsReconciled,
        int conflictCount,
        string summary,
        Guid actorUserId,
        DateTime updatedUtc)
    {
        if (Authority != AccountingAuthorityValues.Migration)
            throw new InvalidOperationException("Cutover validation is available only while an authority change is in progress.");
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        if (conflictCount < 0) throw new ArgumentOutOfRangeException(nameof(conflictCount));

        OpeningBalancesReconciled = openingBalancesReconciled;
        TrialBalanceReconciled = trialBalanceReconciled;
        SourceMappingsReconciled = sourceMappingsReconciled;
        ConflictCount = conflictCount;
        ValidationSummary = NormalizeRequired(summary, nameof(summary), 1000);
        ChangedByUserId = actorUserId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        Version++;
    }

    public void CompleteCutover(Guid actorUserId, DateTime completedUtc)
    {
        if (!IsCutoverReady || TargetAuthority is null)
            throw new InvalidOperationException("Opening balances, the trial balance, source mappings, and all conflicts must be reconciled before cutover can complete.");
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));

        Authority = TargetAuthority;
        TargetAuthority = null;
        ValidateProvider();
        CompletedByUserId = actorUserId;
        CompletedUtc = EntityTimestampNormalizer.NormalizeUtc(completedUtc, nameof(completedUtc));
        ChangedByUserId = actorUserId;
        UpdatedUtc = CompletedUtc.Value;
        Version++;
    }

    private void ValidateProvider()
    {
        var requiresProvider = Authority == AccountingAuthorityValues.ExternalProvider ||
                               TargetAuthority == AccountingAuthorityValues.ExternalProvider;
        if (requiresProvider && string.IsNullOrWhiteSpace(ProviderKey))
            throw new ArgumentException("An external accounting authority requires a provider.", nameof(ProviderKey));
        if (!requiresProvider && Authority != AccountingAuthorityValues.Migration)
            ProviderKey = null;
    }

    private static string NormalizeRequired(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeRequired(value, name, maxLength);
}
