namespace VirtualCompany.Application.ExecutionExceptions;

public sealed record RecordExecutionExceptionRequest(
    Guid CompanyId,
    string Kind,
    string Severity,
    string Title,
    string Summary,
    string SourceType,
    string SourceId,
    Guid? BackgroundExecutionId,
    string? RelatedEntityType,
    string? RelatedEntityId,
    string IncidentKey,
    string? FailureCode,
    IReadOnlyDictionary<string, string?>? Details);

public sealed record ExecutionExceptionDto(
    Guid Id,
    Guid CompanyId,
    string Kind,
    string Severity,
    string Status,
    string Title,
    string Summary,
    string SourceType,
    string SourceId,
    Guid? BackgroundExecutionId,
    string? RelatedEntityType,
    string? RelatedEntityId,
    string IncidentKey,
    string? FailureCode,
    IReadOnlyDictionary<string, string?> Details,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ResolvedAt);

public interface IExecutionExceptionRecorder
{
    Task<Guid> RecordAsync(RecordExecutionExceptionRequest request, CancellationToken cancellationToken);
}

public interface IExecutionExceptionQueryService
{
    Task<IReadOnlyList<ExecutionExceptionDto>> ListAsync(
        Guid companyId,
        string? status,
        string? kind,
        CancellationToken cancellationToken);

    Task<ExecutionExceptionDto> GetAsync(Guid companyId, Guid exceptionId, CancellationToken cancellationToken);
}