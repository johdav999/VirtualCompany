using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class BriefingUpdateJobWorkerOptions
{
    public const string SectionName = "BriefingUpdateJobs";

    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 5;
    public int ClaimTimeoutSeconds { get; set; } = 300;
}

public interface IBriefingUpdateJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public sealed class CompanyBriefingUpdateJobRunner : IBriefingUpdateJobRunner
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IBriefingGenerationPipeline _pipeline;
    private readonly IBackgroundJobExecutor _backgroundJobExecutor;
    private readonly IBackgroundExecutionRetryPolicy _retryPolicy;
    private readonly ICompanyExecutionScopeFactory _companyExecutionScopeFactory;
    private readonly IOptions<BriefingUpdateJobWorkerOptions> _options;
    private readonly ILogger<CompanyBriefingUpdateJobRunner> _logger;

    public CompanyBriefingUpdateJobRunner(
        VirtualCompanyDbContext dbContext,
        IBriefingGenerationPipeline pipeline,
        IBackgroundJobExecutor backgroundJobExecutor,
        IBackgroundExecutionRetryPolicy retryPolicy,
        ICompanyExecutionScopeFactory companyExecutionScopeFactory,
        IOptions<BriefingUpdateJobWorkerOptions> options,
        ILogger<CompanyBriefingUpdateJobRunner> logger)
    {
        _dbContext = dbContext;
        _pipeline = pipeline;
        _backgroundJobExecutor = backgroundJobExecutor;
        _retryPolicy = retryPolicy;
        _companyExecutionScopeFactory = companyExecutionScopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var jobs = await ClaimDueJobsAsync(cancellationToken);
        var handled = 0;

        foreach (var job in jobs)
        {
            using var tenantScope = _companyExecutionScopeFactory.BeginScope(job.CompanyId);
            var attempt = job.AttemptCount + 1;
            var maxAttempts = job.MaxAttempts > 0
                ? job.MaxAttempts
                : Math.Max(1, _options.Value.MaxAttempts);
            var retryDelay = _retryPolicy.GetRetryDelay(attempt);

            var execution = await _backgroundJobExecutor.ExecuteAsync(
                new BackgroundJobExecutionContext(
                    $"briefing-update:{job.TriggerType.ToStorageValue()}",
                    attempt,
                    maxAttempts,
                    job.CompanyId,
                    job.CorrelationId,
                    job.IdempotencyKey,
                    requireCompanyContext: true),
                innerCancellationToken => _pipeline.GenerateAsync(ToContext(job), innerCancellationToken),
                retryDelay,
                cancellationToken);

            var nowUtc = DateTime.UtcNow;
            switch (execution.Outcome)
            {
                case BackgroundJobExecutionOutcome.Succeeded:
                case BackgroundJobExecutionOutcome.IdempotentDuplicate:
                    job.MarkCompleted(nowUtc);
                    handled++;
                    break;
                case BackgroundJobExecutionOutcome.RetryScheduled:
                    job.ScheduleRetry(
                        nowUtc.Add(execution.RetryDelay ?? TimeSpan.Zero),
                        ResolveFailureCode(execution),
                        ResolveFailureMessage(execution),
                        ResolveFailureDetails(execution),
                        nowUtc);
                    break;
                case BackgroundJobExecutionOutcome.Blocked:
                case BackgroundJobExecutionOutcome.PermanentFailure:
                case BackgroundJobExecutionOutcome.RetryExhausted:
                    job.MarkFailed(
                        ResolveFailureCode(execution),
                        ResolveFailureMessage(execution),
                        ResolveFailureDetails(execution),
                        nowUtc);
                    handled++;
                    break;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Briefing update job {JobId} for company {CompanyId} completed worker cycle with status {Status}.",
                job.Id,
                job.CompanyId,
                job.Status.ToStorageValue());
        }

        return handled;
    }

    private async Task<IReadOnlyList<CompanyBriefingUpdateJob>> ClaimDueJobsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var staleBeforeUtc = nowUtc.Subtract(TimeSpan.FromSeconds(Math.Max(30, _options.Value.ClaimTimeoutSeconds)));
        var batchSize = Math.Max(1, _options.Value.BatchSize);
        var candidateIds = await _dbContext.CompanyBriefingUpdateJobs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                (x.Status == CompanyBriefingUpdateJobStatus.Pending ||
                 x.Status == CompanyBriefingUpdateJobStatus.Retrying) &&
                (x.NextAttemptAt == null || x.NextAttemptAt <= nowUtc) ||
                x.Status == CompanyBriefingUpdateJobStatus.Processing &&
                x.StartedAt != null &&
                x.StartedAt <= staleBeforeUtc)
            .OrderBy(x => x.NextAttemptAt ?? x.CreatedAt)
            .ThenBy(x => x.CreatedAt)
            .Take(batchSize)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        if (candidateIds.Length == 0)
        {
            return [];
        }

        // Conditional claiming keeps duplicate workers from executing the same due job.
        await _dbContext.CompanyBriefingUpdateJobs
            .IgnoreQueryFilters()
            .Where(x =>
                candidateIds.Contains(x.Id) &&
                ((x.Status == CompanyBriefingUpdateJobStatus.Pending ||
                  x.Status == CompanyBriefingUpdateJobStatus.Retrying) &&
                 (x.NextAttemptAt == null || x.NextAttemptAt <= nowUtc) ||
                 x.Status == CompanyBriefingUpdateJobStatus.Processing &&
                 x.StartedAt != null &&
                 x.StartedAt <= staleBeforeUtc))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, CompanyBriefingUpdateJobStatus.Processing)
                .SetProperty(x => x.StartedAt, nowUtc)
                .SetProperty(x => x.NextAttemptAt, (DateTime?)null)
                .SetProperty(x => x.UpdatedAt, nowUtc),
                cancellationToken);

        return await _dbContext.CompanyBriefingUpdateJobs
            .IgnoreQueryFilters()
            .Where(x => candidateIds.Contains(x.Id) && x.Status == CompanyBriefingUpdateJobStatus.Processing && x.StartedAt == nowUtc)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private static BriefingGenerationJobContext ToContext(CompanyBriefingUpdateJob job) =>
        new(
            job.Id,
            job.CompanyId,
            job.TriggerType.ToStorageValue(),
            job.BriefingType?.ToStorageValue(),
            job.EventType,
            job.CorrelationId,
            job.IdempotencyKey,
            job.SourceMetadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase));

    private static string ResolveFailureMessage(BackgroundJobExecutionResult execution) =>
        string.IsNullOrWhiteSpace(execution.ErrorMessage)
            ? "Briefing generation job failed."
            : execution.ErrorMessage;

    private static string ResolveFailureCode(BackgroundJobExecutionResult execution) =>
        string.IsNullOrWhiteSpace(execution.ExceptionType)
            ? execution.Outcome.ToString()
            : execution.ExceptionType;

    private static string ResolveFailureDetails(BackgroundJobExecutionResult execution) =>
        $"Outcome={execution.Outcome}; Classification={execution.FailureClassification}; RetryDelay={execution.RetryDelay?.TotalSeconds}";
}

public sealed class BriefingUpdateJobBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<BriefingUpdateJobWorkerOptions> _options;
    private readonly ILogger<BriefingUpdateJobBackgroundService> _logger;

    public BriefingUpdateJobBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<BriefingUpdateJobWorkerOptions> options,
        ILogger<BriefingUpdateJobBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Briefing update job worker is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Clamp(_options.Value.PollIntervalSeconds, 1, 30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IBriefingUpdateJobRunner>();
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
                _logger.LogError(ex, "Briefing update job worker loop failed.");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }
}
