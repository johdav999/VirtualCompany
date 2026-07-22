using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Support;

public abstract class SupportPageBase : ComponentBase
{
    [Inject] protected OnboardingApiClient OnboardingApiClient { get; set; } = default!;
    [Inject] protected SupportApiClient SupportApiClient { get; set; } = default!;
    [Inject] protected AgentApiClient AgentApiClient { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "companyId")]
    public Guid? CompanyId { get; set; }

    protected Guid? ResolvedCompanyId { get; private set; }
    protected string? ShellErrorMessage { get; private set; }

    protected async Task<bool> ResolveCompanyAsync(CancellationToken cancellationToken = default)
    {
        ShellErrorMessage = null;

        try
        {
            var context = await OnboardingApiClient.GetCurrentUserContextAsync(cancellationToken);
            ResolvedCompanyId = CompanyId ?? context?.ActiveCompany?.CompanyId ?? context?.Memberships.FirstOrDefault()?.CompanyId;
            if (ResolvedCompanyId is not Guid companyId)
            {
                ShellErrorMessage = "Choose or create a company before opening support.";
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

    protected string BuildPath(string path) =>
        ResolvedCompanyId is Guid companyId ? $"{path}?companyId={companyId:D}" : path;

    protected async Task<Guid?> ResolveSupportAgentIdAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var roster = await AgentApiClient.GetRosterAsync(companyId, cancellationToken);
        return roster.FirstOrDefault(x =>
            string.Equals(x.Department, "Support", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase))?.Id;
    }
}
