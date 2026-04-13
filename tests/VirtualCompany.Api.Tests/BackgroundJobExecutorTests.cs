using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Application.Workflows;
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
            new BackgroundJobExecutionContext("company-outbox:delivery", 1, 3, companyId, "corr-123", "idem-123"),
            _ => throw new TimeoutException("Temporary timeout."),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(BackgroundJobExecutionOutcome.RetryScheduled, result.Outcome);
        Assert.Equal("corr-123", result.CorrelationId);
        Assert.Equal("idem-123", result.IdempotencyKey);
        Assert.Equal("System.TimeoutException", result.ExceptionType);
        Assert.Equal(TimeSpan.FromSeconds(5), result.RetryDelay);
        Assert.Equal(BackgroundJobFailureClassification.ExternalDependencyTimeout, result.FailureClassification);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Equal("company-outbox:delivery", AssertScopeValue(entry.Scope, "JobName"));
        Assert.Equal(1, AssertScopeValue(entry.Scope, "Attempt"));
        Assert.Equal(3, AssertScopeValue(entry.Scope, "MaxAttempts"));
        Assert.Equal(companyId, AssertScopeValue(entry.Scope, "CompanyId"));
        Assert.Equal("corr-123", AssertScopeValue(entry.Scope, "CorrelationId"));
        Assert.Equal("idem-123", AssertScopeValue(entry.Scope, "IdempotencyKey"));
    }

    [Fact]
    public async Task ExecuteAsync_marks_policy_and_business_failures_blocked_without_retry()
    {
        var logger = new ScopeCapturingLogger<BackgroundJobExecutor>();
        var executor = new BackgroundJobExecutor(logger, new DefaultBackgroundJobFailureClassifier());

        var result = await executor.ExecuteAsync(
            new BackgroundJobExecutionContext("company-outbox:unsupported-topic", 1, 3, correlationId: "corr-permanent"),
            _ => throw new UnauthorizedAccessException("Policy denied."),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(BackgroundJobExecutionOutcome.Blocked, result.Outcome);
        Assert.Equal("Policy denied.", result.ErrorMessage);
        Assert.Equal(BackgroundJobFailureClassification.PermanentPolicy, result.FailureClassification);
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
        Assert.Equal(BackgroundJobFailureClassification.ExternalDependencyTimeout, result.FailureClassification);

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
    public async Task ExecuteAsync_rejects_tenant_scoped_job_without_company_context()
    {
        var logger = new ScopeCapturingLogger<BackgroundJobExecutor>();
        var executor = new BackgroundJobExecutor(logger, new DefaultBackgroundJobFailureClassifier());
        var invoked = false;

        var result = await executor.ExecuteAsync(
            new BackgroundJobExecutionContext(
                "workflow-progression:advance-instance",
                1,
                3,
                requireCompanyContext: true),
            _ =>
            {
                invoked = true;
                return Task.CompletedTask;
            },
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.False(invoked);
        Assert.Equal(BackgroundJobExecutionOutcome.PermanentFailure, result.Outcome);
        Assert.Equal(BackgroundJobFailureClassification.PermanentBusinessRule, result.FailureClassification);
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

    [Fact]
    public async Task ExecuteAsync_treats_internal_task_cancellation_as_timeout_not_host_cancellation()
    {
        var logger = new ScopeCapturingLogger<BackgroundJobExecutor>();
        var executor = new BackgroundJobExecutor(logger, new DefaultBackgroundJobFailureClassifier());

        var result = await executor.ExecuteAsync(
            new BackgroundJobExecutionContext("provider-call", 1, 3, correlationId: "corr-timeout"),
            _ => throw new TaskCanceledException("Provider timed out."),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(BackgroundJobExecutionOutcome.RetryScheduled, result.Outcome);
        Assert.Equal(BackgroundJobFailureClassification.ExternalDependencyTimeout, result.FailureClassification);
    }

    [Fact]
    public async Task ExecuteAsync_bounds_unknown_failures_before_terminal_failure()
    {
        var logger = new ScopeCapturingLogger<BackgroundJobExecutor>();
        var executor = new BackgroundJobExecutor(logger, new DefaultBackgroundJobFailureClassifier());

        var retry = await executor.ExecuteAsync(
            new BackgroundJobExecutionContext("unknown-job", 1, 2, correlationId: "corr-unknown"),
            _ => throw new Exception("Unknown failure."),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        var exhausted = await executor.ExecuteAsync(
            new BackgroundJobExecutionContext("unknown-job", 2, 2, correlationId: "corr-unknown"),
            _ => throw new Exception("Unknown failure."),
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(BackgroundJobExecutionOutcome.RetryScheduled, retry.Outcome);
        Assert.Equal(BackgroundJobFailureClassification.Unknown, retry.FailureClassification);
        Assert.Equal(BackgroundJobExecutionOutcome.RetryExhausted, exhausted.Outcome);
    }

    [Fact]
    public void Classifier_distinguishes_concurrency_and_validation_failures()
    {
        var classifier = new DefaultBackgroundJobFailureClassifier();

        Assert.Equal(
            BackgroundJobFailureClassification.LockContention,
            classifier.Classify(new DbUpdateConcurrencyException("stale row")));
        Assert.Equal(
            BackgroundJobFailureClassification.Validation,
            classifier.Classify(new ArgumentException("bad workflow definition")));
    }

    [Fact]
    public void Classifier_marks_business_policy_approval_and_workflow_validation_as_non_retryable()
    {
        var classifier = new DefaultBackgroundJobFailureClassifier();

        Assert.Equal(
            BackgroundJobFailureClassification.Validation,
            classifier.Classify(new WorkflowValidationException(new Dictionary<string, string[]>
            {
                ["definitionJson"] = ["Invalid workflow definition."]
            })));
        Assert.Equal(
            BackgroundJobFailureClassification.PermanentPolicy,
            classifier.Classify(new UnauthorizedAccessException("Policy denied.")));
        Assert.Equal(
            BackgroundJobFailureClassification.PermanentBusinessRule,
            classifier.Classify(new InvalidOperationException("Business rule failed.")));
        Assert.Equal(
            BackgroundJobFailureClassification.Configuration,
            classifier.Classify(new KeyNotFoundException("Missing workflow definition.")));
        Assert.Equal(
            BackgroundJobFailureClassification.ApprovalRequired,
            classifier.Classify(new WorkflowBlockedException("Approval required.")));

        Assert.False(BackgroundJobFailureClassification.Validation.IsRetryable());
        Assert.False(BackgroundJobFailureClassification.PermanentPolicy.IsRetryable());
        Assert.False(BackgroundJobFailureClassification.PermanentBusinessRule.IsRetryable());
        Assert.False(BackgroundJobFailureClassification.Configuration.IsRetryable());
        Assert.False(BackgroundJobFailureClassification.ApprovalRequired.IsRetryable());
    }

    [Fact]
    public void Classifier_marks_rate_limit_as_retryable()
    {
        var classifier = new DefaultBackgroundJobFailureClassifier();

        var classification = classifier.Classify(new HttpRequestException("Rate limited.", null, HttpStatusCode.TooManyRequests));

        Assert.Equal(BackgroundJobFailureClassification.RateLimited, classification);
        Assert.True(classification.IsRetryable());
    }

    [Fact]
    public async Task ExecuteAsync_derives_stable_idempotency_key_from_logical_execution_identity()
    {
        var logger = new ScopeCapturingLogger<BackgroundJobExecutor>();
        var executor = new BackgroundJobExecutor(logger, new DefaultBackgroundJobFailureClassifier());
        var companyId = Guid.NewGuid();

        var first = await executor.ExecuteAsync(
            new BackgroundJobExecutionContext("workflow-progression:advance-instance", 1, 3, companyId, "corr-stable"),
            _ => Task.CompletedTask,
            TimeSpan.Zero,
            CancellationToken.None);
        var retry = await executor.ExecuteAsync(
            new BackgroundJobExecutionContext("workflow-progression:advance-instance", 2, 3, companyId, "corr-stable"),
            _ => Task.CompletedTask,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.Equal(first.IdempotencyKey, retry.IdempotencyKey);
        Assert.Equal("corr-stable", retry.CorrelationId);
        Assert.NotEqual(first.CorrelationId, (await executor.ExecuteAsync(new BackgroundJobExecutionContext("workflow-progression:advance-instance", 1, 3, companyId), _ => Task.CompletedTask, TimeSpan.Zero, CancellationToken.None)).CorrelationId);
    }

    private static object? AssertScopeValue(IReadOnlyDictionary<string, object?> scope, string key) =>
        scope.TryGetValue(key, out var value)
            ? value
            : throw new XunitException($"Expected scope value '{key}'.");
}
