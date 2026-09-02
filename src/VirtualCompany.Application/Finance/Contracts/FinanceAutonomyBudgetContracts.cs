namespace VirtualCompany.Application.Finance;

public static class FinanceAutonomyBudgetPolicyVersions
{
    public const string V1 = "finance-autonomy-budget-policy-v1";
}

public static class FinanceAutonomyBudgetReasonCodes
{
    public const string Reserved = "finance_autonomy_budget_reserved";
    public const string Reconciled = "finance_autonomy_budget_reconciled";
    public const string Released = "finance_autonomy_budget_released";
    public const string PerRunExceeded = "finance_autonomy_budget_per_run_exceeded";
    public const string WindowExceeded = "finance_autonomy_budget_window_exceeded";
    public const string CompanyBudgetMissing = "finance_autonomy_company_budget_missing";
    public const string EmergencyStopped = "finance_autonomy_budget_emergency_stopped";
    public const string CircuitOpen = "finance_autonomy_circuit_open";
    public const string CircuitReset = "finance_autonomy_circuit_reset";
}

public static class FinanceAutonomyCircuitSignals
{
    public const string PolicyDenial = "policy_denial";
    public const string InvalidPlan = "invalid_plan";
    public const string ProviderAmbiguity = "provider_ambiguity";
    public const string Error = "error";
    public const string StaleEvidence = "stale_evidence";
    public const string AuditOutboxFailure = "audit_outbox_failure";
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    { PolicyDenial, InvalidPlan, ProviderAmbiguity, Error, StaleEvidence, AuditOutboxFailure };
}

public sealed record FinanceAutonomyUsageDefinition(
    int RecordsEvaluated = 0,
    int DraftsOrTasksCreated = 0,
    int ExecuteAttempts = 0,
    decimal AmountExposure = 0,
    long ObjectBytes = 0,
    int ExportsCreated = 0,
    int ModelCalls = 0,
    int ToolCalls = 0,
    decimal EstimatedCost = 0,
    int Retries = 0,
    int RuntimeSeconds = 0);

public sealed record FinanceAutonomyUsageLimitDefinition(
    int? RecordsEvaluated = null,
    int? DraftsOrTasksCreated = null,
    int? ExecuteAttempts = null,
    decimal? AmountExposure = null,
    long? ObjectBytes = null,
    int? ExportsCreated = null,
    int? ModelCalls = null,
    int? ToolCalls = null,
    decimal? EstimatedCost = null,
    int? Retries = null,
    int? RuntimeSeconds = null);

public sealed record UpsertFinanceAutonomyBudgetPolicyCommand(
    Guid? PolicyId,
    Guid? AgentId,
    string? CapabilityId,
    string Timezone,
    int WindowMinutes,
    FinanceAutonomyUsageLimitDefinition PerRunLimits,
    FinanceAutonomyUsageLimitDefinition WindowLimits,
    int PolicyDenialThreshold = 3,
    int InvalidPlanThreshold = 3,
    int ProviderAmbiguityThreshold = 2,
    int ErrorBurstThreshold = 5,
    int StaleEvidenceThreshold = 3,
    int AuditOutboxFailureThreshold = 2,
    int CircuitWindowMinutes = 60,
    int CircuitCooldownMinutes = 60,
    bool IsActive = true,
    long ExpectedVersion = 0);

public sealed record FinanceAutonomyBudgetPolicyDto(
    Guid Id, Guid? AgentId, string? CapabilityId, string ScopeKey, string Timezone, int WindowMinutes,
    FinanceAutonomyUsageLimitDefinition PerRunLimits, FinanceAutonomyUsageLimitDefinition WindowLimits,
    int PolicyDenialThreshold, int InvalidPlanThreshold, int ProviderAmbiguityThreshold,
    int ErrorBurstThreshold, int StaleEvidenceThreshold, int AuditOutboxFailureThreshold,
    int CircuitWindowMinutes, int CircuitCooldownMinutes, bool IsActive, long Version, DateTime UpdatedUtc);

public sealed record FinanceAutonomyBudgetWindowDto(
    Guid Id, Guid PolicyId, string ScopeKey, DateTime WindowStartUtc, DateTime WindowEndUtc,
    FinanceAutonomyUsageDefinition Reserved, FinanceAutonomyUsageDefinition Consumed,
    FinanceAutonomyUsageLimitDefinition Limits, FinanceAutonomyUsageLimitDefinition Remaining, long Version);

public sealed record FinanceAutonomyBudgetReservationDto(
    Guid Id, Guid PolicyId, Guid WindowId, Guid RunId, Guid StepId, int AttemptNumber,
    string Status, FinanceAutonomyUsageDefinition Planned, FinanceAutonomyUsageDefinition Actual,
    DateTime CreatedUtc, DateTime? ReconciledUtc);

public sealed record FinanceAutonomyCircuitBreakerDto(
    Guid Id, Guid AgentId, string CapabilityId, string Status, DateTime WindowStartUtc, DateTime WindowEndUtc,
    int PolicyDenials, int InvalidPlans, int ProviderAmbiguities, int Errors, int StaleEvidence,
    int AuditOutboxFailures, string? OpenReasonCode, string? SafeSummary, DateTime? OpenedUtc,
    DateTime? CooldownUntilUtc, long Version);

public sealed record FinanceAutonomyBudgetAlertDto(
    Guid Id, Guid CircuitId, string ReasonCode, string SafeSummary, string Status,
    DateTime CreatedUtc, DateTime? ResolvedUtc);

public sealed record FinanceAutonomyBudgetQueryResult(
    IReadOnlyList<FinanceAutonomyBudgetPolicyDto> Policies,
    IReadOnlyList<FinanceAutonomyBudgetWindowDto> Windows,
    IReadOnlyList<FinanceAutonomyBudgetReservationDto> RecentReservations,
    IReadOnlyList<FinanceAutonomyCircuitBreakerDto> CircuitBreakers,
    IReadOnlyList<FinanceAutonomyBudgetAlertDto> Alerts,
    DateTime EvaluatedUtc);

public sealed record FinanceAutonomyBudgetReservationDecision(
    bool Allowed, string ReasonCode, string SafeSummary, IReadOnlyList<Guid> ReservationIds);

public sealed record RecordFinanceAutonomyCircuitSignalCommand(
    Guid AgentId, string CapabilityId, string SignalType, string CorrelationId, string? SafeSummary = null);

public interface IFinanceAutonomyBudgetService
{
    Task<FinanceAutonomyBudgetQueryResult> GetAsync(Guid companyId, int take, CancellationToken cancellationToken);
    Task<FinanceAutonomyBudgetPolicyDto> UpsertPolicyAsync(Guid companyId,
        UpsertFinanceAutonomyBudgetPolicyCommand command, CancellationToken cancellationToken);
    Task<FinanceAutonomyCircuitBreakerDto> ResetCircuitAsync(Guid companyId, Guid circuitId,
        long expectedVersion, CancellationToken cancellationToken);
    Task<FinanceAutonomyBudgetReservationDecision> ReserveForClaimAsync(Guid companyId,
        Guid runId, Guid stepId, int attemptNumber, FinanceAutonomyUsageDefinition planned,
        CancellationToken cancellationToken);
    Task ReconcileForAttemptAsync(Guid companyId, Guid runId, Guid stepId, int attemptNumber,
        FinanceAutonomyUsageDefinition actual, bool releaseOnly, CancellationToken cancellationToken);
    Task ReleaseForRunAsync(Guid companyId, Guid runId, CancellationToken cancellationToken);
    Task RecordCircuitSignalAsync(Guid companyId, RecordFinanceAutonomyCircuitSignalCommand command,
        CancellationToken cancellationToken);
}

public sealed class FinanceAutonomyBudgetValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Finance autonomy budget validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public sealed class FinanceAutonomyBudgetConcurrencyException(string message) : Exception(message);
