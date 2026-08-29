using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;

namespace VirtualCompany.Finance.Tests;

public sealed class TreasuryWorkspacePolicyTests
{
    private readonly TreasuryWorkspacePolicy _policy = new();

    [Fact]
    public void Reconnect_and_gap_recovery_return_stable_permission_reasons()
    {
        var denied = _policy.Evaluate(new TreasuryWorkspacePolicyInput(
            CanEdit: false,
            CanApprove: false,
            ConnectionStatus: BankConnectionStatuses.AttentionRequired,
            ConnectionReasonCode: BankConnectionReasonCodes.ExpiredConsent,
            HasOpenGap: true));

        Assert.Equal(TreasuryWorkspaceReasonCodes.FinanceEditRequired,
            Decision(denied, TreasuryWorkspaceActionTypes.Reconnect).ReasonCode);
        Assert.False(Decision(denied, TreasuryWorkspaceActionTypes.RecoverGap).IsAllowed);

        var allowed = _policy.Evaluate(new TreasuryWorkspacePolicyInput(
            CanEdit: true,
            CanApprove: false,
            ConnectionStatus: BankConnectionStatuses.AttentionRequired,
            ConnectionReasonCode: BankConnectionReasonCodes.ExpiredConsent,
            HasOpenGap: true));

        Assert.Equal(TreasuryWorkspaceReasonCodes.ConnectionRecoveryRequired,
            Decision(allowed, TreasuryWorkspaceActionTypes.Reconnect).ReasonCode);
        Assert.Equal(TreasuryWorkspaceReasonCodes.FeedGapOpen,
            Decision(allowed, TreasuryWorkspaceActionTypes.RecoverGap).ReasonCode);
    }

    [Fact]
    public void Reconciliation_and_payment_cancellation_never_bypass_authority()
    {
        var decisions = _policy.Evaluate(new TreasuryWorkspacePolicyInput(
            CanEdit: true,
            CanApprove: false,
            ReconciliationStatus: BankTransactionReconciliationStatuses.Unreconciled,
            PaymentStatus: PaymentExecutionStatuses.Queued,
            PaymentCanCancel: true));

        Assert.True(Decision(decisions, TreasuryWorkspaceActionTypes.Reconcile).IsAllowed);
        Assert.True(Decision(decisions, TreasuryWorkspaceActionTypes.ReviewPayment).IsAllowed);
        var cancellation = Decision(decisions, TreasuryWorkspaceActionTypes.CancelPayment);
        Assert.False(cancellation.IsAllowed);
        Assert.True(cancellation.RequiresApproval);
        Assert.Equal(TreasuryWorkspaceReasonCodes.FinanceApprovalRequired, cancellation.ReasonCode);
    }

    [Theory]
    [InlineData("warning")]
    [InlineData("critical")]
    [InlineData("missing")]
    public void Liquidity_risk_exposes_investigation_without_mutating_state(string risk)
    {
        var decision = Decision(_policy.Evaluate(new TreasuryWorkspacePolicyInput(
            CanEdit: false,
            CanApprove: false,
            LiquidityRisk: risk)), TreasuryWorkspaceActionTypes.InvestigateLiquidity);

        Assert.True(decision.IsAllowed);
        Assert.Equal(TreasuryWorkspaceReasonCodes.LiquidityInvestigationRequired, decision.ReasonCode);
        Assert.False(decision.RequiresApproval);
    }

    private static TreasuryWorkspaceActionDecisionDto Decision(
        IReadOnlyList<TreasuryWorkspaceActionDecisionDto> decisions,
        string action) => decisions.Single(item => item.Action == action);
}
