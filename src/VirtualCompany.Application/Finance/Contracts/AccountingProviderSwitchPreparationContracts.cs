namespace VirtualCompany.Application.Finance;

public static class AccountingProviderSwitchPreparationReasonCodes
{
    public const string TargetMustBeInternal = "preparation_target_must_be_internal";
    public const string SourceMustBeExternal = "preparation_source_must_be_external";
    public const string PlanNotApproved = "preparation_plan_not_approved";
    public const string PlanStale = "preparation_plan_stale";
    public const string ConfigurationMissing = "preparation_configuration_missing";
    public const string ConfigurationIncomplete = "preparation_configuration_incomplete";
    public const string FiscalPeriodMissing = "preparation_fiscal_period_missing";
    public const string ChartRolesMissing = "preparation_chart_roles_missing";
    public const string TaxRulesMissing = "preparation_tax_rules_missing";
    public const string VoucherSeriesMissing = "preparation_voucher_series_missing";
    public const string BaseCurrencyInvalid = "preparation_base_currency_invalid";
    public const string ControlAccountsMissing = "preparation_control_accounts_missing";
    public const string DimensionsUnsupported = "preparation_dimensions_unsupported";
    public const string PolicyComplianceDisclosure = "preparation_policy_compliance_disclosure";
    public const string BlockingGap = "preparation_blocking_gap";
    public const string StagingIncomplete = "preparation_staging_incomplete";
    public const string CandidateInvalid = "preparation_candidate_invalid";
    public const string CandidateEvidenceMissing = "preparation_candidate_evidence_missing";
    public const string CandidateAlreadyRepresented = "preparation_candidate_already_represented";
    public const string ConnectionMissing = "preparation_source_connection_missing";
    public const string NotFound = "preparation_not_found";
    public const string NotReady = "preparation_not_ready";
    public const string ConcurrencyConflict = "preparation_concurrency_conflict";
    public const string Failed = "preparation_failed";
}

public sealed record EvaluateAccountingProviderSwitchInternalReadinessQuery(
    Guid CompanyId,
    Guid SwitchId,
    Guid? PlanId = null);

public sealed record StartAccountingProviderSwitchPreparationCommand(
    Guid CompanyId,
    Guid SwitchId,
    Guid PlanId,
    long ExpectedSwitchVersion,
    Guid ActorUserId,
    string IdempotencyKey,
    string CorrelationId);

public sealed record ReplayAccountingProviderSwitchPreparationCommand(
    Guid CompanyId,
    Guid SwitchId,
    Guid PreparationId,
    Guid ActorUserId,
    string CorrelationId);

public sealed record GetAccountingProviderSwitchPreparationQuery(
    Guid CompanyId,
    Guid SwitchId,
    Guid? PreparationId = null);

public sealed record ListAccountingProviderSwitchNativeCandidatesQuery(
    Guid CompanyId,
    Guid SwitchId,
    Guid? PreparationId = null,
    string? CandidateKind = null,
    string? Status = null,
    int Limit = 500);

public interface IAccountingProviderSwitchInternalReadinessPolicy
{
    Task<AccountingProviderSwitchInternalReadinessDto> EvaluateAsync(
        EvaluateAccountingProviderSwitchInternalReadinessQuery query,
        CancellationToken cancellationToken);
}

public interface IAccountingProviderSwitchPreparationService
{
    Task<AccountingProviderSwitchInternalReadinessDto> GetReadinessAsync(
        EvaluateAccountingProviderSwitchInternalReadinessQuery query,
        CancellationToken cancellationToken);
    Task<AccountingProviderSwitchPreparationDto> StartAsync(
        StartAccountingProviderSwitchPreparationCommand command,
        CancellationToken cancellationToken);
    Task<AccountingProviderSwitchPreparationDto> ReplayAsync(
        ReplayAccountingProviderSwitchPreparationCommand command,
        CancellationToken cancellationToken);
    Task<AccountingProviderSwitchPreparationDto> GetAsync(
        GetAccountingProviderSwitchPreparationQuery query,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingProviderSwitchNativeCandidateDto>> ListCandidatesAsync(
        ListAccountingProviderSwitchNativeCandidatesQuery query,
        CancellationToken cancellationToken);
}

public interface IAccountingProviderSwitchPreparationJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}
