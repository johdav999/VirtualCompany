namespace VirtualCompany.Domain.Entities;

public static class AccountingProviderSwitchStagingDatasets
{
    public const string Accounts = "accounts";
    public const string TaxTreatments = "tax_treatments";
    public const string Counterparties = "counterparties";
    public const string Documents = "documents";
    public const string Journals = "journals";
    public const string JournalLines = "journal_lines";
    public const string Invoices = "invoices";
    public const string Credits = "credits";
    public const string Payments = "payments";
    public const string Allocations = "allocations";
    public const string BankState = "bank_state";
    public const string Currencies = "currencies";
    public const string ExchangeRates = "exchange_rates";
    public const string Dimensions = "dimensions";
    public const string OpenItems = "open_items";
    public const string OpeningBalanceCandidates = "opening_balance_candidates";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Accounts, TaxTreatments, Counterparties, Documents, Journals, JournalLines, Invoices, Credits,
        Payments, Allocations, BankState, Currencies, ExchangeRates, Dimensions, OpenItems,
        OpeningBalanceCandidates
    };

    public static string Normalize(string value)
    {
        var normalized = AccountingProviderSwitchStagingText.Token(value, nameof(value), 64);
        return Supported.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), "The staging dataset is not supported.");
    }
}

public static class AccountingProviderSwitchDispositions
{
    public const string Ready = "ready";
    public const string Mapped = "mapped";
    public const string Transformed = "transformed";
    public const string OpeningBalanceRepresentation = "opening_balance_representation";
    public const string Duplicate = "duplicate";
    public const string ExcludedWithApproval = "excluded_with_approval";
    public const string Missing = "missing";
    public const string Unsupported = "unsupported";
    public const string Conflicting = "conflicting";
    public const string AwaitingEvidence = "awaiting_evidence";
    public const string Blocked = "blocked";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Ready, Mapped, Transformed, OpeningBalanceRepresentation, Duplicate, ExcludedWithApproval,
        Missing, Unsupported, Conflicting, AwaitingEvidence, Blocked
    };

    public static string Normalize(string value)
    {
        var normalized = AccountingProviderSwitchStagingText.Token(value, nameof(value), 48);
        return Supported.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), "The source-record disposition is not supported.");
    }

    public static bool BlocksProgress(string disposition) => Normalize(disposition) is
        Missing or Unsupported or Conflicting or AwaitingEvidence or Blocked;
}

public static class AccountingProviderSwitchMappingTypes
{
    public const string Account = "account";
    public const string TaxCode = "tax_code";
    public const string Dimension = "dimension";
    public const string Counterparty = "counterparty";
    public const string Currency = "currency";
    public const string Numbering = "numbering";
    public const string PaymentAllocation = "payment_allocation";
    public const string Exclusion = "exclusion";
    public const string Transformation = "transformation";
    public const string ManualException = "manual_exception";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        Account, TaxCode, Dimension, Counterparty, Currency, Numbering, PaymentAllocation,
        Exclusion, Transformation, ManualException
    };

    public static string Normalize(string value)
    {
        var normalized = AccountingProviderSwitchStagingText.Token(value, nameof(value), 48);
        return Supported.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), "The mapping type is not supported.");
    }
}

public static class AccountingProviderSwitchMappingStatuses
{
    public const string Suggested = "suggested";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Stale = "stale";
}

public sealed class AccountingProviderSwitchStagedRecord : ICompanyOwnedEntity
{
    private AccountingProviderSwitchStagedRecord() { }

    public AccountingProviderSwitchStagedRecord(
        Guid id,
        Guid companyId,
        Guid switchId,
        Guid extractionBatchId,
        AccountingProviderEndpoint sourceEndpoint,
        string dataset,
        string sourceIdentity,
        string sourceVersion,
        DateTime? providerModifiedUtc,
        string sourceHash,
        string normalizedHash,
        string normalizedDataJson,
        string evidenceJson,
        decimal financialAmount,
        string? currency,
        string initialDisposition,
        DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = AccountingProviderSwitchStagingText.Required(companyId, nameof(companyId));
        SwitchId = AccountingProviderSwitchStagingText.Required(switchId, nameof(switchId));
        ExtractionBatchId = AccountingProviderSwitchStagingText.Required(extractionBatchId, nameof(extractionBatchId));
        SourceEndpointKey = BuildEndpointKey(sourceEndpoint);
        Dataset = AccountingProviderSwitchStagingDatasets.Normalize(dataset);
        SourceIdentity = AccountingProviderSwitchStagingText.Required(sourceIdentity, nameof(sourceIdentity), 256);
        SourceVersion = AccountingProviderSwitchStagingText.Required(sourceVersion, nameof(sourceVersion), 128);
        SourceRecordKeyHash = ComputeIdentityHash(CompanyId, SwitchId, SourceEndpointKey, Dataset, SourceIdentity);
        StableIdentityHash = ComputeIdentityHash(CompanyId, SwitchId, SourceEndpointKey, Dataset, SourceIdentity, SourceVersion);
        ProviderModifiedUtc = providerModifiedUtc.HasValue
            ? AccountingProviderSwitchStagingText.Utc(providerModifiedUtc.Value, nameof(providerModifiedUtc))
            : null;
        SourceHash = AccountingProviderSwitchStagingText.Hash(sourceHash, nameof(sourceHash));
        NormalizedHash = AccountingProviderSwitchStagingText.Hash(normalizedHash, nameof(normalizedHash));
        NormalizedDataJson = AccountingProviderSwitchStagingText.Json(normalizedDataJson, nameof(normalizedDataJson));
        EvidenceJson = AccountingProviderSwitchStagingText.Json(evidenceJson, nameof(evidenceJson));
        FinancialAmount = financialAmount;
        Currency = AccountingProviderSwitchStagingText.Optional(currency, nameof(currency), 16)?.ToUpperInvariant();
        Disposition = AccountingProviderSwitchDispositions.Normalize(initialDisposition);
        IsCurrent = true;
        CreatedUtc = AccountingProviderSwitchStagingText.Utc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        Version = 1;
        ValidateDisposition(null, null, null, false);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid ExtractionBatchId { get; private set; }
    public string SourceEndpointKey { get; private set; } = null!;
    public string Dataset { get; private set; } = null!;
    public string SourceIdentity { get; private set; } = null!;
    public string SourceVersion { get; private set; } = null!;
    public string SourceRecordKeyHash { get; private set; } = null!;
    public string StableIdentityHash { get; private set; } = null!;
    public DateTime? ProviderModifiedUtc { get; private set; }
    public string SourceHash { get; private set; } = null!;
    public string NormalizedHash { get; private set; } = null!;
    public string NormalizedDataJson { get; private set; } = null!;
    public string EvidenceJson { get; private set; } = null!;
    public decimal FinancialAmount { get; private set; }
    public string? Currency { get; private set; }
    public string Disposition { get; private set; } = null!;
    public string? DispositionReason { get; private set; }
    public Guid? MappingDecisionId { get; private set; }
    public int? MappingVersion { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public string? ApprovalBindingHash { get; private set; }
    public Guid? DuplicateOfStagedRecordId { get; private set; }
    public bool IsCurrent { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingProviderSwitch Switch { get; private set; } = null!;

    public bool ReplaceNormalizedSnapshot(Guid extractionBatchId, DateTime? providerModifiedUtc, string sourceHash,
        string normalizedHash, string normalizedDataJson, string evidenceJson, decimal financialAmount,
        string? currency, DateTime updatedUtc)
    {
        var nextSourceHash = AccountingProviderSwitchStagingText.Hash(sourceHash, nameof(sourceHash));
        var nextNormalizedHash = AccountingProviderSwitchStagingText.Hash(normalizedHash, nameof(normalizedHash));
        var contentChanged = !string.Equals(SourceHash, nextSourceHash, StringComparison.Ordinal) ||
                             !string.Equals(NormalizedHash, nextNormalizedHash, StringComparison.Ordinal) ||
                             !string.Equals(EvidenceJson, evidenceJson, StringComparison.Ordinal);
        ExtractionBatchId = AccountingProviderSwitchStagingText.Required(extractionBatchId, nameof(extractionBatchId));
        ProviderModifiedUtc = providerModifiedUtc.HasValue
            ? AccountingProviderSwitchStagingText.Utc(providerModifiedUtc.Value, nameof(providerModifiedUtc))
            : null;
        SourceHash = nextSourceHash;
        NormalizedHash = nextNormalizedHash;
        NormalizedDataJson = AccountingProviderSwitchStagingText.Json(normalizedDataJson, nameof(normalizedDataJson));
        EvidenceJson = AccountingProviderSwitchStagingText.Json(evidenceJson, nameof(evidenceJson));
        FinancialAmount = financialAmount;
        Currency = AccountingProviderSwitchStagingText.Optional(currency, nameof(currency), 16)?.ToUpperInvariant();
        if (contentChanged)
        {
            Disposition = AccountingProviderSwitchDispositions.AwaitingEvidence;
            DispositionReason = "The source or normalized content changed and the prior decision must be reviewed again.";
            MappingDecisionId = null;
            MappingVersion = null;
            ApprovalRequestId = null;
            ApprovalBindingHash = null;
            DuplicateOfStagedRecordId = null;
        }
        Touch(updatedUtc);
        return contentChanged;
    }

    public void MarkSuperseded(DateTime updatedUtc)
    {
        if (!IsCurrent) return;
        IsCurrent = false;
        Touch(updatedUtc);
    }

    public void ResolveDisposition(string disposition, string reason, Guid? mappingDecisionId, int? mappingVersion,
        Guid? approvalRequestId, string? approvalBindingHash, Guid? duplicateOfStagedRecordId,
        bool hasValidApproval, DateTime updatedUtc)
    {
        if (!IsCurrent) throw new InvalidOperationException("A superseded staged record cannot be changed.");
        var normalized = AccountingProviderSwitchDispositions.Normalize(disposition);
        ValidateDisposition(normalized, mappingDecisionId, duplicateOfStagedRecordId, hasValidApproval);
        Disposition = normalized;
        DispositionReason = AccountingProviderSwitchStagingText.Required(reason, nameof(reason), 1000);
        MappingDecisionId = mappingDecisionId;
        MappingVersion = mappingVersion;
        ApprovalRequestId = approvalRequestId;
        ApprovalBindingHash = AccountingProviderSwitchStagingText.Optional(approvalBindingHash, nameof(approvalBindingHash), 64)?.ToLowerInvariant();
        DuplicateOfStagedRecordId = duplicateOfStagedRecordId;
        Touch(updatedUtc);
    }

    private void ValidateDisposition(string? disposition, Guid? mappingDecisionId, Guid? duplicateOfStagedRecordId,
        bool hasValidApproval)
    {
        var value = disposition ?? Disposition;
        if (value == AccountingProviderSwitchDispositions.Mapped && !mappingDecisionId.HasValue)
            throw new InvalidOperationException("A mapped record requires a versioned mapping decision.");
        if (value == AccountingProviderSwitchDispositions.Duplicate && !duplicateOfStagedRecordId.HasValue)
            throw new InvalidOperationException("A duplicate disposition requires the matching staged record.");
        if (value is AccountingProviderSwitchDispositions.ExcludedWithApproval or
            AccountingProviderSwitchDispositions.Transformed or
            AccountingProviderSwitchDispositions.OpeningBalanceRepresentation && !hasValidApproval)
            throw new InvalidOperationException("This material disposition requires a current approved decision.");
        if (value != AccountingProviderSwitchDispositions.Mapped && mappingDecisionId.HasValue &&
            value is not AccountingProviderSwitchDispositions.ExcludedWithApproval and
            not AccountingProviderSwitchDispositions.Transformed and
            not AccountingProviderSwitchDispositions.OpeningBalanceRepresentation)
            throw new InvalidOperationException("This disposition cannot reference a mapping decision.");
    }

    public static string BuildEndpointKey(AccountingProviderEndpoint endpoint) =>
        endpoint.Kind == AccountingProviderEndpointKinds.Internal
            ? AccountingProviderEndpointKinds.Internal
            : $"{AccountingProviderEndpointKinds.External}:{endpoint.ProviderKey}";

    private static string ComputeIdentityHash(params object[] parts)
    {
        var text = string.Join("|", parts.Select(part => part.ToString()?.Trim().ToLowerInvariant()));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
    }

    private void Touch(DateTime updatedUtc)
    {
        UpdatedUtc = AccountingProviderSwitchStagingText.Utc(updatedUtc, nameof(updatedUtc));
        Version++;
    }
}

public sealed class AccountingProviderSwitchMappingSet : ICompanyOwnedEntity
{
    private AccountingProviderSwitchMappingSet() { }

    public AccountingProviderSwitchMappingSet(Guid id, Guid companyId, Guid switchId, string mappingType,
        string scopeKey, int mappingVersion, Guid createdByUserId, DateTime createdUtc)
    {
        if (mappingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(mappingVersion));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = AccountingProviderSwitchStagingText.Required(companyId, nameof(companyId));
        SwitchId = AccountingProviderSwitchStagingText.Required(switchId, nameof(switchId));
        MappingType = AccountingProviderSwitchMappingTypes.Normalize(mappingType);
        ScopeKey = AccountingProviderSwitchStagingText.Required(scopeKey, nameof(scopeKey), 256);
        MappingVersion = mappingVersion;
        CreatedByUserId = AccountingProviderSwitchStagingText.Required(createdByUserId, nameof(createdByUserId));
        CreatedUtc = AccountingProviderSwitchStagingText.Utc(createdUtc, nameof(createdUtc));
        IsCurrent = true;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public string MappingType { get; private set; } = null!;
    public string ScopeKey { get; private set; } = null!;
    public int MappingVersion { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? SupersededUtc { get; private set; }
    public bool IsCurrent { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingProviderSwitch Switch { get; private set; } = null!;
    public ICollection<AccountingProviderSwitchMappingDecision> Decisions { get; } = new List<AccountingProviderSwitchMappingDecision>();

    public void Supersede(DateTime updatedUtc)
    {
        if (!IsCurrent) return;
        IsCurrent = false;
        SupersededUtc = AccountingProviderSwitchStagingText.Utc(updatedUtc, nameof(updatedUtc));
    }
}

public sealed class AccountingProviderSwitchMappingDecision : ICompanyOwnedEntity
{
    private AccountingProviderSwitchMappingDecision() { }

    public AccountingProviderSwitchMappingDecision(Guid id, Guid companyId, Guid switchId, Guid mappingSetId,
        int mappingVersion, string mappingType, string sourceKey, string? targetKey, string suggestionMethod,
        decimal confidence, string evidenceJson, bool isMaterial, long affectedRecordCount,
        decimal affectedFinancialTotal, string bindingHash, Guid createdByUserId, DateTime createdUtc)
    {
        if (mappingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(mappingVersion));
        if (confidence is < 0m or > 1m) throw new ArgumentOutOfRangeException(nameof(confidence));
        if (affectedRecordCount <= 0) throw new ArgumentOutOfRangeException(nameof(affectedRecordCount));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = AccountingProviderSwitchStagingText.Required(companyId, nameof(companyId));
        SwitchId = AccountingProviderSwitchStagingText.Required(switchId, nameof(switchId));
        MappingSetId = AccountingProviderSwitchStagingText.Required(mappingSetId, nameof(mappingSetId));
        MappingVersion = mappingVersion;
        MappingType = AccountingProviderSwitchMappingTypes.Normalize(mappingType);
        SourceKey = AccountingProviderSwitchStagingText.Required(sourceKey, nameof(sourceKey), 256);
        TargetKey = AccountingProviderSwitchStagingText.Optional(targetKey, nameof(targetKey), 256);
        SuggestionMethod = AccountingProviderSwitchStagingText.Token(suggestionMethod, nameof(suggestionMethod), 64);
        Confidence = confidence;
        EvidenceJson = AccountingProviderSwitchStagingText.Json(evidenceJson, nameof(evidenceJson));
        IsMaterial = isMaterial;
        AffectedRecordCount = affectedRecordCount;
        AffectedFinancialTotal = affectedFinancialTotal;
        BindingHash = AccountingProviderSwitchStagingText.Hash(bindingHash, nameof(bindingHash));
        Status = AccountingProviderSwitchMappingStatuses.Suggested;
        CreatedByUserId = AccountingProviderSwitchStagingText.Required(createdByUserId, nameof(createdByUserId));
        CreatedUtc = AccountingProviderSwitchStagingText.Utc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid MappingSetId { get; private set; }
    public int MappingVersion { get; private set; }
    public string MappingType { get; private set; } = null!;
    public string SourceKey { get; private set; } = null!;
    public string? TargetKey { get; private set; }
    public string SuggestionMethod { get; private set; } = null!;
    public decimal Confidence { get; private set; }
    public string EvidenceJson { get; private set; } = null!;
    public bool IsMaterial { get; private set; }
    public long AffectedRecordCount { get; private set; }
    public decimal AffectedFinancialTotal { get; private set; }
    public string BindingHash { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public Guid? ApprovalRequestId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingProviderSwitch Switch { get; private set; } = null!;
    public AccountingProviderSwitchMappingSet MappingSet { get; private set; } = null!;
    public ICollection<AccountingProviderSwitchMappingRecord> AffectedRecords { get; } = new List<AccountingProviderSwitchMappingRecord>();

    public void ApproveAutomatically(DateTime updatedUtc)
    {
        if (IsMaterial || Confidence < 1m || TargetKey is null)
            throw new InvalidOperationException("Only an exact, non-material deterministic mapping can be accepted automatically.");
        Status = AccountingProviderSwitchMappingStatuses.Approved;
        Touch(updatedUtc);
    }

    public void RequestApproval(Guid approvalRequestId, DateTime updatedUtc)
    {
        if (Status == AccountingProviderSwitchMappingStatuses.Stale)
            throw new InvalidOperationException("A stale mapping decision cannot be submitted for approval.");
        ApprovalRequestId = AccountingProviderSwitchStagingText.Required(approvalRequestId, nameof(approvalRequestId));
        Status = AccountingProviderSwitchMappingStatuses.AwaitingApproval;
        Touch(updatedUtc);
    }

    public void MarkStale(DateTime updatedUtc)
    {
        Status = AccountingProviderSwitchMappingStatuses.Stale;
        Touch(updatedUtc);
    }

    public void RecordApprovalDecision(Guid approvalRequestId, bool approved, DateTime updatedUtc)
    {
        if (!ApprovalRequestId.HasValue || ApprovalRequestId.Value != approvalRequestId)
            throw new InvalidOperationException("The approval does not match this mapping decision version.");
        if (Status == AccountingProviderSwitchMappingStatuses.Stale)
            throw new InvalidOperationException("A stale mapping decision cannot be approved or rejected for use.");
        Status = approved
            ? AccountingProviderSwitchMappingStatuses.Approved
            : AccountingProviderSwitchMappingStatuses.Rejected;
        Touch(updatedUtc);
    }

    private void Touch(DateTime updatedUtc)
    {
        UpdatedUtc = AccountingProviderSwitchStagingText.Utc(updatedUtc, nameof(updatedUtc));
        Version++;
    }
}

public sealed class AccountingProviderSwitchMappingRecord : ICompanyOwnedEntity
{
    private AccountingProviderSwitchMappingRecord() { }

    public AccountingProviderSwitchMappingRecord(Guid companyId, Guid switchId, Guid mappingDecisionId,
        Guid stagedRecordId, string stagedSourceHash, string stagedNormalizedHash)
    {
        Id = Guid.NewGuid();
        CompanyId = AccountingProviderSwitchStagingText.Required(companyId, nameof(companyId));
        SwitchId = AccountingProviderSwitchStagingText.Required(switchId, nameof(switchId));
        MappingDecisionId = AccountingProviderSwitchStagingText.Required(mappingDecisionId, nameof(mappingDecisionId));
        StagedRecordId = AccountingProviderSwitchStagingText.Required(stagedRecordId, nameof(stagedRecordId));
        StagedSourceHash = AccountingProviderSwitchStagingText.Hash(stagedSourceHash, nameof(stagedSourceHash));
        StagedNormalizedHash = AccountingProviderSwitchStagingText.Hash(stagedNormalizedHash, nameof(stagedNormalizedHash));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid MappingDecisionId { get; private set; }
    public Guid StagedRecordId { get; private set; }
    public string StagedSourceHash { get; private set; } = null!;
    public string StagedNormalizedHash { get; private set; } = null!;
    public Company Company { get; private set; } = null!;
    public AccountingProviderSwitchMappingDecision MappingDecision { get; private set; } = null!;
    public AccountingProviderSwitchStagedRecord StagedRecord { get; private set; } = null!;
}

internal static class AccountingProviderSwitchStagingText
{
    public static Guid Required(Guid value, string name) =>
        value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;

    public static string Required(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    public static string? Optional(string? value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, name, maxLength);

    public static string Token(string value, string name, int maxLength) =>
        Required(value, name, maxLength).Replace('-', '_').ToLowerInvariant();

    public static string Hash(string value, string name)
    {
        var normalized = Required(value, name, 64).ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException($"{name} must be a SHA-256 hexadecimal value.", name);
        return normalized;
    }

    public static string Json(string value, string name) => Required(value, name, 16000);

    public static DateTime Utc(DateTime value, string name) =>
        value == default
            ? throw new ArgumentException($"{name} is required.", name)
            : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
