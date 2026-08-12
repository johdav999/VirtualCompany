using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Orchestration;

public sealed record RequestOperatingCycleCommand(
    string TriggerType = "manual", string? TriggerReference = null,
    string? IdempotencyKey = null, string? CorrelationId = null);
public sealed record ReviewOperatingPlanCommand(string Decision, string? Comment = null, string? CorrelationId = null);

public sealed record OperatingCycleDto(
    Guid Id, Guid CompanyId, string TriggerType, string? TriggerReference, Guid CoordinatorAgentId,
    string Status, int ConfigurationVersion, string CorrelationId, string IdempotencyKey,
    Guid? SnapshotId, int ModelCallsUsed, int ToolCallsUsed, int TasksCreated,
    decimal MonetaryBudgetUsed, string? FailureCode, string? FailureSummary,
    DateTime RequestedUtc, DateTime? StartedUtc, DateTime? CompletedUtc,
    IReadOnlyList<OperatingPlanDto> Plans);

public sealed record OperatingPlanDto(
    Guid Id, Guid CompanyId, Guid CycleId, int Version, string Status, string Objective,
    string RationaleSummary, IReadOnlyDictionary<string, JsonNode?> Uncertainty,
    IReadOnlyList<OperatingInitiativeDto> Initiatives,
    IReadOnlyList<OperatingValidationResultDto> ValidationResults,
    DateTime CreatedUtc, DateTime UpdatedUtc);

public sealed record OperatingInitiativeDto(
    Guid Id, Guid GoalId, string Title, string DesiredOutcome, string Priority, string Status,
    string CompletionEvidence, Guid? OwnerAgentId, DateTime? TargetUtc, decimal? Budget,
    Guid? TaskId, Guid? WorkflowInstanceId, int Version);

public sealed record OperatingValidationResultDto(
    Guid Id, Guid? DecisionId, string Validator, string ValidatorVersion, string Outcome,
    string ReasonCode, string Explanation, bool ApprovalRequired,
    IReadOnlyDictionary<string, JsonNode?> Evidence, DateTime EvaluatedUtc);

public sealed record OperatingSnapshotDto(
    Guid Id, Guid CompanyId, Guid CycleId, string SchemaVersion,
    IReadOnlyDictionary<string, JsonNode?> Payload, int SourceCount, int DataGapCount,
    bool IsTruncated, DateTime CreatedUtc);
public sealed record OperatingReviewDto(Guid Id, Guid PlanId, int PlanVersion, Guid InitiativeId,
    string Outcome, string Summary, string ExpectedEvidence, string? ActualEvidence,
    string NextAction, string EvidenceVersion, decimal? Confidence, DateTime CreatedUtc);
public sealed record ProposeControlledNotificationCommand(Guid PlanId, Guid RecipientUserId, string Title, string Body,
    string? ActionUrl = null, string? CorrelationId = null);
public sealed record OperatingDecisionDto(Guid Id, Guid PlanId, string ActionClass, string ActionType,
    string TargetType, string TargetId, string RationaleSummary, string RiskLevel, bool ApprovalRequired, DateTime CreatedUtc);

public interface ICompanyOperatingCycleService
{
    Task<OperatingCycleDto> RunRecommendationCycleAsync(Guid companyId, RequestOperatingCycleCommand command, CancellationToken cancellationToken);
    Task<OperatingCycleDto> GetAsync(Guid companyId, Guid cycleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OperatingCycleDto>> ListAsync(Guid companyId, int take, CancellationToken cancellationToken);
    Task<OperatingCycleDto> ReviewPlanAsync(Guid companyId, Guid planId, ReviewOperatingPlanCommand command, CancellationToken cancellationToken);
    Task<OperatingCycleDto> CommitPlanAsync(Guid companyId, Guid planId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OperatingReviewDto>> ReviewCommittedWorkAsync(Guid companyId, CancellationToken cancellationToken);
    Task<OperatingDecisionDto> ProposeControlledNotificationAsync(Guid companyId, ProposeControlledNotificationCommand command, CancellationToken cancellationToken);
    Task<OperatingDecisionDto> ExecuteControlledActionAsync(Guid companyId, Guid decisionId, CancellationToken cancellationToken);
}
public interface ICompanyOperatingCycleAutomationService
{
    Task<OperatingCycleDto> RunScheduledCycleAsync(Guid companyId, RequestOperatingCycleCommand command, CancellationToken cancellationToken);
}
public interface ICompanyOperatingReviewAutomationService
{
    Task<IReadOnlyList<OperatingReviewDto>> ReviewCommittedWorkAutomaticallyAsync(Guid companyId,
        CancellationToken cancellationToken);
}

public interface ICompanyOperatingSnapshotService
{
    Task<OperatingSnapshotDto> CaptureAsync(Guid companyId, Guid cycleId, CancellationToken cancellationToken);
}

public interface ICompanyOperatingSnapshotQueryService
{
    Task<OperatingSnapshotDto> GetAsync(Guid companyId, Guid snapshotId, CancellationToken cancellationToken);
    Task<IReadOnlyList<OperatingSnapshotDto>> ListAsync(Guid companyId, int take, CancellationToken cancellationToken);
}

public interface IOperatingPlanValidationService
{
    Task<IReadOnlyList<OperatingValidationResultDto>> ValidateAsync(Guid companyId, Guid planId, CancellationToken cancellationToken);
}
