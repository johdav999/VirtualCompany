using System.Text.Json.Nodes;
using VirtualCompany.Application.Tasks;

namespace VirtualCompany.Application.Orchestration;

public sealed record RequestedDomainClassification(
    string RequestedDomain,
    string MatchedClassifierRule,
    string Reason);

public interface IRequestedDomainClassifier
{
    RequestedDomainClassification Classify(OrchestrationRequest request, TaskDetailDto task);
}

public sealed record ResponsibilityPolicyEvaluationRequest(
    Guid CompanyId,
    Guid AgentId,
    string AgentRoleName,
    string RequestedDomain,
    IReadOnlyDictionary<string, JsonNode?> DataScopes,
    IReadOnlyDictionary<string, JsonNode?> EscalationRules,
    string CorrelationId);

public sealed record ResponsibilityPolicyDecision(
    bool IsInScope,
    string RequestedDomain,
    string MatchedRule,
    string DecisionType,
    string DelegationTarget,
    string Reason)
{
    public bool RequiresDelegation => !IsInScope;
}

public interface IResponsibilityPolicyEvaluator
{
    ResponsibilityPolicyDecision Evaluate(ResponsibilityPolicyEvaluationRequest request);
}

public static class ResponsibilityPolicyDecisionTypes
{
    public const string InScope = "in_scope";
    public const string Delegation = "delegation";
    public const string Escalation = "escalation";
}

public static class ResponsibilityPolicyRuleKinds
{
    public const string NotConfigured = "responsibility_policy:not_configured";
    public const string ExplicitDeny = "responsibility_policy:deny";
    public const string ExplicitAllow = "responsibility_policy:allow";
    public const string DefaultDeny = "responsibility_policy:default_deny";
}

public static class ResponsibilityPolicyConfigurationKeys
{
    public const string Root = "responsibilityPolicy";
    public const string AllowedDomains = "allowedDomains";
    public const string DeniedDomains = "deniedDomains";
    public const string DelegationTargets = "delegationTargets";
}

public static class RequestedDomainClassifierRules
{
    public const string ActorMetadataRequestedDomain = "requested_domain:actor_metadata:requestedDomain";
    public const string ActorMetadataDomain = "requested_domain:actor_metadata:domain";
    public const string TaskInputRequestedDomain = "requested_domain:task_input:requestedDomain";
    public const string TaskInputDomain = "requested_domain:task_input:domain";
    public const string TaskTypeFallback = "requested_domain:task_type_fallback";
}
