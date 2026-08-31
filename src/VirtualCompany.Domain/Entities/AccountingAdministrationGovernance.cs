namespace VirtualCompany.Domain.Entities;

public static class AccountingAccountLifecycleChangeTypes
{
    public const string Created = "created";
    public const string Renamed = "renamed";
    public const string Governed = "governed";
    public const string Retired = "retired";
}

public sealed class AccountingAccountLifecycleHistory : ICompanyOwnedEntity
{
    private AccountingAccountLifecycleHistory() { }

    public AccountingAccountLifecycleHistory(Guid id, Guid companyId, Guid financeAccountId, long version,
        string changeType, string name, string accountClass, string normalBalance, bool isReportable,
        string postingRestriction, DateOnly effectiveFrom, DateOnly? effectiveTo, Guid? replacementAccountId,
        string reason, Guid? actorUserId, DateTime recordedUtc)
    {
        if (companyId == Guid.Empty || financeAccountId == Guid.Empty) throw new ArgumentException("Company and account are required.");
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        FinanceAccountId = financeAccountId;
        Version = version;
        ChangeType = Required(changeType, nameof(changeType), 32).ToLowerInvariant();
        Name = Required(name, nameof(name), 160);
        AccountClass = Required(accountClass, nameof(accountClass), 32).ToLowerInvariant();
        NormalBalance = Required(normalBalance, nameof(normalBalance), 16).ToLowerInvariant();
        IsReportable = isReportable;
        PostingRestriction = FinanceAccountPostingRestrictionValues.Normalize(postingRestriction);
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
        ReplacementAccountId = replacementAccountId;
        Reason = Required(reason, nameof(reason), 512);
        ActorUserId = actorUserId;
        RecordedUtc = EntityTimestampNormalizer.NormalizeUtc(recordedUtc, nameof(recordedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FinanceAccountId { get; private set; }
    public long Version { get; private set; }
    public string ChangeType { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string AccountClass { get; private set; } = null!;
    public string NormalBalance { get; private set; } = null!;
    public bool IsReportable { get; private set; }
    public string PostingRestriction { get; private set; } = null!;
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public Guid? ReplacementAccountId { get; private set; }
    public string Reason { get; private set; } = null!;
    public Guid? ActorUserId { get; private set; }
    public DateTime RecordedUtc { get; private set; }
    public FinanceAccount FinanceAccount { get; private set; } = null!;

    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
}

public static class AccountingSeriesKinds
{
    public const string Voucher = "voucher";
    public const string StatutoryDocument = "statutory_document";
}

public sealed class AccountingSeriesPolicy : ICompanyOwnedEntity
{
    private AccountingSeriesPolicy() { }

    public AccountingSeriesPolicy(Guid id, Guid companyId, string seriesKind, Guid seriesId,
        string sourceType, string transactionType, int? fiscalYear, Guid? locationDimensionMemberId,
        string? jurisdiction, string policyPackKey, string policyPackVersion, string? providerKey,
        string? providerSeriesCode, bool isActive, Guid actorUserId, DateTime nowUtc, long version = 1)
    {
        if (companyId == Guid.Empty || seriesId == Guid.Empty || actorUserId == Guid.Empty) throw new ArgumentException("Company, series, and actor are required.");
        if (fiscalYear is < 1 or > 9999) throw new ArgumentOutOfRangeException(nameof(fiscalYear));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SeriesKind = Required(seriesKind, nameof(seriesKind), 32).ToLowerInvariant();
        SeriesId = seriesId;
        SourceType = Pattern(sourceType, nameof(sourceType));
        TransactionType = Pattern(transactionType, nameof(transactionType));
        FiscalYear = fiscalYear;
        LocationDimensionMemberId = locationDimensionMemberId;
        Jurisdiction = Optional(jurisdiction, nameof(jurisdiction), 16)?.ToUpperInvariant();
        PolicyPackKey = Required(policyPackKey, nameof(policyPackKey), 64).ToLowerInvariant();
        PolicyPackVersion = Required(policyPackVersion, nameof(policyPackVersion), 32);
        ProviderKey = Optional(providerKey, nameof(providerKey), 64)?.ToLowerInvariant();
        ProviderSeriesCode = Optional(providerSeriesCode, nameof(providerSeriesCode), 64);
        ScopeKey = BuildScopeKey(SourceType, TransactionType, FiscalYear, LocationDimensionMemberId, Jurisdiction, PolicyPackKey, PolicyPackVersion);
        IsActive = isActive;
        UpdatedByUserId = actorUserId;
        CreatedUtc = UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc));
        Version = version > 0 ? version : throw new ArgumentOutOfRangeException(nameof(version));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string SeriesKind { get; private set; } = null!;
    public Guid SeriesId { get; private set; }
    public string SourceType { get; private set; } = null!;
    public string TransactionType { get; private set; } = null!;
    public int? FiscalYear { get; private set; }
    public Guid? LocationDimensionMemberId { get; private set; }
    public string? Jurisdiction { get; private set; }
    public string PolicyPackKey { get; private set; } = null!;
    public string PolicyPackVersion { get; private set; } = null!;
    public string? ProviderKey { get; private set; }
    public string? ProviderSeriesCode { get; private set; }
    public string ScopeKey { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }

    public void Update(string sourceType, string transactionType, int? fiscalYear,
        Guid? locationDimensionMemberId, string? jurisdiction, string policyPackKey, string policyPackVersion,
        string? providerKey, string? providerSeriesCode, bool isActive, Guid actorUserId, DateTime nowUtc)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor is required.", nameof(actorUserId));
        if (fiscalYear is < 1 or > 9999) throw new ArgumentOutOfRangeException(nameof(fiscalYear));
        SourceType = Pattern(sourceType, nameof(sourceType)); TransactionType = Pattern(transactionType, nameof(transactionType));
        FiscalYear = fiscalYear; LocationDimensionMemberId = locationDimensionMemberId;
        Jurisdiction = Optional(jurisdiction, nameof(jurisdiction), 16)?.ToUpperInvariant();
        PolicyPackKey = Required(policyPackKey, nameof(policyPackKey), 64).ToLowerInvariant(); PolicyPackVersion = Required(policyPackVersion, nameof(policyPackVersion), 32);
        ProviderKey = Optional(providerKey, nameof(providerKey), 64)?.ToLowerInvariant(); ProviderSeriesCode = Optional(providerSeriesCode, nameof(providerSeriesCode), 64);
        ScopeKey = BuildScopeKey(SourceType, TransactionType, FiscalYear, LocationDimensionMemberId, Jurisdiction, PolicyPackKey, PolicyPackVersion);
        IsActive = isActive; UpdatedByUserId = actorUserId; UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc)); Version++;
    }

    private static string Pattern(string value, string name) => value?.Trim() == "*" ? "*" : Required(value ?? string.Empty, name, 64).ToLowerInvariant();
    private static string BuildScopeKey(string sourceType, string transactionType, int? fiscalYear,
        Guid? locationDimensionMemberId, string? jurisdiction, string policyPackKey, string policyPackVersion) =>
        $"{sourceType}|{transactionType}|{fiscalYear?.ToString() ?? "*"}|{locationDimensionMemberId?.ToString("D") ?? "*"}|{jurisdiction ?? "*"}|{policyPackKey}|{policyPackVersion}".ToLowerInvariant();
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static string? Optional(string? value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? null : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
}

public sealed class AccountingVoucherGapEvidence : ICompanyOwnedEntity
{
    private AccountingVoucherGapEvidence() { }
    public AccountingVoucherGapEvidence(Guid id, Guid companyId, Guid voucherSeriesId, int fiscalYear,
        long missingNumber, string reason, Guid actorUserId, DateTime recordedUtc)
    {
        if (companyId == Guid.Empty || voucherSeriesId == Guid.Empty || actorUserId == Guid.Empty) throw new ArgumentException("Company, series, and actor are required.");
        if (fiscalYear is < 1 or > 9999 || missingNumber < 1) throw new ArgumentOutOfRangeException(nameof(fiscalYear));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; VoucherSeriesId = voucherSeriesId;
        FiscalYear = fiscalYear; MissingNumber = missingNumber;
        Reason = string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("A gap reason is required.", nameof(reason)) : reason.Trim();
        ActorUserId = actorUserId; RecordedUtc = EntityTimestampNormalizer.NormalizeUtc(recordedUtc, nameof(recordedUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid VoucherSeriesId { get; private set; }
    public int FiscalYear { get; private set; }
    public long MissingNumber { get; private set; }
    public string Reason { get; private set; } = null!;
    public Guid ActorUserId { get; private set; }
    public DateTime RecordedUtc { get; private set; }
    public VoucherSeries VoucherSeries { get; private set; } = null!;
}

public sealed class AccountingCommerceEventReceipt : ICompanyOwnedEntity
{
    private AccountingCommerceEventReceipt() { }
    public AccountingCommerceEventReceipt(Guid id, Guid companyId, Guid eventId, long eventVersion,
        string contractVersion, string eventType, string sourceSystem, DateTime occurredUtc,
        string status, DateTime receivedUtc)
    {
        if (companyId == Guid.Empty || eventId == Guid.Empty) throw new ArgumentException("Company and event are required.");
        if (eventVersion < 1) throw new ArgumentOutOfRangeException(nameof(eventVersion));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; EventId = eventId; EventVersion = eventVersion;
        ContractVersion = Required(contractVersion, nameof(contractVersion), 32); EventType = Required(eventType, nameof(eventType), 64).ToLowerInvariant();
        SourceSystem = Required(sourceSystem, nameof(sourceSystem), 64).ToLowerInvariant();
        OccurredUtc = EntityTimestampNormalizer.NormalizeUtc(occurredUtc, nameof(occurredUtc)); Status = Required(status, nameof(status), 32).ToLowerInvariant();
        ReceivedUtc = EntityTimestampNormalizer.NormalizeUtc(receivedUtc, nameof(receivedUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid EventId { get; private set; }
    public long EventVersion { get; private set; } public string ContractVersion { get; private set; } = null!;
    public string EventType { get; private set; } = null!; public string SourceSystem { get; private set; } = null!;
    public DateTime OccurredUtc { get; private set; } public string Status { get; private set; } = null!; public DateTime ReceivedUtc { get; private set; }
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
}
