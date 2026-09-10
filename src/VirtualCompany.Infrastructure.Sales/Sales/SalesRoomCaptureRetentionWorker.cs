using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Sales;
namespace VirtualCompany.Infrastructure.Sales;
internal sealed class SalesRoomCaptureRetentionWorker(IServiceScopeFactory scopes, ILogger<SalesRoomCaptureRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ISalesRoomCaptureService>().PurgeExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { logger.LogWarning("Browser capture retention cleanup failed; exception type {ExceptionType}.", e.GetType().Name); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
