using System.Text;
using System.Text.Json.Nodes;
using Cronos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TimeZoneConverter;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CronosScheduleExpressionValidator : IScheduleExpressionValidator
{
    public ScheduleValidationResult ValidateCronExpression(string? cronExpression)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            return ScheduleValidationResult.Invalid("Cron expression is required.");
        }

        try
        {
            _ = CronExpression.Parse(cronExpression.Trim(), CronFormat.Standard);
            return ScheduleValidationResult.Valid;
        }
        catch (CronFormatException ex)
        {
            return ScheduleValidationResult.Invalid($"Cron expression is invalid: {ex.Message}");
        }
    }

    public ScheduleValidationResult ValidateTimeZoneId(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return ScheduleValidationResult.Invalid("Timezone is required.");
        }

        try
        {
            _ = ResolveTimeZone(timeZoneId);
            return ScheduleValidationResult.Valid;
        }
        catch (TimeZoneNotFoundException)
        {
            return ScheduleValidationResult.Invalid("Timezone must be a valid IANA or Windows timezone identifier.");
        }
        catch (InvalidTimeZoneException)
        {
            return ScheduleValidationResult.Invalid("Timezone must be a valid IANA or Windows timezone identifier.");
        }
    }

    internal static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        var normalized = string.IsNullOrWhiteSpace(timeZoneId) ? "UTC" : timeZoneId.Trim();
        if (string.Equals(normalized, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return TimeZoneInfo.Utc;
        }

        return TZConvert.GetTimeZoneInfo(normalized);
    }
}

public sealed class CronosScheduledTriggerNextRunCalculator : IScheduledTriggerNextRunCalculator
{
    private readonly IScheduleExpressionValidator _validator;

    public CronosScheduledTriggerNextRunCalculator(IScheduleExpressionValidator validator) =>
        _validator = validator;

    public DateTime? GetNextRunUtc(string cronExpression, string timeZoneId, DateTime referenceUtc)
    {
        var cronValidation = _validator.ValidateCronExpression(cronExpression);
        if (!cronValidation.IsValid)
        {
            throw new ArgumentException(cronValidation.Error, nameof(cronExpression));
        }

        var timezoneValidation = _validator.ValidateTimeZoneId(timeZoneId);
        if (!timezoneValidation.IsValid)
        {
            throw new ArgumentException(timezoneValidation.Error, nameof(timeZoneId));
        }

        var utcReference = referenceUtc.Kind == DateTimeKind.Utc
            ? referenceUtc
            : referenceUtc.ToUniversalTime();
        var expression = CronExpression.Parse(cronExpression.Trim(), CronFormat.Standard);
        var timeZone = CronosScheduleExpressionValidator.ResolveTimeZone(timeZoneId);
        return expression.GetNextOccurrence(utcReference, timeZone);
    }
}

public sealed class EfAgentScheduledTriggerRepository : IAgentScheduledTriggerRepository
{
    private readonly VirtualCompanyDbContext _dbContext;

    public EfAgentScheduledTriggerRepository(VirtualCompanyDbContext dbContext) =>
        _dbContext = dbContext;

    public Task<AgentScheduledTrigger?> GetByIdAsync(
        Guid companyId,
        Guid triggerId,
        CancellationToken cancellationToken) =>
        _dbContext.AgentScheduledTriggers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == triggerId, cancellationToken);

    public async Task<IReadOnlyList<AgentScheduledTrigger>> ListByAgentAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken) =>
        await _dbContext.AgentScheduledTriggers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AgentId == agentId)
            .OrderBy(x => x.Code)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AgentScheduledTrigger>> ListDueAsync(
        Guid companyId,
        DateTime dueAtUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var normalizedDueAtUtc = dueAtUtc.Kind == DateTimeKind.Utc ? dueAtUtc : dueAtUtc.ToUniversalTime();
        var effectiveBatchSize = Math.Max(1, batchSize);
        return await _dbContext.AgentScheduledTriggers
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsEnabled &&
                x.NextRunUtc.HasValue &&
                x.NextRunUtc.Value <= normalizedDueAtUtc &&
                (!x.DisabledUtc.HasValue || normalizedDueAtUtc <= x.DisabledUtc.Value))
            .OrderBy(x => x.NextRunUtc)
            .ThenBy(x => x.Id)
            .Take(effectiveBatchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(
        AgentScheduledTrigger trigger,
        CancellationToken cancellationToken) =>
        await _dbContext.AgentScheduledTriggers.AddAsync(trigger, cancellationToken);

    public Task DeleteAsync(
        AgentScheduledTrigger trigger,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _dbContext.AgentScheduledTriggers.Remove(trigger);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    public async Task<bool> TryRecordEnqueueWindowAsync(
        AgentScheduledTriggerEnqueueWindow window,
        CancellationToken cancellationToken)
    {
        await _dbContext.AgentScheduledTriggerEnqueueWindows.AddAsync(window, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _dbContext.Entry(window).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("IX_agent_scheduled_trigger_enqueue_windows_company_id_scheduled_trigger_id_window_start_at_window_end_at", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("AK_", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("constraint", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class AgentScheduledTriggerService : IAgentScheduledTriggerService
{
    private const int NameMaxLength = 200;
    private const int CodeMaxLength = 100;
    private const int CronExpressionMaxLength = 200;
    private const int TimeZoneIdMaxLength = 100;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly IAgentScheduledTriggerRepository _repository;
    private readonly IScheduleExpressionValidator _validator;
    private readonly IScheduledTriggerNextRunCalculator _nextRunCalculator;
    private readonly TimeProvider _timeProvider;

    public AgentScheduledTriggerService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        IAgentScheduledTriggerRepository repository,
        IScheduleExpressionValidator validator,
        IScheduledTriggerNextRunCalculator nextRunCalculator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _repository = repository;
        _validator = validator;
        _nextRunCalculator = nextRunCalculator;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<AgentScheduledTriggerDto>> ListAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        await EnsureAgentExistsAsync(companyId, agentId, cancellationToken);

        var triggers = await _repository.ListByAgentAsync(companyId, agentId, cancellationToken);
        return triggers.Select(x => x.ToDto()).ToList();
    }

    public async Task<AgentScheduledTriggerDto> GetAsync(
        Guid companyId,
        Guid agentId,
        Guid triggerId,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        var trigger = await GetTriggerForAgentAsync(companyId, agentId, triggerId, cancellationToken);
        return trigger.ToDto();
    }

    public async Task<AgentScheduledTriggerDto> CreateAsync(
        Guid companyId,
        Guid agentId,
        CreateAgentScheduledTriggerCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        await EnsureAgentExistsAsync(companyId, agentId, cancellationToken);

        var normalized = Validate(command);
        await EnsureCodeIsUniqueAsync(companyId, normalized.Code, null, cancellationToken);
        DateTime? nextRunUtc = normalized.Enabled
            ? RequireNextRun(normalized.CronExpression, normalized.TimeZoneId, NowUtc())
            : null;

        var trigger = new AgentScheduledTrigger(
            Guid.NewGuid(),
            companyId,
            agentId,
            normalized.Name,
            normalized.Code,
            normalized.CronExpression,
            normalized.TimeZoneId,
            nextRunUtc,
            normalized.Enabled,
            command.Metadata);

        await _repository.AddAsync(trigger, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return trigger.ToDto();
    }

    public async Task<AgentScheduledTriggerDto> UpdateAsync(
        Guid companyId,
        Guid agentId,
        Guid triggerId,
        UpdateAgentScheduledTriggerCommand command,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        var trigger = await GetTriggerForAgentAsync(companyId, agentId, triggerId, cancellationToken);

        var normalized = Validate(command);
        await EnsureCodeIsUniqueAsync(companyId, normalized.Code, triggerId, cancellationToken);
        DateTime? nextRunUtc = trigger.IsEnabled
            ? RequireNextRun(normalized.CronExpression, normalized.TimeZoneId, NowUtc())
            : null;

        trigger.UpdateSchedule(
            normalized.Name,
            normalized.Code,
            normalized.CronExpression,
            normalized.TimeZoneId,
            nextRunUtc,
            command.Metadata);

        await _repository.SaveChangesAsync(cancellationToken);
        return trigger.ToDto();
    }

    public async Task<AgentScheduledTriggerDto> EnableAsync(
        Guid companyId,
        Guid agentId,
        Guid triggerId,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        var trigger = await GetTriggerForAgentAsync(companyId, agentId, triggerId, cancellationToken);
        var nextRunUtc = RequireNextRun(trigger.CronExpression, trigger.TimeZoneId, NowUtc());

        trigger.Enable(nextRunUtc);
        await _repository.SaveChangesAsync(cancellationToken);
        return trigger.ToDto();
    }

    public async Task<AgentScheduledTriggerDto> DisableAsync(
        Guid companyId,
        Guid agentId,
        Guid triggerId,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        var trigger = await GetTriggerForAgentAsync(companyId, agentId, triggerId, cancellationToken);

        trigger.Disable(NowUtc());
        await _repository.SaveChangesAsync(cancellationToken);
        return trigger.ToDto();
    }

    public async Task DeleteAsync(
        Guid companyId,
        Guid agentId,
        Guid triggerId,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        var trigger = await GetTriggerForAgentAsync(companyId, agentId, triggerId, cancellationToken);

        await _repository.DeleteAsync(trigger, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    private async Task RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _companyMembershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }
    }

    private async Task EnsureAgentExistsAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken);

        if (!exists)
        {
            throw new KeyNotFoundException("Agent not found.");
        }
    }

    private async Task<AgentScheduledTrigger> GetTriggerForAgentAsync(
        Guid companyId,
        Guid agentId,
        Guid triggerId,
        CancellationToken cancellationToken)
    {
        await EnsureAgentExistsAsync(companyId, agentId, cancellationToken);

        var trigger = await _repository.GetByIdAsync(companyId, triggerId, cancellationToken);
        if (trigger is null || trigger.AgentId != agentId)
        {
            throw new KeyNotFoundException("Schedule trigger not found.");
        }

        return trigger;
    }

    private async Task EnsureCodeIsUniqueAsync(
        Guid companyId,
        string code,
        Guid? currentTriggerId,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.AgentScheduledTriggers
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == companyId && x.Code == code && (!currentTriggerId.HasValue || x.Id != currentTriggerId.Value), cancellationToken);

        if (exists)
        {
            throw new AgentScheduledTriggerValidationException(new Dictionary<string, string[]>
            {
                [nameof(CreateAgentScheduledTriggerCommand.Code)] = ["Code must be unique within the company."]
            });
        }
    }

    private DateTime NowUtc() => _timeProvider.GetUtcNow().UtcDateTime;

    private DateTime RequireNextRun(string cronExpression, string timeZoneId, DateTime referenceUtc) =>
        _nextRunCalculator.GetNextRunUtc(cronExpression, timeZoneId, referenceUtc)
        ?? throw new AgentScheduledTriggerValidationException(new Dictionary<string, string[]>
        {
            [nameof(CreateAgentScheduledTriggerCommand.CronExpression)] = ["Cron expression does not have a future occurrence."]
        });

    private NormalizedCreateCommand Validate(CreateAgentScheduledTriggerCommand command)
    {
        var normalized = ValidateScheduleFields(command.Name, command.Code, command.CronExpression, command.TimeZoneId);
        return new NormalizedCreateCommand(normalized.Name, normalized.Code, normalized.CronExpression, normalized.TimeZoneId, command.Enabled);
    }

    private NormalizedScheduleCommand Validate(UpdateAgentScheduledTriggerCommand command) =>
        ValidateScheduleFields(command.Name, command.Code, command.CronExpression, command.TimeZoneId);

    private NormalizedScheduleCommand ValidateScheduleFields(string name, string? code, string cronExpression, string timeZoneId)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var normalizedName = NormalizeRequired(errors, nameof(CreateAgentScheduledTriggerCommand.Name), name, NameMaxLength);
        var normalizedCode = NormalizeCode(errors, code, normalizedName);
        var normalizedCron = NormalizeRequired(errors, nameof(CreateAgentScheduledTriggerCommand.CronExpression), cronExpression, CronExpressionMaxLength);
        var normalizedTimeZone = NormalizeRequired(errors, nameof(CreateAgentScheduledTriggerCommand.TimeZoneId), timeZoneId, TimeZoneIdMaxLength);

        var cronValidation = _validator.ValidateCronExpression(normalizedCron);
        if (!cronValidation.IsValid)
        {
            AddError(errors, nameof(CreateAgentScheduledTriggerCommand.CronExpression), cronValidation.Error!);
        }

        var timezoneValidation = _validator.ValidateTimeZoneId(normalizedTimeZone);
        if (!timezoneValidation.IsValid)
        {
            AddError(errors, nameof(CreateAgentScheduledTriggerCommand.TimeZoneId), timezoneValidation.Error!);
        }

        if (errors.Count > 0)
        {
            throw new AgentScheduledTriggerValidationException(errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
        }

        return new NormalizedScheduleCommand(normalizedName, normalizedCode, normalizedCron, normalizedTimeZone);
    }

    private static string NormalizeRequired(
        IDictionary<string, List<string>> errors,
        string key,
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, key, $"{key} is required.");
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            AddError(errors, key, $"{key} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string NormalizeCode(IDictionary<string, List<string>> errors, string? requestedCode, string fallbackName)
    {
        var code = string.IsNullOrWhiteSpace(requestedCode)
            ? Slugify(fallbackName)
            : requestedCode.Trim();

        if (string.IsNullOrWhiteSpace(code))
        {
            AddError(errors, nameof(CreateAgentScheduledTriggerCommand.Code), "Code is required.");
            return string.Empty;
        }

        if (code.Length > CodeMaxLength)
        {
            AddError(errors, nameof(CreateAgentScheduledTriggerCommand.Code), $"Code must be {CodeMaxLength} characters or fewer.");
        }

        return code.ToUpperInvariant();
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(CodeMaxLength);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }

            if (builder.Length >= CodeMaxLength)
            {
                break;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static void AddError(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }

    private sealed record NormalizedScheduleCommand(string Name, string Code, string CronExpression, string TimeZoneId);
    private sealed record NormalizedCreateCommand(string Name, string Code, string CronExpression, string TimeZoneId, bool Enabled);
}

public sealed class AgentScheduledTriggerSchedulerOptions
{
    public const string SectionName = "AgentScheduledTriggerScheduler";

    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
    public int LockTtlSeconds { get; set; } = 120;
    public int BatchSize { get; set; } = 50;
    public string LockKey { get; set; } = "agent-scheduled-trigger-scheduler";
}

public sealed class AgentScheduledTriggerSchedulerCoordinator : IAgentScheduledTriggerSchedulerCoordinator
{
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IAgentScheduledTriggerPollingService _pollingService;
    private readonly IOptions<AgentScheduledTriggerSchedulerOptions> _options;
    private readonly ILogger<AgentScheduledTriggerSchedulerCoordinator> _logger;

    public AgentScheduledTriggerSchedulerCoordinator(
        IDistributedLockProvider lockProvider,
        IAgentScheduledTriggerPollingService pollingService,
        IOptions<AgentScheduledTriggerSchedulerOptions> options,
        ILogger<AgentScheduledTriggerSchedulerCoordinator> logger)
    {
        _lockProvider = lockProvider;
        _pollingService = pollingService;
        _options = options;
        _logger = logger;
    }

    public async Task<AgentScheduledTriggerPollingRunResult> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var lockKey = string.IsNullOrWhiteSpace(options.LockKey)
            ? "agent-scheduled-trigger-scheduler"
            : options.LockKey.Trim();
        var lockTtl = TimeSpan.FromSeconds(Math.Max(5, options.LockTtlSeconds));
        var batchSize = Math.Max(1, options.BatchSize);

        await using var handle = await _lockProvider.TryAcquireAsync(lockKey, lockTtl, cancellationToken);
        if (handle is null)
        {
            _logger.LogInformation(
                "Agent scheduled trigger scheduler skipped polling because distributed lock {LockKey} was not acquired.",
                lockKey);
            return new AgentScheduledTriggerPollingRunResult(false, 0, 0, 0, 0);
        }

        return await _pollingService.RunDueTriggersAsync(now.UtcDateTime, batchSize, cancellationToken);
    }
}

public sealed class AgentScheduledTriggerPollingService : IAgentScheduledTriggerPollingService
{
    private const int MaxCatchUpOccurrencesPerTrigger = 1000;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAgentScheduledTriggerRepository _repository;
    private readonly IScheduledTriggerNextRunCalculator _nextRunCalculator;
    private readonly ICompanyOutboxEnqueuer _outboxEnqueuer;
    private readonly ILogger<AgentScheduledTriggerPollingService> _logger;

    public AgentScheduledTriggerPollingService(
        VirtualCompanyDbContext dbContext,
        IAgentScheduledTriggerRepository repository,
        IScheduledTriggerNextRunCalculator nextRunCalculator,
        ICompanyOutboxEnqueuer outboxEnqueuer,
        ILogger<AgentScheduledTriggerPollingService> logger)
    {
        _dbContext = dbContext;
        _repository = repository;
        _nextRunCalculator = nextRunCalculator;
        _outboxEnqueuer = outboxEnqueuer;
        _logger = logger;
    }

    public async Task<AgentScheduledTriggerPollingRunResult> RunDueTriggersAsync(
        DateTime dueAtUtc,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var normalizedDueAtUtc = dueAtUtc.Kind == DateTimeKind.Utc
            ? dueAtUtc
            : dueAtUtc.ToUniversalTime();
        var effectiveBatchSize = Math.Max(1, batchSize);
        var companyIds = await ResolveDueCompanyIdsAsync(normalizedDueAtUtc, effectiveBatchSize, cancellationToken);
        var triggersScanned = 0;
        var enqueued = 0;
        var failures = 0;

        foreach (var companyId in companyIds)
        {
            var dueTriggers = await _repository.ListDueAsync(
                companyId,
                normalizedDueAtUtc,
                Math.Max(1, effectiveBatchSize - triggersScanned),
                cancellationToken);

            foreach (var trigger in dueTriggers)
            {
                triggersScanned++;
                try
                {
                    if (await TryEnqueueExecutionRequestAsync(trigger, normalizedDueAtUtc, cancellationToken))
                    {
                        enqueued++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures++;
                    _logger.LogError(
                        ex,
                        "Agent scheduled trigger {TriggerId} for company {CompanyId} failed while enqueueing its due execution request.",
                        trigger.Id,
                        trigger.CompanyId);
                }

                if (triggersScanned >= effectiveBatchSize)
                {
                    return new AgentScheduledTriggerPollingRunResult(true, companyIds.Count, triggersScanned, enqueued, failures);
                }
            }
        }

        return new AgentScheduledTriggerPollingRunResult(true, companyIds.Count, triggersScanned, enqueued, failures);
    }

    private async Task<bool> TryEnqueueExecutionRequestAsync(
        AgentScheduledTrigger trigger,
        DateTime dueAtUtc,
        CancellationToken cancellationToken)
    {
        if (!trigger.NextRunUtc.HasValue)
        {
            return false;
        }

        var nextDueRunUtc = NormalizeUtc(trigger.NextRunUtc.Value);

        if (!trigger.IsEligibleForEnqueue(nextDueRunUtc))
        {
            trigger.MarkEvaluated(dueAtUtc, trigger.IsEnabled ? nextDueRunUtc : null);
            await _repository.SaveChangesAsync(cancellationToken);
            return false;
        }

        var enqueuedAny = false;
        var occurrencesProcessed = 0;

        while (nextDueRunUtc <= dueAtUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!trigger.IsEligibleForEnqueue(nextDueRunUtc))
            {
                break;
            }

            var followingRunUtc = _nextRunCalculator.GetNextRunUtc(
                trigger.CronExpression,
                trigger.TimeZoneId,
                nextDueRunUtc);
            var windowEndUtc = followingRunUtc.HasValue && followingRunUtc.Value > nextDueRunUtc
                ? NormalizeUtc(followingRunUtc.Value)
                : nextDueRunUtc.AddMinutes(1);

            if (await TryRecordAndEnqueueExecutionRequestAsync(
                trigger,
                nextDueRunUtc,
                windowEndUtc,
                dueAtUtc,
                cancellationToken))
            {
                enqueuedAny = true;
            }

            occurrencesProcessed++;
            if (!followingRunUtc.HasValue)
            {
                nextDueRunUtc = default;
                break;
            }

            nextDueRunUtc = NormalizeUtc(followingRunUtc.Value);
            if (occurrencesProcessed >= MaxCatchUpOccurrencesPerTrigger && nextDueRunUtc <= dueAtUtc)
            {
                _logger.LogWarning(
                    "Agent scheduled trigger {TriggerId} for company {CompanyId} reached the catch-up limit of {Limit} due schedule windows.",
                    trigger.Id,
                    trigger.CompanyId,
                    MaxCatchUpOccurrencesPerTrigger);
                break;
            }
        }

        DateTime? nextRunUtc = nextDueRunUtc == default ? null : nextDueRunUtc;
        if (enqueuedAny)
        {
            trigger.MarkEnqueued(dueAtUtc, nextRunUtc);
        }
        else
        {
            trigger.MarkEvaluated(dueAtUtc, nextRunUtc);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return enqueuedAny;
    }

    private async Task<bool> TryRecordAndEnqueueExecutionRequestAsync(
        AgentScheduledTrigger trigger,
        DateTime scheduledAtUtc,
        DateTime windowEndUtc,
        DateTime enqueuedAtUtc,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = $"agent-scheduled-trigger:{trigger.CompanyId:N}:{trigger.Id:N}:{scheduledAtUtc:yyyyMMddHHmmss}";
        var executionRequestId = $"{trigger.Id:N}:{scheduledAtUtc:yyyyMMddHHmmss}";
        var window = new AgentScheduledTriggerEnqueueWindow(
            Guid.NewGuid(),
            trigger.CompanyId,
            trigger.Id,
            scheduledAtUtc,
            windowEndUtc,
            enqueuedAtUtc,
            executionRequestId);

        if (!await _repository.TryRecordEnqueueWindowAsync(window, cancellationToken))
        {
            return false;
        }

        _outboxEnqueuer.Enqueue(
            trigger.CompanyId,
            CompanyOutboxTopics.AgentScheduledTriggerExecutionRequested,
            new AgentScheduledTriggerExecutionRequestMessage(
                trigger.CompanyId,
                trigger.AgentId,
                trigger.Id,
                trigger.Code,
                scheduledAtUtc,
                trigger.CronExpression,
                trigger.TimeZoneId,
                trigger.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase),
                idempotencyKey,
                idempotencyKey),
            idempotencyKey,
            availableAtUtc: enqueuedAtUtc,
            idempotencyKey: idempotencyKey,
            causationId: trigger.Id.ToString("N"));

        return true;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    private async Task<IReadOnlyList<Guid>> ResolveDueCompanyIdsAsync(
        DateTime dueAtUtc,
        int batchSize,
        CancellationToken cancellationToken) =>
        await _dbContext.AgentScheduledTriggers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.IsEnabled &&
                x.NextRunUtc.HasValue &&
                x.NextRunUtc.Value <= dueAtUtc &&
                (!x.DisabledUtc.HasValue || dueAtUtc <= x.DisabledUtc.Value))
            .Select(x => x.CompanyId)
            .Distinct()
            .OrderBy(x => x)
            .Take(Math.Max(1, batchSize))
            .ToListAsync(cancellationToken);
}

public sealed class AgentScheduledTriggerSchedulerBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<AgentScheduledTriggerSchedulerOptions> _options;
    private readonly ILogger<AgentScheduledTriggerSchedulerBackgroundService> _logger;

    public AgentScheduledTriggerSchedulerBackgroundService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<AgentScheduledTriggerSchedulerOptions> options,
        ILogger<AgentScheduledTriggerSchedulerBackgroundService> logger)
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
            _logger.LogInformation("Agent scheduled trigger scheduler background service is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollIntervalSeconds)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var coordinator = scope.ServiceProvider.GetRequiredService<IAgentScheduledTriggerSchedulerCoordinator>();
                await coordinator.RunOnceAsync(_timeProvider.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent scheduled trigger scheduler polling loop failed unexpectedly.");
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
    }
}
