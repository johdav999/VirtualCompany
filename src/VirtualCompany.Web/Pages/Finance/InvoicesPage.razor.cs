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
        SelectedInvoice is null ? null : ToDetailViewModel(SelectedInvoice);
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
            StatusSaveMessage = $"Invoice status saved as {FormatStatusLabel(SelectedInvoice?.Status)}.";
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
        var status = ResolveStatusPresentation(invoice.Status);
        return new InvoiceListItemViewModel(
            invoice.Id,
            string.IsNullOrWhiteSpace(invoice.InvoiceNumber) ? "Invoice" : invoice.InvoiceNumber,
            string.IsNullOrWhiteSpace(invoice.CounterpartyName) ? "Customer not available" : invoice.CounterpartyName,
            FormatCurrency(invoice.Amount, invoice.Currency),
            FormatFriendlyDate(invoice.IssuedUtc),
            FormatFriendlyDate(invoice.DueUtc),
            $"Issued {FormatFriendlyDate(invoice.IssuedUtc)} · Due {FormatFriendlyDate(invoice.DueUtc)}",
            status.Label,
            status.Tone,
            status.Tone,
            isSelected);
    }

    private static InvoiceDetailViewModel ToDetailViewModel(FinanceInvoiceDetailResponse invoice)
    {
        var status = ResolveStatusPresentation(invoice.Status);
        var approvalStatus = string.IsNullOrWhiteSpace(invoice.WorkflowContext?.ApprovalStatus)
            ? "Not required"
            : FormatStatusLabel(invoice.WorkflowContext.ApprovalStatus);

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
                : invoice.WorkflowContext.Rationale);
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

    private static InvoiceStatusPresentation ResolveStatusPresentation(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal) ?? string.Empty;
        return normalized switch
        {
            "approved" => new("Approved", "success"),
            "pending_approval" or "pending" or "review" or "needs_review" => new("Pending approval", "warning"),
            "paid" => new("Paid", "info"),
            "overdue" or "problem" or "rejected" => new("Overdue", "danger"),
            "draft" => new("Draft", "neutral"),
            _ => new(string.IsNullOrWhiteSpace(status) ? "Unknown" : FormatStatusLabel(status), "neutral")
        };
    }

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
        string ReviewSummary);
}
