namespace VirtualCompany.Application.Finance;

public sealed record FortnoxWriteCommandRequest(
    Guid CompanyId,
    Guid? ConnectionId,
    Guid? ActorUserId,
    string HttpMethod,
    string Path,
    string TargetCompany,
    string EntityType,
    string PayloadSummary,
    string PayloadHash,
    string SanitizedPayloadJson,
    Guid WriteRequestId,
    string? CorrelationId = null,
    Guid? ApprovedApprovalId = null);

public sealed record FortnoxWriteCommandResult(
    Guid WriteRequestId,
    Guid? ApprovalId,
    string Status,
    string Message,
    bool CanExecute);

public interface IFortnoxWriteCommandService
{
    Task<FortnoxWriteCommandResult> RequestApprovalAsync(
        FortnoxWriteCommandRequest request,
        CancellationToken cancellationToken);

    Task<FortnoxWriteCommandResult> EnsureApprovedForExecutionAsync(
        FortnoxWriteCommandRequest request,
        CancellationToken cancellationToken);

    Task RecordExecutionSucceededAsync(
        FortnoxWriteCommandRequest request,
        object? responsePayload,
        CancellationToken cancellationToken);

    Task RecordExecutionFailedAsync(
        FortnoxWriteCommandRequest request,
        Exception exception,
        CancellationToken cancellationToken);
}
