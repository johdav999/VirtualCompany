namespace VirtualCompany.Application.Finance;
public sealed record AccountingSetupCompletionDto(AccountingSetupStatusDto SetupStatus, int AccountCount, int PeriodCount, int VoucherSeriesCount, bool WasAlreadyApplied)
{
    public AccountingSetupStatusDto SetupStatus { get; set; } = SetupStatus;
    public int AccountCount { get; set; } = AccountCount;
    public int PeriodCount { get; set; } = PeriodCount;
    public int VoucherSeriesCount { get; set; } = VoucherSeriesCount;
    public bool WasAlreadyApplied { get; set; } = WasAlreadyApplied;

    public AccountingSetupCompletionDto() : this(new(), default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingAccountListItemDto(Guid Id, string Code, string Name, string AccountClass, string NormalBalance, string Currency, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool IsPostingEnabled, bool HasPostedHistory, bool IsProtected, string? ProtectedReason, string? RoleName, string? ReportingPlacement, DateTime UpdatedUtc, bool IsReportable = true, string PostingRestriction = "none", Guid? ReplacementAccountId = null, string LifecycleStatus = "active", long LifecycleVersion = 1, int DependencyCount = 0)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public string AccountClass { get; set; } = AccountClass;
    public string NormalBalance { get; set; } = NormalBalance;
    public string Currency { get; set; } = Currency;
    public DateOnly? EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public bool IsPostingEnabled { get; set; } = IsPostingEnabled;
    public bool HasPostedHistory { get; set; } = HasPostedHistory;
    public bool IsProtected { get; set; } = IsProtected;
    public string? ProtectedReason { get; set; } = ProtectedReason;
    public string? RoleName { get; set; } = RoleName;
    public string? ReportingPlacement { get; set; } = ReportingPlacement;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public bool IsReportable { get; set; } = IsReportable;
    public string PostingRestriction { get; set; } = PostingRestriction;
    public Guid? ReplacementAccountId { get; set; } = ReplacementAccountId;
    public string LifecycleStatus { get; set; } = LifecycleStatus;
    public long LifecycleVersion { get; set; } = LifecycleVersion;
    public int DependencyCount { get; set; } = DependencyCount;

    public AccountingAccountListItemDto() : this(default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, "none", default !, "active", default !, default !)
    {
    }
}

public sealed record AccountingVoucherSeriesPreviewDto(string Code, string DisplayName, string NumberPrefix)
{
    public string Code { get; set; } = Code;
    public string DisplayName { get; set; } = DisplayName;
    public string NumberPrefix { get; set; } = NumberPrefix;

    public AccountingVoucherSeriesPreviewDto() : this(string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record CommerceAccountingCapabilityDto(string CapabilityState, string ContractVersion, bool SupportsInventoryQuantity, bool SupportsInventoryValuation, bool SupportsCogs, IReadOnlyList<string> AcceptedEventTypes, string Explanation)
{
    public string CapabilityState { get; set; } = CapabilityState;
    public string ContractVersion { get; set; } = ContractVersion;
    public bool SupportsInventoryQuantity { get; set; } = SupportsInventoryQuantity;
    public bool SupportsInventoryValuation { get; set; } = SupportsInventoryValuation;
    public bool SupportsCogs { get; set; } = SupportsCogs;
    public IReadOnlyList<string> AcceptedEventTypes { get; set; } = AcceptedEventTypes;
    public string Explanation { get; set; } = Explanation;

    public CommerceAccountingCapabilityDto() : this(string.Empty, string.Empty, default !, default !, default !, [], string.Empty)
    {
    }
}

public sealed record AccountingFiscalYearPreviewDto(Guid CompanyId, DateOnly StartDate, DateOnly EndDate, bool IsValid, IReadOnlyList<AccountingSetupPeriodPreviewDto> Periods, IReadOnlyList<AccountingConfigurationIssueDto> Issues)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public DateOnly StartDate { get; set; } = StartDate;
    public DateOnly EndDate { get; set; } = EndDate;
    public bool IsValid { get; set; } = IsValid;
    public IReadOnlyList<AccountingSetupPeriodPreviewDto> Periods { get; set; } = Periods;
    public IReadOnlyList<AccountingConfigurationIssueDto> Issues { get; set; } = Issues;

    public AccountingFiscalYearPreviewDto() : this(default !, default !, default !, default !, [], [])
    {
    }
}

public sealed record AccountingChartTemplateOptionDto(string TemplateKey, string DisplayName, int AccountCount)
{
    public string TemplateKey { get; set; } = TemplateKey;
    public string DisplayName { get; set; } = DisplayName;
    public int AccountCount { get; set; } = AccountCount;

    public AccountingChartTemplateOptionDto() : this(string.Empty, string.Empty, default !)
    {
    }
}

public sealed record AccountingAccountDependencyDto(string DependencyType, string DisplayName, int Count, bool IsBlocking)
{
    public string DependencyType { get; set; } = DependencyType;
    public string DisplayName { get; set; } = DisplayName;
    public int Count { get; set; } = Count;
    public bool IsBlocking { get; set; } = IsBlocking;

    public AccountingAccountDependencyDto() : this(string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingAccountDetailDto(Guid Id, string Code, string Name, string AccountClass, string NormalBalance, string Currency, DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool IsPostingEnabled, bool RestrictsManualPosting, bool HasPostedHistory, bool IsProtected, string? ProtectedReason, string? RoleName, string? ReportingPlacement, DateTime CreatedUtc, DateTime UpdatedUtc, bool IsReportable = true, string PostingRestriction = "none", Guid? ReplacementAccountId = null, string? ReplacementAccountCode = null, string LifecycleStatus = "active", long LifecycleVersion = 1, IReadOnlyList<AccountingAccountLifecycleHistoryDto>? LifecycleHistory = null)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public string AccountClass { get; set; } = AccountClass;
    public string NormalBalance { get; set; } = NormalBalance;
    public string Currency { get; set; } = Currency;
    public DateOnly? EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public bool IsPostingEnabled { get; set; } = IsPostingEnabled;
    public bool RestrictsManualPosting { get; set; } = RestrictsManualPosting;
    public bool HasPostedHistory { get; set; } = HasPostedHistory;
    public bool IsProtected { get; set; } = IsProtected;
    public string? ProtectedReason { get; set; } = ProtectedReason;
    public string? RoleName { get; set; } = RoleName;
    public string? ReportingPlacement { get; set; } = ReportingPlacement;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;
    public bool IsReportable { get; set; } = IsReportable;
    public string PostingRestriction { get; set; } = PostingRestriction;
    public Guid? ReplacementAccountId { get; set; } = ReplacementAccountId;
    public string? ReplacementAccountCode { get; set; } = ReplacementAccountCode;
    public string LifecycleStatus { get; set; } = LifecycleStatus;
    public long LifecycleVersion { get; set; } = LifecycleVersion;
    public IReadOnlyList<AccountingAccountLifecycleHistoryDto>? LifecycleHistory { get; set; } = LifecycleHistory;

    public AccountingAccountDetailDto() : this(default !, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, default !, "none", default !, default !, "active", default !, [])
    {
    }
}

public sealed record AccountingSetupPreviewDto(Guid CompanyId, string BaseCurrency, DateOnly FiscalYearStart, DateOnly FiscalYearEnd, string PolicyPackName, string ChartTemplateName, bool IsCountryNeutral, bool IsStatutoryComplianceValidated, string ComplianceNotice, string TaxBehavior, bool IsValid, bool IsAlreadyConfigured, IReadOnlyList<AccountingSetupAccountPreviewDto> Accounts, IReadOnlyList<AccountingSetupTaxPreviewDto> TaxRules, IReadOnlyList<AccountingSetupPeriodPreviewDto> Periods, IReadOnlyList<AccountingVoucherSeriesPreviewDto> VoucherSeries, IReadOnlyList<AccountingConfigurationIssueDto> Issues, IReadOnlyList<AccountingConfigurationIssueDto> Warnings, CompanyStatutoryProfileStatusDto? StatutoryProfile = null, string PolicyPackValidationState = "unvalidated", IReadOnlyList<string>? MissingLegalFacts = null, IReadOnlyList<string>? NextActions = null)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public string BaseCurrency { get; set; } = BaseCurrency;
    public DateOnly FiscalYearStart { get; set; } = FiscalYearStart;
    public DateOnly FiscalYearEnd { get; set; } = FiscalYearEnd;
    public string PolicyPackName { get; set; } = PolicyPackName;
    public string ChartTemplateName { get; set; } = ChartTemplateName;
    public bool IsCountryNeutral { get; set; } = IsCountryNeutral;
    public bool IsStatutoryComplianceValidated { get; set; } = IsStatutoryComplianceValidated;
    public string ComplianceNotice { get; set; } = ComplianceNotice;
    public string TaxBehavior { get; set; } = TaxBehavior;
    public bool IsValid { get; set; } = IsValid;
    public bool IsAlreadyConfigured { get; set; } = IsAlreadyConfigured;
    public IReadOnlyList<AccountingSetupAccountPreviewDto> Accounts { get; set; } = Accounts;
    public IReadOnlyList<AccountingSetupTaxPreviewDto> TaxRules { get; set; } = TaxRules;
    public IReadOnlyList<AccountingSetupPeriodPreviewDto> Periods { get; set; } = Periods;
    public IReadOnlyList<AccountingVoucherSeriesPreviewDto> VoucherSeries { get; set; } = VoucherSeries;
    public IReadOnlyList<AccountingConfigurationIssueDto> Issues { get; set; } = Issues;
    public IReadOnlyList<AccountingConfigurationIssueDto> Warnings { get; set; } = Warnings;
    public CompanyStatutoryProfileStatusDto? StatutoryProfile { get; set; } = StatutoryProfile;
    public string PolicyPackValidationState { get; set; } = PolicyPackValidationState;
    public IReadOnlyList<string>? MissingLegalFacts { get; set; } = MissingLegalFacts;
    public IReadOnlyList<string>? NextActions { get; set; } = NextActions;

    public AccountingSetupPreviewDto() : this(default !, string.Empty, default !, default !, string.Empty, string.Empty, default !, default !, string.Empty, string.Empty, default !, default !, [], [], [], [], [], [], default !, string.Empty, [], [])
    {
    }
}

public sealed record AccountingSetupAccountPreviewDto(string Code, string Name, string AccountClass, string NormalBalance, string? RoleName, bool IsControlAccount, string ReportingPlacement)
{
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public string AccountClass { get; set; } = AccountClass;
    public string NormalBalance { get; set; } = NormalBalance;
    public string? RoleName { get; set; } = RoleName;
    public bool IsControlAccount { get; set; } = IsControlAccount;
    public string ReportingPlacement { get; set; } = ReportingPlacement;

    public AccountingSetupAccountPreviewDto() : this(string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty)
    {
    }
}

public sealed record AccountingSetupPeriodPreviewDto(string Name, DateOnly StartDate, DateOnly EndDate)
{
    public string Name { get; set; } = Name;
    public DateOnly StartDate { get; set; } = StartDate;
    public DateOnly EndDate { get; set; } = EndDate;

    public AccountingSetupPeriodPreviewDto() : this(string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingAccountLifecycleHistoryDto(long Version, string ChangeType, string Name, string AccountClass, string NormalBalance, bool IsReportable, string PostingRestriction, DateOnly EffectiveFrom, DateOnly? EffectiveTo, Guid? ReplacementAccountId, string Reason, Guid? ActorUserId, DateTime RecordedUtc)
{
    public long Version { get; set; } = Version;
    public string ChangeType { get; set; } = ChangeType;
    public string Name { get; set; } = Name;
    public string AccountClass { get; set; } = AccountClass;
    public string NormalBalance { get; set; } = NormalBalance;
    public bool IsReportable { get; set; } = IsReportable;
    public string PostingRestriction { get; set; } = PostingRestriction;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public Guid? ReplacementAccountId { get; set; } = ReplacementAccountId;
    public string Reason { get; set; } = Reason;
    public Guid? ActorUserId { get; set; } = ActorUserId;
    public DateTime RecordedUtc { get; set; } = RecordedUtc;

    public AccountingAccountLifecycleHistoryDto() : this(default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, "none", default !, default !, default !, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingPolicyPackOptionDto(string PackKey, string PackVersion, string DisplayName, string? CountryOrRegion, bool IsCountryNeutral, bool IsStatutoryComplianceValidated, string ComplianceNotice, IReadOnlyList<AccountingChartTemplateOptionDto> ChartTemplates)
{
    public string PackKey { get; set; } = PackKey;
    public string PackVersion { get; set; } = PackVersion;
    public string DisplayName { get; set; } = DisplayName;
    public string? CountryOrRegion { get; set; } = CountryOrRegion;
    public bool IsCountryNeutral { get; set; } = IsCountryNeutral;
    public bool IsStatutoryComplianceValidated { get; set; } = IsStatutoryComplianceValidated;
    public string ComplianceNotice { get; set; } = ComplianceNotice;
    public IReadOnlyList<AccountingChartTemplateOptionDto> ChartTemplates { get; set; } = ChartTemplates;

    public AccountingPolicyPackOptionDto() : this(string.Empty, string.Empty, string.Empty, default !, default !, default !, string.Empty, [])
    {
    }
}

public sealed record AccountingPeriodDto(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, bool IsClosed, bool IsReportingLocked, DateTime? ClosedUtc, DateTime? ReportingLockedUtc, DateTime? LastCloseValidatedUtc, DateTime CreatedUtc, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Name { get; set; } = Name;
    public DateOnly StartDate { get; set; } = StartDate;
    public DateOnly EndDate { get; set; } = EndDate;
    public bool IsClosed { get; set; } = IsClosed;
    public bool IsReportingLocked { get; set; } = IsReportingLocked;
    public DateTime? ClosedUtc { get; set; } = ClosedUtc;
    public DateTime? ReportingLockedUtc { get; set; } = ReportingLockedUtc;
    public DateTime? LastCloseValidatedUtc { get; set; } = LastCloseValidatedUtc;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public AccountingPeriodDto() : this(default !, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingSeriesPolicyDto(Guid Id, string SeriesKind, Guid SeriesId, string SeriesCode, string SeriesName, string SourceType, string TransactionType, int? FiscalYear, Guid? LocationDimensionMemberId, string? Jurisdiction, string PolicyPackKey, string PolicyPackVersion, string? ProviderKey, string? ProviderSeriesCode, bool IsActive, long Version, int UnexplainedGapCount)
{
    public Guid Id { get; set; } = Id;
    public string SeriesKind { get; set; } = SeriesKind;
    public Guid SeriesId { get; set; } = SeriesId;
    public string SeriesCode { get; set; } = SeriesCode;
    public string SeriesName { get; set; } = SeriesName;
    public string SourceType { get; set; } = SourceType;
    public string TransactionType { get; set; } = TransactionType;
    public int? FiscalYear { get; set; } = FiscalYear;
    public Guid? LocationDimensionMemberId { get; set; } = LocationDimensionMemberId;
    public string? Jurisdiction { get; set; } = Jurisdiction;
    public string PolicyPackKey { get; set; } = PolicyPackKey;
    public string PolicyPackVersion { get; set; } = PolicyPackVersion;
    public string? ProviderKey { get; set; } = ProviderKey;
    public string? ProviderSeriesCode { get; set; } = ProviderSeriesCode;
    public bool IsActive { get; set; } = IsActive;
    public long Version { get; set; } = Version;
    public int UnexplainedGapCount { get; set; } = UnexplainedGapCount;

    public AccountingSeriesPolicyDto() : this(default !, string.Empty, default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingFiscalYearDto(DateOnly StartDate, DateOnly EndDate, int OpenPeriodCount, int ClosedPeriodCount, int ReportingLockedPeriodCount, IReadOnlyList<AccountingPeriodDto> Periods)
{
    public DateOnly StartDate { get; set; } = StartDate;
    public DateOnly EndDate { get; set; } = EndDate;
    public int OpenPeriodCount { get; set; } = OpenPeriodCount;
    public int ClosedPeriodCount { get; set; } = ClosedPeriodCount;
    public int ReportingLockedPeriodCount { get; set; } = ReportingLockedPeriodCount;
    public IReadOnlyList<AccountingPeriodDto> Periods { get; set; } = Periods;

    public AccountingFiscalYearDto() : this(default !, default !, default !, default !, default !, [])
    {
    }
}

public sealed record AccountingSetupTaxPreviewDto(string Name, decimal? Rate, DateOnly EffectiveFrom)
{
    public string Name { get; set; } = Name;
    public decimal? Rate { get; set; } = Rate;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;

    public AccountingSetupTaxPreviewDto() : this(string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingFiscalYearCreationDto(AccountingFiscalYearDto FiscalYear, bool WasAlreadyPresent)
{
    public AccountingFiscalYearDto FiscalYear { get; set; } = FiscalYear;
    public bool WasAlreadyPresent { get; set; } = WasAlreadyPresent;

    public AccountingFiscalYearCreationDto() : this(new(), default !)
    {
    }
}

public sealed record AccountingAccountLifecyclePreviewDto(Guid AccountId, string AccountCode, bool HasPostedHistory, bool ReplacementRequired, bool CanApply, IReadOnlyList<AccountingAccountDependencyDto> Dependencies, IReadOnlyList<AccountingConfigurationIssueDto> Issues)
{
    public Guid AccountId { get; set; } = AccountId;
    public string AccountCode { get; set; } = AccountCode;
    public bool HasPostedHistory { get; set; } = HasPostedHistory;
    public bool ReplacementRequired { get; set; } = ReplacementRequired;
    public bool CanApply { get; set; } = CanApply;
    public IReadOnlyList<AccountingAccountDependencyDto> Dependencies { get; set; } = Dependencies;
    public IReadOnlyList<AccountingConfigurationIssueDto> Issues { get; set; } = Issues;

    public AccountingAccountLifecyclePreviewDto() : this(default !, string.Empty, default !, default !, default !, [], [])
    {
    }
}
