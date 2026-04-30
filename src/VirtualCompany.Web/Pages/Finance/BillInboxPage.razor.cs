using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class BillInboxPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    private IReadOnlyList<FinanceBillInboxRowResponse> Items { get; set; } = [];
    private bool IsListLoading { get; set; }
    private string? ListErrorMessage { get; set; }
    private bool IsListEmpty => !IsListLoading && string.IsNullOrWhiteSpace(ListErrorMessage) && Items.Count == 0;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Items = [];
        ListErrorMessage = null;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadAsync(companyId);
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is Guid companyId)
        {
            await LoadAsync(companyId);
        }
    }

    private async Task LoadAsync(Guid companyId)
    {
        IsListLoading = true;
        ListErrorMessage = null;

        try
        {
            Items = await FinanceApiClient.GetBillInboxAsync(companyId, 200);
        }
        catch (FinanceApiException ex)
        {
            Items = [];
            ListErrorMessage = ex.Message;
        }
        finally
        {
            IsListLoading = false;
        }
    }

    private string BuildDetailHref(Guid billId) => FinanceRoutes.BuildBillInboxDetailPath(billId, AccessState.CompanyId);

    private static string FormatDate(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatAmount(decimal? amount, string? currency) =>
        amount.HasValue ? $"{currency ?? string.Empty} {amount.Value.ToString("N2", CultureInfo.InvariantCulture)}".Trim() : "n/a";

    private static string FormatWarnings(FinanceBillInboxRowResponse item) =>
        item.ValidationWarningCount == 0 && item.DuplicateWarningCount == 0
            ? "None"
            : $"{item.ValidationWarningCount} validation, {item.DuplicateWarningCount} duplicate";
}