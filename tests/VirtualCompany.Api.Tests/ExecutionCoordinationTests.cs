using Microsoft.Extensions.Options;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Infrastructure.BackgroundJobs;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ExecutionCoordinationTests
{
    [Fact]
    public async Task In_memory_coordination_allows_one_lock_owner_per_key()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-04-13T09:00:00Z"));
        var store = CreateStore(timeProvider);
        var scope = new ExecutionCoordinationLockScope(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "Workflow Progression", "instance-1", "corr-1");

        var first = await store.TryAcquireLockAsync(scope, TimeSpan.FromMinutes(1), CancellationToken.None);
        var second = await store.TryAcquireLockAsync(scope, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal("vc:lock:company:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa:workflow-progression:instance-1", first!.Key);
    }

    [Fact]
    public async Task In_memory_coordination_releases_only_matching_owner()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-04-13T09:00:00Z"));
        var store = CreateStore(timeProvider);
        var scope = new ExecutionCoordinationLockScope(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "retry-worker", "execution-1");
        var lease = await store.TryAcquireLockAsync(scope, TimeSpan.FromMinutes(1), CancellationToken.None);

        var wrongOwnerReleased = await store.ReleaseLockAsync(lease! with { OwnerToken = "wrong-owner" }, CancellationToken.None);
        var stillBlocked = await store.TryAcquireLockAsync(scope, TimeSpan.FromMinutes(1), CancellationToken.None);
        var released = await store.ReleaseLockAsync(lease!, CancellationToken.None);
        var reacquired = await store.TryAcquireLockAsync(scope, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.False(wrongOwnerReleased);
        Assert.Null(stillBlocked);
        Assert.True(released);
        Assert.NotNull(reacquired);
    }

    [Fact]
    public async Task In_memory_coordination_lets_expired_lock_be_reacquired()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-04-13T09:00:00Z"));
        var store = CreateStore(timeProvider);
        var scope = new ExecutionCoordinationLockScope(null, "scheduler", "singleton");

        var first = await store.TryAcquireLockAsync(scope, TimeSpan.FromSeconds(5), CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(6));
        var second = await store.TryAcquireLockAsync(scope, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.OwnerToken, second!.OwnerToken);
    }

    [Fact]
    public async Task In_memory_coordination_round_trips_tenant_execution_state()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-04-13T09:00:00Z"));
        var store = CreateStore(timeProvider);
        var companyId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var key = new ExecutionStateKey(companyId, "execution-42", "corr-42");

        await store.SetStateAsync(key, new TestExecutionState("running", 3), TimeSpan.FromMinutes(5), CancellationToken.None);
        var loaded = await store.GetStateAsync<TestExecutionState>(key, CancellationToken.None);
        var isolated = await store.GetStateAsync<TestExecutionState>(
            new ExecutionStateKey(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), "execution-42", "corr-42"),
            CancellationToken.None);
        var deleted = await store.DeleteStateAsync(key, CancellationToken.None);
        var afterDelete = await store.GetStateAsync<TestExecutionState>(key, CancellationToken.None);

        Assert.Equal(new TestExecutionState("running", 3), loaded);
        Assert.Null(isolated);
        Assert.True(deleted);
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task In_memory_coordination_expires_execution_state_by_ttl()
    {
        var timeProvider = new ManualTimeProvider(DateTimeOffset.Parse("2026-04-13T09:00:00Z"));
        var store = CreateStore(timeProvider);
        var key = new ExecutionStateKey(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), "execution-ttl", "corr-ttl");

        await store.SetStateAsync(key, new TestExecutionState("heartbeat", 1), TimeSpan.FromSeconds(5), CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(6));
        var expired = await store.GetStateAsync<TestExecutionState>(key, CancellationToken.None);

        Assert.Null(expired);
    }

    private static InMemoryExecutionCoordinationService CreateStore(TimeProvider timeProvider) =>
        new(
            Options.Create(new RedisExecutionCoordinationOptions
            {
                KeyPrefix = "vc",
                DefaultLockLeaseSeconds = 120,
                DefaultExecutionStateTtlSeconds = 3600
            }),
            timeProvider);

    private sealed record TestExecutionState(string Status, int Attempt);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
