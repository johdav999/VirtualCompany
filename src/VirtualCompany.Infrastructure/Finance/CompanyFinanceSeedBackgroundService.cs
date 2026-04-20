using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceSeedWorkerOptions
{
    public const string SectionName = "FinanceSeedWorker";

    public bool Enabled { get; set; } = true;
    public int PollIntervalMilliseconds { get; set; } = 1000;
    public int BatchSize { get; set; } = 10;
    public int ClaimTimeoutSeconds { get; set; } = 300;
}

public interface IFinanceSeedJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public sealed class CompanyFinanceSeedJobRunner : IFinanceSeedJobRunner
{
    private const string FinanceSeedStartedAction = "finance.seed.job.started";
    private const string FinanceSeedCompletedAction = "finance.seed.job.completed";
    private const string FinanceSeedFailedAction = "finance.seed.job.failed";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceSeedBootstrapService _bootstrapService;
    private readonly IBackgroundJobExecutor _backgroundJobExecutor;
    private readonly IBackgroundExecutionRetryPolicy _retryPolicy;
    private readonly ICompanyExecutionScopeFactory _companyExecutionScopeFactory;
    private readonly IFinanceSeedTelemetry _financeSeedTelemetry;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly IOptions<FinanceSeedBackfillWorkerOptions> _backfillOptions;
    private readonly IOptions<FinanceSeedWorkerOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyFinanceSeedJobRunner> _logger;

    public CompanyFinanceSeedJobRunner(
        VirtualCompanyDbContext dbContext,
        IFinanceSeedBootstrapService bootstrapService,
        IBackgroundJobExecutor backgroundJobExecutor,
        IBackgroundExecutionRetryPolicy retryPolicy,
        ICompanyExecutionScopeFactory companyExecutionScopeFactory,
        IFinanceSeedTelemetry financeSeedTelemetry,
        IAuditEventWriter auditEventWriter,
        IOptions<FinanceSeedBackfillWorkerOptions> backfillOptions,
        IOptions<FinanceSeedWorkerOptions> options,
        TimeProvider timeProvider,
        ILogger<CompanyFinanceSeedJobRunner> logger)
    {
        _dbContext = dbContext;
        _bootstrapService = bootstrapService;
        _backgroundJobExecutor = backgroundJobExecutor;
        _retryPolicy = retryPolicy;
        _companyExecutionScopeFactory = companyExecutionScopeFactory;
        _financeSeedTelemetry = financeSeedTelemetry;
        _auditEventWriter = auditEventWriter;
        _backfillOptions = backfillOptions;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
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
            var company = await _dbContext.Companies
                .IgnoreQueryFilters()
                .SingleAsync(x => x.Id == execution.CompanyId, cancellationToken);

            var triggerSource = ResolveTriggerSource(company);
            var seedStateBeforeStart = company.FinanceSeedStatus;
            var backfillAttempt = await FindBackfillAttemptAsync(execution.Id, cancellationToken);
            var retryDelay = ResolveRetryDelay(backfillAttempt, attempt);
            if (backfillAttempt is not null)
            {
                backfillAttempt.MarkInProgress(
                    _timeProvider.GetUtcNow().UtcDateTime,
                    FinanceSeedingState.Seeding);
            }
            var startedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
            execution.StartAttempt(execution.CorrelationId, attempt, maxAttempts);
            FinanceSeedingMetadata.MarkSeeding(
                company,
                startedAtUtc,
                startedAtUtc: startedAtUtc,
                triggerSource: triggerSource,
                jobId: execution.Id,
                correlationId: execution.CorrelationId);
            await WriteLifecycleAuditAsync(
                execution,
                FinanceSeedStartedAction,
                AuditEventOutcomes.Pending,
                "Finance seed background execution started.",
                triggerSource,
                attempt,
                durationMs: null,
                errorType: null,
                cancellationToken);

            _logger.LogInformation(
                "Finance seed orchestration started from {TriggerSource} in {SeedMode} mode for company {CompanyId}. Execution {ExecutionId} attempt {Attempt} of {MaxAttempts}. CorrelationId={CorrelationId}, IdempotencyKey={IdempotencyKey}, ActorType={ActorType}.",
                triggerSource,
                FinanceSeedRequestModes.Replace,
                execution.CompanyId,
                execution.Id,
                attempt,
                maxAttempts,
                execution.CorrelationId,
                execution.IdempotencyKey,
                AuditActorTypes.System);

            await _financeSeedTelemetry.TrackAsync(
                FinanceSeedTelemetryEventNames.Started,
                new FinanceSeedTelemetryContext(
                    execution.CompanyId,
                    execution.Id,
                    execution.CorrelationId,
                    execution.IdempotencyKey,
                    triggerSource,
                    seedStateBeforeStart,
                    FinanceSeedingState.Seeding,
                    null,
                    false,
                    attempt,
                    maxAttempts,
                    SeedMode: FinanceSeedRequestModes.Replace,
                    ActorType: AuditActorTypes.System,
                    ActorId: null),
                cancellationToken);

            var result = await _backgroundJobExecutor.ExecuteAsync(
                new BackgroundJobExecutionContext(
                    "finance-seed",
                    attempt,
                    maxAttempts,
                    execution.CompanyId,
                    execution.CorrelationId,
                    execution.IdempotencyKey,
                    requireCompanyContext: true),
                innerCancellationToken => _bootstrapService.GenerateAsync(
                    new FinanceSeedBootstrapCommand(
                        execution.CompanyId,
                        ResolveSeedValue(execution.CompanyId),
                        SeedAnchorUtc: null,
                        ReplaceExisting: true,
                        InjectAnomalies: false),
                    innerCancellationToken),
                retryDelay,
                cancellationToken);

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var terminalEventName = string.Empty;
            var terminalSeedStateAfter = FinanceSeedingState.Seeding;
            long? durationMs = null;
            string? errorType = null;
            string? errorMessageSafe = null;
            switch (result.Outcome)
            {
                case BackgroundJobExecutionOutcome.Succeeded:
                case BackgroundJobExecutionOutcome.IdempotentDuplicate:
                    execution.MarkSucceeded();
                    FinanceSeedingMetadata.MarkSeeded(
                        company,
                        "finance_seed_bootstrap:v1",
                        ResolveSeedValue(execution.CompanyId),
                        execution.CompletedUtc ?? nowUtc);
                    await WriteLifecycleAuditAsync(
                        execution,
                        FinanceSeedCompletedAction,
                        AuditEventOutcomes.Succeeded,
                        "Finance seed background execution completed successfully.",
                        triggerSource,
                        attempt,
                        CalculateDurationMilliseconds(execution.StartedUtc, execution.CompletedUtc ?? nowUtc),
                        errorType: null,
                        cancellationToken);
                    backfillAttempt?.MarkSucceeded(execution.CompletedUtc ?? nowUtc, FinanceSeedingState.Seeded);
                    terminalEventName = FinanceSeedTelemetryEventNames.Completed;
                    terminalSeedStateAfter = FinanceSeedingState.Seeded;
                    durationMs = CalculateDurationMilliseconds(execution.StartedUtc, execution.CompletedUtc ?? nowUtc);
                    handled++;
                    break;
                case BackgroundJobExecutionOutcome.RetryScheduled:
                    errorMessageSafe = ResolveSafeFailureMessage(result);
                    execution.ScheduleRetry(
                        nowUtc.Add(result.RetryDelay ?? TimeSpan.Zero),
                        MapFailureCategory(result.FailureClassification),
                        ResolveFailureCode(result),
                        errorMessageSafe);
                    FinanceSeedingMetadata.MarkSeeding(
                        company,
                        nowUtc,
                        triggerSource: triggerSource,
                        jobId: execution.Id,
                        correlationId: execution.CorrelationId);
                    backfillAttempt?.MarkQueued(
                        execution.Id,
                        execution.IdempotencyKey,
                        nowUtc,
                        FinanceSeedingState.Seeding);
                    break;
                case BackgroundJobExecutionOutcome.Blocked:
                case BackgroundJobExecutionOutcome.PermanentFailure:
                case BackgroundJobExecutionOutcome.RetryExhausted:
                    errorType = ResolveFailureCode(result);
                    errorMessageSafe = ResolveSafeFailureMessage(result);
                    execution.MarkFailed(
                        MapFailureCategory(result.FailureClassification),
                        errorType,
                        errorMessageSafe);
                    FinanceSeedingMetadata.MarkFailed(
                        company,
                        execution.FailureCode,
                        execution.FailureMessage,
                        execution.CompletedUtc ?? nowUtc);
                    await WriteLifecycleAuditAsync(
                        execution,
                        FinanceSeedFailedAction,
                        AuditEventOutcomes.Failed,
                        errorMessageSafe,
                        triggerSource,
                        attempt,
                        CalculateDurationMilliseconds(execution.StartedUtc, execution.CompletedUtc ?? nowUtc),
                        errorType,
                        cancellationToken);
                    terminalEventName = FinanceSeedTelemetryEventNames.Failed;
                    terminalSeedStateAfter = FinanceSeedingState.Failed;
                    durationMs = CalculateDurationMilliseconds(execution.StartedUtc, execution.CompletedUtc ?? nowUtc);
                    backfillAttempt?.MarkFailed(execution.CompletedUtc ?? nowUtc, errorMessageSafe, FinanceSeedingState.Failed);
                    handled++;
                    break;
            }

            if (backfillAttempt is not null)
            {
                await ReconcileBackfillRunAsync(backfillAttempt.RunId, cancellationToken);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(terminalEventName))
            {
                if (string.Equals(terminalEventName, FinanceSeedTelemetryEventNames.Completed, StringComparison.Ordinal))
                {
                    _logger.LogInformation(
                        "Finance seed orchestration completed for company {CompanyId}. Execution {ExecutionId} attempt {Attempt} finished in {DurationMs} ms. TriggerSource={TriggerSource}, SeedMode={SeedMode}, CorrelationId={CorrelationId}, IdempotencyKey={IdempotencyKey}.",
                        execution.CompanyId,
                        execution.Id,
                        attempt,
                        durationMs,
                        triggerSource,
                        FinanceSeedRequestModes.Replace,
                        execution.CorrelationId,
                        execution.IdempotencyKey);
                }
                else
                {
                    _logger.LogWarning(
                        "Finance seed orchestration failed for company {CompanyId}. Execution {ExecutionId} attempt {Attempt} ended with {ErrorType} after {DurationMs} ms. TriggerSource={TriggerSource}, SeedMode={SeedMode}, CorrelationId={CorrelationId}, IdempotencyKey={IdempotencyKey}.",
                        execution.CompanyId,
                        execution.Id,
                        attempt,
                        errorType,
                        durationMs,
                        triggerSource,
                        FinanceSeedRequestModes.Replace,
                        execution.CorrelationId,
                        execution.IdempotencyKey);
                }

                await _financeSeedTelemetry.TrackAsync(
                    terminalEventName,
                    new FinanceSeedTelemetryContext(
                        execution.CompanyId,
                        execution.Id,
                        execution.CorrelationId,
                        execution.IdempotencyKey,
                        triggerSource,
                        FinanceSeedingState.Seeding,
                        terminalSeedStateAfter,
                        null,
                        false,
                        attempt,
                        maxAttempts,
                        durationMs,
                        errorType,
                        errorMessageSafe,
                        SeedMode: FinanceSeedRequestModes.Replace,
                        ActorType: AuditActorTypes.System,
                        ActorId: null),
                    cancellationToken);
            }
        }

        return handled;
    }

    private async Task WriteLifecycleAuditAsync(
        BackgroundExecution execution,
        string action,
        string outcome,
        string rationale,
        string triggerSource,
        int? attempt,
        long? durationMs,
        string? errorType,
        CancellationToken cancellationToken)
    {
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                execution.CompanyId,
                AuditActorTypes.System,
                null,
                action,
                BackgroundExecutionRelatedEntityTypes.FinanceSeed,
                execution.Id.ToString("D"),
                outcome,
                rationale,
                Metadata: new Dictionary<string, string?>
                {
                    ["triggerSource"] = triggerSource,
                    ["source"] = triggerSource,
                    ["mode"] = FinanceSeedRequestModes.Replace,
                    ["actorType"] = AuditActorTypes.System,
                    ["companyId"] = execution.CompanyId.ToString("D"),
                    ["executionId"] = execution.Id.ToString("D"),
                    ["jobStatus"] = execution.Status.ToStorageValue(),
                    ["idempotencyKey"] = execution.IdempotencyKey,
                    ["correlationId"] = execution.CorrelationId,
                    ["attempt"] = attempt?.ToString(),
                    ["maxAttempts"] = execution.MaxAttempts.ToString(),
                    ["durationMs"] = durationMs?.ToString(),
                    ["errorType"] = errorType,
                    ["failureCode"] = execution.FailureCode,
                    ["failureMessage"] = execution.FailureMessage
                },
                CorrelationId: execution.CorrelationId,
                OccurredUtc: _timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);
    }

    private async Task<IReadOnlyList<BackgroundExecution>> ClaimDueExecutionsAsync(CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var staleBeforeUtc = nowUtc.Subtract(TimeSpan.FromSeconds(Math.Max(30, _options.Value.ClaimTimeoutSeconds)));
        var candidateIds = await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.ExecutionType == BackgroundExecutionType.FinanceSeed &&
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
                x.ExecutionType == BackgroundExecutionType.FinanceSeed &&
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
                x.ExecutionType == BackgroundExecutionType.FinanceSeed &&
                x.Status == BackgroundExecutionStatus.InProgress &&
                x.StartedUtc == nowUtc)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);
    }

    private async Task<FinanceSeedBackfillAttempt?> FindBackfillAttemptAsync(Guid backgroundExecutionId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceSeedBackfillAttempts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.BackgroundExecutionId == backgroundExecutionId, cancellationToken);

    private async Task ReconcileBackfillRunAsync(Guid runId, CancellationToken cancellationToken)
    {
        var run = await _dbContext.FinanceSeedBackfillRuns
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == runId, cancellationToken);

        var attempts = await _dbContext.FinanceSeedBackfillAttempts
            .IgnoreQueryFilters()
            .Where(x => x.RunId == runId)
            .ToListAsync(cancellationToken);

        var queuedCount = attempts.Count(x => x.BackgroundExecutionId.HasValue);
        var succeededCount = attempts.Count(x => x.Status == FinanceSeedBackfillAttemptStatus.Succeeded);
        var skippedCount = attempts.Count(x => x.Status == FinanceSeedBackfillAttemptStatus.Skipped);
        var failedCount = attempts.Count(x => x.Status == FinanceSeedBackfillAttemptStatus.Failed);

        run.UpdateProgress(run.ScannedCount, queuedCount, succeededCount, skippedCount, failedCount);

        if (run.CompletedUtc.HasValue)
        {
            run.MarkCompleted(
                run.CompletedUtc.Value,
                failedCount > 0 || run.Status == FinanceSeedBackfillRunStatus.CompletedWithErrors);
        }
    }

    private static int ResolveSeedValue(Guid companyId)
    {
        var bytes = companyId.ToByteArray();
        var value =
            BitConverter.ToInt32(bytes, 0) ^
            BitConverter.ToInt32(bytes, 4) ^
            BitConverter.ToInt32(bytes, 8) ^
            BitConverter.ToInt32(bytes, 12);

        if (value == int.MinValue)
        {
            return 913;
        }

        value = Math.Abs(value);
        return value == 0 ? 913 : value;
    }

    private TimeSpan ResolveRetryDelay(FinanceSeedBackfillAttempt? backfillAttempt, int attempt)
    {
        if (backfillAttempt is null)
        {
            return _retryPolicy.GetRetryDelay(attempt);
        }

        var options = _backfillOptions.Value;
        var baseDelaySeconds = Math.Max(0, options.BaseRetryDelaySeconds);
        if (baseDelaySeconds == 0)
        {
            return TimeSpan.Zero;
        }

        var multiplier = Math.Pow(options.RetryBackoffMultiplier < 1d ? 1d : options.RetryBackoffMultiplier, Math.Max(0, attempt - 1));
        return TimeSpan.FromSeconds(Math.Min(baseDelaySeconds * multiplier, Math.Max(baseDelaySeconds, options.MaxRetryDelaySeconds)));
    }

    private static long? CalculateDurationMilliseconds(DateTime? startedUtc, DateTime? completedUtc)
    {
        if (!startedUtc.HasValue || !completedUtc.HasValue)
        {
            return null;
        }

        var duration = completedUtc.Value - startedUtc.Value;
        return duration < TimeSpan.Zero ? 0L : (long)duration.TotalMilliseconds;
    }

    private static string ResolveFailureCode(BackgroundJobExecutionResult result) =>
        string.IsNullOrWhiteSpace(result.ExceptionType)
            ? result.Outcome.ToString()
            : result.ExceptionType;

    private static string ResolveSafeFailureMessage(BackgroundJobExecutionResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? "Finance seed background execution failed."
            : result.ErrorMessage.Trim().Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

        return message.Length <= 256 ? message : $"{message[..253]}...";
    }

    private static string ResolveTriggerSource(Company company) => FinanceSeedingMetadata.ReadTriggerSource(company) ?? FinanceEntrySources.FinanceEntry;

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

public sealed class FinanceSeedBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<FinanceSeedWorkerOptions> _options;
    private readonly ILogger<FinanceSeedBackgroundService> _logger;

    public FinanceSeedBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<FinanceSeedWorkerOptions> options,
        ILogger<FinanceSeedBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Finance seed worker is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromMilliseconds(Math.Max(100, _options.Value.PollIntervalMilliseconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<IFinanceSeedJobRunner>();
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
                _logger.LogError(ex, "Finance seed worker loop failed.");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }
    }
}
