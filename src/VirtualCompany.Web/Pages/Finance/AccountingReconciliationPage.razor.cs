using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AccountingReconciliationPage : FinancePageBase
{
    [Parameter] public Guid? TransactionId { get; set; }
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    private BankReconciliationWorkspaceResponse Workspace { get; set; } = new();
    private AdvancedReconciliationWorkspaceResponse AdvancedWorkspace { get; set; } = new();
    private AdvancedReconciliationGroupDetailResponse? SelectedAdvanced { get; set; }
    private BankReconciliationDetailResponse? Selected { get; set; }
    private IReadOnlyList<TreasurySourceSummaryResponse> RelatedTreasurySources { get; set; } = [];
    private TreasurySourceDetailResponse? SelectedTreasury { get; set; }
    private TreasuryPostingPreviewResponse? TreasuryPostingPreview { get; set; }
    private IReadOnlyList<AccountingAccountListItemResponse> Accounts { get; set; } = [];
    private IReadOnlyList<AccountingFiscalYearResponse> FiscalYears { get; set; } = [];
    private string? StateFilter { get; set; }
    private string? Search { get; set; }
    private Guid SelectedPaymentId { get; set; }
    private decimal MatchAmount { get; set; }
    private string ReviewReason { get; set; } = string.Empty;
    private string AdvancedDecisionReason { get; set; } = string.Empty;
    private string TreasuryReviewReason { get; set; } = string.Empty;
    private Guid CategorizationAccountId { get; set; }
    private string AdjustmentKind { get; set; } = "bank_fee";
    private decimal AdjustmentDebit { get; set; }
    private decimal AdjustmentCredit { get; set; }
    private string AdjustmentExplanation { get; set; } = string.Empty;
    private Guid ReclassificationAccountId { get; set; }
    private Guid ReclassificationPeriodId { get; set; }
    private bool IsWorkspaceLoading { get; set; }
    private bool IsDetailLoading { get; set; }
    private bool IsSubmitting { get; set; }
    private string? ActionError { get; set; }
    private string? ActionMessage { get; set; }
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private bool CanApproveAdvancedReconciliation => FinanceAccess.CanApproveInvoices(AccessState.MembershipRole);
    private BankReconciliationCandidatePaymentResponse? SelectedPayment =>
        Selected?.CandidatePayments.FirstOrDefault(x => x.PaymentId == SelectedPaymentId);
    private string LauraReconciliationAdvice => Selected?.State == "suspense"
        ? FinanceText["LauraSuspenseAdvice"]
        : Selected?.RemainingAmount > 0
            ? FinanceText["LauraUnmatchedAdvice"]
            : FinanceText["LauraTraceableAdvice"];

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId) return;
        await LoadAdvancedWorkspaceAsync(companyId);
        await LoadWorkspaceAsync(companyId);
        if (!TransactionId.HasValue && Workspace.Items.Count == 0) return;

        try
        {
            Accounts = await FinanceApiClient.GetAccountingAccountsAsync(companyId);
            FiscalYears = await FinanceApiClient.GetAccountingFiscalYearsAsync(companyId);
        }
        catch (FinanceApiException)
        {
            Accounts = [];
            FiscalYears = [];
            ActionError ??= FinanceText["ReconciliationAccountingSetupRequired"];
            return;
        }

        if (TransactionId.HasValue) await SelectAsync(TransactionId.Value, navigate: false);
        else if (Workspace.Items.Count > 0) await SelectAsync(Workspace.Items[0].BankTransactionId, navigate: false);
        if (AdvancedWorkspace.Groups.Count > 0) await SelectAdvancedAsync(AdvancedWorkspace.Groups[0].Id);
    }

    private async Task LoadAdvancedWorkspaceAsync(Guid companyId)
    {
        try { AdvancedWorkspace = await FinanceApiClient.ListAdvancedReconciliationAsync(companyId, search: Search) ?? new(); }
        catch (FinanceApiException ex) { ActionError = ex.Message; AdvancedWorkspace = new(); }
    }

    private async Task LoadWorkspaceAsync(Guid companyId)
    {
        IsWorkspaceLoading = true;
        try { Workspace = await FinanceApiClient.ListBankReconciliationAsync(companyId, StateFilter, Search) ?? new(); }
        catch (FinanceApiException ex) { ActionError = ex.Message; Workspace = new(); }
        finally { IsWorkspaceLoading = false; }
    }

    private async Task ApplyFiltersAsync()
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        await LoadAdvancedWorkspaceAsync(companyId);
        await LoadWorkspaceAsync(companyId);
        Selected = null;
        if (Workspace.Items.Count > 0) await SelectAsync(Workspace.Items[0].BankTransactionId, navigate: false);
    }

    private async Task ClearFiltersAsync()
    {
        StateFilter = null;
        Search = null;
        await ApplyFiltersAsync();
    }

    private async Task SelectAdvancedAsync(Guid groupId)
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        IsDetailLoading = true;
        ActionError = null;
        try
        {
            SelectedAdvanced = await FinanceApiClient.GetAdvancedReconciliationAsync(companyId, groupId);
            AdvancedDecisionReason = SelectedAdvanced?.Summary.RequiresApproval == true
                ? FinanceText["AdvancedMaterialReviewReason"]
                : string.Empty;
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsDetailLoading = false; }
    }

    private async Task AcceptAdvancedAsync()
    {
        if (!CanApproveAdvancedReconciliation || AccessState.CompanyId is not Guid companyId || SelectedAdvanced is null) return;
        if (string.IsNullOrWhiteSpace(AdvancedDecisionReason)) { ActionError = FinanceText["AdvancedDecisionReasonRequired"]; return; }
        IsSubmitting = true; ActionError = null;
        try
        {
            SelectedAdvanced = await FinanceApiClient.AcceptAdvancedReconciliationAsync(companyId, SelectedAdvanced.Summary.Id, new()
            {
                ExpectedVersion = SelectedAdvanced.Summary.Version,
                ExpectedRuleVersion = SelectedAdvanced.Summary.RuleVersion,
                DecisionReason = AdvancedDecisionReason.Trim()
            });
            ActionMessage = FinanceText["AdvancedReconciliationAccepted"];
            await LoadAdvancedWorkspaceAsync(companyId);
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsSubmitting = false; }
    }

    private async Task RejectAdvancedAsync()
    {
        if (!CanApproveAdvancedReconciliation || AccessState.CompanyId is not Guid companyId || SelectedAdvanced is null) return;
        if (string.IsNullOrWhiteSpace(AdvancedDecisionReason)) { ActionError = FinanceText["AdvancedDecisionReasonRequired"]; return; }
        IsSubmitting = true; ActionError = null;
        try
        {
            SelectedAdvanced = await FinanceApiClient.RejectAdvancedReconciliationAsync(companyId, SelectedAdvanced.Summary.Id, new()
            { ExpectedVersion = SelectedAdvanced.Summary.Version, DecisionReason = AdvancedDecisionReason.Trim() });
            ActionMessage = FinanceText["AdvancedReconciliationRejected"];
            await LoadAdvancedWorkspaceAsync(companyId);
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsSubmitting = false; }
    }

    private async Task ReverseAdvancedAsync()
    {
        if (!CanApproveAdvancedReconciliation || AccessState.CompanyId is not Guid companyId || SelectedAdvanced is null || ReclassificationPeriodId == Guid.Empty) return;
        if (string.IsNullOrWhiteSpace(AdvancedDecisionReason)) { ActionError = FinanceText["AdvancedDecisionReasonRequired"]; return; }
        IsSubmitting = true; ActionError = null;
        try
        {
            SelectedAdvanced = await FinanceApiClient.ReverseAdvancedReconciliationAsync(companyId, SelectedAdvanced.Summary.Id, new()
            {
                ExpectedVersion = SelectedAdvanced.Summary.Version,
                FiscalPeriodId = ReclassificationPeriodId,
                PostingDate = DateOnly.FromDateTime(DateTime.Today),
                Reason = AdvancedDecisionReason.Trim()
            });
            ActionMessage = FinanceText["AdvancedReconciliationReversed"];
            await LoadAdvancedWorkspaceAsync(companyId);
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsSubmitting = false; }
    }

    private async Task SelectAsync(Guid transactionId, bool navigate = true)
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        IsDetailLoading = true;
        ActionError = null;
        try
        {
            Selected = await FinanceApiClient.GetBankReconciliationDetailAsync(companyId, transactionId);
            var candidate = Selected?.CandidatePayments.FirstOrDefault();
            SelectedPaymentId = candidate?.PaymentId ?? Guid.Empty;
            MatchAmount = Math.Min(candidate?.AvailableAmount ?? 0m, Selected?.RemainingAmount ?? 0m);
            ReviewReason = Selected?.ReviewReason ?? string.Empty;
            CategorizationAccountId = Accounts.FirstOrDefault(x => x.IsPostingEnabled && !x.IsProtected)?.Id ?? Guid.Empty;
            ReclassificationAccountId = CategorizationAccountId;
            ReclassificationPeriodId = FiscalYears.SelectMany(x => x.Periods).FirstOrDefault(x => !x.IsClosed && !x.IsReportingLocked)?.Id ?? Guid.Empty;
            var treasury = await FinanceApiClient.ListTreasurySourcesAsync(companyId, bankTransactionId: transactionId);
            RelatedTreasurySources = treasury?.Items ?? [];
            SelectedTreasury = RelatedTreasurySources.Count == 0 ? null : await FinanceApiClient.GetTreasurySourceAsync(
                companyId, RelatedTreasurySources[0].SourceType, RelatedTreasurySources[0].Id);
            TreasuryPostingPreview = null;
            TreasuryReviewReason = string.Empty;
            if (navigate)
                Navigation.NavigateTo(FinanceRoutes.WithCompanyContext($"{FinanceRoutes.AccountingReconciliation}/{transactionId:D}", companyId));
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsDetailLoading = false; }
    }

    private Task HandleTransactionKeyAsync(KeyboardEventArgs args, Guid transactionId) =>
        args.Key is "Enter" or " " ? SelectAsync(transactionId) : Task.CompletedTask;

    private async Task SelectTreasuryAsync(TreasurySourceSummaryResponse source)
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        try
        {
            SelectedTreasury = await FinanceApiClient.GetTreasurySourceAsync(companyId, source.SourceType, source.Id);
            TreasuryPostingPreview = null;
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
    }

    private async Task PreviewTreasuryAsync()
    {
        if (!CanManageAccounting || AccessState.CompanyId is not Guid companyId || SelectedTreasury is null || ReclassificationPeriodId == Guid.Empty) return;
        IsSubmitting = true; ActionError = null;
        try
        {
            TreasuryPostingPreview = await FinanceApiClient.PreviewTreasuryPostingAsync(companyId,
                SelectedTreasury.Summary.SourceType, SelectedTreasury.Summary.Id, new()
                { FiscalPeriodId = ReclassificationPeriodId, PostingDate = DateOnly.FromDateTime(DateTime.Today) });
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsSubmitting = false; }
    }

    private async Task PostTreasuryAsync()
    {
        if (!CanApproveAdvancedReconciliation || AccessState.CompanyId is not Guid companyId || SelectedTreasury is null ||
            ReclassificationPeriodId == Guid.Empty || !SelectedTreasury.AllowedActions.CanPost) return;
        IsSubmitting = true; ActionError = null; ActionMessage = null;
        try
        {
            SelectedTreasury = await FinanceApiClient.PostTreasurySourceAsync(companyId,
                SelectedTreasury.Summary.SourceType, SelectedTreasury.Summary.Id, new()
                { FiscalPeriodId = ReclassificationPeriodId, PostingDate = DateOnly.FromDateTime(DateTime.Today), ExpectedVersion = SelectedTreasury.Summary.Version });
            TreasuryPostingPreview = SelectedTreasury.PostingPreview;
            ActionMessage = FinanceText["TreasuryPostedMessage"];
            await LoadWorkspaceAsync(companyId);
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsSubmitting = false; }
    }

    private async Task ReverseTreasuryAsync()
    {
        if (!CanApproveAdvancedReconciliation || AccessState.CompanyId is not Guid companyId || SelectedTreasury is null ||
            ReclassificationPeriodId == Guid.Empty || !SelectedTreasury.AllowedActions.CanReverse) return;
        if (string.IsNullOrWhiteSpace(TreasuryReviewReason)) { ActionError = FinanceText["TreasuryReversalReasonRequired"]; return; }
        IsSubmitting = true; ActionError = null; ActionMessage = null;
        try
        {
            SelectedTreasury = await FinanceApiClient.ReverseTreasurySourceAsync(companyId,
                SelectedTreasury.Summary.SourceType, SelectedTreasury.Summary.Id, new()
                { FiscalPeriodId = ReclassificationPeriodId, PostingDate = DateOnly.FromDateTime(DateTime.Today), ExpectedVersion = SelectedTreasury.Summary.Version, Reason = TreasuryReviewReason.Trim() });
            TreasuryPostingPreview = null; ActionMessage = FinanceText["TreasuryReversedMessage"];
            await LoadWorkspaceAsync(companyId);
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsSubmitting = false; }
    }

    private Task MatchPaymentAsync() => SubmitReconciliationAsync("payment", SelectedPaymentId == Guid.Empty
        ? []
        : [new ReconcileBankTransactionPaymentApiRequest { PaymentId = SelectedPaymentId, AllocatedAmount = MatchAmount }]);
    private Task CategorizeAsync() => SubmitReconciliationAsync("categorization", []);
    private Task PostSuspenseAsync() => SubmitReconciliationAsync("suspense", []);
    private Task LeaveUnmatchedAsync() => SubmitReconciliationAsync("leave_unmatched", []);

    private async Task SubmitReconciliationAsync(string mode, List<ReconcileBankTransactionPaymentApiRequest> payments)
    {
        if (!CanManageAccounting || AccessState.CompanyId is not Guid companyId || Selected is null) return;
        if (mode != "payment" && string.IsNullOrWhiteSpace(ReviewReason))
        {
            ActionError = FinanceText["HandlingReasonRequired"];
            return;
        }
        if ((AdjustmentDebit > 0m || AdjustmentCredit > 0m) &&
            (AdjustmentDebit > 0m == (AdjustmentCredit > 0m) || string.IsNullOrWhiteSpace(AdjustmentExplanation)))
        {
            ActionError = FinanceText["AdjustmentValidation"];
            return;
        }
        IsSubmitting = true;
        ActionError = null;
        ActionMessage = null;
        try
        {
            await FinanceApiClient.ReconcileBankTransactionAsync(companyId, Selected.Transaction.Id, new()
            {
                Payments = payments,
                ExpectedSourceVersion = Selected.SourceVersion,
                HandlingMode = mode,
                ReviewReason = string.IsNullOrWhiteSpace(ReviewReason) ? null : ReviewReason.Trim(),
                CategorizationFinanceAccountId = mode == "categorization" ? CategorizationAccountId : null,
                Adjustments = BuildAdjustments(),
                IdempotencyKey = $"bank-ui:{Selected.Transaction.Id:N}:{Selected.SourceVersion}:{mode}"
            });
            ActionMessage = mode switch
            {
                "payment" => FinanceText["PaymentMatchSaved"],
                "suspense" => FinanceText["SuspensePostedMessage"],
                _ => FinanceText["TransactionUnmatchedMessage"]
            };
            await LoadWorkspaceAsync(companyId);
            await SelectAsync(Selected.Transaction.Id, navigate: false);
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsSubmitting = false; }
    }

    private List<BankReconciliationAdjustmentApiRequest> BuildAdjustments() =>
        AdjustmentDebit <= 0m && AdjustmentCredit <= 0m
            ? []
            : [new BankReconciliationAdjustmentApiRequest
            {
                Kind = AdjustmentKind,
                DebitAmount = AdjustmentDebit,
                CreditAmount = AdjustmentCredit,
                Explanation = AdjustmentExplanation.Trim()
            }];

    private async Task ReclassifyAsync()
    {
        if (!CanManageAccounting || AccessState.CompanyId is not Guid companyId || Selected is null) return;
        if (ReclassificationAccountId == Guid.Empty || ReclassificationPeriodId == Guid.Empty || string.IsNullOrWhiteSpace(ReviewReason))
        {
            ActionError = FinanceText["ReclassificationValidation"];
            return;
        }
        IsSubmitting = true;
        ActionError = null;
        try
        {
            Selected = await FinanceApiClient.ReclassifyBankSuspenseAsync(companyId, Selected.Transaction.Id, new()
            {
                TargetFinanceAccountId = ReclassificationAccountId,
                FiscalPeriodId = ReclassificationPeriodId,
                PostingDate = DateOnly.FromDateTime(DateTime.Today),
                Reason = ReviewReason.Trim(),
                ExpectedSourceVersion = Selected.SourceVersion,
                IdempotencyKey = $"bank-reclassify:{Selected.Transaction.Id:N}:{Selected.SourceVersion}"
            });
            ActionMessage = FinanceText["SuspenseCorrectedMessage"];
            await LoadWorkspaceAsync(companyId);
        }
        catch (FinanceApiException ex) { ActionError = ex.Message; }
        finally { IsSubmitting = false; }
    }

    private int Count(string state) => Workspace.StateCounts.GetValueOrDefault(state);
    private string Money(decimal value, string currency) => string.Format(CultureInfo.CurrentCulture, "{0:N2} {1}", value, currency);
    private string JournalHref(Guid journalId) => FinanceRoutes.WithCompanyContext($"{FinanceRoutes.AccountingJournal}?journalId={journalId:D}", AccessState.CompanyId);
    private string PaymentHref(Guid paymentId) => FinanceRoutes.BuildPaymentDetailPath(paymentId, AccessState.CompanyId);
    private string InvoiceHref(Guid invoiceId) => FinanceRoutes.BuildInvoiceDetailPath(invoiceId, AccessState.CompanyId);
    private string BillHref(Guid billId) => FinanceRoutes.BuildBillDetailPath(billId, AccessState.CompanyId);
    private string StateLabel(string state) => FinanceText[state switch
    {
        "partial" => "PartiallyMatched",
        "matched" => "ReadyToPost",
        "posted" => "Posted",
        "suspense" => "SuspenseFollowUp",
        "conflict" => "Conflict",
        "correction" => "Corrected",
        _ => "Unmatched"
    }];
    private string AdvancedStatusLabel(string status) => FinanceText[status switch
    {
        "accepted" => "AdvancedAccepted",
        "rejected" => "AdvancedRejected",
        "reversed" => "AdvancedReversed",
        "conflict" => "Conflict",
        _ => "NeedsReview"
    }];
    private string AdvancedNodeLabel(string type) => FinanceText[type switch
    {
        "bank_transaction" => "AdvancedBankRow",
        "payment" => "AdvancedPayment",
        "invoice" => "AdvancedInvoice",
        "bill" => "AdvancedBill",
        "residual" => "AdvancedResidual",
        _ => "AdvancedAdjustment"
    }];
    private string TreasuryStatusLabel(string status) => FinanceText[status switch
    {
        "in_transit" => "TreasuryInTransit",
        "awaiting_bank_evidence" => "TreasuryAwaitingBankEvidence",
        "awaiting_approval" => "WaitingForApproval",
        "ready_to_post" => "ReadyToPost",
        "posted" => "Posted",
        "reversed" => "AdvancedReversed",
        _ => "NeedsReview"
    }];
    private string TreasuryTypeLabel(string type) => FinanceText[type switch
    {
        "account_transfer" => "TreasuryInternalTransfer",
        "bank_adjustment" => "TreasuryBankChargeInterest",
        "card_settlement" => "TreasuryCardSettlement",
        _ => "TreasuryPayoutSettlement"
    }];
}
