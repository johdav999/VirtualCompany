using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Finance;

public interface ICompanySimulationProgressionRunner
{
    Task<int> ProgressDueAsync(CancellationToken cancellationToken);
}

public sealed class CompanySimulationProgressionWorkerOptions
{
    public const string SectionName = "CompanySimulationProgressionWorker";

    public bool Enabled { get; set; } = true;
    public int PollIntervalMilliseconds { get; set; } = 1000;
    public int BatchSize { get; set; } = 100;
}

public sealed class CompanySimulationStateService : ICompanySimulationStateService
{
    internal const int ProgressionBucketSeconds = 10;
    private const string ProgressionLockPrefix = "company-simulation-progression";
    private static readonly TimeSpan ProgressionLockLease = TimeSpan.FromSeconds(30);

    private readonly ICompanySimulationStateRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly ILogger<CompanySimulationStateService> _logger;
    private readonly IDistributedLockProvider? _distributedLockProvider;
    private readonly IFinanceGenerationPolicy? _financeGenerationPolicy;
    private readonly ISimulationFeatureGate? _featureGate;

    public CompanySimulationStateService(
        ICompanySimulationStateRepository repository,
        TimeProvider timeProvider,
        ILogger<CompanySimulationStateService> logger)
        : this(repository, timeProvider, null, null, null, null, logger)
    {
    }

    public CompanySimulationStateService(
        ICompanySimulationStateRepository repository,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor,
        ILogger<CompanySimulationStateService> logger)
        : this(repository, timeProvider, companyContextAccessor, null, null, null, logger)
    {
    }

    public CompanySimulationStateService(
        ICompanySimulationStateRepository repository,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor,
        IDistributedLockProvider? distributedLockProvider,
        ILogger<CompanySimulationStateService> logger)
        : this(repository, timeProvider, companyContextAccessor, distributedLockProvider, null, null, logger)
    {
    }

    public CompanySimulationStateService(
        ICompanySimulationStateRepository repository,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor,
        IDistributedLockProvider? distributedLockProvider,
        IFinanceGenerationPolicy? financeGenerationPolicy,
        ISimulationFeatureGate? featureGate,
        ILogger<CompanySimulationStateService> logger)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _companyContextAccessor = companyContextAccessor;
        _distributedLockProvider = distributedLockProvider;
        _financeGenerationPolicy = financeGenerationPolicy;
        _logger = logger;
        _featureGate = featureGate;
    }

    public async Task<CompanySimulationStateDto> GetStateAsync(
        GetCompanySimulationStateQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var featureState = _featureGate?.GetState() ?? new SimulationFeatureStateDto(true, true, true, string.Empty);
        var history = await GetRecentHistoryAsync(query.CompanyId, 10, cancellationToken);
        if (!featureState.BackendExecutionEnabled)
        {
            return CompanySimulationStateDto.Disabled(query.CompanyId, featureState, history);
        }

        var state = await _repository.GetCurrentAsync(query.CompanyId, cancellationToken);
        if (state is null)
        {
            var emptyState = CompanySimulationStateDto.NotStarted(query.CompanyId) with
            { UiVisible = featureState.UiVisible, BackendExecutionEnabled = featureState.BackendExecutionEnabled, BackgroundJobsEnabled = featureState.BackgroundJobsEnabled, DisabledReason = featureState.BackendExecutionEnabled ? null : featureState.DisabledMessage, RecentHistory = history };
            return emptyState;
        }

        return Map(state, featureState, history);
    }

    public async Task<CompanySimulationStateDto> StartAsync(
        StartCompanySimulationCommand command,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Company simulation service starting session. CompanyId: {CompanyId}. StartSimulatedDateTime: {StartSimulatedDateTime}. GenerationEnabled: {GenerationEnabled}. Seed: {Seed}. DeterministicConfigurationJsonPresent: {HasDeterministicConfigurationJson}.",
            command.CompanyId,
            command.StartSimulatedDateTime,
            command.GenerationEnabled,
            command.Seed,
            !string.IsNullOrWhiteSpace(command.DeterministicConfigurationJson));

        EnsureTenant(command.CompanyId);
        EnsureSimulationExecutionEnabled(command.CompanyId, "start");
        if (command.StartSimulatedDateTime == default)
        {
            throw new ArgumentException("Start simulated date/time is required.", nameof(command.StartSimulatedDateTime));
        }

        var existing = await _repository.GetCurrentAsync(command.CompanyId, cancellationToken);
        if (existing is not null && (existing.IsRunning || existing.IsPaused))
        {
            throw new InvalidOperationException("A simulation session is already active. Pause, stop, or resume the current session instead.");
        }

        var transitionedUtc = GetUtcNow();
        var state = await _repository.StartAsync(
            new StartCompanySimulationStateCommand(
                command.CompanyId,
                NormalizeUtc(command.StartSimulatedDateTime),
                command.GenerationEnabled,
                command.Seed,
                command.DeterministicConfigurationJson,
                TransitionedUtc: transitionedUtc),
            cancellationToken);

        _logger.LogInformation(
            "Company simulation service started session. CompanyId: {CompanyId}. SessionId: {SessionId}. Status: {Status}. CurrentSimulatedUtc: {CurrentSimulatedUtc}. LastProgressedUtc: {LastProgressedUtc}. GenerationEnabled: {GenerationEnabled}. Seed: {Seed}.",
            state.CompanyId,
            state.ActiveSessionId,
            state.Status,
            state.CurrentSimulatedUtc,
            state.LastProgressedUtc,
            state.GenerationEnabled,
            state.Seed);

        return await MapWithHistoryAsync(state, cancellationToken);
    }

    public async Task<CompanySimulationStateDto> UpdateSettingsAsync(
        UpdateCompanySimulationSettingsCommand command,
        CancellationToken cancellationToken)
    {
        EnsureSimulationExecutionEnabled(command.CompanyId, "update_settings");
        EnsureTenant(command.CompanyId);
        if (!command.GenerationEnabled.HasValue && command.DeterministicConfigurationJson is null)
        {
            throw new ArgumentException("At least one simulation setting must be provided.", nameof(command));
        }

        var state = await _repository.GetCurrentAsync(command.CompanyId, cancellationToken);
        if (state is null)
        {
            var draft = await _repository.SaveStoppedDraftAsync(
                new SaveCompanySimulationStoppedDraftCommand(
                    command.CompanyId,
                    GetDefaultDraftSimulatedUtc(),
                    command.GenerationEnabled ?? true,
                    ResolveDefaultSeed(command.CompanyId),
                    command.DeterministicConfigurationJson,
                    GetUtcNow()),
                cancellationToken);
            return await MapWithHistoryAsync(draft, cancellationToken);
        }
        if (state.IsRunning)
        {
            state = (await ProgressStateIfDueAsync(command.CompanyId, cancellationToken)).State ?? state;
        }

        if (state.IsRunning &&
            command.GenerationEnabled.HasValue &&
            command.GenerationEnabled.Value != state.GenerationEnabled)
        {
            throw new InvalidOperationException("Pause or stop the simulation before changing finance generation.");
        }

        if (state.IsStopped)
        {
            state = await _repository.SaveStoppedDraftAsync(
                new SaveCompanySimulationStoppedDraftCommand(
                    command.CompanyId,
                    state.CurrentSimulatedUtc,
                    command.GenerationEnabled ?? state.GenerationEnabled,
                    state.Seed,
                    command.DeterministicConfigurationJson ?? state.DeterministicConfigurationJson,
                    GetUtcNow()),
                cancellationToken);
            return await MapWithHistoryAsync(state, cancellationToken);
        }

        var generationEnabled = state.IsRunning ? null : command.GenerationEnabled;
        state = await _repository.UpdateAsync(
            new UpdateCompanySimulationStateCommand(
                command.CompanyId,
                state.CurrentSimulatedUtc,
                state.LastProgressedUtc,
                generationEnabled,
                command.DeterministicConfigurationJson,
                GetUtcNow()),
            cancellationToken);

        return await MapWithHistoryAsync(state, cancellationToken);
    }

    public async Task<CompanySimulationStateDto> PauseAsync(
        PauseCompanySimulationCommand command,
        CancellationToken cancellationToken)
    {
        EnsureSimulationExecutionEnabled(command.CompanyId, "pause");
        EnsureTenant(command.CompanyId);
        var state = await RequireStateAsync(command.CompanyId, cancellationToken);
        if (!state.IsRunning)
        {
            throw new InvalidOperationException("Only running simulations can be paused.");
        }

        state = (await ProgressStateIfDueAsync(command.CompanyId, cancellationToken)).State ?? state;
        state = await _repository.PauseAsync(
            new PauseCompanySimulationStateCommand(command.CompanyId, GetUtcNow()),
            cancellationToken);

        return await MapWithHistoryAsync(state, cancellationToken);
    }

    public async Task<CompanySimulationStateDto> ResumeAsync(
        ResumeCompanySimulationCommand command,
        CancellationToken cancellationToken)
    {
        EnsureSimulationExecutionEnabled(command.CompanyId, "resume");
        EnsureTenant(command.CompanyId);
        var state = await RequireStateAsync(command.CompanyId, cancellationToken);
        if (!state.IsPaused)
        {
            throw new InvalidOperationException("Only paused simulations can be resumed.");
        }

        state = await _repository.ResumeAsync(
            new ResumeCompanySimulationStateCommand(command.CompanyId, GetUtcNow()),
            cancellationToken);

        return await MapWithHistoryAsync(state, cancellationToken);
    }

    public async Task<CompanySimulationStateDto> StepForwardOneDayAsync(
        StepForwardCompanySimulationOneDayCommand command,
        CancellationToken cancellationToken)
    {
        EnsureSimulationExecutionEnabled(command.CompanyId, "step_forward_one_day");
        EnsureTenant(command.CompanyId);
        var state = await RequireStateAsync(command.CompanyId, cancellationToken);
        if (!state.IsPaused)
        {
            throw new InvalidOperationException("Only paused simulations can be stepped forward one day.");
        }

        var observedUtc = GetUtcNow();
        var updated = await _repository.UpdateAsync(
            new UpdateCompanySimulationStateCommand(
                command.CompanyId,
                state.CurrentSimulatedUtc.AddDays(1),
                observedUtc,
                UpdatedUtc: observedUtc),
            cancellationToken);

        if (_financeGenerationPolicy is not null &&
            state.GenerationEnabled &&
            updated.ActiveSessionId.HasValue)
        {
            var generationResult = await _financeGenerationPolicy.GenerateAsync(
                new GenerateCompanySimulationFinanceCommand(
                    updated.CompanyId,
                    updated.ActiveSessionId.Value,
                    updated.StartSimulatedUtc,
                    state.CurrentSimulatedUtc,
                    updated.CurrentSimulatedUtc,
                    updated.Seed,
                    updated.DeterministicConfigurationJson),
                cancellationToken);

            if (_repository is EfCompanySimulationStateRepository historyRepository)
            {
                await historyRepository.RecordFinanceGenerationAsync(
                    updated.CompanyId,
                    updated.ActiveSessionId.Value,
                    generationResult,
                    updated.CurrentSimulatedUtc,
                    cancellationToken);
            }
        }

        return await MapWithHistoryAsync(updated, cancellationToken);
    }

    public async Task<CompanySimulationStateDto> StopAsync(
        StopCompanySimulationCommand command,
        CancellationToken cancellationToken)
    {
        EnsureSimulationExecutionEnabled(command.CompanyId, "stop");
        EnsureTenant(command.CompanyId);
        var state = await RequireStateAsync(command.CompanyId, cancellationToken);
        if (!state.IsRunning && !state.IsPaused)
        {
            throw new InvalidOperationException("Only running or paused simulations can be stopped.");
        }

        if (state.IsRunning)
        {
            state = (await ProgressStateIfDueAsync(command.CompanyId, cancellationToken)).State ?? state;
        }

        state = await _repository.StopAsync(
            new StopCompanySimulationStateCommand(command.CompanyId, GetUtcNow()),
            cancellationToken);

        return await MapWithHistoryAsync(state, cancellationToken);
    }

    public async Task<IReadOnlyList<CompanySimulationRunHistoryDto>> GetRecentHistoryAsync(Guid companyId, int limit, CancellationToken cancellationToken)
    {
        if (_repository is not EfCompanySimulationStateRepository historyRepository)
        {
            return [];
        }

        var histories = await historyRepository.GetRecentHistoryAsync(companyId, limit, cancellationToken);
        return histories.Select(MapHistory).ToArray();
    }

    private async Task<CompanySimulationStateDto> MapWithHistoryAsync(
        CompanySimulationState state,
        CancellationToken cancellationToken)
    {
        return Map(state, _featureGate?.GetState(), await GetRecentHistoryAsync(state.CompanyId, 10, cancellationToken));
    }

    internal async Task<bool> ProgressIfDueAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (_featureGate?.IsBackgroundExecutionAllowed() == false)
        {
            var featureState = _featureGate.GetState();
            _logger.LogInformation(
                "Skipping company simulation progression for company {CompanyId} because simulation execution is disabled. BackendExecutionEnabled={BackendExecutionEnabled}, BackgroundJobsEnabled={BackgroundJobsEnabled}.",
                companyId,
                featureState.BackendExecutionEnabled,
                featureState.BackgroundJobsEnabled);
            return false;
        }

        EnsureTenant(companyId);
        return (await ProgressStateIfDueAsync(companyId, cancellationToken)).Advanced;
    }

    private async Task<CompanySimulationState> RequireStateAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var state = await _repository.GetCurrentAsync(companyId, cancellationToken);
        return state ?? throw new KeyNotFoundException($"Simulation state for company '{companyId}' was not found.");
    }

    private async Task<SimulationProgressionResult> ProgressStateIfDueAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var lockProvider = _distributedLockProvider;
        if (lockProvider is null)
        {
            return await ProgressStateIfDueCoreAsync(companyId, cancellationToken);
        }

        await using var handle = await lockProvider.TryAcquireAsync(
            BuildProgressionLockKey(companyId),
            ProgressionLockLease,
            cancellationToken);

        if (handle is null)
        {
            return new SimulationProgressionResult(
                await _repository.GetCurrentAsync(companyId, cancellationToken),
                false);
        }

        return await ProgressStateIfDueCoreAsync(companyId, cancellationToken);
    }

    private async Task<SimulationProgressionResult> ProgressStateIfDueCoreAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var state = await _repository.GetCurrentAsync(companyId, cancellationToken);
        if (state is null || !state.IsRunning)
        {
            return new SimulationProgressionResult(state, false);
        }

        var computation = TryCalculateProgression(state, GetUtcNow());
        if (computation is null)
        {
            return new SimulationProgressionResult(state, false);
        }

        _logger.LogDebug(
            "Progressing simulation state for company {CompanyId}, session {SessionId} by {DaysToAdvance} day(s). LastProgressedUtc={LastProgressedUtc}, NewLastProgressedUtc={NewLastProgressedUtc}.",
            state.CompanyId,
            state.ActiveSessionId,
            computation.DaysToAdvance,
            state.LastProgressedUtc ?? state.UpdatedUtc,
            computation.NewLastProgressedUtc);

        ProgressCompanySimulationStateResult progressionResult;
        try
        {
            progressionResult = await _repository.TryProgressAsync(
                new UpdateCompanySimulationStateCommand(
                    state.CompanyId,
                    computation.NewCurrentSimulatedUtc,
                    computation.NewLastProgressedUtc,
                    UpdatedUtc: computation.ObservedUtc,
                    ExpectedCurrentSimulatedUtc: state.CurrentSimulatedUtc,
                    ExpectedLastProgressedUtc: state.LastProgressedUtc),
                cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogInformation(
                ex,
                "Simulation progression update lost a concurrency race for company {CompanyId}. Reloading current state.",
                companyId);

            var current = await _repository.GetCurrentAsync(companyId, cancellationToken);
            return new SimulationProgressionResult(current, false);
        }

        var updated = progressionResult.State;
        if (!progressionResult.Applied || updated is null)
        {
            return new SimulationProgressionResult(updated, false);
        }

        if (_financeGenerationPolicy is not null &&
            state.GenerationEnabled &&
            updated.ActiveSessionId.HasValue)
        {
            var generationResult = await _financeGenerationPolicy.GenerateAsync(
                new GenerateCompanySimulationFinanceCommand(
                    updated.CompanyId,
                    updated.ActiveSessionId.Value,
                    updated.StartSimulatedUtc,
                    state.CurrentSimulatedUtc,
                    updated.CurrentSimulatedUtc,
                    updated.Seed,
                    updated.DeterministicConfigurationJson),
                cancellationToken);

            if (_repository is EfCompanySimulationStateRepository historyRepository)
            {
                await historyRepository.RecordFinanceGenerationAsync(
                    updated.CompanyId,
                    updated.ActiveSessionId.Value,
                    generationResult,
                    updated.CurrentSimulatedUtc,
                    cancellationToken);
            }
        }

        return new SimulationProgressionResult(updated, true);
    }

    private void EnsureSimulationExecutionEnabled(Guid companyId, string operationName)
    {
        if (_featureGate?.IsBackendExecutionEnabled() == false)
        {
            var featureState = _featureGate.GetState();
            _logger.LogInformation(
                "Blocked simulation execution for company {CompanyId} on {OperationName} because simulation execution is disabled. BackendExecutionEnabled={BackendExecutionEnabled}, BackgroundJobsEnabled={BackgroundJobsEnabled}.",
                companyId,
                operationName,
                featureState.BackendExecutionEnabled,
                featureState.BackgroundJobsEnabled);

            _featureGate.EnsureBackendExecutionEnabled();
        }
    }

    private static SimulationProgressionComputation? TryCalculateProgression(
        CompanySimulationState state,
        DateTime observedUtc)
    {
        var lastProgressedUtc = state.LastProgressedUtc ?? state.UpdatedUtc;
        if (observedUtc <= lastProgressedUtc)
        {
            return null;
        }

        var elapsed = observedUtc - lastProgressedUtc;
        var daysToAdvance = (long)(elapsed.TotalSeconds / ProgressionBucketSeconds);
        if (daysToAdvance <= 0)
        {
            return null;
        }

        var newLastProgressedUtc = lastProgressedUtc.AddSeconds(daysToAdvance * ProgressionBucketSeconds);
        var newCurrentSimulatedUtc = state.CurrentSimulatedUtc.AddDays(daysToAdvance);
        return new SimulationProgressionComputation(observedUtc, newCurrentSimulatedUtc, newLastProgressedUtc, daysToAdvance);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.IsResolved == true &&
            _companyContextAccessor.CompanyId is Guid resolvedCompanyId &&
            resolvedCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Simulation operations are scoped to the active company context.");
        }
    }

    private DateTime GetUtcNow() => NormalizeUtc(_timeProvider.GetUtcNow().UtcDateTime);

    private DateTime GetDefaultDraftSimulatedUtc()
    {
        var utcNow = GetUtcNow();
        return new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc);
    }

    private static int ResolveDefaultSeed(Guid companyId) =>
        (int)(BitConverter.ToUInt32(companyId.ToByteArray(), 0) & int.MaxValue);

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static CompanySimulationStateDto Map(
        CompanySimulationState state,
        SimulationFeatureStateDto? featureState = null,
        IReadOnlyList<CompanySimulationRunHistoryDto>? history = null)
    {
        var resolved = featureState ?? new SimulationFeatureStateDto(true, true, true, string.Empty);
        return new(
            state.CompanyId,
            state.Status.ToStorageValue(),
            state.CurrentSimulatedUtc,
            state.LastProgressedUtc,
            state.GenerationEnabled,
            state.Seed,
            state.ActiveSessionId,
            state.StartSimulatedUtc,
            state.DeterministicConfigurationJson,
            CanStart: state.IsPaused || state.IsStopped,
            CanPause: state.IsRunning,
            CanStop: state.IsRunning || state.IsPaused,
            CanToggleGeneration: !state.IsRunning,
            UiVisible: resolved.UiVisible,
            BackendExecutionEnabled: resolved.BackendExecutionEnabled,
            BackgroundJobsEnabled: resolved.BackgroundJobsEnabled,
            SupportsStepForwardOneDay: resolved.BackendExecutionEnabled,
            DisabledReason: resolved.BackendExecutionEnabled ? null : resolved.DisabledMessage,
            SupportsRefresh: true,
            RecentHistory: history ?? []);
    }

    private static CompanySimulationRunHistoryDto MapHistory(CompanySimulationRunHistory history) =>
        new(
            history.SessionId,
            history.Status.ToStorageValue(),
            history.StartedUtc,
            history.CompletedUtc,
            history.UpdatedUtc,
            history.GenerationEnabled,
            history.Seed,
            history.StartSimulatedUtc,
            history.CurrentSimulatedUtc,
            history.InjectedAnomalies.ToArray(),
            history.Warnings.ToArray(),
            history.Errors.ToArray(),
            history.StatusTransitions
                .OrderBy(x => x.TransitionedUtc)
                .Select(x => new CompanySimulationStatusTransitionDto(x.Status.ToStorageValue(), x.TransitionedUtc, x.Message))
                .ToArray(),
            history.DayLogs
                .OrderBy(x => x.SimulatedDateUtc)
                .Select(x => new CompanySimulationDayLogDto(
                    x.SimulatedDateUtc,
                    x.TransactionsGenerated,
                    x.InvoicesGenerated,
                    x.AssetPurchasesGenerated,
                    x.BillsGenerated,
                    x.RecurringExpenseInstancesGenerated,
                    x.AlertsGenerated,
                    x.InjectedAnomalies.ToArray(),
                    x.Warnings.ToArray(),
                    x.Errors.ToArray()))
                .ToArray());

    private static string BuildProgressionLockKey(Guid companyId) =>
        $"{ProgressionLockPrefix}:{companyId:N}";

    private sealed record SimulationProgressionResult(CompanySimulationState? State, bool Advanced);
    private sealed record SimulationProgressionComputation(DateTime ObservedUtc, DateTime NewCurrentSimulatedUtc, DateTime NewLastProgressedUtc, long DaysToAdvance);
}

public sealed class CompanySimulationProgressionRunner : ICompanySimulationProgressionRunner
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly CompanySimulationStateService _stateService;
    private readonly ICompanyExecutionScopeFactory _companyExecutionScopeFactory;
    private readonly IOptions<CompanySimulationProgressionWorkerOptions> _options;
    private readonly ILogger<CompanySimulationProgressionRunner> _logger;
    private readonly ISimulationFeatureGate _featureGate;

    public CompanySimulationProgressionRunner(
        VirtualCompanyDbContext dbContext,
        CompanySimulationStateService stateService,
        ICompanyExecutionScopeFactory companyExecutionScopeFactory)
        : this(
            dbContext,
            stateService,
            companyExecutionScopeFactory,
            Options.Create(new CompanySimulationProgressionWorkerOptions()),
            LoggerFactory.Create(builder => { }).CreateLogger<CompanySimulationProgressionRunner>(),
            AlwaysEnabledSimulationFeatureGate.Instance)
    {
    }

    public CompanySimulationProgressionRunner(
        VirtualCompanyDbContext dbContext,
        CompanySimulationStateService stateService,
        ICompanyExecutionScopeFactory companyExecutionScopeFactory,
        IOptions<CompanySimulationProgressionWorkerOptions> options,
        ILogger<CompanySimulationProgressionRunner> logger,
        ISimulationFeatureGate featureGate)
    {
        _dbContext = dbContext;
        _stateService = stateService;
        _companyExecutionScopeFactory = companyExecutionScopeFactory;
        _options = options;
        _logger = logger;
        _featureGate = featureGate;
    }

    public async Task<int> ProgressDueAsync(CancellationToken cancellationToken)
    {
        if (!_featureGate.IsBackgroundExecutionAllowed())
        {
            var featureState = _featureGate.GetState();
            _logger.LogInformation(
                "Skipping company simulation progression because simulation execution is disabled. BackendExecutionEnabled={BackendExecutionEnabled}, BackgroundJobsEnabled={BackgroundJobsEnabled}.",
                featureState.BackendExecutionEnabled,
                featureState.BackgroundJobsEnabled);
            return 0;
        }

        var batchSize = Math.Max(1, _options.Value.BatchSize);
        List<Guid> companyIds;
        try
        {
            companyIds = await _dbContext.CompanySimulationStates
                .IgnoreQueryFilters()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.Status == CompanySimulationStatus.Running)
                .OrderBy(x => x.LastProgressedUtc ?? x.UpdatedUtc)
                .ThenBy(x => x.CompanyId)
                .Select(x => x.CompanyId)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }
        catch (SqlException ex) when (IsMissingSimulationSchema(ex))
        {
            _logger.LogWarning(
                ex,
                "Skipping company simulation progression because the simulation schema is not available yet.");
            return 0;
        }

        var progressed = 0;
        foreach (var companyId in companyIds)
        {
            try
            {
                using var scope = _companyExecutionScopeFactory.BeginScope(companyId);
                if (await _stateService.ProgressIfDueAsync(companyId, cancellationToken))
                {
                    progressed++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { _logger.LogError(ex, "Failed to progress simulation state for company {CompanyId}.", companyId); }
        }

        return progressed;
    }

    private static bool IsMissingSimulationSchema(SqlException exception) =>
        exception.Number == 208 &&
        exception.Message.Contains("company_simulation_", StringComparison.OrdinalIgnoreCase);
}

public sealed class CompanySimulationProgressionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<CompanySimulationProgressionWorkerOptions> _options;
    private readonly ISimulationFeatureGate _featureGate;
    private readonly ILogger<CompanySimulationProgressionBackgroundService> _logger;

    public CompanySimulationProgressionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<CompanySimulationProgressionWorkerOptions> options,
        ISimulationFeatureGate featureGate,
        ILogger<CompanySimulationProgressionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _featureGate = featureGate;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Company simulation progression worker is disabled.");
            return;
        }

        if (!_featureGate.IsBackgroundExecutionAllowed())
        {
            var featureState = _featureGate.GetState();
            _logger.LogInformation(
                "Company simulation progression worker is disabled by simulation feature flags. BackendExecutionEnabled={BackendExecutionEnabled}, BackgroundJobsEnabled={BackgroundJobsEnabled}.",
                featureState.BackendExecutionEnabled,
                featureState.BackgroundJobsEnabled);
            return;
        }

        var pollInterval = TimeSpan.FromMilliseconds(_options.Value.PollIntervalMilliseconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ICompanySimulationProgressionRunner>();
                await runner.ProgressDueAsync(stoppingToken);
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Company simulation progression worker loop failed.");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }
}

internal sealed class AlwaysEnabledSimulationFeatureGate : ISimulationFeatureGate
{
    public static AlwaysEnabledSimulationFeatureGate Instance { get; } = new();

    private static readonly SimulationFeatureStateDto State = new(
        UiVisible: true,
        BackendExecutionEnabled: true,
        BackgroundJobsEnabled: true,
        DisabledMessage: string.Empty);

    private AlwaysEnabledSimulationFeatureGate()
    {
    }

    public SimulationFeatureStateDto GetState() => State;

    public bool IsUiVisible() => true;

    public bool IsBackendExecutionEnabled() => true;

    public bool AreBackgroundJobsEnabled() => true;

    public bool IsBackgroundExecutionAllowed() => true;

    public bool IsFullyDisabled() => false;

    public void EnsureBackendExecutionEnabled() { }

    public void EnsureBackgroundExecutionEnabled() { }
}
