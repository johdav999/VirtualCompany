namespace VirtualCompany.Application.Finance;

public static class FinanceAutonomyEventTypes
{
    public const string NewUncategorizedTransaction = "new_uncategorized_transaction";
    public const string OverdueReceivable = "overdue_receivable";
    public const string StaleCashEvidence = "stale_cash_evidence";
    public const string CloseTaskBlockerChanged = "close_task_blocker_changed";
    public const string ReconciliationFailed = "reconciliation_failed";
    public const string ImportFailed = "import_failed";
    public const string ComplianceObligationExpiring = "compliance_obligation_expiring";
    public const string BackgroundWorkCompleted = "background_work_completed";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        NewUncategorizedTransaction, OverdueReceivable, StaleCashEvidence, CloseTaskBlockerChanged,
        ReconciliationFailed, ImportFailed, ComplianceObligationExpiring, BackgroundWorkCompleted
    };
}

public static class FinanceAutonomyCatchUpBehaviors
{
    public const string Skip = "skip";
    public const string Latest = "latest";
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal) { Skip, Latest };
}

public static class FinanceAutonomyTriggerReasonCodes
{
    public const string Processed = "finance_autonomy_trigger_processed";
    public const string Coalesced = "finance_autonomy_trigger_coalesced";
    public const string Duplicate = "finance_autonomy_trigger_duplicate";
    public const string GrantUnavailable = "finance_autonomy_trigger_grant_unavailable";
    public const string MinimumInterval = "finance_autonomy_trigger_minimum_interval";
    public const string WindowLimit = "finance_autonomy_trigger_window_limit";
    public const string LateEvent = "finance_autonomy_trigger_late_event";
    public const string UnsupportedEvent = "finance_autonomy_trigger_event_unsupported";
    public const string LeaseUnavailable = "finance_autonomy_trigger_lease_unavailable";
    public const string ProcessingFailed = "finance_autonomy_trigger_processing_failed";
    public const string DeadLettered = "finance_autonomy_trigger_dead_lettered";
    public const string Retried = "finance_autonomy_trigger_retried";
}

public sealed record FinanceAutonomyEventSignal(
    Guid CompanyId,
    string EventType,
    string SourceEventId,
    string SourceEventVersion,
    string SourceEntityType,
    string SourceEntityId,
    DateTime OccurredUtc,
    DateTime EvidenceObservedUtc,
    string CoalescingKey,
    string ContentHash,
    string? SafeLabel,
    string CorrelationId,
    string? CapabilityId = null);

public sealed record FinanceAutonomyTriggerProcessResult(
    bool Accepted, bool Duplicate, bool Coalesced, Guid? RunId, string ReasonCode, string SafeSummary);

public sealed record FinanceAutonomyTriggerBatchResult(
    int Considered, int Started, int Coalesced, int Suppressed, int Failed, int DeadLettered);

public sealed record FinanceAutonomyTriggerCursorDto(
    Guid Id, Guid CompanyId, Guid GrantId, Guid GrantVersionId, Guid AgentId, string CapabilityId,
    string TriggerKind, string TriggerKey, string Status, DateTime? CursorUtc, string? LastEventVersion,
    DateTime? CurrentWindowStartUtc, DateTime? CurrentWindowEndUtc, int RunsInWindow,
    Guid? LastRunId, DateTime? LastRunUtc, DateTime? NextEligibleUtc, int AttemptCount,
    string? LeaseOwner, DateTime? LeaseExpiresUtc, string? FailureCode, string? FailureSummary,
    DateTime CreatedUtc, DateTime UpdatedUtc, long Version);

public sealed record FinanceAutonomyTriggerEventDto(
    Guid Id, Guid CursorId, string EventType, string SourceEventId, string SourceEventVersion,
    string SourceEntityType, string SourceEntityId, DateTime OccurredUtc, DateTime EvidenceObservedUtc,
    string CoalescingKey, string ContentHash, string? SafeLabel, string CorrelationId, string Status,
    Guid? RunId, string? FailureCode, string? FailureSummary, DateTime CreatedUtc, DateTime? ProcessedUtc);

public sealed record FinanceAutonomyTriggerQueryResult(
    IReadOnlyList<FinanceAutonomyTriggerCursorDto> Cursors,
    IReadOnlyList<FinanceAutonomyTriggerEventDto> Events);

public interface IFinanceAutonomyTriggerService
{
    Task<FinanceAutonomyTriggerBatchResult> ProcessDueSchedulesAsync(DateTime utcNow, string workerId,
        int batchSize, CancellationToken cancellationToken);
    Task<FinanceAutonomyTriggerProcessResult> ProcessEventAsync(FinanceAutonomyEventSignal signal,
        string workerId, CancellationToken cancellationToken);
    Task<FinanceAutonomyTriggerQueryResult> GetOperationalStateAsync(Guid companyId, int take,
        CancellationToken cancellationToken);
    Task<FinanceAutonomyTriggerCursorDto> RetryDeadLetterAsync(Guid companyId, Guid cursorId,
        long expectedVersion, CancellationToken cancellationToken);
}
