using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class TeamsPresenterStartupValidationService(
    IServiceScopeFactory scopeFactory,
    ILogger<TeamsPresenterStartupValidationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var readiness = await scope.ServiceProvider.GetRequiredService<ITeamsPresenterReadinessService>()
            .GetReadinessAsync(companyId: null, cancellationToken);
        if (!readiness.Enabled || readiness.PackageReady) return;

        logger.LogWarning(
            "Teams presenter configuration was evaluated at startup and remains fail-closed. Blocking checks: {BlockingChecks}.",
            string.Join(',', readiness.Checks.Where(check => !check.Ready).Select(check => check.Name)));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
