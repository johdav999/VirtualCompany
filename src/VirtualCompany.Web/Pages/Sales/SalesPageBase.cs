using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Sales;

public abstract class SalesPageBase : ComponentBase
{
    [Inject] protected OnboardingApiClient OnboardingApiClient { get; set; } = default!;
    [Inject] protected SalesApiClient SalesApiClient { get; set; } = default!;
    [Inject] protected AgentApiClient AgentApiClient { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "companyId")]
    public Guid? CompanyId { get; set; }

    protected Guid? ResolvedCompanyId { get; private set; }
    protected string? ShellErrorMessage { get; private set; }
    protected SalesDashboardResponse? AgentPanelDashboard { get; set; }
    protected string? AgentPanelErrorMessage { get; set; }
    protected string BuildSalesPath(string path) => ResolvedCompanyId is Guid company
        ? SalesJourneyRoutes.Build(path, company, Navigation.Uri) : path;

    protected async Task<bool> ResolveCompanyAsync(CancellationToken cancellationToken = default)
    {
        var location = Navigation.Uri;
        var requestedCompany = CompanyId;
        ResolvedCompanyId = null;
        ShellErrorMessage = null;

        try
        {
            var context = await OnboardingApiClient.GetCurrentUserContextAsync(CompanyId, cancellationToken);
            if (location != Navigation.Uri || requestedCompany != CompanyId) return false;
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

    private int readVersion;
    protected (int Version, string Location) BeginSalesRead() => (++readVersion, Navigation.Uri);
    protected bool SalesReadIsCurrent((int Version, string Location) read) => read.Version == readVersion && Navigation.Uri == read.Location;

    protected async Task RefreshAgentPanelAsync(CancellationToken cancellationToken = default)
    {
        if (ResolvedCompanyId is not Guid companyId)
        {
            return;
        }

        AgentPanelErrorMessage = null;
        var location = Navigation.Uri;
        var result = await SalesApiClient.GetDashboardAsync(companyId, cancellationToken);
        if (ResolvedCompanyId == companyId && Navigation.Uri == location) AgentPanelDashboard = result;
    }

    protected async Task<Guid?> ResolveSalesAgentIdAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var roster = await AgentApiClient.GetRosterAsync(companyId, cancellationToken);
        return roster.FirstOrDefault(x =>
            string.Equals(x.Department, "Sales", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase))?.Id;
    }
}
