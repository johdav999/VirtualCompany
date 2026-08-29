using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class CashPositionPage : FinanceSummaryPageBase<TreasuryWorkspaceResponse>
{
    [Inject] private TreasuryWorkspaceUsageTelemetry UsageTelemetry { get; set; } = default!;

    protected override async Task<TreasuryWorkspaceResponse?> LoadSummaryViewModelAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var workspace = await FinanceApiClient.GetTreasuryWorkspaceAsync(
            companyId,
            horizonDays: 14,
            exceptionLimit: 12,
            taskLimit: 8,
            cancellationToken);
        if (workspace is not null)
        {
            UsageTelemetry.Viewed(
                workspace.Liquidity.RiskLevel,
                workspace.HasStaleEvidence || workspace.HasMissingEvidence,
                CultureInfo.CurrentUICulture.Name);
        }

        return workspace;
    }

    private string FormatMoney(decimal amount, string currency) => LocalMoney.Format(amount, currency);
    private string FormatOptionalMoney(decimal? amount, string currency) =>
        amount.HasValue ? LocalMoney.Format(amount.Value, currency) : FinanceText["TreasuryNotConfigured"];
    private string FormatDateTime(DateTime? value) => value.HasValue
        ? LocalDateTime.DateTime(value.Value)
        : FinanceText["TreasuryEvidenceMissing"];
    private string FormatDate(DateOnly? value) => value.HasValue
        ? value.Value.ToString("d", CultureInfo.CurrentCulture)
        : "—";

    private string EvidenceLabel(string value) => value switch
    {
        TreasuryWorkspaceEvidenceStates.Current => FinanceText["TreasuryEvidenceCurrent"],
        TreasuryWorkspaceEvidenceStates.Stale => FinanceText["TreasuryEvidenceStale"],
        _ => FinanceText["TreasuryEvidenceMissing"]
    };

    private string RiskLabel(string value) => value switch
    {
        "critical" => FinanceText["TreasuryRiskCritical"],
        "warning" => FinanceText["TreasuryRiskWarning"],
        "missing" => FinanceText["TreasuryRiskMissing"],
        _ => FinanceText["TreasuryRiskHealthy"]
    };

    private string SeverityLabel(string value) => value switch
    {
        "critical" => FinanceText["TreasuryRiskCritical"],
        "high" => FinanceText["TreasurySeverityHigh"],
        "medium" => FinanceText["TreasurySeverityMedium"],
        "info" => FinanceText["TreasurySeverityInfo"],
        _ => value
    };

    private string AccountExplanation(TreasuryAccountCoverageResponse account)
    {
        if (!string.Equals(account.ConnectionStatus, "active", StringComparison.OrdinalIgnoreCase))
            return FinanceText["TreasuryAccountConnectionDescription"];
        if (account.AllowedActions.Any(action => action.ReasonCode == TreasuryWorkspaceReasonCodes.FeedGapOpen))
            return FinanceText["TreasuryAccountGapDescription"];
        return account.EvidenceState switch
        {
            TreasuryWorkspaceEvidenceStates.Missing => FinanceText["TreasuryAccountMissingDescription"],
            TreasuryWorkspaceEvidenceStates.Stale => FinanceText["TreasuryAccountStaleDescription"],
            _ => FinanceText["TreasuryAccountCurrentDescription"]
        };
    }

    private string ExceptionTitle(TreasuryWorkspaceExceptionResponse item)
    {
        if (item.Kind == "task") return item.Title;
        if (item.Kind == "reconciliation" && FindReconciliation(item) is { } reconciliation)
            return FinanceText["TreasuryExceptionReconciliationTitle", reconciliation.Counterparty];
        if (item.Kind == "payment" && FindPayment(item) is { } payment)
            return FinanceText["TreasuryExceptionPaymentTitle", payment.Reference];
        return item.Kind switch
        {
            "bank_connection" => FinanceText["TreasuryExceptionBankConnectionTitle"],
            "bank_feed_gap" => FinanceText["TreasuryExceptionFeedGapTitle"],
            "stale_evidence" => FinanceText["TreasuryExceptionStaleEvidenceTitle"],
            "reconciliation" => FinanceText["TreasuryExceptionReconciliationGenericTitle"],
            "payment" => FinanceText["TreasuryExceptionPaymentGenericTitle"],
            "liquidity" => FinanceText["TreasuryExceptionLiquidityTitle"],
            _ => item.Title
        };
    }

    private string ExceptionExplanation(TreasuryWorkspaceExceptionResponse item)
    {
        if (item.Kind == "task") return item.Explanation;
        if (FindAccount(item) is { } account) return AccountExplanation(account);
        if (item.Kind == "reconciliation" && FindReconciliation(item) is { } reconciliation)
            return reconciliation.AgeDays >= 7
                ? FinanceText["TreasuryReconciliationAgedDescription", reconciliation.AgeDays]
                : FinanceText["TreasuryReconciliationReviewDescription"];
        if (item.Kind == "payment" && FindPayment(item) is { } payment) return PaymentExplanation(payment);
        if (item.Kind == "liquidity" && ViewModel is not null)
            return FinanceText["TreasuryLiquidityExceptionDescription",
                FormatMoney(ViewModel.Liquidity.ProjectedCash, ViewModel.Liquidity.Currency),
                FormatDateTime(ViewModel.Liquidity.ProjectionThroughUtc)];
        return item.Explanation;
    }

    private string ProjectionEvidenceBasis(TreasuryProjectionPointResponse point) =>
        ViewModel is not null && point.Date == DateOnly.FromDateTime(ViewModel.AsOfUtc)
            ? FinanceText["TreasuryProjectionPostedBasis"]
            : FinanceText["TreasuryProjectionOpenFlowsBasis"];

    private string PaymentExplanation(TreasuryPaymentWorkItemResponse payment) => payment.Status switch
    {
        "reconciliation_required" => FinanceText["TreasuryPaymentAmbiguousDescription"],
        "rejected" => FinanceText["TreasuryPaymentRejectedDescription"],
        "awaiting_authorization" => FinanceText["TreasuryPaymentAuthorizationDescription"],
        "approved" => FinanceText["TreasuryPaymentApprovedDescription"],
        "queued" or "submitting" => FinanceText["TreasuryPaymentQueuedDescription"],
        "provider_accepted" or "processing" => FinanceText["TreasuryPaymentProcessingDescription"],
        "provider_completed" => FinanceText["TreasuryPaymentCompletedDescription"],
        _ => FinanceText["TreasuryPaymentReviewDescription"]
    };

    private string LauraRoleName() => ViewModel is not null &&
                                      string.Equals(ViewModel.Laura.RoleName, "Finance Manager", StringComparison.OrdinalIgnoreCase)
        ? FinanceText["TreasuryFinanceManager"]
        : ViewModel?.Laura.RoleName ?? string.Empty;

    private string LauraSummary()
    {
        if (ViewModel is null) return string.Empty;
        if (ViewModel.Accounts.Any(account =>
                !string.Equals(account.ConnectionStatus, "active", StringComparison.OrdinalIgnoreCase) ||
                account.AllowedActions.Any(action => action.ReasonCode == TreasuryWorkspaceReasonCodes.FeedGapOpen)))
            return FinanceText["TreasuryLauraRecoverEvidence"];
        if (ViewModel.PaymentWork.ReconciliationRequired > 0)
            return FinanceText["TreasuryLauraAmbiguousPayments"];
        if (ViewModel.Liquidity.RiskLevel is "critical" or "warning")
            return FinanceText["TreasuryLauraLiquidity",
                FormatMoney(ViewModel.Liquidity.ProjectedCash, ViewModel.Liquidity.Currency),
                ViewModel.Liquidity.HorizonDays];
        if (ViewModel.Reconciliation.AgedUnreconciled > 0)
            return FinanceText["TreasuryLauraAgedReconciliation", ViewModel.Reconciliation.AgedUnreconciled];
        return FinanceText["TreasuryLauraStable"];
    }

    private IReadOnlyList<string> MissingEvidenceMessages()
    {
        if (ViewModel is null) return [];
        var messages = new List<string>();
        if (ViewModel.Accounts.Count == 0) messages.Add(FinanceText["TreasuryMissingNoAccounts"]);
        foreach (var account in ViewModel.Accounts)
        {
            if (account.EvidenceState == TreasuryWorkspaceEvidenceStates.Missing)
                messages.Add(FinanceText["TreasuryMissingAccountBalance", account.AccountName]);
            else if (account.EvidenceState == TreasuryWorkspaceEvidenceStates.Stale)
                messages.Add(FinanceText["TreasuryMissingAccountStale", account.AccountName]);
            if (!string.Equals(account.ConnectionStatus, "active", StringComparison.OrdinalIgnoreCase))
                messages.Add(FinanceText["TreasuryMissingConnection", account.AccountName]);
            if (account.AllowedActions.Any(action => action.ReasonCode == TreasuryWorkspaceReasonCodes.FeedGapOpen))
                messages.Add(FinanceText["TreasuryMissingGap", account.AccountName]);
        }
        return messages.Count > 0 ? messages : ViewModel.Laura.MissingEvidence;
    }

    private string CitationLabel(TreasuryEvidenceReferenceResponse citation) =>
        citation.SourceType == "cash_projection" && ViewModel is not null
            ? FinanceText["TreasuryCashProjectionCitation", ViewModel.Liquidity.HorizonDays]
            : citation.Label;

    private TreasuryAccountCoverageResponse? FindAccount(TreasuryWorkspaceExceptionResponse item) =>
        ViewModel?.Accounts.FirstOrDefault(account => item.Id is
            var id && (id == $"connection:{account.ConnectionId:N}" ||
                       id == $"feed-gap:{account.CheckpointId:N}" ||
                       id == $"stale-feed:{account.CompanyBankAccountId:N}"));

    private TreasuryUnreconciledItemResponse? FindReconciliation(TreasuryWorkspaceExceptionResponse item) =>
        ViewModel?.Reconciliation.Items.FirstOrDefault(row =>
            item.Id == $"reconciliation:{row.BankTransactionId:N}");

    private TreasuryPaymentWorkItemResponse? FindPayment(TreasuryWorkspaceExceptionResponse item) =>
        ViewModel?.PaymentWork.Items.FirstOrDefault(payment =>
            item.Id == $"payment:{(payment.ExecutionId ?? payment.BatchId):N}");

    private string ActionLabel(string action) => action switch
    {
        TreasuryWorkspaceActionTypes.Reconnect => FinanceText["TreasuryReconnect"],
        TreasuryWorkspaceActionTypes.RecoverGap => FinanceText["TreasuryRecoverGap"],
        TreasuryWorkspaceActionTypes.Reconcile => FinanceText["TreasuryReconcile"],
        TreasuryWorkspaceActionTypes.ReviewPayment => FinanceText["TreasuryReviewPayment"],
        TreasuryWorkspaceActionTypes.CancelPayment => FinanceText["TreasuryCancelPayment"],
        TreasuryWorkspaceActionTypes.InvestigateLiquidity => FinanceText["TreasuryInvestigateLiquidity"],
        "review_task" => FinanceText["TreasuryReviewTask"],
        _ => FinanceText["TreasuryReviewEvidence"]
    };

    private TreasuryWorkspaceActionDecisionResponse? AccountAction(TreasuryAccountCoverageResponse account) =>
        account.AllowedActions.FirstOrDefault(action =>
            action.ReasonCode is TreasuryWorkspaceReasonCodes.ConnectionRecoveryRequired or
                TreasuryWorkspaceReasonCodes.FeedGapOpen or
                TreasuryWorkspaceReasonCodes.FinanceEditRequired);

    private void TrackAction(TreasuryWorkspaceActionDecisionResponse action) =>
        UsageTelemetry.ActionOpened(action.Action, action.ReasonCode);
}
