using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;
using VirtualCompany.Shared;

namespace VirtualCompany.Web.Pages.Finance;

public partial class InvoicesPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    [Parameter]
    public Guid? InvoiceId { get; set; }

    private IReadOnlyList<FinanceInvoiceResponse> Invoices { get; set; } = [];
    private FinanceInvoiceDetailResponse? SelectedInvoice { get; set; }
    private bool IsListLoading { get; set; }
    private bool IsDetailLoading { get; set; }
    private string? ListErrorMessage { get; set; }
    private string? DetailErrorMessage { get; set; }
    private string EditableStatus { get; set; } = string.Empty;
    private string? StatusValidationMessage { get; set; }
    private string? StatusSaveMessage { get; set; }
    private bool IsSavingStatus { get; set; }

    private bool IsListEmpty => !IsListLoading && string.IsNullOrWhiteSpace(ListErrorMessage) && Invoices.Count == 0;
    private IReadOnlyList<InvoiceListItemViewModel> InvoiceItems =>
        Invoices.Select(invoice => ToListItem(invoice, InvoiceId == invoice.Id)).ToList();
    private InvoiceDetailViewModel? SelectedInvoiceDisplay =>
        SelectedInvoice is null
            ? null
            : ToDetailViewModel(
                SelectedInvoice,
                Invoices.FirstOrDefault(invoice => invoice.Id == SelectedInvoice.Id)?.PaymentContext);
    private string DashboardHref => AccessState.CompanyId is Guid companyId ? $"/dashboard?companyId={companyId:D}" : "/dashboard";
    private bool CanChangeInvoiceApprovalStatus =>
        SelectedInvoice?.Permissions.CanChangeInvoiceApprovalStatus ?? FinanceAccess.CanApproveInvoices(AccessState.MembershipRole);
    private IReadOnlyList<string> EditableStatusOptions =>
        FinanceInvoiceApprovalStatuses.GetEditableValues(SelectedInvoice?.Status);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Invoices = [];
        SelectedInvoice = null;
        ListErrorMessage = null;
        DetailErrorMessage = null;
        EditableStatus = string.Empty;
        StatusValidationMessage = null;
        StatusSaveMessage = null;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadInvoicesAsync(companyId);
        if (InvoiceId is Guid invoiceId)
        {
            await LoadDetailAsync(companyId, invoiceId);
        }
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadInvoicesAsync(companyId);
        if (InvoiceId is Guid invoiceId)
        {
            await LoadDetailAsync(companyId, invoiceId);
        }
    }

    private async Task LoadInvoicesAsync(Guid companyId)
    {
        IsListLoading = true;
        ListErrorMessage = null;

        try
        {
            Invoices = await FinanceApiClient.GetInvoicesAsync(companyId, limit: 200);
        }
        catch (FinanceApiException ex)
        {
            Invoices = [];
            ListErrorMessage = ex.Message;
        }
        finally
        {
            IsListLoading = false;
        }
    }

    private async Task LoadDetailAsync(Guid companyId, Guid invoiceId)
    {
        IsDetailLoading = true;
        DetailErrorMessage = null;

        try
        {
            SelectedInvoice = await FinanceApiClient.GetInvoiceDetailAsync(companyId, invoiceId);
            if (SelectedInvoice is null)
            {
                DetailErrorMessage = "The selected invoice could not be found for this company.";
                EditableStatus = string.Empty;
            }
        }
        catch (FinanceApiException ex)
        {
            SelectedInvoice = null;
            DetailErrorMessage = ex.Message;
        }
        finally
        {
            EditableStatus = SelectedInvoice?.Status ?? string.Empty;
            IsDetailLoading = false;
        }
    }

    private async Task HandleStatusSaveAsync()
    {
        StatusValidationMessage = null;
        StatusSaveMessage = null;

        if (!CanChangeInvoiceApprovalStatus || AccessState.CompanyId is not Guid companyId || SelectedInvoice is null)
        {
            return;
        }

        var normalizedStatus = FinanceInvoiceApprovalStatuses.Normalize(EditableStatus);
        if (!FinanceInvoiceApprovalStatuses.IsSupported(normalizedStatus))
        {
            StatusValidationMessage = "Choose a supported invoice status.";
            return;
        }

        IsSavingStatus = true;
        try
        {
            EditableStatus = normalizedStatus;
            await FinanceApiClient.UpdateInvoiceApprovalStatusAsync(companyId, SelectedInvoice.Id, normalizedStatus);
            await LoadInvoicesAsync(companyId);
            await LoadDetailAsync(companyId, SelectedInvoice.Id);
            StatusSaveMessage = $"Invoice status saved as {FormatInvoiceStatusLabel(SelectedInvoice?.Status)}.";
        }
        catch (FinanceApiValidationException ex)
        {
            StatusValidationMessage = ResolveValidationMessage(ex, "Status");
        }
        catch (FinanceApiException ex)
        {
            StatusValidationMessage = ex.Message;
        }
        finally
        {
            IsSavingStatus = false;
        }
    }

    private string BuildInvoiceHref(Guid invoiceId) =>
        FinanceRoutes.BuildInvoiceDetailPath(invoiceId, AccessState.CompanyId);

    private static string ResolveValidationMessage(FinanceApiValidationException exception, string key)
    {
        if (exception.Errors.TryGetValue(key, out var directErrors) && directErrors.Length > 0)
        {
            return directErrors[0];
        }

        var matched = exception.Errors.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
        return matched.Value is { Length: > 0 } ? matched.Value[0] : exception.Message;
    }

    private string BuildDocumentHref(Guid documentId) =>
        $"/api/companies/{AccessState.CompanyId}/documents/{documentId}";

    private string BuildWorkflowHref(Guid workflowInstanceId) =>
        $"/workflows?companyId={AccessState.CompanyId}&workflowInstanceId={workflowInstanceId:D}";

    private string? BuildApprovalHref(Guid? approvalRequestId) =>
        approvalRequestId is Guid resolvedApprovalRequestId
            ? $"/approvals?companyId={AccessState.CompanyId}&approvalId={resolvedApprovalRequestId:D}"
            : null;

    private string? BuildAuditHref(Guid? auditEventId) =>
        auditEventId is Guid resolvedAuditEventId
            ? $"/audit/{resolvedAuditEventId:D}?companyId={AccessState.CompanyId}"
            : null;

    private static InvoiceListItemViewModel ToListItem(FinanceInvoiceResponse invoice, bool isSelected)
    {
        var paymentSummary = BuildPaymentSummary(invoice.PaymentContext, invoice.ProviderStatus, invoice.Amount, invoice.Currency);
        var status = ResolveStatusPresentation(
            invoice.PostingStatus,
            invoice.SettlementStatus,
            invoice.DueStatus,
            invoice.DocumentKind,
            invoice.Status,
            paymentSummary?.IsPartiallyPaid == true,
            paymentSummary?.IsFullyPaid == true);
        return new InvoiceListItemViewModel(
            invoice.Id,
            string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? "Invoice" : invoice.InvoiceNumber,
            string.IsNullOrWhiteSpace(invoice.CounterpartyName) ? "Customer not available" : invoice.CounterpartyName,
            FormatCurrency(invoice.Amount, invoice.Currency),
            FormatFriendlyDate(invoice.IssuedUtc),
            FormatFriendlyDate(invoice.DueUtc),
            $"Issued {FormatFriendlyDate(invoice.IssuedUtc)} - Due {FormatFriendlyDate(invoice.DueUtc)}",
            status.Label,
            status.Tone,
            status.Tone,
            paymentSummary?.IsPartiallyPaid == true ? "Needs review" : null,
            paymentSummary?.IsPartiallyPaid == true ? "danger" : "neutral",
            isSelected);
    }

    private static InvoiceDetailViewModel ToDetailViewModel(
        FinanceInvoiceDetailResponse invoice,
        FinanceTransactionPaymentContextResponse? listPaymentContext)
    {
        var paymentContext = SelectPaymentContext(invoice.PaymentContext, listPaymentContext);
        var paymentSummary = BuildPaymentSummary(paymentContext, invoice.ProviderStatus, invoice.Amount, invoice.Currency);
        var status = ResolveStatusPresentation(
            invoice.PostingStatus,
            invoice.SettlementStatus,
            invoice.DueStatus,
            invoice.DocumentKind,
            invoice.Status,
            paymentSummary?.IsPartiallyPaid == true,
            paymentSummary?.IsFullyPaid == true);
        var approvalStatus = string.IsNullOrWhiteSpace(invoice.WorkflowContext?.ApprovalStatus)
            ? "Not required"
            : FormatStatusLabel(invoice.WorkflowContext.ApprovalStatus);
        var needsPaymentReview = paymentSummary?.IsPartiallyPaid == true;

        return new InvoiceDetailViewModel(
            string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? "Invoice" : invoice.InvoiceNumber,
            string.IsNullOrWhiteSpace(invoice.CounterpartyName) ? "Customer not available" : invoice.CounterpartyName,
            FormatCurrency(invoice.Amount, invoice.Currency),
            FormatFriendlyDate(invoice.IssuedUtc),
            FormatFriendlyDate(invoice.DueUtc),
            status.Label,
            status.Tone,
            approvalStatus,
            invoice.WorkflowContext?.CanNavigateToApproval == true,
            invoice.WorkflowContext?.ApprovalRequestId,
            invoice.WorkflowContext?.CanNavigateToWorkflow == true,
            invoice.WorkflowContext?.WorkflowInstanceId,
            string.IsNullOrWhiteSpace(invoice.WorkflowContext?.Rationale)
                ? "No review notes are available yet."
                : invoice.WorkflowContext.Rationale,
            FormatDocumentKind(invoice.DocumentKind),
            BuildSourceDetails(invoice.ProviderStatus, invoice.Currency),
            paymentSummary,
            needsPaymentReview ? "Needs review" : null,
            needsPaymentReview ? "danger" : "neutral",
            invoice.RelatedTransactions.Select(ToRelatedTransactionViewModel).ToList());
    }

    private static FinanceTransactionPaymentContextResponse? SelectPaymentContext(
        FinanceTransactionPaymentContextResponse? detailContext,
        FinanceTransactionPaymentContextResponse? listContext)
    {
        if (HasPaymentEvidence(detailContext))
        {
            return detailContext;
        }

        return HasPaymentEvidence(listContext) ? listContext : detailContext ?? listContext;
    }

    private static bool HasPaymentEvidence(FinanceTransactionPaymentContextResponse? paymentContext) =>
        paymentContext is not null &&
        (paymentContext.IsPartiallyPaid ||
         paymentContext.PaidAmount > 0m ||
         paymentContext.RemainingAmount > 0m && paymentContext.RemainingAmount < paymentContext.TotalAmount);

    private string BuildTransactionHref(Guid transactionId) =>
        FinanceRoutes.BuildTransactionDetailPath(transactionId, AccessState.CompanyId);

    private static InvoiceRelatedTransactionViewModel ToRelatedTransactionViewModel(FinanceInvoiceRelatedTransactionResponse transaction)
    {
        var isPayment = IsPaymentTransaction(transaction.TransactionType);
        return new InvoiceRelatedTransactionViewModel(
            transaction.Id,
            FormatFriendlyDate(transaction.TransactionUtc),
            string.IsNullOrWhiteSpace(transaction.Description) ? FormatStatusLabel(transaction.TransactionType) : transaction.Description,
            FormatStatusLabel(transaction.TransactionType),
            string.IsNullOrWhiteSpace(transaction.ExternalReference) ? "No reference" : transaction.ExternalReference,
            FormatCurrency(transaction.Amount, transaction.Currency),
            isPayment ? "Payment" : "Invoice entry",
            isPayment ? "success" : "info");
    }

    private static InvoicePaymentSummaryViewModel? BuildPaymentSummary(
        FinanceTransactionPaymentContextResponse? paymentContext,
        string? providerStatus,
        decimal invoiceAmount,
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
        var providerSummary = BuildPaymentSummaryFromProviderStatus(providerStatus, invoiceAmount, currency);

        if (contextSummary?.IsPartiallyPaid == true || contextSummary?.IsFullyPaid == true || contextSummary?.PaidAmount > 0m)
        {
            return contextSummary;
        }

        return providerSummary ?? contextSummary;
    }

    private static InvoicePaymentSummaryViewModel? BuildPaymentSummaryFromProviderStatus(
        string? providerStatus,
        decimal invoiceAmount,
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
            var totalAmount = decimal.Round(Math.Abs(invoiceAmount), 2, MidpointRounding.AwayFromZero);
            var normalizedRemaining = Math.Max(0m, decimal.Round(Math.Abs(remainingAmount), 2, MidpointRounding.AwayFromZero));
            var paidAmount = Math.Max(0m, decimal.Round(totalAmount - normalizedRemaining, 2, MidpointRounding.AwayFromZero));
            var isPartiallyPaid = paidAmount > 0m && normalizedRemaining > 0m;
            return BuildPaymentSummary(paidAmount, totalAmount, normalizedRemaining, currency, isPartiallyPaid);
        }

        if (TryGetBool(values, "fullyPaid", out var fullyPaid) && fullyPaid)
        {
            var totalAmount = decimal.Round(Math.Abs(invoiceAmount), 2, MidpointRounding.AwayFromZero);
            return BuildPaymentSummary(totalAmount, totalAmount, 0m, currency, false);
        }

        return null;
    }

    private static InvoicePaymentSummaryViewModel BuildPaymentSummary(
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
            ? $"This invoice is partially paid. {FormatCurrency(normalizedPaid, normalizedCurrency)} has been paid and {FormatCurrency(normalizedRemaining, normalizedCurrency)} remains to collect."
            : summary;

        return new InvoicePaymentSummaryViewModel(
            isPartiallyPaid,
            normalizedTotal > 0m && normalizedPaid >= normalizedTotal && normalizedRemaining == 0m,
            normalizedPaid,
            normalizedTotal,
            normalizedRemaining,
            normalizedCurrency,
            summary,
            reason);
    }

    private static IReadOnlyList<SourceDetailViewModel> BuildSourceDetails(string? providerStatus, string currency)
    {
        if (string.IsNullOrWhiteSpace(providerStatus))
        {
            return [];
        }

        var values = ParseProviderStatus(providerStatus);
        if (values.Count == 0)
        {
            return [new SourceDetailViewModel("Provider status", FormatStatusLabel(providerStatus))];
        }

        var details = new List<SourceDetailViewModel>();
        if (TryGetBool(values, "booked", out var booked))
        {
            details.Add(new SourceDetailViewModel("Fortnox posting", booked ? "Booked" : "Not booked"));
        }

        if (TryGetBool(values, "cancelled", out var cancelled) && cancelled)
        {
            details.Add(new SourceDetailViewModel("Fortnox state", "Cancelled"));
        }

        if (TryGetBool(values, "credit", out var credit) && credit)
        {
            details.Add(new SourceDetailViewModel("Fortnox type", "Credit invoice"));
        }

        if (TryGetBool(values, "fullyPaid", out var fullyPaid))
        {
            details.Add(new SourceDetailViewModel("Payment in Fortnox", fullyPaid ? "Fully paid" : "Not fully paid"));
        }

        if (values.TryGetValue("balance", out var balanceValue) &&
            decimal.TryParse(balanceValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var balance))
        {
            details.Add(new SourceDetailViewModel("Balance in Fortnox", FormatCurrency(balance, currency)));
        }

        if (TryGetBool(values, "sent", out var sent))
        {
            details.Add(new SourceDetailViewModel("Sent status", sent ? "Sent to customer" : "Not sent to customer"));
        }

        return details;
    }

    private static Dictionary<string, string> ParseProviderStatus(string providerStatus)
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

    private static bool TryGetBool(IReadOnlyDictionary<string, string> values, string key, out bool result)
    {
        result = false;
        return values.TryGetValue(key, out var value) &&
            bool.TryParse(value, out result);
    }

    private static bool IsPaymentTransaction(string? transactionType)
    {
        var normalized = NormalizeStatusToken(transactionType);
        return normalized is "customer_payment" or "supplier_payment" or "payment";
    }

    private static string FormatCurrency(decimal amount, string currency) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string FormatDate(DateTime value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatFriendlyDate(DateTime value) =>
        value == default ? "Not available" : value.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture);

    private static string FormatDateTime(DateTime value) =>
        value == default
            ? "Unknown time"
            : value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string FormatConfidence(decimal confidence)
    {
        var clamped = Math.Clamp(confidence, 0m, 1m);
        return $"{clamped:P0}";
    }

    private static string FormatActorOrSource(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "System" : value.Trim();

    private static string FormatStatusLabel(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Unknown"
            : string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToLowerInvariant() is { } normalized
                ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized)
                : "Unknown";

    private static string FormatInvoiceStatusLabel(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal) ?? string.Empty;
        return normalized == "approved" ? "Booked" : FormatStatusLabel(value);
    }

    private static InvoiceStatusPresentation ResolveStatusPresentation(
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

        if (kind == "credit_note")
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
            "pending_approval" or "pending" or "review" or "needs_review" => new("Pending approval", "warning"),
            "problem" or "rejected" => new("Overdue", "danger"),
            _ => new(string.IsNullOrWhiteSpace(fallbackStatus) ? "Unknown" : FormatStatusLabel(fallbackStatus), "neutral")
        };
    }

    private static string NormalizeStatusToken(string? value) =>
        value?.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal) ?? string.Empty;

    private static string FormatDocumentKind(string? value) =>
        NormalizeStatusToken(value) switch
        {
            "credit_note" => "Credit note",
            "invoice" => "Invoice",
            _ => FormatStatusLabel(value)
        };

    private sealed record InvoiceStatusPresentation(string Label, string Tone);

    private sealed record InvoiceListItemViewModel(
        Guid Id,
        string DisplayInvoiceNumber,
        string DisplayCustomerName,
        string DisplayAmount,
        string DisplayIssuedDate,
        string DisplayDueDate,
        string DateSummary,
        string FriendlyStatusLabel,
        string StatusTone,
        string IconTone,
        string? ReviewBadgeLabel,
        string ReviewBadgeTone,
        bool IsSelected);

    private sealed record InvoiceDetailViewModel(
        string DisplayInvoiceNumber,
        string DisplayCustomerName,
        string DisplayAmount,
        string DisplayIssuedDate,
        string DisplayDueDate,
        string FriendlyStatusLabel,
        string StatusTone,
        string ApprovalStatus,
        bool CanOpenApproval,
        Guid? ApprovalRequestId,
        bool CanOpenReview,
        Guid? WorkflowInstanceId,
        string ReviewSummary,
        string DocumentKindLabel,
        IReadOnlyList<SourceDetailViewModel> SourceDetails,
        InvoicePaymentSummaryViewModel? PaymentSummary,
        string? ReviewBadgeLabel,
        string ReviewBadgeTone,
        IReadOnlyList<InvoiceRelatedTransactionViewModel> RelatedTransactions);

    private sealed record SourceDetailViewModel(string Label, string Value);

    private sealed record InvoicePaymentSummaryViewModel(
        bool IsPartiallyPaid,
        bool IsFullyPaid,
        decimal PaidAmount,
        decimal TotalAmount,
        decimal RemainingAmount,
        string Currency,
        string Summary,
        string ReviewReason);

    private sealed record InvoiceRelatedTransactionViewModel(
        Guid Id,
        string DisplayDate,
        string Description,
        string CategoryLabel,
        string ExternalReference,
        string DisplayAmount,
        string TypeLabel,
        string TypeTone);
}
