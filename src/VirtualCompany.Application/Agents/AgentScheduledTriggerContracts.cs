using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Agents;

public sealed record ScheduleValidationResult(bool IsValid, string? Error)
{
    public static ScheduleValidationResult Valid { get; } = new(true, null);

    public static ScheduleValidationResult Invalid(string error) =>
        new(false, string.IsNullOrWhiteSpace(error) ? "Schedule value is invalid." : error);
}

public sealed record AgentScheduledTriggerDto(
    Guid Id,
    Guid CompanyId,
    Guid AgentId,
    string Name,
    string Code,
    string CronExpression,
    string TimeZoneId,
    bool IsEnabled,
    DateTime? NextRunAt,
    DateTime? LastEvaluatedAt,
    DateTime? LastEnqueuedAt,
    DateTime? LastRunAt,
    DateTime? EnabledAt,
    DateTime? DisabledAt,
    Dictionary<string, JsonNode?> Metadata,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ScheduledTriggerEnqueueWindowDto(
    Guid Id,
    Guid CompanyId,
    Guid ScheduledTriggerId,
    DateTime WindowStartAt,
    DateTime WindowEndAt,
    DateTime EnqueuedAt,
    string? ExecutionRequestId,
    DateTime CreatedAt);

public sealed record CreateAgentScheduledTriggerCommand(
    string Name,
    string? Code,
    string CronExpression,
    string TimeZoneId,
    bool Enabled = true,
    Dictionary<string, JsonNode?>? Metadata = null);

public sealed record UpdateAgentScheduledTriggerCommand(
    string Name,
    string? Code,
    string CronExpression,
    string TimeZoneId,
    Dictionary<string, JsonNode?>? Metadata = null);

public sealed record AgentScheduledTriggerStateCommand(Dictionary<string, JsonNode?>? Metadata = null);

public interface IScheduleExpressionValidator
{
    ScheduleValidationResult ValidateCronExpression(string? cronExpression);

    ScheduleValidationResult ValidateTimeZoneId(string? timeZoneId);
}

public sealed record AgentScheduledTriggerExecutionRequestMessage(
    Guid CompanyId,
    Guid AgentId,
    Guid TriggerId,
    string TriggerCode,
    DateTime ScheduledAtUtc,
    string CronExpression,
    string TimeZoneId,
    Dictionary<string, JsonNode?> Metadata,
    string CorrelationId,
    string IdempotencyKey);

public interface IScheduledTriggerNextRunCalculator
{
    DateTime? GetNextRunUtc(string cronExpression, string timeZoneId, DateTime referenceUtc);
}

public sealed record AgentScheduledTriggerPollingRunResult(
    bool LockAcquired,
    int CompaniesScanned,
    int TriggersScanned,
    int ExecutionRequestsEnqueued,
    int Failures);

public interface IAgentScheduledTriggerPollingService
{
    Task<AgentScheduledTriggerPollingRunResult> RunDueTriggersAsync(
        DateTime dueAtUtc,
        int batchSize,
        CancellationToken cancellationToken);
}

public interface IAgentScheduledTriggerSchedulerCoordinator
{
    Task<AgentScheduledTriggerPollingRunResult> RunOnceAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IAgentScheduledTriggerService
{
    Task<IReadOnlyList<AgentScheduledTriggerDto>> ListAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken);

    Task<AgentScheduledTriggerDto> GetAsync(Guid companyId, Guid agentId, Guid triggerId, CancellationToken cancellationToken);

    Task<AgentScheduledTriggerDto> CreateAsync(
        Guid companyId,
        Guid agentId,
        CreateAgentScheduledTriggerCommand command,
        CancellationToken cancellationToken);

    Task<AgentScheduledTriggerDto> UpdateAsync(
        Guid companyId,
        Guid agentId,
        Guid triggerId,
        UpdateAgentScheduledTriggerCommand command,
        CancellationToken cancellationToken);

    Task<AgentScheduledTriggerDto> EnableAsync(Guid companyId, Guid agentId, Guid triggerId, CancellationToken cancellationToken);
    Task<AgentScheduledTriggerDto> DisableAsync(Guid companyId, Guid agentId, Guid triggerId, CancellationToken cancellationToken);
    Task DeleteAsync(Guid companyId, Guid agentId, Guid triggerId, CancellationToken cancellationToken);
}

public interface IAgentScheduledTriggerRepository
{
    Task<AgentScheduledTrigger?> GetByIdAsync(
        Guid companyId,
        Guid triggerId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentScheduledTrigger>> ListByAgentAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentScheduledTrigger>> ListDueAsync(
        Guid companyId,
        DateTime dueAtUtc,
        int batchSize,
        CancellationToken cancellationToken);

    Task AddAsync(
        AgentScheduledTrigger trigger,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        AgentScheduledTrigger trigger,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<bool> TryRecordEnqueueWindowAsync(
        AgentScheduledTriggerEnqueueWindow window,
        CancellationToken cancellationToken);
}

public sealed class AgentScheduledTriggerValidationException : Exception
{
    public AgentScheduledTriggerValidationException(IDictionary<string, string[]> errors)
        : base("Agent scheduled trigger validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public static class AgentScheduledTriggerDtoMapper
{
    public static AgentScheduledTriggerDto ToDto(this AgentScheduledTrigger trigger) =>
        new(trigger.Id, trigger.CompanyId, trigger.AgentId, trigger.Name, trigger.Code, trigger.CronExpression, trigger.TimeZoneId, trigger.IsEnabled, trigger.NextRunUtc, trigger.LastEvaluatedUtc, trigger.LastEnqueuedUtc, trigger.LastRunUtc, trigger.EnabledUtc, trigger.DisabledUtc, trigger.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase), trigger.CreatedUtc, trigger.UpdatedUtc);

    public static ScheduledTriggerEnqueueWindowDto ToDto(this AgentScheduledTriggerEnqueueWindow window) =>
        new(window.Id, window.CompanyId, window.ScheduledTriggerId, window.WindowStartUtc, window.WindowEndUtc, window.EnqueuedUtc, window.ExecutionRequestId, window.CreatedUtc);
}
