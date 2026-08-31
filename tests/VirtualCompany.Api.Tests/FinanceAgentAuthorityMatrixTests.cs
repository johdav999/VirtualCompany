using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAgentAuthorityMatrixTests
{
    private static readonly StaticCompanyToolRegistry Registry = new();

    public static IEnumerable<object[]> MatrixRows() =>
        FinanceAgentAuthorityMatrix.Build(Registry).Select(static entry => new object[] { entry });

    [Theory]
    [MemberData(nameof(MatrixRows))]
    public void Registered_finance_tool_has_complete_authorization_and_risk_metadata(
        FinanceAgentAuthorityMatrixEntry entry)
    {
        Assert.False(string.IsNullOrWhiteSpace(entry.ToolName));
        Assert.Matches("^\\d+\\.\\d+\\.\\d+", entry.ToolVersion);
        Assert.Contains(entry.ActionType, new[] { "read", "recommend", "execute" });
        Assert.Equal("finance", entry.Scope);
        Assert.NotEmpty(entry.RequiredCompanyPolicies);
        Assert.NotEmpty(entry.RequiredActorPermissions);
        Assert.StartsWith("effective_authority:", entry.AgentGrant);
        Assert.False(string.IsNullOrWhiteSpace(entry.RiskTier));
        Assert.False(string.IsNullOrWhiteSpace(entry.ApprovalBehavior));
        Assert.False(string.IsNullOrWhiteSpace(entry.ExternalSideEffect));
        Assert.Equal(FinanceAgentAuthorityMatrix.CoverageTest, entry.OwningRegressionTest);

        var registration = Registry.ListTools().Single(candidate =>
            string.Equals(candidate.ToolName, entry.ToolName, StringComparison.OrdinalIgnoreCase));
        var action = ToolActionTypeValues.Parse(entry.ActionType);
        Assert.Contains(action, registration.SupportedActions);

        if (action == ToolActionType.Execute)
        {
            var risk = Assert.IsType<FinanceToolRiskClassification>(registration.FinanceRiskClassification);
            Assert.Contains(risk.RequiredActorPermission, entry.RequiredActorPermissions);
            Assert.Equal(risk.RiskTier, entry.RiskTier);
            Assert.Equal(risk.DefaultApprovalBehavior, entry.ApprovalBehavior);
            Assert.Equal(risk.ExternalSideEffectClassification, entry.ExternalSideEffect);
        }
        else
        {
            Assert.Equal("not_applicable", entry.ApprovalBehavior);
            Assert.Equal("none", entry.ExternalSideEffect);
        }
    }

    [Fact]
    public void Matrix_covers_every_registered_finance_action_exactly_once()
    {
        var expected = Registry.ListTools()
            .Where(static tool => tool.Scopes.Contains("finance"))
            .SelectMany(static tool => tool.SupportedActions.Select(action =>
                $"{tool.ToolName}|{action.ToStorageValue()}|finance"))
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var actual = FinanceAgentAuthorityMatrix.Build(Registry)
            .Select(static entry => $"{entry.ToolName}|{entry.ActionType}|{entry.Scope}")
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expected, actual);
        Assert.Equal(actual.Length, actual.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Authority_metrics_expose_only_bounded_tool_outcome_and_reason_tags()
    {
        var observed = new ConcurrentQueue<(string Name, Dictionary<string, object?> Tags)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == FinanceAgentAuthorityTelemetry.MeterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            var copiedTags = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                copiedTags[tag.Key] = tag.Value;
            }
            observed.Enqueue((instrument.Name, copiedTags));
        });
        listener.Start();

        FinanceAgentAuthorityTelemetry.RecordAuthorization(new FinanceAgentAuthorizationDecisionDto(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), FinanceAgentActorTypes.Human, Guid.NewGuid(),
            FinanceAgentMembershipStates.Active, "approve_invoice", "execute", "finance", ["FinanceApproval"],
            [FinancePermissions.Approve], FinanceAgentAuthorizationOutcomes.Denied,
            FinanceAgentAuthorizationReasonCodes.PermissionMissing, "sentinel-secret-payload", [], DateTime.UtcNow,
            "policy-v1"));
        FinanceAgentAuthorityTelemetry.RecordApproval("approve_invoice", "stale",
            FinanceApprovalContinuationReasonCodes.BindingMismatch);

        Assert.Equal(2, observed.Count);
        Assert.All(observed, measurement =>
        {
            Assert.Contains(measurement.Name,
                new[] { FinanceAgentAuthorityTelemetry.AuthorizationMetricName, FinanceAgentAuthorityTelemetry.ApprovalMetricName });
            Assert.Subset(
                new HashSet<string> { "tool.name", "action.type", "decision.outcome", "reason.code" },
                measurement.Tags.Keys.ToHashSet(StringComparer.Ordinal));
            Assert.DoesNotContain(measurement.Tags, tag =>
                tag.Value?.ToString()?.Contains("sentinel-secret-payload", StringComparison.Ordinal) == true);
        });
    }
}
