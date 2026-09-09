using Microsoft.Extensions.Diagnostics.HealthChecks;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingVoiceHealthCheck(
    IRealtimeAgentSessionGateway gateway,
    IMeetingMediaAdapter media) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var provider = await gateway.GetHealthAsync(cancellationToken);
        var route = await media.GetHealthAsync(cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["provider"] = provider.Provider,
            ["providerStatus"] = provider.Status,
            ["mediaRoute"] = route.Route,
            ["mediaStatus"] = route.Status
        };
        return provider.Available && route.Available
            ? HealthCheckResult.Healthy("The optional Sales meeting voice pilot is available.", data)
            : HealthCheckResult.Degraded("The optional Sales meeting voice pilot is unavailable; typed meeting workflows remain healthy.", data: data);
    }
}
