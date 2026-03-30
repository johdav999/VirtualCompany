using Microsoft.Extensions.Logging;
using VirtualCompany.Infrastructure.BackgroundJobs;
using Xunit;
using Xunit.Sdk;

namespace VirtualCompany.Api.Tests;

public sealed class BackgroundJobExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_schedules_retry_for_transient_failure_and_enriches_scope()
    {
        var logger = new ScopeCapturingLogger<BackgroundJobExecutor>();
        var executor = new BackgroundJobExecutor(logger, new DefaultBackgroundJobFailureClassifier());
        var companyId = Guid.NewGuid();

        var result = await executor.ExecuteAsync(
            new BackgroundJobExecutionContext("company-outbox:delivery", 1, 3, companyId, "corr-123"),
            _ => throw new TimeoutException("Temporary timeout."),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(BackgroundJobExecutionOutcome.RetryScheduled, result.Outcome);
        Assert.Equal("corr-123", result.CorrelationId);
        Assert.Equal("System.TimeoutException", result.ExceptionType);
        Assert.Equal(TimeSpan.FromSeconds(5), result.RetryDelay);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Equal("company-outbox:delivery", AssertScopeValue(entry.Scope, "JobName"));
        Assert.Equal(1, AssertScopeValue(entry.Scope, "Attempt"));
        Assert.Equal(3, AssertScopeValue(entry.Scope, "MaxAttempts"));
        Assert.Equal(companyId, AssertScopeValue(entry.Scope, "CompanyId"));
        Assert.Equal("corr-123", AssertScopeValue(entry.Scope, "CorrelationId"));
    }

    [Fact]
    public async Task ExecuteAsync_marks_permanent_failure_without_retry()
    {
        var logger = new ScopeCapturingLogger<BackgroundJobExecutor>();
        var executor = new BackgroundJobExecutor(logger, new DefaultBackgroundJobFailureClassifier());

        var result = await executor.ExecuteAsync(
            new BackgroundJobExecutionContext("company-outbox:unsupported-topic", 1, 3, correlationId: "corr-permanent"),
            _ => throw new PermanentBackgroundJobException("Unsupported payload."),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(BackgroundJobExecutionOutcome.PermanentFailure, result.Outcome);
        Assert.Equal("Unsupported payload.", result.ErrorMessage);
        Assert.NotEqual("System.TimeoutException", result.ExceptionType);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.Equal("company-outbox:unsupported-topic", AssertScopeValue(entry.Scope, "JobName"));
        Assert.Equal("corr-permanent", AssertScopeValue(entry.Scope, "CorrelationId"));
    }

    [Fact]
    public async Task ExecuteAsync_marks_retry_exhausted_when_transient_failure_reaches_max_attempts()
    {
        var logger = new ScopeCapturingLogger<BackgroundJobExecutor>();
        var executor = new BackgroundJobExecutor(logger, new DefaultBackgroundJobFailureClassifier());

        var result = await executor.ExecuteAsync(
            new BackgroundJobExecutionContext("company-outbox:delivery", 3, 3, correlationId: "corr-exhausted"),
            _ => throw new TimeoutException("Still timing out."),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(BackgroundJobExecutionOutcome.RetryExhausted, result.Outcome);
        Assert.Equal("System.TimeoutException", result.ExceptionType);
        Assert.Null(result.RetryDelay);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.Equal(3, AssertScopeValue(entry.Scope, "Attempt"));
        Assert.Equal(3, AssertScopeValue(entry.Scope, "MaxAttempts"));
    }

    [Fact]
    public async Task ExecuteAsync_logs_success_after_retry_and_generates_correlation_when_missing()
    {
        var logger = new ScopeCapturingLogger<BackgroundJobExecutor>();
        var executor = new BackgroundJobExecutor(logger, new DefaultBackgroundJobFailureClassifier());
        var companyId = Guid.NewGuid();

        var result = await executor.ExecuteAsync(
            new BackgroundJobExecutionContext("company-outbox:delivery", 2, 3, companyId),
            _ => Task.CompletedTask,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(BackgroundJobExecutionOutcome.Succeeded, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.CorrelationId));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.LogLevel);
        Assert.Equal("company-outbox:delivery", AssertScopeValue(entry.Scope, "JobName"));
        Assert.Equal(2, AssertScopeValue(entry.Scope, "Attempt"));
        Assert.Equal(3, AssertScopeValue(entry.Scope, "MaxAttempts"));
        Assert.Equal(companyId, AssertScopeValue(entry.Scope, "CompanyId"));
        Assert.Equal(result.CorrelationId, AssertScopeValue(entry.Scope, "CorrelationId"));
    }

    [Fact]
    public async Task ExecuteAsync_does_not_swallow_cancellation()
    {
        var logger = new ScopeCapturingLogger<BackgroundJobExecutor>();
        var executor = new BackgroundJobExecutor(logger, new DefaultBackgroundJobFailureClassifier());
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => executor.ExecuteAsync(
            new BackgroundJobExecutionContext("company-outbox:delivery", 1, 3),
            _ => Task.FromCanceled(cancellationTokenSource.Token),
            TimeSpan.Zero,
            cancellationTokenSource.Token));

        Assert.Empty(logger.Entries);
    }

    private static object? AssertScopeValue(IReadOnlyDictionary<string, object?> scope, string key) =>
        scope.TryGetValue(key, out var value)
            ? value
            : throw new XunitException($"Expected scope value '{key}'.");
}
