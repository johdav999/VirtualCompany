using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class WorkflowSchedulerOptions
{
    public const string SectionName = "WorkflowScheduler";

    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
    public int LockTtlSeconds { get; set; } = 120;
    public int BatchSize { get; set; } = 50;
    public string LockKey { get; set; } = "workflow-scheduler";
}

public sealed class WorkflowSchedulerCoordinator : IWorkflowSchedulerCoordinator
{
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IWorkflowSchedulePollingService _pollingService;
    private readonly IOptions<WorkflowSchedulerOptions> _options;
    private readonly ILogger<WorkflowSchedulerCoordinator> _logger;

    public WorkflowSchedulerCoordinator(
        IDistributedLockProvider lockProvider,
        IWorkflowSchedulePollingService pollingService,
        IOptions<WorkflowSchedulerOptions> options,
        ILogger<WorkflowSchedulerCoordinator> logger)
    {
        _lockProvider = lockProvider;
        _pollingService = pollingService;
        _options = options;
        _logger = logger;
    }

    public async Task<WorkflowSchedulerRunResult> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var lockKey = string.IsNullOrWhiteSpace(options.LockKey) ? "workflow-scheduler" : options.LockKey.Trim();
        var lockTtl = TimeSpan.FromSeconds(Math.Max(5, options.LockTtlSeconds));
        var batchSize = Math.Max(1, options.BatchSize);

        await using var handle = await _lockProvider.TryAcquireAsync(lockKey, lockTtl, cancellationToken);
        if (handle is null)
        {
            _logger.LogInformation(
                "Workflow scheduler skipped polling because distributed lock {LockKey} was not acquired.",
                lockKey);
            return new WorkflowSchedulerRunResult(false, 0, 0, 0);
        }

        _logger.LogInformation(
            "Workflow scheduler acquired distributed lock {LockKey} with TTL {LockTtlSeconds} seconds.",
            lockKey,
            lockTtl.TotalSeconds);

        var result = await _pollingService.RunDueSchedulesAsync(now.UtcDateTime, batchSize, cancellationToken);
        _logger.LogInformation(
            "Workflow scheduler polling completed. Companies scanned: {CompaniesScanned}. Workflows started: {WorkflowsStarted}. Failures: {Failures}.",
            result.CompaniesScanned,
            result.WorkflowsStarted,
            result.Failures);

        return result;
    }
}

public sealed class WorkflowSchedulePollingService : IWorkflowSchedulePollingService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IWorkflowScheduleTriggerService _scheduleTriggerService;
    private readonly ILogger<WorkflowSchedulePollingService> _logger;
    private readonly IBackgroundJobExecutor _backgroundJobExecutor;
    private readonly IBackgroundExecutionRecorder _backgroundExecutionRecorder;
    private readonly IBackgroundExecutionRetryPolicy _retryPolicy;
    private readonly IBackgroundExecutionIdentityFactory _identityFactory;
    private readonly ICompanyExecutionScopeFactory _companyExecutionScopeFactory;

    public WorkflowSchedulePollingService(
        VirtualCompanyDbContext dbContext,
        IWorkflowScheduleTriggerService scheduleTriggerService,
        IBackgroundJobExecutor backgroundJobExecutor,
        IBackgroundExecutionRecorder backgroundExecutionRecorder,
        IBackgroundExecutionRetryPolicy retryPolicy,
        IBackgroundExecutionIdentityFactory identityFactory,
        ICompanyExecutionScopeFactory companyExecutionScopeFactory,
        ILogger<WorkflowSchedulePollingService> logger)
    {
        _dbContext = dbContext;
        _scheduleTriggerService = scheduleTriggerService;
        _backgroundJobExecutor = backgroundJobExecutor;
        _backgroundExecutionRecorder = backgroundExecutionRecorder;
        _retryPolicy = retryPolicy;
        _identityFactory = identityFactory;
        _companyExecutionScopeFactory = companyExecutionScopeFactory;
        _logger = logger;
    }

    public async Task<WorkflowSchedulerRunResult> RunDueSchedulesAsync(
        DateTime scheduledAtUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var effectiveBatchSize = Math.Max(1, batchSize);
        var dueAtUtc = scheduledAtUtc.Kind == DateTimeKind.Utc
            ? scheduledAtUtc
            : scheduledAtUtc.ToUniversalTime();

        var companyIds = await ResolveDueCompanyIdsAsync(effectiveBatchSize, cancellationToken);
        if (companyIds.Count == 0)
        {
            _logger.LogDebug("Workflow scheduler found no companies with active scheduled workflow definitions.");
            return new WorkflowSchedulerRunResult(true, 0, 0, 0);
        }

        _logger.LogInformation(
            "Workflow scheduler polling {CompanyCount} company scope(s) for due scheduled workflows at {ScheduledAtUtc}.",
            companyIds.Count,
            dueAtUtc);

        var startedCount = 0;
        var failureCount = 0;
        foreach (var companyId in companyIds)
        {
            using var tenantScope = _companyExecutionScopeFactory.BeginScope(companyId);
            var correlationId = $"workflow-scheduler:{companyId:N}:{dueAtUtc:yyyyMMddHHmm}";
            var executionIdentity = _identityFactory.Create(
                companyId,
                "workflow-scheduler",
                correlationId,
                dueAtUtc.ToString("yyyyMMddHHmm"));
            var executionRecord = await _backgroundExecutionRecorder.StartAsync(
                companyId,
                BackgroundExecutionType.ScheduledWorkflow,
                BackgroundExecutionRelatedEntityTypes.Schedule,
                dueAtUtc.ToString("yyyyMMddHHmm"),
                executionIdentity.CorrelationId,
                executionIdentity.IdempotencyKey,
                attempt: 1,
                maxAttempts: 1,
                cancellationToken);

            var execution = await _backgroundJobExecutor.ExecuteAsync(
                new BackgroundJobExecutionContext(
                    "workflow-scheduler:poll-company",
                    attempt: 1,
                    maxAttempts: 1,
                    companyId,
                    executionIdentity.CorrelationId,
                    executionIdentity.IdempotencyKey,
                    requireCompanyContext: true),
                async innerCancellationToken =>
                {
                    var started = await _scheduleTriggerService.StartDueScheduledWorkflowsAsync(
                        companyId,
                        new TriggerScheduledWorkflowsCommand(
                            dueAtUtc,
                            ScheduleKey: null,
                            ContextJson: new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["triggeredBy"] = System.Text.Json.Nodes.JsonValue.Create("workflow-scheduler"),
                                ["correlationId"] = System.Text.Json.Nodes.JsonValue.Create(executionIdentity.CorrelationId),
                                ["idempotencyKey"] = System.Text.Json.Nodes.JsonValue.Create(executionIdentity.IdempotencyKey)
                            }),
                        innerCancellationToken);

                    startedCount += started.Count;
                    if (started.Count > 0)
                    {
                        _logger.LogInformation(
                            "Workflow scheduler started {StartedCount} scheduled workflow instance(s) for company {CompanyId}.",
                            started.Count,
                            companyId);
                    }
                },
                _retryPolicy.GetRetryDelay(1),
                cancellationToken);

            await _backgroundExecutionRecorder.ApplyOutcomeAsync(
                executionRecord,
                execution,
                execution.Outcome == BackgroundJobExecutionOutcome.RetryScheduled ? DateTime.UtcNow.Add(execution.RetryDelay ?? TimeSpan.Zero) : null,
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (execution.Outcome != BackgroundJobExecutionOutcome.Succeeded)
            {
                failureCount++;
                _logger.LogError(
                    "Workflow scheduler failed while processing scheduled workflows for company {CompanyId}. Failure: {FailureMessage}.",
                    companyId,
                    execution.ErrorMessage);
            }
        }

        return new WorkflowSchedulerRunResult(true, companyIds.Count, startedCount, failureCount);
    }

    private async Task<IReadOnlyList<Guid>> ResolveDueCompanyIdsAsync(int batchSize, CancellationToken cancellationToken)
    {
        var companyIds = await _dbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId.HasValue &&
                x.Active &&
                x.TriggerType == WorkflowTriggerType.Schedule)
            .Select(x => x.CompanyId!.Value)
            .Distinct()
            .OrderBy(x => x)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var hasSystemScheduledDefinition = await _dbContext.WorkflowDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x =>
                x.CompanyId == null &&
                x.Active &&
                x.TriggerType == WorkflowTriggerType.Schedule,
                cancellationToken);

        if (hasSystemScheduledDefinition && companyIds.Count < batchSize)
        {
            var remaining = batchSize - companyIds.Count;
            var existing = companyIds.ToHashSet();
            var systemCompanyIds = await _dbContext.Companies
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => !existing.Contains(x.Id))
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .Take(remaining)
                .ToListAsync(cancellationToken);

            companyIds.AddRange(systemCompanyIds);
        }

        return companyIds;
    }
}

public sealed class WorkflowSchedulerBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<WorkflowSchedulerOptions> _options;
    private readonly ILogger<WorkflowSchedulerBackgroundService> _logger;

    public WorkflowSchedulerBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<WorkflowSchedulerOptions> options,
        ILogger<WorkflowSchedulerBackgroundService> logger)
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
            _logger.LogInformation("Workflow scheduler background service is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollIntervalSeconds));
        _logger.LogInformation(
            "Workflow scheduler background service started with poll interval {PollIntervalSeconds} seconds, lock TTL {LockTtlSeconds} seconds, and batch size {BatchSize}.",
            pollInterval.TotalSeconds,
            Math.Max(5, _options.Value.LockTtlSeconds),
            Math.Max(1, _options.Value.BatchSize));

        using var timer = new PeriodicTimer(pollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerCoordinator>();
                await coordinator.RunOnceAsync(_timeProvider.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow scheduler polling loop failed unexpectedly.");
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

        _logger.LogInformation("Workflow scheduler background service stopped.");
    }
}

public sealed class RedisDistributedLockProvider : IDistributedLockProvider
{
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        end

        return 0
        """;

    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<RedisDistributedLockProvider> _logger;

    public RedisDistributedLockProvider(
        IConnectionMultiplexer connectionMultiplexer,
        ILogger<RedisDistributedLockProvider> logger)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _logger = logger;
    }

    public async Task<IDistributedLockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedKey = NormalizeKey(key);
        var token = Guid.NewGuid().ToString("N");
        var acquired = await _connectionMultiplexer
            .GetDatabase()
            .StringSetAsync(normalizedKey, token, ttl, When.NotExists);

        if (!acquired)
        {
            _logger.LogDebug("Redis distributed lock {LockKey} was already held.", normalizedKey);
            return null;
        }

        return new RedisDistributedLockHandle(_connectionMultiplexer, normalizedKey, token, _logger);
    }

    private static string NormalizeKey(string key) =>
        $"locks:{(string.IsNullOrWhiteSpace(key) ? "workflow-scheduler" : key.Trim())}";

    private sealed class RedisDistributedLockHandle : IDistributedLockHandle
    {
        private readonly IConnectionMultiplexer _connectionMultiplexer;
        private readonly string _token;
        private readonly ILogger _logger;
        private int _disposed;

        public RedisDistributedLockHandle(
            IConnectionMultiplexer connectionMultiplexer,
            string key,
            string token,
            ILogger logger)
        {
            _connectionMultiplexer = connectionMultiplexer;
            Key = key;
            _token = token;
            _logger = logger;
        }

        public string Key { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            var result = await _connectionMultiplexer
                .GetDatabase()
                .ScriptEvaluateAsync(ReleaseScript, [Key], [_token]);

            _logger.LogDebug(
                "Redis distributed lock {LockKey} release completed with result {ReleaseResult}.",
                Key,
                result);
        }
    }
}

public sealed class InMemoryDistributedLockProvider : IDistributedLockProvider
{
    private readonly ConcurrentDictionary<string, InMemoryLease> _leases = new(StringComparer.Ordinal);

    public Task<IDistributedLockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedKey = string.IsNullOrWhiteSpace(key) ? "workflow-scheduler" : key.Trim();
        var token = Guid.NewGuid().ToString("N");
        var expiresUtc = DateTimeOffset.UtcNow.Add(ttl);

        while (true)
        {
            if (_leases.TryGetValue(normalizedKey, out var existing))
            {
                if (existing.ExpiresUtc > DateTimeOffset.UtcNow)
                {
                    return Task.FromResult<IDistributedLockHandle?>(null);
                }

                var refreshedLease = new InMemoryLease(token, expiresUtc);
                if (_leases.TryUpdate(normalizedKey, refreshedLease, existing))
                {
                    return Task.FromResult<IDistributedLockHandle?>(new InMemoryDistributedLockHandle(normalizedKey, token, _leases));
                }

                continue;
            }

            var lease = new InMemoryLease(token, expiresUtc);
            if (_leases.TryAdd(normalizedKey, lease))
            {
                return Task.FromResult<IDistributedLockHandle?>(new InMemoryDistributedLockHandle(normalizedKey, token, _leases));
            }
        }
    }

    private sealed record InMemoryLease(string Token, DateTimeOffset ExpiresUtc);

    private sealed class InMemoryDistributedLockHandle : IDistributedLockHandle
    {
        private readonly string _token;
        private readonly ConcurrentDictionary<string, InMemoryLease> _leases;
        private int _disposed;

        public InMemoryDistributedLockHandle(
            string key,
            string token,
            ConcurrentDictionary<string, InMemoryLease> leases)
        {
            Key = key;
            _token = token;
            _leases = leases;
        }

        public string Key { get; }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0 &&
                _leases.TryGetValue(Key, out var lease) &&
                lease.Token == _token)
            {
                _leases.TryRemove(Key, out _);
            }

            return ValueTask.CompletedTask;
        }
    }
}
