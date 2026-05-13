using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Sales;

public abstract class SalesPageBase : ComponentBase
{
    [Inject] protected OnboardingApiClient OnboardingApiClient { get; set; } = default!;
    [Inject] protected SalesApiClient SalesApiClient { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "companyId")]
    public Guid? CompanyId { get; set; }

    protected Guid? ResolvedCompanyId { get; private set; }
    protected string? ShellErrorMessage { get; private set; }
    protected SalesDashboardResponse? AgentPanelDashboard { get; set; }
    protected string? AgentPanelErrorMessage { get; set; }

    protected async Task<bool> ResolveCompanyAsync(CancellationToken cancellationToken = default)
    {
        ShellErrorMessage = null;

        try
        {
            var context = await OnboardingApiClient.GetCurrentUserContextAsync(cancellationToken);
            ResolvedCompanyId = CompanyId ?? context?.ActiveCompany?.CompanyId ?? context?.Memberships.FirstOrDefault()?.CompanyId;
            if (ResolvedCompanyId is not Guid companyId)
            {
                ShellErrorMessage = "Choose or create a company before opening the sales workspace.";
                return false;
            }

            if (CompanyId is null)
            {
                Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("companyId", companyId), replace: true);
            }

            return true;
        }
        catch (OnboardingApiException ex)
        {
            ShellErrorMessage = ex.Message;
            return false;
        }
    }

    protected async Task RefreshAgentPanelAsync(CancellationToken cancellationToken = default)
    {
        if (ResolvedCompanyId is not Guid companyId)
        {
            return;
        }

        AgentPanelErrorMessage = null;
        AgentPanelDashboard = await SalesApiClient.GetDashboardAsync(companyId, cancellationToken);
    }
}
