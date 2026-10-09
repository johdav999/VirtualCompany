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
    string? Comment = null,
    Guid? ClientRequestId = null,
    string? ReviewToken = null);

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
    DateTime CreatedAt,
    ApprovalReviewDto? Review = null);

public sealed record ApprovalReviewDto(string Token, bool CanDecide, bool ProposalChanged,
    DateTime? ExpiresAt, string Reviewer, string VersionEvidence,
    IReadOnlyList<ApprovalComparisonDto> Comparison, IReadOnlyList<ApprovalEvidenceDto> Evidence,
    string? ExecutionStatus = null);
public sealed record ApprovalComparisonDto(string Field, string? Before, string? Proposed);
public sealed record ApprovalEvidenceDto(string Label, string Href);

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
    string? Comment = null,
    string? ReviewerName = null);

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

public sealed record AutomatedApprovalGrant(
    Guid GrantId,
    Guid GrantorUserId,
    Guid AgentId,
    string AgentDisplayName,
    string SupplierName,
    string Stage);

public interface IApprovalAutomationService
{
    Task<ApprovalDecisionResultDto> ApproveUnderStandingGrantAsync(
        Guid companyId,
        Guid approvalId,
        AutomatedApprovalGrant grant,
        CancellationToken cancellationToken);
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
