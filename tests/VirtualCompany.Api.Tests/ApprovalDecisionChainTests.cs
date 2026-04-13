using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ApprovalDecisionChainTests
{
    [Fact]
    public void CreateForTarget_normalizes_required_role_to_single_step()
    {
        var approval = CreateApproval(requiredRole: "finance_approver");

        var step = Assert.Single(approval.Steps);
        Assert.Equal(ApprovalStepApproverType.Role, step.ApproverType);
        Assert.Equal("finance_approver", step.ApproverRef);
        Assert.Equal(step.Id, approval.CurrentActionableStep!.Id);
    }

    [Fact]
    public void CreateForTarget_normalizes_required_user_to_single_step()
    {
        var userId = Guid.NewGuid();
        var approval = CreateApproval(requiredUserId: userId);

        var step = Assert.Single(approval.Steps);
        Assert.Equal(ApprovalStepApproverType.User, step.ApproverType);
        Assert.Equal(userId.ToString("N"), step.ApproverRef);
    }

    [Fact]
    public void CreateForTarget_orders_multi_step_chain()
    {
        var userId = Guid.NewGuid();
        var approval = CreateApproval(steps:
        [
            new ApprovalStepDefinition(2, ApprovalStepApproverType.User, userId.ToString("N")),
            new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "manager")
        ]);

        Assert.Equal([1, 2], approval.Steps.OrderBy(x => x.SequenceNo).Select(x => x.SequenceNo));
        Assert.Equal(1, approval.CurrentActionableStep!.SequenceNo);
    }

    [Fact]
    public void CreateForTarget_rejects_missing_targeting_mode()
    {
        Assert.Throws<ArgumentException>(() => CreateApproval());
    }

    [Fact]
    public void CreateForTarget_rejects_mixed_targeting_modes()
    {
        Assert.Throws<ArgumentException>(() => CreateApproval(
            requiredRole: "manager",
            steps: [new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "finance_approver")]));
    }

    [Fact]
    public void ApproveCurrentStep_prevents_out_of_order_decisions()
    {
        var approval = CreateApproval(steps:
        [
            new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "manager"),
            new ApprovalStepDefinition(2, ApprovalStepApproverType.Role, "finance_approver")
        ]);

        var secondStep = approval.Steps.Single(x => x.SequenceNo == 2);

        Assert.Throws<InvalidOperationException>(() => approval.ApproveCurrentStep(secondStep.Id, Guid.NewGuid(), null));
    }

    [Fact]
    public void Approving_final_step_marks_approval_approved()
    {
        var approval = CreateApproval(steps:
        [
            new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "manager"),
            new ApprovalStepDefinition(2, ApprovalStepApproverType.Role, "finance_approver")
        ]);

        approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, Guid.NewGuid(), "ok");
        Assert.Equal(ApprovalRequestStatus.Pending, approval.Status);
        Assert.Equal(2, approval.CurrentActionableStep!.SequenceNo);

        approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, Guid.NewGuid(), "ok");

        Assert.Equal(ApprovalRequestStatus.Approved, approval.Status);
        Assert.Null(approval.CurrentActionableStep);
        Assert.All(approval.Steps, step => Assert.Equal(ApprovalStepStatus.Approved, step.Status));
        Assert.NotNull(approval.DecidedUtc);
    }

    [Fact]
    public void Rejecting_active_step_marks_approval_rejected()
    {
        var approval = CreateApproval(steps:
        [
            new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "manager"),
            new ApprovalStepDefinition(2, ApprovalStepApproverType.Role, "finance_approver")
        ]);

        approval.RejectCurrentStep(approval.CurrentActionableStep!.Id, Guid.NewGuid(), "no");

        var rejectedStep = approval.Steps.Single(x => x.SequenceNo == 1);
        Assert.Equal(ApprovalRequestStatus.Rejected, approval.Status);
        Assert.Equal("no", approval.DecisionSummary);
        Assert.Equal(ApprovalStepStatus.Rejected, rejectedStep.Status);
        Assert.Equal("no", rejectedStep.Comment);
        Assert.Null(approval.CurrentActionableStep);
    }

    [Fact]
    public void Rejecting_active_step_records_trimmed_comment_in_decision_chain()
    {
        var approval = CreateApproval(requiredRole: "manager");

        approval.RejectCurrentStep(approval.CurrentActionableStep!.Id, Guid.NewGuid(), "  Missing invoice attachment.  ");

        var rejectedStep = approval.Steps.Single();
        Assert.Equal("Missing invoice attachment.", rejectedStep.Comment);
        Assert.Equal("Missing invoice attachment.", approval.DecisionSummary);

        var steps = Assert.IsType<JsonArray>(approval.DecisionChain["steps"]);
        var step = Assert.IsType<JsonObject>(Assert.Single(steps));
        Assert.Equal("rejected", step["status"]!.GetValue<string>());
        Assert.Equal("Missing invoice attachment.", step["comment"]!.GetValue<string>());
    }

    [Fact]
    public void Approved_approval_can_execute_guarded_action()
    {
        var approval = CreateApproval(requiredRole: "manager");

        approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, Guid.NewGuid(), "ok");

        Assert.True(approval.IsTerminal);
        Assert.True(approval.CanExecuteGuardedAction);
        Assert.Null(approval.ExecutionBlockReasonCode);
    }

    [Theory]
    [InlineData(ApprovalRequestStatus.Pending, "approval_pending")]
    [InlineData(ApprovalRequestStatus.Rejected, "approval_rejected")]
    [InlineData(ApprovalRequestStatus.Expired, "approval_expired")]
    [InlineData(ApprovalRequestStatus.Cancelled, "approval_cancelled")]
    public void Non_approved_approval_cannot_execute_guarded_action(ApprovalRequestStatus status, string reasonCode)
    {
        var approval = CreateApprovalWithStatus(status);

        Assert.False(approval.CanExecuteGuardedAction);
        Assert.Equal(reasonCode, approval.ExecutionBlockReasonCode);
        Assert.Equal(status != ApprovalRequestStatus.Pending, approval.IsTerminal);
    }

    [Fact]
    public void Expiring_approval_marks_it_non_actionable()
    {
        var approval = CreateApproval(requiredRole: "manager");

        approval.MarkExpired();

        Assert.Equal(ApprovalRequestStatus.Expired, approval.Status);
        Assert.Equal("Approval request expired.", approval.DecisionSummary);
        Assert.Null(approval.CurrentActionableStep);
        Assert.Throws<InvalidOperationException>(() => approval.ApproveCurrentStep(approval.Steps.Single().Id, Guid.NewGuid(), null));
        Assert.False(approval.CanExecuteGuardedAction);
    }

    [Fact]
    public void Cancelling_approval_marks_it_non_actionable()
    {
        var approval = CreateApproval(requiredRole: "manager");

        approval.MarkCancelled("superseded");

        Assert.Equal(ApprovalRequestStatus.Cancelled, approval.Status);
        Assert.Equal("superseded", approval.DecisionSummary);
        Assert.Null(approval.CurrentActionableStep);
        Assert.Throws<InvalidOperationException>(() => approval.RejectCurrentStep(approval.Steps.Single().Id, Guid.NewGuid(), null));
        Assert.Throws<InvalidOperationException>(() => approval.ApproveCurrentStep(approval.Steps.Single().Id, Guid.NewGuid(), null));
        Assert.False(approval.CanExecuteGuardedAction);
    }

    [Fact]
    public void Legacy_action_approval_gets_default_owner_step()
    {
        var approval = new ApprovalRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "erp", ToolActionType.Execute, "founder", new Dictionary<string, JsonNode?> { ["amount"] = JsonValue.Create(100) });

        var step = Assert.Single(approval.Steps);
        Assert.Equal(ApprovalStepApproverType.Role, step.ApproverType);
        Assert.Equal("owner", step.ApproverRef);
    }

    private static ApprovalRequest CreateApproval(
        string? requiredRole = null,
        Guid? requiredUserId = null,
        IEnumerable<ApprovalStepDefinition>? steps = null) =>
        ApprovalRequest.CreateForTarget(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ApprovalTargetEntityType.Task,
            Guid.NewGuid(),
            "user",
            Guid.NewGuid(),
            "threshold",
            new Dictionary<string, JsonNode?>
            {
                ["amount"] = JsonValue.Create(100)
            },
            requiredRole,
            requiredUserId,
            steps ?? []);

    private static ApprovalRequest CreateApprovalWithStatus(ApprovalRequestStatus status)
    {
        var approval = CreateApproval(requiredRole: "manager");
        switch (status)
        {
            case ApprovalRequestStatus.Approved:
                approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, Guid.NewGuid(), "ok");
                break;
            case ApprovalRequestStatus.Rejected:
                approval.RejectCurrentStep(approval.CurrentActionableStep!.Id, Guid.NewGuid(), "no");
                break;
            case ApprovalRequestStatus.Expired:
                approval.MarkExpired();
                break;
            case ApprovalRequestStatus.Cancelled:
                approval.MarkCancelled("superseded");
                break;
        }

        return approval;
    }
}