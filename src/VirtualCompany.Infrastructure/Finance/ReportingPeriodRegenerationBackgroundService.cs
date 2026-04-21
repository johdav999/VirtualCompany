using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class ReportingPeriodRegenerationWorkerOptions
{
    public const string SectionName = "ReportingPeriodRegenerationWorker";

    public bool Enabled { get; set; } = true;
    public int PollIntervalMilliseconds { get; set; } = 1000;
    public int BatchSize { get; set; } = 10;
    public int ClaimTimeoutSeconds { get; set; } = 300;
}

public sealed class ReportingPeriodRegenerationJobRunner : IReportingPeriodRegenerationJobRunner
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IReportingPeriodCloseService _reportingPeriodCloseService;
    private readonly IBackgroundJobExecutor _backgroundJobExecutor;
    private readonly IBackgroundExecutionRetryPolicy _retryPolicy;
    private readonly ICompanyExecutionScopeFactory _companyExecutionScopeFactory;
    private readonly IOptions<ReportingPeriodRegenerationWorkerOptions> _options;

    public ReportingPeriodRegenerationJobRunner(
        VirtualCompanyDbContext dbContext,
        IReportingPeriodCloseService reportingPeriodCloseService,
        IBackgroundJobExecutor backgroundJobExecutor,
        IBackgroundExecutionRetryPolicy retryPolicy,
        ICompanyExecutionScopeFactory companyExecutionScopeFactory,
        IOptions<ReportingPeriodRegenerationWorkerOptions> options)
    {
        _dbContext = dbContext;
        _reportingPeriodCloseService = reportingPeriodCloseService;
        _backgroundJobExecutor = backgroundJobExecutor;
        _retryPolicy = retryPolicy;
        _companyExecutionScopeFactory = companyExecutionScopeFactory;
        _options = options;
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var executions = await ClaimDueExecutionsAsync(cancellationToken);
        var handled = 0;

        foreach (var execution in executions)
        {
            using var tenantScope = _companyExecutionScopeFactory.BeginScope(execution.CompanyId);
            var attempt = execution.AttemptCount + 1;
            var maxAttempts = Math.Max(1, execution.MaxAttempts);
            var retryDelay = _retryPolicy.GetRetryDelay(attempt);

            execution.StartAttempt(execution.CorrelationId, attempt, maxAttempts);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (!Guid.TryParse(execution.RelatedEntityId, out var fiscalPeriodId))
            {
                execution.MarkFailed(
                    BackgroundExecutionFailureCategory.Validation,
                    "invalid_fiscal_period_reference",
                    "Background reporting regeneration execution does not reference a valid fiscal period id.");
                await _dbContext.SaveChangesAsync(cancellationToken);
                handled++;
                continue;
            }

            var result = await _backgroundJobExecutor.ExecuteAsync(
                new BackgroundJobExecutionContext(
                    "reporting-period-regeneration",
                    attempt,
                    maxAttempts,
                    execution.CompanyId,
                    execution.CorrelationId,
                    execution.IdempotencyKey,
                    requireCompanyContext: true),
                innerCancellationToken => _reportingPeriodCloseService.RunBackgroundRegenerationAsync(
                    execution.CompanyId,
                    fiscalPeriodId,
                    execution.CorrelationId,
                    innerCancellationToken),
                retryDelay,
                cancellationToken);

            var nowUtc = DateTime.UtcNow;
            switch (result.Outcome)
            {
                case BackgroundJobExecutionOutcome.Succeeded:
                case BackgroundJobExecutionOutcome.IdempotentDuplicate:
                    execution.MarkSucceeded();
                    handled++;
                    break;
                case BackgroundJobExecutionOutcome.RetryScheduled:
                    execution.ScheduleRetry(
                        nowUtc.Add(result.RetryDelay ?? TimeSpan.Zero),
                        MapFailureCategory(result.FailureClassification),
                        ResolveFailureCode(result),
                        ResolveFailureMessage(result));
                    break;
                case BackgroundJobExecutionOutcome.Blocked:
                    execution.MarkBlocked(
                        MapFailureCategory(result.FailureClassification),
                        ResolveFailureCode(result),
                        ResolveFailureMessage(result));
                    handled++;
                    break;
                case BackgroundJobExecutionOutcome.PermanentFailure:
                case BackgroundJobExecutionOutcome.RetryExhausted:
                    execution.MarkFailed(
                        MapFailureCategory(result.FailureClassification),
                        ResolveFailureCode(result),
                        ResolveFailureMessage(result));
                    handled++;
                    break;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return handled;
    }

    private async Task<IReadOnlyList<BackgroundExecution>> ClaimDueExecutionsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var staleBeforeUtc = nowUtc.Subtract(TimeSpan.FromSeconds(Math.Max(30, _options.Value.ClaimTimeoutSeconds)));
        var candidateIds = await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.ExecutionType == BackgroundExecutionType.FinanceReportRegeneration &&
                (((x.Status == BackgroundExecutionStatus.Pending || x.Status == BackgroundExecutionStatus.RetryScheduled) &&
                  (x.NextRetryUtc == null || x.NextRetryUtc <= nowUtc)) ||
                 (x.Status == BackgroundExecutionStatus.InProgress &&
                  x.HeartbeatUtc != null &&
                  x.HeartbeatUtc <= staleBeforeUtc)))
            .OrderBy(x => x.NextRetryUtc ?? x.CreatedUtc)
            .ThenBy(x => x.CreatedUtc)
            .Take(Math.Max(1, _options.Value.BatchSize))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        if (candidateIds.Length == 0)
        {
            return [];
        }

        await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .Where(x =>
                candidateIds.Contains(x.Id) &&
                x.ExecutionType == BackgroundExecutionType.FinanceReportRegeneration &&
                (((x.Status == BackgroundExecutionStatus.Pending || x.Status == BackgroundExecutionStatus.RetryScheduled) &&
                  (x.NextRetryUtc == null || x.NextRetryUtc <= nowUtc)) ||
                 (x.Status == BackgroundExecutionStatus.InProgress &&
                  x.HeartbeatUtc != null &&
                  x.HeartbeatUtc <= staleBeforeUtc)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, BackgroundExecutionStatus.InProgress)
                .SetProperty(x => x.StartedUtc, nowUtc)
                .SetProperty(x => x.HeartbeatUtc, nowUtc)
                .SetProperty(x => x.NextRetryUtc, (DateTime?)null)
                .SetProperty(x => x.UpdatedUtc, nowUtc),
                cancellationToken);

        return await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .Where(x =>
                candidateIds.Contains(x.Id) &&
                x.ExecutionType == BackgroundExecutionType.FinanceReportRegeneration &&
                x.Status == BackgroundExecutionStatus.InProgress &&
                x.StartedUtc == nowUtc)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);
    }

    private static string ResolveFailureCode(BackgroundJobExecutionResult result) =>
        string.IsNullOrWhiteSpace(result.ExceptionType) ? result.Outcome.ToString() : result.ExceptionType;

    private static string ResolveFailureMessage(BackgroundJobExecutionResult result) =>
        string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Reporting period regeneration failed." : result.ErrorMessage;

    private static BackgroundExecutionFailureCategory MapFailureCategory(BackgroundJobFailureClassification? classification) =>
        classification switch
        {
            BackgroundJobFailureClassification.Unknown => BackgroundExecutionFailureCategory.Unknown,
            BackgroundJobFailureClassification.LockContention => BackgroundExecutionFailureCategory.LockContention,
            BackgroundJobFailureClassification.ExternalDependencyTimeout => BackgroundExecutionFailureCategory.ExternalDependencyTimeout,
            BackgroundJobFailureClassification.ExternalDependencyUnavailable => BackgroundExecutionFailureCategory.ExternalDependencyUnavailable,
            BackgroundJobFailureClassification.RateLimited => BackgroundExecutionFailureCategory.RateLimited,
            BackgroundJobFailureClassification.PermanentBusinessRule or BackgroundJobFailureClassification.Permanent => BackgroundExecutionFailureCategory.PermanentBusinessRule,
            BackgroundJobFailureClassification.PermanentPolicy => BackgroundExecutionFailureCategory.PermanentPolicy,
            BackgroundJobFailureClassification.Validation => BackgroundExecutionFailureCategory.Validation,
            BackgroundJobFailureClassification.ApprovalRequired => BackgroundExecutionFailureCategory.ApprovalRequired,
            BackgroundJobFailureClassification.Configuration => BackgroundExecutionFailureCategory.Configuration,
            _ => BackgroundExecutionFailureCategory.TransientInfrastructure
        };
}

public sealed class ReportingPeriodRegenerationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ReportingPeriodRegenerationWorkerOptions> _options;
    private readonly ILogger<ReportingPeriodRegenerationBackgroundService> _logger;

    public ReportingPeriodRegenerationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReportingPeriodRegenerationWorkerOptions> options,
        ILogger<ReportingPeriodRegenerationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Reporting period regeneration worker is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(100, _options.Value.PollIntervalMilliseconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IReportingPeriodRegenerationJobRunner>();
                var handled = await runner.RunDueAsync(stoppingToken);
                if (handled == 0)
                {
                    await Task.Delay(pollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reporting period regeneration worker loop failed.");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }
}
