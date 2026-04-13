using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class WorkflowProgressionOptions
{
    public const string SectionName = "WorkflowProgression";

    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 10;
    public int LockTtlSeconds { get; set; } = 120;
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 5;
    public string LockKey { get; set; } = "workflow-progression";
}

public sealed class WorkflowProgressionCoordinator : IWorkflowProgressionCoordinator
{
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IWorkflowProgressionService _progressionService;
    private readonly IOptions<WorkflowProgressionOptions> _options;
    private readonly ILogger<WorkflowProgressionCoordinator> _logger;

    public WorkflowProgressionCoordinator(
        IDistributedLockProvider lockProvider,
        IWorkflowProgressionService progressionService,
        IOptions<WorkflowProgressionOptions> options,
        ILogger<WorkflowProgressionCoordinator> logger)
    {
        _lockProvider = lockProvider;
        _progressionService = progressionService;
        _options = options;
        _logger = logger;
    }

    public async Task<WorkflowProgressionRunResult> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var lockKey = string.IsNullOrWhiteSpace(options.LockKey) ? "workflow-progression" : options.LockKey.Trim();
        var lockTtl = TimeSpan.FromSeconds(Math.Max(5, options.LockTtlSeconds));
        var batchSize = Math.Max(1, options.BatchSize);

        await using var handle = await _lockProvider.TryAcquireAsync(lockKey, lockTtl, cancellationToken);
        if (handle is null)
        {
            _logger.LogInformation(
                "Workflow progression skipped because distributed lock {LockKey} was not acquired.",
                lockKey);
            return new WorkflowProgressionRunResult(false, 0, 0, 0);
        }

        var result = await _progressionService.RunRunnableInstancesAsync(now.UtcDateTime, batchSize, cancellationToken);
        _logger.LogInformation(
            "Workflow progression completed. Instances scanned: {InstancesScanned}. Instances advanced: {InstancesAdvanced}. Failures: {Failures}.",
            result.InstancesScanned,
            result.InstancesAdvanced,
            result.Failures);

        return result;
    }
}

public sealed class WorkflowProgressionService : IWorkflowProgressionService
{
    private static readonly WorkflowInstanceStatus[] RunnableStates =
    [
        WorkflowInstanceStatus.Started,
        WorkflowInstanceStatus.Running
    ];

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IBackgroundJobExecutor _backgroundJobExecutor;
    private readonly IBackgroundExecutionRecorder _backgroundExecutionRecorder;
    private readonly IBackgroundExecutionRetryPolicy _retryPolicy;
    private readonly IOptions<WorkflowProgressionOptions> _options;
    private readonly ILogger<WorkflowProgressionService> _logger;
    private readonly IBackgroundExecutionIdentityFactory _identityFactory;
    private readonly ICompanyExecutionScopeFactory _companyExecutionScopeFactory;

    public WorkflowProgressionService(
        VirtualCompanyDbContext dbContext,
        IBackgroundJobExecutor backgroundJobExecutor,
        IBackgroundExecutionRecorder backgroundExecutionRecorder,
        IBackgroundExecutionRetryPolicy retryPolicy,
        IOptions<WorkflowProgressionOptions> options,
        IBackgroundExecutionIdentityFactory identityFactory,
        ICompanyExecutionScopeFactory companyExecutionScopeFactory,
        ILogger<WorkflowProgressionService> logger)
    {
        _dbContext = dbContext;
        _backgroundJobExecutor = backgroundJobExecutor;
        _backgroundExecutionRecorder = backgroundExecutionRecorder;
        _retryPolicy = retryPolicy;
        _options = options;
        _identityFactory = identityFactory;
        _logger = logger;
        _companyExecutionScopeFactory = companyExecutionScopeFactory;
    }

    public async Task<WorkflowProgressionRunResult> RunRunnableInstancesAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var effectiveNow = utcNow.Kind == DateTimeKind.Utc ? utcNow : utcNow.ToUniversalTime();
        var effectiveBatchSize = Math.Max(1, batchSize);

        var candidates = await _dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .Include(x => x.Definition)
            .Where(x => RunnableStates.Contains(x.State))
            .OrderBy(x => x.UpdatedUtc)
            .Take(effectiveBatchSize)
            .ToListAsync(cancellationToken);

        var advanced = 0;
        var failures = 0;

        foreach (var instance in candidates)
        {
            var processed = await ProcessInstanceAsync(instance, effectiveNow, cancellationToken);
            if (processed == WorkflowProgressionInstanceOutcome.Advanced)
            {
                advanced++;
            }
            else if (processed == WorkflowProgressionInstanceOutcome.Failed)
            {
                failures++;
            }
        }

        return new WorkflowProgressionRunResult(true, candidates.Count, advanced, failures);
    }

    private async Task<WorkflowProgressionInstanceOutcome> ProcessInstanceAsync(
        WorkflowInstance instance,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (instance.CompanyId == Guid.Empty)
        {
            throw new PermanentBackgroundJobException($"Workflow instance '{instance.Id}' is missing tenant context.");
        }

        using var tenantScope = _companyExecutionScopeFactory.BeginScope(instance.CompanyId);
        var correlationId = ResolveCorrelationId(instance);
        var idempotencyKey = ResolveIdempotencyKey(instance) ??
            _identityFactory.CreateIdempotencyKey(
                "workflow-progression",
                instance.CompanyId,
                instance.Id,
                instance.State.ToStorageValue(),
                instance.CurrentStep ?? "instance");
        var existingExecution = await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.CompanyId == instance.CompanyId &&
                x.ExecutionType == BackgroundExecutionType.WorkflowProgression &&
                x.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existingExecution is { Status: BackgroundExecutionStatus.Succeeded or BackgroundExecutionStatus.Failed or BackgroundExecutionStatus.Escalated })
        {
            _logger.LogDebug(
                "Skipped workflow progression for instance {WorkflowInstanceId} because execution {ExecutionId} is terminal.",
                instance.Id,
                existingExecution.Id);
            return WorkflowProgressionInstanceOutcome.Skipped;
        }

        if (existingExecution is { Status: BackgroundExecutionStatus.InProgress })
        {
            _logger.LogDebug(
                "Skipped workflow progression for instance {WorkflowInstanceId} because execution {ExecutionId} is already in progress.",
                instance.Id,
                existingExecution.Id);
            return WorkflowProgressionInstanceOutcome.Skipped;
        }

        if (existingExecution is { Status: BackgroundExecutionStatus.RetryScheduled, NextRetryUtc: { } scheduledRetryUtc } &&
            scheduledRetryUtc > utcNow)
        {
            _logger.LogDebug(
                "Skipped workflow progression for instance {WorkflowInstanceId} until retry time {NextRetryUtc}.",
                instance.Id,
                scheduledRetryUtc);
            return WorkflowProgressionInstanceOutcome.Skipped;
        }

        var attempt = Math.Max(1, (existingExecution?.AttemptCount ?? 0) + 1);
        var maxAttempts = Math.Max(1, _options.Value.MaxAttempts);
        var executionRecord = await _backgroundExecutionRecorder.StartAsync(
            instance.CompanyId,
            BackgroundExecutionType.WorkflowProgression,
            BackgroundExecutionRelatedEntityTypes.WorkflowInstance,
            instance.Id.ToString("N"),
            correlationId,
            idempotencyKey,
            attempt,
            maxAttempts,
            cancellationToken);

        var retryDelay = _retryPolicy.GetRetryDelay(attempt);
        var execution = await _backgroundJobExecutor.ExecuteAsync(
            new BackgroundJobExecutionContext(
                "workflow-progression:advance-instance",
                attempt,
                maxAttempts,
                instance.CompanyId,
                correlationId,
                idempotencyKey,
                requireCompanyContext: true),
            innerCancellationToken => AdvanceInstanceAsync(instance.CompanyId, instance.Id, innerCancellationToken),
            retryDelay,
            cancellationToken);

        var nextRetryUtc = execution.Outcome == BackgroundJobExecutionOutcome.RetryScheduled
            ? utcNow.Add(execution.RetryDelay ?? TimeSpan.Zero)
            : (DateTime?)null;

        if (execution.Outcome is BackgroundJobExecutionOutcome.PermanentFailure or BackgroundJobExecutionOutcome.RetryExhausted)
        {
            await MarkWorkflowTerminalFailureAsync(instance.CompanyId, instance.Id, execution, cancellationToken);
        }

        await _backgroundExecutionRecorder.ApplyOutcomeAsync(executionRecord, execution, nextRetryUtc, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return execution.Outcome switch
        {
            BackgroundJobExecutionOutcome.Succeeded or BackgroundJobExecutionOutcome.IdempotentDuplicate => WorkflowProgressionInstanceOutcome.Advanced,
            BackgroundJobExecutionOutcome.RetryScheduled => WorkflowProgressionInstanceOutcome.Skipped,
            BackgroundJobExecutionOutcome.Blocked or BackgroundJobExecutionOutcome.PermanentFailure or BackgroundJobExecutionOutcome.RetryExhausted => WorkflowProgressionInstanceOutcome.Failed,
            _ => WorkflowProgressionInstanceOutcome.Skipped
        };
    }

    private async Task AdvanceInstanceAsync(Guid companyId, Guid instanceId, CancellationToken cancellationToken)
    {
        var instance = await _dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .Include(x => x.Definition)
            .SingleAsync(x => x.CompanyId == companyId && x.Id == instanceId, cancellationToken);

        if (instance.State is not (WorkflowInstanceStatus.Started or WorkflowInstanceStatus.Running))
        {
            _logger.LogInformation(
                "Skipped workflow progression for instance {WorkflowInstanceId} because state {State} is not runnable.",
                instance.Id,
                instance.State.ToStorageValue());
            return;
        }

        if (!instance.Definition.DefinitionJson.TryGetValue("steps", out var stepsNode) ||
            stepsNode is not JsonArray steps ||
            steps.Count == 0)
        {
            throw new WorkflowValidationException(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["definitionJson"] = ["Workflow definition must contain at least one progression step."]
            });
        }

        var currentStep = string.IsNullOrWhiteSpace(instance.CurrentStep)
            ? ResolveStepId(steps[0] as JsonObject)
            : instance.CurrentStep;
        var currentIndex = ResolveStepIndex(steps, currentStep);
        if (currentIndex < 0)
        {
            throw new WorkflowValidationException(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["currentStep"] = [$"Workflow current step '{currentStep}' does not exist in the definition."]
            });
        }

        var currentStepObject = steps[currentIndex] as JsonObject
            ?? throw new WorkflowValidationException(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["definitionJson"] = ["Workflow step must be a JSON object."]
            });

        var handler = TryGetString(currentStepObject, "handler")
            ?? throw new WorkflowValidationException(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["definitionJson"] = [$"Workflow step '{currentStep}' must declare a handler."]
            });

        if (TryGetBoolean(currentStepObject, "blocked") ||
            TryGetBoolean(currentStepObject, "requiresApproval") && !HasApprovedContext(instance, currentStep))
        {
            var output = CloneNodes(instance.OutputPayload);
            output["blockedStep"] = JsonValue.Create(currentStep);
            output["blockedHandler"] = JsonValue.Create(handler);
            output["reason"] = JsonValue.Create(TryGetString(currentStepObject, "blockedReason") ?? "Workflow step is blocked and requires review.");
            instance.UpdateState(WorkflowInstanceStatus.Blocked, currentStep, output);
            await EnsureOpenWorkflowExceptionAsync(instance, WorkflowExceptionType.Blocked, output, cancellationToken);
            _logger.LogInformation(
                "Workflow instance {WorkflowInstanceId} is blocked at step {StepKey} for company {CompanyId}.",
                instance.Id,
                currentStep,
                instance.CompanyId);
            throw new WorkflowBlockedException(TryGetString(output, "reason") ?? "Workflow step is blocked and requires review.");
        }

        var nextStep = currentIndex + 1 < steps.Count
            ? ResolveStepId(steps[currentIndex + 1] as JsonObject)
            : null;
        var outputPayload = CloneNodes(instance.OutputPayload);
        outputPayload["lastProcessedStep"] = JsonValue.Create(currentStep);
        outputPayload["lastProcessedHandler"] = JsonValue.Create(handler);
        outputPayload["lastProcessedUtc"] = JsonValue.Create(DateTime.UtcNow);

        if (string.IsNullOrWhiteSpace(nextStep))
        {
            instance.UpdateState(WorkflowInstanceStatus.Completed, currentStep, outputPayload);
            _logger.LogInformation(
                "Workflow instance {WorkflowInstanceId} completed for company {CompanyId}.",
                instance.Id,
                instance.CompanyId);
            return;
        }

        instance.UpdateState(WorkflowInstanceStatus.Running, nextStep, outputPayload);
        _logger.LogInformation(
            "Workflow instance {WorkflowInstanceId} advanced from step {CurrentStep} to {NextStep} for company {CompanyId}.",
            instance.Id,
            currentStep,
            nextStep,
            instance.CompanyId);
    }

    private async Task MarkWorkflowTerminalFailureAsync(
        Guid companyId,
        Guid instanceId,
        BackgroundJobExecutionResult execution,
        CancellationToken cancellationToken)
    {
        var instance = await _dbContext.WorkflowInstances
            .IgnoreQueryFilters()
            .Include(x => x.Definition)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == instanceId, cancellationToken);
        if (instance is null || instance.State is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled or WorkflowInstanceStatus.Failed)
        {
            return;
        }

        var outputPayload = CloneNodes(instance.OutputPayload);
        outputPayload["failureCategory"] = JsonValue.Create(execution.FailureClassification?.ToString());
        outputPayload["errorCode"] = JsonValue.Create(execution.ExceptionType ?? execution.Outcome.ToString());
        outputPayload["message"] = JsonValue.Create(execution.ErrorMessage ?? "Workflow progression failed.");
        outputPayload["correlationId"] = JsonValue.Create(execution.CorrelationId);

        instance.UpdateState(WorkflowInstanceStatus.Failed, instance.CurrentStep, outputPayload);
        await EnsureOpenWorkflowExceptionAsync(instance, WorkflowExceptionType.Failed, outputPayload, cancellationToken);
    }

    private async Task EnsureOpenWorkflowExceptionAsync(
        WorkflowInstance instance,
        WorkflowExceptionType exceptionType,
        Dictionary<string, JsonNode?> technicalDetailsJson,
        CancellationToken cancellationToken)
    {
        var stepKey = string.IsNullOrWhiteSpace(instance.CurrentStep) ? "instance" : instance.CurrentStep;
        var exists = await _dbContext.WorkflowExceptions
            .IgnoreQueryFilters()
            .AnyAsync(x =>
                x.CompanyId == instance.CompanyId &&
                x.WorkflowInstanceId == instance.Id &&
                x.StepKey == stepKey &&
                x.ExceptionType == exceptionType &&
                x.Status == WorkflowExceptionStatus.Open,
                cancellationToken);

        if (exists)
        {
            return;
        }

        _dbContext.WorkflowExceptions.Add(new WorkflowException(
            Guid.NewGuid(),
            instance.CompanyId,
            instance.Id,
            instance.DefinitionId,
            stepKey,
            exceptionType,
            exceptionType == WorkflowExceptionType.Failed ? "Workflow progression failed" : "Workflow progression blocked",
            TryGetString(technicalDetailsJson, "message") ??
                TryGetString(technicalDetailsJson, "reason") ??
                "Workflow progression requires review.",
            TryGetString(technicalDetailsJson, "errorCode") ?? TryGetString(technicalDetailsJson, "failureCategory"),
            technicalDetailsJson));
    }

    private static string ResolveCorrelationId(WorkflowInstance instance) =>
        TryGetString(instance.ContextJson, "correlationId") ??
        TryGetString(instance.InputPayload, "correlationId") ??
        instance.TriggerRef ??
        $"workflow-progression:{instance.CompanyId:N}:{instance.Id:N}";

    private static string? ResolveIdempotencyKey(WorkflowInstance instance) =>
        TryGetString(instance.ContextJson, "idempotencyKey") ??
        TryGetString(instance.InputPayload, "idempotencyKey");

    private static int ResolveStepIndex(JsonArray steps, string? currentStep)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            if (string.Equals(ResolveStepId(steps[index] as JsonObject), currentStep, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string? ResolveStepId(JsonObject? step) =>
        TryGetString(step, "id") ?? TryGetString(step, "code") ?? TryGetString(step, "name");

    private static bool HasApprovedContext(WorkflowInstance instance, string? currentStep)
    {
        var key = string.IsNullOrWhiteSpace(currentStep) ? "approvalStatus" : $"{currentStep}:approvalStatus";
        return string.Equals(TryGetString(instance.ContextJson, key), "approved", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(TryGetString(instance.InputPayload, key), "approved", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static bool TryGetBoolean(JsonObject jsonObject, string key) =>
        jsonObject.TryGetPropertyValue(key, out var node) &&
        node is JsonValue value &&
        value.TryGetValue<bool>(out var parsed) &&
        parsed;

    private static string? TryGetString(JsonObject? jsonObject, string key)
    {
        if (jsonObject is null ||
            !jsonObject.TryGetPropertyValue(key, out var node) ||
            node is not JsonValue value ||
            !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static string? TryGetString(IReadOnlyDictionary<string, JsonNode?>? payload, string key)
    {
        if (payload is null ||
            !payload.TryGetValue(key, out var node) ||
            node is not JsonValue value ||
            !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private enum WorkflowProgressionInstanceOutcome
    {
        Skipped,
        Advanced,
        Failed
    }
}

public sealed class WorkflowProgressionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<WorkflowProgressionOptions> _options;
    private readonly ILogger<WorkflowProgressionBackgroundService> _logger;

    public WorkflowProgressionBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<WorkflowProgressionOptions> options,
        ILogger<WorkflowProgressionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Workflow progression background service is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollIntervalSeconds));
        _logger.LogInformation(
            "Workflow progression background service started with poll interval {PollIntervalSeconds} seconds, lock TTL {LockTtlSeconds} seconds, batch size {BatchSize}, and max attempts {MaxAttempts}.",
            pollInterval.TotalSeconds,
            Math.Max(5, _options.Value.LockTtlSeconds),
            Math.Max(1, _options.Value.BatchSize),
            Math.Max(1, _options.Value.MaxAttempts));

        using var timer = new PeriodicTimer(pollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            using var scope = _logger.BeginScope(ExecutionLogScope.ForBackground(correlationId));
            try
            {
                using var serviceScope = _scopeFactory.CreateScope();
                var coordinator = serviceScope.ServiceProvider.GetRequiredService<IWorkflowProgressionCoordinator>();
                await coordinator.RunOnceAsync(_timeProvider.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow progression loop failed unexpectedly.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Workflow progression background service stopped.");
    }
}
