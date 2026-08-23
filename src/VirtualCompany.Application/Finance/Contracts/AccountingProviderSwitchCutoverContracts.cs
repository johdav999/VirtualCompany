namespace VirtualCompany.Application.Finance;

public static class AccountingProviderSwitchCutoverReasonCodes
{
    public const string NotFound = "provider_switch_cutover_not_found";
    public const string NotReady = "provider_switch_cutover_not_ready";
    public const string InvalidState = "provider_switch_cutover_invalid_state";
    public const string ConcurrencyConflict = "provider_switch_cutover_concurrency_conflict";
    public const string PlanStale = "provider_switch_cutover_plan_stale";
    public const string SourceChanged = "provider_switch_cutover_source_changed";
    public const string SourceAuthorityChanged = "provider_switch_cutover_source_authority_changed";
    public const string BoundaryNotReached = "provider_switch_cutover_boundary_not_reached";
    public const string ConnectionUnhealthy = "provider_switch_cutover_connection_unhealthy";
    public const string PendingWrites = "provider_switch_cutover_pending_writes";
    public const string TransferIncomplete = "provider_switch_cutover_transfer_incomplete";
    public const string ProviderReconciliationRequired = "provider_switch_cutover_provider_reconciliation_required";
    public const string FinalReconciliationFailed = "provider_switch_cutover_final_reconciliation_failed";
    public const string ActivationApprovalRequired = "provider_switch_activation_approval_required";
    public const string ActivationApprovalStale = "provider_switch_activation_approval_stale";
    public const string ManualReadinessUnsupported = "provider_switch_manual_readiness_unsupported";
    public const string RecoveryUnsafe = "provider_switch_recovery_requires_corrective_cutover";
}

public sealed record ScheduleAccountingProviderSwitchCutoverCommand(Guid CompanyId, Guid SwitchId,
    Guid PlanId, long ExpectedSwitchVersion, Guid ActorUserId, string IdempotencyKey, string CorrelationId);
public sealed record StartAccountingProviderSwitchFreezeCommand(Guid CompanyId, Guid SwitchId,
    Guid ExecutionId, long ExpectedExecutionVersion, Guid ActorUserId, string CorrelationId);
public sealed record RequestAccountingProviderSwitchActivationApprovalCommand(Guid CompanyId, Guid SwitchId,
    Guid ExecutionId, long ExpectedExecutionVersion, Guid ActorUserId, string CorrelationId);
public sealed record ActivateAccountingProviderSwitchCommand(Guid CompanyId, Guid SwitchId,
    Guid ExecutionId, long ExpectedExecutionVersion, Guid ActorUserId, string CorrelationId);
public sealed record CancelAccountingProviderSwitchCutoverCommand(Guid CompanyId, Guid SwitchId,
    Guid ExecutionId, string Reason, long ExpectedExecutionVersion, Guid ActorUserId, string CorrelationId);
public sealed record RecoverAccountingProviderSwitchCutoverCommand(Guid CompanyId, Guid SwitchId,
    Guid ExecutionId, string Reason, long ExpectedExecutionVersion, Guid ActorUserId, string CorrelationId);
public sealed record ResumeAccountingProviderSwitchCutoverCommand(Guid CompanyId, Guid SwitchId,
    Guid ExecutionId, long ExpectedExecutionVersion, Guid ActorUserId, string CorrelationId);
public sealed record GetAccountingProviderSwitchCutoverQuery(Guid CompanyId, Guid SwitchId, Guid? ExecutionId = null);

public sealed record AccountingProviderSwitchFinalSnapshotDto(Guid Id, string ApprovedSourceSnapshotHash,
    string FinalSourceSnapshotHash, long RecordCount, decimal FinancialTotal, long DeltaRecordCount,
    decimal DeltaFinancialTotal, DateTime ExtractionStartedUtc, DateTime ExtractionCompletedUtc);
public sealed record AccountingProviderSwitchFinalCheckDto(Guid Id, string CheckKey, string Result,
    string ReasonCode, string Explanation, string EvidenceJson, DateTime CalculatedUtc);
public sealed record AccountingProviderSwitchActivationApprovalDto(Guid ApprovalRequestId, string Status,
    string FinalSnapshotHash, string ReconciliationHash, long SwitchVersion, DateTime RequestedUtc);
public sealed record AccountingProviderSwitchCutoverAllowedActionsDto(bool CanStartFreeze,
    bool CanRequestActivationApproval, bool CanActivate, bool CanCancel, bool CanRetry,
    bool CanRecoverSource, bool RequiresProviderReconciliation, bool RequiresCorrectiveCutover);
public sealed record AccountingProviderSwitchCutoverDto(Guid Id, Guid CompanyId, Guid SwitchId, Guid PlanId,
    int PlanVersion, string PlanHash, Guid? PreparationId, Guid? TargetTransferBatchId,
    Guid? AuthorityPeriodId, string Status, string CurrentStep, bool TargetActivityRecorded,
    bool RetryIsSafe, bool ProviderReconciliationRequired, string? FailureCode, string? FailureSummary,
    string? NextAction, int AttemptCount, DateTime? NextAttemptUtc, DateTime ScheduledUtc,
    DateTime RequestedUtc, DateTime? FreezeStartedUtc, DateTime? ReconciledUtc, DateTime? ActivatedUtc,
    DateTime? CompletedUtc, long Version, AccountingProviderSwitchFinalSnapshotDto? FinalSnapshot,
    IReadOnlyList<AccountingProviderSwitchFinalCheckDto> Checks,
    AccountingProviderSwitchActivationApprovalDto? ActivationApproval,
    AccountingProviderSwitchCutoverAllowedActionsDto AllowedActions);

public sealed record AccountingProviderSwitchFinalTransferExecutionResult(bool Succeeded, bool IsAmbiguous,
    bool IsRetryable, string? ProviderExternalId, string SafeSummary);

public interface IAccountingProviderSwitchFinalTransferExecutor
{
    string ProviderKey { get; }
    Task<AccountingProviderSwitchFinalTransferExecutionResult> ExecuteApprovedAsync(Guid companyId,
        Guid writeRequestId, CancellationToken cancellationToken);
}

public interface IAccountingProviderSwitchCutoverPolicy
{
    AccountingProviderSwitchCutoverAllowedActionsDto AllowedActions(string status, bool targetActivityRecorded,
        bool retryIsSafe, bool providerReconciliationRequired, bool hasApprovedActivation);
}

public interface IAccountingProviderSwitchCutoverService
{
    Task<AccountingProviderSwitchCutoverDto> ScheduleAsync(ScheduleAccountingProviderSwitchCutoverCommand command,
        CancellationToken cancellationToken);
    Task<AccountingProviderSwitchCutoverDto> StartFreezeAsync(StartAccountingProviderSwitchFreezeCommand command,
        CancellationToken cancellationToken);
    Task<AccountingProviderSwitchCutoverDto> GetAsync(GetAccountingProviderSwitchCutoverQuery query,
        CancellationToken cancellationToken);
    Task<AccountingProviderSwitchCutoverDto> RequestActivationApprovalAsync(
        RequestAccountingProviderSwitchActivationApprovalCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchCutoverDto> ActivateAsync(ActivateAccountingProviderSwitchCommand command,
        CancellationToken cancellationToken);
    Task<AccountingProviderSwitchCutoverDto> CancelAsync(CancelAccountingProviderSwitchCutoverCommand command,
        CancellationToken cancellationToken);
    Task<AccountingProviderSwitchCutoverDto> RecoverAsync(RecoverAccountingProviderSwitchCutoverCommand command,
        CancellationToken cancellationToken);
    Task<AccountingProviderSwitchCutoverDto> ResumeAsync(ResumeAccountingProviderSwitchCutoverCommand command,
        CancellationToken cancellationToken);
}

public interface IAccountingProviderSwitchCutoverJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}
