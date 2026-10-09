using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Finance;
public sealed record AccountingConfigurationIssueDto(string ReasonCode, string Explanation, string? SubjectKey = null, bool IsBlocking = true)
{
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string? SubjectKey { get; set; } = SubjectKey;
    public bool IsBlocking { get; set; } = IsBlocking;

    public AccountingConfigurationIssueDto() : this(string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record AccountingAccountRoleReferenceDto(string RoleKey, string DisplayName, bool IsRequired, bool IsControlAccount, Guid? FinanceAccountId, string? FinanceAccountCode, string? FinanceAccountName)
{
    public string RoleKey { get; set; } = RoleKey;
    public string DisplayName { get; set; } = DisplayName;
    public bool IsRequired { get; set; } = IsRequired;
    public bool IsControlAccount { get; set; } = IsControlAccount;
    public Guid? FinanceAccountId { get; set; } = FinanceAccountId;
    public string? FinanceAccountCode { get; set; } = FinanceAccountCode;
    public string? FinanceAccountName { get; set; } = FinanceAccountName;

    public AccountingAccountRoleReferenceDto() : this(string.Empty, string.Empty, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingPolicyPackImpactPreviewDto(Guid CompanyId, string TargetPackKey, string TargetPackVersion, DateOnly EffectiveFrom, bool IsAllowed, bool IsUpgrade, IReadOnlyList<string> AddedRequiredAccountRoles, IReadOnlyList<string> RemovedAccountRoles, IReadOnlyList<string> AddedTaxRules, IReadOnlyList<string> RemovedTaxRules, IReadOnlyList<string> AddedExports, IReadOnlyList<string> RemovedExports, IReadOnlyList<AccountingConfigurationIssueDto> Issues, IReadOnlyList<AccountingConfigurationIssueDto> Warnings)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public string TargetPackKey { get; set; } = TargetPackKey;
    public string TargetPackVersion { get; set; } = TargetPackVersion;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;
    public bool IsAllowed { get; set; } = IsAllowed;
    public bool IsUpgrade { get; set; } = IsUpgrade;
    public IReadOnlyList<string> AddedRequiredAccountRoles { get; set; } = AddedRequiredAccountRoles;
    public IReadOnlyList<string> RemovedAccountRoles { get; set; } = RemovedAccountRoles;
    public IReadOnlyList<string> AddedTaxRules { get; set; } = AddedTaxRules;
    public IReadOnlyList<string> RemovedTaxRules { get; set; } = RemovedTaxRules;
    public IReadOnlyList<string> AddedExports { get; set; } = AddedExports;
    public IReadOnlyList<string> RemovedExports { get; set; } = RemovedExports;
    public IReadOnlyList<AccountingConfigurationIssueDto> Issues { get; set; } = Issues;
    public IReadOnlyList<AccountingConfigurationIssueDto> Warnings { get; set; } = Warnings;

    public AccountingPolicyPackImpactPreviewDto() : this(default !, string.Empty, string.Empty, default !, default !, default !, [], [], [], [], [], [], [], [])
    {
    }
}

public sealed record AccountingSetupStatusDto(Guid CompanyId, bool IsConfigured, bool CanUseInternalLedger, bool IsReady, bool IsCountrySpecificComplianceConfigured, string Authority, string SetupState, AccountingConfigurationDto? Configuration, IReadOnlyList<AccountingConfigurationIssueDto> Issues, IReadOnlyList<AccountingConfigurationIssueDto> Warnings, CompanyStatutoryProfileStatusDto? StatutoryProfile = null, string PolicyPackValidationState = "not_selected", IReadOnlyList<string>? MissingLegalFacts = null, IReadOnlyList<string>? NextActions = null)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public bool IsConfigured { get; set; } = IsConfigured;
    public bool CanUseInternalLedger { get; set; } = CanUseInternalLedger;
    public bool IsReady { get; set; } = IsReady;
    public bool IsCountrySpecificComplianceConfigured { get; set; } = IsCountrySpecificComplianceConfigured;
    public string Authority { get; set; } = Authority;
    public string SetupState { get; set; } = SetupState;
    public AccountingConfigurationDto? Configuration { get; set; } = Configuration;
    public IReadOnlyList<AccountingConfigurationIssueDto> Issues { get; set; } = Issues;
    public IReadOnlyList<AccountingConfigurationIssueDto> Warnings { get; set; } = Warnings;
    public CompanyStatutoryProfileStatusDto? StatutoryProfile { get; set; } = StatutoryProfile;
    public string PolicyPackValidationState { get; set; } = PolicyPackValidationState;
    public IReadOnlyList<string>? MissingLegalFacts { get; set; } = MissingLegalFacts;
    public IReadOnlyList<string>? NextActions { get; set; } = NextActions;

    public AccountingSetupStatusDto() : this(default !, default !, default !, default !, default !, string.Empty, string.Empty, default !, [], [], default !, string.Empty, [], [])
    {
    }
}

public sealed record AccountingConfigurationDto(Guid Id, Guid CompanyId, string BaseCurrency, int FiscalYearStartMonth, int FiscalYearStartDay, string Authority, string SetupState, string PolicyPackKey, string PolicyPackVersion, DateOnly PolicyPackEffectiveFrom, int RoundingPrecision, string RoundingMode, long Version, bool IsCountryNeutral, bool IsStatutoryComplianceValidated, string ComplianceNotice, IReadOnlyList<AccountingAccountRoleReferenceDto> AccountRoles, IReadOnlyList<AccountingPolicyPackSelectionDto> PolicyPackHistory, DateTime CreatedUtc, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public string BaseCurrency { get; set; } = BaseCurrency;
    public int FiscalYearStartMonth { get; set; } = FiscalYearStartMonth;
    public int FiscalYearStartDay { get; set; } = FiscalYearStartDay;
    public string Authority { get; set; } = Authority;
    public string SetupState { get; set; } = SetupState;
    public string PolicyPackKey { get; set; } = PolicyPackKey;
    public string PolicyPackVersion { get; set; } = PolicyPackVersion;
    public DateOnly PolicyPackEffectiveFrom { get; set; } = PolicyPackEffectiveFrom;
    public int RoundingPrecision { get; set; } = RoundingPrecision;
    public string RoundingMode { get; set; } = RoundingMode;
    public long Version { get; set; } = Version;
    public bool IsCountryNeutral { get; set; } = IsCountryNeutral;
    public bool IsStatutoryComplianceValidated { get; set; } = IsStatutoryComplianceValidated;
    public string ComplianceNotice { get; set; } = ComplianceNotice;
    public IReadOnlyList<AccountingAccountRoleReferenceDto> AccountRoles { get; set; } = AccountRoles;
    public IReadOnlyList<AccountingPolicyPackSelectionDto> PolicyPackHistory { get; set; } = PolicyPackHistory;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public AccountingConfigurationDto() : this(default !, default !, string.Empty, default !, default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, string.Empty, default !, default !, default !, string.Empty, [], [], default !, default !)
    {
    }
}

public sealed record AccountingPolicyPackSelectionDto(Guid Id, string PackKey, string PackVersion, string DefinitionHash, bool IsStatutoryComplianceValidated, DateOnly EffectiveFrom, DateOnly? EffectiveTo, Guid SelectedByUserId, DateTime SelectedUtc)
{
    public Guid Id { get; set; } = Id;
    public string PackKey { get; set; } = PackKey;
    public string PackVersion { get; set; } = PackVersion;
    public string DefinitionHash { get; set; } = DefinitionHash;
    public bool IsStatutoryComplianceValidated { get; set; } = IsStatutoryComplianceValidated;
    public DateOnly EffectiveFrom { get; set; } = EffectiveFrom;
    public DateOnly? EffectiveTo { get; set; } = EffectiveTo;
    public Guid SelectedByUserId { get; set; } = SelectedByUserId;
    public DateTime SelectedUtc { get; set; } = SelectedUtc;

    public AccountingPolicyPackSelectionDto() : this(default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record AccountingCapabilityDecisionDto(Guid CompanyId, string CapabilityKey, bool IsAvailable, string? ReasonCode, string Explanation, string PolicyPackKey, string PolicyPackVersion)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public string CapabilityKey { get; set; } = CapabilityKey;
    public bool IsAvailable { get; set; } = IsAvailable;
    public string? ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string PolicyPackKey { get; set; } = PolicyPackKey;
    public string PolicyPackVersion { get; set; } = PolicyPackVersion;

    public AccountingCapabilityDecisionDto() : this(default !, string.Empty, default !, default !, string.Empty, string.Empty, string.Empty)
    {
    }
}
