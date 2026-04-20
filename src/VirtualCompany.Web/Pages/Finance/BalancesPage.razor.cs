using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class BalancesPage : FinanceSummaryPageBase<BalancesSummaryViewModel>
{
    protected override async Task<BalancesSummaryViewModel?> LoadSummaryViewModelAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var referenceUtc = await FinanceApiClient.GetFinanceReferenceUtcAsync(companyId, cancellationToken);
        var balances = await FinanceApiClient.GetBalancesAsync(companyId, referenceUtc, cancellationToken);
        return FinanceSummaryPresenter.ToBalancesViewModel(companyId, referenceUtc, balances);
    }
}
