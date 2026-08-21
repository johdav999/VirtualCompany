using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Finance;

public static class AccountingPolicyPackDefaults
{
    public const string CountryNeutralPackKey = "country-neutral";
    public const string CountryNeutralVersion = "1.0.0";
    public const string CountryNeutralBankingVersion = "1.1.0";
}

public static class AccountingAccountRoleKeys
{
    public const string Cash = "cash";
    public const string Bank = "bank";
    public const string AccountsReceivable = "accounts_receivable";
    public const string AccountsPayable = "accounts_payable";
    public const string BankFee = "bank_fee";
    public const string RoundingDifference = "rounding_difference";
    public const string ExchangeGain = "exchange_gain";
    public const string ExchangeLoss = "exchange_loss";
    public const string SettlementDiscount = "settlement_discount";
    public const string Suspense = "suspense";

    public static IReadOnlySet<string> BankAdjustmentRoles { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        BankFee,
        RoundingDifference,
        ExchangeGain,
        ExchangeLoss,
        SettlementDiscount
    };
}

public static class AccountingConfigurationReasonCodes
{
    public const string IncompleteConfiguration = "incomplete_configuration";
    public const string UnsupportedPackVersion = "unsupported_pack_version";
    public const string InvalidUpgrade = "invalid_upgrade";
    public const string MissingRequiredAccountRole = "missing_required_account_role";
    public const string CountrySpecificCapabilityUnavailable = "country_specific_capability_unavailable";
    public const string ConfigurationAlreadyExists = "accounting_configuration_already_exists";
    public const string ConfigurationNotFound = "accounting_configuration_not_found";
    public const string ConcurrencyConflict = "accounting_configuration_concurrency_conflict";
    public const string InvalidAccountRole = "invalid_account_role";
    public const string InvalidChartTemplate = "invalid_chart_template";
    public const string InvalidFiscalYear = "invalid_fiscal_year";
    public const string SetupConflict = "accounting_setup_conflict";
    public const string AccountNotFound = "accounting_account_not_found";
    public const string AccountCodeConflict = "accounting_account_code_conflict";
    public const string AccountProtected = "accounting_account_protected";
    public const string AccountHasPostedHistory = "accounting_account_has_posted_history";
    public const string PeriodNotFound = "accounting_period_not_found";
    public const string PeriodOverlap = "accounting_period_overlap";
    public const string PeriodGap = "accounting_period_gap";
}

public static class AccountingPolicyCapabilityKeys
{
    public const string CountrySpecificReporting = "country_specific_reporting";
    public const string CountrySpecificTax = "country_specific_tax";
    public const string StatutoryExport = "statutory_export";
}

public sealed record AccountingChartAccountTemplate(
    string Code,
    string Label,
    string AccountClass,
    string NormalBalance,
    string? DefaultRoleKey = null);

public sealed record AccountingChartTemplateDefinition(
    string Key,
    string DisplayName,
    IReadOnlyList<AccountingChartAccountTemplate> Accounts);

public sealed record AccountingAccountRoleDefinition(
    string Key,
    string DisplayName,
    bool IsRequired,
    bool IsControlAccount);

public sealed record AccountingTaxRuleDefinition(
    string Key,
    string DisplayName,
    DateOnly EffectiveFrom,
    decimal? Rate,
    string? LiabilityAccountRoleKey,
    string? RecoverableAccountRoleKey,
    string AmountMethod = CustomerInvoiceTaxMethodValues.Exclusive);

public sealed record AccountingInvoicePolicyDefinition(
    bool RequiresSequentialNumbers,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> SupportedDocumentTypes);

public sealed record AccountingReportingMappingDefinition(
    string AccountClass,
    string Statement,
    string SectionKey);

public sealed record AccountingRetentionAndLockPolicyDefinition(
    int? MinimumRetentionYears,
    bool RequiresEvidenceForPosting,
    bool AllowsPeriodReopening,
    string Description);

public sealed record AccountingPolicyPackDefinition(
    string PackKey,
    string Version,
    string DisplayName,
    string? CountryOrRegion,
    bool IsCountryNeutral,
    bool IsStatutoryComplianceValidated,
    string ComplianceNotice,
    IReadOnlyList<AccountingChartTemplateDefinition> ChartTemplates,
    IReadOnlyList<AccountingAccountRoleDefinition> AccountRoles,
    IReadOnlyList<AccountingTaxRuleDefinition> TaxRules,
    AccountingInvoicePolicyDefinition InvoicePolicy,
    IReadOnlyList<AccountingReportingMappingDefinition> ReportingMappings,
    IReadOnlyDictionary<string, string> Terminology,
    AccountingRetentionAndLockPolicyDefinition RetentionAndLockPolicy,
    IReadOnlyList<string> SupportedExports,
    IReadOnlyList<string> SupportedCapabilities);

public interface IAccountingPolicyPack
{
    AccountingPolicyPackDefinition Definition { get; }
    string DefinitionHash { get; }
}

public interface IAccountingPolicyPackResolver
{
    IAccountingPolicyPack Resolve(string packKey, string version);
    bool TryResolve(string packKey, string version, out IAccountingPolicyPack? pack);
    IReadOnlyList<IAccountingPolicyPack> GetAll();
}

public sealed record AccountingConfigurationIssueDto(
    string ReasonCode,
    string Explanation,
    string? SubjectKey = null,
    bool IsBlocking = true);

public sealed record AccountingAccountRoleReferenceDto(
    string RoleKey,
    string DisplayName,
    bool IsRequired,
    bool IsControlAccount,
    Guid? FinanceAccountId,
    string? FinanceAccountCode,
    string? FinanceAccountName);

public sealed record AccountingPolicyPackSelectionDto(
    Guid Id,
    string PackKey,
    string PackVersion,
    string DefinitionHash,
    bool IsStatutoryComplianceValidated,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    Guid SelectedByUserId,
    DateTime SelectedUtc);

public sealed record AccountingConfigurationDto(
    Guid Id,
    Guid CompanyId,
    string BaseCurrency,
    int FiscalYearStartMonth,
    int FiscalYearStartDay,
    string Authority,
    string SetupState,
    string PolicyPackKey,
    string PolicyPackVersion,
    DateOnly PolicyPackEffectiveFrom,
    int RoundingPrecision,
    string RoundingMode,
    long Version,
    bool IsCountryNeutral,
    bool IsStatutoryComplianceValidated,
    string ComplianceNotice,
    IReadOnlyList<AccountingAccountRoleReferenceDto> AccountRoles,
    IReadOnlyList<AccountingPolicyPackSelectionDto> PolicyPackHistory,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record AccountingSetupStatusDto(
    Guid CompanyId,
    bool IsConfigured,
    bool CanUseInternalLedger,
    bool IsReady,
    bool IsCountrySpecificComplianceConfigured,
    string Authority,
    string SetupState,
    AccountingConfigurationDto? Configuration,
    IReadOnlyList<AccountingConfigurationIssueDto> Issues,
    IReadOnlyList<AccountingConfigurationIssueDto> Warnings);

public sealed record AccountingPolicyPackImpactPreviewDto(
    Guid CompanyId,
    string TargetPackKey,
    string TargetPackVersion,
    DateOnly EffectiveFrom,
    bool IsAllowed,
    bool IsUpgrade,
    IReadOnlyList<string> AddedRequiredAccountRoles,
    IReadOnlyList<string> RemovedAccountRoles,
    IReadOnlyList<string> AddedTaxRules,
    IReadOnlyList<string> RemovedTaxRules,
    IReadOnlyList<string> AddedExports,
    IReadOnlyList<string> RemovedExports,
    IReadOnlyList<AccountingConfigurationIssueDto> Issues,
    IReadOnlyList<AccountingConfigurationIssueDto> Warnings);

public sealed record AccountingCapabilityDecisionDto(
    Guid CompanyId,
    string CapabilityKey,
    bool IsAvailable,
    string? ReasonCode,
    string Explanation,
    string PolicyPackKey,
    string PolicyPackVersion);

public sealed record GetAccountingSetupStatusQuery(Guid CompanyId);

public sealed record CreateInitialAccountingConfigurationCommand(
    Guid CompanyId,
    string BaseCurrency,
    int FiscalYearStartMonth,
    int FiscalYearStartDay,
    string PolicyPackKey,
    string PolicyPackVersion,
    DateOnly EffectiveFrom,
    int RoundingPrecision,
    string RoundingMode,
    IReadOnlyDictionary<string, Guid> AccountRoleAssignments,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record PreviewAccountingPolicyPackSelectionQuery(
    Guid CompanyId,
    string PackKey,
    string PackVersion,
    DateOnly EffectiveFrom,
    IReadOnlyDictionary<string, Guid>? AccountRoleAssignments = null);

public sealed record ApplyAccountingPolicyPackSelectionCommand(
    Guid CompanyId,
    string PackKey,
    string PackVersion,
    DateOnly EffectiveFrom,
    long ExpectedVersion,
    IReadOnlyDictionary<string, Guid> AccountRoleAssignments,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record ValidateAccountingConfigurationQuery(Guid CompanyId);
public sealed record GetAccountingCapabilityQuery(Guid CompanyId, string CapabilityKey);

public interface IAccountingConfigurationService
{
    Task<AccountingSetupStatusDto> GetSetupStatusAsync(GetAccountingSetupStatusQuery query, CancellationToken cancellationToken);
    Task<AccountingSetupStatusDto> CreateInitialAsync(CreateInitialAccountingConfigurationCommand command, CancellationToken cancellationToken);
    Task<AccountingPolicyPackImpactPreviewDto> PreviewPolicyPackSelectionAsync(PreviewAccountingPolicyPackSelectionQuery query, CancellationToken cancellationToken);
    Task<AccountingSetupStatusDto> ApplyPolicyPackSelectionAsync(ApplyAccountingPolicyPackSelectionCommand command, CancellationToken cancellationToken);
    Task<AccountingSetupStatusDto> ValidateAsync(ValidateAccountingConfigurationQuery query, CancellationToken cancellationToken);
    Task<AccountingCapabilityDecisionDto> GetCapabilityAsync(GetAccountingCapabilityQuery query, CancellationToken cancellationToken);
}

public sealed class AccountingConfigurationException : Exception
{
    public AccountingConfigurationException(string reasonCode, string message, bool isConflict = false)
        : base(message)
    {
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode)
            ? throw new ArgumentException("ReasonCode is required.", nameof(reasonCode))
            : reasonCode.Trim();
        IsConflict = isConflict;
    }

    public string ReasonCode { get; }
    public bool IsConflict { get; }
}
