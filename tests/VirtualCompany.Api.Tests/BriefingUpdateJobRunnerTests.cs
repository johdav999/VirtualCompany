using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BriefingUpdateJobRunnerTests
{
    [Fact]
    public async Task Duplicate_idempotency_key_is_enforced_per_company()
    {
        await using var fixture = await BriefingJobFixture.CreateAsync();
        var companyId = await fixture.SeedCompanyAsync();
        var producer = new BriefingUpdateJobProducer(
            fixture.DbContext,
            NullLogger<BriefingUpdateJobProducer>.Instance);

        var command = new EnqueueBriefingUpdateJobCommand(
            companyId,
            CompanyBriefingUpdateJobTriggerTypeValues.EventDriven,
            null,
            BriefingUpdateEventTypes.TaskStatusChanged,
            "correlation-1",
            "briefing-event:test",
            null);

        var first = await producer.EnqueueAsync(command, CancellationToken.None);
        var second = await producer.EnqueueAsync(command with { CorrelationId = "correlation-2" }, CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.JobId, second.JobId);
        Assert.Equal(1, await fixture.DbContext.CompanyBriefingUpdateJobs.CountAsync());
    }

    [Fact]
    public async Task Scheduled_enqueue_persists_schedule_context_and_metadata()
    {
        await using var fixture = await BriefingJobFixture.CreateAsync();
        var companyId = await fixture.SeedCompanyAsync();
        var producer = new BriefingUpdateJobProducer(
            fixture.DbContext,
            NullLogger<BriefingUpdateJobProducer>.Instance);
        var periodStartUtc = DateTime.SpecifyKind(DateTime.Parse("2026-04-13T00:00:00Z"), DateTimeKind.Utc);
        var periodEndUtc = DateTime.SpecifyKind(DateTime.Parse("2026-04-14T00:00:00Z"), DateTimeKind.Utc);

        var result = await producer.EnqueueScheduledAsync(
            companyId,
            CompanyBriefingUpdateJobTriggerTypeValues.Daily,
            CompanyBriefingTypeValues.Daily,
            "briefing-schedule:test",
            "briefing:test:daily:20260413",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["triggerSource"] = JsonValue.Create(BriefingUpdateJobSources.Schedule),
                ["scheduleCadence"] = JsonValue.Create(CompanyBriefingUpdateJobTriggerTypeValues.Daily),
                ["periodStartUtc"] = JsonValue.Create(periodStartUtc),
                ["periodEndUtc"] = JsonValue.Create(periodEndUtc)
            },
            CancellationToken.None);

        var job = await fixture.DbContext.CompanyBriefingUpdateJobs
            .AsNoTracking()
            .SingleAsync(x => x.Id == result.JobId);

        Assert.True(result.Created);
        Assert.Equal(companyId, job.CompanyId);
        Assert.Equal(CompanyBriefingUpdateJobTriggerType.Daily, job.TriggerType);
        Assert.Equal(CompanyBriefingType.Daily, job.BriefingType);
        Assert.Null(job.EventType);
        Assert.Equal("briefing-schedule:test", job.CorrelationId);
        Assert.Equal("briefing:test:daily:20260413", job.IdempotencyKey);
        Assert.Equal(BriefingUpdateJobSources.Schedule, job.SourceMetadata["triggerSource"]!.GetValue<string>());
        Assert.Equal(CompanyBriefingUpdateJobTriggerTypeValues.Daily, job.SourceMetadata["scheduleCadence"]!.GetValue<string>());
    }

    [Fact]
    public async Task Retryable_failure_schedules_retry_and_records_error_details()
    {
        await using var fixture = await BriefingJobFixture.CreateAsync();
        var companyId = await fixture.SeedCompanyAsync();
        var job = fixture.AddJob(companyId, maxAttempts: 3);
        await fixture.DbContext.SaveChangesAsync();
        fixture.Pipeline.FailuresBeforeSuccess = 1;

        var handled = await fixture.Runner.RunDueAsync(CancellationToken.None);

        Assert.Equal(0, handled);
        Assert.Equal(CompanyBriefingUpdateJobStatus.Retrying, job.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.NotNull(job.NextAttemptAt);
        Assert.NotNull(job.LastErrorCode);
        Assert.Contains("Configured briefing pipeline failure", job.LastError);
        Assert.NotNull(job.LastErrorDetails);
    }

    [Fact]
    public async Task Exhausted_retry_marks_final_failure()
    {
        await using var fixture = await BriefingJobFixture.CreateAsync();
        var companyId = await fixture.SeedCompanyAsync();
        var job = fixture.AddJob(companyId, maxAttempts: 1);
        await fixture.DbContext.SaveChangesAsync();
        fixture.Pipeline.FailuresBeforeSuccess = 1;

        var handled = await fixture.Runner.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, handled);
        Assert.Equal(CompanyBriefingUpdateJobStatus.Failed, job.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.NotNull(job.FinalFailedAt);
        Assert.NotNull(job.CompletedAt);
        Assert.NotNull(job.LastErrorCode);
    }

    [Fact]
    public async Task Scheduled_and_event_driven_jobs_use_same_pipeline()
    {
        await using var fixture = await BriefingJobFixture.CreateAsync();
        var companyId = await fixture.SeedCompanyAsync();
        fixture.AddJob(companyId, CompanyBriefingUpdateJobTriggerType.Daily, CompanyBriefingType.Daily, eventType: null);
        fixture.AddJob(companyId, CompanyBriefingUpdateJobTriggerType.EventDriven, null, BriefingUpdateEventTypes.AgentGeneratedAlert);
        await fixture.DbContext.SaveChangesAsync();

        var handled = await fixture.Runner.RunDueAsync(CancellationToken.None);

        Assert.Equal(2, handled);
        Assert.Equal(2, fixture.Pipeline.Calls.Count);
        Assert.Contains(fixture.Pipeline.Calls, x => x.TriggerType == CompanyBriefingUpdateJobTriggerTypeValues.Daily);
        Assert.Contains(fixture.Pipeline.Calls, x => x.TriggerType == CompanyBriefingUpdateJobTriggerTypeValues.EventDriven);
    }

    private sealed class RecordingBriefingPipeline : IBriefingGenerationPipeline
    {
        private int _remainingFailures;

        public List<BriefingGenerationJobContext> Calls { get; } = [];

        public int FailuresBeforeSuccess
        {
            get => Volatile.Read(ref _remainingFailures);
            set => Volatile.Write(ref _remainingFailures, value);
        }

        public Task<CompanyBriefingGenerationResult> GenerateAsync(BriefingGenerationJobContext job, CancellationToken cancellationToken)
        {
            Calls.Add(job);
            if (Interlocked.Decrement(ref _remainingFailures) >= 0)
            {
                throw new TimeoutException("Configured briefing pipeline failure.");
            }

            var briefing = new CompanyBriefingDto(
                Guid.NewGuid(),
                job.CompanyId,
                CompanyBriefingType.Daily.ToStorageValue(),
                DateTime.UtcNow.AddDays(-1),
                DateTime.UtcNow,
                "Daily briefing",
                "Briefing body",
                [],
                [],
                null,
                DateTime.UtcNow);
            return Task.FromResult(new CompanyBriefingGenerationResult(briefing, false, 0));
        }
    }

    private sealed class BriefingJobFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private BriefingJobFixture(SqliteConnection connection, VirtualCompanyDbContext dbContext, RecordingBriefingPipeline pipeline, CompanyBriefingUpdateJobRunner runner)
        {
            _connection = connection;
            DbContext = dbContext;
            Pipeline = pipeline;
            Runner = runner;
        }

        public VirtualCompanyDbContext DbContext { get; }
        public RecordingBriefingPipeline Pipeline { get; }
        public CompanyBriefingUpdateJobRunner Runner { get; }

        public static async Task<BriefingJobFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var dbContext = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options);
            await dbContext.Database.EnsureCreatedAsync();
            var pipeline = new RecordingBriefingPipeline();
            var runner = new CompanyBriefingUpdateJobRunner(
                dbContext,
                pipeline,
                new BackgroundJobExecutor(NullLogger<BackgroundJobExecutor>.Instance, new DefaultBackgroundJobFailureClassifier()),
                new ExponentialBackgroundExecutionRetryPolicy(Options.Create(new BackgroundExecutionOptions { BaseRetryDelaySeconds = 0, MaxRetryDelaySeconds = 0 })),
                new NoopCompanyExecutionScopeFactory(),
                Options.Create(new BriefingUpdateJobWorkerOptions { BatchSize = 10, MaxAttempts = 3, ClaimTimeoutSeconds = 30 }),
                NullLogger<CompanyBriefingUpdateJobRunner>.Instance);
            return new BriefingJobFixture(connection, dbContext, pipeline, runner);
        }

        public async Task<Guid> SeedCompanyAsync()
        {
            var company = new Company(Guid.NewGuid(), "Test Company", "Software", "SaaS", "UTC", "USD", "en", "EU");
            DbContext.Companies.Add(company);
            await DbContext.SaveChangesAsync();
            return company.Id;
        }

        public CompanyBriefingUpdateJob AddJob(
            Guid companyId,
            CompanyBriefingUpdateJobTriggerType triggerType = CompanyBriefingUpdateJobTriggerType.EventDriven,
            CompanyBriefingType? briefingType = null,
            string? eventType = BriefingUpdateEventTypes.TaskStatusChanged,
            int maxAttempts = 3)
        {
            var job = new CompanyBriefingUpdateJob(
                Guid.NewGuid(),
                companyId,
                triggerType,
                briefingType,
                eventType,
                Guid.NewGuid().ToString("N"),
                Guid.NewGuid().ToString("N"),
                null,
                maxAttempts);
            DbContext.CompanyBriefingUpdateJobs.Add(job);
            return job;
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class NoopCompanyExecutionScopeFactory : ICompanyExecutionScopeFactory
    {
        public ICompanyExecutionScope BeginScope(Guid companyId) => new NoopCompanyExecutionScope();
    }

    private sealed class NoopCompanyExecutionScope : ICompanyExecutionScope
    {
        public void Dispose()
        {
        }
    }
}