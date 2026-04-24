using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyOutboxDispatcherOptions
{
    public const string SectionName = "CompanyOutboxDispatcher";

    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 20;
    public int PollIntervalSeconds { get; set; } = 5;
    public int MaxAttempts { get; set; } = 5;
    public int RetryDelaySeconds { get; set; } = 30;
    public int ClaimTimeoutSeconds { get; set; } = 60;
}

internal sealed class CompanyOutboxEnqueuer : ICompanyOutboxEnqueuer
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ILogger<CompanyOutboxEnqueuer> _logger;
    private readonly IBackgroundExecutionIdentityFactory _identityFactory;

    public CompanyOutboxEnqueuer(
        VirtualCompanyDbContext dbContext,
        IBackgroundExecutionIdentityFactory identityFactory,
        ILogger<CompanyOutboxEnqueuer> logger)
    {
        _dbContext = dbContext;
        _identityFactory = identityFactory;
        _logger = logger;
    }

    public void Enqueue(
        Guid companyId,
        string topic,
        object payload,
        string? correlationId = null,
        DateTime? availableAtUtc = null,
        string? idempotencyKey = null,
        string? messageType = null,
        string? causationId = null,
        IReadOnlyDictionary<string, string?>? headers = null)
    {
        var messageTypeValue = ResolveMessageType(payload, topic, messageType);
        var payloadJson = JsonSerializer.Serialize(payload, SerializerOptions);
        var effectiveCorrelationId = _identityFactory.EnsureCorrelationId(correlationId);
        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? null
            : idempotencyKey.Trim();

        if (normalizedIdempotencyKey is not null && AlreadyQueued(companyId, topic, normalizedIdempotencyKey))
        {
            _logger.LogInformation(
                "Skipped duplicate company outbox message {Topic} for company {CompanyId} with idempotency key {IdempotencyKey}.",
                topic,
                companyId,
                normalizedIdempotencyKey);
            return;
        }

        var outboxMessage = new CompanyOutboxMessage(
            Guid.NewGuid(),
            companyId,
            topic,
            payloadJson,
            correlationId: effectiveCorrelationId,
            messageType: messageTypeValue,
            availableUtc: availableAtUtc,
            idempotencyKey: normalizedIdempotencyKey,
            causationId: causationId,
            headersJson: SerializeHeaders(headers));

        _dbContext.CompanyOutboxMessages.Add(outboxMessage);

        using var scope = _logger.BeginScope(ExecutionLogScope.ForOutboxMessage(
            outboxMessage.Id,
            outboxMessage.CompanyId,
            outboxMessage.CorrelationId,
            outboxMessage.Topic,
            outboxMessage.MessageType,
            outboxMessage.IdempotencyKey));

        _logger.LogInformation(
            "Enqueued company outbox message {Topic} ({MessageType}) for company {CompanyId}.",
            outboxMessage.Topic,
            outboxMessage.MessageType ?? outboxMessage.Topic,
            outboxMessage.CompanyId);
    }

    private bool AlreadyQueued(Guid companyId, string topic, string idempotencyKey) =>
        _dbContext.CompanyOutboxMessages.Local.Any(x =>
            x.CompanyId == companyId &&
            x.Topic == topic &&
            x.IdempotencyKey == idempotencyKey) ||
        _dbContext.CompanyOutboxMessages
            .AsNoTracking()
            .Any(x =>
                x.CompanyId == companyId &&
                x.Topic == topic &&
                x.IdempotencyKey == idempotencyKey);

    private static string ResolveMessageType(object payload, string topic, string? explicitMessageType)
    {
        if (!string.IsNullOrWhiteSpace(explicitMessageType))
        {
            return explicitMessageType.Trim();
        }

        var payloadType = payload.GetType();
        var typeName = payloadType.FullName;
        return string.IsNullOrWhiteSpace(typeName) || typeName.StartsWith("<>", StringComparison.Ordinal)
            ? topic
            : typeName;
    }

    private static string? SerializeHeaders(IReadOnlyDictionary<string, string?>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(headers, SerializerOptions);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}

internal sealed class CompanyOutboxPermanentException : PermanentBackgroundJobException
{
    public CompanyOutboxPermanentException(string message)
        : base(message)
    {
    }
}

public interface ICompanyOutboxProcessor
{
    Task<int> DispatchPendingAsync(CancellationToken cancellationToken);
}

public sealed class CompanyOutboxProcessor : ICompanyOutboxProcessor
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();
    private static readonly Meter Meter = new("VirtualCompany.ExecutiveCockpit.Outbox");
    private static readonly Counter<long> CockpitInvalidationEvents = Meter.CreateCounter<long>("executive_cockpit_invalidation_events");
    private static readonly Histogram<double> CockpitInvalidationDuration = Meter.CreateHistogram<double>("executive_cockpit_invalidation_duration_ms", "ms");
    private static readonly Histogram<double> CockpitInvalidationLag = Meter.CreateHistogram<double>("executive_cockpit_invalidation_lag_ms", "ms");

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyInvitationDeliveryDispatcher _invitationDeliveryDispatcher;
    private readonly ICompanyNotificationDispatcher _notificationDispatcher;
    private readonly IInternalWorkflowEventTriggerService _workflowEventTriggerService;
    private readonly IFinanceWorkflowTriggerService _financeWorkflowTriggerService;
    private readonly ITriggerExecutionService _triggerExecutionService;
    private readonly IExecutiveCockpitDashboardCacheInvalidator _cockpitCacheInvalidator;
    private readonly IOptions<CompanyOutboxDispatcherOptions> _options;
    private readonly IBackgroundJobExecutor _backgroundJobExecutor;
    private readonly ILogger<CompanyOutboxProcessor> _logger;
    private readonly IBackgroundExecutionRecorder _backgroundExecutionRecorder;
    private readonly IBackgroundExecutionRetryPolicy _retryPolicy;
    private readonly IBackgroundExecutionIdentityFactory _identityFactory;
    private readonly ICompanyExecutionScopeFactory _companyExecutionScopeFactory;
    private readonly IBriefingUpdateJobProducer _briefingUpdateJobProducer;

    public CompanyOutboxProcessor(
        VirtualCompanyDbContext dbContext,
        ICompanyInvitationDeliveryDispatcher invitationDeliveryDispatcher,
        ICompanyNotificationDispatcher notificationDispatcher,
        ITriggerExecutionService triggerExecutionService,
        IFinanceWorkflowTriggerService financeWorkflowTriggerService,
        IInternalWorkflowEventTriggerService workflowEventTriggerService,
        IExecutiveCockpitDashboardCacheInvalidator cockpitCacheInvalidator,
        IOptions<CompanyOutboxDispatcherOptions> options,
        IBackgroundJobExecutor backgroundJobExecutor,
        IBackgroundExecutionRecorder backgroundExecutionRecorder,
        IBackgroundExecutionRetryPolicy retryPolicy,
        IBackgroundExecutionIdentityFactory identityFactory,
        ICompanyExecutionScopeFactory companyExecutionScopeFactory,
        IBriefingUpdateJobProducer briefingUpdateJobProducer,
        ILogger<CompanyOutboxProcessor> logger)
    {
        _dbContext = dbContext;
        _invitationDeliveryDispatcher = invitationDeliveryDispatcher;
        _notificationDispatcher = notificationDispatcher;
        _triggerExecutionService = triggerExecutionService;
        _financeWorkflowTriggerService = financeWorkflowTriggerService;
        _workflowEventTriggerService = workflowEventTriggerService;
        _cockpitCacheInvalidator = cockpitCacheInvalidator;
        _options = options;
        _backgroundJobExecutor = backgroundJobExecutor;
        _backgroundExecutionRecorder = backgroundExecutionRecorder;
        _retryPolicy = retryPolicy;
        _identityFactory = identityFactory;
        _companyExecutionScopeFactory = companyExecutionScopeFactory;
        _briefingUpdateJobProducer = briefingUpdateJobProducer;
        _logger = logger;
    }

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var claimedMessages = await ClaimBatchAsync(cancellationToken);
        var handledCount = 0;
        var processedCount = 0;
        var retryCount = 0;
        var permanentFailureCount = 0;

        if (claimedMessages.Count > 0)
        {
            _logger.LogInformation(
                "Company outbox dispatcher claimed {ClaimedCount} message(s) for processing.",
                claimedMessages.Count);
        }

        foreach (var message in claimedMessages)
        {
            using var tenantScope = _companyExecutionScopeFactory.BeginScope(message.CompanyId);
            using var scope = _logger.BeginScope(ExecutionLogScope.ForOutboxMessage(
                message.Id,
                message.CompanyId,
                message.CorrelationId ?? message.ClaimToken,
                message.Topic,
                message.MessageType,
                message.IdempotencyKey));

            var attempt = message.AttemptCount + 1;
            _logger.LogInformation("Dispatching company outbox message {Topic} ({MessageType}) on attempt {Attempt}.", message.Topic, message.MessageType ?? message.Topic, attempt);
            var dispatchIdentity = _identityFactory.FromExisting(
                message.CompanyId,
                message.CorrelationId ?? message.ClaimToken ?? _identityFactory.CreateCorrelationId(),
                message.IdempotencyKey ?? _identityFactory.CreateIdempotencyKey("company-outbox-dispatch", message.CompanyId, message.Id));

            var executionRecord = await _backgroundExecutionRecorder.StartAsync(
                message.CompanyId,
                BackgroundExecutionType.OutboxDispatch,
                BackgroundExecutionRelatedEntityTypes.OutboxMessage,
                message.Id.ToString("N"),
                dispatchIdentity.CorrelationId,
                dispatchIdentity.IdempotencyKey,
                attempt,
                MaxAttempts,
                cancellationToken);
            var retryDelay = _retryPolicy.GetRetryDelay(attempt);

            var execution = await _backgroundJobExecutor.ExecuteAsync(
                new BackgroundJobExecutionContext(
                    $"company-outbox:{message.Topic}",
                    attempt,
                    MaxAttempts,
                    message.CompanyId,
                    dispatchIdentity.CorrelationId,
                    dispatchIdentity.IdempotencyKey,
                    requireCompanyContext: true),
                innerCancellationToken => DispatchAsync(message, innerCancellationToken),
                retryDelay,
                cancellationToken);

            switch (execution.Outcome)
            {
                case BackgroundJobExecutionOutcome.Succeeded:
                case BackgroundJobExecutionOutcome.IdempotentDuplicate:
                    message.MarkProcessed();
                    _logger.LogInformation(
                        "Dispatched company outbox message {Topic} ({MessageType}) successfully on attempt {Attempt}.",
                        message.Topic,
                        message.MessageType ?? message.Topic,
                        attempt);
                    await _backgroundExecutionRecorder.ApplyOutcomeAsync(executionRecord, execution, null, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    handledCount++;
                    processedCount++;
                    break;
                case BackgroundJobExecutionOutcome.RetryScheduled:
                {
                    var nextAttemptAtUtc = DateTime.UtcNow.Add(execution.RetryDelay ?? TimeSpan.Zero);
                    message.ScheduleRetry(nextAttemptAtUtc, ResolveFailureMessage(execution));
                    _logger.LogWarning(
                        "Retry scheduled for company outbox message {Topic} ({MessageType}) after attempt {Attempt}. FailureClassification: {FailureClassification}. Next attempt at {AvailableUtc}.",
                        message.Topic,
                        message.MessageType ?? message.Topic,
                        attempt,
                        execution.FailureClassification,
                        nextAttemptAtUtc);
                    await _backgroundExecutionRecorder.ApplyOutcomeAsync(executionRecord, execution, nextAttemptAtUtc, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    retryCount++;
                    break;
                }
                case BackgroundJobExecutionOutcome.Blocked:
                case BackgroundJobExecutionOutcome.PermanentFailure:
                case BackgroundJobExecutionOutcome.RetryExhausted:
                {
                    var failureMessage = ResolveFailureMessage(execution);
                    message.MarkDiscarded(failureMessage);
                    _logger.LogError(
                        "Discarded company outbox message {Topic} ({MessageType}) after attempt {Attempt}. Outcome: {Outcome}. FailureClassification: {FailureClassification}. Error: {ErrorMessage}",
                        message.Topic,
                        message.MessageType ?? message.Topic,
                        attempt,
                        execution.Outcome,
                        execution.FailureClassification,
                        failureMessage);
                    await _backgroundExecutionRecorder.ApplyOutcomeAsync(executionRecord, execution, null, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    handledCount++;
                    permanentFailureCount++;
                    break;
                }
            }
        }

        if (claimedMessages.Count > 0)
        {
            _logger.LogInformation(
                "Company outbox dispatcher completed batch. Claimed: {ClaimedCount}, Processed: {ProcessedCount}, RetryScheduled: {RetryCount}, PermanentFailures: {PermanentFailureCount}.",
                claimedMessages.Count,
                processedCount,
                retryCount,
                permanentFailureCount);
        }

        return handledCount;
    }

    private int BatchSize => Math.Max(1, _options.Value.BatchSize);
    private int MaxAttempts => Math.Max(1, _options.Value.MaxAttempts);

    private static string ResolveFailureMessage(BackgroundJobExecutionResult execution) => string.IsNullOrWhiteSpace(execution.ErrorMessage) ? "Unhandled company outbox processing failure." : execution.ErrorMessage;
    private TimeSpan ClaimTimeout => TimeSpan.FromSeconds(Math.Max(5, _options.Value.ClaimTimeoutSeconds));

    private async Task<IReadOnlyList<CompanyOutboxMessage>> ClaimBatchAsync(CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var claimToken = Guid.NewGuid().ToString("N");
        var claimStaleBeforeUtc = utcNow.Subtract(ClaimTimeout);

        var candidateIds = await _dbContext.CompanyOutboxMessages
            .AsNoTracking()
            .Where(x => x.ProcessedUtc == null &&
                        x.AvailableUtc <= utcNow &&
                        ((x.Status != CompanyOutboxMessageStatus.InProgress &&
                          (x.ClaimedUtc == null || x.ClaimedUtc <= claimStaleBeforeUtc)) ||
                         (x.Status == CompanyOutboxMessageStatus.InProgress &&
                          x.ClaimedUtc != null && x.ClaimedUtc <= claimStaleBeforeUtc)))
            .OrderBy(x => x.AvailableUtc)
            .ThenBy(x => x.CreatedUtc)
            .Take(BatchSize)
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        if (candidateIds.Length == 0)
        {
            return [];
        }

        // The conditional update is the outbox lease. Competing dispatchers may see
        // the same candidate ids, but only one can move each row to InProgress.
        await _dbContext.CompanyOutboxMessages
            .Where(x => candidateIds.Contains(x.Id) &&
                        x.ProcessedUtc == null &&
                        x.AvailableUtc <= utcNow &&
                        ((x.Status != CompanyOutboxMessageStatus.InProgress &&
                          (x.ClaimedUtc == null || x.ClaimedUtc <= claimStaleBeforeUtc)) ||
                         (x.Status == CompanyOutboxMessageStatus.InProgress &&
                          x.ClaimedUtc != null && x.ClaimedUtc <= claimStaleBeforeUtc)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ClaimToken, claimToken)
                .SetProperty(x => x.ClaimedUtc, (DateTime?)utcNow)
                .SetProperty(x => x.Status, CompanyOutboxMessageStatus.InProgress)
                .SetProperty(x => x.LastError, (string?)null),
                cancellationToken);

        return await _dbContext.CompanyOutboxMessages
            .Where(x => x.ClaimToken == claimToken && x.Status == CompanyOutboxMessageStatus.InProgress)
            .OrderBy(x => x.AvailableUtc)
            .ThenBy(x => x.CreatedUtc)
            .ToArrayAsync(cancellationToken);
    }

    private static ProcessFinanceWorkflowTriggerCommand? BuildFinanceWorkflowTriggerCommand(
        CompanyOutboxMessage message,
        PlatformEventEnvelope payload)
    {
        var triggerType = message.Topic switch
        {
            CompanyOutboxTopics.FinanceInvoiceCreated => FinanceWorkflowTriggerTypes.Invoice,
            CompanyOutboxTopics.FinanceBillCreated => FinanceWorkflowTriggerTypes.Bill,
            CompanyOutboxTopics.FinancePaymentCreated => FinanceWorkflowTriggerTypes.Payment,
            CompanyOutboxTopics.FinanceTransactionCreated => FinanceWorkflowTriggerTypes.Cash,
            CompanyOutboxTopics.FinanceSimulationDayAdvanced => FinanceWorkflowTriggerTypes.SimulationDayAdvanced,
            _ => null
        };
        if (triggerType is null)
        {
            return null;
        }

        var sourceEntityVersion = TryReadMetadataString(payload.Metadata, "sourceEntityVersion") ?? payload.EventId;
        var sourceMetadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in payload.Metadata)
        {
            sourceMetadata[pair.Key] = pair.Value?.DeepClone();
        }

        sourceMetadata["eventId"] = JsonValue.Create(payload.EventId);
        sourceMetadata["sourceOutboxMessageId"] = JsonValue.Create(message.Id.ToString("N"));
        sourceMetadata["sourceOutboxTopic"] = JsonValue.Create(message.Topic);
        sourceMetadata["sourceOutboxMessageType"] = JsonValue.Create(message.MessageType);
        sourceMetadata["sourceOutboxIdempotencyKey"] = JsonValue.Create(message.IdempotencyKey);

        return new ProcessFinanceWorkflowTriggerCommand(
            payload.CompanyId,
            triggerType,
            payload.SourceEntityType,
            payload.SourceEntityId,
            sourceEntityVersion,
            payload.OccurredAtUtc,
            string.IsNullOrWhiteSpace(payload.CorrelationId) ? message.CorrelationId ?? payload.EventId : payload.CorrelationId,
            payload.EventId,
            message.CausationId ?? payload.EventId,
            message.Id.ToString("N"),
            sourceMetadata);
    }

    private async Task DispatchAsync(CompanyOutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.CompanyId == Guid.Empty)
        {
            throw new CompanyOutboxPermanentException("Company outbox message is missing tenant context.");
        }

        switch (message.Topic)
        {
            case CompanyOutboxTopics.InvitationDeliveryRequested:
            {
                var payload = Deserialize<CompanyInvitationDeliveryRequestedMessage>(message);
                if (payload.CompanyId != message.CompanyId)
                {
                    throw new CompanyOutboxPermanentException("Company invitation outbox payload tenant does not match the outbox message tenant.");
                }

                var correlationId = string.IsNullOrWhiteSpace(payload.CorrelationId)
                    ? message.CorrelationId
                    : payload.CorrelationId;

                await _invitationDeliveryDispatcher.DispatchAsync(
                    payload with { CorrelationId = correlationId },
                    cancellationToken);
                break;
            }
            case CompanyOutboxTopics.NotificationDeliveryRequested:
            {
                var payload = Deserialize<NotificationDeliveryRequestedMessage>(message);
                if (payload.CompanyId != message.CompanyId)
                {
                    throw new CompanyOutboxPermanentException("Notification outbox payload tenant does not match the outbox message tenant.");
                }

                await _notificationDispatcher.DispatchAsync(payload with { CorrelationId = payload.CorrelationId ?? message.CorrelationId }, cancellationToken);
                await EnqueueBriefingJobForNotificationAsync(message, payload, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                break;
            }
            case CompanyOutboxTopics.TaskCreated:
            case CompanyOutboxTopics.TaskUpdated:
            case CompanyOutboxTopics.DocumentUploaded:
            case CompanyOutboxTopics.WorkflowStateChanged:
            case CompanyOutboxTopics.FinanceTransactionCreated:
            case CompanyOutboxTopics.FinanceInvoiceCreated:
            case CompanyOutboxTopics.FinanceBillCreated:
            case CompanyOutboxTopics.FinancePaymentCreated:
            case CompanyOutboxTopics.FinanceSimulationDayAdvanced:
            case CompanyOutboxTopics.FinanceThresholdBreached:
            {
                var payload = Deserialize<PlatformEventEnvelope>(message);
                if (payload.CompanyId != message.CompanyId)
                {
                    throw new CompanyOutboxPermanentException("Platform event outbox payload tenant does not match the outbox message tenant.");
                }

                if (!string.Equals(payload.EventType, message.Topic, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CompanyOutboxPermanentException($"Platform event payload type '{payload.EventType}' does not match outbox topic '{message.Topic}'.");
                }

                var financeTriggerCommand = BuildFinanceWorkflowTriggerCommand(message, payload);
                if (financeTriggerCommand is not null)
                {
                    await _financeWorkflowTriggerService.ProcessAsync(
                        financeTriggerCommand with
                        {
                            CorrelationId = string.IsNullOrWhiteSpace(financeTriggerCommand.CorrelationId) ? message.CorrelationId ?? payload.EventId : financeTriggerCommand.CorrelationId
                        },
                        cancellationToken);
                }
                await _workflowEventTriggerService.HandleAsync(
                    payload with { CorrelationId = string.IsNullOrWhiteSpace(payload.CorrelationId) ? message.CorrelationId ?? payload.EventId : payload.CorrelationId },
                    cancellationToken);
                await InvalidateCockpitAsync(message, payload.SourceEntityType, TryParseGuid(payload.SourceEntityId), cancellationToken);
                await EnqueueBriefingJobForPlatformEventAsync(message, payload, cancellationToken);
                break;
            }
            case CompanyOutboxTopics.ApprovalUpdated:
            case CompanyOutboxTopics.AgentStatusUpdated:
            {
                var payload = Deserialize<PlatformEventEnvelope>(message);
                if (payload.CompanyId != message.CompanyId)
                {
                    throw new CompanyOutboxPermanentException("Cockpit invalidation event payload tenant does not match the outbox message tenant.");
                }

                if (!string.Equals(payload.EventType, message.Topic, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CompanyOutboxPermanentException($"Cockpit invalidation event payload type '{payload.EventType}' does not match outbox topic '{message.Topic}'.");
                }

                await InvalidateCockpitAsync(message, payload.SourceEntityType, TryParseGuid(payload.SourceEntityId), cancellationToken);
                await EnqueueBriefingJobForPlatformEventAsync(message, payload, cancellationToken);
                break;
            }
            case CompanyOutboxTopics.AgentScheduledTriggerExecutionRequested:
            {
                var payload = Deserialize<AgentScheduledTriggerExecutionRequestMessage>(message);
                if (payload.CompanyId != message.CompanyId)
                {
                    throw new CompanyOutboxPermanentException("Agent scheduled trigger outbox payload tenant does not match the outbox message tenant.");
                }

                await _triggerExecutionService.ProcessScheduledTriggerAsync(
                    payload with { CorrelationId = string.IsNullOrWhiteSpace(payload.CorrelationId) ? message.CorrelationId ?? payload.IdempotencyKey : payload.CorrelationId },
                    MaxAttempts,
                    cancellationToken);
                break;
            }
            case CompanyOutboxTopics.InvitationCreated:
            case CompanyOutboxTopics.InvitationResent:
            case CompanyOutboxTopics.InvitationRevoked:
            case CompanyOutboxTopics.InvitationAccepted:
            case CompanyOutboxTopics.MembershipRoleChanged:
                _logger.LogInformation(
                    "Acknowledged company outbox event '{Topic}' ({MessageType}) without external dispatch.",
                    message.Topic,
                    message.MessageType ?? message.Topic);
                break;
            default:
                throw new CompanyOutboxPermanentException($"Unsupported company outbox topic '{message.Topic}'.");
        }
    }

    private async Task EnqueueBriefingJobForPlatformEventAsync(
        CompanyOutboxMessage message,
        PlatformEventEnvelope payload,
        CancellationToken cancellationToken)
    {
        var briefingEventType = NormalizeBriefingEventType(payload);
        if (briefingEventType is null)
        {
            return;
        }

        var idempotencyKey = $"briefing-event:{payload.CompanyId:N}:{payload.EventId}";
        var sourceMetadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sourceOutboxMessageId"] = JsonValue.Create(message.Id.ToString("N")),
            ["sourceEventId"] = JsonValue.Create(payload.EventId),
            ["sourceEventType"] = JsonValue.Create(payload.EventType),
            ["sourceEntityType"] = JsonValue.Create(payload.SourceEntityType),
            ["sourceEntityId"] = JsonValue.Create(payload.SourceEntityId),
            ["occurredAtUtc"] = JsonValue.Create(payload.OccurredAtUtc),
            ["payloadRef"] = JsonValue.Create($"company_outbox_messages/{message.Id:N}"),
            ["metadata"] = new JsonObject(payload.Metadata.ToDictionary(x => x.Key, x => x.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase))
        };

        var result = await _briefingUpdateJobProducer.EnqueueEventDrivenAsync(
            payload.CompanyId,
            briefingEventType,
            string.IsNullOrWhiteSpace(payload.CorrelationId) ? message.CorrelationId ?? payload.EventId : payload.CorrelationId,
            idempotencyKey,
            sourceMetadata,
            cancellationToken);

        _logger.LogInformation(
            "Briefing update producer handled supported event {EventType} for company {CompanyId}. JobId={JobId} Created={Created} CorrelationId={CorrelationId}.",
            briefingEventType,
            payload.CompanyId,
            result.JobId,
            result.Created,
            payload.CorrelationId);
    }

    private async Task EnqueueBriefingJobForNotificationAsync(
        CompanyOutboxMessage message,
        NotificationDeliveryRequestedMessage payload,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(payload.NotificationType, CompanyNotificationType.Escalation.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var idempotencyKey = $"briefing-event:{payload.CompanyId:N}:escalation:{payload.DedupeKey}";
        await _briefingUpdateJobProducer.EnqueueEventDrivenAsync(
            payload.CompanyId,
            BriefingUpdateEventTypes.Escalation,
            payload.CorrelationId ?? message.CorrelationId ?? idempotencyKey,
            idempotencyKey,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["sourceOutboxMessageId"] = JsonValue.Create(message.Id.ToString("N")),
                ["sourceEntityType"] = JsonValue.Create(payload.RelatedEntityType),
                ["sourceEntityId"] = payload.RelatedEntityId.HasValue ? JsonValue.Create(payload.RelatedEntityId.Value.ToString("N")) : null,
                ["notificationType"] = JsonValue.Create(payload.NotificationType),
                ["payloadRef"] = JsonValue.Create($"company_outbox_messages/{message.Id:N}")
            },
            cancellationToken);
    }

    private static string? NormalizeBriefingEventType(PlatformEventEnvelope payload)
    {
        if (string.Equals(payload.EventType, CompanyOutboxTopics.TaskUpdated, StringComparison.OrdinalIgnoreCase))
        {
            return BriefingUpdateEventTypes.TaskStatusChanged;
        }

        if (string.Equals(payload.EventType, CompanyOutboxTopics.WorkflowStateChanged, StringComparison.OrdinalIgnoreCase))
        {
            return BriefingUpdateEventTypes.WorkflowStateChanged;
        }

        if (string.Equals(payload.EventType, CompanyOutboxTopics.ApprovalUpdated, StringComparison.OrdinalIgnoreCase))
        {
            return payload.Metadata.TryGetValue("reason", out var reason) &&
                   string.Equals(reason?.GetValue<string>(), "created", StringComparison.OrdinalIgnoreCase)
                ? BriefingUpdateEventTypes.ApprovalRequested
                : BriefingUpdateEventTypes.ApprovalDecision;
        }

        return null;
    }

    private static string? TryReadMetadataString(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        string key)
    {
        foreach (var pair in metadata)
        {
            if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) ||
                pair.Value is not JsonValue value ||
                !value.TryGetValue<string>(out var text) ||
                string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            return text.Trim();
        }

        return null;
    }

    private async Task InvalidateCockpitAsync(
        CompanyOutboxMessage message,
        string? entityType,
        Guid? entityId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await _cockpitCacheInvalidator.InvalidateAsync(
            new ExecutiveCockpitCacheInvalidationEvent(
                message.CompanyId,
                message.Topic,
                entityType,
                entityId,
                message.CreatedUtc),
            cancellationToken);
        CockpitInvalidationEvents.Add(
            1,
            new KeyValuePair<string, object?>("trigger", message.Topic));
        CockpitInvalidationDuration.Record(
            stopwatch.Elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>("trigger", message.Topic));
        CockpitInvalidationLag.Record(
            Math.Max(0, (DateTime.UtcNow - message.CreatedUtc).TotalMilliseconds),
            new KeyValuePair<string, object?>("trigger", message.Topic));
        _logger.LogInformation(
            "Processed executive cockpit cache invalidation for company {CompanyId} from trigger {Trigger} in {ElapsedMilliseconds} ms.",
            message.CompanyId,
            message.Topic,
            stopwatch.Elapsed.TotalMilliseconds);
    }

    private static Guid? TryParseGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    private static T Deserialize<T>(CompanyOutboxMessage message)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(message.PayloadJson, SerializerOptions)
                ?? throw new CompanyOutboxPermanentException($"Company outbox payload for topic '{message.Topic}' is empty.");
        }
        catch (JsonException ex)
        {
            throw new CompanyOutboxPermanentException($"Company outbox payload for topic '{message.Topic}' is invalid JSON: {ex.Message}");
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }
}

internal sealed class CompanyInvitationDeliveryDispatcher : ICompanyInvitationDeliveryDispatcher
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyInvitationSender _invitationSender;
    private readonly ILogger<CompanyInvitationDeliveryDispatcher> _logger;
    private readonly IBackgroundExecutionIdentityFactory _identityFactory;

    public CompanyInvitationDeliveryDispatcher(
        VirtualCompanyDbContext dbContext,
        ICompanyInvitationSender invitationSender,
        IBackgroundExecutionIdentityFactory identityFactory,
        ILogger<CompanyInvitationDeliveryDispatcher> logger)
    {
        _dbContext = dbContext;
        _identityFactory = identityFactory;
        _invitationSender = invitationSender;
        _logger = logger;
    }

    public async Task DispatchAsync(CompanyInvitationDeliveryRequestedMessage message, CancellationToken cancellationToken)
    {
        var invitation = await _dbContext.CompanyInvitations
            .Include(x => x.Company)
            .SingleOrDefaultAsync(x => x.CompanyId == message.CompanyId && x.Id == message.InvitationId, cancellationToken)
            ?? throw new CompanyOutboxPermanentException($"Company invitation '{message.InvitationId}' was not found.");

        var correlationId = _identityFactory.EnsureCorrelationId(message.CorrelationId);
        using var scope = _logger.BeginScope(ExecutionLogScope.ForBackground(
            correlationId,
            invitation.CompanyId));

        invitation.SyncExpiration(DateTime.UtcNow);
        if (invitation.Status != CompanyInvitationStatus.Pending)
        {
            if (invitation.HasDeliveredCurrentToken())
            {
                invitation.RecordIgnoredDeliveryAttempt(correlationId, $"Invitation delivery was ignored because invitation status is '{invitation.Status.ToStorageValue()}'.");
            }
            else
            {
                invitation.MarkDeliverySkipped($"Invitation delivery was skipped because invitation status is '{invitation.Status.ToStorageValue()}'.", correlationId);
            }

            _logger.LogInformation("Skipped invitation delivery for invitation {InvitationId} because status is {Status}.", invitation.Id, invitation.Status);
            return;
        }

        if (!string.Equals(invitation.TokenHash, CompanyInvitationTokenHasher.ComputeHash(message.AcceptanceToken), StringComparison.Ordinal))
        {
            invitation.RecordIgnoredDeliveryAttempt(correlationId, "Invitation delivery was ignored because a newer invitation token exists.");
            _logger.LogInformation("Ignored stale invitation delivery message for invitation {InvitationId}.", invitation.Id);
            return;
        }

        if (invitation.HasDeliveredCurrentToken())
        {
            invitation.RecordIgnoredDeliveryAttempt(correlationId, "Invitation delivery was ignored because the current token has already been delivered.");
            _logger.LogInformation("Ignored duplicate invitation delivery for invitation {InvitationId}.", invitation.Id);
            return;
        }

        try
        {
            invitation.MarkDeliveryAttempt(correlationId);

            await _invitationSender.SendAsync(
                message with
                {
                    CompanyName = string.IsNullOrWhiteSpace(message.CompanyName) ? invitation.Company.Name : message.CompanyName,
                    Email = invitation.Email,
                    Role = invitation.Role,
                    ExpiresAtUtc = invitation.ExpiresAtUtc,
                    CorrelationId = correlationId
                },
                cancellationToken);

            invitation.MarkDelivered(correlationId);
            _logger.LogInformation("Delivered invitation {InvitationId} for company {CompanyId}.", invitation.Id, invitation.CompanyId);
        }
        catch (Exception ex)
        {
            invitation.MarkDeliveryFailed(string.IsNullOrWhiteSpace(ex.Message) ? "Invitation delivery failed." : ex.Message, correlationId);
            throw;
        }
    }
}

internal sealed class CompanyNotificationDispatcher : ICompanyNotificationDispatcher
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ILogger<CompanyNotificationDispatcher> _logger;

    public CompanyNotificationDispatcher(
        VirtualCompanyDbContext dbContext,
        ILogger<CompanyNotificationDispatcher> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task DispatchAsync(NotificationDeliveryRequestedMessage message, CancellationToken cancellationToken)
    {
        if (message.CompanyId == Guid.Empty)
        {
            throw new CompanyOutboxPermanentException("Notification payload is missing tenant context.");
        }

        if (string.IsNullOrWhiteSpace(message.DedupeKey))
        {
            throw new CompanyOutboxPermanentException("Notification payload is missing a deduplication key.");
        }

        var notificationType = ParseNotificationType(message.NotificationType);
        var priority = CompanyNotificationPriorityValues.Parse(message.Priority);
        var recipients = await ResolveRecipientsAsync(message, cancellationToken);
        var createdCount = 0;
        var pendingDedupeKeys = new List<string>();

        foreach (var userId in recipients)
        {
            var dedupeKey = $"{message.DedupeKey}:{userId:N}";
            var exists = await _dbContext.CompanyNotifications
                .AnyAsync(x => x.CompanyId == message.CompanyId && x.UserId == userId && x.DedupeKey == dedupeKey, cancellationToken);
            if (exists)
            {
                continue;
            }

            pendingDedupeKeys.Add(dedupeKey);
            createdCount++;
            _dbContext.CompanyNotifications.Add(new CompanyNotification(
                Guid.NewGuid(),
                message.CompanyId,
                userId,
                notificationType,
                priority,
                message.Title,
                message.Body,
                message.RelatedEntityType,
                message.RelatedEntityId,
                message.ActionUrl,
                message.MetadataJson,
                dedupeKey,
                message.BriefingId));
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            if (!await AllPendingNotificationsAlreadyExistAsync(message.CompanyId, pendingDedupeKeys, cancellationToken))
            {
                throw;
            }

            DetachPendingNotifications();
            _logger.LogInformation(
                ex,
                "Treated duplicate notification delivery for {NotificationType} in company {CompanyId} as idempotent success.",
                message.NotificationType,
                message.CompanyId);
        }

        _logger.LogInformation(
            "Created {NotificationCount} in-app notification(s) for {NotificationType} in company {CompanyId}.",
            createdCount,
            message.NotificationType,
            message.CompanyId);

        void DetachPendingNotifications()
        {
            foreach (var entry in _dbContext.ChangeTracker.Entries<CompanyNotification>().Where(x => x.State == EntityState.Added).ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private async Task<bool> AllPendingNotificationsAlreadyExistAsync(Guid companyId, IReadOnlyCollection<string> dedupeKeys, CancellationToken cancellationToken) =>
        dedupeKeys.Count == 0 || await _dbContext.CompanyNotifications.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId && dedupeKeys.Contains(x.DedupeKey), cancellationToken) == dedupeKeys.Distinct(StringComparer.Ordinal).Count();

    private async Task<IReadOnlyList<Guid>> ResolveRecipientsAsync(NotificationDeliveryRequestedMessage message, CancellationToken cancellationToken)
    {
        if (message.RecipientUserId is Guid recipientUserId && recipientUserId != Guid.Empty)
        {
            var isActive = await _dbContext.CompanyMemberships
                .IgnoreQueryFilters()
                .AnyAsync(x => x.CompanyId == message.CompanyId && x.UserId == recipientUserId && x.Status == CompanyMembershipStatus.Active, cancellationToken);
            return isActive ? [recipientUserId] : [];
        }

        var query = _dbContext.CompanyMemberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == message.CompanyId && x.Status == CompanyMembershipStatus.Active && x.UserId.HasValue);

        if (CompanyMembershipRoles.TryParse(message.RecipientRole, out var role))
        {
            query = query.Where(x => x.Role == role || x.Role == CompanyMembershipRole.Owner || x.Role == CompanyMembershipRole.Admin);
        }
        else
        {
            query = query.Where(x => x.Role == CompanyMembershipRole.Owner || x.Role == CompanyMembershipRole.Admin);
        }

        return await query.Select(x => x.UserId!.Value).Distinct().ToListAsync(cancellationToken);
    }

    private static CompanyNotificationType ParseNotificationType(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "approval_requested" => CompanyNotificationType.ApprovalRequested,
            "escalation" => CompanyNotificationType.Escalation,
            "workflow_failure" => CompanyNotificationType.WorkflowFailure,
            "briefing_available" => CompanyNotificationType.BriefingAvailable,
            _ => throw new CompanyOutboxPermanentException($"Unsupported notification type '{value}'.")
        };
}

internal sealed class LoggingCompanyInvitationSender : ICompanyInvitationSender
{
    private readonly ILogger<LoggingCompanyInvitationSender> _logger;

    public LoggingCompanyInvitationSender(ILogger<LoggingCompanyInvitationSender> logger)
    {
        _logger = logger;
    }

    public Task<CompanyInvitationSendResult> SendAsync(CompanyInvitationDeliveryRequestedMessage invitation, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Simulated invitation delivery for invitation {InvitationId}, company {CompanyId}, email {Email}, role {Role}, correlation {CorrelationId}.",
            invitation.InvitationId,
            invitation.CompanyId,
            invitation.Email,
            invitation.Role.ToStorageValue(),
            invitation.CorrelationId);

        return Task.FromResult(new CompanyInvitationSendResult($"log:{invitation.InvitationId:N}"));
    }
}

internal sealed class CompanyOutboxDispatcherBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<CompanyOutboxDispatcherOptions> _options;
    private readonly ILogger<CompanyOutboxDispatcherBackgroundService> _logger;

    public CompanyOutboxDispatcherBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<CompanyOutboxDispatcherOptions> options,
        ILogger<CompanyOutboxDispatcherBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("Company outbox dispatcher is disabled.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollIntervalSeconds));
        var batchSize = Math.Max(1, _options.Value.BatchSize);

        _logger.LogInformation(
            "Company outbox dispatcher started with batch size {BatchSize}, poll interval {PollIntervalSeconds} seconds, max attempts {MaxAttempts}, and retry base delay {RetryDelaySeconds} seconds.",
            batchSize,
            pollInterval.TotalSeconds,
            Math.Max(1, _options.Value.MaxAttempts),
            Math.Max(0, _options.Value.RetryDelaySeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            var executionCorrelationId = Guid.NewGuid().ToString("N");
            using var scope = _logger.BeginScope(ExecutionLogScope.ForBackground(executionCorrelationId));

            try
            {
                using var serviceScope = _serviceScopeFactory.CreateScope();
                var processor = serviceScope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
                var handledCount = await processor.DispatchPendingAsync(stoppingToken);
                if (handledCount > 0)
                {
                    _logger.LogInformation("Company outbox dispatcher processed {HandledCount} message(s) in the current cycle.", handledCount);
                }

                if (handledCount < batchSize)
                {
                    await Task.Delay(pollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Company outbox dispatcher loop failed.");
                await Task.Delay(pollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Company outbox dispatcher stopped.");
    }
}
