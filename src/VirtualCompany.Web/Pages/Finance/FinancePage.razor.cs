using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class FinancePage : FinancePageBase, IDisposable
{
    [Inject] protected FinanceApiClient FinanceApiClient { get; set; } = default!;
    [Inject] protected IOptions<FinanceSimulationControlPanelOptions> SimulationControlPanelOptionsAccessor { get; set; } = default!;
    [Inject] protected ILogger<FinancePage> Logger { get; set; } = default!;

    private const int PollIntervalMilliseconds = 750;

    private CancellationTokenSource? _entryStateCts;
    private int _entryStateVersion;
    private CancellationTokenSource? _simulationStateCts;
    private int _simulationStateVersion;

    protected FinanceEntryInitializationResponse? EntryState { get; private set; }
    protected bool IsEntryStateLoading { get; private set; }
    protected string? EntryStateErrorMessage { get; private set; }
    protected string? ManualSeedErrorMessage { get; private set; }
    protected bool IsManualSeedConfirmationVisible { get; private set; }
    protected bool IsSubmittingManualSeed { get; private set; }
    protected bool ShowFinanceReadyBanner { get; private set; }
    protected FinanceCompanySimulationStateResponse? SimulationState { get; private set; }
    protected bool IsSimulationStateLoading { get; private set; }
    protected string? SimulationStateErrorMessage { get; private set; }
    protected string? SimulationActionMessage { get; private set; }
    protected bool IsSimulationActionInFlight { get; private set; }
    protected bool RequestedGenerationEnabled { get; private set; } = true;

    protected bool IsSimulationControlPanelVisible =>
        SimulationControlPanelOptionsAccessor.Value.UiVisible &&
        (SimulationState?.UiVisible ?? true) &&
        IsFinanceReady;
    protected bool CanManageSimulation => FinanceAccess.CanManageSimulation(AccessState.MembershipRole);
    protected bool IsSimulationBackendExecutionEnabled => SimulationState?.BackendExecutionEnabled ?? true;
    protected string? SimulationDisabledReason => SimulationState?.DisabledReason;
    protected IReadOnlyList<FinanceCompanySimulationRunHistoryResponse> RecentSimulationHistory => SimulationState?.RecentHistory ?? [];
    protected bool SupportsSimulationRefresh => SimulationState?.SupportsRefresh ?? true;
    protected bool SupportsSimulationHistory => RecentSimulationHistory.Count > 0;
    protected bool SupportsStepForwardOneDay => SimulationState?.SupportsStepForwardOneDay ?? false;
    protected bool IsSimulationBusy => IsSimulationStateLoading || IsSimulationActionInFlight;
    protected bool CanStartSimulation => CanManageSimulation && !IsSimulationBusy && (SimulationState?.CanStart ?? true);
    protected bool CanPauseSimulation => CanManageSimulation && !IsSimulationBusy && (SimulationState?.CanPause ?? false);
    protected bool CanStopSimulation => CanManageSimulation && !IsSimulationBusy && (SimulationState?.CanStop ?? false);
    protected bool CanStepForwardSimulation => CanManageSimulation && !IsSimulationBusy && SupportsStepForwardOneDay && IsSimulationPaused;
    protected bool CanEditGenerationEnabled => CanManageSimulation && !IsSimulationBusy && (SimulationState?.CanToggleGeneration ?? true);
    protected string SimulationStatusLabel => ResolveSimulationStatusLabel(SimulationState?.Status);
    protected string SimulationStatusBadgeClass => $"badge rounded-pill {ResolveSimulationStatusBadgeClass(SimulationState?.Status)}";
    protected string SimulationStatusDescription => ResolveSimulationStatusDescription(SimulationState?.Status);
    protected string CurrentSimulatedDateTimeLabel => FormatSimulationTimestamp(SimulationState?.CurrentSimulatedDateTime, "Not started");
    protected string LastProgressionTimestampLabel => FormatSimulationTimestamp(SimulationState?.LastProgressionTimestamp, "No progression recorded yet");
    protected string LastProgressionMetadataLabel => ResolveLastProgressionMetadata(SimulationState);
    protected string GenerationStateLabel => RequestedGenerationEnabled ? "On" : "Off";

    protected bool IsFinanceRequested =>
        string.Equals(EntryState?.ProgressState, FinanceEntryProgressStateContractValues.SeedingRequested, StringComparison.OrdinalIgnoreCase);

    protected bool IsFinanceInProgress =>
        string.Equals(EntryState?.ProgressState, FinanceEntryProgressStateContractValues.InProgress, StringComparison.OrdinalIgnoreCase);

    protected bool IsFinanceInitializing =>
        IsFinanceRequested ||
        IsFinanceInProgress ||
        string.Equals(EntryState?.InitializationStatus, FinanceEntryInitializationContractValues.Initializing, StringComparison.OrdinalIgnoreCase);

    protected bool IsFinanceFailed =>
        string.Equals(EntryState?.ProgressState, FinanceEntryProgressStateContractValues.Failed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(EntryState?.InitializationStatus, FinanceEntryInitializationContractValues.Failed, StringComparison.OrdinalIgnoreCase);

    protected bool IsFinanceReady => IsReady(EntryState);

    protected bool IsFinanceSeeded =>
        string.Equals(EntryState?.ProgressState, FinanceEntryProgressStateContractValues.Seeded, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(EntryState?.SeedingState, FinanceSeedingStateContractValues.Seeded, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(EntryState?.SeedingState, FinanceSeedingStateContractValues.FullySeeded, StringComparison.OrdinalIgnoreCase);

    protected string ManualSeedActionLabel =>
        string.Equals(ResolveRecommendedAction(), FinanceRecommendedActionContractValues.Regenerate, StringComparison.OrdinalIgnoreCase)
            ? "Regenerate finance data"
            : "Generate finance data";

    protected string ManualSeedActionDescription =>
        string.Equals(ResolveRecommendedAction(), FinanceRecommendedActionContractValues.Regenerate, StringComparison.OrdinalIgnoreCase)
            ? "Regenerate the tenant-scoped finance seed dataset in replace mode."
            : "Generate the tenant-scoped finance seed dataset before opening finance workflows.";

    protected string ManualSeedConfirmationMessage =>
        EntryState?.ConfirmationMessage ??
        "Regenerating finance data in replace mode overwrites the current seeded dataset for this company.";

    protected string GenerationToggleHelpText =>
        IsSimulationNotStarted
            ? "Changes are saved to the company simulation state and applied when the next simulation session starts."
            : CanEditGenerationEnabled
                ? "Changes are saved to the active company simulation state."
                : "Pause or stop the simulation before changing finance generation.";

    protected string FinanceStateMessage =>
        EntryState?.Message ??
        "Finance data is being prepared for this company.";

    protected string FinanceProgressTitle => IsFinanceRequested ? "Initializing finance data" : "Finance setup in progress";

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        CancelEntryStateRefresh();
        CancelSimulationStateRefresh();
        if (IsLoading || !AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            ResetEntryState();
            ResetSimulationState();
            return;
        }

        await LoadEntryStateAsync(companyId);
    }

    protected Task RefreshEntryStateAsync() =>
        AccessState.CompanyId is Guid companyId
            ? LoadEntryStateAsync(companyId)
            : Task.CompletedTask;

    protected Task RetryFinanceSetupAsync() =>
        AccessState.CompanyId is Guid companyId
            ? RequestEntryStateAsync(companyId, retryOnFailure: true)
            : Task.CompletedTask;

    private async Task LoadEntryStateAsync(Guid companyId)
    {
        IsEntryStateLoading = true;
        ManualSeedErrorMessage = null;
        EntryStateErrorMessage = null;
        ShowFinanceReadyBanner = false;
        await InvokeAsync(StateHasChanged);

        var previousState = EntryState;
        var operation = BeginEntryStateOperation();

        try
        {
            var entryState = await FinanceApiClient.GetEntryInitializationStateAsync(companyId, operation.CancellationTokenSource.Token);
            if (!IsCurrentOperation(operation.Version, operation.CancellationTokenSource))
            {
                return;
            }

            ApplyEntryState(entryState, previousState);
            await LoadSimulationStateForEntryStateAsync(companyId);
            if (ShouldPoll(entryState))
            {
                IsEntryStateLoading = false;
                _ = PollUntilReadyAsync(companyId, operation.Version, operation.CancellationTokenSource);
            }
        }
        catch (OperationCanceledException) when (operation.CancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (FinanceApiException ex)
        {
            if (!IsCurrentOperation(operation.Version, operation.CancellationTokenSource))
            {
                return;
            }

            EntryStateErrorMessage = ex.Message;
        }
        finally
        {
            if (IsCurrentOperation(operation.Version, operation.CancellationTokenSource))
            {
                IsEntryStateLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task RequestEntryStateAsync(Guid companyId, bool retryOnFailure)
    {
        EntryStateErrorMessage = null;
        ManualSeedErrorMessage = null;
        IsManualSeedConfirmationVisible = false;
        IsEntryStateLoading = false;
        var previousState = EntryState;
        var operation = BeginEntryStateOperation();

        ApplyEntryState(CreateRequestingState(previousState, companyId, retryOnFailure), previousState);
        await InvokeAsync(StateHasChanged);
        await RequestEntryStateCoreAsync(companyId, operation.Version, operation.CancellationTokenSource, retryOnFailure);
    }

    private async Task RequestEntryStateCoreAsync(Guid companyId, int loadVersion, CancellationTokenSource cancellationTokenSource, bool retryOnFailure)
    {
        try
        {
            var nextState = retryOnFailure
                ? await FinanceApiClient.RetryEntryInitializationAsync(companyId, cancellationTokenSource.Token)
                : await FinanceApiClient.RequestEntryInitializationAsync(companyId, cancellationTokenSource.Token);

            if (!IsCurrentOperation(loadVersion, cancellationTokenSource))
            {
                return;
            }

            var previousState = EntryState;
            ApplyEntryState(nextState, previousState);
            await LoadSimulationStateForEntryStateAsync(companyId);
            if (ShouldPoll(nextState))
            {
                _ = PollUntilReadyAsync(companyId, loadVersion, cancellationTokenSource);
            }
        }
        catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (FinanceApiException ex)
        {
            if (!IsCurrentOperation(loadVersion, cancellationTokenSource))
            {
                return;
            }

            EntryStateErrorMessage = ex.Message;
        }
        finally
        {
            if (IsCurrentOperation(loadVersion, cancellationTokenSource))
            {
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task PollUntilReadyAsync(Guid companyId, int loadVersion, CancellationTokenSource cancellationTokenSource)
    {
        while (!cancellationTokenSource.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollIntervalMilliseconds, cancellationTokenSource.Token);
                var previousState = EntryState;
                var nextState = await FinanceApiClient.GetEntryInitializationStateAsync(companyId, cancellationTokenSource.Token);
                if (!IsCurrentOperation(loadVersion, cancellationTokenSource))
                {
                    return;
                }

                ApplyEntryState(nextState, previousState);
                await LoadSimulationStateForEntryStateAsync(companyId);
                await InvokeAsync(StateHasChanged);
                if (!ShouldPoll(nextState))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
            {
                break;
            }
            catch (FinanceApiException ex)
            {
                if (!IsCurrentOperation(loadVersion, cancellationTokenSource))
                {
                    return;
                }

                EntryStateErrorMessage = ex.Message;
                await InvokeAsync(StateHasChanged);
                return;
            }
        }
    }

    protected Task RefreshSimulationStateAsync() =>
        AccessState.CompanyId is Guid companyId && IsSimulationControlPanelVisible && !IsSimulationActionInFlight
            ? LoadSimulationStateAsync(companyId)
            : Task.CompletedTask;

    protected Task HandleStartSimulationAsync() =>
        ExecuteSimulationMutationAsync(
            async (companyId, cancellationToken) =>
            {
                var startUtc = ResolveSimulationStartUtc();
                var seed = ResolveSimulationSeed(companyId);
                Logger.LogInformation(
                    "Finance UI simulation action requested. CompanyId: {CompanyId}. Action: {Action}. IsPaused: {IsPaused}. StartSimulatedUtc: {StartSimulatedUtc}. GenerationEnabled: {GenerationEnabled}. Seed: {Seed}.",
                    companyId,
                    IsSimulationPaused ? "resume" : "start",
                    IsSimulationPaused,
                    startUtc,
                    RequestedGenerationEnabled,
                    seed);

                return IsSimulationPaused
                    ? await FinanceApiClient.ResumeCompanySimulationAsync(companyId, cancellationToken)
                    : await FinanceApiClient.StartCompanySimulationAsync(
                        companyId,
                        new FinanceCompanySimulationStartRequest
                        {
                            StartSimulatedDateTime = startUtc,
                            GenerationEnabled = RequestedGenerationEnabled,
                            Seed = seed
                        },
                        cancellationToken);
            },
            IsSimulationPaused ? "Simulation resumed." : "Simulation started.");

    protected Task HandleStepForwardSimulationAsync() =>
        ExecuteSimulationMutationAsync(
            (companyId, cancellationToken) => FinanceApiClient.StepForwardCompanySimulationAsync(companyId, cancellationToken),
            "Simulation advanced by 1 day.",
            (companyId, currentState) => CreateOptimisticStepForwardState(companyId, currentState));

    protected Task HandlePauseSimulationAsync() =>
        ExecuteSimulationMutationAsync(
            (companyId, cancellationToken) => FinanceApiClient.PauseCompanySimulationAsync(companyId, cancellationToken),
            "Simulation paused.",
            (companyId, currentState) => CreateOptimisticStatusState(companyId, currentState, FinanceCompanySimulationStatusValues.Paused));

    protected Task HandleStopSimulationAsync() =>
        ExecuteSimulationMutationAsync(
            (companyId, cancellationToken) => FinanceApiClient.StopCompanySimulationAsync(companyId, cancellationToken),
            "Simulation stopped.",
            (companyId, currentState) => CreateOptimisticStatusState(companyId, currentState, FinanceCompanySimulationStatusValues.Stopped));

    protected async Task HandleGenerationEnabledChangedAsync(ChangeEventArgs args)
    {
        var nextValue = args.Value switch
        {
            bool boolValue => boolValue,
            string stringValue when bool.TryParse(stringValue, out var parsed) => parsed,
            _ => RequestedGenerationEnabled
        };

        var previousValue = RequestedGenerationEnabled;
        RequestedGenerationEnabled = nextValue;
        SimulationActionMessage = null;
        SimulationStateErrorMessage = null;

        if (!CanManageSimulation)
        {
            return;
        }

        if (AccessState.CompanyId is not Guid companyId || IsSimulationActionInFlight)
        {
            return;
        }

        if (!CanEditGenerationEnabled)
        {
            RequestedGenerationEnabled = SimulationState?.GenerationEnabled ?? previousValue;
            await InvokeAsync(StateHasChanged);
            return;
        }

        await ExecuteSimulationMutationAsync(
            (resolvedCompanyId, cancellationToken) => FinanceApiClient.UpdateCompanySimulationSettingsAsync(
                resolvedCompanyId,
                new FinanceCompanySimulationUpdateRequest
                {
                    GenerationEnabled = nextValue
                },
                cancellationToken),
            nextValue ? "Generation enabled." : "Generation disabled.",
            (resolvedCompanyId, currentState) => CreateOptimisticGenerationState(resolvedCompanyId, currentState, nextValue));

        if (!string.IsNullOrWhiteSpace(SimulationStateErrorMessage))
        {
            RequestedGenerationEnabled = SimulationState?.GenerationEnabled ?? previousValue;
        }
    }

    private async Task LoadSimulationStateForEntryStateAsync(Guid companyId)
    {
        if (!IsSimulationControlPanelVisible)
        {
            ResetSimulationState();
            return;
        }

        await LoadSimulationStateAsync(companyId);
    }

    private async Task LoadSimulationStateAsync(Guid companyId)
    {
        var operation = BeginSimulationStateOperation();
        IsSimulationStateLoading = true;
        SimulationStateErrorMessage = null;
        await InvokeAsync(StateHasChanged);

        try
        {
            var state = await FinanceApiClient.GetCompanySimulationStateAsync(companyId, operation.CancellationTokenSource.Token);
            if (!IsCurrentSimulationOperation(operation.Version, operation.CancellationTokenSource))
            {
                return;
            }

            ApplySimulationState(state);
            if (ShouldPollSimulation(state))
            {
                IsSimulationStateLoading = false;
                _ = PollSimulationStateAsync(companyId, operation.Version, operation.CancellationTokenSource);
            }
        }
        catch (OperationCanceledException) when (operation.CancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (FinanceApiException ex)
        {
            if (!IsCurrentSimulationOperation(operation.Version, operation.CancellationTokenSource))
            {
                return;
            }

            SimulationStateErrorMessage = ex.Message;
            SimulationState = null;
        }
        finally
        {
            if (IsCurrentSimulationOperation(operation.Version, operation.CancellationTokenSource))
            {
                IsSimulationStateLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private async Task PollSimulationStateAsync(Guid companyId, int loadVersion, CancellationTokenSource cancellationTokenSource)
    {
        while (!cancellationTokenSource.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(GetSimulationPollIntervalMilliseconds(), cancellationTokenSource.Token);
                var nextState = await FinanceApiClient.GetCompanySimulationStateAsync(companyId, cancellationTokenSource.Token);
                if (!IsCurrentSimulationOperation(loadVersion, cancellationTokenSource))
                {
                    return;
                }

                ApplySimulationState(nextState);
                await InvokeAsync(StateHasChanged);
                if (!ShouldPollSimulation(nextState))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationTokenSource.IsCancellationRequested)
            {
                break;
            }
            catch (FinanceApiException ex)
            {
                if (!IsCurrentSimulationOperation(loadVersion, cancellationTokenSource))
                {
                    return;
                }

                SimulationStateErrorMessage = ex.Message;
                await InvokeAsync(StateHasChanged);
                return;
            }
        }
    }

    protected async Task HandleManualSeedActionAsync()
    {
        ManualSeedErrorMessage = null;
        if (IsFinanceSeeded)
        {
            IsManualSeedConfirmationVisible = true;
            await InvokeAsync(StateHasChanged);
            return;
        }

        await SubmitManualSeedAsync();
    }

    protected void CancelManualSeedConfirmation() =>
        IsManualSeedConfirmationVisible = false;

    protected Task ConfirmManualSeedAsync() =>
        SubmitManualSeedAsync();

    private async Task SubmitManualSeedAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || IsSubmittingManualSeed)
        {
            return;
        }

        var previousState = EntryState;
        var operation = BeginEntryStateOperation();
        IsManualSeedConfirmationVisible = false;
        IsSubmittingManualSeed = true;
        EntryStateErrorMessage = null;
        ManualSeedErrorMessage = null;
        ApplyEntryState(CreateRequestingState(previousState, companyId, isRetry: false), previousState);
        await InvokeAsync(StateHasChanged);

        try
        {
            var nextState = await FinanceApiClient.RequestManualSeedAsync(
                companyId,
                new FinanceManualSeedRequest
                {
                    Mode = FinanceManualSeedModes.Replace,
                    ConfirmReplace = IsFinanceSeeded
                },
                operation.CancellationTokenSource.Token);

            if (!IsCurrentOperation(operation.Version, operation.CancellationTokenSource))
            {
                return;
            }

            ApplyEntryState(nextState, previousState);
            if (ShouldPoll(nextState))
            {
                _ = PollUntilReadyAsync(companyId, operation.Version, operation.CancellationTokenSource);
            }
        }
        catch (OperationCanceledException) when (operation.CancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (FinanceApiException ex)
        {
            if (!IsCurrentOperation(operation.Version, operation.CancellationTokenSource))
            {
                return;
            }

            ManualSeedErrorMessage = ex.Message;
        }
        finally
        {
            IsSubmittingManualSeed = false;
            if (IsCurrentOperation(operation.Version, operation.CancellationTokenSource))
            {
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private static bool ShouldPoll(FinanceEntryInitializationResponse state) =>
        string.Equals(state.ProgressState, FinanceEntryProgressStateContractValues.SeedingRequested, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state.ProgressState, FinanceEntryProgressStateContractValues.InProgress, StringComparison.OrdinalIgnoreCase) ||
        (string.IsNullOrWhiteSpace(state.ProgressState) &&
         string.Equals(state.InitializationStatus, FinanceEntryInitializationContractValues.Initializing, StringComparison.OrdinalIgnoreCase));

    private static bool IsReady(FinanceEntryInitializationResponse? state) =>
        state is not null &&
        (string.Equals(state.ProgressState, FinanceEntryProgressStateContractValues.Seeded, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(state.InitializationStatus, FinanceEntryInitializationContractValues.Ready, StringComparison.OrdinalIgnoreCase));

    private static FinanceEntryInitializationResponse CreateRequestingState(
        FinanceEntryInitializationResponse? currentState,
        Guid companyId,
        bool isRetry) =>
        new()
        {
            CompanyId = companyId,
            InitializationStatus = FinanceEntryInitializationContractValues.Initializing,
            ProgressState = FinanceEntryProgressStateContractValues.SeedingRequested,
            SeedingState = FinanceSeedingStateContractValues.Seeding,
            SeedJobEnqueued = true,
            SeedJobActive = true,
            CanRetry = false,
            CanRefresh = true,
            Message = isRetry
                ? "Retrying finance data initialization in the background. This page refreshes automatically while setup runs."
                : "Requesting finance data initialization in the background. This page refreshes automatically while setup runs.",
            CheckedAtUtc = DateTime.UtcNow,
            SeededAtUtc = currentState?.SeededAtUtc,
            LastAttemptedUtc = currentState?.LastAttemptedUtc,
            LastCompletedUtc = currentState?.LastCompletedUtc,
            LastErrorCode = null,
            LastErrorMessage = null,
            JobStatus = currentState?.JobStatus,
            CorrelationId = currentState?.CorrelationId
        };

    private void ApplyEntryState(FinanceEntryInitializationResponse nextState, FinanceEntryInitializationResponse? previousState)
    {
        EntryState = nextState;
        if (previousState is not null && !IsReady(previousState) && IsReady(nextState))
        {
            ShowFinanceReadyBanner = true;
        }
        else if (!IsReady(nextState))
        {
            ShowFinanceReadyBanner = false;
        }
    }

    private (int Version, CancellationTokenSource CancellationTokenSource) BeginEntryStateOperation()
    {
        var version = Interlocked.Increment(ref _entryStateVersion);
        var cancellationTokenSource = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _entryStateCts, cancellationTokenSource);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        return (version, cancellationTokenSource);
    }

    private bool IsCurrentOperation(int loadVersion, CancellationTokenSource cancellationTokenSource) =>
        loadVersion == _entryStateVersion &&
        ReferenceEquals(_entryStateCts, cancellationTokenSource) &&
        !cancellationTokenSource.IsCancellationRequested;

    private void ResetEntryState()
    {
        EntryState = null;
        EntryStateErrorMessage = null;
        ManualSeedErrorMessage = null;
        IsEntryStateLoading = false;
        IsSubmittingManualSeed = false;
        IsManualSeedConfirmationVisible = false;
        ShowFinanceReadyBanner = false;
    }

    private void CancelEntryStateRefresh()
    {
        var cancellationTokenSource = Interlocked.Exchange(ref _entryStateCts, null);
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }

    private async Task ExecuteSimulationMutationAsync(
        Func<Guid, CancellationToken, Task<FinanceCompanySimulationStateResponse>> callback,
        string successMessage,
        Func<Guid, FinanceCompanySimulationStateResponse?, FinanceCompanySimulationStateResponse>? optimisticStateFactory = null)
    {
        if (!CanManageSimulation || AccessState.CompanyId is not Guid companyId || IsSimulationActionInFlight)
        {
            return;
        }

        var previousState = CloneSimulationState(SimulationState);
        var previousGenerationEnabled = RequestedGenerationEnabled;
        IsSimulationActionInFlight = true;
        SimulationActionMessage = null;
        SimulationStateErrorMessage = null;
        if (optimisticStateFactory is not null)
        {
            ApplySimulationState(optimisticStateFactory(companyId, previousState));
        }

        await InvokeAsync(StateHasChanged);

        try
        {
            var state = await callback(companyId, CancellationToken.None);
            Logger.LogInformation(
                "Finance UI simulation action completed. CompanyId: {CompanyId}. Status: {Status}. SessionId: {SessionId}. CurrentSimulatedDateTime: {CurrentSimulatedDateTime}. LastProgressionTimestamp: {LastProgressionTimestamp}. GenerationEnabled: {GenerationEnabled}.",
                companyId,
                state.Status,
                state.ActiveSessionId,
                state.CurrentSimulatedDateTime,
                state.LastProgressionTimestamp,
                state.GenerationEnabled);
            ApplySimulationState(state);
            SimulationActionMessage = successMessage;
            _ = ReconcileSimulationStateAfterDelayAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            Logger.LogWarning(
                ex,
                "Finance UI simulation action failed. CompanyId: {CompanyId}.",
                companyId);
            SimulationState = previousState;
            RequestedGenerationEnabled = previousGenerationEnabled;
            SimulationStateErrorMessage = ex.Message;
        }
        finally
        {
            IsSimulationActionInFlight = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ApplySimulationState(FinanceCompanySimulationStateResponse state)
    {
        SimulationState = state;
        if (state.GenerationEnabled.HasValue)
        {
            RequestedGenerationEnabled = state.GenerationEnabled.Value;
        }
    }

    private async Task ReconcileSimulationStateAfterDelayAsync(Guid companyId)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1500, Math.Max(500, GetSimulationPollIntervalMilliseconds()))));
            if (AccessState.CompanyId == companyId)
            {
                await LoadSimulationStateAsync(companyId);
            }
        }
        catch (Exception) when (!OperatingSystem.IsBrowser()) { }
    }

    private (int Version, CancellationTokenSource CancellationTokenSource) BeginSimulationStateOperation()
    {
        var version = Interlocked.Increment(ref _simulationStateVersion);
        var cancellationTokenSource = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _simulationStateCts, cancellationTokenSource);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();
        return (version, cancellationTokenSource);
    }

    private bool IsCurrentSimulationOperation(int loadVersion, CancellationTokenSource cancellationTokenSource) =>
        loadVersion == _simulationStateVersion &&
        ReferenceEquals(_simulationStateCts, cancellationTokenSource) &&
        !cancellationTokenSource.IsCancellationRequested;

    private void ResetSimulationState()
    {
        SimulationState = null;
        SimulationStateErrorMessage = null;
        SimulationActionMessage = null;
        IsSimulationStateLoading = false;
        IsSimulationActionInFlight = false;
        RequestedGenerationEnabled = true;
    }

    private void CancelSimulationStateRefresh()
    {
        var cancellationTokenSource = Interlocked.Exchange(ref _simulationStateCts, null);
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }

    public void Dispose()
    {
        CancelEntryStateRefresh();
        CancelSimulationStateRefresh();
    }

    private bool IsSimulationRunning => IsSimulationStatus(FinanceCompanySimulationStatusValues.Running);
    private bool IsSimulationPaused => IsSimulationStatus(FinanceCompanySimulationStatusValues.Paused);
    private bool IsSimulationStopped => IsSimulationStatus(FinanceCompanySimulationStatusValues.Stopped);
    private bool IsSimulationNotStarted => IsSimulationStatus(FinanceCompanySimulationStatusValues.NotStarted) || SimulationState is null;

    private bool IsSimulationStatus(string status) =>
        string.Equals(SimulationState?.Status, status, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldPollSimulation(FinanceCompanySimulationStateResponse state) =>
        string.Equals(state.Status, FinanceCompanySimulationStatusValues.Running, StringComparison.OrdinalIgnoreCase);

    private int GetSimulationPollIntervalMilliseconds() =>
        Math.Max(250, SimulationControlPanelOptionsAccessor.Value.PollIntervalMilliseconds);

    private DateTime ResolveSimulationStartUtc()
    {
        if (SimulationState?.CurrentSimulatedDateTime is DateTime currentSimulatedDateTime)
        {
            return currentSimulatedDateTime;
        }
        if (RecentSimulationHistory.FirstOrDefault()?.CurrentSimulatedDateTime is DateTime historyCurrentSimulatedDateTime)
        {
            return historyCurrentSimulatedDateTime;
        }

        if (EntryState?.SeededAtUtc is DateTime seededAtUtc)
        {
            return DateTime.SpecifyKind(seededAtUtc, DateTimeKind.Utc);
        }

        var utcNow = DateTime.UtcNow;
        return new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc);
    }

    private int ResolveSimulationSeed(Guid companyId) =>
        SimulationState?.Seed ?? (int)(BitConverter.ToUInt32(companyId.ToByteArray(), 0) & int.MaxValue);

    private FinanceCompanySimulationStateResponse CreateOptimisticStatusState(
        Guid companyId,
        FinanceCompanySimulationStateResponse? currentState,
        string status)
    {
        var optimisticState = CreateSimulationStateBaseline(companyId, currentState);
        optimisticState.Status = status;
        optimisticState.LastProgressionTimestamp ??= DateTime.UtcNow;
        optimisticState.CanStart = !string.Equals(status, FinanceCompanySimulationStatusValues.Running, StringComparison.OrdinalIgnoreCase);
        optimisticState.CanPause = string.Equals(status, FinanceCompanySimulationStatusValues.Running, StringComparison.OrdinalIgnoreCase);
        optimisticState.CanStop = !string.Equals(status, FinanceCompanySimulationStatusValues.Stopped, StringComparison.OrdinalIgnoreCase);
        optimisticState.CanToggleGeneration = !string.Equals(status, FinanceCompanySimulationStatusValues.Running, StringComparison.OrdinalIgnoreCase);
        optimisticState.SupportsStepForwardOneDay = true;
        if (string.Equals(status, FinanceCompanySimulationStatusValues.Stopped, StringComparison.OrdinalIgnoreCase))
        {
            optimisticState.ActiveSessionId = null;
        }

        return optimisticState;
    }

    private FinanceCompanySimulationStateResponse CreateOptimisticGenerationState(
        Guid companyId,
        FinanceCompanySimulationStateResponse? currentState,
        bool generationEnabled)
    {
        var optimisticState = CreateSimulationStateBaseline(companyId, currentState);
        optimisticState.GenerationEnabled = generationEnabled;
        optimisticState.CanToggleGeneration = !string.Equals(optimisticState.Status, FinanceCompanySimulationStatusValues.Running, StringComparison.OrdinalIgnoreCase);
        optimisticState.SupportsStepForwardOneDay = true;
        return optimisticState;
    }

    private FinanceCompanySimulationStateResponse CreateOptimisticStepForwardState(
        Guid companyId,
        FinanceCompanySimulationStateResponse? currentState)
    {
        var optimisticState = CreateSimulationStateBaseline(companyId, currentState);
        optimisticState.Status = FinanceCompanySimulationStatusValues.Paused;
        optimisticState.CurrentSimulatedDateTime = (optimisticState.CurrentSimulatedDateTime ?? ResolveSimulationStartUtc()).AddDays(1);
        optimisticState.LastProgressionTimestamp = DateTime.UtcNow;
        optimisticState.CanStart = true;
        optimisticState.CanPause = false;
        optimisticState.CanStop = true;
        optimisticState.CanToggleGeneration = true;
        optimisticState.SupportsStepForwardOneDay = true;
        return optimisticState;
    }

    private FinanceCompanySimulationStateResponse CreateSimulationStateBaseline(
        Guid companyId,
        FinanceCompanySimulationStateResponse? currentState)
    {
        var simulationState = CloneSimulationState(currentState) ?? FinanceCompanySimulationStateResponse.NotStarted(companyId);
        simulationState.CompanyId = companyId;
        simulationState.GenerationEnabled ??= RequestedGenerationEnabled;
        simulationState.Seed ??= ResolveSimulationSeed(companyId);
        simulationState.SupportsRefresh = true;
        simulationState.SupportsStepForwardOneDay = true;
        return simulationState;
    }

    private static FinanceCompanySimulationStateResponse? CloneSimulationState(FinanceCompanySimulationStateResponse? state)
    {
        if (state is null)
        {
            return null;
        }

        return new FinanceCompanySimulationStateResponse
        {
            CompanyId = state.CompanyId,
            Status = state.Status,
            CurrentSimulatedDateTime = state.CurrentSimulatedDateTime,
            LastProgressionTimestamp = state.LastProgressionTimestamp,
            GenerationEnabled = state.GenerationEnabled,
            Seed = state.Seed,
            ActiveSessionId = state.ActiveSessionId,
            StartSimulatedDateTime = state.StartSimulatedDateTime,
            CanStart = state.CanStart,
            CanPause = state.CanPause,
            CanStop = state.CanStop,
            CanToggleGeneration = state.CanToggleGeneration,
            SupportsStepForwardOneDay = state.SupportsStepForwardOneDay,
            SupportsRefresh = state.SupportsRefresh,
            DeterministicConfigurationJson = state.DeterministicConfigurationJson
            ,UiVisible = state.UiVisible,
            BackendExecutionEnabled = state.BackendExecutionEnabled,
            BackgroundJobsEnabled = state.BackgroundJobsEnabled,
            DisabledReason = state.DisabledReason,
            RecentHistory = state.RecentHistory.Select(CloneRunHistory).ToList()
        };
    }

    private static FinanceCompanySimulationRunHistoryResponse CloneRunHistory(FinanceCompanySimulationRunHistoryResponse run) =>
        new()
        {
            SessionId = run.SessionId,
            Status = run.Status,
            StartedUtc = run.StartedUtc,
            CompletedUtc = run.CompletedUtc,
            LastUpdatedUtc = run.LastUpdatedUtc,
            GenerationEnabled = run.GenerationEnabled,
            Seed = run.Seed,
            StartSimulatedDateTime = run.StartSimulatedDateTime,
            CurrentSimulatedDateTime = run.CurrentSimulatedDateTime,
            InjectedAnomalies = run.InjectedAnomalies.ToList(),
            Warnings = run.Warnings.ToList(),
            Errors = run.Errors.ToList(),
            StatusTransitions = run.StatusTransitions.Select(CloneStatusTransition).ToList(),
            DayLogs = run.DayLogs.Select(CloneDayLog).ToList()
        };

    private static FinanceCompanySimulationStatusTransitionResponse CloneStatusTransition(FinanceCompanySimulationStatusTransitionResponse transition) =>
        new()
        {
            Status = transition.Status,
            TransitionedUtc = transition.TransitionedUtc,
            Message = transition.Message
        };

    private static FinanceCompanySimulationDayLogResponse CloneDayLog(FinanceCompanySimulationDayLogResponse dayLog) =>
        new()
        {
            SimulatedDateUtc = dayLog.SimulatedDateUtc,
            TransactionsGenerated = dayLog.TransactionsGenerated,
            InvoicesGenerated = dayLog.InvoicesGenerated,
            BillsGenerated = dayLog.BillsGenerated,
            AssetPurchasesGenerated = dayLog.AssetPurchasesGenerated,
            RecurringExpenseInstancesGenerated = dayLog.RecurringExpenseInstancesGenerated,
            AlertsGenerated = dayLog.AlertsGenerated,
            InjectedAnomalies = dayLog.InjectedAnomalies.ToList(),
            Warnings = dayLog.Warnings.ToList(),
            Errors = dayLog.Errors.ToList()
        };

    private static string FormatSimulationTimestamp(DateTime? value, string emptyValue) =>
        value.HasValue ? $"{value.Value:yyyy-MM-dd HH:mm:ss} UTC" : emptyValue;

    private static string ResolveSimulationStatusLabel(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            FinanceCompanySimulationStatusValues.Running => "Running",
            FinanceCompanySimulationStatusValues.Paused => "Paused",
            FinanceCompanySimulationStatusValues.Stopped => "Stopped",
            _ => "Not started"
        };

    private static string ResolveSimulationStatusDescription(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            FinanceCompanySimulationStatusValues.Running => "The simulation is progressing automatically and the panel refreshes while it runs.",
            FinanceCompanySimulationStatusValues.Paused => "The simulation is paused. You can change generation settings before resuming.",
            FinanceCompanySimulationStatusValues.Stopped => "The simulation is stopped. Start creates a new run from the selected simulated time.",
            _ => "The simulation is stopped. Choose the generation setting, refresh the state, or start when you are ready."
        };

    private static string ResolveSimulationStatusBadgeClass(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            FinanceCompanySimulationStatusValues.Running => "text-bg-success",
            FinanceCompanySimulationStatusValues.Paused => "text-bg-warning",
            FinanceCompanySimulationStatusValues.Stopped => "text-bg-secondary",
            _ => "text-bg-secondary"
        };

    private static string ResolveLastProgressionMetadata(FinanceCompanySimulationStateResponse? state)
    {
        if (state is null)
        {
            return "No progression metadata available.";
        }

        var segments = new List<string>();
        if (state.ActiveSessionId is Guid activeSessionId)
        {
            segments.Add($"Session {activeSessionId.ToString("N")[..8]}");
        }
        if (state.Seed.HasValue)
        {
            segments.Add($"Seed {state.Seed.Value}");
        }

        return segments.Count == 0 ? "No additional progression metadata." : string.Join(" | ", segments);
    }

    private string ResolveRecommendedAction()
    {
        if (!string.IsNullOrWhiteSpace(EntryState?.RecommendedAction))
        {
            return EntryState!.RecommendedAction;
        }
        return IsFinanceSeeded ? FinanceRecommendedActionContractValues.Regenerate : FinanceRecommendedActionContractValues.Generate;
    }
}
