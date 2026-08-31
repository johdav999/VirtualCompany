using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CurrencyRevaluationWorkerOptions
{
    public const string SectionName = "CurrencyRevaluation:Worker";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 900;
}

public sealed class CurrencyRevaluationBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<CurrencyRevaluationWorkerOptions> options,
    ILogger<CurrencyRevaluationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var handled = await scope.ServiceProvider.GetRequiredService<ICurrencyRevaluationService>()
                    .RunScheduledAsync(stoppingToken);
                if (handled > 0)
                    logger.LogInformation("Currency revaluation worker handled {Count} scheduled action(s).", handled);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogError(exception, "The currency revaluation worker cycle failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollIntervalSeconds, 60, 86400)),
                stoppingToken);
        }
    }
}
