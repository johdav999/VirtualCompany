using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Orchestration;

public sealed record RecordOperatingEventCommand(string EventType, string SourceType, string SourceId,
    int SourceVersion, DateTime ObservedUtc, string Materiality, string DeduplicationKey,
    string CorrelationId, Guid? AffectedGoalId = null, Dictionary<string, JsonNode?>? Payload = null);

public sealed record OperatingEventDto(Guid Id, string EventType, string SourceType, string SourceId,
    int SourceVersion, DateTime ObservedUtc, string Materiality, string Status,
    string? SuppressionReason, Guid? AffectedGoalId, DateTime CreatedUtc, DateTime? ProcessedUtc);

public sealed record OperatingCycleRequestDto(Guid Id, Guid? OperatingEventId, Guid? OperatingCycleId,
    string TriggerType, string? TriggerReference, string Status, DateTime NotBeforeUtc,
    int AttemptCount, int MaxAttempts, DateTime? LeaseExpiresUtc, string? FailureCode,
    string? FailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);

public sealed record OperatingCycleRequestRunResult(int Claimed, int Completed, int Suppressed,
    int Retried, int DeadLettered);

public interface ICompanyOperatingEventService
{
    Task<OperatingEventDto> RecordAsync(Guid companyId, RecordOperatingEventCommand command,
        CancellationToken cancellationToken);
    Task<OperatingCycleRequestDto> RequestAsync(Guid companyId, string triggerType,
        string? triggerReference, string deduplicationKey, string correlationId, DateTime notBeforeUtc,
        Guid? operatingEventId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OperatingEventDto>> ListEventsAsync(Guid companyId, int take,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<OperatingCycleRequestDto>> ListRequestsAsync(Guid companyId, int take,
        CancellationToken cancellationToken);
}

public interface IOperatingCycleRequestProcessor
{
    Task<OperatingCycleRequestRunResult> RunOnceAsync(int batchSize, CancellationToken cancellationToken);
}
