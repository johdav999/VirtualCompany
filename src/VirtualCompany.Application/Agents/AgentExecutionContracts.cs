using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Agents;

public sealed record ExecuteAgentToolCommand(
    string ToolName,
    string ActionType,
    string? Scope,
    Dictionary<string, JsonNode?>? RequestPayload,
    string? ThresholdCategory,
    string? ThresholdKey,
    decimal? ThresholdValue,
    bool SensitiveAction = false,
    Guid? TaskId = null,
    Guid? WorkflowInstanceId = null,
    string? CorrelationId = null);

public sealed record ExecuteAgentToolResultDto(
    Guid ExecutionId,
    string Status,
    Guid? ApprovalRequestId,
    ToolExecutionDecisionDto PolicyDecision,
    Dictionary<string, JsonNode?>? ExecutionResult,
    string Message,
    Dictionary<string, JsonNode?>? ApprovalDecisionChain = null,
    ToolExecutionDenialDto? Denial = null);

public sealed record ToolExecutionDenialDto(
    string Code,
    string UserFacingMessage,
    IReadOnlyList<string> ReasonCodes,
    string RationaleSummary,
    Dictionary<string, JsonNode?> Metadata,
    string SchemaVersion = PolicyDecisionSchemaVersions.V1)
{
    public static ToolExecutionDenialDto FromDecision(ToolExecutionDecisionDto decision, string userFacingMessage)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var primaryReasonCode = decision.ReasonCodes.FirstOrDefault(static code => !string.IsNullOrWhiteSpace(code));
        var rationaleSummary = decision.Reasons?.FirstOrDefault(reason =>
            string.Equals(reason.Code, primaryReasonCode, StringComparison.OrdinalIgnoreCase))?.Summary
            ?? "Action blocked by policy before execution.";

        var metadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["policyOutcome"] = JsonValue.Create(decision.Outcome),
            ["policyDecisionSchemaVersion"] = JsonValue.Create(decision.SchemaVersion),
            ["primaryReasonCode"] = string.IsNullOrWhiteSpace(primaryReasonCode) ? null : JsonValue.Create(primaryReasonCode),
            ["policyEvaluationVersion"] = string.IsNullOrWhiteSpace(decision.Audit?.PolicyVersion) ? null : JsonValue.Create(decision.Audit.PolicyVersion),
            ["policyCorrelationId"] = string.IsNullOrWhiteSpace(decision.Audit?.CorrelationId) ? null : JsonValue.Create(decision.Audit.CorrelationId),
            ["executionId"] = decision.Audit is null ? null : JsonValue.Create(decision.Audit.ExecutionId),
            ["toolName"] = string.IsNullOrWhiteSpace(decision.Tool?.ToolName) ? null : JsonValue.Create(decision.Tool.ToolName),
            ["actionType"] = string.IsNullOrWhiteSpace(decision.Tool?.ActionType) ? null : JsonValue.Create(decision.Tool.ActionType),
            ["scope"] = string.IsNullOrWhiteSpace(decision.Tool?.Scope) ? null : JsonValue.Create(decision.Tool.Scope),
            ["companyId"] = decision.Tenant is null ? null : JsonValue.Create(decision.Tenant.CompanyId),
            ["agentId"] = decision.Actor is null ? null : JsonValue.Create(decision.Actor.AgentId)
        };

        return new ToolExecutionDenialDto(
            "policy_denied",
            string.IsNullOrWhiteSpace(userFacingMessage)
                ? "This action was blocked by policy."
                : userFacingMessage.Trim(),
            decision.ReasonCodes.ToArray(),
            rationaleSummary,
            metadata,
            decision.SchemaVersion);
    }
}

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
    ToolActionType? ActionType,
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
    ToolActionType ActionType,
    string? Scope,
    IReadOnlyDictionary<string, JsonNode?> RequestPayload,
    Guid? TaskId = null,
    Guid? WorkflowInstanceId = null,
    string? CorrelationId = null,
    Guid ExecutionId = default,
    string? ToolVersion = null);

public sealed record ToolExecutionResult(
    bool Success,
    string Status,
    string ToolName,
    ToolActionType ActionType,
    Dictionary<string, JsonNode?> Payload,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    Dictionary<string, JsonNode?>? Metadata = null)
{
    public const string SchemaVersion = "2026-04-13";
    public string ActionTypeValue => ActionType.ToStorageValue();

    public string Summary =>
        string.IsNullOrWhiteSpace(ErrorMessage)
            ? $"{ToolName} {ActionTypeValue} completed with status '{Status}'."
            : ErrorMessage!;

    public Dictionary<string, JsonNode?> ToStructuredPayload()
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create(SchemaVersion),
            ["success"] = JsonValue.Create(Success),
            ["status"] = JsonValue.Create(Status),
            ["toolName"] = JsonValue.Create(ToolName),
            ["actionType"] = JsonValue.Create(ActionTypeValue),
            ["payload"] = ToJsonObject(Payload),
            ["errorCode"] = string.IsNullOrWhiteSpace(ErrorCode) ? null : JsonValue.Create(ErrorCode),
            ["errorMessage"] = string.IsNullOrWhiteSpace(ErrorMessage) ? null : JsonValue.Create(ErrorMessage),
            ["metadata"] = ToJsonObject(Metadata)
        };

        foreach (var (key, value) in Payload)
        {
            payload.TryAdd(key, value?.DeepClone());
        }

        return payload;
    }

    public static ToolExecutionResult Succeeded(
        string toolName,
        ToolActionType actionType,
        Dictionary<string, JsonNode?> payload,
        Dictionary<string, JsonNode?>? metadata = null) =>
        new(true, "executed", toolName, actionType, CloneNodes(payload), Metadata: CloneNodes(metadata));

    public static ToolExecutionResult Failed(
        string toolName,
        ToolActionType actionType,
        string status,
        string errorCode,
        string errorMessage,
        Dictionary<string, JsonNode?>? payload = null,
        Dictionary<string, JsonNode?>? metadata = null) =>
        new(false, status, toolName, actionType, CloneNodes(payload), errorCode, errorMessage, CloneNodes(metadata));

    private static JsonObject ToJsonObject(IReadOnlyDictionary<string, JsonNode?>? nodes)
    {
        var jsonObject = new JsonObject();
        foreach (var (key, value) in CloneNodes(nodes))
        {
            jsonObject[key] = value?.DeepClone();
        }

        return jsonObject;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

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
    public const string ToolActionTypeMismatch = "tool_action_type_mismatch";
    public const string ToolActionNotPermitted = "tool_action_not_permitted";
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