using System.Text.Json.Nodes;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class AgentAuthorityTransparencyPresenterTests
{
    [Theory]
    [InlineData("available", "AuthorityStateAvailable", "is-ready")]
    [InlineData("approval_required", "AuthorityStateApprovalRequired", "is-review")]
    [InlineData("configuration_required", "AuthorityStateConfigurationRequired", "is-setup")]
    [InlineData("permission_denied", "AuthorityStatePermissionDenied", "is-blocked")]
    [InlineData("integration_unavailable", "AuthorityStateIntegrationUnavailable", "is-setup")]
    [InlineData("not_implemented", "AuthorityStateNotImplemented", "is-blocked")]
    public void CapabilityState_HasStableLocalizedPresentation(string state, string resourceKey, string tone)
    {
        Assert.Equal(resourceKey, AgentAuthorityTransparencyPresenter.StateLabelKey(state));
        Assert.Equal(tone, AgentAuthorityTransparencyPresenter.StateTone(state));
    }

    [Theory]
    [InlineData("pending", "ApprovalStatusPending", false)]
    [InlineData("approved", "ApprovalStatusApproved", false)]
    [InlineData("rejected", "ApprovalStatusRejected", false)]
    [InlineData("expired", "ApprovalStatusExpired", true)]
    [InlineData("cancelled", "ApprovalStatusCancelled", false)]
    [InlineData("stale", "ApprovalStatusStale", true)]
    [InlineData("superseded", "ApprovalStatusSuperseded", true)]
    [InlineData("revoked", "ApprovalStatusRevoked", true)]
    public void ApprovalState_HasStableLifecyclePresentation(string status, string resourceKey, bool requiresFreshReview)
    {
        Assert.Equal(resourceKey, AgentAuthorityTransparencyPresenter.ApprovalStatusLabelKey(status));
        Assert.Equal(requiresFreshReview, AgentAuthorityTransparencyPresenter.RequiresFreshReview(status));
    }

    [Fact]
    public void CapabilityRows_ExposeActionPermissionApprovalAndReadinessFromEffectiveAuthority()
    {
        var catalog = new AgentCapabilityCatalogViewModel
        {
            EffectiveTools =
            [
                Tool("read_cash", "read", "available", "finance.view"),
                Tool("recommend_category", "recommend", "available", "finance.view"),
                Tool("post_expense", "execute", "approval_required", "finance.accounting.admin")
            ]
        };

        var rows = AgentAuthorityTransparencyPresenter.SelectCapabilityRows(catalog);

        Assert.Collection(rows,
            row => AssertRow(row, "read", "finance.view", "not_required", "ready"),
            row => AssertRow(row, "recommend", "finance.view", "not_required", "ready"),
            row => AssertRow(row, "execute", "finance.accounting.admin", "required", "ready"));
    }

    [Fact]
    public void CapabilityRows_PreferAuthoritativeTransparencyFieldsFromApi()
    {
        var tool = Tool("post_expense", "execute", "available", "finance.edit");
        tool.ActorPermission = "finance.accounting.admin";
        tool.ApprovalBehavior = "always_review";
        tool.IntegrationState = "setup_required";

        var row = Assert.Single(AgentAuthorityTransparencyPresenter.SelectCapabilityRows(
            new AgentCapabilityCatalogViewModel { EffectiveTools = [tool] }));

        Assert.Equal("finance.accounting.admin", row.ActorPermission);
        Assert.Equal("always_review", row.ApprovalRequirement);
        Assert.Equal("setup_required", row.IntegrationState);
    }

    [Fact]
    public void ApprovalPreview_WhitelistsSafeEvidenceAndDropsRawPolicyAndSecretData()
    {
        var agentId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var issuedUtc = new DateTime(2026, 8, 30, 9, 15, 0, DateTimeKind.Utc);
        var expiresUtc = issuedUtc.AddHours(12);
        var approval = new ApprovalRequestViewModel
        {
            Id = approvalId,
            TargetEntityType = "finance_invoice",
            TargetEntityId = targetId,
            RequestedByActorType = "agent",
            RequestedByActorId = agentId,
            Status = "pending",
            RequiredRole = "finance_approver",
            AffectedDataSummary = "Invoice INV-1042 · 12,400 SEK",
            CreatedAt = issuedUtc,
            ThresholdContext = new(StringComparer.OrdinalIgnoreCase)
            {
                ["toolName"] = "approve_invoice",
                ["actionType"] = "execute",
                ["toolExecutionAttemptId"] = executionId,
                ["riskClassification"] = new JsonObject { ["riskTier"] = "high", ["hiddenPolicyPrompt"] = "do not expose" },
                ["approvalBinding"] = new JsonObject
                {
                    ["issuedUtc"] = issuedUtc,
                    ["expiresUtc"] = expiresUtc,
                    ["segregationRequired"] = true,
                    ["payloadHash"] = "secret-hash"
                },
                ["rawPayload"] = new JsonObject { ["providerSecret"] = "top-secret" }
            }
        };

        var preview = Assert.IsType<AgentApprovalPreview>(
            AgentAuthorityTransparencyPresenter.CreateApprovalPreview([approval], agentId));

        Assert.Equal(approvalId, preview.ApprovalId);
        Assert.Equal("Invoice INV-1042 · 12,400 SEK", preview.TargetSummary);
        Assert.Equal("approve_invoice", preview.ToolName);
        Assert.Equal("execute", preview.ActionType);
        Assert.Equal("high", preview.RiskTier);
        Assert.Equal(executionId, preview.ToolExecutionId);
        Assert.Equal(issuedUtc, preview.EvidenceAt);
        Assert.Equal(expiresUtc, preview.ExpiresAt);
        Assert.True(preview.RequiresIndependentApproval);
        Assert.DoesNotContain("secret-hash", preview.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret", preview.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("hiddenPolicyPrompt", preview.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ApprovalPreview_IgnoresRequestsFromOtherActorsAndAgents()
    {
        var agentId = Guid.NewGuid();
        var approvals = new[]
        {
            new ApprovalRequestViewModel { RequestedByActorType = "user", RequestedByActorId = agentId, CreatedAt = DateTime.UtcNow },
            new ApprovalRequestViewModel { RequestedByActorType = "agent", RequestedByActorId = Guid.NewGuid(), CreatedAt = DateTime.UtcNow }
        };

        Assert.Null(AgentAuthorityTransparencyPresenter.CreateApprovalPreview(approvals, agentId));
    }

    [Theory]
    [InlineData("finance_transaction", "/finance/transactions/")]
    [InlineData("finance_bill", "/finance/supplier-bills/")]
    [InlineData("finance_payment", "/finance/payments/")]
    [InlineData("finance_invoice", "/finance/invoices/")]
    [InlineData("finance_anomaly", "/finance/issues/")]
    [InlineData("finance_alert", "/finance/alerts/")]
    public void TargetLink_UsesKnownSafeFinanceRoutes(string targetType, string expectedPath)
    {
        var preview = new AgentApprovalPreview(
            Guid.NewGuid(), "pending", targetType, Guid.NewGuid(), "Record", null, null,
            DateTime.UtcNow, null, null, null, false, null, null);

        var href = AgentAuthorityTransparencyPresenter.BuildTargetHref(preview, Guid.NewGuid());

        Assert.NotNull(href);
        Assert.Contains(expectedPath, href, StringComparison.Ordinal);
    }

    private static EffectiveAgentToolAuthorityViewModel Tool(
        string name,
        string action,
        string state,
        string permission) => new()
        {
            ToolName = name,
            ActionType = action,
            State = state,
            ReasonCode = state == "approval_required" ? "authority_approval_required" : "authority_available",
            RequiredFinancePermissions = [permission]
        };

    private static void AssertRow(
        AgentAuthorityCapabilityRow row,
        string action,
        string permission,
        string approval,
        string integration)
    {
        Assert.Equal(action, row.ActionMode);
        Assert.Equal(permission, row.ActorPermission);
        Assert.Equal(approval, row.ApprovalRequirement);
        Assert.Equal(integration, row.IntegrationState);
    }
}
