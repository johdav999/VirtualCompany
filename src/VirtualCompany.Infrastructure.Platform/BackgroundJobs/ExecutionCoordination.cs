using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Workflows;

namespace VirtualCompany.Infrastructure.BackgroundJobs;

public sealed class RedisExecutionCoordinationOptions
{
    public const string SectionName = "RedisExecutionCoordination";

    public string KeyPrefix { get; set; } = "vc";
    public int DefaultLockLeaseSeconds { get; set; } = 120;
    public int DefaultExecutionStateTtlSeconds { get; set; } = 3600;
}

public sealed class RedisExecutionCoordinationService :
    IExecutionCoordinationStore,
    IExecutionCoordinationKeyBuilder,
    IDistributedLockProvider
{
    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('del', KEYS[1])
        end

        return 0
        """;

    private const string RenewScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
            return redis.call('pexpire', KEYS[1], ARGV[2])
        end

        return 0
        """;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IOptions<RedisExecutionCoordinationOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RedisExecutionCoordinationService> _logger;

    public RedisExecutionCoordinationService(
        IConnectionMultiplexer connectionMultiplexer,
        IOptions<RedisExecutionCoordinationOptions> options,
        TimeProvider timeProvider,
        ILogger<RedisExecutionCoordinationService> logger)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ExecutionCoordinationLockLease?> TryAcquireLockAsync(
        ExecutionCoordinationLockScope scope,
        TimeSpan? leaseDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = BuildLockKey(scope);
        var token = Guid.NewGuid().ToString("N");
        var ttl = ResolveLockLease(leaseDuration);
        var acquired = await _connectionMultiplexer
            .GetDatabase()
            .StringSetAsync(key, token, ttl, When.NotExists);

        if (!acquired)
        {
            _logger.LogDebug(
                "Redis coordination lock {LockKey} was not acquired for company {CompanyId}, category {LockCategory}, resource {LockResource}, correlation {CorrelationId}.",
                key,
                scope.CompanyId,
                scope.Category,
                scope.Resource,
                scope.CorrelationId);
            return null;
        }

        var lease = new ExecutionCoordinationLockLease(
            key,
            token,
            _timeProvider.GetUtcNow().Add(ttl),
            scope.CompanyId,
            NormalizeSegment(scope.Category, "general"),
            NormalizeSegment(scope.Resource, "resource"),
            scope.CorrelationId);

        _logger.LogInformation(
            "Redis coordination lock {LockKey} acquired for company {CompanyId}, category {LockCategory}, resource {LockResource}, correlation {CorrelationId}, TTL {LockTtlSeconds} seconds.",
            lease.Key,
            lease.CompanyId,
            lease.Category,
            lease.Resource,
            lease.CorrelationId,
            ttl.TotalSeconds);

        return lease;
    }

    public async Task<bool> RenewLockAsync(
        ExecutionCoordinationLockLease lease,
        TimeSpan? leaseDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ttl = ResolveLockLease(leaseDuration);
        var result = await _connectionMultiplexer
            .GetDatabase()
            .ScriptEvaluateAsync(
                RenewScript,
                [(RedisKey)lease.Key],
                [(RedisValue)lease.OwnerToken, (RedisValue)((long)ttl.TotalMilliseconds)]);

        var renewed = (long)result == 1;
        if (!renewed)
        {
            _logger.LogWarning(
                "Redis coordination lock {LockKey} was not renewed because owner token did not match or the lease expired.",
                lease.Key);
        }

        return renewed;
    }

    public async Task<bool> ReleaseLockAsync(
        ExecutionCoordinationLockLease lease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await _connectionMultiplexer
            .GetDatabase()
            .ScriptEvaluateAsync(
                ReleaseScript,
                [(RedisKey)lease.Key],
                [(RedisValue)lease.OwnerToken]);

        var released = (long)result == 1;
        if (!released)
        {
            _logger.LogWarning(
                "Redis coordination lock {LockKey} was not released because owner token did not match or the lease expired.",
                lease.Key);
        }
        else
        {
            _logger.LogDebug("Redis coordination lock {LockKey} released.", lease.Key);
        }

        return released;
    }

    public async Task SetStateAsync<TState>(
        ExecutionStateKey key,
        TState state,
        TimeSpan? ttl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = BuildExecutionStateKey(key);
        var stateTtl = ResolveStateTtl(ttl);
        var payload = JsonSerializer.Serialize(state, SerializerOptions);

        await _connectionMultiplexer
            .GetDatabase()
            .StringSetAsync(redisKey, payload, stateTtl);

        _logger.LogDebug(
            "Redis execution state {ExecutionStateKey} set for company {CompanyId}, execution {ExecutionId}, correlation {CorrelationId}, TTL {StateTtlSeconds} seconds.",
            redisKey,
            key.CompanyId,
            key.ExecutionId,
            key.CorrelationId,
            stateTtl.TotalSeconds);
    }

    public async Task<TState?> GetStateAsync<TState>(
        ExecutionStateKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = BuildExecutionStateKey(key);
        var payload = await _connectionMultiplexer
            .GetDatabase()
            .StringGetAsync(redisKey);

        return payload.HasValue
            ? JsonSerializer.Deserialize<TState>(payload!, SerializerOptions)
            : default;
    }

    public async Task<bool> DeleteStateAsync(
        ExecutionStateKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var redisKey = BuildExecutionStateKey(key);
        var deleted = await _connectionMultiplexer
            .GetDatabase()
            .KeyDeleteAsync(redisKey);

        _logger.LogDebug(
            "Redis execution state {ExecutionStateKey} delete for company {CompanyId}, execution {ExecutionId}, correlation {CorrelationId} returned {Deleted}.",
            redisKey,
            key.CompanyId,
            key.ExecutionId,
            key.CorrelationId,
            deleted);

        return deleted;
    }

    public string BuildLockKey(ExecutionCoordinationLockScope scope)
    {
        var tenantScope = scope.CompanyId.HasValue
            ? $"company:{scope.CompanyId.Value:N}"
            : "system";
        return $"{NormalizePrefix()}:lock:{tenantScope}:{NormalizeSegment(scope.Category, "general")}:{NormalizeSegment(scope.Resource, "resource")}";
    }

    public string BuildExecutionStateKey(ExecutionStateKey key)
    {
        if (key.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required for tenant execution state.", nameof(key));
        }

        return $"{NormalizePrefix()}:execstate:company:{key.CompanyId:N}:{NormalizeSegment(key.ExecutionId, "execution")}";
    }

    async Task<IDistributedLockHandle?> IDistributedLockProvider.TryAcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var lease = await TryAcquireLockAsync(
            new ExecutionCoordinationLockScope(null, "legacy", key),
            ttl,
            cancellationToken);

        return lease is null
            ? null
            : new ExecutionCoordinationDistributedLockHandle(this, lease);
    }

    private TimeSpan ResolveLockLease(TimeSpan? requestedLease)
    {
        if (requestedLease.HasValue && requestedLease.Value > TimeSpan.Zero)
        {
            return requestedLease.Value;
        }

        return TimeSpan.FromSeconds(Math.Max(5, _options.Value.DefaultLockLeaseSeconds));
    }

    private TimeSpan ResolveStateTtl(TimeSpan? requestedTtl)
    {
        if (requestedTtl.HasValue && requestedTtl.Value > TimeSpan.Zero)
        {
            return requestedTtl.Value;
        }

        return TimeSpan.FromSeconds(Math.Max(1, _options.Value.DefaultExecutionStateTtlSeconds));
    }

    private string NormalizePrefix() => NormalizeSegment(_options.Value.KeyPrefix, "vc");

    private static string NormalizeSegment(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append('-');
            }
        }

        var normalized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private sealed class ExecutionCoordinationDistributedLockHandle : IDistributedLockHandle
    {
        private readonly IExecutionCoordinationStore _store;
        private readonly ExecutionCoordinationLockLease _lease;
        private int _disposed;

        public ExecutionCoordinationDistributedLockHandle(
            IExecutionCoordinationStore store,
            ExecutionCoordinationLockLease lease)
        {
            _store = store;
            _lease = lease;
            Key = lease.Key;
        }

        public string Key { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            await _store.ReleaseLockAsync(_lease, CancellationToken.None);
        }
    }
}

public sealed class InMemoryExecutionCoordinationService :
    IExecutionCoordinationStore,
    IExecutionCoordinationKeyBuilder,
    IDistributedLockProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, InMemoryLease> _leases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, InMemoryState> _states = new(StringComparer.Ordinal);
    private readonly IOptions<RedisExecutionCoordinationOptions> _options;
    private readonly TimeProvider _timeProvider;

    public InMemoryExecutionCoordinationService(
        IOptions<RedisExecutionCoordinationOptions> options,
        TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    public Task<ExecutionCoordinationLockLease?> TryAcquireLockAsync(
        ExecutionCoordinationLockScope scope,
        TimeSpan? leaseDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = BuildLockKey(scope);
        var token = Guid.NewGuid().ToString("N");
        var ttl = ResolveLockLease(leaseDuration);
        var expiresUtc = _timeProvider.GetUtcNow().Add(ttl);

        while (true)
        {
            if (_leases.TryGetValue(key, out var existing))
            {
                if (existing.ExpiresAtUtc > _timeProvider.GetUtcNow())
                {
                    return Task.FromResult<ExecutionCoordinationLockLease?>(null);
                }

                var replacement = new InMemoryLease(token, expiresUtc);
                if (!_leases.TryUpdate(key, replacement, existing))
                {
                    continue;
                }
            }
            else
            {
                var lease = new InMemoryLease(token, expiresUtc);
                if (!_leases.TryAdd(key, lease))
                {
                    continue;
                }
            }

            return Task.FromResult<ExecutionCoordinationLockLease?>(
                new ExecutionCoordinationLockLease(
                    key,
                    token,
                    expiresUtc,
                    scope.CompanyId,
                    NormalizeSegment(scope.Category, "general"),
                    NormalizeSegment(scope.Resource, "resource"),
                    scope.CorrelationId));
        }
    }

    public Task<bool> RenewLockAsync(
        ExecutionCoordinationLockLease lease,
        TimeSpan? leaseDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_leases.TryGetValue(lease.Key, out var existing) ||
            existing.OwnerToken != lease.OwnerToken ||
            existing.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            return Task.FromResult(false);
        }

        var ttl = ResolveLockLease(leaseDuration);
        var renewed = _leases.TryUpdate(
            lease.Key,
            existing with { ExpiresAtUtc = _timeProvider.GetUtcNow().Add(ttl) },
            existing);

        return Task.FromResult(renewed);
    }

    public Task<bool> ReleaseLockAsync(
        ExecutionCoordinationLockLease lease,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_leases.TryGetValue(lease.Key, out var existing) ||
            existing.OwnerToken != lease.OwnerToken)
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_leases.TryRemove(new KeyValuePair<string, InMemoryLease>(lease.Key, existing)));
    }

    public Task SetStateAsync<TState>(
        ExecutionStateKey key,
        TState state,
        TimeSpan? ttl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stateKey = BuildExecutionStateKey(key);
        var payload = JsonSerializer.Serialize(state, SerializerOptions);
        _states[stateKey] = new InMemoryState(payload, _timeProvider.GetUtcNow().Add(ResolveStateTtl(ttl)));
        return Task.CompletedTask;
    }

    public Task<TState?> GetStateAsync<TState>(
        ExecutionStateKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stateKey = BuildExecutionStateKey(key);
        if (!_states.TryGetValue(stateKey, out var state))
        {
            return Task.FromResult<TState?>(default);
        }

        if (state.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            _states.TryRemove(stateKey, out _);
            return Task.FromResult<TState?>(default);
        }

        return Task.FromResult(JsonSerializer.Deserialize<TState>(state.Payload, SerializerOptions));
    }

    public Task<bool> DeleteStateAsync(
        ExecutionStateKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_states.TryRemove(BuildExecutionStateKey(key), out _));
    }

    public string BuildLockKey(ExecutionCoordinationLockScope scope)
    {
        var tenantScope = scope.CompanyId.HasValue
            ? $"company:{scope.CompanyId.Value:N}"
            : "system";
        return $"{NormalizePrefix()}:lock:{tenantScope}:{NormalizeSegment(scope.Category, "general")}:{NormalizeSegment(scope.Resource, "resource")}";
    }

    public string BuildExecutionStateKey(ExecutionStateKey key)
    {
        if (key.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required for tenant execution state.", nameof(key));
        }

        return $"{NormalizePrefix()}:execstate:company:{key.CompanyId:N}:{NormalizeSegment(key.ExecutionId, "execution")}";
    }

    async Task<IDistributedLockHandle?> IDistributedLockProvider.TryAcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var lease = await TryAcquireLockAsync(
            new ExecutionCoordinationLockScope(null, "legacy", key),
            ttl,
            cancellationToken);

        return lease is null
            ? null
            : new ExecutionCoordinationDistributedLockHandle(this, lease);
    }

    private TimeSpan ResolveLockLease(TimeSpan? requestedLease)
    {
        if (requestedLease.HasValue && requestedLease.Value > TimeSpan.Zero)
        {
            return requestedLease.Value;
        }

        return TimeSpan.FromSeconds(Math.Max(5, _options.Value.DefaultLockLeaseSeconds));
    }

    private TimeSpan ResolveStateTtl(TimeSpan? requestedTtl)
    {
        if (requestedTtl.HasValue && requestedTtl.Value > TimeSpan.Zero)
        {
            return requestedTtl.Value;
        }

        return TimeSpan.FromSeconds(Math.Max(1, _options.Value.DefaultExecutionStateTtlSeconds));
    }

    private string NormalizePrefix() => NormalizeSegment(_options.Value.KeyPrefix, "vc");

    private static string NormalizeSegment(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append('-');
            }
        }

        var normalized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private sealed record InMemoryLease(string OwnerToken, DateTimeOffset ExpiresAtUtc);

    private sealed record InMemoryState(string Payload, DateTimeOffset ExpiresAtUtc);

    private sealed class ExecutionCoordinationDistributedLockHandle : IDistributedLockHandle
    {
        private readonly IExecutionCoordinationStore _store;
        private readonly ExecutionCoordinationLockLease _lease;
        private int _disposed;

        public ExecutionCoordinationDistributedLockHandle(
            IExecutionCoordinationStore store,
            ExecutionCoordinationLockLease lease)
        {
            _store = store;
            _lease = lease;
            Key = lease.Key;
        }

        public string Key { get; }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            await _store.ReleaseLockAsync(_lease, CancellationToken.None);
        }
    }
}
