using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchCutoverBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AccountingProviderSwitchCutoverWorkerOptions _options;
    private readonly ILogger<AccountingProviderSwitchCutoverBackgroundService> _logger;
    public AccountingProviderSwitchCutoverBackgroundService(IServiceScopeFactory scopeFactory,
        IOptions<AccountingProviderSwitchCutoverWorkerOptions> options,
        ILogger<AccountingProviderSwitchCutoverBackgroundService> logger)
    { _scopeFactory = scopeFactory; _options = options.Value; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds));
        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IAccountingProviderSwitchCutoverJobRunner>()
                    .RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { _logger.LogError(exception, "Accounting provider switch cutover processing failed; durable state remains recoverable."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
