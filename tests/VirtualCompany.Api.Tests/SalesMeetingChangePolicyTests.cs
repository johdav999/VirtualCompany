using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class SalesMeetingChangePolicyTests
{
    private readonly SalesMeetingChangePolicy policy = new();

    [Theory]
    [InlineData(SalesMeetingChangeTargetType.Deal, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.DealStage, SalesMeetingChangeValueKind.Guid, false, true)]
    [InlineData(SalesMeetingChangeTargetType.Deal, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.DealProbability, SalesMeetingChangeValueKind.Decimal, false, true)]
    [InlineData(SalesMeetingChangeTargetType.Deal, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.DealValue, SalesMeetingChangeValueKind.Decimal, true, false)]
    [InlineData(SalesMeetingChangeTargetType.Deal, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.DealNextStep, SalesMeetingChangeValueKind.String, false, true)]
    [InlineData(SalesMeetingChangeTargetType.Lead, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.LeadNextAction, SalesMeetingChangeValueKind.String, false, true)]
    [InlineData(SalesMeetingChangeTargetType.Contact, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.ContactEmail, SalesMeetingChangeValueKind.Email, false, true)]
    [InlineData(SalesMeetingChangeTargetType.CustomerMinutes, SalesMeetingChangeAction.SendCustomerMinutes, SalesMeetingChangeField.CustomerMinutesRecipient, SalesMeetingChangeValueKind.Email, true, false)]
    [InlineData(SalesMeetingChangeTargetType.MeetingSession, SalesMeetingChangeAction.ScheduleNextMeeting, SalesMeetingChangeField.NextMeeting, SalesMeetingChangeValueKind.NextMeeting, true, false)]
    public void Policy_classifies_each_executable_information_or_action_class(SalesMeetingChangeTargetType target,
        SalesMeetingChangeAction action, SalesMeetingChangeField field, SalesMeetingChangeValueKind kind,
        bool requiresApproval, bool safeForBulk)
    {
        var result = policy.Evaluate(target, action, field, kind, CompanyMembershipRole.Manager);
        Assert.True(result.IsAllowed); Assert.Equal(requiresApproval, result.RequiresApproval); Assert.Equal(safeForBulk, result.IsSafeForBulkApproval);
    }

    [Theory]
    [InlineData(SalesMeetingChangeField.Discount)]
    [InlineData(SalesMeetingChangeField.PricePromise)]
    [InlineData(SalesMeetingChangeField.ContractTerm)]
    public void Commercial_commitments_are_always_gated(SalesMeetingChangeField field)
    {
        var result = policy.Evaluate(SalesMeetingChangeTargetType.Deal, SalesMeetingChangeAction.UpdateField, field, SalesMeetingChangeValueKind.String, CompanyMembershipRole.Owner);
        Assert.True(result.RequiresApproval); Assert.False(result.IsSafeForBulkApproval); Assert.Equal(SalesMeetingChangeRiskClass.AlwaysGated, result.RiskClass);
    }

    [Fact]
    public void Arbitrary_target_field_combinations_and_internal_accountant_access_are_denied()
    {
        Assert.False(policy.Evaluate(SalesMeetingChangeTargetType.Contact, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.DealStage, SalesMeetingChangeValueKind.Guid, CompanyMembershipRole.Owner).IsAllowed);
        Assert.False(policy.Evaluate(SalesMeetingChangeTargetType.Deal, SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.DealStage, SalesMeetingChangeValueKind.Guid, CompanyMembershipRole.Accountant).IsAllowed);
    }

    [Fact]
    public void Editing_invalidates_exact_approval_and_stable_business_input_has_stable_idempotency()
    {
        var company = Guid.NewGuid(); var session = Guid.NewGuid(); var artifact = Guid.NewGuid(); var target = Guid.NewGuid(); var actor = Guid.NewGuid();
        SalesMeetingChangeProposal Create() => new(Guid.NewGuid(), company, session, artifact, SalesMeetingChangeTargetType.Deal, target,
            SalesMeetingChangeAction.UpdateField, SalesMeetingChangeField.DealNextStep, SalesMeetingChangeValueKind.String,
            "\"Send proposal\"", "null", "42", .9m, "Customer requested a proposal.", "[\"observation:abc\"]", "evidence-hash",
            SalesMeetingChangeRiskClass.ConfirmationRequired, false, SalesMeetingChangePolicy.CurrentVersion, actor, DateTime.UtcNow);
        var first = Create(); var second = Create(); Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        first.Approve(1, "binding", actor, DateTime.UtcNow); Assert.Equal(SalesMeetingChangeProposalStatus.Approved, first.Status);
        first.Edit(2, SalesMeetingChangeValueKind.String, "\"Send revised proposal\"", .8m, "Edited", "[\"observation:def\"]", "new-evidence", SalesMeetingChangeRiskClass.ConfirmationRequired, false, SalesMeetingChangePolicy.CurrentVersion, actor, DateTime.UtcNow);
        Assert.Equal(SalesMeetingChangeProposalStatus.Draft, first.Status); Assert.Null(first.ApprovalBindingHash); Assert.Null(first.ApprovedUtc);
    }
}
