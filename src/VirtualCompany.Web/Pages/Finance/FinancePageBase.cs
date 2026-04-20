using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public abstract class FinancePageBase : ComponentBase
{
    [Inject] protected OnboardingApiClient ApiClient { get; set; } = default!;
    [Inject] protected FinanceAccessResolver FinanceAccessResolver { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = FinanceRoutes.CompanyIdQueryKey)]
    public Guid? CompanyId { get; set; }

    [SupplyParameterFromQuery(Name = DashboardRoutes.SourceQueryKey)]
    public string? Source { get; set; }

    [SupplyParameterFromQuery(Name = DashboardRoutes.ActionQueryKey)]
    public string? Action { get; set; }

    [SupplyParameterFromQuery(Name = DashboardRoutes.RangeQueryKey)]
    public string? Range { get; set; }

    protected bool IsLoading { get; private set; } = true;
    protected CurrentUserContextViewModel? CurrentUserContext { get; private set; }
    protected string? ErrorMessage { get; private set; }
    protected FinanceAccessState AccessState { get; private set; } =
        FinanceAccessState.Forbidden("Finance access is unavailable.");

    protected override async Task OnParametersSetAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            CurrentUserContext = await ApiClient.GetCurrentUserContextAsync();

            AccessState = FinanceAccessResolver.Resolve(CurrentUserContext, CompanyId);

            // Normalize direct finance navigation onto an explicit company-scoped URL.
            if (AccessState.IsAllowed && CompanyId is null && AccessState.CompanyId is Guid resolvedCompanyId)
            {
                Navigation.NavigateTo(Navigation.GetUriWithQueryParameter(FinanceRoutes.CompanyIdQueryKey, resolvedCompanyId), replace: true);
            }
        }
        catch (OnboardingApiException ex)
        {
            ErrorMessage = ex.Message;
            CurrentUserContext = null;
            AccessState = FinanceAccessState.Forbidden("Finance is unavailable while the active company context cannot be resolved.");
        }
        finally { IsLoading = false; }
    }
}
