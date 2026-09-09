using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class TeamsPresenterHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var readiness = await scope.ServiceProvider.GetRequiredService<ITeamsPresenterReadinessService>()
            .GetReadinessAsync(companyId: null, cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["enabled"] = readiness.Enabled,
            ["packageReady"] = readiness.PackageReady,
            ["liveCallingReady"] = readiness.LiveCallingReady,
            ["mediaRoute"] = readiness.MediaRoute,
            ["blockingChecks"] = readiness.Checks.Where(check => !check.Ready).Select(check => check.Name).ToArray()
        };

        if (!readiness.Enabled)
        {
            return HealthCheckResult.Healthy("Teams presenter capabilities are disabled.", data);
        }

        return readiness.LiveCallingReady
            ? HealthCheckResult.Healthy("Teams presenter live calling is ready.", data)
            : HealthCheckResult.Degraded("Teams presenter is enabled but later permission, call-control, surface, or media-host gates remain incomplete.", data: data);
    }
}
