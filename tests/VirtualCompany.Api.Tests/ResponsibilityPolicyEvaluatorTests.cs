using System.Text.Json.Nodes;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ResponsibilityPolicyEvaluatorTests
{
    private readonly ResponsibilityPolicyEvaluator _evaluator = new();

    [Fact]
    public void Allowed_domain_returns_in_scope_decision()
    {
        var decision = _evaluator.Evaluate(Request("finance.payments"));

        Assert.True(decision.IsInScope);
        Assert.False(decision.RequiresDelegation);
        Assert.Equal("finance.payments", decision.RequestedDomain);
        Assert.StartsWith(ResponsibilityPolicyRuleKinds.ExplicitAllow, decision.MatchedRule, StringComparison.Ordinal);
    }

    [Fact]
    public void Denied_domain_takes_precedence_over_allowed_domain()
    {
        var decision = _evaluator.Evaluate(Request("finance.payments.high_risk"));

        Assert.False(decision.IsInScope);
        Assert.Equal(ResponsibilityPolicyDecisionTypes.Escalation, decision.DecisionType);
        Assert.Equal("Chief Financial Officer", decision.DelegationTarget);
        Assert.StartsWith(ResponsibilityPolicyRuleKinds.ExplicitDeny, decision.MatchedRule, StringComparison.Ordinal);
    }

    [Fact]
    public void Unmatched_domain_defaults_to_out_of_scope_with_default_delegation_target()
    {
        var decision = _evaluator.Evaluate(Request("legal.contracts"));

        Assert.False(decision.IsInScope);
        Assert.Equal(ResponsibilityPolicyRuleKinds.DefaultDeny, decision.MatchedRule);
        Assert.Equal(ResponsibilityPolicyDecisionTypes.Delegation, decision.DecisionType);
        Assert.Equal("Legal Lead", decision.DelegationTarget);
    }

    [Fact]
    public void Unconfigured_policy_defaults_to_out_of_scope_escalation()
    {
        var decision = _evaluator.Evaluate(new ResponsibilityPolicyEvaluationRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Finance Manager",
            "finance.payments",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            "corr-policy"));

        Assert.False(decision.IsInScope);
        Assert.True(decision.RequiresDelegation);
        Assert.Equal(ResponsibilityPolicyRuleKinds.NotConfigured, decision.MatchedRule);
        Assert.Equal(ResponsibilityPolicyDecisionTypes.Escalation, decision.DecisionType);
        Assert.Equal("manager", decision.DelegationTarget);
    }

    private static ResponsibilityPolicyEvaluationRequest Request(string requestedDomain) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Finance Manager",
            requestedDomain,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["responsibilityPolicy"] = new JsonObject
                {
                    ["allowedDomains"] = new JsonArray(
                        JsonValue.Create("finance.*"),
                        JsonValue.Create("finance.payments.high_risk")),
                    ["deniedDomains"] = new JsonArray(
                        JsonValue.Create("finance.payments.high_risk")),
                    ["delegationTargets"] = new JsonObject
                    {
                        ["finance.payments.high_risk"] = new JsonObject
                        {
                            ["target"] = JsonValue.Create("Chief Financial Officer"),
                            ["actionType"] = JsonValue.Create("escalation")
                        },
                        ["default"] = JsonValue.Create("Legal Lead")
                    }
                }
            },
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["escalateTo"] = JsonValue.Create("Founder")
            },
            "corr-policy");
}
