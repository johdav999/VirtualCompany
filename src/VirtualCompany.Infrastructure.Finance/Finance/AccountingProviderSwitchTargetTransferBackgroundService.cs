using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchTargetTransferBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<AccountingProviderSwitchTargetTransferWorkerOptions> options,
    ILogger<AccountingProviderSwitchTargetTransferBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        var delay = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handled = await scope.ServiceProvider.GetRequiredService<IAccountingProviderSwitchTargetTransferJobRunner>()
                    .RunDueAsync(stoppingToken);
                if (handled == 0) await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Accounting provider-switch target transfer worker loop failed.");
                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
