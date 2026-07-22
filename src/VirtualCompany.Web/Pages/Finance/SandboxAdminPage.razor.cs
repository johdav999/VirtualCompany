using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class SandboxAdminPage : FinancePageBase, IDisposable
{
    private const int DefaultSimulationPollingIntervalMilliseconds = 3000;

    [Inject] private IFinanceSandboxAdminService SandboxAdminService { get; set; } = default!;
    [Inject] private IOptions<FinanceSimulationControlPanelOptions> SimulationOptions { get; set; } = default!;

    private readonly SeedGenerationFormModel _seedGenerationForm = new();
    private IReadOnlyList<GenerationModeOption> _generationModeOptions =>
    [
        new(FinanceSandboxSeedGenerationModes.Refresh, FinanceText["SandboxReplaceDataset"], FinanceText["SandboxReplaceDatasetDescription"]),
        new(FinanceSandboxSeedGenerationModes.RefreshWithAnomalies, FinanceText["SandboxReplaceDatasetWithAnomalies"], FinanceText["SandboxReplaceDatasetWithAnomaliesDescription"])
    ];
    private EditContext _seedGenerationEditContext = default!;
    private EditContext _anomalyInjectionEditContext = default!;
    private EditContext _simulationControlsEditContext = default!;
    private ValidationMessageStore _seedGenerationMessageStore = default!;
    private ValidationMessageStore _anomalyInjectionMessageStore = default!;
    private ValidationMessageStore _simulationControlsMessageStore = default!;
    private FinanceSandboxSeedGenerationViewModel? _seedGenerationResult;
    private string? _seedGenerationErrorMessage;
    private readonly AnomalyInjectionFormModel _anomalyInjectionForm = new();
    private string? _anomalyInjectionErrorMessage;
    private string? _anomalyInjectionSuccessMessage;
    private bool _isSubmittingAnomalyInjection;
    private Guid? _selectedAnomalyId;
    private FinanceSandboxAnomalyDetailViewModel? _selectedAnomalyDetail;
    private bool _isLoadingSelectedAnomaly;
    private string? _selectedAnomalyErrorMessage;
    private readonly SimulationControlsFormModel _simulationControlsForm = new();
    private string? _simulationActionErrorMessage;
    private string? _simulationActionSuccessMessage;
    private bool _isAdvancingSimulation;
    private bool _isStartingProgressionRun;
    private bool _isChangingSimulationLifecycle;
    private string? _simulationLifecycleErrorMessage;
    private string? _simulationLifecycleSuccessMessage;
    private FinanceSandboxProgressionRunViewModel? _latestProgressionRun;
    private FinanceSandboxSimulationControlsViewModel? _latestSimulationControls;
    private readonly List<CompanyOption> _companyOptions = [];
    private bool _isSubmittingSeedGeneration;
    private bool _isRefreshingSimulationControls;
    private bool _isPollingSimulationControls;
    private string? _simulationRefreshErrorMessage;
    private DateTime? _lastSimulationRefreshUtc;
    private Guid? _simulationPollingCompanyId;
    private CancellationTokenSource? _simulationPollingCts;
    private int _datasetGenerationSectionVersion;
    private int _anomalyInjectionSectionVersion;
    private int _simulationDiagnosticsSectionVersion;
    private string DatasetGenerationSectionKey => $"dataset-generation:{_datasetGenerationSectionVersion}";
    private string AnomalyInjectionSectionKey => $"anomaly-injection:{_anomalyInjectionSectionVersion}";
    private string SimulationDiagnosticsSectionKey => $"simulation-diagnostics:{_simulationDiagnosticsSectionVersion}";

    [Parameter]
    public int SimulationPollingIntervalMilliseconds { get; set; } = DefaultSimulationPollingIntervalMilliseconds;

    private bool SimulationUiVisible => SimulationOptions.Value.UiVisible;

    private bool HasActiveProgressionRun => IsNonTerminalProgressionStatus((_latestProgressionRun ?? _latestSimulationControls?.CurrentRun)?.Status);

    private bool HasSandboxAdminAccess =>
        AccessState.IsAllowed &&
        FinanceAccess.CanAccessSandboxAdmin(AccessState.MembershipRole);

    private string BuildFinanceHomeHref() =>
        FinanceRoutes.WithCompanyContext(FinanceRoutes.Home, AccessState.CompanyId);

    private string SelectedGenerationModeDescription =>
        _generationModeOptions.FirstOrDefault(option => string.Equals(option.Value, _seedGenerationForm.GenerationMode, StringComparison.OrdinalIgnoreCase))?.Description
        ?? FinanceText["SandboxSelectGenerationModeHelp"];

    private string CompanySelectionDescription =>
        _companyOptions.Count > 1
            ? FinanceText["SandboxChangingCompanyHelp"]
            : FinanceText["SandboxActiveCompanyHelp"];

    private bool IsSimulationBusy => _isAdvancingSimulation || _isStartingProgressionRun || _isChangingSimulationLifecycle;

    protected override void OnInitialized()
    {
        _seedGenerationEditContext = new EditContext(_seedGenerationForm);
        _seedGenerationMessageStore = new ValidationMessageStore(_seedGenerationEditContext);
        _seedGenerationEditContext.OnValidationRequested += HandleSeedGenerationValidationRequested;
        _seedGenerationEditContext.OnFieldChanged += HandleSeedGenerationFieldChanged;

        _anomalyInjectionEditContext = new EditContext(_anomalyInjectionForm);
        _anomalyInjectionMessageStore = new ValidationMessageStore(_anomalyInjectionEditContext);
        _anomalyInjectionEditContext.OnValidationRequested += HandleAnomalyInjectionValidationRequested;
        _anomalyInjectionEditContext.OnFieldChanged += HandleAnomalyInjectionFieldChanged;

        _simulationControlsEditContext = new EditContext(_simulationControlsForm);
        _simulationControlsMessageStore = new ValidationMessageStore(_simulationControlsEditContext);
        _simulationControlsEditContext.OnValidationRequested += HandleSimulationControlsValidationRequested;
        _simulationControlsEditContext.OnFieldChanged += HandleSimulationControlsFieldChanged;
        _simulationControlsForm.AdvanceHours = 24;
        _simulationControlsForm.ProgressionRunHours = 24;
        _simulationControlsForm.ExecutionStepHours = 24;
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        _companyOptions.Clear();
        _companyOptions.AddRange(GetSandboxAdminCompanyOptions());

        if (AccessState.CompanyId is Guid companyId)
        {
            var companyChanged = _seedGenerationForm.CompanyId != Guid.Empty && _seedGenerationForm.CompanyId != companyId;
            _seedGenerationForm.CompanyId = companyId;
            _seedGenerationForm.CompanyName = ResolveCompanyName(companyId);

            if (companyChanged)
            {
                _seedGenerationResult = null;
                _seedGenerationErrorMessage = null;
                _anomalyInjectionForm.CompanyId = companyId;
                _anomalyInjectionForm.ScenarioProfileCode = string.Empty;
                _anomalyInjectionErrorMessage = null;
                _anomalyInjectionSuccessMessage = null;
                _selectedAnomalyId = null;
                _selectedAnomalyDetail = null;
                _simulationActionErrorMessage = null;
                _simulationActionSuccessMessage = null;
                _simulationLifecycleErrorMessage = null;
                _simulationLifecycleSuccessMessage = null;
                _simulationRefreshErrorMessage = null;
                _latestProgressionRun = null;
                _latestSimulationControls = null;
                _lastSimulationRefreshUtc = null;
                ClearSeedGenerationValidation();
                StopSimulationPolling();
            }
        }
        else if (_companyOptions.Count > 0 && _seedGenerationForm.CompanyId == Guid.Empty)
        {
            await HandleCompanySelectionChangedAsync(_companyOptions[0].CompanyId);
        }

        if (_seedGenerationForm.AnchorDateUtc == default)
        {
            _seedGenerationForm.AnchorDateUtc = DateTime.UtcNow.Date;
            _simulationControlsForm.AdvanceHours = 24;
            _simulationControlsForm.ProgressionRunHours = 24;
        }
    }

    private Task HandleCompanySelectionChangedAsync(Guid companyId)
    {
        if (_isSubmittingSeedGeneration || companyId == Guid.Empty || companyId == _seedGenerationForm.CompanyId)
        {
            return Task.CompletedTask;
        }

        var selectedCompany = _companyOptions.FirstOrDefault(option => option.CompanyId == companyId);
        _seedGenerationForm.CompanyId = companyId;
        _seedGenerationForm.CompanyName = selectedCompany?.CompanyName ?? ResolveCompanyName(companyId);
        _seedGenerationResult = null;
        _seedGenerationErrorMessage = null;
        _anomalyInjectionForm.CompanyId = companyId;
        _anomalyInjectionForm.ScenarioProfileCode = string.Empty;
        _anomalyInjectionErrorMessage = null;
        _anomalyInjectionSuccessMessage = null;
        _selectedAnomalyId = null;
        _selectedAnomalyDetail = null;
        _simulationActionErrorMessage = null;
        _simulationActionSuccessMessage = null;
        _simulationLifecycleErrorMessage = null;
        _simulationLifecycleSuccessMessage = null;
        _simulationRefreshErrorMessage = null;
        _latestProgressionRun = null;
        _latestSimulationControls = null;
        _lastSimulationRefreshUtc = null;
        ClearSeedGenerationValidation();
        StopSimulationPolling();

        Navigation.NavigateTo(FinanceRoutes.WithCompanyContext(FinanceRoutes.SandboxAdmin, companyId));
        return Task.CompletedTask;
    }

    private string GetGenerationModeLabel(string value) =>
        _generationModeOptions.FirstOrDefault(option => string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))?.Label
        ?? value;

    private async Task HandleSeedGenerationSubmitAsync()
    {
        if (_isSubmittingSeedGeneration || _seedGenerationForm.CompanyId == Guid.Empty)
        {
            return;
        }

        var companyId = _seedGenerationForm.CompanyId;
        _seedGenerationForm.CompanyName = ResolveCompanyName(companyId);

        if (_isSubmittingSeedGeneration)
        {
            return;
        }

        ClearSeedGenerationValidation();
        _seedGenerationResult = null;
        _seedGenerationErrorMessage = null;
        _isSubmittingSeedGeneration = true;

        try
        {
            var result = await SandboxAdminService.GenerateSeedDatasetAsync(
                new FinanceSandboxSeedGenerationCommand
                {
                    CompanyId = companyId,
                    SeedValue = _seedGenerationForm.SeedValue,
                    AnchorDateUtc = NormalizeAnchorDate(_seedGenerationForm.AnchorDateUtc),
                    GenerationMode = _seedGenerationForm.GenerationMode
                });

            _seedGenerationResult = result;
            if (result.Succeeded)
            {
                _datasetGenerationSectionVersion++;
                _simulationDiagnosticsSectionVersion++;
            }
        }
        catch (FinanceApiValidationException ex)
        {
            ApplySeedGenerationValidation(ex.Errors);
            _seedGenerationErrorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? FinanceText["SandboxDatasetRejected"]
                : ex.Message;
        }
        catch (FinanceApiException ex)
        {
            _seedGenerationErrorMessage = ex.Message;
        }
        finally
        {
            _isSubmittingSeedGeneration = false;
        }
    }

    private void HandleSeedGenerationValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        _seedGenerationMessageStore.Clear();
        if (_seedGenerationForm.SeedValue <= 0)
            _seedGenerationMessageStore.Add(new FieldIdentifier(_seedGenerationForm, nameof(SeedGenerationFormModel.SeedValue)), FinanceText["SandboxValidationPositiveSeed"]);
        if (_seedGenerationForm.CompanyId == Guid.Empty)
            _seedGenerationMessageStore.Add(new FieldIdentifier(_seedGenerationForm, nameof(SeedGenerationFormModel.CompanyId)), FinanceText["SandboxValidationSelectCompany"]);
        if (_seedGenerationForm.AnchorDateUtc == default)
            _seedGenerationMessageStore.Add(new FieldIdentifier(_seedGenerationForm, nameof(SeedGenerationFormModel.AnchorDateUtc)), FinanceText["SandboxValidationSelectDate"]);
        if (!FinanceSandboxSeedGenerationModes.IsSupported(_seedGenerationForm.GenerationMode))
            _seedGenerationMessageStore.Add(new FieldIdentifier(_seedGenerationForm, nameof(SeedGenerationFormModel.GenerationMode)), FinanceText["SandboxValidationGenerationMode"]);
    }

    private void HandleSeedGenerationFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        _seedGenerationMessageStore.Clear(e.FieldIdentifier);
        _seedGenerationEditContext.NotifyValidationStateChanged();
    }

    private void HandleAnomalyInjectionValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        _anomalyInjectionMessageStore.Clear();
        if (_anomalyInjectionForm.CompanyId == Guid.Empty)
            _anomalyInjectionMessageStore.Add(new FieldIdentifier(_anomalyInjectionForm, nameof(AnomalyInjectionFormModel.CompanyId)), FinanceText["SandboxValidationAnomalyCompany"]);
        if (string.IsNullOrWhiteSpace(_anomalyInjectionForm.ScenarioProfileCode))
            _anomalyInjectionMessageStore.Add(new FieldIdentifier(_anomalyInjectionForm, nameof(AnomalyInjectionFormModel.ScenarioProfileCode)), FinanceText["SandboxValidationScenario"]);
    }

    private void HandleAnomalyInjectionFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        _anomalyInjectionMessageStore.Clear(e.FieldIdentifier);
        _anomalyInjectionEditContext.NotifyValidationStateChanged();
    }

    private void HandleSimulationControlsValidationRequested(object? sender, ValidationRequestedEventArgs e)
    {
        _simulationControlsMessageStore.Clear();
        if (_simulationControlsForm.AdvanceHours is < 1 or > 720)
            _simulationControlsMessageStore.Add(new FieldIdentifier(_simulationControlsForm, nameof(SimulationControlsFormModel.AdvanceHours)), FinanceText["SandboxValidationAdvanceIncrement"]);
        if (_simulationControlsForm.ProgressionRunHours is < 1 or > 720)
            _simulationControlsMessageStore.Add(new FieldIdentifier(_simulationControlsForm, nameof(SimulationControlsFormModel.ProgressionRunHours)), FinanceText["SandboxValidationProgressionDuration"]);
        if (_simulationControlsForm.ExecutionStepHours is < 1 or > 168)
            _simulationControlsMessageStore.Add(new FieldIdentifier(_simulationControlsForm, nameof(SimulationControlsFormModel.ExecutionStepHours)), FinanceText["SandboxValidationExecutionStep"]);
    }

    private void HandleSimulationControlsFieldChanged(object? sender, FieldChangedEventArgs e)
    {
        _simulationControlsMessageStore.Clear(e.FieldIdentifier);
        _simulationControlsEditContext.NotifyValidationStateChanged();
    }

    private void ClearAnomalyInjectionValidation()
    {
        _anomalyInjectionMessageStore.Clear();
        _anomalyInjectionEditContext.NotifyValidationStateChanged();
    }

    private async Task HandleAnomalyInjectionSubmitAsync()
    {
        if (_isSubmittingAnomalyInjection || _anomalyInjectionForm.CompanyId == Guid.Empty)
        {
            return;
        }

        _anomalyInjectionErrorMessage = null;
        _anomalyInjectionSuccessMessage = null;
        _selectedAnomalyErrorMessage = null;
        _selectedAnomalyDetail = null;
        _selectedAnomalyId = null;
        ClearAnomalyInjectionValidation();
        _isSubmittingAnomalyInjection = true;

        try
        {
            var detail = await SandboxAdminService.InjectAnomalyAsync(new FinanceSandboxAnomalyInjectionCommand
            {
                CompanyId = _anomalyInjectionForm.CompanyId,
                ScenarioProfileCode = _anomalyInjectionForm.ScenarioProfileCode
            });

            _selectedAnomalyId = detail.Id;
            _selectedAnomalyDetail = detail;
            _anomalyInjectionSuccessMessage = FinanceText["SandboxScenarioRegistered", detail.ScenarioProfileName];
            _anomalyInjectionSectionVersion++;
        }
        catch (FinanceApiValidationException ex)
        {
            ApplyAnomalyInjectionValidation(ex.Errors);
            _anomalyInjectionErrorMessage = string.IsNullOrWhiteSpace(ex.Message)
                ? FinanceText["SandboxAnomalyRejected"]
                : ex.Message;
        }
        catch (FinanceApiException ex)
        {
            _anomalyInjectionErrorMessage = ex.Message;
        }
        finally
        {
            _isSubmittingAnomalyInjection = false;
        }
    }

    private async Task HandleAnomalySelectionAsync(Guid anomalyId)
    {
        if (AccessState.CompanyId is not Guid companyId || anomalyId == Guid.Empty)
        {
            return;
        }

        _selectedAnomalyId = anomalyId;
        _selectedAnomalyErrorMessage = null;
        _isLoadingSelectedAnomaly = true;

        try
        {
            _selectedAnomalyDetail = await SandboxAdminService.GetAnomalyDetailAsync(companyId, anomalyId);
        }
        catch (FinanceApiException ex)
        {
            _selectedAnomalyDetail = null;
            _selectedAnomalyErrorMessage = ex.Message;
        }
        finally
        {
            _isLoadingSelectedAnomaly = false;
        }
    }

    private async Task HandleAdvanceSimulationAsync()
    {
        if (!_simulationControlsEditContext.Validate() || AccessState.CompanyId is not Guid companyId || IsSimulationBusy)
        {
            return;
        }

        await ExecuteSimulationAdvanceAsync(companyId, _simulationControlsForm.AdvanceHours, "advance");
    }

    private async Task HandleStartProgressionRunAsync()
    {
        if (!_simulationControlsEditContext.Validate() || AccessState.CompanyId is not Guid companyId || IsSimulationBusy)
        {
            return;
        }

        await ExecuteSimulationAdvanceAsync(companyId, _simulationControlsForm.ProgressionRunHours, "progression_run");
    }

    private async Task HandleRefreshSimulationControlsAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || _isRefreshingSimulationControls)
        {
            return;
        }
        if (!SimulationUiVisible)
        {
            return;
        }

        await RefreshSimulationControlsAsync(companyId, CancellationToken.None);
    }

    private void ClearSeedGenerationValidation()
    {
        _seedGenerationMessageStore.Clear();
        _seedGenerationEditContext.NotifyValidationStateChanged();
    }

    private static DateTime NormalizeAnchorDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private async Task ExecuteSimulationAdvanceAsync(Guid companyId, int incrementHours, string runType)
    {
        _simulationActionErrorMessage = null;
        _simulationActionSuccessMessage = null;

        if (string.Equals(runType, "advance", StringComparison.OrdinalIgnoreCase))
        {
            _isAdvancingSimulation = true;
        }
        else
        {
            _isStartingProgressionRun = true;
        }

        try
        {
            var command = new FinanceSandboxSimulationAdvanceCommand
            {
                CompanyId = companyId,
                IncrementHours = incrementHours,
                ExecutionStepHours = _simulationControlsForm.ExecutionStepHours,
                Accelerated = _simulationControlsForm.Accelerated
            };

            _latestProgressionRun = string.Equals(runType, "advance", StringComparison.OrdinalIgnoreCase)
                ? await SandboxAdminService.AdvanceSimulationAsync(command)
                : await SandboxAdminService.StartProgressionRunAsync(command);

            _simulationRefreshErrorMessage = null;
            UpdateSimulationPolling(companyId, _latestProgressionRun);
            await RefreshSimulationControlsAsync(companyId, CancellationToken.None);

            _simulationActionSuccessMessage = BuildSimulationSuccessMessage(
                runType,
                incrementHours,
                _latestProgressionRun?.Status);

            _anomalyInjectionSectionVersion++;
        }
        catch (FinanceApiException ex)
        {
            _simulationActionErrorMessage = ex.Message;
        }
        finally
        {
            _isAdvancingSimulation = false;
            _isStartingProgressionRun = false;
        }
    }

    private void ApplyAnomalyInjectionValidation(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (key, messages) in errors)
        {
            var fieldName = key.Contains(nameof(FinanceSandboxAnomalyInjectionRequest.ScenarioProfileCode), StringComparison.OrdinalIgnoreCase)
                ? nameof(AnomalyInjectionFormModel.ScenarioProfileCode)
                : string.Empty;
            var fieldIdentifier = string.IsNullOrWhiteSpace(fieldName)
                ? new FieldIdentifier(_anomalyInjectionForm, string.Empty)
                : new FieldIdentifier(_anomalyInjectionForm, fieldName);
            _anomalyInjectionMessageStore.Add(fieldIdentifier, messages);
        }

        _anomalyInjectionEditContext.NotifyValidationStateChanged();
    }

    private string SelectedScenarioProfileDescription(FinanceSandboxAnomalyInjectionViewModel injection) =>
        injection.AvailableScenarioProfiles.FirstOrDefault(profile => string.Equals(profile.Code, _anomalyInjectionForm.ScenarioProfileCode, StringComparison.OrdinalIgnoreCase))?.Description
        ?? FinanceText["SandboxChooseScenarioHelp"];

    private async Task HandleStartOrResumeSimulationAsync(FinanceSandboxSimulationDiagnosticsViewModel diagnostics)
    {
        if (AccessState.CompanyId is not Guid companyId || _isChangingSimulationLifecycle)
        {
            return;
        }

        if (string.Equals(diagnostics.Status, FinanceCompanySimulationStatusValues.Paused, StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteCompanySimulationLifecycleActionAsync(
                () => SandboxAdminService.ResumeCompanySimulationAsync(companyId),
                FinanceText["SandboxSimulationResumed"]);
            return;
        }

        await ExecuteCompanySimulationLifecycleActionAsync(
            () => SandboxAdminService.StartCompanySimulationAsync(new FinanceSandboxCompanySimulationStartCommand
            {
                CompanyId = companyId,
                StartSimulatedDateTime = NormalizeAnchorDate(_seedGenerationForm.AnchorDateUtc == default ? DateTime.UtcNow.Date : _seedGenerationForm.AnchorDateUtc),
                GenerationEnabled = diagnostics.GenerationEnabled ?? true,
                Seed = _seedGenerationForm.SeedValue
            }),
            FinanceText["SandboxSimulationStarted"]);
    }

    private Task HandlePauseSimulationAsync() =>
        AccessState.CompanyId is Guid companyId
            ? ExecuteCompanySimulationLifecycleActionAsync(
                () => SandboxAdminService.PauseCompanySimulationAsync(companyId),
                FinanceText["SandboxSimulationPaused"])
            : Task.CompletedTask;

    private Task HandleStopSimulationAsync() =>
        AccessState.CompanyId is Guid companyId
            ? ExecuteCompanySimulationLifecycleActionAsync(
                () => SandboxAdminService.StopCompanySimulationAsync(companyId),
                FinanceText["SandboxSimulationStopped"])
            : Task.CompletedTask;

    private Task HandleStepForwardOneDayAsync() =>
        AccessState.CompanyId is Guid companyId
            ? ExecuteCompanySimulationLifecycleActionAsync(
                () => SandboxAdminService.StepForwardCompanySimulationAsync(companyId),
                FinanceText["SandboxSimulationAdvancedDay"])
            : Task.CompletedTask;

    private Task HandleToggleGenerationAsync(FinanceSandboxSimulationDiagnosticsViewModel diagnostics)
    {
        if (AccessState.CompanyId is not Guid companyId || !diagnostics.GenerationEnabled.HasValue)
        {
            return Task.CompletedTask;
        }

        var nextValue = !diagnostics.GenerationEnabled.Value;
        return ExecuteCompanySimulationLifecycleActionAsync(
            () => SandboxAdminService.UpdateCompanySimulationGenerationAsync(companyId, nextValue),
            nextValue ? FinanceText["SandboxGenerationEnabled"] : FinanceText["SandboxGenerationDisabled"]);
    }

    private async Task ExecuteCompanySimulationLifecycleActionAsync(
        Func<Task<FinanceSandboxSimulationDiagnosticsViewModel>> action,
        string successMessage)
    {
        if (_isChangingSimulationLifecycle)
        {
            return;
        }

        _simulationLifecycleErrorMessage = null;
        _simulationLifecycleSuccessMessage = null;
        _isChangingSimulationLifecycle = true;

        try
        {
            _ = await action();
            _simulationLifecycleSuccessMessage = successMessage;
            _simulationDiagnosticsSectionVersion++;
            _anomalyInjectionSectionVersion++;

            if (AccessState.CompanyId is Guid companyId)
            {
                await RefreshSimulationControlsAsync(companyId, CancellationToken.None);
            }
        }
        catch (FinanceApiException ex)
        {
            _simulationLifecycleErrorMessage = ex.Message;
        }
        finally
        {
            _isChangingSimulationLifecycle = false;
        }
    }

    private string ResolveBackendMessageAlertClass(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "failure" or "error" => "alert alert-danger mb-2",
            "warning" => "alert alert-warning mb-2",
            _ => "alert alert-info mb-2"
        };

    private static string ResolveSimulationLifecycleBadgeClass(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            FinanceCompanySimulationStatusValues.Running => "badge text-bg-success",
            FinanceCompanySimulationStatusValues.Paused => "badge text-bg-warning",
            FinanceCompanySimulationStatusValues.Stopped => "badge text-bg-secondary",
            _ => "badge text-bg-secondary"
        };

    private string ResolveSimulationLifecycleLabel(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            FinanceCompanySimulationStatusValues.Running => FinanceText["SandboxRunning"],
            FinanceCompanySimulationStatusValues.Paused => FinanceText["SandboxPaused"],
            FinanceCompanySimulationStatusValues.Stopped => FinanceText["SandboxStopped"],
            _ => FinanceText["SandboxNotStarted"]
        };

    private static bool IsPausedSimulation(string? status) =>
        string.Equals(status, FinanceCompanySimulationStatusValues.Paused, StringComparison.OrdinalIgnoreCase);

    private string FormatPlainLabel(string? value)
    {
        var formatted = FinanceAnomalyPresentation.FormatLabel(value);
        return string.IsNullOrWhiteSpace(formatted)
            ? FinanceText["None"]
            : formatted;
    }

    private string FormatProfileName(string? value) =>
        FormatPlainLabel(value);

    private string FormatAnomalyList(IReadOnlyList<string> values) =>
        values.Count == 0
            ? FinanceText["None"]
            : string.Join(", ", values.Select(FormatPlainLabel));

    private string FormatMessageList(IReadOnlyList<string> values) =>
        values.Count == 0
            ? FinanceText["None"]
            : string.Join(" | ", values.Select(FormatPlainLabel));

    private string FormatUtc(DateTime? value, string? fallback = null) =>
        value.HasValue ? LocalDateTime.DateTime(value.Value) : fallback ?? FinanceText["SandboxUnavailable"];

    private string FormatSimulationSnapshotMessage(
        IReadOnlyList<string> anomalies,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors)
    {
        var segments = new List<string>();
        if (anomalies.Count > 0)
        {
            segments.Add(FinanceText["SandboxSnapshotAnomalies", FormatAnomalyList(anomalies)]);
        }

        if (warnings.Count > 0)
        {
            segments.Add(FinanceText["SandboxSnapshotWarnings", FormatMessageList(warnings)]);
        }

        if (errors.Count > 0)
        {
            segments.Add(FinanceText["SandboxSnapshotErrors", FormatMessageList(errors)]);
        }

        return segments.Count == 0
            ? FinanceText["SandboxSnapshotClear"]
            : string.Join(" ", segments);
    }

    private static string ResolveSimulationSnapshotAlertClass(FinanceSandboxSimulationRunHistoryViewModel? run) =>
        run is null
            ? "alert alert-info mb-0"
            : run.Errors.Count > 0
                ? "alert alert-danger mb-0"
                : run.Warnings.Count > 0
                    ? "alert alert-warning mb-0"
                    : run.InjectedAnomalies.Count > 0
                        ? "alert alert-info mb-0"
                        : "alert alert-info mb-0";

    private string? BuildAffectedRecordHref(FinanceSandboxAnomalyDetailViewModel detail) =>
        detail.AffectedRecordId is not Guid recordId || recordId == Guid.Empty
            ? null
            : detail.AffectedRecordType.Trim().ToLowerInvariant() switch
            {
                "transaction" => FinanceRoutes.BuildTransactionDetailPath(recordId, AccessState.CompanyId),
                "invoice" => FinanceRoutes.BuildInvoiceDetailPath(recordId, AccessState.CompanyId),
                _ => null
            };

    private Task<FinanceSandboxDatasetGenerationViewModel?> LoadDatasetGenerationAsync(Guid companyId, CancellationToken cancellationToken) =>
        SandboxAdminService.GetDatasetGenerationAsync(companyId, cancellationToken);

    private Task<FinanceSandboxAnomalyInjectionViewModel?> LoadAnomalyInjectionAsync(Guid companyId, CancellationToken cancellationToken) =>
        SandboxAdminService.GetAnomalyInjectionAsync(companyId, cancellationToken);

    private Task<FinanceSandboxSimulationDiagnosticsViewModel?> LoadSimulationDiagnosticsAsync(Guid companyId, CancellationToken cancellationToken) =>
        SandboxAdminService.GetSimulationDiagnosticsAsync(companyId, cancellationToken);

    private async Task<FinanceSandboxSimulationControlsViewModel?> LoadSimulationControlsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var simulation = await SandboxAdminService.GetSimulationControlsAsync(companyId, cancellationToken);
        ApplySimulationControlsSnapshot(companyId, simulation);
        return simulation;
    }

    private async Task RefreshSimulationControlsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (_isRefreshingSimulationControls)
        {
            return;
        }

        _isRefreshingSimulationControls = true;

        try
        {
            var simulation = await SandboxAdminService.GetSimulationControlsAsync(companyId, cancellationToken);
            ApplySimulationControlsSnapshot(companyId, simulation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (FinanceApiException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _simulationRefreshErrorMessage = ex.Message;
        }
        finally
        {
            _isRefreshingSimulationControls = false;

            if (!cancellationToken.IsCancellationRequested)
            {
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    private void ApplySimulationControlsSnapshot(Guid companyId, FinanceSandboxSimulationControlsViewModel? simulation)
    {
        _latestSimulationControls = simulation;
        _latestProgressionRun = simulation?.CurrentRun ?? simulation?.RunHistory.FirstOrDefault();
        _simulationRefreshErrorMessage = null;
        _lastSimulationRefreshUtc = DateTime.UtcNow;
        UpdateSimulationPolling(companyId, _latestProgressionRun);
    }

    private void UpdateSimulationPolling(Guid companyId, FinanceSandboxProgressionRunViewModel? latestRun)
    {
        if (IsNonTerminalProgressionStatus(latestRun?.Status))
        {
            EnsureSimulationPolling(companyId);
            return;
        }

        StopSimulationPolling();
    }

    private void EnsureSimulationPolling(Guid companyId)
    {
        if (_isPollingSimulationControls && _simulationPollingCompanyId == companyId)
        {
            return;
        }

        StopSimulationPolling();

        _simulationPollingCompanyId = companyId;
        _simulationPollingCts = new CancellationTokenSource();
        _isPollingSimulationControls = true;

        // Poll in the background until the latest run reaches a terminal backend state.
        _ = PollSimulationControlsAsync(companyId, _simulationPollingCts.Token);
    }

    private async Task PollSimulationControlsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(10, SimulationPollingIntervalMilliseconds)));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshSimulationControlsAsync(companyId, cancellationToken);

                if (!IsNonTerminalProgressionStatus(_latestProgressionRun?.Status))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (_simulationPollingCompanyId == companyId && !IsNonTerminalProgressionStatus(_latestProgressionRun?.Status))
            {
                StopSimulationPolling();
            }
        }
    }

    private void StopSimulationPolling()
    {
        var pollingCts = Interlocked.Exchange(ref _simulationPollingCts, null);
        pollingCts?.Cancel();
        pollingCts?.Dispose();
        _simulationPollingCompanyId = null;
        _isPollingSimulationControls = false;
    }

    private string BuildSimulationSuccessMessage(string runType, int incrementHours, string? status) =>
        string.Equals(runType, "advance", StringComparison.OrdinalIgnoreCase)
            ? FinanceText["SandboxAdvancedHoursMessage", incrementHours]
            : IsNonTerminalProgressionStatus(status)
                ? FinanceText["SandboxProgressionStartedMessage", incrementHours]
                : FinanceText["SandboxProgressionCompletedMessage", incrementHours];

    private static bool IsNonTerminalProgressionStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status) && !IsTerminalProgressionStatus(status);

    private static bool IsTerminalProgressionStatus(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            null or "" => false,
            "completed" or "complete" or "succeeded" or "success" or "completed_with_warnings" or "completed-with-warnings" or "warning" or "warnings" or "failed" or "failure" or "error" or "cancelled" or "canceled" => true,
            _ => false
        };

    private IReadOnlyList<CompanyOption> GetSandboxAdminCompanyOptions() =>
        (CurrentUserContext?.Memberships ?? [])
            .Where(membership =>
                string.Equals(membership.Status, "active", StringComparison.OrdinalIgnoreCase) &&
                FinanceAccess.CanAccessSandboxAdmin(membership.MembershipRole))
            .GroupBy(membership => membership.CompanyId)
            .Select(group => group.First())
            .OrderBy(membership => membership.CompanyName, StringComparer.OrdinalIgnoreCase)
            .Select(membership => new CompanyOption(
                membership.CompanyId,
                membership.CompanyName,
                membership.MembershipRole))
            .ToArray();

    private string ResolveCompanyName(Guid companyId) =>
        _companyOptions.FirstOrDefault(option => option.CompanyId == companyId)?.CompanyName
        ?? AccessState.CompanyName ?? FinanceText["SandboxActiveCompany"];

    private Task<FinanceSandboxToolExecutionVisibilityViewModel?> LoadToolExecutionVisibilityAsync(Guid companyId, CancellationToken cancellationToken) =>
        SandboxAdminService.GetToolExecutionVisibilityAsync(companyId, cancellationToken);

    private Task<FinanceSandboxDomainEventsViewModel?> LoadDomainEventsAsync(Guid companyId, CancellationToken cancellationToken) =>
        SandboxAdminService.GetDomainEventsAsync(companyId, cancellationToken);

    private void ApplySeedGenerationValidation(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (key, messages) in errors)
        {
            var fieldName = ResolveSeedGenerationFieldName(key);
            var fieldIdentifier = string.IsNullOrWhiteSpace(fieldName)
                ? new FieldIdentifier(_seedGenerationForm, string.Empty)
                : new FieldIdentifier(_seedGenerationForm, fieldName);
            _seedGenerationMessageStore.Add(fieldIdentifier, messages);
        }

        _seedGenerationEditContext.NotifyValidationStateChanged();
    }

    private static string ResolveSeedGenerationFieldName(string key)
    {
        if (key.Contains(nameof(FinanceSandboxSeedGenerationRequest.CompanyId), StringComparison.OrdinalIgnoreCase))
        {
            return nameof(SeedGenerationFormModel.CompanyId);
        }

        if (key.Contains(nameof(FinanceSandboxSeedGenerationRequest.SeedValue), StringComparison.OrdinalIgnoreCase))
        {
            return nameof(SeedGenerationFormModel.SeedValue);
        }

        if (key.Contains(nameof(FinanceSandboxSeedGenerationRequest.AnchorDateUtc), StringComparison.OrdinalIgnoreCase) ||
            key.Contains("SeedAnchorUtc", StringComparison.OrdinalIgnoreCase))
        {
            return nameof(SeedGenerationFormModel.AnchorDateUtc);
        }

        if (key.Contains(nameof(FinanceSandboxSeedGenerationRequest.GenerationMode), StringComparison.OrdinalIgnoreCase))
        {
            return nameof(SeedGenerationFormModel.GenerationMode);
        }

        return string.Empty;
    }

    public void Dispose()
    {
        _seedGenerationEditContext.OnValidationRequested -= HandleSeedGenerationValidationRequested;
        _seedGenerationEditContext.OnFieldChanged -= HandleSeedGenerationFieldChanged;
        _anomalyInjectionEditContext.OnValidationRequested -= HandleAnomalyInjectionValidationRequested;
        _anomalyInjectionEditContext.OnFieldChanged -= HandleAnomalyInjectionFieldChanged;
        _simulationControlsEditContext.OnValidationRequested -= HandleSimulationControlsValidationRequested;
        _simulationControlsEditContext.OnFieldChanged -= HandleSimulationControlsFieldChanged;
        StopSimulationPolling();
    }

    private sealed class SeedGenerationFormModel
    {
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;

        public int SeedValue { get; set; } = 302;

        public DateTime AnchorDateUtc { get; set; }

        public string GenerationMode { get; set; } = FinanceSandboxSeedGenerationModes.Refresh;
    }

    private sealed class AnomalyInjectionFormModel
    {
        public Guid CompanyId { get; set; }

        public string ScenarioProfileCode { get; set; } = string.Empty;
    }

    private sealed class SimulationControlsFormModel
    {
        public int AdvanceHours { get; set; } = 24;

        public int ProgressionRunHours { get; set; } = 24;

        public int? ExecutionStepHours { get; set; } = 24;

        public bool Accelerated { get; set; } = true;
    }

    private sealed record GenerationModeOption(
        string Value,
        string Label,
        string Description);

    private sealed record CompanyOption(
        Guid CompanyId,
        string CompanyName,
        string MembershipRole);
}
