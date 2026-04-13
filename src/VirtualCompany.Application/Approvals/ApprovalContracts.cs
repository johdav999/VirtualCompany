using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Approvals;

public sealed record CreateApprovalRequestCommand(
    string TargetEntityType,
    Guid TargetEntityId,
    string RequestedByActorType,
    Guid RequestedByActorId,
    string ApprovalType,
    Dictionary<string, JsonNode?>? ThresholdContext,
    string? RequiredRole = null,
    Guid? RequiredUserId = null,
    IReadOnlyList<CreateApprovalStepInput>? Steps = null);

public sealed record CreateApprovalStepInput(
    int SequenceNo,
    string ApproverType,
    string ApproverRef);

public sealed record ApprovalDecisionCommand(
    Guid ApprovalId,
    string Decision,
    Guid? StepId = null,
    string? Comment = null);

public sealed record ApprovalRequestDto(
    Guid Id,
    Guid CompanyId,
    string TargetEntityType,
    Guid TargetEntityId,
    string RequestedByActorType,
    Guid RequestedByActorId,
    string ApprovalType,
    string? RequiredRole,
    Guid? RequiredUserId,
    string Status,
    Dictionary<string, JsonNode?> ThresholdContext,
    IReadOnlyList<ApprovalStepDto> Steps,
    ApprovalStepDto? CurrentStep,
    string? DecisionSummary,
    string? RejectionComment,
    string RationaleSummary,
    string AffectedDataSummary,
    IReadOnlyList<ApprovalAffectedEntityDto> AffectedEntities,
    string? ThresholdSummary,
    DateTime CreatedAt);

public sealed record ApprovalAffectedEntityDto(
    string EntityType,
    Guid EntityId,
    string Label);

public sealed record ApprovalStepDto(
    Guid Id,
    int SequenceNo,
    string ApproverType,
    string ApproverRef,
    string Status,
    Guid? DecidedByUserId = null,
    DateTime? DecidedAt = null,
    string? Comment = null);

public sealed record ApprovalDecisionResultDto(
    ApprovalRequestDto Approval,
    ApprovalStepDto DecidedStep,
    ApprovalStepDto? NextStep,
    bool IsFinalized);

public interface IApprovalRequestService
{
    Task<IReadOnlyList<ApprovalRequestDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken);
    Task<ApprovalRequestDto> GetAsync(Guid companyId, Guid approvalId, CancellationToken cancellationToken);
    Task<ApprovalRequestDto> CreateAsync(Guid companyId, CreateApprovalRequestCommand command, CancellationToken cancellationToken);
    Task<ApprovalDecisionResultDto> DecideAsync(Guid companyId, ApprovalDecisionCommand command, CancellationToken cancellationToken);
}

public sealed class ApprovalValidationException : Exception
{
    public ApprovalValidationException(IDictionary<string, string[]> errors)
        : base("Approval request validation failed.") =>
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class ApprovalDecisionForbiddenException : Exception
{
    public ApprovalDecisionForbiddenException(string message)
        : base(message)
    {
    }
}