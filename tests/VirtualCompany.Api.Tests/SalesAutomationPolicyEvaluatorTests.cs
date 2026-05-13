using VirtualCompany.Application.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesAutomationPolicyEvaluatorTests
{
    private readonly SalesAutomationPolicyEvaluator _evaluator = new();

    [Fact]
    public void ManualOnlyRequiresApprovalForFollowUpSend()
    {
        var decision = _evaluator.Evaluate(SalesAutomationPolicyModes.ManualOnly, SalesRecommendationActions.SendEmail, SalesRecommendationRiskLevels.Low);

        Assert.True(decision.RequiresApproval);
        Assert.False(decision.CanAutoExecute);
        Assert.Equal("approval_required", decision.ExecutionMode);
    }

    [Fact]
    public void DraftOnlyAllowsDraftButNotSend()
    {
        var draft = _evaluator.Evaluate(SalesAutomationPolicyModes.DraftOnly, SalesRecommendationActions.CreateDraftReply, SalesRecommendationRiskLevels.Medium);
        var send = _evaluator.Evaluate(SalesAutomationPolicyModes.DraftOnly, SalesRecommendationActions.SendEmail, SalesRecommendationRiskLevels.Low);

        Assert.False(draft.RequiresApproval);
        Assert.True(draft.CanAutoExecute);
        Assert.True(send.RequiresApproval);
    }

    [Fact]
    public void AutoSendOnlyAppliesToLowRiskFollowUps()
    {
        var lowRisk = _evaluator.Evaluate(SalesAutomationPolicyModes.AutoSendLowRiskFollowUps, SalesRecommendationActions.SendEmail, SalesRecommendationRiskLevels.Low);
        var highRisk = _evaluator.Evaluate(SalesAutomationPolicyModes.AutoSendLowRiskFollowUps, SalesRecommendationActions.SendEmail, SalesRecommendationRiskLevels.High);

        Assert.True(lowRisk.CanAutoExecute);
        Assert.False(lowRisk.RequiresApproval);
        Assert.True(highRisk.RequiresApproval);
    }

    [Fact]
    public void FinanceDocumentsAlwaysRequireApproval()
    {
        var decision = _evaluator.Evaluate(SalesAutomationPolicyModes.AutoSendLowRiskFollowUps, SalesRecommendationActions.CreateFinanceDocument, SalesRecommendationRiskLevels.Low);

        Assert.True(decision.RequiresApproval);
        Assert.False(decision.CanAutoExecute);
    }
}