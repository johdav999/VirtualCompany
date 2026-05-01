using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class BillsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    [Parameter]
    public Guid? BillId { get; set; }

    private IReadOnlyList<FinanceBillResponse> Bills { get; set; } = [];
    private FinanceBillDetailResponse? SelectedBill { get; set; }
    private bool IsListLoading { get; set; }
    private bool IsDetailLoading { get; set; }
    private string? ListErrorMessage { get; set; }
    private string? DetailErrorMessage { get; set; }

    private bool IsListEmpty => !IsListLoading && string.IsNullOrWhiteSpace(ListErrorMessage) && Bills.Count == 0;
    private IReadOnlyList<BillListItemViewModel> BillItems =>
        Bills.Select(bill => ToListItem(bill, BillId == bill.Id)).ToList();
    private BillDetailViewModel? SelectedBillDisplay =>
        SelectedBill is null ? null : ToDetailViewModel(SelectedBill);
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

    private static string FormatCurrency(decimal amount, string currency) => $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string FormatFriendlyDate(DateTime value) =>
        value == default ? "Not available" : value.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture);

    private static string FormatStatusLabel(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Unknown"
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
                string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .ToLowerInvariant());

    private static BillListItemViewModel ToListItem(FinanceBillResponse bill, bool isSelected)
    {
        var status = ResolveStatusPresentation(bill.Status);
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
            isSelected);
    }

    private static BillDetailViewModel ToDetailViewModel(FinanceBillDetailResponse bill)
    {
        var status = ResolveStatusPresentation(bill.Status);
        return new BillDetailViewModel(
            string.IsNullOrWhiteSpace(bill.BillNumber) ? "Bill" : bill.BillNumber,
            string.IsNullOrWhiteSpace(bill.CounterpartyName) ? "Supplier not available" : bill.CounterpartyName,
            FormatCurrency(bill.Amount, bill.Currency),
            FormatFriendlyDate(bill.ReceivedUtc),
            FormatFriendlyDate(bill.DueUtc),
            status.Label,
            status.Tone,
            ResolveApprovalStatus(bill.Status),
            bill.AgentInsights.Count == 0
                ? "No warnings are linked to this bill."
                : $"{bill.AgentInsights.Count} finance note{(bill.AgentInsights.Count == 1 ? string.Empty : "s")} linked to this bill.");
    }

    private static BillStatusPresentation ResolveStatusPresentation(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal) ?? string.Empty;
        return normalized switch
        {
            "paid" => new("Paid", "success"),
            "open" => new("Open", "warning"),
            "pending_approval" or "pending" or "approval_pending" => new("Pending approval", "info"),
            "overdue" or "problem" or "failed" => new("Overdue", "danger"),
            _ => new(string.IsNullOrWhiteSpace(status) ? "Unknown" : FormatStatusLabel(status), "neutral")
        };
    }

    private static string ResolveApprovalStatus(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant().Replace("-", "_", StringComparison.Ordinal) ?? string.Empty;
        return normalized switch
        {
            "pending_approval" or "approval_pending" => "Pending approval",
            "approved" or "paid" => "Approved",
            _ => "Not required"
        };
    }

    private sealed record BillStatusPresentation(string Label, string Tone);

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
        string WarningSummary);
}
