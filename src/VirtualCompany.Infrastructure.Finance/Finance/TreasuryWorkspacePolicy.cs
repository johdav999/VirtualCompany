using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class TreasuryWorkspacePolicy : ITreasuryWorkspacePolicy
{
    public IReadOnlyList<TreasuryWorkspaceActionDecisionDto> Evaluate(TreasuryWorkspacePolicyInput input) =>
    [
        Reconnect(input),
        RecoverGap(input),
        Reconcile(input),
        ReviewPayment(input),
        CancelPayment(input),
        InvestigateLiquidity(input)
    ];

    private static TreasuryWorkspaceActionDecisionDto Reconnect(TreasuryWorkspacePolicyInput input)
    {
        var recoveryRequired = !string.IsNullOrWhiteSpace(input.ConnectionStatus) &&
            (!string.Equals(input.ConnectionStatus, BankConnectionStatuses.Active, StringComparison.OrdinalIgnoreCase) ||
             input.ConnectionReasonCode is BankConnectionReasonCodes.ExpiredConsent or
                 BankConnectionReasonCodes.MissingConsent or BankConnectionReasonCodes.ScopeLoss or
                 BankConnectionReasonCodes.ProviderOutage);
        if (!recoveryRequired)
        {
            return Block(TreasuryWorkspaceActionTypes.Reconnect,
                TreasuryWorkspaceReasonCodes.ConnectionCurrent,
                "The connection has current consent and does not require reconnection.");
        }

        return input.CanEdit
            ? Allow(TreasuryWorkspaceActionTypes.Reconnect,
                TreasuryWorkspaceReasonCodes.ConnectionRecoveryRequired,
                "Reconnect or renew the bank consent before relying on new provider evidence.")
            : Permission(TreasuryWorkspaceActionTypes.Reconnect, approval: false);
    }

    private static TreasuryWorkspaceActionDecisionDto RecoverGap(TreasuryWorkspacePolicyInput input)
    {
        if (!input.HasOpenGap)
        {
            return Block(TreasuryWorkspaceActionTypes.RecoverGap,
                TreasuryWorkspaceReasonCodes.FeedGapUnavailable,
                "No retained open feed range is available for recovery.");
        }

        return input.CanEdit
            ? Allow(TreasuryWorkspaceActionTypes.RecoverGap,
                TreasuryWorkspaceReasonCodes.FeedGapOpen,
                "Recover the bounded retained range without replaying already imported rows.")
            : Permission(TreasuryWorkspaceActionTypes.RecoverGap, approval: false);
    }

    private static TreasuryWorkspaceActionDecisionDto Reconcile(TreasuryWorkspacePolicyInput input)
    {
        var needsReconciliation = input.ReconciliationStatus is BankTransactionReconciliationStatuses.Unreconciled or
            BankTransactionReconciliationStatuses.PartiallyReconciled;
        if (!needsReconciliation)
        {
            return Block(TreasuryWorkspaceActionTypes.Reconcile,
                TreasuryWorkspaceReasonCodes.ReconciliationComplete,
                "The retained bank row is already fully reconciled.");
        }

        return input.CanEdit
            ? Allow(TreasuryWorkspaceActionTypes.Reconcile,
                TreasuryWorkspaceReasonCodes.ReconciliationRequired,
                "Review retained bank evidence and complete reconciliation through the authoritative workflow.")
            : Permission(TreasuryWorkspaceActionTypes.Reconcile, approval: false);
    }

    private static TreasuryWorkspaceActionDecisionDto ReviewPayment(TreasuryWorkspacePolicyInput input) =>
        string.IsNullOrWhiteSpace(input.PaymentStatus)
            ? Block(TreasuryWorkspaceActionTypes.ReviewPayment,
                TreasuryWorkspaceReasonCodes.PaymentUnavailable,
                "No retained payment instruction or execution is available to review.")
            : Allow(TreasuryWorkspaceActionTypes.ReviewPayment,
                TreasuryWorkspaceReasonCodes.PaymentReviewAvailable,
                "Open the retained instruction, approval, provider acknowledgement, and settlement evidence.");

    private static TreasuryWorkspaceActionDecisionDto CancelPayment(TreasuryWorkspacePolicyInput input)
    {
        var lifecycleAllowsCancellation = input.PaymentCanCancel ||
            input.PaymentStatus is PaymentExecutionStatuses.Queued or PaymentExecutionStatuses.AwaitingAuthorization;
        if (!lifecycleAllowsCancellation)
        {
            return Block(TreasuryWorkspaceActionTypes.CancelPayment,
                TreasuryWorkspaceReasonCodes.PaymentCancellationUnsafe,
                "The current provider lifecycle does not permit a safe cancellation.", approval: true);
        }

        return input.CanApprove
            ? Allow(TreasuryWorkspaceActionTypes.CancelPayment,
                TreasuryWorkspaceReasonCodes.PaymentCancellationAllowed,
                "Cancellation is available, subject to the current authority recheck.", approval: true)
            : Permission(TreasuryWorkspaceActionTypes.CancelPayment, approval: true);
    }

    private static TreasuryWorkspaceActionDecisionDto InvestigateLiquidity(TreasuryWorkspacePolicyInput input)
    {
        var needsInvestigation = input.LiquidityRisk is "critical" or "warning" or "missing";
        return needsInvestigation
            ? Allow(TreasuryWorkspaceActionTypes.InvestigateLiquidity,
                TreasuryWorkspaceReasonCodes.LiquidityInvestigationRequired,
                "Review the projection, thresholds, and cited inflow and outflow evidence.")
            : Block(TreasuryWorkspaceActionTypes.InvestigateLiquidity,
                TreasuryWorkspaceReasonCodes.LiquidityHealthy,
                "The current short-horizon projection remains above the configured liquidity thresholds.");
    }

    private static TreasuryWorkspaceActionDecisionDto Permission(string action, bool approval) =>
        Block(action,
            approval ? TreasuryWorkspaceReasonCodes.FinanceApprovalRequired : TreasuryWorkspaceReasonCodes.FinanceEditRequired,
            approval
                ? "Finance approval permission is required for this action."
                : "Finance edit permission is required for this action.",
            approval);

    private static TreasuryWorkspaceActionDecisionDto Allow(
        string action,
        string reasonCode,
        string explanation,
        bool approval = false) =>
        new(action, true, reasonCode, explanation, approval);

    private static TreasuryWorkspaceActionDecisionDto Block(
        string action,
        string reasonCode,
        string explanation,
        bool approval = false) =>
        new(action, false, reasonCode, explanation, approval);
}
