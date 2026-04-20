using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class MonthlySummaryPage : FinanceSummaryPageBase<MonthlySummaryViewModel>
{
    protected override async Task<MonthlySummaryViewModel?> LoadSummaryViewModelAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var monthlySummary = await FinanceApiClient.GetMonthlySummaryAsync(companyId, cancellationToken: cancellationToken);
        return FinanceSummaryPresenter.ToMonthlySummaryViewModel(monthlySummary);
    }
}
