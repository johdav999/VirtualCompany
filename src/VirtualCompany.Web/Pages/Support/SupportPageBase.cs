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

    [SupplyParameterFromQuery(Name = "returnUrl")] public string? ReturnUrl { get; set; }
    [SupplyParameterFromQuery(Name = "supportReturnUrl")] public string? SupportReturnUrl { get; set; }
    [SupplyParameterFromQuery(Name = "healthReturnUrl")] public string? HealthReturnUrl { get; set; }
    protected string? HealthPath => ResolvedCompanyId is Guid id ? DashboardRoutes.NormalizeHealthPath(HealthReturnUrl, id) : null;
    protected string BackPath => ResolvedCompanyId is Guid id
        ? SupportJourneyRoutes.NormalizeReturn(SupportReturnUrl, id) ?? BuildPath("/support") : "/support";
    protected string OverviewPath => ResolvedCompanyId is Guid id
        ? DashboardRoutes.NormalizeOverviewPath(ReturnUrl, id) ?? DashboardRoutes.BuildTodayPath(id, "customers") : "/dashboard";
    protected string RecordPath(string path) => ResolvedCompanyId is Guid id
        ? SupportJourneyRoutes.Record(path, id, Navigation.Uri) : path;
    protected Guid? ResolvedCompanyId { get; private set; }
    protected string? ShellErrorMessage { get; private set; }

    private int resolveVersion;
    protected async Task<bool> ResolveCompanyAsync(CancellationToken cancellationToken = default)
    {
        var version = ++resolveVersion; var location = Navigation.Uri;
        ShellErrorMessage = null;
        ResolvedCompanyId = null;

        try
        {
            var context = await OnboardingApiClient.GetCurrentUserContextAsync(CompanyId, cancellationToken);
            if (version != resolveVersion || location != Navigation.Uri) return false;
            ResolvedCompanyId = CompanyId ?? context?.ActiveCompany?.CompanyId ?? context?.Memberships.FirstOrDefault()?.CompanyId;
            if (ResolvedCompanyId is not Guid companyId || companyId == Guid.Empty)
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
        ResolvedCompanyId is Guid companyId ? SupportJourneyRoutes.Build(path, companyId, ReturnUrl, SupportReturnUrl) : path;

    protected async Task<Guid?> ResolveSupportAgentIdAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var roster = await AgentApiClient.GetRosterAsync(companyId, cancellationToken);
        return roster.FirstOrDefault(x =>
            string.Equals(x.Department, "Support", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Status, "active", StringComparison.OrdinalIgnoreCase))?.Id;
    }
}
