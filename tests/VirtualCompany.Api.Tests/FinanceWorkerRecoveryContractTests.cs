using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceWorkerRecoveryContractTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Background_execution_retains_operator_evidence_and_uses_versions_for_each_transition()
    {
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var retryable = CreateExecution(companyId, BackgroundExecutionType.FinanceSeed);

        retryable.RecordLease("finance-worker-1", UtcNow.AddMinutes(5), UtcNow);
        retryable.StartAttempt("corr-1", 1, 3);
        retryable.MarkFailed(BackgroundExecutionFailureCategory.ExternalDependencyTimeout, "provider_timeout", "The provider timed out.");
        var failedVersion = retryable.Version;

        retryable.Queue(UtcNow.AddMinutes(1), "manual-retry-correlation", resetAttempts: true);

        Assert.Equal(BackgroundExecutionStatus.Pending, retryable.Status);
        Assert.Equal(0, retryable.AttemptCount);
        Assert.Null(retryable.LeaseOwner);
        Assert.Null(retryable.LeaseExpiresUtc);
        Assert.Null(retryable.FailureCategory);
        Assert.Equal(failedVersion + 1, retryable.Version);

        var stoppable = CreateExecution(companyId, BackgroundExecutionType.FinanceReportRegeneration);
        stoppable.Cancel(actorId, "The reporting period was reopened.", UtcNow);

        Assert.Equal(BackgroundExecutionStatus.Cancelled, stoppable.Status);
        Assert.Equal(actorId, stoppable.CancelledByUserId);
        Assert.Equal("The reporting period was reopened.", stoppable.CancellationReason);
        Assert.True(stoppable.IsTerminal);

        var permanent = CreateExecution(companyId, BackgroundExecutionType.FinanceInsightRefresh);
        permanent.StartAttempt("corr-2", 1, 1);
        permanent.MarkFailed(BackgroundExecutionFailureCategory.PoisonPayload, "invalid_snapshot", "The snapshot descriptor is invalid.");
        permanent.Acknowledge(actorId, "The invalid request was removed at its source.", UtcNow);

        Assert.Equal(actorId, permanent.AcknowledgedByUserId);
        Assert.Equal("The invalid request was removed at its source.", permanent.Acknowledgement);
        Assert.Throws<InvalidOperationException>(() => permanent.Cancel(actorId, "Too late to stop.", UtcNow));
    }

    [Fact]
    public async Task Attempt_recorder_closes_abandoned_attempt_and_resumes_with_retained_history()
    {
        var companyId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var execution = CreateExecution(companyId, BackgroundExecutionType.FinanceReportRegeneration);
        execution.RecordLease("worker-before-restart", UtcNow.AddMinutes(-1), UtcNow.AddMinutes(-6));
        execution.StartAttempt("corr-recovery", 1, 3);
        var abandoned = new BackgroundExecutionAttempt(Guid.NewGuid(), companyId, execution.Id,
            "report-regeneration", 1, "worker-before-restart", UtcNow.AddMinutes(-1), UtcNow.AddMinutes(-6));
        db.Companies.Add(new Company(companyId, "Finance recovery company"));
        db.BackgroundExecutions.Add(execution);
        db.BackgroundExecutionAttempts.Add(abandoned);
        await db.SaveChangesAsync();

        var recorder = new FinanceBackgroundExecutionAttemptRecorder(db, new FixedTimeProvider(UtcNow));
        var resumed = await recorder.StartAsync(execution, "report-regeneration", CancellationToken.None);

        Assert.Equal(2, resumed.AttemptNumber);
        Assert.Equal(BackgroundExecutionAttemptOutcomes.InProgress, resumed.Outcome);
        Assert.Equal(2, execution.AttemptCount);
        var attempts = await db.BackgroundExecutionAttempts.IgnoreQueryFilters()
            .OrderBy(x => x.AttemptNumber).ToListAsync();
        Assert.Equal(2, attempts.Count);
        Assert.Equal(BackgroundExecutionAttemptOutcomes.LeaseExpired, attempts[0].Outcome);
        Assert.Equal(BackgroundExecutionFailureCategory.TransientInfrastructure, attempts[0].FailureCategory);
        Assert.NotNull(attempts[0].CompletedUtc);
    }

    [Fact]
    public void Recovery_configuration_rejects_unbounded_or_invalid_operator_limits()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{FinanceWorkerRecoveryOptions.SectionName}:BacklogWarningMinutes"] = "0",
            [$"{FinanceWorkerRecoveryOptions.SectionName}:LeaseGraceSeconds"] = "-1",
            [$"{FinanceWorkerRecoveryOptions.SectionName}:MaximumVisibleItems"] = "5000"
        }).Build();
        var services = new ServiceCollection();
        services.AddFinanceInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<FinanceWorkerRecoveryOptions>>().Value);
    }

    [Fact]
    public async Task Missing_worker_configuration_fails_readiness_with_operator_safe_evidence()
    {
        var check = new FinanceWorkerReadinessHealthCheck(new ConfigurationBuilder().Build());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("finance_worker_configuration_missing", result.Data["readinessCode"]);
        Assert.NotEmpty(Assert.IsType<string[]>(result.Data["missingSections"]));
    }

    [Fact]
    public void Worker_metrics_include_company_scope_without_payload_content()
    {
        var observed = new ConcurrentBag<(string Name, IReadOnlyDictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == FinanceWorkerOperationsTelemetry.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            observed.Add((instrument.Name, tags.ToArray().ToDictionary(x => x.Key, x => x.Value))));
        listener.Start();
        var companyId = Guid.NewGuid();
        var telemetry = new FinanceWorkerOperationsTelemetry();

        telemetry.OperatorAction("retry", companyId, "finance-seed", "queued");
        telemetry.ObserveHealth(new(companyId, "attention", UtcNow, 3, 1, 0, 1, 0, 0,
            UtcNow.AddMinutes(-5), [], ["Backlog requires attention."]));

        Assert.Contains(observed, x => x.Name == "finance.worker.operator_actions" &&
            Equals(x.Tags["company_id"], companyId.ToString("D")));
        Assert.Contains(observed, x => x.Name == "finance.worker.backlog");
        Assert.Contains(observed, x => x.Name == "finance.worker.failures");
        Assert.DoesNotContain(observed.SelectMany(x => x.Tags.Values), value =>
            value?.ToString()?.Contains("payload", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static BackgroundExecution CreateExecution(Guid companyId, BackgroundExecutionType type) =>
        new(Guid.NewGuid(), companyId, type, BackgroundExecutionRelatedEntityTypes.FinanceSeed,
            Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), 3);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
