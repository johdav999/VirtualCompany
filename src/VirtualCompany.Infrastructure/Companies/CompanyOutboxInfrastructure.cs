using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Companies;
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

    public CompanyOutboxEnqueuer(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Enqueue(Guid companyId, string topic, object payload, string? correlationId = null, DateTime? availableAtUtc = null)
    {
        _dbContext.CompanyOutboxMessages.Add(new CompanyOutboxMessage(
            Guid.NewGuid(),
            companyId,
            topic,
            JsonSerializer.Serialize(payload, SerializerOptions),
            correlationId,
            availableAtUtc));
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

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyInvitationDeliveryDispatcher _invitationDeliveryDispatcher;
    private readonly IOptions<CompanyOutboxDispatcherOptions> _options;
    private readonly IBackgroundJobExecutor _backgroundJobExecutor;
    private readonly ILogger<CompanyOutboxProcessor> _logger;

    public CompanyOutboxProcessor(
        VirtualCompanyDbContext dbContext,
        ICompanyInvitationDeliveryDispatcher invitationDeliveryDispatcher,
        IOptions<CompanyOutboxDispatcherOptions> options,
        IBackgroundJobExecutor backgroundJobExecutor,
        ILogger<CompanyOutboxProcessor> logger)
    {
        _dbContext = dbContext;
        _invitationDeliveryDispatcher = invitationDeliveryDispatcher;
        _options = options;
        _backgroundJobExecutor = backgroundJobExecutor;
        _logger = logger;
    }

    public async Task<int> DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var claimedMessages = await ClaimBatchAsync(cancellationToken);
        var handledCount = 0;

        foreach (var message in claimedMessages)
        {
            using var scope = _logger.BeginScope(ExecutionLogScope.ForOutboxMessage(message.Id, message.CompanyId, message.CorrelationId ?? message.ClaimToken));

            var attempt = message.AttemptCount + 1;
            var execution = await _backgroundJobExecutor.ExecuteAsync(
                new BackgroundJobExecutionContext(
                    $"company-outbox:{message.Topic}",
                    attempt,
                    MaxAttempts,
                    message.CompanyId,
                    message.CorrelationId ?? message.ClaimToken),
                innerCancellationToken => DispatchAsync(message, innerCancellationToken),
                GetRetryDelay(attempt),
                cancellationToken);

            switch (execution.Outcome)
            {
                case BackgroundJobExecutionOutcome.Succeeded:
                    message.MarkProcessed();
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    handledCount++;
                    break;
                case BackgroundJobExecutionOutcome.PermanentFailure:
                case BackgroundJobExecutionOutcome.RetryExhausted:
                    message.MarkDiscarded(ResolveFailureMessage(execution));
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    handledCount++;
                    break;
                case BackgroundJobExecutionOutcome.RetryScheduled:
                    message.ScheduleRetry(
                        DateTime.UtcNow.Add(execution.RetryDelay ?? TimeSpan.Zero),
                        ResolveFailureMessage(execution));
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    break;
            }
        }

        return handledCount;
    }

    private int BatchSize => Math.Max(1, _options.Value.BatchSize);
    private int MaxAttempts => Math.Max(1, _options.Value.MaxAttempts);

    private TimeSpan GetRetryDelay(int attempt)
    {
        var baseDelaySeconds = Math.Max(0, _options.Value.RetryDelaySeconds);
        if (baseDelaySeconds == 0)
        {
            return TimeSpan.Zero;
        }

        var multiplier = Math.Pow(2d, Math.Max(0, attempt - 1));
        var delaySeconds = Math.Min(baseDelaySeconds * multiplier, TimeSpan.FromMinutes(15).TotalSeconds);
        return TimeSpan.FromSeconds(delaySeconds);
    }

    private static string ResolveFailureMessage(BackgroundJobExecutionResult execution) => string.IsNullOrWhiteSpace(execution.ErrorMessage) ? "Unhandled company outbox processing failure." : execution.ErrorMessage;
    private TimeSpan ClaimTimeout => TimeSpan.FromSeconds(Math.Max(5, _options.Value.ClaimTimeoutSeconds));

    private async Task<IReadOnlyList<CompanyOutboxMessage>> ClaimBatchAsync(CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;
        var claimToken = Guid.NewGuid().ToString("N");

        var candidates = await _dbContext.CompanyOutboxMessages
            .Where(x => x.ProcessedUtc == null &&
                        x.AvailableUtc <= utcNow &&
                        (x.ClaimedUtc == null || x.ClaimedUtc <= utcNow - ClaimTimeout))
            .OrderBy(x => x.AvailableUtc)
            .ThenBy(x => x.CreatedUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        foreach (var candidate in candidates)
        {
            candidate.TryClaim(claimToken, utcNow, ClaimTimeout);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogDebug(ex, "Company outbox claim contention detected.");
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            return [];
        }

        return candidates
            .Where(x => string.Equals(x.ClaimToken, claimToken, StringComparison.Ordinal))
            .ToArray();
    }

    private async Task DispatchAsync(CompanyOutboxMessage message, CancellationToken cancellationToken)
    {
        switch (message.Topic)
        {
            case CompanyOutboxTopics.InvitationDeliveryRequested:
            {
                var payload = Deserialize<CompanyInvitationDeliveryRequestedMessage>(message);
                var correlationId = string.IsNullOrWhiteSpace(payload.CorrelationId)
                    ? message.CorrelationId
                    : payload.CorrelationId;

                await _invitationDeliveryDispatcher.DispatchAsync(
                    payload with { CorrelationId = correlationId },
                    cancellationToken);
                break;
            }
            case CompanyOutboxTopics.InvitationCreated:
            case CompanyOutboxTopics.InvitationResent:
            case CompanyOutboxTopics.InvitationRevoked:
            case CompanyOutboxTopics.InvitationAccepted:
            case CompanyOutboxTopics.MembershipRoleChanged:
                _logger.LogInformation("Acknowledged company outbox event '{Topic}' without external dispatch.", message.Topic);
                break;
            default:
                throw new CompanyOutboxPermanentException($"Unsupported company outbox topic '{message.Topic}'.");
        }
    }

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

    public CompanyInvitationDeliveryDispatcher(
        VirtualCompanyDbContext dbContext,
        ICompanyInvitationSender invitationSender,
        ILogger<CompanyInvitationDeliveryDispatcher> logger)
    {
        _dbContext = dbContext;
        _invitationSender = invitationSender;
        _logger = logger;
    }

    public async Task DispatchAsync(CompanyInvitationDeliveryRequestedMessage message, CancellationToken cancellationToken)
    {
        var invitation = await _dbContext.CompanyInvitations
            .Include(x => x.Company)
            .SingleOrDefaultAsync(x => x.CompanyId == message.CompanyId && x.Id == message.InvitationId, cancellationToken)
            ?? throw new CompanyOutboxPermanentException($"Company invitation '{message.InvitationId}' was not found.");

        var correlationId = string.IsNullOrWhiteSpace(message.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : message.CorrelationId;

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
