using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class PaymentsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    [Parameter]
    public Guid? PaymentId { get; set; }

    [SupplyParameterFromQuery(Name = "type")]
    public string? Type { get; set; }

    private IReadOnlyList<FinancePaymentResponse> Payments { get; set; } = [];
    private FinancePaymentResponse? SelectedPayment { get; set; }
    private bool IsListLoading { get; set; }
    private bool IsDetailLoading { get; set; }
    private string? ListErrorMessage { get; set; }
    private string? DetailErrorMessage { get; set; }

    private bool IsListEmpty => !IsListLoading && string.IsNullOrWhiteSpace(ListErrorMessage) && Payments.Count == 0;
    private string? TypeFilterValue => NormalizeOptionalText(Type);
    private string ClearFiltersHref => FinanceRoutes.WithCompanyContext(FinanceRoutes.Payments, AccessState.CompanyId);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Payments = [];
        SelectedPayment = null;
        ListErrorMessage = null;
        DetailErrorMessage = null;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadPaymentsAsync(companyId);

        if (PaymentId is Guid paymentId)
        {
            await LoadDetailAsync(companyId, paymentId);
        }
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadPaymentsAsync(companyId);
        if (PaymentId is Guid paymentId)
        {
            await LoadDetailAsync(companyId, paymentId);
        }
    }

    private async Task LoadPaymentsAsync(Guid companyId)
    {
        IsListLoading = true;
        ListErrorMessage = null;

        try
        {
            Payments = await FinanceApiClient.GetPaymentsAsync(companyId, TypeFilterValue, 200);
        }
        catch (FinanceApiException ex)
        {
            Payments = [];
            ListErrorMessage = ex.Message;
        }
        finally
        {
            IsListLoading = false;
        }
    }

    private async Task LoadDetailAsync(Guid companyId, Guid paymentId)
    {
        IsDetailLoading = true;
        DetailErrorMessage = null;

        try
        {
            SelectedPayment = await FinanceApiClient.GetPaymentDetailAsync(companyId, paymentId);
            if (SelectedPayment is null)
            {
                DetailErrorMessage = "The selected payment could not be found in the active company context.";
            }
        }
        catch (FinanceApiException ex)
        {
            SelectedPayment = null;
            DetailErrorMessage = ex.Message;
        }
        finally
        {
            IsDetailLoading = false;
        }
    }

    private string BuildPaymentHref(Guid paymentId)
    {
        var path = FinanceRoutes.BuildPaymentDetailPath(paymentId, null);
        var query = new List<string> { $"{FinanceRoutes.CompanyIdQueryKey}={AccessState.CompanyId}" };
        if (!string.IsNullOrWhiteSpace(TypeFilterValue))
        {
            query.Add($"type={Uri.EscapeDataString(TypeFilterValue)}");
        }

        return $"{path}?{string.Join("&", query)}";
    }

    private string GetPaymentListItemClass(Guid paymentId) =>
        PaymentId == paymentId
            ? "list-group-item list-group-item-action active"
            : "list-group-item list-group-item-action";

    private bool IsTypeSelected(string option) =>
        string.Equals(TypeFilterValue, option, StringComparison.OrdinalIgnoreCase);

    private static string GetStatusBadgeClass(string status) =>
        string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
            ? "text-bg-success"
            : string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase)
                ? "text-bg-danger"
                : "text-bg-warning";

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    private static string FormatCurrency(decimal amount, string currency) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string FormatDate(DateTime value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatLabel(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "n/a"
            : string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}