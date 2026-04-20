using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class CashPositionPage : FinanceSummaryPageBase<CashPositionSummaryViewModel>
{
    protected override async Task<CashPositionSummaryViewModel?> LoadSummaryViewModelAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var referenceUtc = await FinanceApiClient.GetFinanceReferenceUtcAsync(companyId, cancellationToken);
        var response = await FinanceApiClient.GetCashPositionAsync(companyId, referenceUtc, cancellationToken);
        return FinanceSummaryPresenter.ToCashPositionViewModel(response);
    }
}
