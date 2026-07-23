using System.Text.Json.Nodes;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class ResponsibilityPolicyEvaluator : IResponsibilityPolicyEvaluator
{
    private const string DefaultDelegationTarget = "manager";

    public ResponsibilityPolicyDecision Evaluate(ResponsibilityPolicyEvaluationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestedDomain = ResponsibilityDomainRules.NormalizeRequestedDomain(request.RequestedDomain, "unknown");
        var configured = TryReadPolicy(request.DataScopes, out var policy);
        if (!configured)
        {
            var defaultDelegationTarget = ResolveDelegationTarget(AgentResponsibilityPolicy.Empty, requestedDomain, request.EscalationRules);
            return new ResponsibilityPolicyDecision(
                false,
                requestedDomain,
                ResponsibilityPolicyRuleKinds.NotConfigured,
                defaultDelegationTarget.ActionType,
                defaultDelegationTarget.Target,
                "No role responsibility policy is configured for this agent, so default-deny applies.");
        }

        // Deterministic precedence: explicit deny, explicit allow, then default-deny.
        var denyMatch = ResponsibilityDomainRules.FirstMatch(policy.DeniedDomains, requestedDomain);
        if (denyMatch is not null)
        {
            var delegationTarget = ResolveDelegationTarget(policy, requestedDomain, request.EscalationRules);
            return new ResponsibilityPolicyDecision(
                false,
                requestedDomain,
                $"{ResponsibilityPolicyRuleKinds.ExplicitDeny}:{denyMatch.DomainPattern}",
                delegationTarget.ActionType,
                delegationTarget.Target,
                "The requested domain is explicitly denied for this agent role.");
        }

        var allowMatch = ResponsibilityDomainRules.FirstMatch(policy.AllowedDomains, requestedDomain);
        if (allowMatch is not null)
        {
            return new ResponsibilityPolicyDecision(
                true,
                requestedDomain,
                $"{ResponsibilityPolicyRuleKinds.ExplicitAllow}:{allowMatch.DomainPattern}",
                ResponsibilityPolicyDecisionTypes.InScope,
                string.Empty,
                "The requested domain is allowed for this agent role.");
        }

        var fallbackTarget = ResolveDelegationTarget(policy, requestedDomain, request.EscalationRules);
        return new ResponsibilityPolicyDecision(
            false,
            requestedDomain,
            ResponsibilityPolicyRuleKinds.DefaultDeny,
            fallbackTarget.ActionType,
            fallbackTarget.Target,
            "The requested domain did not match an allowed responsibility domain.");
    }

    private static bool TryReadPolicy(
        IReadOnlyDictionary<string, JsonNode?> dataScopes,
        out AgentResponsibilityPolicy policy)
    {
        policy = AgentResponsibilityPolicy.Empty;
        if (!dataScopes.TryGetValue(ResponsibilityPolicyConfigurationKeys.Root, out var root) || root is not JsonObject policyObject)
        {
            return false;
        }

        var allowed = ResponsibilityDomainRules.Normalize(
            ReadStringArray(policyObject[ResponsibilityPolicyConfigurationKeys.AllowedDomains]),
            "allow");
        var denied = ResponsibilityDomainRules.Normalize(
            ReadStringArray(policyObject[ResponsibilityPolicyConfigurationKeys.DeniedDomains]),
            "deny");
        var delegationTargets = ReadDelegationTargets(policyObject[ResponsibilityPolicyConfigurationKeys.DelegationTargets]);

        policy = new AgentResponsibilityPolicy(allowed, denied, delegationTargets);
        return true;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonNode? node)
    {
        if (node is not JsonArray array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in array)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                values.Add(text.Trim());
            }
        }

        return values;
    }

    private static IReadOnlyDictionary<string, DelegationTarget> ReadDelegationTargets(JsonNode? node)
    {
        var targets = new Dictionary<string, DelegationTarget>(StringComparer.OrdinalIgnoreCase);
        if (node is not JsonObject targetObject)
        {
            return targets;
        }

        foreach (var (domain, value) in targetObject)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                continue;
            }

            if (value is JsonValue scalar && scalar.TryGetValue<string>(out var target) && !string.IsNullOrWhiteSpace(target))
            {
                targets[domain.Trim()] = new DelegationTarget(target.Trim());
                continue;
            }

            if (value is not JsonObject objectValue)
            {
                continue;
            }

            var objectTarget = ReadString(objectValue["target"]);
            if (string.IsNullOrWhiteSpace(objectTarget))
            {
                continue;
            }

            var actionType = ReadString(objectValue["actionType"]);
            targets[domain.Trim()] = new DelegationTarget(
                objectTarget,
                string.Equals(actionType, "escalate", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(actionType, ResponsibilityPolicyDecisionTypes.Escalation, StringComparison.OrdinalIgnoreCase)
                    ? ResponsibilityPolicyDecisionTypes.Escalation
                    : ResponsibilityPolicyDecisionTypes.Delegation);
        }

        return targets;
    }

    private static DelegationTarget ResolveDelegationTarget(
        AgentResponsibilityPolicy policy,
        string requestedDomain,
        IReadOnlyDictionary<string, JsonNode?> escalationRules)
    {
        if (policy.DelegationTargets.TryGetValue(requestedDomain, out var exactTarget))
        {
            return exactTarget;
        }

        if (policy.DelegationTargets.TryGetValue("default", out var defaultTarget))
        {
            return defaultTarget;
        }

        var escalationTarget = ReadString(escalationRules.TryGetValue("escalateTo", out var target) ? target : null);
        return string.IsNullOrWhiteSpace(escalationTarget)
            ? DelegationTarget.EscalateTo(DefaultDelegationTarget)
            : DelegationTarget.EscalateTo(escalationTarget);
    }

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
}
