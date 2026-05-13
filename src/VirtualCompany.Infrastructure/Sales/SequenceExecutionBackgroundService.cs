using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Sales;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SequenceExecutionWorkerOptions
{
    public const string SectionName = "Sales:SequenceExecutionWorker";
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 50;
}

public sealed class SequenceExecutionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SequenceExecutionWorkerOptions> _options;
    private readonly ILogger<SequenceExecutionBackgroundService> _logger;

    public SequenceExecutionBackgroundService(IServiceScopeFactory scopeFactory, IOptionsMonitor<SequenceExecutionWorkerOptions> options, ILogger<SequenceExecutionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            if (options.Enabled)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<ISequenceExecutionService>();
                    await service.ProcessDueStepsAsync(DateTime.UtcNow, options.BatchSize, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Outbound sequence worker failed while processing due steps.");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.PollSeconds, 5, 300)), stoppingToken);
        }
    }
}