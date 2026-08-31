using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceToolRiskPolicyTests
{
    private readonly StaticCompanyToolRegistry _registry = new();
    private readonly PolicyGuardrailEngine _engine;

    public FinanceToolRiskPolicyTests() => _engine = new PolicyGuardrailEngine(_registry);

    [Fact]
    public void Every_registered_finance_execute_tool_has_one_explicit_versioned_risk_policy()
    {
        var financeExecuteTools = _registry.ListTools()
            .Where(tool => tool.Scopes.Contains("finance") && tool.SupportedActions.Contains(ToolActionType.Execute))
            .OrderBy(tool => tool.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(
            financeExecuteTools.Select(tool => tool.ToolName),
            FinanceToolRiskPolicyCatalog.All.Select(policy => policy.ToolName));
        foreach (var registration in financeExecuteTools)
        {
            var risk = Assert.IsType<FinanceToolRiskClassification>(registration.FinanceRiskClassification);
            Assert.Equal(FinanceToolRiskPolicyVersions.V1, risk.PolicyVersion);
            Assert.Equal(registration.ToolName, risk.ToolName);
            Assert.True(registration.SensitiveAction);
            Assert.False(string.IsNullOrWhiteSpace(risk.RiskTier));
            Assert.False(string.IsNullOrWhiteSpace(risk.Reversibility));
            Assert.False(string.IsNullOrWhiteSpace(risk.RequiredActorPermission));
            Assert.False(string.IsNullOrWhiteSpace(risk.DefaultApprovalBehavior));
            Assert.False(string.IsNullOrWhiteSpace(risk.ThresholdCategory));
            Assert.False(string.IsNullOrWhiteSpace(risk.ExternalSideEffectClassification));
        }
    }

    [Fact]
    public void High_consequence_finance_side_effect_classes_are_sensitive_by_default()
    {
        Assert.Contains(FinanceToolExternalSideEffects.ApprovalStateChange, FinanceToolExternalSideEffects.SensitiveByDefault);
        Assert.Contains(FinanceToolExternalSideEffects.AccountingPosting, FinanceToolExternalSideEffects.SensitiveByDefault);
        Assert.Contains(FinanceToolExternalSideEffects.PeriodCloseOrLock, FinanceToolExternalSideEffects.SensitiveByDefault);
        Assert.Contains(FinanceToolExternalSideEffects.ProviderWrite, FinanceToolExternalSideEffects.SensitiveByDefault);
        Assert.Contains(FinanceToolExternalSideEffects.PaymentAction, FinanceToolExternalSideEffects.SensitiveByDefault);
        Assert.Contains(FinanceToolExternalSideEffects.ComplianceSubmission, FinanceToolExternalSideEffects.SensitiveByDefault);
        Assert.Contains(FinanceToolExternalSideEffects.YearEnd, FinanceToolExternalSideEffects.SensitiveByDefault);
        Assert.Contains(FinanceToolExternalSideEffects.MigrationExecution, FinanceToolExternalSideEffects.SensitiveByDefault);
    }

    [Fact]
    public void Caller_false_cannot_override_authoritative_sensitive_classification()
    {
        var decision = _engine.Evaluate(Request(
            "approve_invoice",
            new JsonObject(),
            sensitiveAction: false,
            riskContext: new FinanceToolRiskEvaluationContext(null, 1, null, null, true, "invoice")));

        Assert.Equal(PolicyDecisionOutcomeValues.RequireApproval, decision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.SensitiveActionRequiresApproval, decision.ReasonCodes);
        Assert.Equal(FinanceToolRiskPolicyVersions.V1, decision.Metadata["riskPolicyVersion"]!.GetValue<string>());
        Assert.True(decision.Metadata["authoritativeSensitiveAction"]!.GetValue<bool>());
    }

    [Fact]
    public void Require_approval_for_execute_is_additive_to_bounded_exception()
    {
        var policy = CategorizationPolicy("category-policy-v1", 250m, 1);
        policy["requireApprovalForExecute"] = true;

        var decision = _engine.Evaluate(Request(
            "categorize_transaction",
            policy,
            riskContext: CategoryContext(250m, 1)));

        Assert.Equal(PolicyDecisionOutcomeValues.RequireApproval, decision.Outcome);
        Assert.Equal("configured_approval_rule", decision.Metadata["riskPolicyState"]!.GetValue<string>());
    }

    [Fact]
    public void Versioned_reversible_categorization_exception_allows_exact_boundaries()
    {
        var decision = _engine.Evaluate(Request(
            "categorize_transaction",
            CategorizationPolicy("category-policy-v7", 250m, 2),
            riskContext: CategoryContext(250m, 2)));

        Assert.Equal(PolicyDecisionOutcomeValues.Allow, decision.Outcome);
        Assert.Equal("category-policy-v7", decision.Metadata["financeApprovalPolicyVersion"]!.GetValue<string>());
        Assert.True(decision.Metadata["boundedFinanceExceptionApplied"]!.GetValue<bool>());
        Assert.Equal(2, decision.ThresholdEvaluations!.Count);
        Assert.All(decision.ThresholdEvaluations, evaluation => Assert.False(evaluation.ApprovalRequired));
    }

    [Theory]
    [InlineData(250.01, 2)]
    [InlineData(250, 3)]
    public void Categorization_outside_amount_or_batch_limit_requires_approval(double amount, int count)
    {
        var decision = _engine.Evaluate(Request(
            "categorize_transaction",
            CategorizationPolicy("category-policy-v2", 250m, 2),
            riskContext: CategoryContext((decimal)amount, count)));

        Assert.Equal(PolicyDecisionOutcomeValues.RequireApproval, decision.Outcome);
        Assert.Contains(decision.ThresholdEvaluations!, evaluation => evaluation.Exceeded);
    }

    [Fact]
    public void Missing_categorization_exception_defaults_to_review_and_missing_route_denies()
    {
        var request = Request(
            "categorize_transaction",
            new JsonObject(),
            riskContext: CategoryContext(10m, 1)) with
        {
            EscalationRules = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        };

        var decision = _engine.Evaluate(request);

        Assert.Equal(PolicyDecisionOutcomeValues.Deny, decision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.ApprovalRouteMissing, decision.ReasonCodes);
        Assert.Equal("missing_or_invalid_exception_configuration", decision.Metadata["riskPolicyState"]!.GetValue<string>());
    }

    [Fact]
    public void Categorization_policy_version_change_is_visible_in_decision_evidence()
    {
        var first = _engine.Evaluate(Request("categorize_transaction",
            CategorizationPolicy("category-policy-v1", 250m, 1), riskContext: CategoryContext(10m, 1)));
        var second = _engine.Evaluate(Request("categorize_transaction",
            CategorizationPolicy("category-policy-v2", 250m, 1), riskContext: CategoryContext(10m, 1)));

        Assert.Equal("category-policy-v1", first.Metadata["financeApprovalPolicyVersion"]!.GetValue<string>());
        Assert.Equal("category-policy-v2", second.Metadata["financeApprovalPolicyVersion"]!.GetValue<string>());
    }

    private PolicyEvaluationRequest Request(
        string toolName,
        JsonObject financePolicy,
        bool sensitiveAction = false,
        FinanceToolRiskEvaluationContext? riskContext = null)
    {
        var companyId = Guid.NewGuid();
        return new(
            companyId,
            Guid.NewGuid(),
            companyId,
            "active",
            "level_2",
            true,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["allowed"] = new JsonArray(toolName),
                ["actions"] = new JsonArray("execute"),
                ["denied"] = new JsonArray(),
                ["deniedActions"] = new JsonArray()
            },
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["execute"] = new JsonArray("finance")
            },
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["financePolicy"] = financePolicy
            },
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["escalateTo"] = "owner"
            },
            toolName,
            ToolActionType.Execute,
            "finance",
            new Dictionary<string, JsonNode?>(),
            null,
            null,
            null,
            sensitiveAction,
            Guid.NewGuid(),
            "risk-test",
            FinanceRiskContext: riskContext);
    }

    private static JsonObject CategorizationPolicy(string version, decimal maxAmount, int maxBatchCount) => new()
    {
        ["policyVersion"] = "company-finance-policy-v1",
        ["requireApprovalForExecute"] = false,
        ["categorizationException"] = new JsonObject
        {
            ["enabled"] = true,
            ["policyVersion"] = version,
            ["maxAmount"] = maxAmount,
            ["maxBatchCount"] = maxBatchCount,
            ["requiredCurrentState"] = "uncategorized",
            ["allowedCategories"] = new JsonArray("software", "office_supplies")
        }
    };

    private static FinanceToolRiskEvaluationContext CategoryContext(decimal amount, int count) =>
        new(amount, count, "uncategorized", "software", true, "finance_transactions");
}
