using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingExportWorkerOptions
{
    public const string SectionName = "AccountingExportWorker";
    public bool Enabled { get; set; } = true;
    public int PollIntervalMilliseconds { get; set; } = 2000;
}

public sealed class AccountingExportBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AccountingExportBackgroundService> _logger;
    private readonly IOptions<AccountingExportWorkerOptions> _options;

    public AccountingExportBackgroundService(IServiceScopeFactory scopeFactory,
        IOptions<AccountingExportWorkerOptions> options, ILogger<AccountingExportBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Accounting export worker is disabled.");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<Application.Finance.IAccountingReportingService>()
                    .RunDueExportsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Accounting export background processing failed. Queued exports remain recoverable.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(100, _options.Value.PollIntervalMilliseconds)), stoppingToken);
        }
    }
}
