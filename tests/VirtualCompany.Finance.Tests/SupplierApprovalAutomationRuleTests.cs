using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Finance.Tests;

public sealed class SupplierApprovalAutomationRuleTests
{
    [Fact]
    public void Enable_records_supplier_stage_agent_and_grantor()
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var grantorId = Guid.NewGuid();
        var now = new DateTime(2026, 7, 29, 16, 0, 0, DateTimeKind.Utc);

        var rule = new SupplierApprovalAutomationRule(
            Guid.NewGuid(),
            companyId,
            "5591234567",
            "Prosa Test Services AB",
            "559123-4567",
            SupplierApprovalAutomationStages.SupplierCreation,
            agentId,
            "Laura",
            grantorId,
            "Alice Admin",
            now);

        Assert.Equal(companyId, rule.CompanyId);
        Assert.Equal("5591234567", rule.SupplierKey);
        Assert.Equal(SupplierApprovalAutomationStages.SupplierCreation, rule.Stage);
        Assert.Equal(agentId, rule.AgentId);
        Assert.Equal("Laura", rule.AgentDisplayName);
        Assert.Equal(grantorId, rule.GrantedByUserId);
        Assert.True(rule.IsActive);
        Assert.Equal(now, rule.CreatedUtc);
        Assert.Equal(now, rule.UpdatedUtc);
        Assert.Null(rule.RevokedUtc);
    }

    [Fact]
    public void Revoke_deactivates_rule_without_losing_audit_identity()
    {
        var grantorId = Guid.NewGuid();
        var enabledAt = new DateTime(2026, 7, 29, 16, 0, 0, DateTimeKind.Utc);
        var revokedAt = enabledAt.AddMinutes(5);

        var rule = new SupplierApprovalAutomationRule(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "5591234567",
            "Prosa Test Services AB",
            "559123-4567",
            SupplierApprovalAutomationStages.InvoiceRegistration,
            Guid.NewGuid(),
            "Laura",
            grantorId,
            "Alice Admin",
            enabledAt);

        rule.Revoke(revokedAt);

        Assert.False(rule.IsActive);
        Assert.Equal(revokedAt, rule.UpdatedUtc);
        Assert.Equal(revokedAt, rule.RevokedUtc);
        Assert.Equal(grantorId, rule.GrantedByUserId);
    }
}
