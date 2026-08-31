using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Agents;

public static class FinanceAgentActorTypes
{
    public const string Human = "human";
    public const string DelegatedBackground = "delegated_background";
    public const string Missing = "missing";
}

public static class FinanceAgentMembershipStates
{
    public const string Active = "active";
    public const string NotApplicable = "not_applicable";
    public const string Missing = "missing";
    public const string Inactive = "inactive";
}

public static class FinanceAgentAuthorizationOutcomes
{
    public const string Allowed = "allowed";
    public const string Denied = "denied";
}

public static class FinanceAgentAuthorizationReasonCodes
{
    public const string Authorized = "finance_actor_authorized";
    public const string ActorMissing = "finance_actor_missing";
    public const string MembershipMissing = "finance_membership_missing";
    public const string MembershipInactive = "finance_membership_inactive";
    public const string PermissionMissing = "finance_permission_missing";
    public const string DelegationMissing = "finance_delegation_missing";
    public const string DelegationExpired = "finance_delegation_expired";
    public const string DelegationRevoked = "finance_delegation_revoked";
    public const string DelegationCompanyMismatch = "finance_delegation_company_mismatch";
    public const string DelegationAgentMismatch = "finance_delegation_agent_mismatch";
    public const string DelegationWorkflowMismatch = "finance_delegation_workflow_mismatch";
    public const string DelegationCapabilityMismatch = "finance_delegation_capability_mismatch";
    public const string DelegationActionMismatch = "finance_delegation_action_mismatch";
    public const string DelegationScopeMismatch = "finance_delegation_scope_mismatch";
}

public sealed record FinanceAgentAuthorizationEvidenceDto(
    string Type,
    string Reference,
    string Result);

public sealed record FinanceAgentAuthorizationDecisionDto(
    Guid CompanyId,
    Guid AgentId,
    Guid ExecutionId,
    string ActorType,
    Guid? ActorId,
    string MembershipState,
    string ToolName,
    string ActionType,
    string? Scope,
    IReadOnlyList<string> RequiredCompanyPolicies,
    IReadOnlyList<string> RequiredFinancePermissions,
    string Outcome,
    string ReasonCode,
    string Explanation,
    IReadOnlyList<FinanceAgentAuthorizationEvidenceDto> Evidence,
    DateTime EvaluatedAtUtc,
    string PolicyVersion,
    Guid? DelegationAuthorityId = null,
    Guid? OriginatingWorkflowInstanceId = null)
{
    public bool IsAllowed => string.Equals(Outcome, FinanceAgentAuthorizationOutcomes.Allowed, StringComparison.Ordinal);
}

public sealed record FinanceAgentAuthorizationRequest(
    Guid CompanyId,
    Guid AgentId,
    Guid ExecutionId,
    string ToolName,
    ToolActionType ActionType,
    string? Scope,
    Guid? WorkflowInstanceId,
    string? CorrelationId,
    Guid? ActorUserId = null,
    Guid? DelegationAuthorityId = null,
    bool IsApprovedContinuation = false);

public interface IFinanceAgentAuthorizationService
{
    Task<FinanceAgentAuthorizationDecisionDto> AuthorizeAsync(
        FinanceAgentAuthorizationRequest request,
        CancellationToken cancellationToken);
}

public sealed record FinanceAgentAuthorityMatrixEntry(
    string ToolName,
    string ToolVersion,
    string ActionType,
    string Scope,
    IReadOnlyList<string> RequiredCompanyPolicies,
    IReadOnlyList<string> RequiredActorPermissions,
    string AgentGrant,
    string RiskTier,
    string ApprovalBehavior,
    string ExternalSideEffect,
    string OwningRegressionTest);
