using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class WorkflowProgressionCoordinatorTests
{
    [Fact]
    public async Task Run_once_skips_progression_when_lock_is_not_acquired()
    {
        var progression = new RecordingProgressionService();
        var coordinator = new WorkflowProgressionCoordinator(
            new DenyLockProvider(),
            progression,
            Options.Create(new WorkflowProgressionOptions
            {
                LockKey = "test-workflow-progression",
                LockTtlSeconds = 30,
                BatchSize = 10
            }),
            NullLogger<WorkflowProgressionCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(DateTimeOffset.Parse("2026-04-13T08:00:00Z"), CancellationToken.None);

        Assert.False(result.LockAcquired);
        Assert.Equal(0, progression.CallCount);
        Assert.Equal(0, result.InstancesAdvanced);
    }

    [Fact]
    public async Task Run_once_invokes_progression_when_lock_is_acquired()
    {
        var progression = new RecordingProgressionService(new WorkflowProgressionRunResult(true, 4, 3, 1));
        var coordinator = new WorkflowProgressionCoordinator(
            new AllowLockProvider(),
            progression,
            Options.Create(new WorkflowProgressionOptions
            {
                LockKey = "test-workflow-progression",
                LockTtlSeconds = 30,
                BatchSize = 12
            }),
            NullLogger<WorkflowProgressionCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(DateTimeOffset.Parse("2026-04-13T08:00:00Z"), CancellationToken.None);

        Assert.True(result.LockAcquired);
        Assert.Equal(1, progression.CallCount);
        Assert.Equal(12, progression.LastBatchSize);
        Assert.Equal(4, result.InstancesScanned);
        Assert.Equal(3, result.InstancesAdvanced);
        Assert.Equal(1, result.Failures);
    }

    private sealed class RecordingProgressionService : IWorkflowProgressionService
    {
        private readonly WorkflowProgressionRunResult _result;

        public RecordingProgressionService()
            : this(new WorkflowProgressionRunResult(true, 0, 0, 0))
        {
        }

        public RecordingProgressionService(WorkflowProgressionRunResult result) => _result = result;

        public int CallCount { get; private set; }
        public int LastBatchSize { get; private set; }

        public Task<WorkflowProgressionRunResult> RunRunnableInstancesAsync(
            DateTime utcNow,
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