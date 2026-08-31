using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AdvancedAccountingPage : FinancePageBase
{
    private static readonly HashSet<string> SupportedWorkspaces =
        ["currency-rates", "dimensions", "schedules", "fixed-assets", "revaluation"];

    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [Parameter] public string? Workspace { get; set; }
    [SupplyParameterFromQuery(Name = "runId")] public Guid? RequestedRunId { get; set; }

    private List<AccountingPeriodResponse> Periods { get; set; } = [];
    private Guid SelectedPeriodId { get; set; }
    private AccountingPeriodResponse? SelectedPeriod => Periods.FirstOrDefault(x => x.Id == SelectedPeriodId);
    private string ActiveWorkspace { get; set; } = "currency-rates";
    private string FunctionalCurrency { get; set; } = "SEK";
    private string? WorkspaceError { get; set; }
    private Guid? LoadedCompanyId { get; set; }
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private bool CanApproveSchedules => FinanceAccess.CanApproveInvoices(AccessState.MembershipRole);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        ActiveWorkspace = ResolveWorkspace();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId || companyId == LoadedCompanyId) return;

        LoadedCompanyId = companyId;
        await LoadContextAsync(companyId);
    }

    private async Task LoadContextAsync(Guid companyId)
    {
        WorkspaceError = null;
        try
        {
            var yearsTask = FinanceApiClient.GetAccountingFiscalYearsAsync(companyId);
            var readinessTask = FinanceApiClient.GetExchangeRateReadinessAsync(companyId);
            await Task.WhenAll(yearsTask, readinessTask);
            Periods = (await yearsTask).SelectMany(x => x.Periods).OrderByDescending(x => x.StartDate).ToList();
            SelectedPeriodId = Periods.FirstOrDefault(x => !x.IsClosed)?.Id ?? Periods.FirstOrDefault()?.Id ?? Guid.Empty;
            FunctionalCurrency = (await readinessTask)?.FunctionalCurrency ?? FunctionalCurrency;
        }
        catch (FinanceApiException exception)
        {
            WorkspaceError = exception.Message;
        }
    }

    private Task RetryContextAsync() => AccessState.CompanyId is Guid companyId
        ? LoadContextAsync(companyId)
        : Task.CompletedTask;

    private string ResolveWorkspace()
    {
        if (!string.IsNullOrWhiteSpace(Workspace) && SupportedWorkspaces.Contains(Workspace)) return Workspace;
        var path = "/" + Navigation.ToBaseRelativePath(Navigation.Uri).Split('?', '#')[0].Trim('/');
        return path switch
        {
            FinanceRoutes.AccountingDimensions => "dimensions",
            FinanceRoutes.AccountingSchedules => "schedules",
            FinanceRoutes.AccountingFixedAssets => "fixed-assets",
            FinanceRoutes.AccountingRevaluation => "revaluation",
            _ => "currency-rates"
        };
    }

    private Task ChangePeriodAsync(ChangeEventArgs args)
    {
        if (Guid.TryParse(args.Value?.ToString(), out var periodId)) SelectedPeriodId = periodId;
        return Task.CompletedTask;
    }

    private string Href(string route) => FinanceRoutes.WithCompanyContext(route, AccessState.CompanyId);
    private string JournalEntryUrl(Guid ledgerEntryId) => FinanceRoutes.WithCompanyContext($"{FinanceRoutes.AccountingJournal}?entryId={ledgerEntryId:D}", AccessState.CompanyId);
}
