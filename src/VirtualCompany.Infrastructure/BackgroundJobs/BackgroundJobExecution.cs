using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.BackgroundJobs;

public enum BackgroundJobFailureClassification
{
    Unknown = -1,
    Transient = 0,
    Permanent = 1,
    LockContention = 2,
    ExternalDependencyTimeout = 3,
    ExternalDependencyUnavailable = 4,
    PermanentBusinessRule = 5,
    PermanentPolicy = 6,
    Validation = 7,
    Configuration = 8,
    RateLimited = 9,
    ApprovalRequired = 10
}

public static class BackgroundJobFailureClassificationExtensions
{
    public static bool IsRetryable(this BackgroundJobFailureClassification classification) =>
        classification is BackgroundJobFailureClassification.Transient
            or BackgroundJobFailureClassification.LockContention
            or BackgroundJobFailureClassification.Unknown
            or BackgroundJobFailureClassification.RateLimited
            or BackgroundJobFailureClassification.ExternalDependencyTimeout
            or BackgroundJobFailureClassification.ExternalDependencyUnavailable;

    public static BackgroundJobFailureDisposition GetDisposition(this BackgroundJobFailureClassification classification) =>
        classification.IsRetryable()
            ? BackgroundJobFailureDisposition.Retry
            : classification is BackgroundJobFailureClassification.PermanentPolicy
                or BackgroundJobFailureClassification.PermanentBusinessRule
                or BackgroundJobFailureClassification.ApprovalRequired
                ? BackgroundJobFailureDisposition.Block
                : BackgroundJobFailureDisposition.Fail;
}

public enum BackgroundJobFailureDisposition
{
    Retry = 0,
    Fail = 1,
    Block = 2
}

public enum BackgroundJobExecutionOutcome
{
    Succeeded = 0,
    RetryScheduled = 1,
    PermanentFailure = 2,
    RetryExhausted = 3,
    IdempotentDuplicate = 4,
    Blocked = 5
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
        string? correlationId = null,
        string? idempotencyKey = null,
        bool requireCompanyContext = false)
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
        CompanyId = companyId == Guid.Empty ? null : companyId;
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        RequireCompanyContext = requireCompanyContext;
    }

    public string JobName { get; }
    public int Attempt { get; }
    public int MaxAttempts { get; }
    public Guid? CompanyId { get; }
    public string? CorrelationId { get; }
    public string? IdempotencyKey { get; }
    public bool RequireCompanyContext { get; }
}

public sealed record BackgroundJobExecutionResult(
    BackgroundJobExecutionOutcome Outcome,
    string CorrelationId,
    string IdempotencyKey,
    string? ErrorMessage,
    string? ExceptionType,
    TimeSpan? RetryDelay,
    BackgroundJobFailureClassification? FailureClassification)
{
    public static BackgroundJobExecutionResult Success(string correlationId, string idempotencyKey) =>
        new(BackgroundJobExecutionOutcome.Succeeded, correlationId, idempotencyKey, null, null, null, null);

    public static BackgroundJobExecutionResult RetryScheduled(string correlationId, string idempotencyKey, string errorMessage, string exceptionType, TimeSpan retryDelay, BackgroundJobFailureClassification classification) =>
        new(BackgroundJobExecutionOutcome.RetryScheduled, correlationId, idempotencyKey, errorMessage, exceptionType, retryDelay, classification);

    public static BackgroundJobExecutionResult PermanentFailure(string correlationId, string idempotencyKey, string errorMessage, string exceptionType, BackgroundJobFailureClassification classification) =>
        new(BackgroundJobExecutionOutcome.PermanentFailure, correlationId, idempotencyKey, errorMessage, exceptionType, null, classification);

    public static BackgroundJobExecutionResult Blocked(string correlationId, string idempotencyKey, string errorMessage, string exceptionType, BackgroundJobFailureClassification classification) =>
        new(BackgroundJobExecutionOutcome.Blocked, correlationId, idempotencyKey, errorMessage, exceptionType, null, classification);

    public static BackgroundJobExecutionResult RetryExhausted(string correlationId, string idempotencyKey, string errorMessage, string exceptionType, BackgroundJobFailureClassification classification) =>
        new(BackgroundJobExecutionOutcome.RetryExhausted, correlationId, idempotencyKey, errorMessage, exceptionType, null, classification);
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
            if (current is WorkflowBlockedException)
            {
                return BackgroundJobFailureClassification.ApprovalRequired;
            }

            if (current is UnauthorizedAccessException)
            {
                return BackgroundJobFailureClassification.PermanentPolicy;
            }

            if (current is WorkflowValidationException or ArgumentException or JsonException)
            {
                return BackgroundJobFailureClassification.Validation;
            }

            if (current is PermanentBackgroundJobException or NotSupportedException or InvalidOperationException)
            {
                return BackgroundJobFailureClassification.PermanentBusinessRule;
            }

            if (current is OperationCanceledException)
            {
                return BackgroundJobFailureClassification.ExternalDependencyTimeout;
            }

            if (current is KeyNotFoundException)
            {
                return BackgroundJobFailureClassification.Configuration;
            }

            if (current is DbUpdateConcurrencyException)
            {
                return BackgroundJobFailureClassification.LockContention;
            }

            if (current is TimeoutException)
            {
                return BackgroundJobFailureClassification.ExternalDependencyTimeout;
            }

            if (current is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
            {
                return BackgroundJobFailureClassification.RateLimited;
            }

            if (current is HttpRequestException { StatusCode: HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError })
            {
                return BackgroundJobFailureClassification.ExternalDependencyUnavailable;
            }

            if (current is IOException or SocketException or RedisException or DbUpdateException)
            {
                return BackgroundJobFailureClassification.ExternalDependencyUnavailable;
            }
        }

        return BackgroundJobFailureClassification.Unknown;
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
    private readonly IBackgroundExecutionIdentityFactory _identityFactory;

    public BackgroundJobExecutor(
        ILogger<BackgroundJobExecutor> logger,
        IBackgroundJobFailureClassifier failureClassifier)
        : this(logger, failureClassifier, new DefaultBackgroundExecutionIdentityFactory())
    {
    }

    public BackgroundJobExecutor(
        ILogger<BackgroundJobExecutor> logger,
        IBackgroundJobFailureClassifier failureClassifier,
        IBackgroundExecutionIdentityFactory identityFactory)
    {
        _logger = logger;
        _failureClassifier = failureClassifier;
        _identityFactory = identityFactory;
    }

    public async Task<BackgroundJobExecutionResult> ExecuteAsync(
        BackgroundJobExecutionContext context,
        Func<CancellationToken, Task> handler,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        var effectiveCorrelationId = _identityFactory.EnsureCorrelationId(context.CorrelationId);
        var effectiveIdempotencyKey = string.IsNullOrWhiteSpace(context.IdempotencyKey)
            ? _identityFactory.CreateIdempotencyKey("background-job", context.CompanyId, context.JobName, effectiveCorrelationId)
            : context.IdempotencyKey.Trim();
        var normalizedRetryDelay = retryDelay < TimeSpan.Zero ? TimeSpan.Zero : retryDelay;

        using var scope = _logger.BeginScope(ExecutionLogScope.ForBackgroundJob(
            context.JobName,
            context.Attempt,
            context.MaxAttempts,
            effectiveCorrelationId,
            context.CompanyId,
            effectiveIdempotencyKey));

        try
        {
            if (context.RequireCompanyContext && !context.CompanyId.HasValue)
            {
                throw new PermanentBackgroundJobException("Tenant-scoped background execution requires a non-empty CompanyId.");
            }

            await handler(cancellationToken);

            if (context.Attempt > 1)
            {
                _logger.LogInformation(
                    "Background job {JobName} succeeded on attempt {Attempt} of {MaxAttempts}.",
                    context.JobName,
                    context.Attempt,
                    context.MaxAttempts);
            }

            return BackgroundJobExecutionResult.Success(effectiveCorrelationId, effectiveIdempotencyKey);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && ex.CancellationToken != cancellationToken)
        {
            return HandleFailure(context, effectiveCorrelationId, effectiveIdempotencyKey, normalizedRetryDelay, ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HandleFailure(context, effectiveCorrelationId, effectiveIdempotencyKey, normalizedRetryDelay, ex);
        }
    }

    private BackgroundJobExecutionResult HandleFailure(
        BackgroundJobExecutionContext context,
        string effectiveCorrelationId,
        string effectiveIdempotencyKey,
        TimeSpan normalizedRetryDelay,
        Exception ex)
    {
        var errorMessage = TrimError(ex.Message);
        var exceptionType = ex.GetType().FullName ?? ex.GetType().Name;
        var classification = _failureClassifier.Classify(ex);
        var disposition = classification.GetDisposition();

        if (disposition == BackgroundJobFailureDisposition.Block)
        {
            _logger.LogError(
                ex,
                "Background job {JobName} is blocked on attempt {Attempt} of {MaxAttempts}. FailureClassification: {FailureClassification}. RetryDecision: {RetryDecision}. ExceptionType: {ExceptionType}.",
                context.JobName,
                context.Attempt,
                context.MaxAttempts,
                classification,
                disposition,
                exceptionType);
            return BackgroundJobExecutionResult.Blocked(effectiveCorrelationId, effectiveIdempotencyKey, errorMessage, exceptionType, classification);
        }

        if (disposition == BackgroundJobFailureDisposition.Fail)
        {
            _logger.LogError(
                ex,
                "Background job {JobName} failed permanently on attempt {Attempt} of {MaxAttempts}. FailureClassification: {FailureClassification}. RetryDecision: {RetryDecision}. ExceptionType: {ExceptionType}.",
                context.JobName,
                context.Attempt,
                context.MaxAttempts,
                classification,
                disposition,
                exceptionType);
            return BackgroundJobExecutionResult.PermanentFailure(effectiveCorrelationId, effectiveIdempotencyKey, errorMessage, exceptionType, classification);
        }

        if (context.Attempt >= context.MaxAttempts)
        {
            _logger.LogError(
                ex,
                "Background job {JobName} exhausted retries on attempt {Attempt} of {MaxAttempts}. FailureClassification: {FailureClassification}. RetryDecision: {RetryDecision}. ExceptionType: {ExceptionType}.",
                context.JobName,
                context.Attempt,
                context.MaxAttempts,
                classification,
                BackgroundJobExecutionOutcome.RetryExhausted,
                exceptionType);
            return BackgroundJobExecutionResult.RetryExhausted(effectiveCorrelationId, effectiveIdempotencyKey, errorMessage, exceptionType, classification);
        }

        _logger.LogWarning(
            ex,
            "Background job {JobName} failed with a retryable error on attempt {Attempt} of {MaxAttempts}. FailureClassification: {FailureClassification}. RetryDecision: {RetryDecision}. Retrying in {RetryDelaySeconds} seconds. ExceptionType: {ExceptionType}.",
            context.JobName,
            context.Attempt,
            context.MaxAttempts,
            classification,
            BackgroundJobExecutionOutcome.RetryScheduled,
            normalizedRetryDelay.TotalSeconds,
            exceptionType);
        return BackgroundJobExecutionResult.RetryScheduled(effectiveCorrelationId, effectiveIdempotencyKey, errorMessage, exceptionType, normalizedRetryDelay, classification);
    }

    private static string TrimError(string? errorMessage) =>
        string.IsNullOrWhiteSpace(errorMessage) ? "Unhandled background job failure." : errorMessage.Trim()[..Math.Min(errorMessage.Trim().Length, 2000)];
}

public sealed class BackgroundExecutionOptions
{
    public const string SectionName = "BackgroundExecution";

    public int MaxAttempts { get; set; } = 5;
    public int BaseRetryDelaySeconds { get; set; } = 30;
    public int MaxRetryDelaySeconds { get; set; } = 900;
    public int StaleExecutionTimeoutSeconds { get; set; } = 900;
}

public interface IBackgroundExecutionRetryPolicy
{
    TimeSpan GetRetryDelay(int attempt);
}

public interface IBackgroundExecutionRecorder
{
    Task<BackgroundExecution> StartAsync(
        Guid companyId,
        BackgroundExecutionType executionType,
        string relatedEntityType,
        string relatedEntityId,
        string correlationId,
        string idempotencyKey,
        int attempt,
        int maxAttempts,
        CancellationToken cancellationToken);

    Task ApplyOutcomeAsync(
        BackgroundExecution execution,
        BackgroundJobExecutionResult result,
        DateTime? nextRetryUtc,
        CancellationToken cancellationToken);

    Task<int> RecoverStaleExecutionsAsync(DateTime utcNow, CancellationToken cancellationToken);
}

public sealed class ExponentialBackgroundExecutionRetryPolicy : IBackgroundExecutionRetryPolicy
{
    private readonly IOptions<BackgroundExecutionOptions> _options;

    public ExponentialBackgroundExecutionRetryPolicy(IOptions<BackgroundExecutionOptions> options) => _options = options;

    public TimeSpan GetRetryDelay(int attempt)
    {
        var baseDelaySeconds = Math.Max(0, _options.Value.BaseRetryDelaySeconds);
        if (baseDelaySeconds == 0)
        {
            return TimeSpan.Zero;
        }

        var maxDelaySeconds = Math.Max(baseDelaySeconds, _options.Value.MaxRetryDelaySeconds);
        var multiplier = Math.Pow(2d, Math.Max(0, attempt - 1));
        return TimeSpan.FromSeconds(Math.Min(baseDelaySeconds * multiplier, maxDelaySeconds));
    }
}

public sealed class BackgroundExecutionRecorder : IBackgroundExecutionRecorder
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IOptions<BackgroundExecutionOptions> _options;
    private readonly ILogger<BackgroundExecutionRecorder> _logger;

    public BackgroundExecutionRecorder(
        VirtualCompanyDbContext dbContext,
        IOptions<BackgroundExecutionOptions> options,
        ILogger<BackgroundExecutionRecorder> logger)
    {
        _dbContext = dbContext;
        _options = options;
        _logger = logger;
    }

    public async Task<BackgroundExecution> StartAsync(
        Guid companyId,
        BackgroundExecutionType executionType,
        string relatedEntityType,
        string relatedEntityId,
        string correlationId,
        string idempotencyKey,
        int attempt,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"{executionType.ToStorageValue()}:{relatedEntityType}:{relatedEntityId}:{correlationId}"
            : idempotencyKey.Trim();
        var execution = await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.ExecutionType == executionType &&
                x.IdempotencyKey == normalizedIdempotencyKey,
                cancellationToken);

        if (execution is null)
        {
            execution = new BackgroundExecution(
                Guid.NewGuid(),
                companyId,
                executionType,
                relatedEntityType,
                relatedEntityId,
                correlationId,
                normalizedIdempotencyKey,
                maxAttempts);
            _dbContext.BackgroundExecutions.Add(execution);
        }

        if (!execution.IsTerminal)
        {
            execution.StartAttempt(correlationId, attempt, maxAttempts);
        }

        return execution;
    }

    public async Task ApplyOutcomeAsync(
        BackgroundExecution execution,
        BackgroundJobExecutionResult result,
        DateTime? nextRetryUtc,
        CancellationToken cancellationToken)
    {
        switch (result.Outcome)
        {
            case BackgroundJobExecutionOutcome.Succeeded:
            case BackgroundJobExecutionOutcome.IdempotentDuplicate:
                execution.MarkSucceeded();
                break;
            case BackgroundJobExecutionOutcome.RetryScheduled:
                execution.ScheduleRetry(
                    nextRetryUtc ?? DateTime.UtcNow.Add(result.RetryDelay ?? TimeSpan.Zero),
                    MapFailureCategory(result.FailureClassification),
                    ResolveFailureCode(result),
                    ResolveFailureMessage(result));
                break;
            case BackgroundJobExecutionOutcome.Blocked:
                execution.MarkBlocked(
                    MapFailureCategory(result.FailureClassification),
                    ResolveFailureCode(result),
                    ResolveFailureMessage(result));
                break;
            case BackgroundJobExecutionOutcome.PermanentFailure:
            case BackgroundJobExecutionOutcome.RetryExhausted:
                execution.MarkFailed(
                    MapFailureCategory(result.FailureClassification),
                    ResolveFailureCode(result),
                    ResolveFailureMessage(result),
                    escalationId: null);
                break;
        }

        _logger.LogInformation(
            "Background execution {ExecutionId} for company {CompanyId} finished with {Outcome}. FailureCategory: {FailureCategory}. RetryDecision: {RetryDecision}.",
            execution.Id,
            execution.CompanyId,
            result.Outcome,
            execution.FailureCategory,
            result.FailureClassification?.GetDisposition());

        await Task.CompletedTask;
    }

    public async Task<int> RecoverStaleExecutionsAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(30, _options.Value.StaleExecutionTimeoutSeconds));
        var staleBeforeUtc = utcNow.Subtract(timeout);
        var staleExecutions = await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .Where(x => x.Status == BackgroundExecutionStatus.InProgress && x.HeartbeatUtc < staleBeforeUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var execution in staleExecutions)
        {
            execution.RecoverStale(utcNow, "Background execution was recovered after a stale in-progress heartbeat.");
        }

        return staleExecutions.Count;
    }

    private static BackgroundExecutionFailureCategory MapFailureCategory(BackgroundJobFailureClassification? classification) =>
        classification switch
        {
            BackgroundJobFailureClassification.Unknown => BackgroundExecutionFailureCategory.Unknown,
            BackgroundJobFailureClassification.LockContention => BackgroundExecutionFailureCategory.LockContention,
            BackgroundJobFailureClassification.ExternalDependencyTimeout => BackgroundExecutionFailureCategory.ExternalDependencyTimeout,
            BackgroundJobFailureClassification.ExternalDependencyUnavailable => BackgroundExecutionFailureCategory.ExternalDependencyUnavailable,
            BackgroundJobFailureClassification.RateLimited => BackgroundExecutionFailureCategory.RateLimited,
            BackgroundJobFailureClassification.PermanentBusinessRule or BackgroundJobFailureClassification.Permanent => BackgroundExecutionFailureCategory.PermanentBusinessRule,
            BackgroundJobFailureClassification.PermanentPolicy => BackgroundExecutionFailureCategory.PermanentPolicy,
            BackgroundJobFailureClassification.Validation => BackgroundExecutionFailureCategory.Validation,
            BackgroundJobFailureClassification.ApprovalRequired => BackgroundExecutionFailureCategory.ApprovalRequired,
            BackgroundJobFailureClassification.Configuration => BackgroundExecutionFailureCategory.Configuration,
            _ => BackgroundExecutionFailureCategory.TransientInfrastructure
        };

    private static string ResolveFailureCode(BackgroundJobExecutionResult result) =>
        string.IsNullOrWhiteSpace(result.ExceptionType) ? result.Outcome.ToString() : result.ExceptionType;

    private static string ResolveFailureMessage(BackgroundJobExecutionResult result) =>
        string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Background execution failed." : result.ErrorMessage;
}