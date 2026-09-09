using Microsoft.Extensions.Diagnostics.HealthChecks;
using VirtualCompany.Application.Agents;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class RealtimeAgentGatewayHealthCheck(IRealtimeAgentSessionGateway gateway) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var health = await gateway.GetHealthAsync(cancellationToken);
        var data = new Dictionary<string, object> { ["provider"] = health.Provider, ["model"] = health.Model, ["enabled"] = health.Enabled };
        return health.Available ? HealthCheckResult.Healthy("Shared realtime agent gateway is available.", data)
            : HealthCheckResult.Degraded(health.Message ?? "Shared realtime agent gateway is disabled or unavailable.", data: data);
    }
}
