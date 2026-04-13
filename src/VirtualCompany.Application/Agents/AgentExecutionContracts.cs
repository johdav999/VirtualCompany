using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Agents;

public sealed record ExecuteAgentToolCommand(
    string ToolName,
    string ActionType,
    string? Scope,
    Dictionary<string, JsonNode?>? RequestPayload,
    string? ThresholdCategory,
    string? ThresholdKey,
    decimal? ThresholdValue,
    bool SensitiveAction = false);

public sealed record ExecuteAgentToolResultDto(
    Guid ExecutionId,
    string Status,
    Guid? ApprovalRequestId,
    ToolExecutionDecisionDto PolicyDecision,
    Dictionary<string, JsonNode?>? ExecutionResult,
    string Message,
    Dictionary<string, JsonNode?>? ApprovalDecisionChain = null);

public static class PolicyDecisionSchemaVersions
{
    public const string V1 = "2026-03-31";
}

public static class PolicyDecisionEvaluationVersions
{
    public const string Current = "task_8_3_7";
}

public sealed record PolicyDecisionReasonDto(
    string Code,
    string Category,
    string Summary);

public sealed record PolicyDecisionTenantContextDto(
    Guid CompanyId,
    Guid AgentCompanyId,
    bool CompanyScopeMatched);

public sealed record PolicyDecisionActorContextDto(
    Guid AgentId,
    string AgentStatus,
    bool CanReceiveAssignments);

public sealed record PolicyDecisionToolContextDto(
    string ToolName,
    string ActionType,
    string? Scope);

public sealed record PolicyDecisionThresholdEvaluationDto(
    string Category,
    string Key,
    decimal? RequestedValue,
    decimal? ConfiguredValue,
    bool Exceeded,
    bool ApprovalRequired,
    bool SensitiveAction,
    string EvaluationState);

public sealed record PolicyDecisionApprovalRequirementDto(
    string RequirementType,
    string? ApprovalTarget,
    string? PolicySource,
    string? ThresholdCategory,
    string? ThresholdKey,
    decimal? RequestedValue,
    decimal? ConfiguredValue,
    IReadOnlyList<string>? MatchedActions = null,
    IReadOnlyList<string>? MatchedTools = null,
    IReadOnlyList<string>? MatchedScopes = null);

public sealed record PolicyDecisionAuditContextDto(
    Guid ExecutionId,
    string? CorrelationId,
    DateTime EvaluatedAtUtc,
    string PolicyMode,
    string PolicyVersion);

public sealed record ToolExecutionDecisionDto(
    string Outcome,
    IReadOnlyList<string> ReasonCodes,
    string Explanation,
    string EvaluatedAutonomyLevel,
    string EvaluatedActionType,
    string? EvaluatedScope,
    bool ApprovalRequired,
    Dictionary<string, JsonNode?> Metadata,
    string SchemaVersion = PolicyDecisionSchemaVersions.V1,
    IReadOnlyList<PolicyDecisionReasonDto>? Reasons = null,
    PolicyDecisionTenantContextDto? Tenant = null,
    PolicyDecisionActorContextDto? Actor = null,
    PolicyDecisionToolContextDto? Tool = null,
    IReadOnlyList<PolicyDecisionThresholdEvaluationDto>? ThresholdEvaluations = null,
    PolicyDecisionApprovalRequirementDto? ApprovalRequirement = null,
    PolicyDecisionAuditContextDto? Audit = null);

public sealed record PolicyEvaluationRequest(
    Guid CompanyId,
    Guid AgentId,
    Guid AgentCompanyId,
    string AgentStatus,
    string EvaluatedAutonomyLevel,
    bool CanReceiveAssignments,
    IReadOnlyDictionary<string, JsonNode?> ToolPermissions,
    IReadOnlyDictionary<string, JsonNode?> DataScopes,
    IReadOnlyDictionary<string, JsonNode?> ApprovalThresholds,
    IReadOnlyDictionary<string, JsonNode?> EscalationRules,
    string ToolName,
    string ActionType,
    string? Scope,
    IReadOnlyDictionary<string, JsonNode?> RequestPayload,
    string? ThresholdCategory,
    string? ThresholdKey,
    decimal? ThresholdValue,
    bool SensitiveAction,
    Guid ExecutionId,
    string? CorrelationId);

public sealed record ToolExecutionRequest(
    Guid CompanyId,
    Guid AgentId,
    string ToolName,
    string ActionType,
    string? Scope,
    IReadOnlyDictionary<string, JsonNode?> RequestPayload);

public sealed record ToolExecutionResult(
    string Summary,
    Dictionary<string, JsonNode?> Payload);

public interface IPolicyGuardrailEngine
{
    ToolExecutionDecisionDto Evaluate(PolicyEvaluationRequest request);
}

public interface IAgentToolExecutionService
{
    Task<ExecuteAgentToolResultDto> ExecuteAsync(
        Guid companyId,
        Guid agentId,
        ExecuteAgentToolCommand command,
        CancellationToken cancellationToken);
}

public interface ICompanyToolExecutor
{
    Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken);
}

public sealed class AgentExecutionValidationException : Exception
{
    public AgentExecutionValidationException(IDictionary<string, string[]> errors)
        : base("Agent tool execution validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public static class PolicyDecisionOutcomeValues
{
    public const string Allow = "allow";
    public const string Deny = "deny";
    public const string RequireApproval = "require_approval";
}

public static class PolicyDecisionReasonCodes
{
    public const string PolicyChecksPassed = "policy_checks_passed";
    public const string InvalidCompanyContext = "invalid_company_context";
    public const string ApprovalRequiredByPolicy = "approval_required_by_policy";
    public const string InvalidActionType = "invalid_action_type";
    public const string InvalidAutonomyLevel = "invalid_autonomy_level";
    public const string AgentStatusDisallowsExecution = "agent_status_disallows_execution";
    public const string MissingPolicyConfiguration = "missing_policy_configuration";
    public const string InvalidPolicyConfiguration = "invalid_policy_configuration";
    public const string AmbiguousPolicyConfiguration = "ambiguous_policy_configuration";
    public const string ToolNotConfigured = "tool_not_configured";
    public const string ToolExplicitlyDenied = "tool_explicitly_denied";
    public const string ToolNotPermitted = "tool_not_permitted";
    public const string TenantScopeViolation = "tenant_scope_violation";
    public const string DataScopeViolation = "data_scope_violation";
    public const string ScopeNotPermitted = "scope_not_permitted";
    public const string AutonomyLevelBlocksAction = "autonomy_level_blocks_action";
    public const string ScopeContextMissing = "scope_context_missing";
    public const string AutonomyLevelRequiresApproval = "autonomy_level_requires_approval";
    public const string ApprovalRouteMissing = "approval_route_missing";
    public const string ApprovalRequired = "approval_required";
    public const string ApprovalPending = "approval_pending";
    public const string ApprovalRejected = "approval_rejected";
    public const string ApprovalExpired = "approval_expired";
    public const string ApprovalCancelled = "approval_cancelled";
    public const string ThresholdContextMissing = "threshold_context_missing";
    public const string ThresholdConfigurationMissing = "threshold_configuration_missing";
    public const string ThresholdExceededRequiresApproval = "threshold_exceeded_requires_approval";
    public const string SensitiveActionRequiresApproval = "sensitive_action_requires_approval";
}