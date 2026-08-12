using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Orchestration;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class OperatingWorkDispatchBackgroundService(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<OperatingWorkDispatchBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IOperatingWorkDispatcher>().RunOnceAsync(10, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Company operating work dispatch failed."); }
        }
    }
}
