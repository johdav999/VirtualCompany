using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingMigrationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<AccountingMigrationWorkerOptions> _options;
    private readonly ILogger<AccountingMigrationBackgroundService> _logger;

    public AccountingMigrationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<AccountingMigrationWorkerOptions> options,
        ILogger<AccountingMigrationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Accounting migration worker is disabled.");
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<IAccountingMigrationJobRunner>();
                var handled = await runner.RunDueAsync(stoppingToken);
                if (handled == 0) await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Accounting migration worker loop failed.");
                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
