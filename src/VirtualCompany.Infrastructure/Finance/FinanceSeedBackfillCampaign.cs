using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceSeedBackfillWorkerOptions
{
    public const string SectionName = "FinanceSeedBackfill";

    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 300;
    public int ScanPageSize { get; set; } = 100;
    public int EnqueueBatchSize { get; set; } = 20;
    public int MaxCompaniesPerRun { get; set; } = 200;
    public int MaxConcurrentEnqueues { get; set; } = 4;
    public int RateLimitCount { get; set; } = 4;
    public int RateLimitWindowSeconds { get; set; } = 1;
    public int MaxRetries { get; set; } = 4;
    public int BaseRetryDelaySeconds { get; set; } = 30;
    public double RetryBackoffMultiplier { get; set; } = 2d;
    public int MaxRetryDelaySeconds { get; set; } = 900;
    public int DelayBetweenBatchesMs { get; set; } = 250;
    public int LockTtlSeconds { get; set; } = 600;
    public string LockKey { get; set; } = "finance-seed-backfill";

    public int BatchSize
    {
        get => EnqueueBatchSize;
        set => EnqueueBatchSize = value;
    }

    public int MaxConcurrency
    {
        get => MaxConcurrentEnqueues;
        set => MaxConcurrentEnqueues = value;
    }
}

public interface IFinanceSeedBackfillDelayStrategy
{
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public interface IFinanceSeedBackfillExecutionScheduler
{
    Task<FinanceSeedBackfillScheduleResult> ScheduleAsync(
        FinanceSeedBackfillScheduleRequest request,
        CancellationToken cancellationToken);
}

public sealed record FinanceSeedBackfillScheduleRequest(
    Guid RunId,
    Guid CompanyId,
    FinanceSeedingState SeedStateBefore,
    string EligibilityReason);

public sealed record FinanceSeedBackfillScheduleResult(
    Guid CompanyId,
    FinanceSeedingState SeedStateBefore,
    FinanceSeedBackfillAttemptStatus AttemptStatus,
    DateTime OccurredAtUtc,
    Guid? BackgroundExecutionId,
    string? IdempotencyKey,
    string? SkipReason,
    string? ErrorDetails)
{
    public static FinanceSeedBackfillScheduleResult Queued(
        Guid companyId,
        FinanceSeedingState seedStateBefore,
        DateTime occurredAtUtc,
        Guid backgroundExecutionId,
        string idempotencyKey) =>
        new(
            companyId,
            seedStateBefore,
            FinanceSeedBackfillAttemptStatus.Queued,
            occurredAtUtc,
            backgroundExecutionId,
            idempotencyKey,
            null,
            null);

    public static FinanceSeedBackfillScheduleResult Skipped(
        Guid companyId,
        FinanceSeedingState seedStateBefore,
        DateTime occurredAtUtc,
        string skipReason) =>
        new(
            companyId,
            seedStateBefore,
            FinanceSeedBackfillAttemptStatus.Skipped,
            occurredAtUtc,
            null,
            null,
            skipReason,
            null);

    public static FinanceSeedBackfillScheduleResult Failed(
        Guid companyId,
        FinanceSeedingState seedStateBefore,
        DateTime occurredAtUtc,
        string errorDetails) =>
        new(
            companyId,
            seedStateBefore,
            FinanceSeedBackfillAttemptStatus.Failed,
            occurredAtUtc,
            null,
            null,
            null,
            errorDetails);
}

public sealed class SystemFinanceSeedBackfillDelayStrategy : IFinanceSeedBackfillDelayStrategy
{
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(delay, cancellationToken);
}

public sealed class FinanceSeedBackfillExecutionScheduler : IFinanceSeedBackfillExecutionScheduler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<FinanceSeedBackfillWorkerOptions> _options;
    private readonly ILogger<FinanceSeedBackfillExecutionScheduler> _logger;

    public FinanceSeedBackfillExecutionScheduler(
        IServiceScopeFactory scopeFactory,
        IOptions<FinanceSeedBackfillWorkerOptions> options,
        TimeProvider timeProvider,
        ILogger<FinanceSeedBackfillExecutionScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<FinanceSeedBackfillScheduleResult> ScheduleAsync(
        FinanceSeedBackfillScheduleRequest request,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var identityFactory = scope.ServiceProvider.GetRequiredService<IBackgroundExecutionIdentityFactory>();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var company = await dbContext.Companies
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == request.CompanyId, cancellationToken);

        if (company is null)
        {
            return FinanceSeedBackfillScheduleResult.Failed(
                request.CompanyId,
                request.SeedStateBefore,
                nowUtc,
                "Finance seed backfill company was not found.");
        }

        var existingExecution = await dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == request.CompanyId && x.ExecutionType == BackgroundExecutionType.FinanceSeed)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingExecution is not null && FinanceSeedBackfillExecutionStates.IsActive(existingExecution.Status))
        {
            return FinanceSeedBackfillScheduleResult.Skipped(
                request.CompanyId,
                request.SeedStateBefore,
                nowUtc,
                FinanceSeedBackfillSkipReasons.ActiveExecution);
        }

        var recordChecks = await LoadRecordChecksAsync(dbContext, request.CompanyId, cancellationToken);
        if (company.FinanceSeedStatus == FinanceSeedingState.Seeded || recordChecks.IsComplete)
        {
            return FinanceSeedBackfillScheduleResult.Skipped(
                request.CompanyId,
                request.SeedStateBefore,
                nowUtc,
                FinanceSeedBackfillSkipReasons.AlreadySeeded);
        }

        var identity = identityFactory.Create(
            request.CompanyId,
            "finance-seed-backfill",
            correlationId: null,
            BackgroundExecutionType.FinanceSeed.ToStorageValue(),
            request.RunId.ToString("N"),
            request.CompanyId.ToString("N"));

        var execution = new BackgroundExecution(
            Guid.NewGuid(),
            request.CompanyId,
            BackgroundExecutionType.FinanceSeed,
            BackgroundExecutionRelatedEntityTypes.FinanceSeed,
            request.RunId.ToString("D"),
            identity.CorrelationId,
            identity.IdempotencyKey,
            maxAttempts: Math.Max(1, _options.Value.MaxRetries + 1));

        FinanceSeedingMetadata.MarkSeeding(
            company,
            nowUtc,
            requestedAtUtc: nowUtc,
            triggerSource: FinanceEntrySources.Backfill,
            jobId: execution.Id,
            correlationId: execution.CorrelationId);

        dbContext.BackgroundExecutions.Add(execution);
        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Queued finance seed backfill execution {ExecutionId} for company {CompanyId} in run {RunId}.",
            execution.Id,
            request.CompanyId,
            request.RunId);

        return FinanceSeedBackfillScheduleResult.Queued(
            request.CompanyId,
            request.SeedStateBefore,
            nowUtc,
            execution.Id,
            execution.IdempotencyKey);
    }

    private static async Task<FinanceBackfillRecordChecks> LoadRecordChecksAsync(
        VirtualCompanyDbContext dbContext,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var hasAccounts = await dbContext.FinanceAccounts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
            var hasCounterparties = await dbContext.FinanceCounterparties.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
            var hasTransactions = await dbContext.FinanceTransactions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
            var hasBalances = await dbContext.FinanceBalances.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
            var hasPolicy = await dbContext.FinancePolicyConfigurations.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
            var hasInvoices = await dbContext.FinanceInvoices.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
            var hasBills = await dbContext.FinanceBills.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);

            return new FinanceBackfillRecordChecks(
                hasAccounts,
                hasCounterparties,
                hasTransactions,
                hasBalances,
                hasPolicy,
                hasInvoices,
                hasBills);
        }
        catch (SqlException ex) when (IsMissingFinanceSchema(ex))
        {
            return new FinanceBackfillRecordChecks(
                HasAccounts: false,
                HasCounterparties: false,
                HasTransactions: false,
                HasBalances: false,
                HasPolicyConfiguration: false,
                HasInvoices: false,
                HasBills: false);
        }
    }

    private static bool IsMissingFinanceSchema(SqlException exception) =>
        exception.Number == 208 &&
        exception.Message.Contains("finance_", StringComparison.OrdinalIgnoreCase);
}

public sealed class FinanceSeedBackfillOrchestrator : IFinanceSeedBackfillOrchestrator
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceSeedBackfillExecutionScheduler _scheduler;
    private readonly IFinanceSeedBackfillDelayStrategy _delayStrategy;
    private readonly IOptions<FinanceSeedBackfillWorkerOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FinanceSeedBackfillOrchestrator> _logger;

    public FinanceSeedBackfillOrchestrator(
        VirtualCompanyDbContext dbContext,
        IFinanceSeedBackfillExecutionScheduler scheduler,
        IFinanceSeedBackfillDelayStrategy delayStrategy,
        IOptions<FinanceSeedBackfillWorkerOptions> options,
        TimeProvider timeProvider,
        ILogger<FinanceSeedBackfillOrchestrator> logger)
    {
        _dbContext = dbContext;
        _scheduler = scheduler;
        _delayStrategy = delayStrategy;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<FinanceSeedBackfillRunDto> RunAsync(CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var options = _options.Value;
        var run = new FinanceSeedBackfillRun(
            Guid.NewGuid(),
            nowUtc,
            JsonSerializer.Serialize(new
            {
                options.ScanPageSize,
                options.EnqueueBatchSize,
                options.MaxCompaniesPerRun,
                options.MaxConcurrentEnqueues,
                options.RateLimitCount,
                options.RateLimitWindowSeconds,
                options.MaxRetries,
                options.BaseRetryDelaySeconds,
                options.RetryBackoffMultiplier,
                options.MaxRetryDelaySeconds,
                options.DelayBetweenBatchesMs
            }));
        _dbContext.FinanceSeedBackfillRuns.Add(run);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await ExecuteRunAsync(run, options, cancellationToken);
            run.MarkCompleted(_timeProvider.GetUtcNow().UtcDateTime, run.FailedCount > 0);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            run.MarkFailed(_timeProvider.GetUtcNow().UtcDateTime, ex.Message);
            run.SetErrorDetails($"{FinanceSeedBackfillRunErrors.UnhandledFailure}: {ex.Message}");
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation(
            "Finance seed backfill run {RunId} completed with status {Status}. Scanned={ScannedCount}, Queued={QueuedCount}, Succeeded={SucceededCount}, Skipped={SkippedCount}, Failed={FailedCount}.",
            run.Id,
            run.Status.ToStorageValue(),
            run.ScannedCount,
            run.QueuedCount,
            run.SucceededCount,
            run.SkippedCount,
            run.FailedCount);

        return MapRun(run);
    }

    private async Task ExecuteRunAsync(
        FinanceSeedBackfillRun run,
        FinanceSeedBackfillWorkerOptions options,
        CancellationToken cancellationToken)
    {
        var pageIndex = 0;
        var pacingState = new FinanceBackfillDispatchPacingState(_timeProvider.GetUtcNow().UtcDateTime);

        while (!cancellationToken.IsCancellationRequested && run.QueuedCount < options.MaxCompaniesPerRun)
        {
            var page = await LoadPageAsync(pageIndex, options.ScanPageSize, cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            pageIndex++;
            var pageState = await LoadPageStateAsync(page.Select(x => x.Id).ToArray(), cancellationToken);
            var eligible = new List<FinanceSeedBackfillCandidate>(page.Count);

            foreach (var company in page)
            {
                var evaluation = EvaluateCompany(company, pageState);
                run.UpdateProgress(
                    run.ScannedCount + 1,
                    run.QueuedCount,
                    run.SucceededCount,
                    run.SkippedCount,
                    run.FailedCount);

                if (!evaluation.IsEligible)
                {
                    var skippedAttempt = new FinanceSeedBackfillAttempt(
                        Guid.NewGuid(),
                        run.Id,
                        company.Id,
                        _timeProvider.GetUtcNow().UtcDateTime,
                        evaluation.SeedStateBefore);
                    skippedAttempt.MarkSkipped(
                        _timeProvider.GetUtcNow().UtcDateTime,
                        evaluation.Reason,
                        evaluation.SeedStateBefore);
                    _dbContext.FinanceSeedBackfillAttempts.Add(skippedAttempt);
                    run.UpdateProgress(
                        run.ScannedCount,
                        run.QueuedCount,
                        run.SucceededCount,
                        run.SkippedCount + 1,
                        run.FailedCount);
                    continue;
                }

                if (eligible.Count + run.QueuedCount >= options.MaxCompaniesPerRun)
                {
                    var cappedAttempt = new FinanceSeedBackfillAttempt(
                        Guid.NewGuid(),
                        run.Id,
                        company.Id,
                        _timeProvider.GetUtcNow().UtcDateTime,
                        evaluation.SeedStateBefore);
                    cappedAttempt.MarkSkipped(
                        _timeProvider.GetUtcNow().UtcDateTime,
                        FinanceSeedBackfillSkipReasons.MaxQueuedPerRunReached,
                        evaluation.SeedStateBefore);
                    _dbContext.FinanceSeedBackfillAttempts.Add(cappedAttempt);
                    run.UpdateProgress(
                        run.ScannedCount,
                        run.QueuedCount,
                        run.SucceededCount,
                        run.SkippedCount + 1,
                        run.FailedCount);
                    continue;
                }

                eligible.Add(new FinanceSeedBackfillCandidate(company.Id, evaluation.SeedStateBefore, evaluation.Reason));
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (eligible.Count == 0)
            {
                continue;
            }

            for (var index = 0; index < eligible.Count; index += options.EnqueueBatchSize)
            {
                var batch = eligible.Skip(index).Take(options.EnqueueBatchSize).ToArray();
                var results = await ScheduleBatchAsync(run.Id, batch, options, pacingState, cancellationToken);

                foreach (var result in results)
                {
                    var attempt = new FinanceSeedBackfillAttempt(
                        Guid.NewGuid(),
                        run.Id,
                        result.CompanyId,
                        result.OccurredAtUtc,
                        result.SeedStateBefore);

                    switch (result.AttemptStatus)
                    {
                        case FinanceSeedBackfillAttemptStatus.Queued:
                            attempt.MarkQueued(
                                result.BackgroundExecutionId!.Value,
                                result.IdempotencyKey!,
                                result.OccurredAtUtc,
                                FinanceSeedingState.Seeding);
                            run.UpdateProgress(
                                run.ScannedCount,
                                run.QueuedCount + 1,
                                run.SucceededCount,
                                run.SkippedCount,
                                run.FailedCount);
                            break;
                        case FinanceSeedBackfillAttemptStatus.Skipped:
                            attempt.MarkSkipped(
                                result.OccurredAtUtc,
                                result.SkipReason ?? FinanceSeedBackfillSkipReasons.IneligibleState,
                                result.SeedStateBefore);
                            run.UpdateProgress(
                                run.ScannedCount,
                                run.QueuedCount,
                                run.SucceededCount,
                                run.SkippedCount + 1,
                                run.FailedCount);
                            break;
                        default:
                            attempt.MarkFailed(
                                result.OccurredAtUtc,
                                result.ErrorDetails,
                                FinanceSeedingState.Failed);
                            run.UpdateProgress(
                                run.ScannedCount,
                                run.QueuedCount,
                                run.SucceededCount,
                                run.SkippedCount,
                                run.FailedCount + 1);
                            break;
                    }

                    _dbContext.FinanceSeedBackfillAttempts.Add(attempt);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                if (index + options.EnqueueBatchSize < eligible.Count)
                {
                    await _delayStrategy.DelayAsync(
                        TimeSpan.FromMilliseconds(Math.Max(0, options.DelayBetweenBatchesMs)),
                        cancellationToken);
                }
            }
        }
    }

    private async Task<IReadOnlyList<FinanceSeedBackfillScheduleResult>> ScheduleBatchAsync(
        Guid runId,
        IReadOnlyList<FinanceSeedBackfillCandidate> candidates,
        FinanceSeedBackfillWorkerOptions options,
        FinanceBackfillDispatchPacingState pacingState,
        CancellationToken cancellationToken)
    {
        var concurrency = Math.Max(1, options.MaxConcurrentEnqueues);
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var tasks = new List<Task<FinanceSeedBackfillScheduleResult>>(candidates.Count);

        foreach (var candidate in candidates)
        {
            await WaitForDispatchWindowAsync(options, pacingState, cancellationToken);
            await gate.WaitAsync(cancellationToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    return await _scheduler.ScheduleAsync(
                    new FinanceSeedBackfillScheduleRequest(
                        runId,
                        candidate.CompanyId,
                        candidate.SeedStateBefore,
                        candidate.EligibilityReason),
                    cancellationToken);
                }
                finally
                {
                    gate.Release();
                }
            }, cancellationToken));
        }

        return await Task.WhenAll(tasks);
    }

    private async Task<IReadOnlyList<FinanceBackfillCompanySnapshot>> LoadPageAsync(
        int pageIndex,
        int pageSize,
        CancellationToken cancellationToken) =>
        await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.FinanceSeedStatus != FinanceSeedingState.Seeded)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .Skip(pageIndex * pageSize)
            .Take(pageSize)
            .Select(x => new FinanceBackfillCompanySnapshot(
                x.Id,
                x.FinanceSeedStatus,
                x.FinanceSeededUtc,
                x.Settings))
            .ToListAsync(cancellationToken);

    private async Task WaitForDispatchWindowAsync(
        FinanceSeedBackfillWorkerOptions options,
        FinanceBackfillDispatchPacingState pacingState,
        CancellationToken cancellationToken)
    {
        var minimumInterval = ResolveMinimumDispatchInterval(options);
        if (minimumInterval <= TimeSpan.Zero)
        {
            return;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        if (pacingState.NextDispatchUtc > nowUtc)
        {
            await _delayStrategy.DelayAsync(pacingState.NextDispatchUtc - nowUtc, cancellationToken);
            nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        }

        pacingState.NextDispatchUtc = nowUtc.Add(minimumInterval);
    }

    private static TimeSpan ResolveMinimumDispatchInterval(FinanceSeedBackfillWorkerOptions options) =>
        options.RateLimitCount <= 0 || options.RateLimitWindowSeconds <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(TimeSpan.FromSeconds(options.RateLimitWindowSeconds).Ticks / options.RateLimitCount);

    private async Task<FinanceBackfillPageState> LoadPageStateAsync(Guid[] companyIds, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> accounts;
        IReadOnlyList<Guid> counterparties;
        IReadOnlyList<Guid> transactions;
        IReadOnlyList<Guid> balances;
        IReadOnlyList<Guid> policies;
        IReadOnlyList<Guid> invoices;
        IReadOnlyList<Guid> bills;

        try
        {
            accounts = await LoadCompanySetAsync(_dbContext.FinanceAccounts, companyIds, cancellationToken);
            counterparties = await LoadCompanySetAsync(_dbContext.FinanceCounterparties, companyIds, cancellationToken);
            transactions = await LoadCompanySetAsync(_dbContext.FinanceTransactions, companyIds, cancellationToken);
            balances = await LoadCompanySetAsync(_dbContext.FinanceBalances, companyIds, cancellationToken);
            policies = await LoadCompanySetAsync(_dbContext.FinancePolicyConfigurations, companyIds, cancellationToken);
            invoices = await LoadCompanySetAsync(_dbContext.FinanceInvoices, companyIds, cancellationToken);
            bills = await LoadCompanySetAsync(_dbContext.FinanceBills, companyIds, cancellationToken);
        }
        catch (SqlException ex) when (IsMissingFinanceSchema(ex))
        {
            accounts = [];
            counterparties = [];
            transactions = [];
            balances = [];
            policies = [];
            invoices = [];
            bills = [];
        }

        var latestExecutions = await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => companyIds.Contains(x.CompanyId) && x.ExecutionType == BackgroundExecutionType.FinanceSeed)
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

        return new FinanceBackfillPageState(
            new HashSet<Guid>(accounts),
            new HashSet<Guid>(counterparties),
            new HashSet<Guid>(transactions),
            new HashSet<Guid>(balances),
            new HashSet<Guid>(policies),
            new HashSet<Guid>(invoices),
            new HashSet<Guid>(bills),
            latestExecutions
                .GroupBy(x => x.CompanyId)
                .ToDictionary(x => x.Key, x => x.First()));
    }

    private static bool IsMissingFinanceSchema(SqlException exception) =>
        exception.Number == 208 &&
        exception.Message.Contains("finance_", StringComparison.OrdinalIgnoreCase);

    private static async Task<IReadOnlyList<Guid>> LoadCompanySetAsync<TEntity>(
        IQueryable<TEntity> source,
        IReadOnlyCollection<Guid> companyIds,
        CancellationToken cancellationToken)
        where TEntity : class, ICompanyOwnedEntity =>
        await source
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => companyIds.Contains(x.CompanyId))
            .Select(x => x.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);

    private static FinanceBackfillEvaluation EvaluateCompany(
        FinanceBackfillCompanySnapshot company,
        FinanceBackfillPageState state)
    {
        var metadata = FinanceSeedingMetadata.Read(company.Settings);
        var checks = state.BuildChecks(company.Id);
        var latestExecution = state.LatestExecutions.TryGetValue(company.Id, out var execution)
            ? execution
            : null;

        if (company.FinanceSeedStatus == FinanceSeedingState.Seeded || company.FinanceSeededUtc.HasValue || checks.IsComplete)
        {
            return new FinanceBackfillEvaluation(false, FinanceSeedBackfillSkipReasons.AlreadySeeded, company.FinanceSeedStatus);
        }

        if (latestExecution is not null && FinanceSeedBackfillExecutionStates.IsActive(latestExecution.Status))
        {
            return new FinanceBackfillEvaluation(false, FinanceSeedBackfillSkipReasons.ActiveExecution, company.FinanceSeedStatus);
        }

        if (company.FinanceSeedStatus == FinanceSeedingState.NotSeeded && !checks.HasAnyRecords)
        {
            return new FinanceBackfillEvaluation(true, FinanceSeedBackfillEligibilityReasons.NotSeeded, FinanceSeedingState.NotSeeded);
        }

        if (checks.HasAnyRecords && !checks.IsComplete)
        {
            return new FinanceBackfillEvaluation(
                true,
                metadata?.State == FinanceSeedingState.Seeding || company.FinanceSeedStatus == FinanceSeedingState.Seeding
                    ? FinanceSeedBackfillEligibilityReasons.PartialSeedResume
                    : FinanceSeedBackfillEligibilityReasons.OrphanedPartialSeed,
                FinanceSeedingState.Seeding);
        }

        return new FinanceBackfillEvaluation(false, FinanceSeedBackfillSkipReasons.IneligibleState, company.FinanceSeedStatus);
    }

    private static FinanceSeedBackfillRunDto MapRun(FinanceSeedBackfillRun run) =>
        new(
            run.Id,
            run.Status,
            run.StartedUtc,
            run.CompletedUtc,
            run.ScannedCount,
            run.QueuedCount,
            run.SucceededCount,
            run.SkippedCount,
            run.FailedCount,
            run.ConfigurationSnapshotJson,
            run.ErrorDetails);

    private sealed record FinanceBackfillCompanySnapshot(
        Guid Id,
        FinanceSeedingState FinanceSeedStatus,
        DateTime? FinanceSeededUtc,
        CompanySettings Settings);

    private sealed record FinanceBackfillEvaluation(
        bool IsEligible,
        string Reason,
        FinanceSeedingState SeedStateBefore);

    private sealed record FinanceSeedBackfillCandidate(
        Guid CompanyId,
        FinanceSeedingState SeedStateBefore,
        string EligibilityReason);
}

public sealed class FinanceSeedBackfillQueryService : IFinanceSeedBackfillQueryService
{
    private readonly VirtualCompanyDbContext _dbContext;

    public FinanceSeedBackfillQueryService(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<FinanceSeedBackfillRunDto>> GetRecentRunsAsync(
        GetFinanceSeedBackfillRunsQuery query,
        CancellationToken cancellationToken) =>
        await _dbContext.FinanceSeedBackfillRuns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderByDescending(x => x.StartedUtc)
            .Take(Math.Max(1, query.Limit))
            .Select(x => new FinanceSeedBackfillRunDto(
                x.Id,
                x.Status,
                x.StartedUtc,
                x.CompletedUtc,
                x.ScannedCount,
                x.QueuedCount,
                x.SucceededCount,
                x.SkippedCount,
                x.FailedCount,
                x.ConfigurationSnapshotJson,
                x.ErrorDetails))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FinanceSeedBackfillAttemptDto>> GetAttemptsAsync(Guid runId, CancellationToken cancellationToken)
    {
        var attempts = await _dbContext.FinanceSeedBackfillAttempts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.StartedUtc)
            .ToListAsync(cancellationToken);

        var executionIds = attempts
            .Where(x => x.BackgroundExecutionId.HasValue)
            .Select(x => x.BackgroundExecutionId!.Value)
            .Distinct()
            .ToArray();

        var executionLookup = executionIds.Length == 0
            ? new Dictionary<Guid, BackgroundExecution>()
            : await _dbContext.BackgroundExecutions
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => executionIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        return attempts
            .Select(x =>
            {
                executionLookup.TryGetValue(x.BackgroundExecutionId ?? Guid.Empty, out var execution);
                var status = execution?.Status switch
                {
                    BackgroundExecutionStatus.Pending or BackgroundExecutionStatus.RetryScheduled => FinanceSeedBackfillAttemptStatus.Queued,
                    BackgroundExecutionStatus.InProgress when x.Status == FinanceSeedBackfillAttemptStatus.Queued => FinanceSeedBackfillAttemptStatus.InProgress,
                    _ => x.Status
                };

                return new FinanceSeedBackfillAttemptDto(
                    x.Id,
                    x.RunId,
                    x.CompanyId,
                    status,
                    x.StartedUtc,
                    x.CompletedUtc,
                    x.SkipReason,
                    execution?.FailureCode,
                    x.ErrorDetails ?? execution?.FailureMessage,
                    Math.Max(0, (execution?.AttemptCount ?? 1) - 1),
                    x.BackgroundExecutionId,
                    x.IdempotencyKey,
                    x.SeedStateBefore,
                    x.SeedStateAfter);
            })
            .ToList();
    }
}

public sealed class FinanceSeedBackfillBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IOptions<FinanceSeedBackfillWorkerOptions> _options;
    private readonly ILogger<FinanceSeedBackfillBackgroundService> _logger;

    public FinanceSeedBackfillBackgroundService(
        IServiceScopeFactory scopeFactory,
        IDistributedLockProvider lockProvider,
        IOptions<FinanceSeedBackfillWorkerOptions> options,
        ILogger<FinanceSeedBackfillBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _lockProvider = lockProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Finance seed backfill worker is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollIntervalSeconds));
        var lockTtl = TimeSpan.FromSeconds(Math.Max(30, _options.Value.LockTtlSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var lockHandle = await _lockProvider.TryAcquireAsync(_options.Value.LockKey, lockTtl, stoppingToken);
                if (lockHandle is null)
                {
                    _logger.LogDebug("Finance seed backfill worker skipped this cycle because the distributed lock was not acquired.");
                    await Task.Delay(pollInterval, stoppingToken);
                    continue;
                }

                using var scope = _scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IFinanceSeedBackfillOrchestrator>();
                var run = await orchestrator.RunAsync(stoppingToken);

                _logger.LogInformation(
                    "Finance seed backfill run {RunId} finished. Status={Status}, Scanned={ScannedCount}, Queued={QueuedCount}, Succeeded={SucceededCount}, Skipped={SkippedCount}, Failed={FailedCount}.",
                    run.RunId,
                    run.Status.ToStorageValue(),
                    run.ScannedCount,
                    run.QueuedCount,
                    run.SucceededCount,
                    run.SkippedCount,
                    run.FailedCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Finance seed backfill worker loop failed.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }
}

internal sealed record FinanceBackfillPageState(
    HashSet<Guid> CompaniesWithAccounts,
    HashSet<Guid> CompaniesWithCounterparties,
    HashSet<Guid> CompaniesWithTransactions,
    HashSet<Guid> CompaniesWithBalances,
    HashSet<Guid> CompaniesWithPolicyConfigurations,
    HashSet<Guid> CompaniesWithInvoices,
    HashSet<Guid> CompaniesWithBills,
    IReadOnlyDictionary<Guid, BackgroundExecution> LatestExecutions)
{
    public FinanceBackfillRecordChecks BuildChecks(Guid companyId) =>
        new(
            CompaniesWithAccounts.Contains(companyId),
            CompaniesWithCounterparties.Contains(companyId),
            CompaniesWithTransactions.Contains(companyId),
            CompaniesWithBalances.Contains(companyId),
            CompaniesWithPolicyConfigurations.Contains(companyId),
            CompaniesWithInvoices.Contains(companyId),
            CompaniesWithBills.Contains(companyId));
}

internal sealed record FinanceBackfillRecordChecks(
    bool HasAccounts,
    bool HasCounterparties,
    bool HasTransactions,
    bool HasBalances,
    bool HasPolicyConfiguration,
    bool HasInvoices,
    bool HasBills)
{
    public bool IsComplete => HasAccounts && HasCounterparties && HasTransactions && HasBalances && HasPolicyConfiguration;

    public bool HasAnyRecords =>
        HasAccounts ||
        HasCounterparties ||
        HasTransactions ||
        HasBalances ||
        HasPolicyConfiguration ||
        HasInvoices ||
        HasBills;
}

internal sealed class FinanceBackfillDispatchPacingState
{
    public FinanceBackfillDispatchPacingState(DateTime nextDispatchUtc) => NextDispatchUtc = nextDispatchUtc;
    public DateTime NextDispatchUtc { get; set; }
}
