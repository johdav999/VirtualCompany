using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceAutonomyApprovalOptions
{
    public const string SectionName = "FinanceAutonomyApprovals";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 25;
}

public sealed class FinanceAutonomyApprovalBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<FinanceAutonomyApprovalOptions> options,
    TimeProvider clock,
    ILogger<FinanceAutonomyApprovalBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollIntervalSeconds, 2, 3600)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<IFinanceAutonomyApprovalCoordinator>();
                var result = await coordinator.ProcessBatchAsync(clock.GetUtcNow().UtcDateTime,
                    Math.Clamp(options.Value.BatchSize, 1, 100), stoppingToken);
                if (result.Continued + result.Blocked + result.Escalated > 0)
                    logger.LogInformation(
                        "Finance autonomy approvals: {Pending} pending, {Continued} continued, {Blocked} blocked, {Escalated} escalated.",
                        result.Pending, result.Continued, result.Blocked, result.Escalated);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Finance autonomy approval coordination failed safely."); }
            try { await timer.WaitForNextTickAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
