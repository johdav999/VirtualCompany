using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.BackgroundJobs;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AuditPackageBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundJobExecutor _executor;
    private readonly AuditPackageOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AuditPackageBackgroundService> _logger;

    public AuditPackageBackgroundService(IServiceScopeFactory scopeFactory, IBackgroundJobExecutor executor,
        IOptions<AuditPackageOptions> options, TimeProvider time,
        ILogger<AuditPackageBackgroundService> logger)
    {
        _scopeFactory = scopeFactory; _executor = executor; _options = options.Value;
        _time = time; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollIntervalSeconds), _time);
        do
        {
            var pollIdentity = _time.GetUtcNow().UtcDateTime.ToString("yyyyMMddHHmm");
            var result = await _executor.ExecuteAsync(new BackgroundJobExecutionContext(
                    "finance.audit_package_assembly", 1, 1, idempotencyKey: $"audit-package-poll:{pollIdentity}"),
                async cancellationToken =>
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<IAuditPackageService>();
                    await service.ProcessPendingAsync(_options.ClaimBatchSize, cancellationToken);
                    await service.ExpireAsync(Math.Max(10, _options.ClaimBatchSize), cancellationToken);
                }, TimeSpan.Zero, stoppingToken);
            if (result.Outcome is not (BackgroundJobExecutionOutcome.Succeeded or BackgroundJobExecutionOutcome.IdempotentDuplicate))
                _logger.LogWarning("Audit-package background poll ended with {Outcome}.", result.Outcome);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
