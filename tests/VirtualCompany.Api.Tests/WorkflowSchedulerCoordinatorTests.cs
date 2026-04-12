using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class WorkflowSchedulerCoordinatorTests
{
    [Fact]
    public async Task Run_once_skips_polling_when_lock_is_not_acquired()
    {
        var polling = new RecordingPollingService();
        var coordinator = new WorkflowSchedulerCoordinator(
            new DenyLockProvider(),
            polling,
            Options.Create(new WorkflowSchedulerOptions
            {
                LockKey = "test-workflow-scheduler",
                LockTtlSeconds = 30,
                BatchSize = 10
            }),
            NullLogger<WorkflowSchedulerCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(DateTimeOffset.Parse("2026-04-12T08:00:00Z"), CancellationToken.None);

        Assert.False(result.LockAcquired);
        Assert.Equal(0, polling.CallCount);
        Assert.Equal(0, result.WorkflowsStarted);
    }

    [Fact]
    public async Task In_memory_lock_provider_allows_one_holder_per_key()
    {
        var provider = new InMemoryDistributedLockProvider();

        await using var first = await provider.TryAcquireAsync("scheduler", TimeSpan.FromMinutes(1), CancellationToken.None);
        var second = await provider.TryAcquireAsync("scheduler", TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.NotNull(first);
        Assert.Null(second);

        await first!.DisposeAsync();
        await using var third = await provider.TryAcquireAsync("scheduler", TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.NotNull(third);
    }

    [Fact]
    public async Task Run_once_invokes_polling_when_lock_is_acquired()
    {
        var polling = new RecordingPollingService(new WorkflowSchedulerRunResult(true, 2, 3, 0));
        var coordinator = new WorkflowSchedulerCoordinator(
            new AllowLockProvider(),
            polling,
            Options.Create(new WorkflowSchedulerOptions
            {
                LockKey = "test-workflow-scheduler",
                LockTtlSeconds = 30,
                BatchSize = 10
            }),
            NullLogger<WorkflowSchedulerCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(DateTimeOffset.Parse("2026-04-12T08:00:00Z"), CancellationToken.None);

        Assert.True(result.LockAcquired);
        Assert.Equal(1, polling.CallCount);
        Assert.Equal(10, polling.LastBatchSize);
        Assert.Equal(3, result.WorkflowsStarted);
    }

    private sealed class RecordingPollingService : IWorkflowSchedulePollingService
    {
        private readonly WorkflowSchedulerRunResult _result;

        public RecordingPollingService()
            : this(new WorkflowSchedulerRunResult(true, 0, 0, 0))
        {
        }

        public RecordingPollingService(WorkflowSchedulerRunResult result) => _result = result;

        public int CallCount { get; private set; }
        public int LastBatchSize { get; private set; }

        public Task<WorkflowSchedulerRunResult> RunDueSchedulesAsync(
            DateTime scheduledAtUtc,
            int batchSize,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastBatchSize = batchSize;
            return Task.FromResult(_result);
        }
    }

    private sealed class DenyLockProvider : IDistributedLockProvider
    {
        public Task<IDistributedLockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken) =>
            Task.FromResult<IDistributedLockHandle?>(null);
    }

    private sealed class AllowLockProvider : IDistributedLockProvider
    {
        public Task<IDistributedLockHandle?> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken cancellationToken) =>
            Task.FromResult<IDistributedLockHandle?>(new TestLockHandle(key));
    }

    private sealed class TestLockHandle : IDistributedLockHandle
    {
        public TestLockHandle(string key) => Key = key;

        public string Key { get; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
