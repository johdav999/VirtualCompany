using Microsoft.Extensions.Diagnostics.HealthChecks;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class TeamsMediaHostHealthCheck(ITeamsMediaHostRuntime runtime) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var status = runtime.GetStatus();
        var data = new Dictionary<string, object>
        {
            ["state"] = status.State,
            ["acceptingNewCalls"] = status.AcceptingNewCalls,
            ["activeCalls"] = status.ActiveCalls,
            ["maximumActiveCalls"] = status.MaximumActiveCalls,
            ["sdkVersion"] = status.MediaSdkVersion,
            ["reasonCode"] = status.Checks.FirstOrDefault(check => !check.Ready)?.ReasonCode ?? TeamsMediaHostProblemCodes.Ready
        };
        if (status.State == TeamsMediaHostStates.Disabled)
            return Task.FromResult(HealthCheckResult.Healthy("The optional Teams media host is disabled.", data));
        return Task.FromResult(status.AcceptingNewCalls
            ? HealthCheckResult.Healthy("The Teams media host is accepting new calls.", data)
            : HealthCheckResult.Degraded("The Teams media host is draining, full, or failed a runtime prerequisite.", data: data));
    }
}
