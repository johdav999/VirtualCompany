namespace VirtualCompany.Application.Finance;

public static class AccountingProviderSwitchReasonCodes
{
    public const string NotFound = "accounting_provider_switch_not_found";
    public const string DuplicateActiveSwitch = "accounting_provider_switch_already_active";
    public const string InvalidEndpoint = "accounting_provider_switch_invalid_endpoint";
    public const string SameEndpoint = "accounting_provider_switch_same_endpoint";
    public const string InvalidStrategy = "accounting_provider_switch_invalid_strategy";
    public const string FiscalPeriodNotFound = "accounting_provider_switch_fiscal_period_not_found";
    public const string MonthlyBoundaryRequired = "accounting_provider_switch_monthly_boundary_required";
    public const string FutureBoundaryRequired = "accounting_provider_switch_future_boundary_required";
    public const string SourceAuthorityMismatch = "accounting_provider_switch_source_authority_mismatch";
    public const string ResponsibleUserInvalid = "accounting_provider_switch_responsible_user_invalid";
    public const string ResponsibleAgentInvalid = "accounting_provider_switch_responsible_agent_invalid";
    public const string ConcurrencyConflict = "accounting_provider_switch_concurrency_conflict";
    public const string IllegalTransition = "accounting_provider_switch_illegal_transition";
    public const string PlanLocked = "accounting_provider_switch_plan_locked";
    public const string CancellationUnavailable = "accounting_provider_switch_cancellation_unavailable";
    public const string AssessmentNotFound = "accounting_provider_switch_assessment_not_found";
    public const string AssessmentStaleVersion = "accounting_provider_switch_assessment_stale_version";
    public const string AssessmentUnavailable = "accounting_provider_switch_assessment_unavailable";
    public const string AssessmentReplayUnavailable = "accounting_provider_switch_assessment_replay_unavailable";
    public const string StagedRecordNotFound = "accounting_provider_switch_staged_record_not_found";
    public const string InvalidStagedRecord = "accounting_provider_switch_invalid_staged_record";
    public const string StagingUnavailable = "accounting_provider_switch_staging_unavailable";
    public const string MappingDecisionNotFound = "accounting_provider_switch_mapping_decision_not_found";
    public const string MappingDecisionStale = "accounting_provider_switch_mapping_decision_stale";
    public const string MappingApprovalRequired = "accounting_provider_switch_mapping_approval_required";
    public const string MappingApprovalInvalid = "accounting_provider_switch_mapping_approval_invalid";
    public const string StagingIncomplete = "accounting_provider_switch_staging_incomplete";
}

public sealed record AccountingProviderSwitchEndpointDto(string Kind, string? ProviderKey, string DisplayName);

public sealed record AccountingProviderSwitchDto(
    Guid Id,
    Guid CompanyId,
    AccountingProviderSwitchEndpointDto Source,
    AccountingProviderSwitchEndpointDto Target,
    string Direction,
    Guid EffectiveFiscalPeriodId,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    string MigrationStrategy,
    string MigrationStrategyLabel,
    string Reason,
    Guid ResponsibleUserId,
    Guid? ResponsibleAgentId,
    string Status,
    string StatusLabel,
    string? BlockedFromStatus,
    string? FailureCode,
    string? FailureSummary,
    Guid CreatedByUserId,
    Guid UpdatedByUserId,
    Guid? CancelledByUserId,
    string? CancellationReason,
    string CorrelationId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime StatusChangedUtc,
    DateTime? BlockedUtc,
    DateTime? CancelledUtc,
    DateTime? CompletedUtc,
    long Version);

public sealed record AccountingProviderSwitchAllowedActionsDto(
    Guid SwitchId,
    long Version,
    string Status,
    bool IsTerminal,
    bool CanUpdatePlan,
    bool CanCancel,
    bool IsReadyForNextStep,
    IReadOnlyList<string> AllowedTransitions,
    string Explanation,
    string? BlockingReasonCode,
    string? BlockingSummary);

public sealed record CreateAccountingProviderSwitchCommand(
    Guid CompanyId,
    string SourceKind,
    string? SourceProviderKey,
    string TargetKind,
    string? TargetProviderKey,
    Guid EffectiveFiscalPeriodId,
    string MigrationStrategy,
    string Reason,
    Guid ResponsibleUserId,
    Guid? ResponsibleAgentId,
    Guid ActorUserId,
    string CorrelationId);

public sealed record UpdateAccountingProviderSwitchPlanCommand(
    Guid CompanyId,
    Guid SwitchId,
    string SourceKind,
    string? SourceProviderKey,
    string TargetKind,
    string? TargetProviderKey,
    Guid EffectiveFiscalPeriodId,
    string MigrationStrategy,
    string Reason,
    Guid ResponsibleUserId,
    Guid? ResponsibleAgentId,
    long ExpectedVersion,
    Guid ActorUserId,
    string CorrelationId);

public sealed record CancelAccountingProviderSwitchCommand(
    Guid CompanyId,
    Guid SwitchId,
    string Reason,
    long ExpectedVersion,
    Guid ActorUserId,
    string CorrelationId);

public sealed record TransitionAccountingProviderSwitchCommand(
    Guid CompanyId,
    Guid SwitchId,
    string NextStatus,
    long ExpectedVersion,
    Guid ActorUserId,
    string CorrelationId);

public sealed record BlockAccountingProviderSwitchCommand(
    Guid CompanyId,
    Guid SwitchId,
    string FailureCode,
    string FailureSummary,
    long ExpectedVersion,
    Guid ActorUserId,
    string CorrelationId);

public sealed record GetAccountingProviderSwitchQuery(Guid CompanyId, Guid SwitchId);
public sealed record ListAccountingProviderSwitchesQuery(Guid CompanyId, string? Status = null, int Limit = 50);
public sealed record GetAccountingProviderSwitchAllowedActionsQuery(Guid CompanyId, Guid SwitchId);

public interface IAccountingProviderSwitchService
{
    Task<AccountingProviderSwitchDto> CreateAsync(CreateAccountingProviderSwitchCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchDto> GetAsync(GetAccountingProviderSwitchQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingProviderSwitchDto>> ListAsync(ListAccountingProviderSwitchesQuery query, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchDto> UpdatePlanAsync(UpdateAccountingProviderSwitchPlanCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchDto> CancelAsync(CancelAccountingProviderSwitchCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchAllowedActionsDto> GetAllowedActionsAsync(GetAccountingProviderSwitchAllowedActionsQuery query, CancellationToken cancellationToken);

    // These application-owned operations are intentionally not exposed as general-purpose transport endpoints.
    // Later assessment, approval, execution, and recovery workflows use them after their own policy checks.
    Task<AccountingProviderSwitchDto> TransitionAsync(TransitionAccountingProviderSwitchCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchDto> BlockAsync(BlockAccountingProviderSwitchCommand command, CancellationToken cancellationToken);
}
