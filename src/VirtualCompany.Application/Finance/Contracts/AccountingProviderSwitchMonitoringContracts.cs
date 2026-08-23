namespace VirtualCompany.Application.Finance;

public static class AccountingProviderSwitchMonitoringReasonCodes
{
    public const string NotFound = "provider_switch_monitoring_not_found";
    public const string InvalidState = "provider_switch_monitoring_invalid_state";
    public const string ConcurrencyConflict = "provider_switch_monitoring_concurrency_conflict";
    public const string WindowIncomplete = "provider_switch_monitoring_window_incomplete";
    public const string FinalCheckRequired = "provider_switch_monitoring_final_check_required";
    public const string CheckPending = "provider_switch_monitoring_check_pending";
    public const string BlockingIncident = "provider_switch_monitoring_blocking_incident";
    public const string ApprovalRequired = "provider_switch_monitoring_closure_approval_required";
    public const string ApprovalStale = "provider_switch_monitoring_closure_approval_stale";
    public const string RetryExhausted = "provider_switch_monitoring_retry_exhausted";
    public const string CheckFailed = "provider_switch_monitoring_check_failed";
    public const string CorrectiveCutoverUnavailable = "provider_switch_monitoring_corrective_cutover_unavailable";
}

public static class AccountingProviderSwitchMonitoringCheckKeys
{
    public const string ProviderSyncHealth = "provider_sync_health";
    public const string ProjectionIntegrity = "projection_integrity";
    public const string InvoiceCompleteness = "invoice_completeness";
    public const string MappingIntegrity = "mapping_integrity";
    public const string ConnectionAndScopes = "connection_and_scopes";
    public const string BankReconciliation = "bank_reconciliation";
    public const string FormerAuthorityPostingAttempts = "former_authority_posting_attempts";
    public const string FinancialControls = "financial_controls";
    public const string ExternalOutcomes = "external_outcomes";
    public const string ArchiveAvailability = "archive_availability";
}

public sealed record GetAccountingProviderSwitchMonitoringQuery(Guid CompanyId, Guid SwitchId);
public sealed record GetAccountingProviderSwitchOperationsQuery(Guid CompanyId);
public sealed record RunAccountingProviderSwitchMonitoringCommand(Guid CompanyId, Guid SwitchId,
    long ExpectedVersion, Guid ActorUserId, string CorrelationId);
public sealed record RetryAccountingProviderSwitchMonitoringCommand(Guid CompanyId, Guid SwitchId,
    long ExpectedVersion, Guid ActorUserId, string CorrelationId);
public sealed record AcceptAccountingProviderSwitchMonitoringExceptionCommand(Guid CompanyId, Guid SwitchId,
    Guid IncidentId, long ExpectedIncidentVersion, string Explanation, string Scope, decimal FinancialImpact,
    string EvidenceReference, Guid ActorUserId, string CorrelationId);
public sealed record RequestAccountingProviderSwitchMonitoringClosureCommand(Guid CompanyId, Guid SwitchId,
    long ExpectedVersion, Guid ActorUserId, string CorrelationId);
public sealed record CloseAccountingProviderSwitchMonitoringCommand(Guid CompanyId, Guid SwitchId,
    long ExpectedVersion, Guid ActorUserId, string Summary, string CorrelationId);
public sealed record CreateCorrectiveAccountingProviderSwitchCommand(Guid CompanyId, Guid SwitchId,
    Guid EffectiveFiscalPeriodId, long ExpectedVersion, Guid ActorUserId, string Reason, string CorrelationId);

public sealed record AccountingProviderSwitchMonitoringCheckDto(string CheckKey, string Status, string Severity,
    bool IsBlocking, string ReasonCode, string Explanation, string EvidenceJson, DateTime ObservedUtc);
public sealed record AccountingProviderSwitchMonitoringIncidentDto(Guid Id, string CheckKey, string Severity,
    bool IsBlocking, string Explanation, string Status, Guid? TaskId, int OccurrenceCount,
    DateTime FirstObservedUtc, DateTime LastObservedUtc, Guid? AcceptedByUserId, string? ExceptionExplanation,
    string? ExceptionScope, decimal? FinancialImpact, string? EvidenceReference, long Version);
public sealed record AccountingProviderSwitchMonitoringAllowedActionsDto(bool CanRunNow, bool CanRetry,
    bool CanReconnectAccess, bool CanReconcileProviderOutcome, bool CanRequestClosure, bool CanClose,
    bool CanCreateCorrectiveCutover, string Explanation);
public sealed record AccountingProviderSwitchMonitoringDto(Guid Id, Guid CompanyId, Guid SwitchId,
    Guid ActivationExecutionId, int WindowDays, Guid AssignedOwnerUserId, Guid? AssignedOwnerAgentId,
    string Status, int CheckSequence, int AttemptCount, int ConsecutiveFailureCount, DateTime StartedUtc,
    DateTime WindowEndsUtc, DateTime? LastSuccessfulCheckUtc, DateTime? NextRunUtc, string? FailureCode,
    string? FailureSummary, Guid? ClosureApprovalRequestId, Guid? CorrectiveSwitchId, DateTime? ClosedUtc,
    long Version, IReadOnlyList<AccountingProviderSwitchMonitoringCheckDto> Checks,
    IReadOnlyList<AccountingProviderSwitchMonitoringIncidentDto> Incidents,
    AccountingProviderSwitchMonitoringAllowedActionsDto AllowedActions);

public sealed record AccountingProviderSwitchOperationIssueDto(string Category, string Severity, long Count,
    string Explanation, string NextAction);
public sealed record AccountingProviderSwitchOperationsDto(Guid CompanyId, DateTime CalculatedUtc,
    long StuckWorkflows, long ExpiredApprovals, long StaleFreezes, long ExhaustedRetries,
    long AmbiguousOutcomes, long UnreconciledTotals, IReadOnlyList<AccountingProviderSwitchOperationIssueDto> Issues);

public interface IAccountingProviderSwitchMonitoringService
{
    Task<AccountingProviderSwitchMonitoringDto> GetAsync(GetAccountingProviderSwitchMonitoringQuery query, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchOperationsDto> GetOperationsAsync(GetAccountingProviderSwitchOperationsQuery query, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchMonitoringDto> RunNowAsync(RunAccountingProviderSwitchMonitoringCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchMonitoringDto> RetryAsync(RetryAccountingProviderSwitchMonitoringCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchMonitoringDto> AcceptExceptionAsync(AcceptAccountingProviderSwitchMonitoringExceptionCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchMonitoringDto> RequestClosureAsync(RequestAccountingProviderSwitchMonitoringClosureCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchMonitoringDto> CloseAsync(CloseAccountingProviderSwitchMonitoringCommand command, CancellationToken cancellationToken);
    Task<AccountingProviderSwitchMonitoringDto> CreateCorrectiveCutoverAsync(CreateCorrectiveAccountingProviderSwitchCommand command, CancellationToken cancellationToken);
}

public interface IAccountingProviderSwitchMonitoringJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}
