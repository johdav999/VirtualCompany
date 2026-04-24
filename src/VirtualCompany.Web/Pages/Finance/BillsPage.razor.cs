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
                DetailErrorMessage = "The selected bill could not be found in the active company context.";
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

    private string GetBillListItemClass(Guid billId) =>
        BillId == billId
            ? "list-group-item list-group-item-action active"
            : "list-group-item list-group-item-action";

    private static string FormatCurrency(decimal amount, string currency) => $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";
    private static string FormatDate(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string FormatLabel(string? value) => string.IsNullOrWhiteSpace(value) ? "n/a" : string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}