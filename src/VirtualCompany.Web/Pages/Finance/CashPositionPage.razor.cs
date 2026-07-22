using System.Globalization;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class CashPositionPage : FinanceSummaryPageBase<CashPositionSummaryViewModel>
{
    private string DashboardHref => AccessState.CompanyId is Guid companyId ? $"/dashboard?companyId={companyId:D}" : "/dashboard";

    private string FormattedAsOf => ViewModel is null ? FinanceText["Now"] : FormatCashPositionDate(ViewModel.AsOf);

    protected override async Task<CashPositionSummaryViewModel?> LoadSummaryViewModelAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var referenceUtc = await FinanceApiClient.GetFinanceReferenceUtcAsync(companyId, cancellationToken);
        var response = await FinanceApiClient.GetCashPositionAsync(companyId, referenceUtc, cancellationToken);
        return FinanceSummaryPresenter.ToCashPositionViewModel(response);
    }

    private string FormatCashPositionDate(string value)
    {
        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd HH:mm 'UTC'",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var asOfUtc)
            ? LocalDateTime.DateTime(asOfUtc)
            : value;
    }
}
