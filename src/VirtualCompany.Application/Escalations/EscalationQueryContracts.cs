namespace VirtualCompany.Application.Escalations;

public sealed record EscalationRecordFilter(
    Guid? SourceEntityId = null,
    string? SourceEntityType = null,
    Guid? PolicyId = null,
    string? CorrelationId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int? Skip = null,
    int? Take = null);

public sealed record EscalationRecordDto(
    Guid Id,
    Guid CompanyId,
    Guid PolicyId,
    Guid SourceEntityId,
    string SourceEntityType,
    int EscalationLevel,
    string Reason,
    DateTime TriggeredAt,
    string? CorrelationId,
    string Status,
    DateTime CreatedAt,
    int LifecycleVersion,
    DateTime? ResolvedAt = null,
    DateTime? ReopenedAt = null);

public sealed record EscalationRecordListResult(
    IReadOnlyList<EscalationRecordDto> Items,
    int TotalCount,
    int Skip,
    int Take);

public sealed record PolicyEvaluationHistoryFilter(
    Guid? SourceEntityId = null,
    string? SourceEntityType = null,
    Guid? PolicyId = null,
    string? CorrelationId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int? Skip = null,
    int? Take = null);

public sealed record PolicyEvaluationHistoryItemDto(
    Guid AuditEventId,
    Guid CompanyId,
    Guid? PolicyId,
    Guid? SourceEntityId,
    string? SourceEntityType,
    int? EscalationLevel,
    string Action,
    string Outcome,
    bool? ConditionsMet,
    string? EvaluationResult,
    string? Reason,
    DateTime EvaluatedAt,
    string? CorrelationId,
    Guid? EscalationRecordId,
    string TargetType,
    string TargetId,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record PolicyEvaluationHistoryResult(
    IReadOnlyList<PolicyEvaluationHistoryItemDto> Items,
    int TotalCount,
    int Skip,
    int Take);

public interface IEscalationQueryService
{
    Task<EscalationRecordListResult> ListEscalationsAsync(
        Guid companyId,
        EscalationRecordFilter filter,
        CancellationToken cancellationToken);

    Task<EscalationRecordDto> GetEscalationAsync(Guid companyId, Guid escalationId, CancellationToken cancellationToken);

    Task<PolicyEvaluationHistoryResult> ListPolicyEvaluationHistoryAsync(
        Guid companyId,
        PolicyEvaluationHistoryFilter filter,
        CancellationToken cancellationToken);
}
