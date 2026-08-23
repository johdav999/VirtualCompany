using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchAssessmentWorkerOptions
{
    public const string SectionName = "AccountingProviderSwitchAssessment";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 10;
    public int ClaimBatchSize { get; set; } = 4;
    public int PageSize { get; set; } = 100;
    public int LeaseSeconds { get; set; } = 60;
    public int MaximumAttempts { get; set; } = 4;
}

public sealed class AccountingProviderSwitchAssessmentBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<AccountingProviderSwitchAssessmentWorkerOptions> options,
    ILogger<AccountingProviderSwitchAssessmentBackgroundService> logger) : BackgroundService
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
                var handled = await scope.ServiceProvider.GetRequiredService<IAccountingProviderSwitchAssessmentJobRunner>()
                    .RunDueAsync(stoppingToken);
                if (handled == 0) await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Accounting provider-switch assessment worker loop failed.");
                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
