using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Marketing;

public sealed record RequestMarketingOperatingRun(string TriggerType, string TriggerReference, string IdempotencyKey,
    string CorrelationId, Guid? CompanyGoalId = null, Guid? OperatingInitiativeId = null, Guid? WorkTaskId = null,
    string Cadence = "on_demand", int? ExpectedGoalVersion = null, int? ExpectedPlanVersion = null,
    int? ExpectedInitiativeVersion = null);
public sealed record MarketingOperatingRunDto(Guid Id, Guid CompanyId, Guid AgentId, Guid? CompanyGoalId,
    Guid? OperatingInitiativeId, Guid? WorkTaskId, string TriggerType, string TriggerReference,
    string EffectiveAuthority, string Status, string SelectedWorkJson, string EvidenceJson,
    string MissingEvidenceJson, string? OutcomeSummary, string? RecoveryCode, decimal? BudgetLimit,
    decimal BudgetUsed, int AttemptCount, DateTime CreatedUtc, DateTime? CompletedUtc,
    string AssignmentContextJson = "{}", int ProgressCount = 0, int OutcomeCount = 0);
public sealed record MarketingOperatingActionDto(Guid Id, Guid MarketingOperatingRunId, int Sequence, int Version,
    string ActionType, string Title, string? Capability, string? Tool, string TargetJson, string SourceVersion,
    string GoalRelevance, string DependenciesJson, string ExpectedCompletionEvidence, string AuthorityDecision,
    bool RequiresApproval, string IdempotencyKey, decimal EstimatedCost, decimal ActualCost, string Status,
    int AttemptCount, int MaximumAttempts, DateTime? LeaseExpiresUtc, string? ArtifactType, Guid? ArtifactId,
    string ActualEvidenceJson, string? RecoveryCode, string? RecoveryGuidance, DateTime? NextAttemptUtc,
    DateTime CreatedUtc, DateTime? CompletedUtc);
public sealed record RetryMarketingOperatingActionRequest(string RecoveryRationale);
public sealed record CancelMarketingOperatingActionRequest(string Rationale);

public static class MarketingAssignmentReasonCodes
{
    public const string CompanyPaused = "company_paused";
    public const string GoalInactive = "goal_inactive";
    public const string PlanUnavailable = "plan_unavailable";
    public const string StaleGoalVersion = "stale_goal_version";
    public const string StalePlanVersion = "stale_plan_version";
    public const string StaleInitiativeVersion = "stale_initiative_version";
    public const string WrongOwner = "wrong_owner";
    public const string CrossCompanyLink = "cross_company_link";
    public const string DependencyBlocked = "dependency_blocked";
    public const string DuplicateActiveAssignment = "duplicate_active_assignment";
    public const string CompletionEvidenceMissing = "completion_evidence_missing";
    public const string BudgetExhausted = "budget_exhausted";
    public const string CapacityExhausted = "capacity_exhausted";
    public const string TaskUnavailable = "task_unavailable";
}

public sealed record MarketingAssignmentDependencyDto(
    Guid InitiativeId, string Title, string Status, bool IsHard, bool IsSatisfied);

public sealed record MarketingAssignmentContextDto(
    Guid CompanyId, Guid CompanyGoalId, int GoalVersion, Guid OperatingCycleId,
    Guid? OperatingSnapshotId, string? SnapshotSchemaVersion, Guid OperatingPlanId, int PlanVersion,
    Guid OperatingInitiativeId, int InitiativeVersion, Guid? WorkTaskId, int? TaskLifecycleVersion,
    string DesiredOutcome, string Priority, DateTime StartUtc, DateTime TargetUtc,
    Guid AgentId, IReadOnlyList<Guid> ContributorAgentIds, Guid? ReviewerUserId,
    IReadOnlyList<MarketingAssignmentDependencyDto> Dependencies, decimal? BudgetLimit, decimal BudgetUsed,
    int CapacityLimit, int CapacityUsed, string CompletionEvidence, string ValidationState,
    IReadOnlyList<string> ActionRestrictions, string CorrelationId, string Authority,
    bool IsAccepted, string ReasonCode, string Explanation);

public sealed record MarketingAuthorityContext(
    CompanyAutonomyLevel CompanyAuthority, AgentAutonomyLevel AgentAuthority,
    CompanyAutonomyLevel MarketingActionCeiling, bool CompanyPaused = false, bool GoalActive = true,
    bool InitiativeAllowed = true, bool AgentAvailable = true, bool CapabilityAllowed = true,
    bool ToolAllowed = true, bool MarketingActionAllowed = true, bool ConsentAllowed = true,
    bool ApprovalSatisfied = true, bool ProviderHealthy = true, bool WorkloadAvailable = true,
    bool BudgetAvailable = true);

public sealed record MarketingAuthorityDecision(
    bool Allowed, CompanyAutonomyLevel EffectiveAuthority, string ReasonCode,
    string Explanation, bool RequiresApproval);

public static class MarketingAuthorityPolicy
{
    public static MarketingAuthorityDecision Evaluate(MarketingAuthorityContext context)
    {
        var agentCeiling = context.AgentAuthority switch
        {
            AgentAutonomyLevel.Level0 => CompanyAutonomyLevel.Recommend,
            AgentAutonomyLevel.Level1 => CompanyAutonomyLevel.Organize,
            AgentAutonomyLevel.Level2 => CompanyAutonomyLevel.OperateInternally,
            _ => CompanyAutonomyLevel.ControlledExecution
        };
        var effective = (CompanyAutonomyLevel)Math.Min((int)context.CompanyAuthority,
            Math.Min((int)agentCeiling, (int)context.MarketingActionCeiling));
        var restriction = new (bool Restricted, string Code, string Explanation)[]
        {
            (context.CompanyPaused, "company_paused", "Company operation is paused."),
            (!context.GoalActive, "goal_inactive", "The company goal is not active."),
            (!context.InitiativeAllowed, "initiative_restricted", "The initiative does not permit this action."),
            (!context.AgentAvailable, "marketing_agent_unavailable", "Maya is not available for new work."),
            (!context.CapabilityAllowed, "capability_not_allowed", "Maya's capability scope does not allow this action."),
            (!context.ToolAllowed, "tool_not_allowed", "Maya's tool scope does not allow this action."),
            (!context.MarketingActionAllowed, "marketing_policy_restricted", "Marketing policy does not allow this action."),
            (!context.ConsentAllowed, "consent_required", "Customer consent does not allow this action."),
            (!context.ProviderHealthy, "provider_unavailable", "The required provider connection is not healthy."),
            (!context.WorkloadAvailable, "capacity_exhausted", "Maya's configured work capacity is exhausted."),
            (!context.BudgetAvailable, "budget_exhausted", "The assignment budget is exhausted.")
        }.FirstOrDefault(x => x.Restricted);
        if (restriction.Restricted)
            return new(false, CompanyAutonomyLevel.Recommend, restriction.Code, restriction.Explanation, false);
        if (!context.ApprovalSatisfied && effective > CompanyAutonomyLevel.Recommend)
            return new(true, CompanyAutonomyLevel.Recommend, "approval_required",
                "Approval is required before authority can exceed recommendations.", true);
        return new(true, effective, "allowed", "The action is within all current authority limits.", false);
    }
}

public sealed record ReportMarketingWorkCommand(
    Guid MarketingOperatingRunId, Guid OperatingInitiativeId, Guid? WorkTaskId,
    string IdempotencyKey, string EvidenceVersion, string CompletedArtifactsJson,
    string ExpectedResultsJson, string ActualResultsJson, decimal? Confidence,
    string DataGapsJson, string BlockersJson, string DependenciesJson,
    string ChangedForecastJson, string Lessons, string RequestedNextAction,
    string CorrelationId);

public sealed record MarketingWorkEvidenceDto(
    Guid Id, Guid CompanyId, Guid MarketingOperatingRunId, Guid OperatingInitiativeId,
    Guid? WorkTaskId, string RecordType, int Version, string IdempotencyKey,
    string EvidenceVersion, string CompletedArtifactsJson, string ExpectedResultsJson,
    string ActualResultsJson, decimal? Confidence, string DataGapsJson, string BlockersJson,
    string DependenciesJson, string ChangedForecastJson, string Lessons,
    string RequestedNextAction, string CorrelationId, DateTime CreatedUtc);

public sealed record RaiseMarketingCompanySignalCommand(
    Guid? MarketingOperatingRunId, string SignalType, string Severity, string Summary,
    string EvidenceJson, string IdempotencyKey, string CorrelationId);

public sealed record MarketingCompanySignalDto(
    Guid Id, Guid CompanyId, Guid? MarketingOperatingRunId, string SignalType,
    string Severity, string Summary, string EvidenceJson, string Status,
    bool CycleEvaluationRequested, string IdempotencyKey, string CorrelationId,
    DateTime CreatedUtc, DateTime UpdatedUtc);

public sealed class MarketingAssignmentException(string reasonCode, string message) : InvalidOperationException(message)
{
    public string ReasonCode { get; } = reasonCode;
}

public interface IMarketingCompanyOrchestrationService
{
    Task<MarketingAssignmentContextDto> ResolveAssignmentAsync(Guid companyId, Guid marketingAgentId,
        RequestMarketingOperatingRun request, CancellationToken ct);
    Task<MarketingWorkEvidenceDto> ReportProgressAsync(Guid companyId, ReportMarketingWorkCommand command, CancellationToken ct);
    Task<MarketingWorkEvidenceDto> ReportOutcomeAsync(Guid companyId, ReportMarketingWorkCommand command, CancellationToken ct);
    Task<MarketingCompanySignalDto> RaiseSignalAsync(Guid companyId, RaiseMarketingCompanySignalCommand command, CancellationToken ct);
    Task<IReadOnlyList<MarketingWorkEvidenceDto>> ListWorkEvidenceAsync(Guid companyId, Guid? runId, CancellationToken ct);
    Task<IReadOnlyList<MarketingCompanySignalDto>> ListSignalsAsync(Guid companyId, CancellationToken ct);
}
public interface IMarketingOperatingLoopService
{
    Task<MarketingOperatingRunDto> RunAsync(Guid companyId, Guid marketingAgentId, RequestMarketingOperatingRun request, CancellationToken ct);
    Task<IReadOnlyList<MarketingOperatingRunDto>> ListAsync(Guid companyId, int take, CancellationToken ct);
    Task<IReadOnlyList<MarketingOperatingActionDto>> ListActionsAsync(Guid companyId, Guid runId, CancellationToken ct);
    Task<MarketingOperatingActionDto?> RetryActionAsync(Guid companyId, Guid runId, Guid actionId,
        RetryMarketingOperatingActionRequest request, CancellationToken ct);
    Task<MarketingOperatingActionDto?> CancelActionAsync(Guid companyId, Guid runId, Guid actionId,
        CancelMarketingOperatingActionRequest request, CancellationToken ct);
}
