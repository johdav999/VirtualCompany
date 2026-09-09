using Microsoft.Extensions.Diagnostics.HealthChecks;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class TeamsMeetingMediaHealthCheck(ITeamsMeetingMediaAdapter media) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var health = await media.GetHealthAsync(cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["enabled"] = health.Enabled,
            ["approved"] = health.Approved,
            ["route"] = health.Route,
            ["sdkVersion"] = health.SdkVersion ?? "unknown",
            ["reasonCode"] = health.ReasonCode ?? "ready"
        };
        if (!health.Enabled) return HealthCheckResult.Healthy("Teams meeting audio is disabled.", data);
        return health.Available
            ? HealthCheckResult.Healthy("The approved Teams meeting-media adapter is ready.", data)
            : HealthCheckResult.Degraded(health.Message ?? "Teams meeting audio is unavailable; typed controls remain available.", data: data);
    }
}
