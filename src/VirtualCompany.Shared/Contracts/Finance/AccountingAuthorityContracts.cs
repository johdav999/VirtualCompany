namespace VirtualCompany.Application.Finance;
public sealed record AccountingAuthorityIssueDto(string ReasonCode, string Explanation, bool IsBlocking = true, Guid? SubjectId = null)
{
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public bool IsBlocking { get; set; } = IsBlocking;
    public Guid? SubjectId { get; set; } = SubjectId;

    public AccountingAuthorityIssueDto() : this(string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingAuthorityProviderDto(string ProviderKey, string DisplayName, bool IsConnected, string ConnectionStatus, DateTime? LastSuccessfulSyncUtc, IReadOnlyCollection<string> GrantedScopes, string ModeExplanation, string? SafeIssueSummary)
{
    public string ProviderKey { get; set; } = ProviderKey;
    public string DisplayName { get; set; } = DisplayName;
    public bool IsConnected { get; set; } = IsConnected;
    public string ConnectionStatus { get; set; } = ConnectionStatus;
    public DateTime? LastSuccessfulSyncUtc { get; set; } = LastSuccessfulSyncUtc;
    public IReadOnlyCollection<string> GrantedScopes { get; set; } = GrantedScopes;
    public string ModeExplanation { get; set; } = ModeExplanation;
    public string? SafeIssueSummary { get; set; } = SafeIssueSummary;

    public AccountingAuthorityProviderDto() : this(string.Empty, string.Empty, default !, string.Empty, default !, [], string.Empty, default !)
    {
    }
}

public sealed record AccountingAuthorityReadModel(Guid CompanyId, AccountingAuthorityPeriodDto? CurrentPeriod, IReadOnlyList<AccountingAuthorityPeriodDto> Periods, IReadOnlyList<AccountingAuthorityProviderDto> Providers, IReadOnlyList<AccountingProviderExportDto> Exports, int PendingExportCount, int ReconciliationRequiredCount, string Explanation, bool CanChangeAuthority)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public AccountingAuthorityPeriodDto? CurrentPeriod { get; set; } = CurrentPeriod;
    public IReadOnlyList<AccountingAuthorityPeriodDto> Periods { get; set; } = Periods;
    public IReadOnlyList<AccountingAuthorityProviderDto> Providers { get; set; } = Providers;
    public IReadOnlyList<AccountingProviderExportDto> Exports { get; set; } = Exports;
    public int PendingExportCount { get; set; } = PendingExportCount;
    public int ReconciliationRequiredCount { get; set; } = ReconciliationRequiredCount;
    public string Explanation { get; set; } = Explanation;
    public bool CanChangeAuthority { get; set; } = CanChangeAuthority;

    public AccountingAuthorityReadModel() : this(default !, default !, [], [], [], default !, default !, string.Empty, default !)
    {
    }
}

public sealed record AccountingProviderExportDto(Guid Id, Guid LedgerEntryId, string JournalNumber, DateOnly PostingDate, string SourceType, string SourceId, string SourceVersion, string ProviderKey, string ProviderName, string Status, string StatusLabel, Guid WriteRequestId, Guid? ApprovalRequestId, string? FailureCategory, string? SafeSummary, string? ProviderExternalId, int AttemptCount, long Version, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid LedgerEntryId { get; set; } = LedgerEntryId;
    public string JournalNumber { get; set; } = JournalNumber;
    public DateOnly PostingDate { get; set; } = PostingDate;
    public string SourceType { get; set; } = SourceType;
    public string SourceId { get; set; } = SourceId;
    public string SourceVersion { get; set; } = SourceVersion;
    public string ProviderKey { get; set; } = ProviderKey;
    public string ProviderName { get; set; } = ProviderName;
    public string Status { get; set; } = Status;
    public string StatusLabel { get; set; } = StatusLabel;
    public Guid WriteRequestId { get; set; } = WriteRequestId;
    public Guid? ApprovalRequestId { get; set; } = ApprovalRequestId;
    public string? FailureCategory { get; set; } = FailureCategory;
    public string? SafeSummary { get; set; } = SafeSummary;
    public string? ProviderExternalId { get; set; } = ProviderExternalId;
    public int AttemptCount { get; set; } = AttemptCount;
    public long Version { get; set; } = Version;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public AccountingProviderExportDto() : this(default !, default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingAuthorityChangePreview(Guid CompanyId, string CurrentAuthority, string TargetAuthority, string? ProviderKey, Guid EffectiveFiscalPeriodId, DateOnly EffectiveFrom, DateOnly EffectiveTo, int PostedJournalCount, int PendingExportCount, int UnmappedSourceCount, string PreviewToken, long ExpectedCurrentVersion, bool IsAllowed, IReadOnlyList<AccountingAuthorityIssueDto> Issues, IReadOnlyList<AccountingAuthorityIssueDto> Warnings)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public string CurrentAuthority { get; set; } = CurrentAuthority;
    public string TargetAuthority { get; set; } = TargetAuthority;
    public string? ProviderKey { get; set; } = ProviderKey;
    public Guid EffectiveFiscalPeriodId { get; set; } = EffectiveFiscalPeriodId;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly EffectiveTo { get; set; } = EffectiveTo;
    public int PostedJournalCount { get; set; } = PostedJournalCount;
    public int PendingExportCount { get; set; } = PendingExportCount;
    public int UnmappedSourceCount { get; set; } = UnmappedSourceCount;
    public string PreviewToken { get; set; } = PreviewToken;
    public long ExpectedCurrentVersion { get; set; } = ExpectedCurrentVersion;
    public bool IsAllowed { get; set; } = IsAllowed;
    public IReadOnlyList<AccountingAuthorityIssueDto> Issues { get; set; } = Issues;
    public IReadOnlyList<AccountingAuthorityIssueDto> Warnings { get; set; } = Warnings;

    public AccountingAuthorityChangePreview() : this(default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, string.Empty, default !, default !, [], [])
    {
    }
}

public sealed record AccountingAuthorityPeriodDto(Guid Id, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Authority, string AuthorityLabel, string? TargetAuthority, string? TargetAuthorityLabel, string? ProviderKey, string? ProviderName, string ChangeReason, bool OpeningBalancesReconciled, bool TrialBalanceReconciled, bool SourceMappingsReconciled, int ConflictCount, string? ValidationSummary, bool IsCutoverReady, long Version, DateTime UpdatedUtc, DateTime? CompletedUtc)
{
    public Guid Id { get; set; } = Id;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public string Authority { get; set; } = Authority;
    public string AuthorityLabel { get; set; } = AuthorityLabel;
    public string? TargetAuthority { get; set; } = TargetAuthority;
    public string? TargetAuthorityLabel { get; set; } = TargetAuthorityLabel;
    public string? ProviderKey { get; set; } = ProviderKey;
    public string? ProviderName { get; set; } = ProviderName;
    public string ChangeReason { get; set; } = ChangeReason;
    public bool OpeningBalancesReconciled { get; set; } = OpeningBalancesReconciled;
    public bool TrialBalanceReconciled { get; set; } = TrialBalanceReconciled;
    public bool SourceMappingsReconciled { get; set; } = SourceMappingsReconciled;
    public int ConflictCount { get; set; } = ConflictCount;
    public string? ValidationSummary { get; set; } = ValidationSummary;
    public bool IsCutoverReady { get; set; } = IsCutoverReady;
    public long Version { get; set; } = Version;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public DateTime? CompletedUtc { get; set; } = CompletedUtc;

    public AccountingAuthorityPeriodDto() : this(default !, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}
