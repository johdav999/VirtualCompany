namespace VirtualCompany.Application.Finance;

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

public static class AccountingGovernanceReasonCodes
{
    public const string ReplacementRequired = "accounting_account_replacement_required";
    public const string ReplacementInvalid = "accounting_account_replacement_invalid";
    public const string LifecycleConflict = "accounting_account_lifecycle_conflict";
    public const string SeriesPolicyConflict = "accounting_series_policy_conflict";
    public const string SeriesPolicyMismatch = "accounting_series_policy_mismatch";
    public const string VoucherGapNotFound = "accounting_voucher_gap_not_found";
    public const string InventoryUnsupported = "accounting_inventory_unsupported";
    public const string CommerceContractUnsupported = "accounting_commerce_contract_unsupported";
}
public sealed record PreviewAccountingAccountLifecycleQuery(Guid CompanyId, Guid AccountId, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, Guid? ReplacementAccountId, string AccountClass, string NormalBalance,
    bool IsReportable, string PostingRestriction);
public sealed record ApplyAccountingAccountLifecycleCommand(Guid CompanyId, Guid AccountId, string Name,
    string AccountClass, string NormalBalance, bool IsReportable, string PostingRestriction,
    DateOnly EffectiveFrom, DateOnly? EffectiveTo, Guid? ReplacementAccountId, string Reason,
    long ExpectedLifecycleVersion, Guid ActorUserId, string? CorrelationId = null);
public sealed record SaveAccountingSeriesPolicyCommand(Guid CompanyId, Guid? PolicyId, string SeriesKind,
    Guid SeriesId, string SourceType, string TransactionType, int? FiscalYear, Guid? LocationDimensionMemberId,
    string? Jurisdiction, string? ProviderKey, string? ProviderSeriesCode, bool IsActive,
    long? ExpectedVersion, Guid ActorUserId, string? CorrelationId = null);
public sealed record RecordVoucherGapEvidenceCommand(Guid CompanyId, Guid VoucherSeriesId, int FiscalYear,
    long MissingNumber, string Reason, Guid ActorUserId, string? CorrelationId = null);
public sealed record SubmitCommerceAccountingEventCommand(Guid CompanyId, Guid EventId, long EventVersion,
    string ContractVersion, string EventType, string SourceSystem, DateTime OccurredUtc,
    bool RequiresInventoryAccounting, Guid ActorUserId, string? CorrelationId = null);
public sealed record CommerceAccountingEventResultDto(Guid EventId, long EventVersion, string Status, string Explanation);

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

public sealed record GetAccountingPeriodsQuery(Guid CompanyId);
public sealed record GetAccountingPeriodQuery(Guid CompanyId, Guid PeriodId);

public sealed record PreviewAccountingFiscalYearQuery(Guid CompanyId, DateOnly FiscalYearStart);

public sealed record CreateAccountingFiscalYearCommand(
    Guid CompanyId,
    DateOnly FiscalYearStart,
    Guid ActorUserId,
    string? IdempotencyKey = null,
    string? CorrelationId = null);

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
    Task<AccountingAccountLifecyclePreviewDto> PreviewAccountLifecycleAsync(PreviewAccountingAccountLifecycleQuery query, CancellationToken cancellationToken);
    Task<AccountingAccountDetailDto> ApplyAccountLifecycleAsync(ApplyAccountingAccountLifecycleCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingSeriesPolicyDto>> GetSeriesPoliciesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<AccountingSeriesPolicyDto> SaveSeriesPolicyAsync(SaveAccountingSeriesPolicyCommand command, CancellationToken cancellationToken);
    Task<AccountingSeriesPolicyDto> RecordVoucherGapEvidenceAsync(RecordVoucherGapEvidenceCommand command, CancellationToken cancellationToken);
    Task<CommerceAccountingCapabilityDto> GetCommerceCapabilityAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CommerceAccountingEventResultDto> SubmitCommerceEventAsync(SubmitCommerceAccountingEventCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingFiscalYearDto>> GetFiscalYearsAsync(GetAccountingPeriodsQuery query, CancellationToken cancellationToken);
    Task<AccountingPeriodDto> GetPeriodAsync(GetAccountingPeriodQuery query, CancellationToken cancellationToken);
    Task<AccountingFiscalYearPreviewDto> PreviewFiscalYearAsync(PreviewAccountingFiscalYearQuery query, CancellationToken cancellationToken);
    Task<AccountingFiscalYearCreationDto> CreateFiscalYearAsync(CreateAccountingFiscalYearCommand command, CancellationToken cancellationToken);
}
