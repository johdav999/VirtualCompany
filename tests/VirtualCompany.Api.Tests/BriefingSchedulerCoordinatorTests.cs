using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BriefingSchedulerCoordinatorTests
{
    [Fact]
    public async Task Run_once_skips_generation_when_lock_is_not_acquired()
    {
        var service = new RecordingBriefingService();
        var coordinator = new BriefingSchedulerCoordinator(
            new DenyLockProvider(),
            service,
            Options.Create(new BriefingSchedulerOptions
            {
                LockKey = "test-briefing-scheduler",
                LockTtlSeconds = 30,
                BatchSize = 10
            }),
            NullLogger<BriefingSchedulerCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(DateTimeOffset.Parse("2026-04-13T08:00:00Z"), CancellationToken.None);

        Assert.False(result.LockAcquired);
        Assert.Equal(0, service.CallCount);
        Assert.Equal(0, result.BriefingsGenerated);
    }

    [Fact]
    public async Task Run_once_invokes_due_generation_when_lock_is_acquired()
    {
        var service = new RecordingBriefingService(new BriefingSchedulerRunResult(true, 3, 2, 2, 0));
        var coordinator = new BriefingSchedulerCoordinator(
            new AllowLockProvider(),
            service,
            Options.Create(new BriefingSchedulerOptions
            {
                LockKey = "test-briefing-scheduler",
                LockTtlSeconds = 30,
                BatchSize = 10
            }),
            NullLogger<BriefingSchedulerCoordinator>.Instance);

        var result = await coordinator.RunOnceAsync(DateTimeOffset.Parse("2026-04-13T08:00:00Z"), CancellationToken.None);

        Assert.True(result.LockAcquired);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(10, service.LastBatchSize);
        Assert.Equal(2, result.BriefingsGenerated);
        Assert.Equal(2, result.NotificationsCreated);
    }

    private sealed class RecordingBriefingService : ICompanyBriefingService
    {
        private readonly BriefingSchedulerRunResult _result;

        public RecordingBriefingService()
            : this(new BriefingSchedulerRunResult(true, 0, 0, 0, 0))
        {
        }

        public RecordingBriefingService(BriefingSchedulerRunResult result) => _result = result;

        public int CallCount { get; private set; }
        public int LastBatchSize { get; private set; }

        public Task<BriefingAggregateResultDto> AggregateAsync(Guid companyId, GenerateCompanyBriefingCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CompanyBriefingGenerationResult> GenerateAsync(Guid companyId, GenerateCompanyBriefingCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BriefingSchedulerRunResult> GenerateDueAsync(GenerateDueBriefingsCommand command, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_result);
        }

        public Task<DashboardBriefingCardDto> GetLatestDashboardBriefingsAsync(Guid companyId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CompanyBriefingDeliveryPreferenceDto> GetDeliveryPreferenceAsync(Guid companyId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CompanyBriefingDeliveryPreferenceDto> UpdateDeliveryPreferenceAsync(Guid companyId, UpdateCompanyBriefingDeliveryPreferenceCommand command, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
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

    private sealed record TestLockHandle(string Key) : IDistributedLockHandle
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
