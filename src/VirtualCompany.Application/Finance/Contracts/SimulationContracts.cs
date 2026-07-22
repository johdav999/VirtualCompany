using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

public sealed record ReportingPeriodRegenerationRequestResultDto(
    Guid CompanyId,
    Guid FiscalPeriodId,
    bool Queued,
    Guid? BackgroundExecutionId,
    int SnapshotCount,
    string Status,
    DateTime RequestedAtUtc,
    DateTime? CompletedAtUtc,
    string ActorType,
    Guid? ActorId,
    ReportingPeriodLockStateDto LockState);

public sealed record GetFinanceSeedAnomaliesQuery(
    Guid CompanyId,
    string? AnomalyType = null,
    Guid? AffectedRecordId = null,
    int Limit = 100);

public sealed record GetFinanceSeedAnomalyByIdQuery(
    Guid CompanyId,
    Guid AnomalyId);

public sealed record RerunFinanceBootstrapCommand(
    Guid CompanyId,
    bool RerunPlanningBackfill = true,
    bool RerunApprovalBackfill = true,
    int BatchSize = 250,
    string? CorrelationId = null);

public sealed record FinanceBootstrapRerunResultDto(
    Guid CompanyId,
    string CorrelationId,
    FinanceSeedingState SeedState,
    bool PlanningBackfillRan,
    bool ApprovalBackfillRan,
    int PlanningRowsInserted,
    FinanceApprovalTaskBackfillResultDto ApprovalBackfill,
    DateTime CompletedAtUtc,
    string Summary);

public sealed record FinanceBootstrapRerunRequestDto(
    bool RerunPlanningBackfill = true,
    bool RerunApprovalBackfill = true,
    int BatchSize = 250,
    string? CorrelationId = null);

public sealed record FinanceSeedBootstrapCommand(
    Guid CompanyId,
    int SeedValue,
    DateTime? SeedAnchorUtc = null,
    bool ReplaceExisting = true,
    bool InjectAnomalies = false,
    string? AnomalyScenarioProfile = null);

public sealed record GetCompanySimulationClockQuery(
    Guid CompanyId);

public sealed record AdvanceCompanySimulationTimeCommand(
    Guid CompanyId,
    int TotalHours,
    int? ExecutionStepHours = null,
    bool Accelerated = false);

public sealed record RunScheduledCompanySimulationCommand(
    Guid CompanyId);

public sealed record CompanySimulationClockDto(
    Guid CompanyId,
    DateTime CurrentUtc,
    bool Enabled,
    bool AutoAdvanceEnabled,
    int DefaultStepHours,
    int AutoAdvanceIntervalSeconds,
    DateTime? LastAdvancedUtc);

public sealed record GenerateCompanySimulationFinanceCommand(
    Guid CompanyId,
    Guid ActiveSessionId,
    DateTime StartSimulatedUtc,
    DateTime PreviousSimulatedUtc,
    DateTime CurrentSimulatedUtc,
    int Seed,
    string? DeterministicConfigurationJson);

public sealed record CompanySimulationFinanceGenerationDayLogDto(
    DateTime SimulatedDateUtc,
    int TransactionsCreated,
    int InvoicesCreated,
    int BillsCreated,
    int AssetPurchasesCreated,
    int RecurringExpenseInstancesCreated,
    int AlertsCreated,
    IReadOnlyList<string> InjectedAnomalies,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public int GeneratedRecordCount =>
        TransactionsCreated + InvoicesCreated + BillsCreated + AssetPurchasesCreated + RecurringExpenseInstancesCreated;
}

public sealed record CompanySimulationFinanceGenerationResultDto(
    Guid CompanyId,
    Guid ActiveSessionId,
    int DaysProcessed,
    int InvoicesCreated,
    int BillsCreated,
    int TransactionsCreated,
    int BalancesCreated,
    int AssetPurchasesCreated,
    int RecurringExpenseInstancesCreated,
    int WorkflowTasksCreated,
    int ApprovalRequestsCreated,
    int AuditEventsCreated,
    int ActivityEventsCreated,
    int AlertsCreated,
    IReadOnlyList<CompanySimulationFinanceGenerationDayLogDto>? DailyLogs = null);

public sealed record FinanceDeterministicGenerationContext(
    Guid CompanyId,
    int Seed,
    DateTime StartSimulatedUtc,
    DateTime SimulatedDateUtc,
    int DayIndex,
    string? DeterministicConfigurationJson);

public interface IFinanceGenerationPolicy
{
    Task<CompanySimulationFinanceGenerationResultDto> GenerateAsync(
        GenerateCompanySimulationFinanceCommand command,
        CancellationToken cancellationToken);
}

public sealed record StartCompanySimulationStateCommand(
    Guid CompanyId,
    DateTime StartSimulatedUtc,
    bool GenerationEnabled,
    int Seed,
    string? DeterministicConfigurationJson = null,
    Guid? SessionId = null,
    DateTime? TransitionedUtc = null);

public sealed record SaveCompanySimulationStoppedDraftCommand(
    Guid CompanyId,
    DateTime ReferenceSimulatedUtc,
    bool GenerationEnabled,
    int Seed,
    string? DeterministicConfigurationJson = null,
    DateTime? UpdatedUtc = null);

public sealed record UpdateCompanySimulationStateCommand(
    Guid CompanyId,
    DateTime CurrentSimulatedUtc,
    DateTime? LastProgressedUtc = null,
    bool? GenerationEnabled = null,
    string? DeterministicConfigurationJson = null,
    DateTime? UpdatedUtc = null,
    DateTime? ExpectedCurrentSimulatedUtc = null,
    DateTime? ExpectedLastProgressedUtc = null);

public sealed record ProgressCompanySimulationStateResult(
    CompanySimulationState? State,
    bool Applied);

public sealed record PauseCompanySimulationStateCommand(
    Guid CompanyId,
    DateTime? PausedUtc = null);

public sealed record ResumeCompanySimulationStateCommand(
    Guid CompanyId,
    DateTime? ResumedUtc = null);

public sealed record StopCompanySimulationStateCommand(
    Guid CompanyId,
    DateTime? StoppedUtc = null);

public interface ICompanySimulationStateRepository
{
    Task<CompanySimulationState?> GetCurrentAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CompanySimulationState?> GetByActiveSessionAsync(Guid companyId, Guid activeSessionId, CancellationToken cancellationToken);
    Task<CompanySimulationState> StartAsync(StartCompanySimulationStateCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationState> SaveStoppedDraftAsync(SaveCompanySimulationStoppedDraftCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationState> UpdateAsync(UpdateCompanySimulationStateCommand command, CancellationToken cancellationToken);
    Task<ProgressCompanySimulationStateResult> TryProgressAsync(UpdateCompanySimulationStateCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationState> PauseAsync(PauseCompanySimulationStateCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationState> ResumeAsync(ResumeCompanySimulationStateCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationState> StopAsync(StopCompanySimulationStateCommand command, CancellationToken cancellationToken);
}

public sealed record SimulationExecutionLogDto(
    Guid CompanyId,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    int TransactionsGenerated,
    int InvoicesGenerated,
    int BillsGenerated,
    int RecurringExpenseInstancesGenerated,
    int EventsEmitted);

public sealed record AdvanceCompanySimulationTimeResultDto(
    Guid CompanyId,
    DateTime PreviousUtc,
    DateTime CurrentUtc,
    int TotalHoursProcessed,
    int ExecutionStepHours,
    int TransactionsGenerated,
    int InvoicesGenerated,
    int BillsGenerated,
    int RecurringExpenseInstancesGenerated,
    int EventsEmitted,
    IReadOnlyList<SimulationExecutionLogDto> Logs);

public sealed record FinanceSeedBootstrapResultDto(
    Guid CompanyId,
    int SeedValue,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    int AccountCount,
    int CounterpartyCount,
    int SupplierCount,
    int CategoryCount,
    int InvoiceCount,
    int BillCount,
    int RecurringExpenseCount,
    int TransactionCount,
    int BalanceCount,
    int PaymentCount,
    int DocumentCount,
    Guid PolicyConfigurationId,
    IReadOnlyList<FinanceSeedRecurringExpenseDto> RecurringExpenses,
    IReadOnlyList<FinanceSeedValidationErrorDto> ValidationErrors,
    IReadOnlyList<FinanceSeedAnomalyDto> Anomalies);

public sealed record FinanceSeedAnomalyDto(
    Guid Id,
    string AnomalyType,
    string ScenarioProfile,
    IReadOnlyList<Guid> AffectedRecordIds,
    string ExpectedDetectionMetadataJson);

public sealed record FinanceSeedRecurringExpenseDto(
    Guid Id,
    Guid SupplierId,
    string CategoryId,
    string Name,
    decimal Amount,
    string Currency,
    string Cadence,
    int DayOfPeriod);

public sealed record FinanceSeedValidationErrorDto(
    string Code,
    string Message);

public sealed record FinanceSeedingStateDiagnosticsDto(
    bool MetadataPresent,
    FinanceSeedingState? PersistedState,
    FinanceSeedingState? MetadataState,
    bool MetadataIndicatesComplete,
    bool UsedFastPath,
    string Reason,
    bool HasAccounts,
    bool HasCounterparties,
    bool HasTransactions,
    bool HasBalances,
    bool HasPolicyConfiguration,
    bool HasInvoices,
    bool HasBills);

public static class FinanceSeedingStateDerivedFromValues
{
    public const string Metadata = "metadata";
    public const string RecordChecks = "record_checks";
}

public sealed record FinanceSeedingStateResultDto(
    Guid CompanyId,
    FinanceSeedingState State,
    string DerivedFrom,
    DateTime CheckedAtUtc,
    FinanceSeedingStateDiagnosticsDto Diagnostics);

public static class FinanceSeedTelemetryEventNames
{
    public const string Requested = "requested";
    public const string Started = "started";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed record GetFinanceSeedBackfillRunsQuery(
    int Limit = 20);

public sealed record FinanceSeedBackfillRunDto(
    Guid RunId,
    FinanceSeedBackfillRunStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    int ScannedCount,
    int QueuedCount,
    int SucceededCount,
    int SkippedCount,
    int FailedCount,
    string ConfigurationSnapshotJson,
    string? ErrorDetails);

public sealed record FinanceSeedBackfillAttemptDto(
    Guid AttemptId,
    Guid RunId,
    Guid CompanyId,
    FinanceSeedBackfillAttemptStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? SkipReason,
    string? ErrorCode,
    string? ErrorDetails,
    int RetryCount,
    Guid? BackgroundExecutionId,
    string? IdempotencyKey,
    FinanceSeedingState SeedStateBefore,
    FinanceSeedingState? SeedStateAfter);

public interface IFinanceSeedBootstrapService
{
    Task<FinanceSeedBootstrapResultDto> GenerateAsync(FinanceSeedBootstrapCommand command, CancellationToken cancellationToken);
}

public interface IFinanceSeedingStateResolver
{
    Task<FinanceSeedingStateResultDto> ResolveAsync(Guid companyId, CancellationToken cancellationToken = default);
}

public interface IFinanceSeedingStateService
{
    Task<FinanceSeedingStateResultDto> GetCompanyFinanceSeedingStateAsync(Guid companyId, CancellationToken cancellationToken = default);
}

public interface IFinanceSeedBackfillOrchestrator
{
    Task<FinanceSeedBackfillRunDto> RunAsync(CancellationToken cancellationToken);
}

public interface IFinanceSeedBackfillQueryService
{
    Task<IReadOnlyList<FinanceSeedBackfillRunDto>> GetRecentRunsAsync(GetFinanceSeedBackfillRunsQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinanceSeedBackfillAttemptDto>> GetAttemptsAsync(Guid runId, CancellationToken cancellationToken);
}

public interface IReportingPeriodRegenerationJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public sealed record FinanceSeedTelemetryContext(
    Guid CompanyId,
    Guid JobId,
    string? CorrelationId,
    string? IdempotencyKey,
    string TriggerSource,
    FinanceSeedingState SeedStateBefore,
    FinanceSeedingState SeedStateAfter,
    Guid? UserId,
    bool JobAlreadyRunning = false,
    int? Attempt = null,
    int? MaxAttempts = null,
    long? DurationMs = null,
    string? ErrorType = null,
    string? ErrorMessageSafe = null,
    string? SeedMode = null,
    string? ActorType = null,
    Guid? ActorId = null);

public interface IFinanceSeedTelemetry
{
    Task TrackAsync(string eventName, FinanceSeedTelemetryContext context, CancellationToken cancellationToken = default);
}

public interface IFinanceBootstrapRerunService
{
    Task<FinanceBootstrapRerunResultDto> RerunAsync(
        RerunFinanceBootstrapCommand command,
        CancellationToken cancellationToken);
}

public interface ICompanySimulationService
{
    Task<CompanySimulationClockDto> GetClockAsync(
        GetCompanySimulationClockQuery query,
        CancellationToken cancellationToken);

    Task<AdvanceCompanySimulationTimeResultDto> AdvanceAsync(
        AdvanceCompanySimulationTimeCommand command,
        CancellationToken cancellationToken);

    Task<AdvanceCompanySimulationTimeResultDto?> RunScheduledAdvanceAsync(
        RunScheduledCompanySimulationCommand command,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}

public static class FinanceEntryInitializationStates
{
    public const string Ready = "ready";
    public const string Initializing = "initializing";
    public const string Failed = "failed";
}

public static class FinanceSeedRequestModes
{
    public const string Replace = "replace";
    public const string Append = "append";

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    public static bool IsSupported(string? value)
    {
        var normalized = Normalize(value);
        return string.Equals(normalized, Replace, StringComparison.Ordinal) ||
               string.Equals(normalized, Append, StringComparison.Ordinal);
    }
}

