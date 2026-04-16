using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

internal sealed class BriefingUpdateJobProducer : IBriefingUpdateJobProducer
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ILogger<BriefingUpdateJobProducer> _logger;

    public BriefingUpdateJobProducer(
        VirtualCompanyDbContext dbContext,
        ILogger<BriefingUpdateJobProducer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task<BriefingUpdateJobEnqueueResult> EnqueueEventDrivenAsync(
        Guid companyId,
        string eventType,
        string correlationId,
        string idempotencyKey,
        Dictionary<string, JsonNode?>? sourceMetadata,
        CancellationToken cancellationToken) =>
        EnqueueAsync(
            new EnqueueBriefingUpdateJobCommand(
                companyId,
                CompanyBriefingUpdateJobTriggerTypeValues.EventDriven,
                null,
                eventType,
                correlationId,
                idempotencyKey,
                sourceMetadata),
            cancellationToken);

    public Task<BriefingUpdateJobEnqueueResult> EnqueueScheduledAsync(
        Guid companyId,
        string triggerType,
        string briefingType,
        string correlationId,
        string idempotencyKey,
        Dictionary<string, JsonNode?>? sourceMetadata,
        CancellationToken cancellationToken) =>
        EnqueueAsync(
            new EnqueueBriefingUpdateJobCommand(
                companyId,
                triggerType,
                briefingType,
                null,
                correlationId,
                idempotencyKey,
                sourceMetadata),
            cancellationToken);

    public async Task<BriefingUpdateJobEnqueueResult> EnqueueAsync(
        EnqueueBriefingUpdateJobCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);

        var triggerType = CompanyBriefingUpdateJobTriggerTypeValues.Parse(command.TriggerType);
        CompanyBriefingType? briefingType = string.IsNullOrWhiteSpace(command.BriefingType)
            ? null
            : CompanyBriefingTypeValues.Parse(command.BriefingType);
        var idempotencyKey = command.IdempotencyKey.Trim();

        var existing = await FindExistingAsync(command.CompanyId, idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Skipped duplicate briefing update job for company {CompanyId} with idempotency key {IdempotencyKey}.",
                command.CompanyId,
                idempotencyKey);
            return ToResult(existing, created: false);
        }

        var job = new CompanyBriefingUpdateJob(
            Guid.NewGuid(),
            command.CompanyId,
            triggerType,
            briefingType,
            NormalizeEventType(command.EventType),
            command.CorrelationId,
            idempotencyKey,
            command.SourceMetadata,
            maxAttempts: 5,
            command.NextAttemptAtUtc);

        _dbContext.CompanyBriefingUpdateJobs.Add(job);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var duplicate = await TryDetachAndFindExistingAsync(command.CompanyId, idempotencyKey, cancellationToken);
            if (duplicate is null)
            {
                throw;
            }

            _logger.LogInformation(
                "Skipped concurrently inserted duplicate briefing update job for company {CompanyId} with idempotency key {IdempotencyKey}.",
                command.CompanyId,
                idempotencyKey);
            return ToResult(duplicate, created: false);
        }

        _logger.LogInformation(
            "Created briefing update job {JobId} for company {CompanyId}. TriggerType={TriggerType} EventType={EventType} CorrelationId={CorrelationId} IdempotencyKey={IdempotencyKey}.",
            job.Id,
            job.CompanyId,
            job.TriggerType.ToStorageValue(),
            job.EventType,
            job.CorrelationId,
            job.IdempotencyKey);

        return ToResult(job, created: true);
    }

    public async Task ScheduleRetryAsync(
        Guid companyId,
        Guid jobId,
        DateTime nextAttemptAtUtc,
        string? errorCode,
        string error,
        string? errorDetails,
        CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(companyId, jobId, cancellationToken);
        job.ScheduleRetry(nextAttemptAtUtc, errorCode, error, errorDetails, DateTime.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Scheduled retry for briefing update job {JobId} for company {CompanyId}. AttemptCount={AttemptCount} NextAttemptAt={NextAttemptAt} ErrorCode={ErrorCode}.",
            job.Id,
            job.CompanyId,
            job.AttemptCount,
            job.NextAttemptAt);
    }

    public async Task RecordFinalFailureAsync(
        Guid companyId,
        Guid jobId,
        string? errorCode,
        string error,
        string? errorDetails,
        CancellationToken cancellationToken)
    {
        var job = await GetJobAsync(companyId, jobId, cancellationToken);
        job.MarkFailed(errorCode, error, errorDetails, DateTime.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogError(
            "Recorded final failure for briefing update job {JobId} for company {CompanyId}. AttemptCount={AttemptCount} ErrorCode={ErrorCode} Error={Error}.",
            job.Id,
            job.CompanyId,
            job.AttemptCount,
            job.LastError);
    }

    private async Task<CompanyBriefingUpdateJob> GetJobAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken) =>
        await _dbContext.CompanyBriefingUpdateJobs
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == jobId, cancellationToken)
        ?? throw new KeyNotFoundException("Briefing update job not found.");

    private async Task<CompanyBriefingUpdateJob?> TryDetachAndFindExistingAsync(
        Guid companyId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        foreach (var entry in _dbContext.ChangeTracker.Entries<CompanyBriefingUpdateJob>().Where(x => x.State == EntityState.Added).ToList())
        {
            entry.State = EntityState.Detached;
        }

        return await FindExistingAsync(companyId, idempotencyKey, cancellationToken);
    }

    private Task<CompanyBriefingUpdateJob?> FindExistingAsync(
        Guid companyId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        _dbContext.CompanyBriefingUpdateJobs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.IdempotencyKey == idempotencyKey,
                cancellationToken);

    private static void Validate(EnqueueBriefingUpdateJobCommand command)
    {
        if (command.CompanyId == Guid.Empty)
        {
            throw new BriefingValidationException(new Dictionary<string, string[]>
            {
                [nameof(command.CompanyId)] = ["Company id is required."]
            });
        }

        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        AddRequired(errors, nameof(command.TriggerType), command.TriggerType, 32);
        AddRequired(errors, nameof(command.CorrelationId), command.CorrelationId, 128);
        AddRequired(errors, nameof(command.IdempotencyKey), command.IdempotencyKey, 300);

        if (string.Equals(command.TriggerType, CompanyBriefingUpdateJobTriggerTypeValues.EventDriven, StringComparison.OrdinalIgnoreCase) &&
            !IsSupportedEventType(command.EventType))
        {
            errors[nameof(command.EventType)] =
            [
                "Event-driven briefing update jobs require a supported event type."
            ];
        }

        if (IsScheduledTriggerType(command.TriggerType))
        {
            if (string.IsNullOrWhiteSpace(command.BriefingType))
            {
                errors[nameof(command.BriefingType)] =
                [
                    "Scheduled briefing update jobs require a briefing type."
                ];
            }

            if (!string.IsNullOrWhiteSpace(command.EventType))
            {
                errors[nameof(command.EventType)] = ["Scheduled briefing update jobs cannot include an event type."];
            }
        }

        if (errors.Count > 0)
        {
            throw new BriefingValidationException(errors);
        }
    }

    private static void AddRequired(Dictionary<string, string[]> errors, string name, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[name] = [$"{name} is required."];
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            errors[name] = [$"{name} must be {maxLength} characters or fewer."];
        }
    }

    private static string? NormalizeEventType(string? eventType) =>
        string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim();

    private static bool IsScheduledTriggerType(string? triggerType) =>
        string.Equals(triggerType, CompanyBriefingUpdateJobTriggerTypeValues.Daily, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(triggerType, CompanyBriefingUpdateJobTriggerTypeValues.Weekly, StringComparison.OrdinalIgnoreCase);

    private static bool IsSupportedEventType(string? eventType) =>
        string.Equals(eventType, BriefingUpdateEventTypes.TaskStatusChanged, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, BriefingUpdateEventTypes.WorkflowStateChanged, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, BriefingUpdateEventTypes.ApprovalRequested, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, BriefingUpdateEventTypes.ApprovalDecision, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, BriefingUpdateEventTypes.Escalation, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(eventType, BriefingUpdateEventTypes.AgentGeneratedAlert, StringComparison.OrdinalIgnoreCase);

    private static BriefingUpdateJobEnqueueResult ToResult(CompanyBriefingUpdateJob job, bool created) =>
        new(
            job.Id,
            created,
            job.Status.ToStorageValue(),
            job.IdempotencyKey);
}
