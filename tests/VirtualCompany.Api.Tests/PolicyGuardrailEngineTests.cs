using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class PolicyGuardrailEngineTests
{
    private readonly PolicyGuardrailEngine _engine = new();

    [Fact]
    public void Evaluate_allows_in_scope_level_2_execution_within_threshold()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            thresholdCategory: "approval",
            thresholdKey: "expenseUsd",
            thresholdValue: 750));

        Assert.Equal("allow", decision.Outcome);
        Assert.Contains("policy_checks_passed", decision.ReasonCodes);
        Assert.Equal("execute", decision.EvaluatedActionType);
        Assert.Equal(PolicyDecisionSchemaVersions.V1, decision.SchemaVersion);
        Assert.NotNull(decision.Tenant);
        Assert.NotNull(decision.Actor);
        Assert.NotNull(decision.Audit);
        Assert.Equal("default_deny", decision.Metadata["policyMode"]!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_denies_when_tool_is_not_permitted()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_3",
            toolName: "wire_transfer",
            actionType: "execute",
            scope: "payments"));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains("tool_not_permitted", decision.ReasonCodes);
    }

    [Fact]
    public void Evaluate_denies_when_action_exceeds_autonomy_level()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_0",
            toolName: "erp",
            actionType: "execute",
            scope: "payments"));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains("autonomy_level_blocks_action", decision.ReasonCodes);
    }

    [Fact]
    public void Evaluate_denies_when_agent_status_is_not_execution_eligible()
    {
        var request = CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments") with
        {
            AgentStatus = "paused"
        };

        var decision = _engine.Evaluate(request);

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains("agent_status_disallows_execution", decision.ReasonCodes);
        Assert.Equal("paused", decision.Metadata["agentStatus"]!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_denies_when_action_type_is_unknown()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "destroy",
            scope: "payments"));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.InvalidActionType, decision.ReasonCodes);
    }

    [Fact]
    public void Evaluate_denies_when_action_type_is_missing()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: null,
            scope: "payments"));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.InvalidActionType, decision.ReasonCodes);
        Assert.Equal(string.Empty, decision.EvaluatedActionType);
    }

    [Fact]
    public void Evaluate_denies_when_configured_action_scope_does_not_allow_requested_action()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            toolPermissions: Payload(
                ("allowed", new JsonArray(JsonValue.Create("erp"))),
                ("actions", new JsonArray(JsonValue.Create("read"), JsonValue.Create("recommend"))))));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.ToolActionNotPermitted, decision.ReasonCodes);
        Assert.Equal("configured", decision.Metadata["actionPolicyState"]!.GetValue<string>());
        Assert.False(decision.Metadata["actionAllowed"]!.GetValue<bool>());
    }

    [Fact]
    public void Evaluate_denies_when_configured_action_scope_is_ambiguous()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            toolPermissions: Payload(
                ("allowed", new JsonArray(JsonValue.Create("erp"))),
                ("actions", new JsonArray(JsonValue.Create("execute"))),
                ("deniedActions", new JsonArray(JsonValue.Create("execute"))))));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.AmbiguousPolicyConfiguration, decision.ReasonCodes);
        Assert.Equal("ambiguous", decision.Metadata["actionPolicyState"]!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_denies_when_autonomy_level_is_missing_or_invalid()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: string.Empty,
            toolName: "erp",
            actionType: "execute",
            scope: "payments"));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.InvalidAutonomyLevel, decision.ReasonCodes);
    }

    [Fact]
    public void Evaluate_allows_level_0_recommendation_when_tool_and_scope_are_permitted()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "0",
            toolName: "erp",
            actionType: "recommend",
            scope: "finance"));

        Assert.Equal("allow", decision.Outcome);
        Assert.Equal("recommend", decision.EvaluatedActionType);
        Assert.Equal("finance", decision.EvaluatedScope);
        Assert.Contains("policy_checks_passed", decision.ReasonCodes);
    }

    [Fact]
    public void Evaluate_default_denies_when_tool_policy_configuration_is_missing()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolPermissions: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            toolName: "erp",
            actionType: "execute",
            scope: "payments"));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains("missing_policy_configuration", decision.ReasonCodes);
        Assert.NotNull(decision.Reasons);
        Assert.Contains(decision.Reasons!, x => x.Code == PolicyDecisionReasonCodes.MissingPolicyConfiguration && x.Category == "policy_configuration");
    }

    [Fact]
    public void Evaluate_default_denies_when_policy_configuration_section_is_null()
    {
        var request = CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments") with
        {
            ToolPermissions = null!
        };

        var decision = _engine.Evaluate(request);

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.MissingPolicyConfiguration, decision.ReasonCodes);
        Assert.Equal("missing", decision.Metadata["policyConfigurationState"]!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_denies_when_tool_identity_is_missing()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: " ",
            actionType: "execute",
            scope: "payments"));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.ToolNotConfigured, decision.ReasonCodes);
    }

    [Fact]
    public void Evaluate_requires_approval_when_sensitive_execute_exceeds_threshold()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            thresholdCategory: "approval",
            thresholdKey: "expenseUsd",
            thresholdValue: 1500,
            sensitiveAction: true));

        Assert.Equal("require_approval", decision.Outcome);
        Assert.Contains("sensitive_action_requires_approval", decision.ReasonCodes);
        Assert.Contains("threshold_exceeded_requires_approval", decision.ReasonCodes);
        Assert.True(decision.Metadata["blockedPendingApproval"]!.GetValue<bool>());
        Assert.Equal("awaiting_approval", decision.Metadata["executionState"]!.GetValue<string>());
        Assert.True(decision.Metadata["thresholdEvaluation"]!["sensitiveAction"]!.GetValue<bool>());
        Assert.NotNull(decision.ApprovalRequirement);
        Assert.Equal("threshold", decision.ApprovalRequirement!.RequirementType);
        Assert.NotNull(decision.ThresholdEvaluations);
        Assert.Single(decision.ThresholdEvaluations!);
        Assert.Equal("expenseUsd", decision.ThresholdEvaluations![0].Key);
    }

    [Fact]
    public void Evaluate_denies_when_company_scope_does_not_match()
    {
        var companyId = Guid.NewGuid();
        var decision = _engine.Evaluate(new PolicyEvaluationRequest(
            companyId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "active",
            "level_2",
            true,
            Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            Payload(("read", new JsonArray(JsonValue.Create("finance"))), ("execute", new JsonArray(JsonValue.Create("payments")))),
            Payload(("approval", new JsonObject { ["expenseUsd"] = 1000 })),
            Payload(("escalateTo", JsonValue.Create("founder"))),
            "erp",
            ToolActionType.Execute,
            "payments",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            null,
            null,
            null,
            false,
            Guid.NewGuid(),
            "corr-tenant"));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains("tenant_scope_violation", decision.ReasonCodes);
        Assert.False(decision.Tenant!.CompanyScopeMatched);
    }

    [Fact]
    public void Evaluate_allows_sensitive_execute_when_value_is_within_threshold()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            thresholdCategory: "approval",
            thresholdKey: "expenseUsd",
            thresholdValue: 750,
            sensitiveAction: true));

        Assert.Equal("allow", decision.Outcome);
        Assert.Contains("policy_checks_passed", decision.ReasonCodes);
        Assert.False(decision.Metadata["executionBlocked"]!.GetValue<bool>());
        Assert.False(decision.Metadata["thresholdEvaluation"]!["exceeded"]!.GetValue<bool>());
        Assert.True(decision.Metadata["thresholdEvaluation"]!["sensitiveAction"]!.GetValue<bool>());
    }

    [Fact]
    public void Evaluate_requires_approval_when_threshold_is_exceeded()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            thresholdCategory: "approval",
            thresholdKey: "expenseUsd",
            thresholdValue: 1500));

        Assert.Equal("require_approval", decision.Outcome);
        Assert.Contains("threshold_exceeded_requires_approval", decision.ReasonCodes);
    }

    [Fact]
    public void Evaluate_requires_approval_for_level_1_execution_even_when_other_policy_checks_pass()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_1",
            toolName: "erp",
            actionType: "execute",
            scope: "payments"));

        Assert.Equal("require_approval", decision.Outcome);
        Assert.Contains("autonomy_level_requires_approval", decision.ReasonCodes);
    }

    [Fact]
    public void Evaluate_denies_sensitive_execute_when_threshold_context_is_missing()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            sensitiveAction: true));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains("threshold_context_missing", decision.ReasonCodes);
        Assert.True(decision.Metadata["executionBlocked"]!.GetValue<bool>());
        Assert.Equal("missing_request_context", decision.Metadata["thresholdEvaluationState"]!.GetValue<string>());
        Assert.False(decision.ApprovalRequired);
    }

    [Fact]
    public void Evaluate_denies_when_execute_scope_is_missing()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: null));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains("scope_context_missing", decision.ReasonCodes);
    }

    [Fact]
    public void Evaluate_denies_when_scope_violates_data_boundary()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "hr"));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains("data_scope_violation", decision.ReasonCodes);
        Assert.NotNull(decision.Reasons);
        Assert.Contains(decision.Reasons!, x => x.Code == PolicyDecisionReasonCodes.DataScopeViolation && x.Category == "scope");
    }

    [Fact]
    public void Evaluate_denies_when_tool_permissions_are_ambiguous()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            toolPermissions: Payload(
                ("allowed", new JsonArray(JsonValue.Create("erp"))),
                ("denied", new JsonArray(JsonValue.Create("erp"))))));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains("ambiguous_policy_configuration", decision.ReasonCodes);
    }

    [Fact]
    public void Evaluate_supports_explicit_execute_scope_bucket()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            dataScopes: Payload(
                ("read", new JsonArray(JsonValue.Create("finance"))),
                ("execute", new JsonArray(JsonValue.Create("payments"))))));

        Assert.Equal("allow", decision.Outcome);
        Assert.Equal("execute", decision.Metadata["scopePolicyBucket"]!.GetValue<string>());
        Assert.False(decision.Metadata["scopePolicyFallbackApplied"]!.GetValue<bool>());
    }

    [Fact]
    public void Evaluate_denies_when_execute_scope_bucket_is_missing_even_if_legacy_write_bucket_exists()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            dataScopes: Payload(
                ("read", new JsonArray(JsonValue.Create("finance"))),
                ("write", new JsonArray(JsonValue.Create("payments"))))));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.MissingPolicyConfiguration, decision.ReasonCodes);
        Assert.Equal("missing", decision.Metadata["scopeConfigState"]!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_requires_approval_when_explicit_policy_rule_matches_request()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_3",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            escalationRules: Payload(
                ("escalateTo", JsonValue.Create("founder")),
                ("requireApproval", new JsonObject
                {
                    ["actions"] = new JsonArray(JsonValue.Create("execute")),
                    ["tools"] = new JsonArray(JsonValue.Create("erp"))
                }))));

        Assert.Equal("require_approval", decision.Outcome);
        Assert.Contains("approval_required_by_policy", decision.ReasonCodes);
    }

    [Fact]
    public void Evaluate_denies_when_approval_policy_is_ambiguous()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_3",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            escalationRules: Payload(
                ("escalateTo", JsonValue.Create("founder")),
                ("requireApproval", new JsonObject
                {
                    ["actions"] = new JsonArray()
                }))));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.AmbiguousPolicyConfiguration, decision.ReasonCodes);
        Assert.Equal("ambiguous", decision.Metadata["approvalRequirementPolicyState"]!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_denies_when_threshold_configuration_is_invalid()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_2",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            thresholdCategory: "approval",
            thresholdKey: "expenseUsd",
            thresholdValue: 250,
            thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = -1 }))));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.InvalidPolicyConfiguration, decision.ReasonCodes);
        Assert.Equal("invalid", decision.Metadata["thresholdConfigurationState"]!.GetValue<string>());
    }

    [Fact]
    public void Evaluate_default_denies_when_approval_route_is_missing_for_required_approval()
    {
        var decision = _engine.Evaluate(CreateRequest(
            autonomyLevel: "level_1",
            toolName: "erp",
            actionType: "execute",
            scope: "payments",
            escalationRules: Payload()));

        Assert.Equal("deny", decision.Outcome);
        Assert.Contains("approval_route_missing", decision.ReasonCodes);
        Assert.False(decision.ApprovalRequired);
        Assert.True(decision.Metadata["executionBlocked"]!.GetValue<bool>());
        Assert.False(decision.Metadata["blockedPendingApproval"]!.GetValue<bool>());
    }

    private static PolicyEvaluationRequest CreateRequest(
        string autonomyLevel,
        string toolName,
        string? actionType,
        string? scope,
        string? thresholdCategory = null,
        string? thresholdKey = null,
        decimal? thresholdValue = null,
        Dictionary<string, JsonNode?>? toolPermissions = null,
        Dictionary<string, JsonNode?>? dataScopes = null,
        Dictionary<string, JsonNode?>? thresholds = null,
        Dictionary<string, JsonNode?>? escalationRules = null,
        bool sensitiveAction = false)
    {
        var parsedActionType = ToolActionTypeValues.TryParse(actionType, out var value) ? value : (ToolActionType?)null;
        var companyId = Guid.NewGuid();
        return new PolicyEvaluationRequest(
            companyId,
            Guid.NewGuid(),
            companyId,
            "active",
            autonomyLevel,
            true,
            toolPermissions ?? Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
            dataScopes ?? Payload(
                ("read", new JsonArray(JsonValue.Create("finance"))),
                ("recommend", new JsonArray(JsonValue.Create("finance"))),
                ("execute", new JsonArray(JsonValue.Create("payments")))),
            thresholds ?? Payload(("approval", new JsonObject { ["expenseUsd"] = 1000 })),
            escalationRules ?? Payload(("escalateTo", JsonValue.Create("founder"))),
            toolName,
            parsedActionType,
            scope,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            thresholdCategory,
            thresholdKey,
            thresholdValue,
            sensitiveAction,
            Guid.NewGuid(),
            "corr-policy-001");
    }

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] properties)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in properties)
        {
            payload[key] = value?.DeepClone();
        }

        return payload;
    }
}