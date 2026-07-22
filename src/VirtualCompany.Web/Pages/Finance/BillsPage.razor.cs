using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class BillsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [Inject] private ILogger<BillsPage> Logger { get; set; } = default!;

    [Parameter]
    public Guid? BillId { get; set; }

    private IReadOnlyList<FinanceBillResponse> Bills { get; set; } = [];
    private FinanceBillDetailResponse? SelectedBill { get; set; }
    private bool IsListLoading { get; set; }
    private bool IsDetailLoading { get; set; }
    private bool IsPaymentProposalSubmitting { get; set; }
    private bool IsPaymentExportSubmitting { get; set; }
    private bool IsSourceDocumentAttachmentSubmitting { get; set; }
    private bool IsDraftUpdateSubmitting { get; set; }
    private bool IsDraftBookkeepingSubmitting { get; set; }
    private bool IsPaidExpensePostingSubmitting { get; set; }
    private bool IsCancellationSubmitting { get; set; }
    private bool IsCreditNoteSubmitting { get; set; }
    private bool IsEnrichmentSubmitting { get; set; }
    private bool IsEnrichmentSyncSubmitting { get; set; }
    private bool IsReconciliationSubmitting { get; set; }
    private string? ListErrorMessage { get; set; }
    private string? DetailErrorMessage { get; set; }
    private string? PaymentProposalMessage { get; set; }
    private string? PaymentExportMessage { get; set; }
    private string? SourceDocumentAttachmentMessage { get; set; }
    private string? DraftActionMessage { get; set; }
    private string? CorrectionActionMessage { get; set; }
    private string? EnrichmentMessage { get; set; }
    private string ActiveBillFilter { get; set; } = BillLifecycleFilters.All;

    private IReadOnlyList<FinanceBillResponse> FilteredBills =>
        Bills.Where(BillMatchesActiveFilter).ToList();
    private bool IsListEmpty => !IsListLoading && string.IsNullOrWhiteSpace(ListErrorMessage) && FilteredBills.Count == 0;
    private IReadOnlyList<BillListItemViewModel> BillItems =>
        FilteredBills.Select(bill => ToListItem(bill, BillId == bill.Id)).ToList();
    private IReadOnlyList<BillFilterViewModel> BillFilters => BuildBillFilters();
    private BillDetailViewModel? SelectedBillDisplay =>
        SelectedBill is null
            ? null
            : ToDetailViewModel(
                SelectedBill,
                Bills.FirstOrDefault(bill => bill.Id == SelectedBill.Id)?.PaymentContext);
    private string DashboardHref => AccessState.CompanyId is Guid companyId ? $"/dashboard?companyId={companyId:D}" : "/dashboard";

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Bills = [];
        SelectedBill = null;
        ListErrorMessage = null;
        DetailErrorMessage = null;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadBillsAsync(companyId);
        if (BillId is Guid billId)
        {
            await LoadDetailAsync(companyId, billId);
        }
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadBillsAsync(companyId);
        if (BillId is Guid billId)
        {
            await LoadDetailAsync(companyId, billId);
        }
    }

    private async Task LoadBillsAsync(Guid companyId)
    {
        IsListLoading = true;
        ListErrorMessage = null;

        try
        {
            Bills = await FinanceApiClient.GetBillsAsync(companyId, 200);
        }
        catch (FinanceApiException ex)
        {
            Bills = [];
            ListErrorMessage = ex.Message;
        }
        finally
        {
            IsListLoading = false;
        }
    }

    private async Task LoadDetailAsync(Guid companyId, Guid billId)
    {
        IsDetailLoading = true;
        DetailErrorMessage = null;

        try
        {
            SelectedBill = await FinanceApiClient.GetBillDetailAsync(companyId, billId);
            if (SelectedBill is null)
            {
                DetailErrorMessage = "The selected bill could not be found for this company.";
            }
        }
        catch (FinanceApiException ex)
        {
            SelectedBill = null;
            DetailErrorMessage = ex.Message;
        }
        finally
        {
            IsDetailLoading = false;
        }
    }

    private string BuildBillHref(Guid billId) => FinanceRoutes.BuildBillDetailPath(billId, AccessState.CompanyId);
    private string BuildDocumentHref(Guid documentId) => $"/api/companies/{AccessState.CompanyId}/documents/{documentId}";
    private string BuildTransactionHref(Guid transactionId) =>
        FinanceRoutes.BuildTransactionDetailPath(transactionId, AccessState.CompanyId);

    private void SetBillFilter(string filter)
    {
        ActiveBillFilter = string.IsNullOrWhiteSpace(filter) ? BillLifecycleFilters.All : filter;
    }

    private string FormatCurrency(decimal amount, string currency) => LocalMoney.Format(amount, currency);

    private string FormatFriendlyDate(DateTime value) =>
        value == default ? "Not available" : LocalDateTime.Date(DateOnly.FromDateTime(value));

    private string FormatStatusLabel(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Unknown"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .ToLowerInvariant());

    private IReadOnlyList<BillFilterViewModel> BuildBillFilters() =>
        [
            BuildFilter(BillLifecycleFilters.All, "All", Bills.Count),
            BuildFilter(BillLifecycleFilters.DueSoon, "Due soon", Bills.Count(IsDueSoonBill)),
            BuildFilter(BillLifecycleFilters.Overdue, "Overdue", Bills.Count(IsOverdueBill)),
            BuildFilter(BillLifecycleFilters.Unpaid, "Unpaid", Bills.Count(IsOpenUnpaidBill)),
            BuildFilter(BillLifecycleFilters.Paid, "Paid", Bills.Count(IsPaidBill)),
            BuildFilter(BillLifecycleFilters.Cancelled, "Cancelled", Bills.Count(IsCancelledBill))
        ];

    private BillFilterViewModel BuildFilter(string value, string label, int count) =>
        new(value, label, count, string.Equals(ActiveBillFilter, value, StringComparison.OrdinalIgnoreCase));

    private bool BillMatchesActiveFilter(FinanceBillResponse bill) =>
        NormalizeStatusToken(ActiveBillFilter) switch
        {
            BillLifecycleFilters.DueSoon => IsDueSoonBill(bill),
            BillLifecycleFilters.Overdue => IsOverdueBill(bill),
            BillLifecycleFilters.Unpaid => IsOpenUnpaidBill(bill),
            BillLifecycleFilters.Paid => IsPaidBill(bill),
            BillLifecycleFilters.Cancelled => IsCancelledBill(bill),
            _ => true
        };

    private BillListItemViewModel ToListItem(FinanceBillResponse bill, bool isSelected)
    {
        var paymentSummary = BuildPaymentSummary(bill.PaymentContext, bill.ProviderStatus, bill.Amount, bill.Currency);
        var effectiveSettlementStatus = ResolveEffectiveSettlementStatus(bill.SettlementStatus, bill.ProviderStatus, paymentSummary);
        var effectiveFallbackStatus = ResolveEffectiveFallbackStatus(bill.Status, bill.ProviderStatus, paymentSummary);
        var status = ResolveStatusPresentation(
            bill.PostingStatus,
            effectiveSettlementStatus,
            bill.DueStatus,
            bill.DocumentKind,
            effectiveFallbackStatus,
            paymentSummary?.IsPartiallyPaid == true,
            paymentSummary?.IsFullyPaid == true);
        var proposalStatus = ResolvePaymentProposalPresentation(bill.PaymentProposal);
        var correctionStatus = ResolveCorrectionListPresentation(bill.CorrectionActions);
        var enrichmentStatus = ResolveEnrichmentListPresentation(bill.EnrichmentAction);
        var secondaryStatus = enrichmentStatus ?? correctionStatus ?? proposalStatus;
        return new BillListItemViewModel(
            bill.Id,
            string.IsNullOrWhiteSpace(bill.BillNumber) ? "Bill" : bill.BillNumber,
            string.IsNullOrWhiteSpace(bill.CounterpartyName) ? "Supplier not available" : bill.CounterpartyName,
            FormatCurrency(bill.Amount, bill.Currency),
            FormatFriendlyDate(bill.ReceivedUtc),
            FormatFriendlyDate(bill.DueUtc),
            $"Received {FormatFriendlyDate(bill.ReceivedUtc)} · Due {FormatFriendlyDate(bill.DueUtc)}",
            status.Label,
            status.Tone,
            status.Tone,
            secondaryStatus?.Label ?? (paymentSummary?.IsPartiallyPaid == true ? "Needs review" : null),
            secondaryStatus?.Tone ?? (paymentSummary?.IsPartiallyPaid == true ? "danger" : "neutral"),
            isSelected);
    }

    private BillDetailViewModel ToDetailViewModel(
        FinanceBillDetailResponse bill,
        FinanceTransactionPaymentContextResponse? listPaymentContext)
    {
        var paymentContext = SelectPaymentContext(bill.PaymentContext, listPaymentContext);
        var paymentSummary = BuildPaymentSummary(paymentContext, bill.ProviderStatus, bill.Amount, bill.Currency);
        var effectiveSettlementStatus = ResolveEffectiveSettlementStatus(bill.SettlementStatus, bill.ProviderStatus, paymentSummary);
        var effectiveFallbackStatus = ResolveEffectiveFallbackStatus(bill.Status, bill.ProviderStatus, paymentSummary);
        var status = ResolveStatusPresentation(
            bill.PostingStatus,
            effectiveSettlementStatus,
            bill.DueStatus,
            bill.DocumentKind,
            effectiveFallbackStatus,
            paymentSummary?.IsPartiallyPaid == true,
            paymentSummary?.IsFullyPaid == true);
        var needsPaymentReview = paymentSummary?.IsPartiallyPaid == true;
        var proposal = BuildPaymentProposalViewModel(bill.PaymentProposal);
        var sourceDocumentAttachment = BuildSourceDocumentAttachmentViewModel(bill.SourceDocumentAttachment, bill.LinkedDocument);
        var draftAction = BuildDraftActionViewModel(
            bill.DraftAction,
            bill.PostingStatus,
            effectiveSettlementStatus,
            bill.DocumentKind,
            effectiveFallbackStatus,
            paymentSummary?.RemainingAmount ?? bill.Amount);
        var correctionActions = BuildCorrectionActionsViewModel(
            bill.CorrectionActions,
            bill.PostingStatus,
            effectiveSettlementStatus,
            bill.DocumentKind,
            effectiveFallbackStatus);
        var enrichment = BuildEnrichmentViewModel(bill.EnrichmentAction);
        var canRequestPaymentProposal = CanRequestPaymentProposalAfter(proposal) &&
            IsPayableSupplierBill(
                bill.PostingStatus,
                effectiveSettlementStatus,
                bill.DocumentKind,
                effectiveFallbackStatus,
                paymentSummary?.RemainingAmount ?? bill.Amount);
        var paidExpensePosting = BuildPaidExpensePostingViewModel(bill.PaidExpensePostingAvailability);
        return new BillDetailViewModel(
            string.IsNullOrWhiteSpace(bill.BillNumber) ? "Bill" : bill.BillNumber,
            string.IsNullOrWhiteSpace(bill.CounterpartyName) ? "Supplier not available" : bill.CounterpartyName,
            FormatCurrency(bill.Amount, bill.Currency),
            FormatFriendlyDate(bill.ReceivedUtc),
            FormatFriendlyDate(bill.DueUtc),
            status.Label,
            status.Tone,
            ResolveApprovalStatus(bill.Status),
            needsPaymentReview
                ? paymentSummary!.ReviewReason
                : bill.AgentInsights.Count == 0
                    ? "No warnings are linked to this bill."
                : $"{bill.AgentInsights.Count} finance note{(bill.AgentInsights.Count == 1 ? string.Empty : "s")} linked to this bill.",
            FormatDocumentKind(bill.DocumentKind),
            BuildSourceDetails(bill.ProviderStatus, bill.Currency),
            paymentSummary,
            proposal,
            sourceDocumentAttachment,
            draftAction,
            correctionActions,
            enrichment,
            canRequestPaymentProposal,
            paidExpensePosting,
            needsPaymentReview ? "Needs review" : null,
            needsPaymentReview ? "danger" : "neutral",
            bill.RelatedTransactions.Select(ToRelatedTransactionViewModel).ToList());
    }

    private async Task SuggestEnrichmentAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedBill is null)
        {
            EnrichmentMessage = "Laura enrichment could not start because the selected bill context is missing. Reload and try again.";
            return;
        }

        IsEnrichmentSubmitting = true;
        EnrichmentMessage = null;
        try
        {
            Logger.LogInformation(
                "Supplier bill enrichment suggestion request started from UI. CompanyId: {CompanyId}. BillId: {BillId}. BillNumber: {BillNumber}.",
                companyId,
                SelectedBill.Id,
                SelectedBill.BillNumber);
            var action = await FinanceApiClient.SuggestSupplierInvoiceEnrichmentAsync(companyId, SelectedBill.Id);
            SelectedBill.EnrichmentAction = action;
            EnrichmentMessage = string.IsNullOrWhiteSpace(action.ResponseSummary)
                ? "Laura enrichment suggestions were sent for approval."
                : action.ResponseSummary;
            await LoadBillsAsync(companyId);
            await LoadDetailAsync(companyId, SelectedBill.Id);
        }
        catch (FinanceApiException ex)
        {
            EnrichmentMessage = ex.Message;
            Logger.LogWarning(ex, "Supplier bill enrichment suggestion request failed in UI. CompanyId: {CompanyId}. BillId: {BillId}.", companyId, SelectedBill.Id);
        }
        finally
        {
            IsEnrichmentSubmitting = false;
        }
    }

    private async Task SyncEnrichmentAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedBill is null)
        {
            EnrichmentMessage = "Fortnox enrichment sync could not start because the selected bill context is missing. Reload and try again.";
            return;
        }

        IsEnrichmentSyncSubmitting = true;
        EnrichmentMessage = null;
        try
        {
            Logger.LogInformation(
                "Supplier bill enrichment sync request started from UI. CompanyId: {CompanyId}. BillId: {BillId}. CurrentStatus: {Status}.",
                companyId,
                SelectedBill.Id,
                SelectedBill.EnrichmentAction?.Status);
            var action = await FinanceApiClient.SyncSupplierInvoiceEnrichmentAsync(companyId, SelectedBill.Id);
            SelectedBill.EnrichmentAction = action;
            EnrichmentMessage = string.IsNullOrWhiteSpace(action.ResponseSummary)
                ? "Fortnox enrichment sync state was recorded."
                : action.ResponseSummary;
            await LoadBillsAsync(companyId);
            await LoadDetailAsync(companyId, SelectedBill.Id);
        }
        catch (FinanceApiException ex)
        {
            EnrichmentMessage = ex.Message;
            Logger.LogWarning(ex, "Supplier bill enrichment sync request failed in UI. CompanyId: {CompanyId}. BillId: {BillId}.", companyId, SelectedBill.Id);
        }
        finally
        {
            IsEnrichmentSyncSubmitting = false;
        }
    }

    private async Task ReconcileSupplierInvoiceAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedBill is null)
        {
            EnrichmentMessage = "Reconciliation could not start because the selected bill context is missing. Reload and try again.";
            return;
        }

        IsReconciliationSubmitting = true;
        EnrichmentMessage = null;
        try
        {
            var action = await FinanceApiClient.ReconcileSupplierInvoiceAsync(companyId, SelectedBill.Id);
            SelectedBill.EnrichmentAction = action;
            EnrichmentMessage = action.ReconciliationWarnings.Count == 0
                ? "No reconciliation warnings were found."
                : "Reconciliation warnings were refreshed.";
            await LoadBillsAsync(companyId);
            await LoadDetailAsync(companyId, SelectedBill.Id);
        }
        catch (FinanceApiException ex)
        {
            EnrichmentMessage = ex.Message;
            Logger.LogWarning(ex, "Supplier bill reconciliation request failed in UI. CompanyId: {CompanyId}. BillId: {BillId}.", companyId, SelectedBill.Id);
        }
        finally
        {
            IsReconciliationSubmitting = false;
        }
    }

    private async Task UpdateFortnoxDraftAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedBill is null)
        {
            Logger.LogWarning(
                "Supplier bill Fortnox draft update click ignored because context is missing. CompanyId: {CompanyId}. HasSelectedBill: {HasSelectedBill}.",
                AccessState.CompanyId,
                SelectedBill is not null);
            DraftActionMessage = "Fortnox draft update could not start because the selected bill context is missing. Reload and try again.";
            return;
        }

        Logger.LogInformation(
            "Supplier bill Fortnox draft update request started from UI. CompanyId: {CompanyId}. BillId: {BillId}. BillNumber: {BillNumber}. PostingStatus: {PostingStatus}. DraftActionStatus: {DraftActionStatus}.",
            companyId,
            SelectedBill.Id,
            SelectedBill.BillNumber,
            SelectedBill.PostingStatus,
            SelectedBill.DraftAction?.Status);

        IsDraftUpdateSubmitting = true;
        DraftActionMessage = null;
        try
        {
            var action = await FinanceApiClient.UpdateSupplierBillFortnoxDraftAsync(companyId, SelectedBill.Id);
            SelectedBill.DraftAction = action;
            DraftActionMessage = string.IsNullOrWhiteSpace(action.ResponseSummary)
                ? "Fortnox draft update state was recorded."
                : action.ResponseSummary;
            Logger.LogInformation(
                "Supplier bill Fortnox draft update request completed from UI. CompanyId: {CompanyId}. BillId: {BillId}. ActionId: {ActionId}. Status: {Status}. ProviderKey: {ProviderKey}.",
                companyId,
                SelectedBill.Id,
                action.Id,
                action.Status,
                action.ProviderKey);
            await LoadBillsAsync(companyId);
            await LoadDetailAsync(companyId, SelectedBill.Id);
        }
        catch (FinanceApiException ex)
        {
            DraftActionMessage = ex.Message;
            Logger.LogWarning(
                ex,
                "Supplier bill Fortnox draft update request failed in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        catch (Exception ex)
        {
            DraftActionMessage = "Fortnox draft update failed before the request completed. Check the logs for details.";
            Logger.LogError(
                ex,
                "Supplier bill Fortnox draft update request failed unexpectedly in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        finally
        {
            IsDraftUpdateSubmitting = false;
        }
    }

    private async Task BookkeepFortnoxDraftAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedBill is null)
        {
            Logger.LogWarning(
                "Supplier bill Fortnox bookkeeping click ignored because context is missing. CompanyId: {CompanyId}. HasSelectedBill: {HasSelectedBill}.",
                AccessState.CompanyId,
                SelectedBill is not null);
            DraftActionMessage = "Fortnox bookkeeping could not start because the selected bill context is missing. Reload and try again.";
            return;
        }

        Logger.LogInformation(
            "Supplier bill Fortnox bookkeeping request started from UI. CompanyId: {CompanyId}. BillId: {BillId}. BillNumber: {BillNumber}. PostingStatus: {PostingStatus}. DraftActionStatus: {DraftActionStatus}.",
            companyId,
            SelectedBill.Id,
            SelectedBill.BillNumber,
            SelectedBill.PostingStatus,
            SelectedBill.DraftAction?.Status);

        IsDraftBookkeepingSubmitting = true;
        DraftActionMessage = null;
        try
        {
            var action = await FinanceApiClient.BookkeepSupplierBillFortnoxDraftAsync(companyId, SelectedBill.Id);
            SelectedBill.DraftAction = action;
            DraftActionMessage = string.IsNullOrWhiteSpace(action.ResponseSummary)
                ? "Fortnox bookkeeping state was recorded."
                : action.ResponseSummary;
            Logger.LogInformation(
                "Supplier bill Fortnox bookkeeping request completed from UI. CompanyId: {CompanyId}. BillId: {BillId}. ActionId: {ActionId}. Status: {Status}. ProviderKey: {ProviderKey}.",
                companyId,
                SelectedBill.Id,
                action.Id,
                action.Status,
                action.ProviderKey);
            await LoadBillsAsync(companyId);
            await LoadDetailAsync(companyId, SelectedBill.Id);
        }
        catch (FinanceApiException ex)
        {
            DraftActionMessage = ex.Message;
            Logger.LogWarning(
                ex,
                "Supplier bill Fortnox bookkeeping request failed in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        catch (Exception ex)
        {
            DraftActionMessage = "Fortnox bookkeeping failed before the request completed. Check the logs for details.";
            Logger.LogError(
                ex,
                "Supplier bill Fortnox bookkeeping request failed unexpectedly in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        finally
        {
            IsDraftBookkeepingSubmitting = false;
        }
    }

    private async Task PostPaidSupplierBillExpenseAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedBill is null)
        {
            Logger.LogWarning(
                "Paid supplier bill expense posting click ignored because context is missing. CompanyId: {CompanyId}. HasSelectedBill: {HasSelectedBill}.",
                AccessState.CompanyId,
                SelectedBill is not null);
            DraftActionMessage = "Laura could not post this expense because the selected bill context is missing. Reload and try again.";
            return;
        }

        Logger.LogInformation(
            "Paid supplier bill expense posting request started from UI. CompanyId: {CompanyId}. BillId: {BillId}. BillNumber: {BillNumber}. PostingStatus: {PostingStatus}. DraftActionStatus: {DraftActionStatus}.",
            companyId,
            SelectedBill.Id,
            SelectedBill.BillNumber,
            SelectedBill.PostingStatus,
            SelectedBill.DraftAction?.Status);

        IsPaidExpensePostingSubmitting = true;
        DraftActionMessage = null;
        try
        {
            var posting = await FinanceApiClient.PostPaidSupplierBillExpenseAsync(companyId, SelectedBill.Id);
            SelectedBill.DraftAction = posting.DraftAction;
            DraftActionMessage = string.IsNullOrWhiteSpace(posting.Summary)
                ? "Laura recorded the expense posting state."
                : posting.Summary;
            Logger.LogInformation(
                "Paid supplier bill expense posting request completed from UI. CompanyId: {CompanyId}. BillId: {BillId}. DraftActionId: {DraftActionId}. Status: {Status}. Posted: {Posted}.",
                companyId,
                SelectedBill.Id,
                posting.DraftActionId,
                posting.Status,
                posting.Posted);
            await LoadBillsAsync(companyId);
            await LoadDetailAsync(companyId, SelectedBill.Id);
        }
        catch (FinanceApiException ex)
        {
            DraftActionMessage = ex.Message;
            Logger.LogWarning(
                ex,
                "Paid supplier bill expense posting request failed in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        catch (Exception ex)
        {
            DraftActionMessage = "Laura could not post the expense before the request completed. Check the logs for details.";
            Logger.LogError(
                ex,
                "Paid supplier bill expense posting request failed unexpectedly in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        finally
        {
            IsPaidExpensePostingSubmitting = false;
        }
    }

    private async Task AttachSourceDocumentAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedBill is null)
        {
            Logger.LogWarning(
                "Supplier bill source document attachment click ignored because context is missing. CompanyId: {CompanyId}. HasSelectedBill: {HasSelectedBill}.",
                AccessState.CompanyId,
                SelectedBill is not null);
            SourceDocumentAttachmentMessage = "Source document attachment could not start because the selected bill context is missing. Reload and try again.";
            return;
        }

        Logger.LogInformation(
            "Supplier bill source document attachment request started from UI. CompanyId: {CompanyId}. BillId: {BillId}. BillNumber: {BillNumber}. CurrentStatus: {Status}. HasLinkedDocument: {HasLinkedDocument}.",
            companyId,
            SelectedBill.Id,
            SelectedBill.BillNumber,
            SelectedBill.SourceDocumentAttachment?.Status,
            SelectedBill.LinkedDocument.Document is not null);

        IsSourceDocumentAttachmentSubmitting = true;
        SourceDocumentAttachmentMessage = null;
        try
        {
            var attachment = await FinanceApiClient.AttachSupplierBillSourceDocumentAsync(companyId, SelectedBill.Id);
            SelectedBill.SourceDocumentAttachment = attachment;
            SourceDocumentAttachmentMessage = string.IsNullOrWhiteSpace(attachment.ResponseSummary)
                ? "Source document attachment state was recorded."
                : attachment.ResponseSummary;
            Logger.LogInformation(
                "Supplier bill source document attachment request completed from UI. CompanyId: {CompanyId}. BillId: {BillId}. AttachmentId: {AttachmentId}. Status: {Status}. ProviderKey: {ProviderKey}.",
                companyId,
                SelectedBill.Id,
                attachment.Id,
                attachment.Status,
                attachment.ProviderKey);
            await LoadBillsAsync(companyId);
            await LoadDetailAsync(companyId, SelectedBill.Id);
        }
        catch (FinanceApiException ex)
        {
            SourceDocumentAttachmentMessage = ex.Message;
            Logger.LogWarning(
                ex,
                "Supplier bill source document attachment request failed in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        catch (Exception ex)
        {
            SourceDocumentAttachmentMessage = "Source document attachment failed before the request completed. Check the logs for details.";
            Logger.LogError(
                ex,
                "Supplier bill source document attachment request failed unexpectedly in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        finally
        {
            IsSourceDocumentAttachmentSubmitting = false;
        }
    }

    private async Task RequestPaymentProposalAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedBill is null)
        {
            Logger.LogWarning(
                "Supplier bill payment proposal click ignored because context is missing. CompanyId: {CompanyId}. HasSelectedBill: {HasSelectedBill}.",
                AccessState.CompanyId,
                SelectedBill is not null);
            PaymentProposalMessage = "Payment approval could not start because the selected bill context is missing. Reload and try again.";
            return;
        }

        Logger.LogInformation(
            "Supplier bill payment proposal request started from UI. CompanyId: {CompanyId}. BillId: {BillId}. BillNumber: {BillNumber}. Amount: {Amount}. SettlementStatus: {SettlementStatus}. PaidAmount: {PaidAmount}. RemainingAmount: {RemainingAmount}.",
            companyId,
            SelectedBill.Id,
            SelectedBill.BillNumber,
            SelectedBill.Amount,
            SelectedBill.SettlementStatus,
            SelectedBill.PaymentContext?.PaidAmount,
            SelectedBill.PaymentContext?.RemainingAmount);

        IsPaymentProposalSubmitting = true;
        PaymentProposalMessage = null;
        try
        {
            var proposal = await FinanceApiClient.RequestSupplierBillPaymentProposalAsync(companyId, SelectedBill.Id);
            SelectedBill.PaymentProposal = proposal;
            PaymentProposalMessage = "Payment proposal sent for approval. No payment was initiated.";
            Logger.LogInformation(
                "Supplier bill payment proposal request completed from UI. CompanyId: {CompanyId}. BillId: {BillId}. ProposalId: {ProposalId}. Status: {Status}. Amount: {Amount}. ApprovalRequestId: {ApprovalRequestId}.",
                companyId,
                SelectedBill.Id,
                proposal.Id,
                proposal.Status,
                proposal.Amount,
                proposal.ApprovalRequestId);
            await LoadBillsAsync(companyId);
            await LoadDetailAsync(companyId, SelectedBill.Id);
        }
        catch (FinanceApiException ex)
        {
            PaymentProposalMessage = ex.Message;
            Logger.LogWarning(
                ex,
                "Supplier bill payment proposal request failed in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        catch (Exception ex)
        {
            PaymentProposalMessage = "Payment approval failed before the request completed. Check the logs for details.";
            Logger.LogError(
                ex,
                "Supplier bill payment proposal request failed unexpectedly in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        finally
        {
            IsPaymentProposalSubmitting = false;
        }
    }

    private async Task ExportPaymentInstructionAsync(string exportMode)
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedBill is null)
        {
            Logger.LogWarning(
                "Supplier bill payment export click ignored because context is missing. CompanyId: {CompanyId}. HasSelectedBill: {HasSelectedBill}.",
                AccessState.CompanyId,
                SelectedBill is not null);
            PaymentExportMessage = "Payment export could not start because the selected bill context is missing. Reload and try again.";
            return;
        }

        Logger.LogInformation(
            "Supplier bill payment export request started from UI. CompanyId: {CompanyId}. BillId: {BillId}. BillNumber: {BillNumber}. ProposalStatus: {ProposalStatus}. ExportMode: {ExportMode}. ExportStatus: {ExportStatus}.",
            companyId,
            SelectedBill.Id,
            SelectedBill.BillNumber,
            SelectedBill.PaymentProposal?.Status,
            exportMode,
            SelectedBill.PaymentProposal?.ExportStatus);

        IsPaymentExportSubmitting = true;
        PaymentExportMessage = null;
        try
        {
            var proposal = await FinanceApiClient.ExportSupplierBillPaymentInstructionAsync(companyId, SelectedBill.Id, exportMode);
            SelectedBill.PaymentProposal = proposal;
            PaymentExportMessage = string.IsNullOrWhiteSpace(proposal.ExportResponseSummary)
                ? "Payment export state was recorded. No payment was initiated automatically."
                : proposal.ExportResponseSummary;
            Logger.LogInformation(
                "Supplier bill payment export request completed from UI. CompanyId: {CompanyId}. BillId: {BillId}. ProposalId: {ProposalId}. ExportMode: {ExportMode}. ExportStatus: {ExportStatus}. ProviderKey: {ProviderKey}.",
                companyId,
                SelectedBill.Id,
                proposal.Id,
                proposal.ExportMode,
                proposal.ExportStatus,
                proposal.ExportProviderKey);
            await LoadBillsAsync(companyId);
            await LoadDetailAsync(companyId, SelectedBill.Id);
        }
        catch (FinanceApiException ex)
        {
            PaymentExportMessage = ex.Message;
            Logger.LogWarning(
                ex,
                "Supplier bill payment export request failed in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        catch (Exception ex)
        {
            PaymentExportMessage = "Payment export failed before the request completed. Check the logs for details.";
            Logger.LogError(
                ex,
                "Supplier bill payment export request failed unexpectedly in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        finally
        {
            IsPaymentExportSubmitting = false;
        }
    }

    private async Task CancelSupplierInvoiceAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedBill is null)
        {
            Logger.LogWarning(
                "Supplier bill cancellation click ignored because context is missing. CompanyId: {CompanyId}. HasSelectedBill: {HasSelectedBill}.",
                AccessState.CompanyId,
                SelectedBill is not null);
            CorrectionActionMessage = "Cancellation could not start because the selected bill context is missing. Reload and try again.";
            return;
        }

        Logger.LogInformation(
            "Supplier bill cancellation request started from UI. CompanyId: {CompanyId}. BillId: {BillId}. BillNumber: {BillNumber}. PostingStatus: {PostingStatus}. SettlementStatus: {SettlementStatus}.",
            companyId,
            SelectedBill.Id,
            SelectedBill.BillNumber,
            SelectedBill.PostingStatus,
            SelectedBill.SettlementStatus);

        IsCancellationSubmitting = true;
        CorrectionActionMessage = null;
        try
        {
            var action = await FinanceApiClient.CancelSupplierInvoiceAsync(companyId, SelectedBill.Id);
            ReplaceCorrectionAction(action);
            CorrectionActionMessage = string.IsNullOrWhiteSpace(action.ResponseSummary)
                ? "Cancellation state was recorded."
                : action.ResponseSummary;
            Logger.LogInformation(
                "Supplier bill cancellation request completed from UI. CompanyId: {CompanyId}. BillId: {BillId}. ActionId: {ActionId}. Status: {Status}. ProviderKey: {ProviderKey}.",
                companyId,
                SelectedBill.Id,
                action.Id,
                action.Status,
                action.ProviderKey);
            await LoadBillsAsync(companyId);
            await LoadDetailAsync(companyId, SelectedBill.Id);
        }
        catch (FinanceApiException ex)
        {
            CorrectionActionMessage = ex.Message;
            Logger.LogWarning(
                ex,
                "Supplier bill cancellation request failed in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        catch (Exception ex)
        {
            CorrectionActionMessage = "Cancellation failed before the request completed. Check the logs for details.";
            Logger.LogError(
                ex,
                "Supplier bill cancellation request failed unexpectedly in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        finally
        {
            IsCancellationSubmitting = false;
        }
    }

    private async Task CreateSupplierCreditNoteAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || SelectedBill is null)
        {
            Logger.LogWarning(
                "Supplier bill credit note click ignored because context is missing. CompanyId: {CompanyId}. HasSelectedBill: {HasSelectedBill}.",
                AccessState.CompanyId,
                SelectedBill is not null);
            CorrectionActionMessage = "Credit note creation could not start because the selected bill context is missing. Reload and try again.";
            return;
        }

        Logger.LogInformation(
            "Supplier bill credit note request started from UI. CompanyId: {CompanyId}. BillId: {BillId}. BillNumber: {BillNumber}. PostingStatus: {PostingStatus}. SettlementStatus: {SettlementStatus}.",
            companyId,
            SelectedBill.Id,
            SelectedBill.BillNumber,
            SelectedBill.PostingStatus,
            SelectedBill.SettlementStatus);

        IsCreditNoteSubmitting = true;
        CorrectionActionMessage = null;
        try
        {
            var action = await FinanceApiClient.CreateSupplierCreditNoteAsync(companyId, SelectedBill.Id);
            ReplaceCorrectionAction(action);
            CorrectionActionMessage = string.IsNullOrWhiteSpace(action.ResponseSummary)
                ? "Credit note state was recorded."
                : action.ResponseSummary;
            Logger.LogInformation(
                "Supplier bill credit note request completed from UI. CompanyId: {CompanyId}. BillId: {BillId}. ActionId: {ActionId}. Status: {Status}. ProviderCreditNoteNumber: {ProviderCreditNoteNumber}.",
                companyId,
                SelectedBill.Id,
                action.Id,
                action.Status,
                action.ProviderCreditNoteNumber);
            await LoadBillsAsync(companyId);
            await LoadDetailAsync(companyId, SelectedBill.Id);
        }
        catch (FinanceApiException ex)
        {
            CorrectionActionMessage = ex.Message;
            Logger.LogWarning(
                ex,
                "Supplier bill credit note request failed in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        catch (Exception ex)
        {
            CorrectionActionMessage = "Credit note creation failed before the request completed. Check the logs for details.";
            Logger.LogError(
                ex,
                "Supplier bill credit note request failed unexpectedly in UI. CompanyId: {CompanyId}. BillId: {BillId}.",
                companyId,
                SelectedBill.Id);
        }
        finally
        {
            IsCreditNoteSubmitting = false;
        }
    }

    private void ReplaceCorrectionAction(SupplierInvoiceCorrectionActionResponse action)
    {
        if (SelectedBill is null)
        {
            return;
        }

        SelectedBill.CorrectionActions.RemoveAll(x => string.Equals(x.ActionType, action.ActionType, StringComparison.OrdinalIgnoreCase));
        SelectedBill.CorrectionActions.Add(action);
    }

    private FinanceTransactionPaymentContextResponse? SelectPaymentContext(
        FinanceTransactionPaymentContextResponse? detailContext,
        FinanceTransactionPaymentContextResponse? listContext)
    {
        if (HasPaymentEvidence(detailContext))
        {
            return detailContext;
        }

        return HasPaymentEvidence(listContext) ? listContext : detailContext ?? listContext;
    }

    private bool HasPaymentEvidence(FinanceTransactionPaymentContextResponse? paymentContext) =>
        paymentContext is not null &&
        (paymentContext.IsPartiallyPaid ||
         paymentContext.PaidAmount > 0m ||
         paymentContext.RemainingAmount > 0m && paymentContext.RemainingAmount < paymentContext.TotalAmount);

    private BillRelatedTransactionViewModel ToRelatedTransactionViewModel(FinanceInvoiceRelatedTransactionResponse transaction)
    {
        var isPayment = IsPaymentTransaction(transaction.TransactionType);
        return new BillRelatedTransactionViewModel(
            transaction.Id,
            FormatFriendlyDate(transaction.TransactionUtc),
            string.IsNullOrWhiteSpace(transaction.Description) ? FormatStatusLabel(transaction.TransactionType) : transaction.Description,
            FormatStatusLabel(transaction.TransactionType),
            string.IsNullOrWhiteSpace(transaction.ExternalReference) ? "No reference" : transaction.ExternalReference,
            FormatCurrency(transaction.Amount, transaction.Currency),
            isPayment ? "Payment" : "Bill entry",
            isPayment ? "success" : "info");
    }

    private BillPaymentSummaryViewModel? BuildPaymentSummary(
        FinanceTransactionPaymentContextResponse? paymentContext,
        string? providerStatus,
        decimal billAmount,
        string currency)
    {
        var contextSummary = paymentContext is null
            ? null
            : BuildPaymentSummary(
                paymentContext.PaidAmount,
                paymentContext.TotalAmount,
                paymentContext.RemainingAmount,
                paymentContext.Currency,
                paymentContext.IsPartiallyPaid);
        var providerSummary = BuildPaymentSummaryFromProviderStatus(providerStatus, billAmount, currency);

        if (providerSummary is not null && HasProviderBalanceStatus(providerStatus))
        {
            return providerSummary;
        }

        if (contextSummary?.IsPartiallyPaid == true || contextSummary?.IsFullyPaid == true || contextSummary?.PaidAmount > 0m)
        {
            return contextSummary;
        }

        return providerSummary ?? contextSummary;
    }

    private BillPaymentSummaryViewModel? BuildPaymentSummaryFromProviderStatus(
        string? providerStatus,
        decimal billAmount,
        string currency)
    {
        if (string.IsNullOrWhiteSpace(providerStatus))
        {
            return null;
        }

        var values = ParseProviderStatus(providerStatus);
        if (values.TryGetValue("balance", out var balanceValue) &&
            decimal.TryParse(balanceValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var remainingAmount))
        {
            var totalAmount = decimal.Round(Math.Abs(billAmount), 2, MidpointRounding.AwayFromZero);
            var normalizedRemaining = Math.Max(0m, decimal.Round(Math.Abs(remainingAmount), 2, MidpointRounding.AwayFromZero));
            var paidAmount = Math.Max(0m, decimal.Round(totalAmount - normalizedRemaining, 2, MidpointRounding.AwayFromZero));
            var isPartiallyPaid = paidAmount > 0m && normalizedRemaining > 0m;
            return BuildPaymentSummary(paidAmount, totalAmount, normalizedRemaining, currency, isPartiallyPaid);
        }

        if (TryGetBool(values, "fullyPaid", out var fullyPaid) && fullyPaid)
        {
            var totalAmount = decimal.Round(Math.Abs(billAmount), 2, MidpointRounding.AwayFromZero);
            return BuildPaymentSummary(totalAmount, totalAmount, 0m, currency, false);
        }

        return null;
    }

    private PaymentProposalViewModel? BuildPaymentProposalViewModel(SupplierInvoicePaymentProposalResponse? proposal)
    {
        if (proposal is null)
        {
            return null;
        }

        var status = NormalizeStatusToken(proposal.Status);
        var exportMode = NormalizeStatusToken(proposal.ExportMode);
        var exportStatus = NormalizeStatusToken(proposal.ExportStatus);
        var needsFortnoxBookkeep = NeedsFortnoxBookkeep(proposal);
        var (exportLabel, exportTone, canExport) = exportStatus switch
        {
            "export_requested" when exportMode == "prepare_payment_file" => ("Manual payment file required", "warning", false),
            "export_requested" when exportMode == "manual_export" => ("Manual export required", "warning", false),
            "export_requested" => ("Ready to register in Fortnox", "warning", status == "ready_for_payment"),
            "exported" when needsFortnoxBookkeep => ("Finalize in Fortnox", "warning", true),
            "exported" when exportMode == "prepare_payment_file" => ("Payment file prepared", "success", false),
            "exported" => ("Payment booked in Fortnox", "success", false),
            "failed" when exportMode == "prepare_payment_file" => ("Payment file failed", "danger", true),
            "failed" => ("Export failed", "danger", true),
            "cancelled" => ("Export cancelled", "neutral", false),
            _ => ("Not exported", "neutral", status == "ready_for_payment")
        };
        var exportActionLabel = exportMode == "prepare_payment_file"
            ? "Retry payment file"
            : needsFortnoxBookkeep
                ? "Finalize payment in Fortnox"
                : "Register payment in Fortnox";
        var canPreparePaymentFile =
            status == "ready_for_payment" &&
            !needsFortnoxBookkeep &&
            exportStatus is "" or "not_exported" or "failed" &&
            (exportStatus != "failed" || exportMode == "prepare_payment_file");
        var canRegisterPayment =
            status == "ready_for_payment" &&
            (canExport || exportStatus is "" or "not_exported" or "failed") &&
            exportMode != "prepare_payment_file";
        var (label, tone, description) = status switch
        {
            "awaiting_approval" => ("Awaiting approval", "warning", "Payment proposal is waiting for approval before it can be marked ready for payment/export."),
            "ready_for_payment" => ("Ready for payment/export", "success", "Payment proposal was approved. No payment has been initiated automatically."),
            "rejected" => ("Rejected", "danger", "Payment proposal was rejected."),
            "cancelled" => ("Cancelled", "neutral", "Payment proposal was cancelled."),
            "exported" => ("Exported", "info", "Payment proposal has been exported."),
            _ => ("Draft", "neutral", "Payment proposal has been created but is not ready yet.")
        };

        return new PaymentProposalViewModel(
            proposal.Id,
            label,
            tone,
            description,
            FormatCurrency(proposal.Amount, proposal.Currency),
            FormatFriendlyDate(proposal.DueUtc),
            string.IsNullOrWhiteSpace(proposal.PaymentReference) ? "No reference" : proposal.PaymentReference,
            proposal.ApprovalRequestId,
            proposal.TaskId,
            exportMode,
            exportStatus,
            exportLabel,
            exportTone,
            canExport || canRegisterPayment,
            canPreparePaymentFile,
            canRegisterPayment,
            exportActionLabel,
            proposal.ExportProviderKey,
            proposal.ExportResponseSummary,
            proposal.ExportRequestedUtc);
    }

    private SourceDocumentAttachmentViewModel BuildSourceDocumentAttachmentViewModel(
        SupplierInvoiceSourceDocumentAttachmentResponse? attachment,
        FinanceLinkedDocumentAccessResponse linkedDocument)
    {
        var hasSourceDocument = linkedDocument.Document is not null;
        var status = NormalizeStatusToken(attachment?.Status);
        if (!hasSourceDocument && string.IsNullOrWhiteSpace(status))
        {
            return new SourceDocumentAttachmentViewModel(
                "No source document available",
                "neutral",
                "No source document is linked to this supplier bill.",
                false,
                null,
                null,
                null);
        }

        var (label, tone, description, canAttach) = status switch
        {
            "attached" => ("Attached", "success", attachment?.ResponseSummary ?? "Source document has been attached in Fortnox.", false),
            "attachment_requested" => ("Attachment requested", "warning", attachment?.ResponseSummary ?? "Source document attachment has been requested.", false),
            "failed" => ("Attachment failed", "danger", attachment?.ResponseSummary ?? "Source document attachment failed. You can retry.", hasSourceDocument),
            "not_available" => ("No source document available", "neutral", attachment?.ResponseSummary ?? "No source document is linked to this supplier bill.", false),
            _ => ("Not attached", "neutral", hasSourceDocument ? "Attach the linked source document to this supplier invoice in Fortnox." : "No source document is linked to this supplier bill.", hasSourceDocument)
        };

        return new SourceDocumentAttachmentViewModel(
            label,
            tone,
            description,
            canAttach,
            attachment?.ProviderKey,
            attachment?.RequestedUtc,
            attachment?.AttachedUtc);
    }

    private SupplierInvoiceDraftActionViewModel BuildDraftActionViewModel(
        SupplierInvoiceDraftActionResponse? action,
        string? postingStatus,
        string? settlementStatus,
        string? documentKind,
        string? fallbackStatus,
        decimal remainingAmount)
    {
        var status = NormalizeStatusToken(action?.Status);
        var isEditableDraft = CanManageFortnoxDraft(postingStatus, settlementStatus, documentKind, fallbackStatus, remainingAmount);
        var (label, tone, description) = status switch
        {
            "update_pending" => ("Update pending", "warning", action?.ResponseSummary ?? "Fortnox draft update has been requested."),
            "updated" => ("Updated", "success", action?.ResponseSummary ?? "Fortnox draft was updated."),
            "bookkeeping_requested" => ("Bookkeeping requested", "warning", action?.ResponseSummary ?? "Bookkeeping has been requested in Fortnox."),
            "booked" => ("Booked", "success", action?.ResponseSummary ?? "Supplier invoice was booked in Fortnox."),
            "failed" => ("Failed", "danger", action?.ResponseSummary ?? "The Fortnox draft action failed. You can retry while the invoice is still editable."),
            _ => ("Draft", "neutral", isEditableDraft
                ? "Update corrected fields in Fortnox, or bookkeep the supplier invoice when it is ready."
                : "Fortnox draft actions are available only while the supplier invoice is still editable.")
        };

        var canUpdate = isEditableDraft && status is "" or "draft" or "failed";
        var canBookkeep = isEditableDraft && status is "" or "draft" or "updated" or "failed";
        return new SupplierInvoiceDraftActionViewModel(
            label,
            tone,
            description,
            canUpdate,
            canBookkeep,
            action?.ProviderKey,
            action?.RequestedUtc,
            action?.UpdatedInProviderUtc,
            action?.BookedUtc);
    }

    private SupplierInvoiceCorrectionActionsViewModel BuildCorrectionActionsViewModel(
        IReadOnlyList<SupplierInvoiceCorrectionActionResponse>? actions,
        string? postingStatus,
        string? settlementStatus,
        string? documentKind,
        string? fallbackStatus)
    {
        var cancellation = actions?
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefault(x => NormalizeStatusToken(x.ActionType) == "cancellation");
        var creditNote = actions?
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefault(x => NormalizeStatusToken(x.ActionType) == "credit_note");

        var cancellationStatus = NormalizeStatusToken(cancellation?.Status);
        var creditNoteStatus = NormalizeStatusToken(creditNote?.Status);
        var isCancellationApproved = cancellation?.ApprovedUtc is not null;
        var isCreditNoteApproved = creditNote?.ApprovedUtc is not null;
        var canCancelBase = CanRequestCancellation(postingStatus, settlementStatus, documentKind, fallbackStatus);
        var canCreditNoteBase = CanRequestCreditNote(postingStatus, settlementStatus, documentKind, fallbackStatus);

        var cancellationPresentation = cancellationStatus switch
        {
            "cancellation_requested" when isCancellationApproved => ("Cancellation approved", "warning", "Cancellation is approved. Run the action to update Fortnox.", canCancelBase),
            "cancellation_requested" => ("Approval requested", "warning", cancellation?.ResponseSummary ?? "Cancellation approval is pending. Fortnox has not been changed.", false),
            "cancelled" => ("Cancelled", "success", cancellation?.ResponseSummary ?? "Supplier invoice was cancelled in Fortnox.", false),
            "cancellation_failed" => ("Cancellation failed", "danger", cancellation?.ResponseSummary ?? "Cancellation failed. You can retry while the invoice is still eligible.", canCancelBase),
            _ => ("Not cancelled", "neutral", canCancelBase
                ? "Cancel this supplier invoice in Fortnox when it should be voided."
                : "Cancellation is available only for unpaid, uncredited supplier invoices.", canCancelBase)
        };

        var creditNotePresentation = creditNoteStatus switch
        {
            "credit_note_requested" when isCreditNoteApproved => ("Credit note approved", "warning", "Credit note creation is approved. Run the action to update Fortnox.", canCreditNoteBase),
            "credit_note_requested" => ("Approval requested", "warning", creditNote?.ResponseSummary ?? "Credit note approval is pending. Fortnox has not been changed.", false),
            "credit_note_created" => ("Credit note created", "success", creditNote?.ResponseSummary ?? "Supplier credit note was created in Fortnox.", false),
            "credit_note_failed" => ("Credit note failed", "danger", creditNote?.ResponseSummary ?? "Credit note creation failed. You can retry.", canCreditNoteBase),
            _ => ("No credit note", "neutral", canCreditNoteBase
                ? "Create a linked supplier credit note for a correction, duplicate, refund, or overbilling case."
                : "Credit note creation is available only for active supplier invoices that are not already credited.", canCreditNoteBase)
        };

        return new SupplierInvoiceCorrectionActionsViewModel(
            cancellationPresentation.Item1,
            cancellationPresentation.Item2,
            cancellationPresentation.Item3,
            cancellationPresentation.Item4,
            cancellation?.ProviderKey,
            cancellation?.RequestedUtc,
            cancellation?.ApprovedUtc,
            cancellation?.CompletedUtc,
            creditNotePresentation.Item1,
            creditNotePresentation.Item2,
            creditNotePresentation.Item3,
            creditNotePresentation.Item4,
            creditNote?.ProviderKey,
            creditNote?.RequestedUtc,
            creditNote?.ApprovedUtc,
            creditNote?.CompletedUtc,
            creditNote?.CreditNoteBillId,
            creditNote?.ProviderCreditNoteNumber);
    }

    private SupplierInvoiceEnrichmentViewModel BuildEnrichmentViewModel(SupplierInvoiceEnrichmentActionResponse? action)
    {
        if (action is null)
        {
            return new SupplierInvoiceEnrichmentViewModel(
                "Not suggested",
                "neutral",
                "Laura can suggest coding, supplier data, comments, and reconciliation checks for this bill.",
                "No suggestion yet.",
                [],
                [],
                true,
                false,
                true,
                null,
                null,
                null,
                null);
        }

        var status = NormalizeStatusToken(action.Status);
        var presentation = status switch
        {
            "awaiting_approval" => ("Awaiting approval", "warning", "Laura's enrichment suggestions are waiting for approval before Fortnox can be updated."),
            "approved" => ("Approved", "success", "Laura's enrichment suggestions are approved and ready to sync to Fortnox."),
            "sync_requested" => ("Sync requested", "warning", "Fortnox enrichment sync is in progress or waiting for provider confirmation."),
            "synced" => ("Synced", "success", action.ResponseSummary ?? "Supported enrichment changes were synced to Fortnox."),
            "failed" => ("Sync failed", "danger", action.ResponseSummary ?? "Fortnox enrichment sync failed. You can retry after reviewing the details."),
            "reconciliation_warning" => ("Needs review", "danger", "Reconciliation found warnings that should be reviewed."),
            _ => ("Not suggested", "neutral", "Laura can suggest coding, supplier data, comments, and reconciliation checks for this bill.")
        };

        return new SupplierInvoiceEnrichmentViewModel(
            presentation.Item1,
            presentation.Item2,
            presentation.Item3,
            BuildEnrichmentSuggestionSummary(action.SuggestionPayload),
            BuildEnrichmentWarningItems(action.ReconciliationWarnings),
            BuildEnrichmentSuggestionItems(action.SuggestionPayload),
            status is "not_suggested" or "failed" or "synced" or "reconciliation_warning",
            status is "approved" or "failed",
            true,
            action.ProviderKey,
            action.RequestedUtc,
            action.ApprovedUtc,
            action.SyncedUtc);
    }

    private string BuildEnrichmentSuggestionSummary(JsonObject? suggestion)
    {
        var account = ReadJsonString(suggestion?["coding"] as JsonObject, "ledgerAccount");
        var comment = ReadJsonString(suggestion, "comment");
        if (!string.IsNullOrWhiteSpace(account))
        {
            return $"Laura suggests account {account}. {comment}";
        }

        return string.IsNullOrWhiteSpace(comment)
            ? "No enrichment suggestion has been generated yet."
            : comment;
    }

    private IReadOnlyList<string> BuildEnrichmentSuggestionItems(JsonObject? suggestion)
    {
        var items = new List<string>();
        var coding = suggestion?["coding"] as JsonObject;
        var supplier = suggestion?["supplier"] as JsonObject;
        AddIfPresent(items, "Ledger account", ReadJsonString(coding, "ledgerAccount"));
        AddIfPresent(items, "Cost center", ReadJsonString(coding, "costCenter"));
        AddIfPresent(items, "Project", ReadJsonString(coding, "project"));
        AddIfPresent(items, "Supplier number", ReadJsonString(supplier, "supplierNumber"));
        AddIfPresent(items, "Supplier VAT/org number", ReadJsonString(supplier, "vatOrTaxId"));
        AddIfPresent(items, "Supplier email", ReadJsonString(supplier, "email"));
        return items;
    }

    private IReadOnlyList<string> BuildEnrichmentWarningItems(JsonArray? warnings) =>
        warnings is null
            ? []
            : warnings
                .OfType<JsonObject>()
                .Select(warning => ReadJsonString(warning, "message"))
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Select(message => message!)
                .ToArray();

    private void AddIfPresent(List<string> items, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            items.Add($"{label}: {value}");
        }
    }

    private string? ReadJsonString(JsonObject? obj, string propertyName) =>
        obj is not null && obj.TryGetPropertyValue(propertyName, out var node) && node is not null && !string.IsNullOrWhiteSpace(node.ToString())
            ? node.ToString()
            : null;

    private bool CanRequestCancellation(
        string? postingStatus,
        string? settlementStatus,
        string? documentKind,
        string? fallbackStatus)
    {
        var posting = NormalizeStatusToken(postingStatus);
        var settlement = NormalizeStatusToken(settlementStatus);
        var kind = NormalizeStatusToken(documentKind);
        var fallback = NormalizeStatusToken(fallbackStatus);

        return kind == "supplier_invoice" &&
            posting != "cancelled" &&
            settlement is not ("paid" or "credited") &&
            fallback is not ("paid" or "cancelled" or "canceled" or "void");
    }

    private bool CanRequestCreditNote(
        string? postingStatus,
        string? settlementStatus,
        string? documentKind,
        string? fallbackStatus)
    {
        var posting = NormalizeStatusToken(postingStatus);
        var settlement = NormalizeStatusToken(settlementStatus);
        var kind = NormalizeStatusToken(documentKind);
        var fallback = NormalizeStatusToken(fallbackStatus);

        return kind == "supplier_invoice" &&
            posting != "cancelled" &&
            settlement != "credited" &&
            fallback is not ("cancelled" or "canceled" or "void");
    }

    private BillStatusPresentation? ResolvePaymentProposalPresentation(SupplierInvoicePaymentProposalResponse? proposal)
    {
        if (proposal is null)
        {
            return null;
        }

        var exportMode = NormalizeStatusToken(proposal.ExportMode);
        var exportStatus = NormalizeStatusToken(proposal.ExportStatus);

        return NormalizeStatusToken(proposal.Status) switch
        {
            "awaiting_approval" => new("Payment approval", "warning"),
            "ready_for_payment" when exportStatus == "export_requested" && exportMode == "prepare_payment_file" => new("Manual payment file", "warning"),
            "ready_for_payment" when exportStatus == "export_requested" => new("Register in Fortnox", "warning"),
            "exported" when NeedsFortnoxBookkeep(proposal) => new("Finalize payment", "warning"),
            "ready_for_payment" when exportStatus == "exported" && exportMode == "prepare_payment_file" => new("Payment file ready", "success"),
            "ready_for_payment" when exportStatus == "exported" => new("Payment registered", "success"),
            "ready_for_payment" => new("Ready to pay", "success"),
            "rejected" => new("Payment rejected", "danger"),
            "cancelled" => new("Payment cancelled", "neutral"),
            "exported" => new("Payment registered", "info"),
            _ => new("Payment draft", "neutral")
        };
    }

    private BillStatusPresentation? ResolveCorrectionListPresentation(IReadOnlyList<SupplierInvoiceCorrectionActionResponse>? actions)
    {
        if (actions is null || actions.Count == 0)
        {
            return null;
        }

        var latest = actions.OrderByDescending(x => x.UpdatedUtc).FirstOrDefault();
        return NormalizeStatusToken(latest?.Status) switch
        {
            "cancellation_requested" when latest?.ApprovedUtc is not null => new("Cancellation approved", "warning"),
            "cancellation_requested" => new("Approval requested", "warning"),
            "cancelled" => new("Cancelled in Fortnox", "success"),
            "cancellation_failed" => new("Cancellation failed", "danger"),
            "credit_note_requested" when latest?.ApprovedUtc is not null => new("Credit note approved", "warning"),
            "credit_note_requested" => new("Approval requested", "warning"),
            "credit_note_created" => new("Credit note created", "success"),
            "credit_note_failed" => new("Credit note failed", "danger"),
            _ => null
        };
    }

    private BillStatusPresentation? ResolveEnrichmentListPresentation(SupplierInvoiceEnrichmentActionResponse? action)
    {
        if (action is null)
        {
            return null;
        }

        return NormalizeStatusToken(action.Status) switch
        {
            "awaiting_approval" => new("Enrichment approval", "warning"),
            "approved" => new("Ready to sync", "success"),
            "sync_requested" => new("Sync requested", "warning"),
            "synced" => new("Enriched", "success"),
            "failed" => new("Enrichment failed", "danger"),
            "reconciliation_warning" => new("Reconcile warning", "danger"),
            _ => null
        };
    }

    private bool CanRequestPaymentProposalAfter(PaymentProposalViewModel? proposal) =>
        proposal is null ||
        NormalizeStatusToken(proposal.StatusLabel) is "exported" or "rejected" or "cancelled";

    private bool NeedsFortnoxBookkeep(SupplierInvoicePaymentProposalResponse proposal)
    {
        var summary = proposal.ExportResponseSummary;
        return NormalizeStatusToken(proposal.ExportStatus) == "exported" &&
               !string.IsNullOrWhiteSpace(summary) &&
               summary.Contains("registered supplier invoice payment", StringComparison.OrdinalIgnoreCase) &&
               !summary.Contains("booked", StringComparison.OrdinalIgnoreCase);
    }

    private BillPaymentSummaryViewModel BuildPaymentSummary(
        decimal paidAmount,
        decimal totalAmount,
        decimal remainingAmount,
        string currency,
        bool isPartiallyPaid)
    {
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "SEK" : currency;
        var normalizedPaid = decimal.Round(Math.Abs(paidAmount), 2, MidpointRounding.AwayFromZero);
        var normalizedTotal = decimal.Round(Math.Abs(totalAmount), 2, MidpointRounding.AwayFromZero);
        var normalizedRemaining = Math.Max(0m, decimal.Round(Math.Abs(remainingAmount), 2, MidpointRounding.AwayFromZero));
        var summary = $"{FormatCurrency(normalizedPaid, normalizedCurrency)} paid; {FormatCurrency(normalizedRemaining, normalizedCurrency)} remaining";
        var reason = isPartiallyPaid
            ? $"This supplier bill is partially paid. {FormatCurrency(normalizedPaid, normalizedCurrency)} has been paid and {FormatCurrency(normalizedRemaining, normalizedCurrency)} remains to pay."
            : summary;

        return new BillPaymentSummaryViewModel(
            isPartiallyPaid,
            normalizedTotal > 0m && normalizedPaid >= normalizedTotal && normalizedRemaining == 0m,
            normalizedPaid,
            normalizedTotal,
            normalizedRemaining,
            normalizedCurrency,
            summary,
            reason);
    }

    private IReadOnlyList<BillSourceDetailViewModel> BuildSourceDetails(string? providerStatus, string currency)
    {
        if (string.IsNullOrWhiteSpace(providerStatus))
        {
            return [];
        }

        var values = ParseProviderStatus(providerStatus);
        if (values.Count == 0)
        {
            return [new BillSourceDetailViewModel("Provider status", FormatStatusLabel(providerStatus))];
        }

        var details = new List<BillSourceDetailViewModel>();
        if (TryGetBool(values, "booked", out var booked))
        {
            details.Add(new BillSourceDetailViewModel("Fortnox posting", booked ? "Booked" : "Not booked"));
        }

        if (TryGetBool(values, "cancelled", out var cancelled) && cancelled)
        {
            details.Add(new BillSourceDetailViewModel("Fortnox state", "Cancelled"));
        }

        if (TryGetBool(values, "credit", out var credit) && credit)
        {
            details.Add(new BillSourceDetailViewModel("Fortnox type", "Credit bill"));
        }

        if (TryGetBool(values, "fullyPaid", out var fullyPaid))
        {
            details.Add(new BillSourceDetailViewModel("Payment in Fortnox", fullyPaid ? "Fully paid" : "Not fully paid"));
        }

        if (values.TryGetValue("balance", out var balanceValue) &&
            decimal.TryParse(balanceValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var balance))
        {
            details.Add(new BillSourceDetailViewModel("Balance in Fortnox", FormatCurrency(balance, currency)));
        }

        if (TryGetBool(values, "sent", out var sent))
        {
            details.Add(new BillSourceDetailViewModel("Sent status", sent ? "Sent" : "Not sent"));
        }

        return details;
    }

    private Dictionary<string, string> ParseProviderStatus(string providerStatus)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in providerStatus.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = segment.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex >= segment.Length - 1)
            {
                continue;
            }

            values[segment[..separatorIndex].Trim()] = segment[(separatorIndex + 1)..].Trim();
        }

        return values;
    }

    private bool TryGetBool(IReadOnlyDictionary<string, string> values, string key, out bool result)
    {
        result = false;
        return values.TryGetValue(key, out var value) &&
            bool.TryParse(value, out result);
    }

    private bool HasProviderBalanceStatus(string? providerStatus) =>
        !string.IsNullOrWhiteSpace(providerStatus) &&
        ParseProviderStatus(providerStatus).ContainsKey("balance");

    private string? ResolveEffectiveSettlementStatus(
        string? settlementStatus,
        string? providerStatus,
        BillPaymentSummaryViewModel? paymentSummary)
    {
        if (!HasProviderBalanceStatus(providerStatus) || paymentSummary is null)
        {
            return settlementStatus;
        }

        if (paymentSummary.IsFullyPaid)
        {
            return "paid";
        }

        return paymentSummary.IsPartiallyPaid ? "partially_paid" : "unpaid";
    }

    private string? ResolveEffectiveFallbackStatus(
        string? fallbackStatus,
        string? providerStatus,
        BillPaymentSummaryViewModel? paymentSummary)
    {
        if (!HasProviderBalanceStatus(providerStatus) || paymentSummary is null)
        {
            return fallbackStatus;
        }

        return paymentSummary.IsFullyPaid ? "paid" : "open";
    }

    private bool IsPaymentTransaction(string? transactionType)
    {
        var normalized = NormalizeStatusToken(transactionType);
        return normalized is "customer_payment" or "supplier_payment" or "payment";
    }

    private bool IsDueSoonBill(FinanceBillResponse bill) =>
        NormalizeStatusToken(bill.DueStatus) == "due_soon" && IsOpenUnpaidBill(bill);

    private bool IsOverdueBill(FinanceBillResponse bill) =>
        NormalizeStatusToken(bill.DueStatus) == "overdue" && IsOpenUnpaidBill(bill);

    private bool IsPaidBill(FinanceBillResponse bill) =>
        NormalizeStatusToken(bill.SettlementStatus) == "paid" ||
        NormalizeStatusToken(bill.Status) == "paid";

    private bool IsCancelledBill(FinanceBillResponse bill) =>
        NormalizeStatusToken(bill.PostingStatus) == "cancelled" ||
        NormalizeStatusToken(bill.Status) is "cancelled" or "canceled" or "void";

    private bool IsOpenUnpaidBill(FinanceBillResponse bill)
    {
        var settlement = NormalizeStatusToken(bill.SettlementStatus);
        var kind = NormalizeStatusToken(bill.DocumentKind);
        return !IsPaidBill(bill) &&
               !IsCancelledBill(bill) &&
               settlement != "credited" &&
               kind != "supplier_credit_note";
    }

    private bool IsPayableSupplierBill(
        string? postingStatus,
        string? settlementStatus,
        string? documentKind,
        string? fallbackStatus,
        decimal amount)
    {
        var posting = NormalizeStatusToken(postingStatus);
        var settlement = NormalizeStatusToken(settlementStatus);
        var kind = NormalizeStatusToken(documentKind);
        var fallback = NormalizeStatusToken(fallbackStatus);

        return amount != 0m &&
            kind == "supplier_invoice" &&
            posting != "cancelled" &&
            settlement is not ("paid" or "credited") &&
            fallback is not ("paid" or "cancelled" or "canceled" or "void");
    }

    private bool CanManageFortnoxDraft(
        string? postingStatus,
        string? settlementStatus,
        string? documentKind,
        string? fallbackStatus,
        decimal remainingAmount)
    {
        var posting = NormalizeStatusToken(postingStatus);
        var settlement = NormalizeStatusToken(settlementStatus);
        var kind = NormalizeStatusToken(documentKind);
        var fallback = NormalizeStatusToken(fallbackStatus);

        return remainingAmount != 0m &&
            kind == "supplier_invoice" &&
            posting == "draft" &&
            settlement is not ("paid" or "credited") &&
            fallback is not ("paid" or "cancelled" or "canceled" or "void" or "booked");
    }

    private PaidExpensePostingViewModel BuildPaidExpensePostingViewModel(
        PaidSupplierBillExpenseAvailabilityResponse? availability)
    {
        if (availability is not null)
        {
            return new PaidExpensePostingViewModel(
                availability.CanPost,
                string.IsNullOrWhiteSpace(availability.StatusLabel)
                    ? availability.CanPost ? "Ready to post" : "Not ready"
                    : availability.StatusLabel,
                string.IsNullOrWhiteSpace(availability.StatusTone)
                    ? availability.CanPost ? "success" : "warning"
                    : availability.StatusTone,
                string.IsNullOrWhiteSpace(availability.Message)
                    ? availability.CanPost
                        ? "Laura can post this paid supplier bill as an expense."
                        : "Resolve the listed items before Laura posts this expense."
                    : availability.Message,
                availability.AccountCode,
                availability.BlockingReasons);
        }

        return new PaidExpensePostingViewModel(
            false,
            "Not ready",
            "warning",
            "Expense posting eligibility is unavailable. Refresh the bill before posting.",
            null,
            []);
    }

    private BillStatusPresentation ResolveStatusPresentation(
        string? postingStatus,
        string? settlementStatus,
        string? dueStatus,
        string? documentKind,
        string? fallbackStatus,
        bool forcePartiallyPaid = false,
        bool forcePaid = false)
    {
        var kind = NormalizeStatusToken(documentKind);
        var posting = NormalizeStatusToken(postingStatus);
        var settlement = NormalizeStatusToken(settlementStatus);
        var due = NormalizeStatusToken(dueStatus);
        var fallback = NormalizeStatusToken(fallbackStatus);

        if (kind == "supplier_credit_note")
        {
            return new("Credit note", "info");
        }

        if (posting == "cancelled" || fallback is "void" or "cancelled" or "canceled")
        {
            return new("Cancelled", "neutral");
        }

        if (forcePartiallyPaid)
        {
            return new("Partially paid", "warning");
        }

        if (forcePaid)
        {
            return new("Paid", "success");
        }

        if (settlement == "paid" || fallback == "paid")
        {
            return new("Paid", "success");
        }

        if (settlement == "partially_paid")
        {
            return new("Partially paid", "warning");
        }

        if (settlement == "credited")
        {
            return new("Credited", "info");
        }

        if (due == "overdue" || fallback == "overdue")
        {
            return new("Overdue", "danger");
        }

        if (posting == "draft" || fallback is "draft" or "open")
        {
            return new("Draft", "neutral");
        }

        if (posting == "booked" || fallback == "approved")
        {
            return new("Booked", "success");
        }

        return fallback switch
        {
            "pending_approval" or "pending" or "approval_pending" => new("Pending approval", "info"),
            "problem" or "failed" => new("Overdue", "danger"),
            _ => new(string.IsNullOrWhiteSpace(fallbackStatus) ? "Unknown" : FormatStatusLabel(fallbackStatus), "neutral")
        };
    }

    private string NormalizeStatusToken(string? value) =>
        value?.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal) ?? string.Empty;

    private string FormatDocumentKind(string? value) =>
        NormalizeStatusToken(value) switch
        {
            "supplier_credit_note" => "Credit note",
            "supplier_invoice" => "Supplier invoice",
            _ => FormatStatusLabel(value)
        };

    private string ResolveApprovalStatus(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal) ?? string.Empty;
        return normalized switch
        {
            "pending_approval" or "approval_pending" => "Pending approval",
            "approved" => "Booked",
            "paid" => "Approved",
            _ => "Not required"
        };
    }

    private sealed record BillStatusPresentation(string Label, string Tone);

    private class BillLifecycleFilters
    {
        public const string All = "all";
        public const string DueSoon = "due_soon";
        public const string Overdue = "overdue";
        public const string Unpaid = "unpaid";
        public const string Paid = "paid";
        public const string Cancelled = "cancelled";
    }

    private sealed record BillFilterViewModel(string Value, string Label, int Count, bool IsActive);

    private sealed record BillListItemViewModel(
        Guid Id,
        string DisplayBillNumber,
        string DisplaySupplierName,
        string DisplayAmount,
        string DisplayReceivedDate,
        string DisplayDueDate,
        string DateSummary,
        string FriendlyStatusLabel,
        string StatusTone,
        string IconTone,
        string? ReviewBadgeLabel,
        string ReviewBadgeTone,
        bool IsSelected);

    private sealed record BillDetailViewModel(
        string DisplayBillNumber,
        string DisplaySupplierName,
        string DisplayAmount,
        string DisplayReceivedDate,
        string DisplayDueDate,
        string FriendlyStatusLabel,
        string StatusTone,
        string ApprovalStatus,
        string WarningSummary,
        string DocumentKindLabel,
        IReadOnlyList<BillSourceDetailViewModel> SourceDetails,
        BillPaymentSummaryViewModel? PaymentSummary,
        PaymentProposalViewModel? PaymentProposal,
        SourceDocumentAttachmentViewModel SourceDocumentAttachment,
        SupplierInvoiceDraftActionViewModel DraftAction,
        SupplierInvoiceCorrectionActionsViewModel Corrections,
        SupplierInvoiceEnrichmentViewModel Enrichment,
        bool CanRequestPaymentProposal,
        PaidExpensePostingViewModel PaidExpensePosting,
        string? ReviewBadgeLabel,
        string ReviewBadgeTone,
        IReadOnlyList<BillRelatedTransactionViewModel> RelatedTransactions);

    private sealed record BillSourceDetailViewModel(string Label, string Value);

    private sealed record BillPaymentSummaryViewModel(
        bool IsPartiallyPaid,
        bool IsFullyPaid,
        decimal PaidAmount,
        decimal TotalAmount,
        decimal RemainingAmount,
        string Currency,
        string Summary,
        string ReviewReason);

    private sealed record PaymentProposalViewModel(
        Guid Id,
        string StatusLabel,
        string StatusTone,
        string Description,
        string DisplayAmount,
        string DisplayDueDate,
        string PaymentReference,
        Guid? ApprovalRequestId,
        Guid? TaskId,
        string ExportMode,
        string ExportStatus,
        string ExportLabel,
        string ExportTone,
        bool CanExport,
        bool CanPreparePaymentFile,
        bool CanRegisterPayment,
        string ExportActionLabel,
        string? ExportProviderKey,
        string? ExportResponseSummary,
        DateTime? ExportRequestedUtc);

    private sealed record SourceDocumentAttachmentViewModel(
        string StatusLabel,
        string StatusTone,
        string Description,
        bool CanAttach,
        string? ProviderKey,
        DateTime? RequestedUtc,
        DateTime? AttachedUtc);

    private sealed record SupplierInvoiceDraftActionViewModel(
        string StatusLabel,
        string StatusTone,
        string Description,
        bool CanUpdate,
        bool CanBookkeep,
        string? ProviderKey,
        DateTime? RequestedUtc,
        DateTime? UpdatedInProviderUtc,
        DateTime? BookedUtc);

    private sealed record PaidExpensePostingViewModel(
        bool CanPost,
        string StatusLabel,
        string StatusTone,
        string Message,
        string? AccountCode,
        IReadOnlyList<string> BlockingReasons);

    private sealed record SupplierInvoiceCorrectionActionsViewModel(
        string CancellationStatusLabel,
        string CancellationStatusTone,
        string CancellationDescription,
        bool CanCancel,
        string? CancellationProviderKey,
        DateTime? CancellationRequestedUtc,
        DateTime? CancellationApprovedUtc,
        DateTime? CancellationCompletedUtc,
        string CreditNoteStatusLabel,
        string CreditNoteStatusTone,
        string CreditNoteDescription,
        bool CanCreateCreditNote,
        string? CreditNoteProviderKey,
        DateTime? CreditNoteRequestedUtc,
        DateTime? CreditNoteApprovedUtc,
        DateTime? CreditNoteCompletedUtc,
        Guid? CreditNoteBillId,
        string? ProviderCreditNoteNumber);

    private sealed record SupplierInvoiceEnrichmentViewModel(
        string StatusLabel,
        string StatusTone,
        string Description,
        string SuggestionSummary,
        IReadOnlyList<string> WarningItems,
        IReadOnlyList<string> SuggestionItems,
        bool CanSuggest,
        bool CanSync,
        bool CanReconcile,
        string? ProviderKey,
        DateTime? RequestedUtc,
        DateTime? ApprovedUtc,
        DateTime? SyncedUtc);

    private sealed record BillRelatedTransactionViewModel(
        Guid Id,
        string DisplayDate,
        string Description,
        string CategoryLabel,
        string ExternalReference,
        string DisplayAmount,
        string TypeLabel,
        string TypeTone);
}
