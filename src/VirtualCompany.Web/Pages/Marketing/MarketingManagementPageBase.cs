using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Pages.Marketing;

public abstract class MarketingManagementPageBase : ComponentBase
{
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [Inject] protected OnboardingApiClient Onboarding { get; set; } = null!;
    [SupplyParameterFromQuery] public Guid? CompanyId { get; set; }
    protected async Task<Guid> Company(CancellationToken ct)
    {
        if (CompanyId is { } company && company != Guid.Empty) return company;
        var context = await Onboarding.GetCurrentUserContextAsync(null, ct);
        var current = context?.ActiveCompany?.CompanyId ?? context?.Memberships.FirstOrDefault()?.CompanyId;
        if (current is null || current == Guid.Empty) throw new InvalidOperationException("Choose a company before opening Marketing reports.");
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("companyId", current), replace: true);
        return current.Value;
    }
    protected string Path(string path, Guid company) => MarketingJourneyRoutes.Build(path, company, Navigation.Uri, returnToCurrent: true);
    protected string? Previous(Guid company) => MarketingJourneyRoutes.NormalizeRecordReturn(
        System.Web.HttpUtility.ParseQueryString(new Uri(Navigation.Uri).Query)["recordReturnUrl"], company);
    protected static bool Expected(Exception ex) => ex is HttpRequestException or InvalidOperationException or InvalidDataException or
        TodayWorkspaceAccessException or OnboardingApiException or OperationCanceledException or JSException;
    protected static string Error(Exception ex) => ex is TodayWorkspaceAccessException ? "Marketing access changed; results withheld." :
        ex is OperationCanceledException ? "The connection timed out. Retry with the same saved request identity." : ex.Message;
}
