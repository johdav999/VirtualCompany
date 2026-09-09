using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class TeamsCallControlHealthCheck(IOptions<TeamsPresenterOptions> configured) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var o = configured.Value;
        if (!o.Enabled || !o.CallControlEnabled) return Task.FromResult(HealthCheckResult.Healthy("Teams call control is disabled."));
        var valid = string.Equals(o.CallControlProvider, "microsoft_graph", StringComparison.Ordinal) &&
                    Uri.TryCreate(o.BotCallingCallbackUrl, UriKind.Absolute, out var callback) && callback.Scheme == Uri.UriSchemeHttps &&
                    o.MaxActiveCallsPerCompany > 0 && o.MaxActiveCallsPerHost > 0;
        var data = new Dictionary<string, object> { ["provider"] = o.CallControlProvider, ["host"] = o.CallControlHostId,
            ["companyLimit"] = o.MaxActiveCallsPerCompany, ["hostLimit"] = o.MaxActiveCallsPerHost };
        return Task.FromResult(valid
            ? HealthCheckResult.Healthy("Durable Teams call control is configured.", data)
            : HealthCheckResult.Unhealthy("Teams call-control configuration is invalid.", data: data));
    }
}
