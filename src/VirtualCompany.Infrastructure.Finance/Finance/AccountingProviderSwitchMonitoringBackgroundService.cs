using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchMonitoringBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AccountingProviderSwitchMonitoringOptions _options;
    private readonly ILogger<AccountingProviderSwitchMonitoringBackgroundService> _logger;
    public AccountingProviderSwitchMonitoringBackgroundService(IServiceScopeFactory scopeFactory,
        IOptions<AccountingProviderSwitchMonitoringOptions> options,
        ILogger<AccountingProviderSwitchMonitoringBackgroundService> logger)
    { _scopeFactory = scopeFactory; _options = options.Value; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        do
        {
            try { await using var scope = _scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IAccountingProviderSwitchMonitoringJobRunner>().RunDueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { _logger.LogError(exception, "Accounting provider-switch monitoring failed; durable state remains recoverable."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
