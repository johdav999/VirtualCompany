using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceAutonomyExecutorOptions
{
    public const string SectionName = "FinanceAutonomyExecutor";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 10;
}

public sealed class FinanceAutonomyExecutorBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<FinanceAutonomyExecutorOptions> options,
    TimeProvider clock,
    ILogger<FinanceAutonomyExecutorBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Finance autonomy executor is disabled.");
            return;
        }
        var interval = TimeSpan.FromSeconds(Math.Clamp(options.Value.PollIntervalSeconds, 2, 3600));
        var workerId = $"{Environment.MachineName}:{Environment.ProcessId}:finance-autonomy";
        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var executor = scope.ServiceProvider.GetRequiredService<IFinanceAutonomyExecutor>();
                var result = await executor.ProcessBatchAsync(clock.GetUtcNow().UtcDateTime, workerId,
                    Math.Clamp(options.Value.BatchSize, 1, 100), stoppingToken);
                if (result.Claimed > 0)
                    logger.LogInformation(
                        "Finance autonomy executor claimed {Claimed}, completed {Completed}, awaiting approval {AwaitingApproval}, retried {Retried}, reconciling {Reconciling}, blocked {Blocked}, and dead-lettered {DeadLettered}.",
                        result.Claimed, result.Completed, result.AwaitingApproval, result.Retried,
                        result.Reconciling, result.Blocked, result.DeadLettered);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Finance autonomy executor poll failed safely."); }
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
