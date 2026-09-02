using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceAutonomyTriggerOptions
{
    public const string SectionName = "FinanceAutonomyTriggers";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 50;
}

public sealed class FinanceAutonomyTriggerBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<FinanceAutonomyTriggerOptions> options,
    TimeProvider clock,
    ILogger<FinanceAutonomyTriggerBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Finance autonomy trigger worker is disabled.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.PollIntervalSeconds, 5, 3600));
        var workerId = $"{Environment.MachineName}:{Environment.ProcessId}:finance-triggers";
        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var triggers = scope.ServiceProvider.GetRequiredService<IFinanceAutonomyTriggerService>();
                var result = await triggers.ProcessDueSchedulesAsync(clock.GetUtcNow().UtcDateTime, workerId,
                    Math.Clamp(options.Value.BatchSize, 1, 100), stoppingToken);
                if (result.Started + result.Coalesced + result.Failed + result.DeadLettered > 0)
                    logger.LogInformation("Finance trigger poll considered {Considered}, started {Started}, coalesced {Coalesced}, failed {Failed}, and dead-lettered {DeadLettered}.",
                        result.Considered, result.Started, result.Coalesced, result.Failed, result.DeadLettered);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Finance autonomy trigger polling failed safely."); }

            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
