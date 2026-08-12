namespace VirtualCompany.Application.Orchestration;

public sealed record OperatingDispatchDto(
    Guid Id, Guid CompanyId, Guid InitiativeId, Guid TaskId, string Kind, string Status,
    int AttemptCount, int MaxAttempts, DateTime? NextAttemptUtc, DateTime? LeaseExpiresUtc,
    Guid? OrchestrationRunId, Guid? CollaborationPlanId, string? FailureCode,
    string? FailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);

public sealed record OperatingCollaborationParticipantDto(
    Guid Id, Guid InitiativeId, Guid AgentId, string Role, string Pattern, int Sequence,
    string Objective, string ExpectedArtifact);

public sealed record OperatingDispatchRunResult(int Claimed, int Completed, int AwaitingApproval,
    int Retried, int Blocked, int DeadLettered);

public interface IOperatingWorkDispatcher
{
    Task<OperatingDispatchRunResult> RunOnceAsync(int batchSize, CancellationToken cancellationToken);
}

public interface IOperatingDispatchQueryService
{
    Task<IReadOnlyList<OperatingDispatchDto>> ListAsync(Guid companyId, int take,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<OperatingCollaborationParticipantDto>> ListCollaborationAsync(
        Guid companyId, Guid initiativeId, CancellationToken cancellationToken);
}
