namespace VirtualCompany.Application.Finance;

public static class CompanySimulationLifecycleStatusValues
{
    public const string NotStarted = "not_started";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Stopped = "stopped";
}

public sealed record GetCompanySimulationStateQuery(Guid CompanyId);

public sealed record StartCompanySimulationCommand(
    Guid CompanyId,
    DateTime StartSimulatedDateTime,
    bool GenerationEnabled,
    int Seed,
    string? DeterministicConfigurationJson = null);

public sealed record UpdateCompanySimulationSettingsCommand(
    Guid CompanyId,
    bool? GenerationEnabled = null,
    string? DeterministicConfigurationJson = null);

public sealed record PauseCompanySimulationCommand(Guid CompanyId);

public sealed record CompanySimulationStatusTransitionDto(
    string Status,
    DateTime TransitionedUtc,
    string? Message = null);

public sealed record CompanySimulationDayLogDto(
    DateTime SimulatedDateUtc,
    int TransactionsGenerated,
    int InvoicesGenerated,
    int BillsGenerated,
    int RecurringExpenseInstancesGenerated,
    int AlertsGenerated,
    IReadOnlyList<string> InjectedAnomalies,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public int GeneratedRecordCount =>
        TransactionsGenerated +
        InvoicesGenerated +
        BillsGenerated +
        RecurringExpenseInstancesGenerated;
}

public sealed record CompanySimulationRunHistoryDto(
    Guid SessionId,
    string Status,
    DateTime StartedUtc,
    DateTime? CompletedUtc,
    DateTime LastUpdatedUtc,
    bool GenerationEnabled,
    int Seed,
    DateTime StartSimulatedDateTime,
    DateTime? CurrentSimulatedDateTime,
    IReadOnlyList<string> InjectedAnomalies,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    IReadOnlyList<CompanySimulationStatusTransitionDto> StatusTransitions,
    IReadOnlyList<CompanySimulationDayLogDto> DayLogs);

public sealed record SimulationFeatureStateDto(
    bool UiVisible,
    bool BackendExecutionEnabled,
    bool BackgroundJobsEnabled,
    string DisabledMessage);

public sealed record ResumeCompanySimulationCommand(Guid CompanyId);

public sealed record StepForwardCompanySimulationOneDayCommand(Guid CompanyId);

public sealed record StopCompanySimulationCommand(Guid CompanyId);

public sealed record CompanySimulationStateDto(
    Guid CompanyId,
    string Status,
    DateTime? CurrentSimulatedDateTime,
    DateTime? LastProgressionTimestamp,
    bool? GenerationEnabled,
    int? Seed,
    Guid? ActiveSessionId,
    DateTime? StartSimulatedDateTime,
    string? DeterministicConfigurationJson,
    bool CanStart = false,
    bool CanPause = false,
    bool CanStop = false,
    bool CanToggleGeneration = false,
    bool UiVisible = true,
    bool BackendExecutionEnabled = true,
    bool BackgroundJobsEnabled = true,
    bool SupportsStepForwardOneDay = false,
    string? DisabledReason = null,
    bool SupportsRefresh = true,
    IReadOnlyList<CompanySimulationRunHistoryDto>? RecentHistory = null)
{
    public static CompanySimulationStateDto NotStarted(Guid companyId) =>
        new(
            companyId,
            CompanySimulationLifecycleStatusValues.NotStarted,
            null,
            null,
            true,
            null,
            null,
            null,
            null,
            CanStart: true,
            CanPause: false,
            CanStop: false,
            CanToggleGeneration: true,
            UiVisible: true,
            BackendExecutionEnabled: true,
            BackgroundJobsEnabled: true,
            SupportsStepForwardOneDay: true,
            SupportsRefresh: true,
            RecentHistory: []);

    public static CompanySimulationStateDto Disabled(
        Guid companyId,
        SimulationFeatureStateDto featureState,
        IReadOnlyList<CompanySimulationRunHistoryDto>? recentHistory = null) =>
        new(
            companyId,
            CompanySimulationLifecycleStatusValues.NotStarted,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            CanStart: false,
            CanPause: false,
            CanStop: false,
            CanToggleGeneration: false,
            UiVisible: featureState.UiVisible,
            BackendExecutionEnabled: featureState.BackendExecutionEnabled,
            BackgroundJobsEnabled: featureState.BackgroundJobsEnabled,
            SupportsStepForwardOneDay: false,
            DisabledReason: featureState.DisabledMessage,
            SupportsRefresh: true,
            RecentHistory: recentHistory ?? []);
}

public interface ICompanySimulationStateService
{
    Task<CompanySimulationStateDto> GetStateAsync(GetCompanySimulationStateQuery query, CancellationToken cancellationToken);
    Task<CompanySimulationStateDto> StartAsync(StartCompanySimulationCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationStateDto> UpdateSettingsAsync(UpdateCompanySimulationSettingsCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationStateDto> PauseAsync(PauseCompanySimulationCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationStateDto> ResumeAsync(ResumeCompanySimulationCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationStateDto> StepForwardOneDayAsync(StepForwardCompanySimulationOneDayCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationStateDto> StopAsync(StopCompanySimulationCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompanySimulationRunHistoryDto>> GetRecentHistoryAsync(Guid companyId, int limit, CancellationToken cancellationToken);
}

public interface ISimulationFeatureGate
{
    SimulationFeatureStateDto GetState();
    bool IsUiVisible();
    bool IsBackendExecutionEnabled();
    bool AreBackgroundJobsEnabled();
    bool IsBackgroundExecutionAllowed();
    bool IsFullyDisabled();
    void EnsureBackendExecutionEnabled();
    void EnsureBackgroundExecutionEnabled();
}

public sealed class SimulationBackendDisabledException : InvalidOperationException
{
    public SimulationBackendDisabledException(string message, bool isBackgroundExecution = false)
        : base(message)
    {
        IsBackgroundExecution = isBackgroundExecution;
    }

    public bool IsBackgroundExecution { get; }
}