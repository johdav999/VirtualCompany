using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Pages.Finance;

public abstract class FinancePlanningPageBase : ComponentBase, IDisposable
{
    [Inject] protected FinanceRollingPlanningApiClient Planning { get; set; } = null!;
    [Inject] protected OnboardingApiClient Onboarding { get; set; } = null!;
    [Inject] protected NavigationManager Navigation { get; set; } = null!;
    [SupplyParameterFromQuery] public Guid? CompanyId { get; set; }
    [SupplyParameterFromQuery] public int? Year { get; set; }
    [SupplyParameterFromQuery] public int? Month { get; set; }
    [SupplyParameterFromQuery] public int? Months { get; set; }
    [SupplyParameterFromQuery] public string? BudgetVersion { get; set; }
    [SupplyParameterFromQuery] public string? ForecastVersion { get; set; }
    [SupplyParameterFromQuery] public string? Currency { get; set; }
    [SupplyParameterFromQuery] public Guid? FinanceAccountId { get; set; }
    [SupplyParameterFromQuery] public Guid? CostCenterId { get; set; }
    [SupplyParameterFromQuery] public Guid? FiscalPeriodId { get; set; }
    protected Guid Company;
    protected FinancePlanningReport? Report;
    protected bool Busy;
    protected string? Error;
    protected CancellationTokenSource Reads = new();
    private bool disposed;
    protected virtual void Restricted() => Report = null;
    protected string Link(string path) => FinanceJourneyRoutes.Build(path, Company, Navigation.Uri);
    protected string Back => FinanceJourneyRoutes.Back(Navigation.Uri, Company);
    protected FinancePlanningQuery Query(int defaultMonths = 1) => new(Year ?? DateTime.UtcNow.Year, Month ?? DateTime.UtcNow.Month,
        Months ?? defaultMonths, Text(BudgetVersion), Text(ForecastVersion), Text(Currency)?.ToUpperInvariant(), FinanceAccountId, CostCenterId, FiscalPeriodId);
    protected static string? Text(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
    protected static string Money(decimal? x) => x?.ToString("N2") ?? "Unavailable";
    protected abstract Task Load(CancellationToken ct);
    protected override async Task OnParametersSetAsync() => await Reload();
    protected async Task Reload()
    {
        Reads.Cancel(); Reads.Dispose(); Reads = new(); Report = null;
        var token = Reads.Token;
        await Run(async () => {
            Company = CompanyId ?? Guid.Empty;
            if(Company == Guid.Empty) { var context = await Onboarding.GetCurrentUserContextAsync(null, token);
                Company = context?.ActiveCompany?.CompanyId ?? context?.Memberships.FirstOrDefault()?.CompanyId ?? Guid.Empty;
                if(Company == Guid.Empty) throw new InvalidOperationException("Choose a company before opening Finance planning.");
                Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("companyId", Company), replace:true); return; }
            await Load(token);
        });
    }
    protected async Task Run(Func<Task> action)
    {
        var token = Reads.Token; var location = Navigation.Uri; Busy = true; Error = null;
        try { await action(); }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
        catch(Exception ex) when(ex is HttpRequestException or InvalidOperationException or InvalidDataException or ArgumentException or TodayWorkspaceAccessException or OnboardingApiException or JSException or OperationCanceledException)
        { if(!disposed && !token.IsCancellationRequested && Navigation.Uri == location) { Error = ex is TodayWorkspaceAccessException ? "Finance access changed; retained and current results are withheld." : ex is JSException ? "The CSV could not be downloaded. Try the download again." : ex.Message;
            if(ex is TodayWorkspaceAccessException) Restricted(); } }
        finally { if(!disposed && !token.IsCancellationRequested && Navigation.Uri == location) Busy = false; }
    }
    public void Dispose() { disposed = true; Reads.Cancel(); Reads.Dispose(); }
}
