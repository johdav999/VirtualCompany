using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Application.Workflows;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class BriefingSchedulerOptions
{
    public const string SectionName = "BriefingScheduler";

    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 900;
    public int LockTtlSeconds { get; set; } = 300;
    public int BatchSize { get; set; } = 50;
    public string LockKey { get; set; } = "briefing-scheduler";
}

public sealed class BriefingSchedulerCoordinator
{
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ICompanyBriefingService _briefingService;
    private readonly IOptions<BriefingSchedulerOptions> _options;
    private readonly ILogger<BriefingSchedulerCoordinator> _logger;

    public BriefingSchedulerCoordinator(
        IDistributedLockProvider lockProvider,
        ICompanyBriefingService briefingService,
        IOptions<BriefingSchedulerOptions> options,
        ILogger<BriefingSchedulerCoordinator> logger)
    {
        _lockProvider = lockProvider;
        _briefingService = briefingService;
        _options = options;
        _logger = logger;
    }

    public async Task<BriefingSchedulerRunResult> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var lockKey = string.IsNullOrWhiteSpace(options.LockKey) ? "briefing-scheduler" : options.LockKey.Trim();
        var lockTtl = TimeSpan.FromSeconds(Math.Max(30, options.LockTtlSeconds));
        await using var handle = await _lockProvider.TryAcquireAsync(lockKey, lockTtl, cancellationToken);
        if (handle is null)
        {
            _logger.LogInformation("Briefing scheduler skipped polling because distributed lock {LockKey} was not acquired.", lockKey);
            return new BriefingSchedulerRunResult(false, 0, 0, 0, 0);
        }

        var result = await _briefingService.GenerateDueAsync(
            new GenerateDueBriefingsCommand(now.UtcDateTime, Math.Max(1, options.BatchSize)),
            cancellationToken);
        _logger.LogInformation(
            "Briefing scheduler completed. Companies scanned: {CompaniesScanned}. Briefings generated: {BriefingsGenerated}. Notifications created: {NotificationsCreated}. Failures: {Failures}.",
            result.CompaniesScanned,
            result.BriefingsGenerated,
            result.NotificationsCreated,
            result.Failures);
        return result;
    }
}

public sealed class BriefingSchedulerBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<BriefingSchedulerOptions> _options;
    private readonly ILogger<BriefingSchedulerBackgroundService> _logger;

    public BriefingSchedulerBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<BriefingSchedulerOptions> options,
        ILogger<BriefingSchedulerBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Briefing scheduler background service is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Max(60, _options.Value.PollIntervalSeconds));
        using var timer = new PeriodicTimer(pollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<BriefingSchedulerCoordinator>();
                await coordinator.RunOnceAsync(_timeProvider.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Briefing scheduler polling loop failed unexpectedly.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Briefing scheduler background service stopped.");
    }
}