using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ToolExecutionPolicyDecisionJsonSerializerTests
{
    [Fact]
    public void Serialize_and_deserialize_round_trips_structured_policy_decision()
    {
        var decision = new ToolExecutionDecisionDto(
            PolicyDecisionOutcomeValues.RequireApproval,
            [PolicyDecisionReasonCodes.ThresholdExceededRequiresApproval, PolicyDecisionReasonCodes.ApprovalRequired],
            "Approval is required because the threshold was exceeded.",
            "level_2",
            "execute",
            "payments",
            true,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["executionState"] = JsonValue.Create("awaiting_approval")
            },
            PolicyDecisionSchemaVersions.V1,
            [
                new PolicyDecisionReasonDto(PolicyDecisionReasonCodes.ThresholdExceededRequiresApproval, "threshold", "The request exceeded a configured threshold."),
                new PolicyDecisionReasonDto(PolicyDecisionReasonCodes.ApprovalRequired, "approval", "A human approval step is required.")
            ],
            new PolicyDecisionTenantContextDto(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("11111111-1111-1111-1111-111111111111"), true),
            new PolicyDecisionActorContextDto(Guid.Parse("22222222-2222-2222-2222-222222222222"), "active", true),
            new PolicyDecisionToolContextDto("erp", "execute", "payments"),
            [
                new PolicyDecisionThresholdEvaluationDto("approval", "expenseUsd", 1500m, 1000m, true, true, false, "evaluated")
            ],
            new PolicyDecisionApprovalRequirementDto(
                "threshold",
                "founder",
                "approval_thresholds",
                "approval",
                "expenseUsd",
                1500m,
                1000m,
                ["execute"],
                ["erp"],
                ["payments"]),
            new PolicyDecisionAuditContextDto(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "corr-serializer-001",
                new DateTime(2026, 3, 31, 12, 0, 0, DateTimeKind.Utc),
                "default_deny",
                PolicyDecisionEvaluationVersions.Current));

        var payload = ToolExecutionPolicyDecisionJsonSerializer.Serialize(decision);

        Assert.Equal(PolicyDecisionSchemaVersions.V1, payload["schemaVersion"]!.GetValue<string>());
        Assert.Equal("threshold", payload["approvalRequirement"]!["requirementType"]!.GetValue<string>());
        Assert.Equal("threshold_exceeded_requires_approval", payload["reasons"]![0]!["code"]!.GetValue<string>());

        var roundTrip = ToolExecutionPolicyDecisionJsonSerializer.Deserialize(payload);

        Assert.Equal(decision.SchemaVersion, roundTrip.SchemaVersion);
        Assert.Equal(decision.Tenant!.CompanyId, roundTrip.Tenant!.CompanyId);
        Assert.Single(roundTrip.ThresholdEvaluations!);
        Assert.Equal("threshold", roundTrip.ApprovalRequirement!.RequirementType);
        Assert.Equal("corr-serializer-001", roundTrip.Audit!.CorrelationId);
    }
}
