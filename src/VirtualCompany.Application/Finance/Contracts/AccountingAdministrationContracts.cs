namespace VirtualCompany.Application.Finance;

public sealed record AccountingPolicyPackOptionDto(
    string PackKey,
    string PackVersion,
    string DisplayName,
    string? CountryOrRegion,
    bool IsCountryNeutral,
    bool IsStatutoryComplianceValidated,
    string ComplianceNotice,
    IReadOnlyList<AccountingChartTemplateOptionDto> ChartTemplates);

public sealed record AccountingChartTemplateOptionDto(
    string TemplateKey,
    string DisplayName,
    int AccountCount);

public sealed record AccountingSetupAccountPreviewDto(
    string Code,
    string Name,
    string AccountClass,
    string NormalBalance,
    string? RoleName,
    bool IsControlAccount,
    string ReportingPlacement);

public sealed record AccountingSetupTaxPreviewDto(
    string Name,
    decimal? Rate,
    DateOnly EffectiveFrom);

public sealed record AccountingSetupPeriodPreviewDto(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate);

public sealed record AccountingVoucherSeriesPreviewDto(
    string Code,
    string DisplayName,
    string NumberPrefix);

public sealed record AccountingSetupPreviewDto(
    Guid CompanyId,
    string BaseCurrency,
    DateOnly FiscalYearStart,
    DateOnly FiscalYearEnd,
    string PolicyPackName,
    string ChartTemplateName,
    bool IsCountryNeutral,
    bool IsStatutoryComplianceValidated,
    string ComplianceNotice,
    string TaxBehavior,
    bool IsValid,
    bool IsAlreadyConfigured,
    IReadOnlyList<AccountingSetupAccountPreviewDto> Accounts,
    IReadOnlyList<AccountingSetupTaxPreviewDto> TaxRules,
    IReadOnlyList<AccountingSetupPeriodPreviewDto> Periods,
    IReadOnlyList<AccountingVoucherSeriesPreviewDto> VoucherSeries,
    IReadOnlyList<AccountingConfigurationIssueDto> Issues,
    IReadOnlyList<AccountingConfigurationIssueDto> Warnings,
    CompanyStatutoryProfileStatusDto? StatutoryProfile = null,
    string PolicyPackValidationState = "unvalidated",
    IReadOnlyList<string>? MissingLegalFacts = null,
    IReadOnlyList<string>? NextActions = null);

public sealed record PreviewAccountingSetupQuery(
    Guid CompanyId,
    string BaseCurrency,
    DateOnly FiscalYearStart,
    string PolicyPackKey,
    string PolicyPackVersion,
    string ChartTemplateKey,
    IReadOnlyDictionary<string, string>? AccountRoleCodeAssignments = null);

public sealed record CompleteAccountingSetupCommand(
    Guid CompanyId,
    string BaseCurrency,
    DateOnly FiscalYearStart,
    string PolicyPackKey,
    string PolicyPackVersion,
    string ChartTemplateKey,
    IReadOnlyDictionary<string, string>? AccountRoleCodeAssignments,
    Guid ActorUserId,
    string? IdempotencyKey = null,
    string? CorrelationId = null);

public sealed record AccountingSetupCompletionDto(
    AccountingSetupStatusDto SetupStatus,
    int AccountCount,
    int PeriodCount,
    int VoucherSeriesCount,
    bool WasAlreadyApplied);

public sealed record AccountingAccountListItemDto(
    Guid Id,
    string Code,
    string Name,
    string AccountClass,
    string NormalBalance,
    string Currency,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsPostingEnabled,
    bool HasPostedHistory,
    bool IsProtected,
    string? ProtectedReason,
    string? RoleName,
    string? ReportingPlacement,
    DateTime UpdatedUtc);

public sealed record AccountingAccountDetailDto(
    Guid Id,
    string Code,
    string Name,
    string AccountClass,
    string NormalBalance,
    string Currency,
    DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo,
    bool IsPostingEnabled,
    bool RestrictsManualPosting,
    bool HasPostedHistory,
    bool IsProtected,
    string? ProtectedReason,
    string? RoleName,
    string? ReportingPlacement,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record GetAccountingAccountsQuery(
    Guid CompanyId,
    string? Search = null,
    string? AccountClass = null,
    string? Status = null);

public sealed record GetAccountingAccountQuery(Guid CompanyId, Guid AccountId);

public sealed record CreateAccountingAccountCommand(
    Guid CompanyId,
    string Code,
    string Name,
    string AccountClass,
    string NormalBalance,
    DateOnly EffectiveFrom,
    Guid ActorUserId,
    string? CorrelationId = null,
    string? SourceCatalogKey = null,
    string? SourceCatalogVersion = null,
    string? SourceCatalogSha256 = null,
    bool AccountingSemanticsConfirmed = false,
    bool CompanySuitabilityConfirmed = false);

public sealed record RenameAccountingAccountCommand(
    Guid CompanyId,
    Guid AccountId,
    string Name,
    DateTime ExpectedUpdatedUtc,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record DeactivateAccountingAccountCommand(
    Guid CompanyId,
    Guid AccountId,
    DateOnly EffectiveTo,
    DateTime ExpectedUpdatedUtc,
    Guid ActorUserId,
    string? CorrelationId = null);

public sealed record AccountingPeriodDto(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsClosed,
    bool IsReportingLocked,
    DateTime? ClosedUtc,
    DateTime? ReportingLockedUtc,
    DateTime? LastCloseValidatedUtc,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record AccountingFiscalYearDto(
    DateOnly StartDate,
    DateOnly EndDate,
    int OpenPeriodCount,
    int ClosedPeriodCount,
    int ReportingLockedPeriodCount,
    IReadOnlyList<AccountingPeriodDto> Periods);

public sealed record GetAccountingPeriodsQuery(Guid CompanyId);
public sealed record GetAccountingPeriodQuery(Guid CompanyId, Guid PeriodId);

public sealed record PreviewAccountingFiscalYearQuery(Guid CompanyId, DateOnly FiscalYearStart);

public sealed record AccountingFiscalYearPreviewDto(
    Guid CompanyId,
    DateOnly StartDate,
    DateOnly EndDate,
    bool IsValid,
    IReadOnlyList<AccountingSetupPeriodPreviewDto> Periods,
    IReadOnlyList<AccountingConfigurationIssueDto> Issues);

public sealed record CreateAccountingFiscalYearCommand(
    Guid CompanyId,
    DateOnly FiscalYearStart,
    Guid ActorUserId,
    string? IdempotencyKey = null,
    string? CorrelationId = null);

public sealed record AccountingFiscalYearCreationDto(
    AccountingFiscalYearDto FiscalYear,
    bool WasAlreadyPresent);

public interface IAccountingAdministrationService
{
    Task<IReadOnlyList<AccountingPolicyPackOptionDto>> GetPolicyPacksAsync(CancellationToken cancellationToken);
    Task<AccountingSetupPreviewDto> PreviewSetupAsync(PreviewAccountingSetupQuery query, CancellationToken cancellationToken);
    Task<AccountingSetupCompletionDto> CompleteSetupAsync(CompleteAccountingSetupCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingAccountListItemDto>> GetAccountsAsync(GetAccountingAccountsQuery query, CancellationToken cancellationToken);
    Task<AccountingAccountDetailDto> GetAccountAsync(GetAccountingAccountQuery query, CancellationToken cancellationToken);
    Task<AccountingAccountDetailDto> CreateAccountAsync(CreateAccountingAccountCommand command, CancellationToken cancellationToken);
    Task<AccountingChartCatalogPageDto> GetChartCatalogAsync(GetAccountingChartCatalogQuery query, CancellationToken cancellationToken);
    Task<AccountingAccountDetailDto> CreateAccountFromCatalogAsync(CreateAccountingAccountFromCatalogCommand command, CancellationToken cancellationToken);
    Task<AccountingAccountDetailDto> RenameAccountAsync(RenameAccountingAccountCommand command, CancellationToken cancellationToken);
    Task<AccountingAccountDetailDto> DeactivateAccountAsync(DeactivateAccountingAccountCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingFiscalYearDto>> GetFiscalYearsAsync(GetAccountingPeriodsQuery query, CancellationToken cancellationToken);
    Task<AccountingPeriodDto> GetPeriodAsync(GetAccountingPeriodQuery query, CancellationToken cancellationToken);
    Task<AccountingFiscalYearPreviewDto> PreviewFiscalYearAsync(PreviewAccountingFiscalYearQuery query, CancellationToken cancellationToken);
    Task<AccountingFiscalYearCreationDto> CreateFiscalYearAsync(CreateAccountingFiscalYearCommand command, CancellationToken cancellationToken);
}
