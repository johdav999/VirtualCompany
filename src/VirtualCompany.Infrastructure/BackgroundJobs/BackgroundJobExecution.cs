using System.IO;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Infrastructure.Observability;

namespace VirtualCompany.Infrastructure.BackgroundJobs;

public enum BackgroundJobFailureClassification
{
    Transient = 0,
    Permanent = 1
}

public enum BackgroundJobExecutionOutcome
{
    Succeeded = 0,
    RetryScheduled = 1,
    PermanentFailure = 2,
    RetryExhausted = 3
}

public class PermanentBackgroundJobException : Exception
{
    public PermanentBackgroundJobException(string message)
        : base(message)
    {
    }

    public PermanentBackgroundJobException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class BackgroundJobExecutionContext
{
    public BackgroundJobExecutionContext(
        string jobName,
        int attempt,
        int maxAttempts,
        Guid? companyId = null,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(jobName))
        {
            throw new ArgumentException("JobName is required.", nameof(jobName));
        }

        if (attempt <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt must be greater than zero.");
        }

        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be greater than zero.");
        }

        JobName = jobName.Trim();
        Attempt = attempt;
        MaxAttempts = maxAttempts;
        CompanyId = companyId;
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim();
    }

    public string JobName { get; }
    public int Attempt { get; }
    public int MaxAttempts { get; }
    public Guid? CompanyId { get; }
    public string? CorrelationId { get; }
}

public sealed record BackgroundJobExecutionResult(
    BackgroundJobExecutionOutcome Outcome,
    string CorrelationId,
    string? ErrorMessage,
    string? ExceptionType,
    TimeSpan? RetryDelay)
{
    public static BackgroundJobExecutionResult Success(string correlationId) =>
        new(BackgroundJobExecutionOutcome.Succeeded, correlationId, null, null, null);

    public static BackgroundJobExecutionResult RetryScheduled(string correlationId, string errorMessage, string exceptionType, TimeSpan retryDelay) =>
        new(BackgroundJobExecutionOutcome.RetryScheduled, correlationId, errorMessage, exceptionType, retryDelay);

    public static BackgroundJobExecutionResult PermanentFailure(string correlationId, string errorMessage, string exceptionType) =>
        new(BackgroundJobExecutionOutcome.PermanentFailure, correlationId, errorMessage, exceptionType, null);

    public static BackgroundJobExecutionResult RetryExhausted(string correlationId, string errorMessage, string exceptionType) =>
        new(BackgroundJobExecutionOutcome.RetryExhausted, correlationId, errorMessage, exceptionType, null);
}

public interface IBackgroundJobFailureClassifier
{
    BackgroundJobFailureClassification Classify(Exception exception);
}

public interface IBackgroundJobExecutor
{
    Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        Func<CancellationToken, Task> handler,
        TimeSpan retryDelay,
        CancellationToken cancellationToken);
}

public sealed class DefaultBackgroundJobFailureClassifier : IBackgroundJobFailureClassifier
{
    public BackgroundJobFailureClassification Classify(Exception exception)
    {
        foreach (var current in Enumerate(exception))
        {
            if (current is PermanentBackgroundJobException or ArgumentException or JsonException or NotSupportedException)
            {
                return BackgroundJobFailureClassification.Permanent;
            }

            if (current is TimeoutException or HttpRequestException or IOException or SocketException or DbUpdateException)
            {
                return BackgroundJobFailureClassification.Transient;
            }
        }

        return BackgroundJobFailureClassification.Transient;
    }

    private static IEnumerable<Exception> Enumerate(Exception exception)
    {
        if (exception is AggregateException aggregateException)
        {
            foreach (var innerException in aggregateException.Flatten().InnerExceptions)
            {
                foreach (var nestedException in Enumerate(innerException))
                {
                    yield return nestedException;
                }
            }

            yield break;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}

public sealed class BackgroundJobExecutor : IBackgroundJobExecutor
{
    private readonly ILogger<BackgroundJobExecutor> _logger;
    private readonly IBackgroundJobFailureClassifier _failureClassifier;

    public BackgroundJobExecutor(
        ILogger<BackgroundJobExecutor> logger,
        IBackgroundJobFailureClassifier failureClassifier)
    {
        _logger = logger;
        _failureClassifier = failureClassifier;
    }

    public async Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        Func<CancellationToken, Task> handler,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        var effectiveCorrelationId = string.IsNullOrWhiteSpace(context.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : context.CorrelationId;
        var normalizedRetryDelay = retryDelay < TimeSpan.Zero ? TimeSpan.Zero : retryDelay;

        using var scope = _logger.BeginScope(ExecutionLogScope.ForBackgroundJob(
            context.JobName,
            context.Attempt,
            context.MaxAttempts,
            effectiveCorrelationId,
            context.CompanyId));

        try
        {
            await handler(cancellationToken);

            if (context.Attempt > 1)
            {
                _logger.LogInformation(
                    "Background job {JobName} succeeded on attempt {Attempt} of {MaxAttempts}.",
                    context.JobName,
                    context.Attempt,
                    context.MaxAttempts);
            }

            return BackgroundJobExecutionResult.Success(effectiveCorrelationId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var errorMessage = TrimError(ex.Message);
            var exceptionType = ex.GetType().FullName ?? ex.GetType().Name;
            var classification = _failureClassifier.Classify(ex);

            if (classification == BackgroundJobFailureClassification.Permanent)
            {
                _logger.LogError(
                    ex,
                    "Background job {JobName} failed permanently on attempt {Attempt} of {MaxAttempts}. ExceptionType: {ExceptionType}.",
                    context.JobName,
                    context.Attempt,
                    context.MaxAttempts,
                    exceptionType);
                return BackgroundJobExecutionResult.PermanentFailure(effectiveCorrelationId, errorMessage, exceptionType);
            }

            if (context.Attempt >= context.MaxAttempts)
            {
                _logger.LogError(
                    ex,
                    "Background job {JobName} exhausted retries on attempt {Attempt} of {MaxAttempts}. ExceptionType: {ExceptionType}.",
                    context.JobName,
                    context.Attempt,
                    context.MaxAttempts,
                    exceptionType);
                return BackgroundJobExecutionResult.RetryExhausted(effectiveCorrelationId, errorMessage, exceptionType);
            }

            _logger.LogWarning(
                ex,
                "Background job {JobName} failed with a retryable error on attempt {Attempt} of {MaxAttempts}. Retrying in {RetryDelaySeconds} seconds. ExceptionType: {ExceptionType}.",
                context.JobName,
                context.Attempt,
                context.MaxAttempts,
                normalizedRetryDelay.TotalSeconds,
                exceptionType);
            return BackgroundJobExecutionResult.RetryScheduled(effectiveCorrelationId, errorMessage, exceptionType, normalizedRetryDelay);
        }
    }

    private static string TrimError(string? errorMessage) =>
        string.IsNullOrWhiteSpace(errorMessage) ? "Unhandled background job failure." : errorMessage.Trim()[..Math.Min(errorMessage.Trim().Length, 2000)];
}