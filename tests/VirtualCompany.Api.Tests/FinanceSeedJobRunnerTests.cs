using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSeedJobRunnerTests
{
    [Fact]
    public async Task Runner_marks_finance_seed_job_completed_and_records_started_and_completed_audits()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Finance Seed Runner Company"));
        dbContext.BackgroundExecutions.Add(new BackgroundExecution(
            Guid.NewGuid(),
            companyId,
            BackgroundExecutionType.FinanceSeed,
            BackgroundExecutionRelatedEntityTypes.FinanceSeed,
            companyId.ToString("D"),
            "finance-seed-runner",
            $"finance-seed:{companyId:N}",
            maxAttempts: 5));
        await dbContext.SaveChangesAsync();

        var telemetry = new TestFinanceSeedTelemetry();
        var logger = new ScopeCapturingLogger<CompanyFinanceSeedJobRunner>();
        var runner = CreateRunner(
            dbContext,
            new CompanyFinanceSeedBootstrapService(dbContext),
            new AuditEventWriter(dbContext),
            telemetry,
            logger);

        var handled = await runner.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, handled);

        var execution = await dbContext.BackgroundExecutions.SingleAsync(x => x.CompanyId == companyId);
        Assert.Equal(BackgroundExecutionStatus.Succeeded, execution.Status);

        var company = await dbContext.Companies.SingleAsync(x => x.Id == companyId);
        Assert.Equal(FinanceSeedingState.FullySeeded, company.FinanceSeedStatus);
        Assert.NotNull(company.FinanceSeededUtc);

        var actions = await dbContext.AuditEvents
            .Where(x => x.CompanyId == companyId)
            .Select(x => x.Action)
            .ToListAsync();

        Assert.Contains("finance.seed.job.started", actions);
        Assert.Contains("finance.seed.job.completed", actions);

        var startedEvent = Assert.Single(telemetry.Events.Where(x => x.EventName == FinanceSeedTelemetryEventNames.Started));
        Assert.Equal(companyId, startedEvent.Context.CompanyId);
        Assert.Equal(FinanceEntrySources.FinanceEntry, startedEvent.Context.TriggerSource);
        Assert.Equal(FinanceSeedingState.NotSeeded, startedEvent.Context.SeedStateBefore);
        Assert.Equal(FinanceSeedingState.Seeding, startedEvent.Context.SeedStateAfter);
        Assert.Equal(1, startedEvent.Context.Attempt);
        Assert.Equal(FinanceSeedRequestModes.Replace, startedEvent.Context.SeedMode);
        Assert.Equal(AuditActorTypes.System, startedEvent.Context.ActorType);

        var completedEvent = Assert.Single(telemetry.Events.Where(x => x.EventName == FinanceSeedTelemetryEventNames.Completed));
        Assert.Equal(companyId, completedEvent.Context.CompanyId);
        Assert.Equal(FinanceSeedingState.Seeding, completedEvent.Context.SeedStateBefore);
        Assert.Equal(FinanceSeedingState.Seeded, completedEvent.Context.SeedStateAfter);
        Assert.NotNull(completedEvent.Context.DurationMs);
        Assert.True(completedEvent.Context.DurationMs >= 0);
        Assert.Equal(FinanceSeedRequestModes.Replace, completedEvent.Context.SeedMode);
        Assert.Equal(AuditActorTypes.System, completedEvent.Context.ActorType);

        var startedLog = Assert.Single(logger.Entries.Where(x => x.Message.Contains("Finance seed orchestration started", StringComparison.Ordinal)));
        Assert.Equal(companyId, Assert.IsType<Guid>(startedLog.State["CompanyId"]));
        Assert.Equal(FinanceEntrySources.FinanceEntry, Assert.IsType<string>(startedLog.State["TriggerSource"]));
        Assert.Equal(FinanceSeedRequestModes.Replace, Assert.IsType<string>(startedLog.State["SeedMode"]));
        Assert.Equal(1, Assert.IsType<int>(startedLog.State["Attempt"]));

        var completedLog = Assert.Single(logger.Entries.Where(x => x.Message.Contains("Finance seed orchestration completed", StringComparison.Ordinal)));
        Assert.Equal(companyId, Assert.IsType<Guid>(completedLog.State["CompanyId"]));
        Assert.Equal(1, Assert.IsType<int>(completedLog.State["Attempt"]));
        Assert.Equal(FinanceSeedRequestModes.Replace, Assert.IsType<string>(completedLog.State["SeedMode"]));
        Assert.NotNull(completedLog.State["DurationMs"]);
    }

    [Fact]
    public async Task Runner_marks_finance_seed_job_failed_and_records_failure_audit_when_generation_exhausts_retries()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Finance Seed Failure Company"));
        dbContext.BackgroundExecutions.Add(new BackgroundExecution(
            Guid.NewGuid(),
            companyId,
            BackgroundExecutionType.FinanceSeed,
            BackgroundExecutionRelatedEntityTypes.FinanceSeed,
            companyId.ToString("D"),
            "finance-seed-failure",
            $"finance-seed:{companyId:N}",
            maxAttempts: 1));
        await dbContext.SaveChangesAsync();

        var telemetry = new TestFinanceSeedTelemetry();
        var logger = new ScopeCapturingLogger<CompanyFinanceSeedJobRunner>();
        var runner = CreateRunner(
            dbContext,
            new ThrowingFinanceSeedBootstrapService(),
            new AuditEventWriter(dbContext),
            telemetry,
            logger);

        var handled = await runner.RunDueAsync(CancellationToken.None);

        Assert.Equal(1, handled);

        var execution = await dbContext.BackgroundExecutions.SingleAsync(x => x.CompanyId == companyId);
        Assert.Equal(BackgroundExecutionStatus.Failed, execution.Status);
        Assert.Contains("Configured finance seed failure.", execution.FailureMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var company = await dbContext.Companies.SingleAsync(x => x.Id == companyId);
        Assert.Equal(FinanceSeedingState.Failed, company.FinanceSeedStatus);

        var actions = await dbContext.AuditEvents
            .Where(x => x.CompanyId == companyId)
            .Select(x => x.Action)
            .ToListAsync();

        Assert.Contains("finance.seed.job.started", actions);
        Assert.Contains("finance.seed.job.failed", actions);

        var startedEvent = Assert.Single(telemetry.Events.Where(x => x.EventName == FinanceSeedTelemetryEventNames.Started));
        Assert.Equal(companyId, startedEvent.Context.CompanyId);
        Assert.Equal(FinanceSeedingState.NotSeeded, startedEvent.Context.SeedStateBefore);
        Assert.Equal(FinanceSeedingState.Seeding, startedEvent.Context.SeedStateAfter);

        var failedEvent = Assert.Single(telemetry.Events.Where(x => x.EventName == FinanceSeedTelemetryEventNames.Failed));
        Assert.Equal(companyId, failedEvent.Context.CompanyId);
        Assert.Equal(FinanceSeedingState.Seeding, failedEvent.Context.SeedStateBefore);
        Assert.Equal(FinanceSeedingState.Failed, failedEvent.Context.SeedStateAfter);
        Assert.Equal(nameof(InvalidOperationException), failedEvent.Context.ErrorType);
        Assert.Contains("Configured finance seed failure.", failedEvent.Context.ErrorMessageSafe ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(failedEvent.Context.DurationMs);
        Assert.True(failedEvent.Context.DurationMs >= 0);
        Assert.Equal(FinanceSeedRequestModes.Replace, failedEvent.Context.SeedMode);
        Assert.Equal(AuditActorTypes.System, failedEvent.Context.ActorType);

        var failedLog = Assert.Single(logger.Entries.Where(x => x.Message.Contains("Finance seed orchestration failed", StringComparison.Ordinal)));
        Assert.Equal(companyId, Assert.IsType<Guid>(failedLog.State["CompanyId"]));
        Assert.Equal(1, Assert.IsType<int>(failedLog.State["Attempt"]));
        Assert.Equal(nameof(InvalidOperationException), Assert.IsType<string>(failedLog.State["ErrorType"]));
        Assert.Equal(FinanceSeedRequestModes.Replace, Assert.IsType<string>(failedLog.State["SeedMode"]));
        Assert.NotNull(failedLog.State["DurationMs"]);
    }

    [Fact]
    public async Task Runner_uses_backfill_retry_policy_and_keeps_backfill_attempt_queued_for_transient_failures()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Finance Seed Retry Company"));

        var run = new FinanceSeedBackfillRun(Guid.NewGuid(), DateTime.UtcNow, "{}");
        var execution = new BackgroundExecution(
            Guid.NewGuid(),
            companyId,
            BackgroundExecutionType.FinanceSeed,
            BackgroundExecutionRelatedEntityTypes.FinanceSeed,
            run.Id.ToString("D"),
            "finance-seed-backfill-retry",
            $"finance-seed:{companyId:N}",
            maxAttempts: 5);

        var attempt = new FinanceSeedBackfillAttempt(
            Guid.NewGuid(),
            run.Id,
            companyId,
            DateTime.UtcNow,
            FinanceSeedingState.NotSeeded);
        attempt.MarkQueued(execution.Id, execution.IdempotencyKey, DateTime.UtcNow, FinanceSeedingState.Seeding);

        dbContext.FinanceSeedBackfillRuns.Add(run);
        dbContext.BackgroundExecutions.Add(execution);
        dbContext.FinanceSeedBackfillAttempts.Add(attempt);
        await dbContext.SaveChangesAsync();

        var telemetry = new TestFinanceSeedTelemetry();
        var logger = new ScopeCapturingLogger<CompanyFinanceSeedJobRunner>();
        var runner = CreateRunner(
            dbContext,
            new TimeoutFinanceSeedBootstrapService(),
            new AuditEventWriter(dbContext),
            telemetry,
            logger,
            new BackgroundExecutionOptions { BaseRetryDelaySeconds = 0, MaxRetryDelaySeconds = 0 },
            new FinanceSeedBackfillWorkerOptions { BaseRetryDelaySeconds = 7, RetryBackoffMultiplier = 3d, MaxRetryDelaySeconds = 90 });

        var handled = await runner.RunDueAsync(CancellationToken.None);

        Assert.Equal(0, handled);

        var refreshedExecution = await dbContext.BackgroundExecutions.SingleAsync(x => x.Id == execution.Id);
        Assert.Equal(BackgroundExecutionStatus.RetryScheduled, refreshedExecution.Status);
        Assert.NotNull(refreshedExecution.NextRetryUtc);
        Assert.InRange((refreshedExecution.NextRetryUtc!.Value - refreshedExecution.UpdatedUtc).TotalSeconds, 6.5d, 7.5d);
        Assert.Equal(FinanceSeedBackfillAttemptStatus.Queued, (await dbContext.FinanceSeedBackfillAttempts.SingleAsync(x => x.Id == attempt.Id)).Status);
    }

    private static CompanyFinanceSeedJobRunner CreateRunner(
        VirtualCompanyDbContext dbContext,
        IFinanceSeedBootstrapService bootstrapService,
        IAuditEventWriter auditEventWriter,
        IFinanceSeedTelemetry telemetry,
        ScopeCapturingLogger<CompanyFinanceSeedJobRunner> logger,
        BackgroundExecutionOptions? executionOptions = null,
        FinanceSeedBackfillWorkerOptions? backfillOptions = null)
    {
        var companyContextAccessor = new RequestCompanyContextAccessor();
        return new CompanyFinanceSeedJobRunner(
            dbContext,
            bootstrapService,
            new BackgroundJobExecutor(
                NullLogger<BackgroundJobExecutor>.Instance,
                new DefaultBackgroundJobFailureClassifier(),
                new DefaultBackgroundExecutionIdentityFactory()),
            new ExponentialBackgroundExecutionRetryPolicy(Options.Create(executionOptions ?? new BackgroundExecutionOptions
            {
                BaseRetryDelaySeconds = 0,
                MaxRetryDelaySeconds = 0
            })),
            new CompanyExecutionScopeFactory(companyContextAccessor),
            telemetry,
            auditEventWriter,
            Options.Create(backfillOptions ?? new FinanceSeedBackfillWorkerOptions()),
            Options.Create(new FinanceSeedWorkerOptions
            {
                BatchSize = 10,
                ClaimTimeoutSeconds = 300
            }),
            TimeProvider.System,
            logger);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options);

    private sealed class ThrowingFinanceSeedBootstrapService : IFinanceSeedBootstrapService
    {
        public Task<FinanceSeedBootstrapResultDto> GenerateAsync(FinanceSeedBootstrapCommand command, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Configured finance seed failure.");
    }

    private sealed class TimeoutFinanceSeedBootstrapService : IFinanceSeedBootstrapService
    {
        public Task<FinanceSeedBootstrapResultDto> GenerateAsync(FinanceSeedBootstrapCommand command, CancellationToken cancellationToken) =>
            throw new TimeoutException("Transient finance seed timeout.");
    }
}