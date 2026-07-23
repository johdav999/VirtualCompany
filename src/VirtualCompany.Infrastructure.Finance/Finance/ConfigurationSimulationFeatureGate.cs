using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SimulationFeatureOptions
{
    public const string SectionName = "SimulationFeatures";

    public bool UiVisible { get; set; } = true;
    public bool BackendExecutionEnabled { get; set; } = true;
    public bool BackgroundJobsEnabled { get; set; } = true;
    public string DisabledMessage { get; set; } = "Simulation is disabled by configuration.";
}

public sealed class ConfigurationSimulationFeatureGate : ISimulationFeatureGate
{
    private readonly IOptions<SimulationFeatureOptions> _options;

    public ConfigurationSimulationFeatureGate(IOptions<SimulationFeatureOptions> options)
    {
        _options = options;
    }

    public SimulationFeatureStateDto GetState() =>
        new(
            _options.Value.UiVisible,
            _options.Value.BackendExecutionEnabled,
            _options.Value.BackgroundJobsEnabled,
            _options.Value.DisabledMessage);

    public bool IsUiVisible() => _options.Value.UiVisible;

    public bool IsBackendExecutionEnabled() => _options.Value.BackendExecutionEnabled;

    public bool AreBackgroundJobsEnabled() => _options.Value.BackgroundJobsEnabled;

    public bool IsBackgroundExecutionAllowed() =>
        _options.Value.BackendExecutionEnabled &&
        _options.Value.BackgroundJobsEnabled;

    public bool IsFullyDisabled() =>
        !_options.Value.UiVisible &&
        !_options.Value.BackendExecutionEnabled &&
        !_options.Value.BackgroundJobsEnabled;

    public void EnsureBackendExecutionEnabled()
    {
        if (!_options.Value.BackendExecutionEnabled)
        {
            throw new SimulationBackendDisabledException(_options.Value.DisabledMessage);
        }
    }

    public void EnsureBackgroundExecutionEnabled()
    {
        if (!IsBackgroundExecutionAllowed())
        {
            throw new SimulationBackendDisabledException(_options.Value.DisabledMessage, isBackgroundExecution: true);
        }
    }
}