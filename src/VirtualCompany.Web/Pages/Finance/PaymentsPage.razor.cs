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
    private string DashboardHref => AccessState.CompanyId is Guid companyId ? $"/dashboard?companyId={companyId:D}" : "/dashboard";
    private string LauraHref => AccessState.CompanyId is Guid companyId ? $"/agents?companyId={companyId:D}&agent=Laura" : "/agents";
    private IReadOnlyList<PaymentListItemViewModel> PaymentItems =>
        Payments.Select(payment => ToListItem(payment, PaymentId == payment.Id)).ToList();
    private PaymentDetailViewModel? SelectedPaymentDisplay =>
        SelectedPayment is null ? null : ToDetailViewModel(SelectedPayment);

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

    private bool IsTypeSelected(string option) =>
        string.Equals(TypeFilterValue, option, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    private static string FormatCurrency(decimal amount, string currency) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string FormatFriendlyDate(DateTime value) =>
        value == default ? "Not available" : value.ToString("MMM dd, yyyy", CultureInfo.InvariantCulture);

    private static string FormatLabel(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "Not available"
            : string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToLowerInvariant() is { } normalized
                ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized)
                : "Not available";

    private static string FormatPaymentMethod(string? value)
    {
        var normalized = NormalizeOptionalText(value);
        return normalized switch
        {
            "ach" => "ACH",
            "bank_transfer" => "Bank transfer",
            "card" => "Card",
            "wire" or "wire_transfer" => "Wire transfer",
            null => "Not available",
            _ => FormatLabel(normalized)
        };
    }

    private static PaymentStatusPresentation ResolveStatusPresentation(string? status)
    {
        var normalized = NormalizeOptionalText(status) ?? string.Empty;
        return normalized switch
        {
            "completed" or "paid" or "settled" => new("Completed", "success"),
            "pending" or "processing" or "scheduled" => new("Pending", "warning"),
            "failed" or "cancelled" or "rejected" => new("Failed", "danger"),
            _ => new(string.IsNullOrWhiteSpace(status) ? "Unknown" : FormatLabel(status), "neutral")
        };
    }

    private static PaymentTypePresentation ResolveTypePresentation(string? paymentType)
    {
        var normalized = NormalizeOptionalText(paymentType) ?? string.Empty;
        return normalized switch
        {
            "incoming" => new("Incoming", "success", "+"),
            "outgoing" => new("Outgoing", "warning", "-"),
            _ => new(string.IsNullOrWhiteSpace(paymentType) ? "Payment" : FormatLabel(paymentType), "neutral", "?")
        };
    }

    private static PaymentListItemViewModel ToListItem(FinancePaymentResponse payment, bool isSelected)
    {
        var status = ResolveStatusPresentation(payment.Status);
        var type = ResolveTypePresentation(payment.PaymentType);
        var method = FormatPaymentMethod(payment.Method);
        var reference = string.IsNullOrWhiteSpace(payment.CounterpartyReference) ? "Payment" : payment.CounterpartyReference;

        return new PaymentListItemViewModel(
            payment.Id,
            reference,
            method,
            FormatFriendlyDate(payment.PaymentDate),
            FormatCurrency(payment.Amount, payment.Currency),
            status.Label,
            status.Tone,
            type.Tone,
            type.IconText,
            isSelected);
    }

    private static PaymentDetailViewModel ToDetailViewModel(FinancePaymentResponse payment)
    {
        var status = ResolveStatusPresentation(payment.Status);
        var type = ResolveTypePresentation(payment.PaymentType);
        var reference = string.IsNullOrWhiteSpace(payment.CounterpartyReference) ? "Payment" : payment.CounterpartyReference;

        return new PaymentDetailViewModel(
            reference,
            type.Label,
            FormatCurrency(payment.Amount, payment.Currency),
            status.Label,
            status.Tone,
            FormatPaymentMethod(payment.Method),
            FormatFriendlyDate(payment.PaymentDate));
    }

    private sealed record PaymentStatusPresentation(string Label, string Tone);

    private sealed record PaymentTypePresentation(string Label, string Tone, string IconText);

    private sealed record PaymentListItemViewModel(
        Guid Id,
        string Reference,
        string MethodLabel,
        string DisplayDate,
        string DisplayAmount,
        string StatusLabel,
        string StatusTone,
        string IconTone,
        string IconText,
        bool IsSelected);

    private sealed record PaymentDetailViewModel(
        string Reference,
        string PaymentTypeLabel,
        string DisplayAmount,
        string StatusLabel,
        string StatusTone,
        string MethodLabel,
        string DisplayDate);
}
