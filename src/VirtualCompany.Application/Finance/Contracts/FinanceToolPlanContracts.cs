using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Finance;

public static class FinanceToolPlanVersions
{
    public const string ContractV1 = "finance-tool-plan-v1";
    public const string PromptV1 = "finance-tool-planner-v1";
    public const string CapabilityV1 = "1.0.0";
}

public static class FinanceToolPlanStates
{
    public const string Ready = "ready";
    public const string NeedsClarification = "needs_clarification";
    public const string ConfirmationRequired = "confirmation_required";
    public const string ApprovalRequired = "approval_required";
    public const string Unsupported = "unsupported";
    public const string Failed = "failed";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Ready, NeedsClarification, ConfirmationRequired, ApprovalRequired, Unsupported, Failed
    };
}

public static class FinanceToolPlanReasonCodes
{
    public const string Planned = "finance_plan_ready";
    public const string ClarificationRequired = "finance_plan_clarification_required";
    public const string ConfirmationRequired = "finance_plan_confirmation_required";
    public const string ApprovalRequired = "finance_plan_approval_required";
    public const string UnsupportedRequest = "finance_plan_unsupported_request";
    public const string ActorNotAuthorized = "finance_plan_actor_not_authorized";
    public const string NoPermittedTools = "finance_plan_no_permitted_tools";
    public const string InvalidProviderResult = "finance_plan_invalid_provider_result";
    public const string InvalidTool = "finance_plan_invalid_tool";
    public const string InvalidToolVersion = "finance_plan_invalid_tool_version";
    public const string InvalidAction = "finance_plan_invalid_action";
    public const string InvalidScope = "finance_plan_invalid_scope";
    public const string InvalidArguments = "finance_plan_invalid_arguments";
    public const string MissingMaterialInput = "finance_plan_missing_material_input";
    public const string InvalidDependencies = "finance_plan_invalid_dependencies";
    public const string CyclicDependencies = "finance_plan_cyclic_dependencies";
    public const string UngroundedTarget = "finance_plan_ungrounded_target";
    public const string MixedCompanyContext = "finance_plan_mixed_company_context";
    public const string SensitiveContextRejected = "finance_plan_sensitive_context_rejected";
    public const string RequestBoundaryExceeded = "finance_plan_request_boundary_exceeded";
    public const string LimitExceeded = "finance_plan_limit_exceeded";
    public const string ProviderUnavailable = "finance_plan_provider_unavailable";
    public const string ProviderRateLimited = "finance_plan_provider_rate_limited";
    public const string TimedOut = "finance_plan_timed_out";
}

public static class FinanceToolPlanCheckpointStates
{
    public const string NotRequired = "not_required";
    public const string Required = "required";
    public const string Pending = "pending";
}

public sealed record FinanceToolPlanContextItem(
    Guid CompanyId,
    string SourceId,
    string SourceType,
    string Title,
    string Content,
    string? RecordId = null,
    string? RecordVersion = null,
    DateTime? UpdatedUtc = null);

public sealed record FinanceToolPlanRequest(
    Guid CompanyId,
    Guid AgentId,
    string UserRequest,
    IReadOnlyList<FinanceToolPlanContextItem>? Context = null,
    Guid? TaskId = null,
    Guid? ConversationId = null,
    string? CorrelationId = null,
    IReadOnlyList<FinancePlanningReference>? References = null);

public sealed record FinanceToolPlanLimits(
    int MaximumSteps,
    int MaximumRecords,
    int MaximumInputCharacters,
    int MaximumOutputCharacters,
    int MaximumModelCalls,
    int MaximumToolCalls,
    int MaximumElapsedSeconds,
    decimal MaximumEstimatedCost);

public sealed record FinanceToolPlanStep(
    string StepId,
    int Order,
    IReadOnlyList<string> Dependencies,
    string ExpectedAction,
    string ExpectedEffect,
    string ToolName,
    string ToolVersion,
    string ActionType,
    string Scope,
    IReadOnlyDictionary<string, JsonNode?> NormalizedArguments,
    IReadOnlyList<string> EvidenceRequirements,
    string ConfirmationState,
    string ApprovalState,
    decimal EstimatedCost);

public sealed record FinanceToolPlan(
    Guid PlanId,
    int Revision,
    string ContractVersion,
    Guid CompanyId,
    Guid AgentId,
    string State,
    string ReasonCode,
    string SafeExplanation,
    IReadOnlyList<FinanceToolPlanStep> Steps,
    FinanceToolPlanLimits Limits,
    string EffectiveAuthorityVersion,
    string EffectiveAuthorityHash,
    string PlanningContextVersion,
    string PlanningContextHash,
    IReadOnlyList<FinancePlanningEvidenceReference> GroundedEvidence,
    string RequestHash,
    string CorrelationId,
    DateTime CreatedUtc)
{
    public bool CanExecute => false;
}

public interface IFinanceToolPlanner
{
    Task<FinanceToolPlan> PlanAsync(FinanceToolPlanRequest request, CancellationToken cancellationToken);
}
