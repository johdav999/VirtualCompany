using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class BasAccountCatalogPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    private const int PageSize = 25;
    private AccountingChartCatalogPageResponse? Catalog { get; set; }
    private IReadOnlyList<AccountingChartCatalogAccountResponse> Accounts => Catalog?.Accounts ?? [];
    private IReadOnlyList<AccountingChartCatalogGroupResponse> Groups => Catalog?.Groups ?? [];
    private AccountingChartCatalogAccountResponse? Selected { get; set; }
    private CatalogCreationModel Creation { get; set; } = CatalogCreationModel.Empty();
    private string Search { get; set; } = string.Empty;
    private string GroupCode { get; set; } = string.Empty;
    private bool K2Only { get; set; }
    private bool ExcludeExisting { get; set; } = true;
    private int Skip { get; set; }
    private bool IsCatalogLoading { get; set; }
    private bool IsSaving { get; set; }
    private string? ListError { get; set; }
    private string? ActionError { get; set; }
    private string? ActionMessage { get; set; }
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private int CurrentPage => Catalog is null ? 1 : (Catalog.Skip / Math.Max(1, Catalog.Take)) + 1;
    private int TotalPages => Catalog is null ? 1 : Math.Max(1, (int)Math.Ceiling(Catalog.MatchedAccountCount / (double)Math.Max(1, Catalog.Take)));
    private bool CanCreateSelected =>
        CanManageAccounting && !IsSaving && Selected is { IsAlreadyAdded: false } &&
        (!Selected.RequiresNameSelection || !string.IsNullOrWhiteSpace(Creation.NameSv)) &&
        !string.IsNullOrWhiteSpace(Creation.AccountClass) &&
        !string.IsNullOrWhiteSpace(Creation.NormalBalance) &&
        Creation.AccountingSemanticsConfirmed && Creation.CompanySuitabilityConfirmed;

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (AccessState.IsAllowed && AccessState.CompanyId is Guid companyId)
        {
            await LoadCatalogAsync(companyId);
        }
    }

    private async Task LoadCatalogAsync(Guid companyId)
    {
        IsCatalogLoading = true;
        ListError = null;
        try
        {
            Catalog = await FinanceApiClient.GetAccountingChartCatalogAsync(
                companyId,
                search: Search,
                groupCode: GroupCode,
                k2Only: K2Only,
                excludeExisting: ExcludeExisting,
                skip: Skip,
                take: PageSize);
            if (Selected is not null)
            {
                var refreshed = Accounts.FirstOrDefault(account => account.Code == Selected.Code);
                if (refreshed is not null) SelectAccount(refreshed);
                else { Selected = null; Creation = CatalogCreationModel.Empty(); }
            }
        }
        catch (FinanceApiException exception)
        {
            Catalog = null;
            Selected = null;
            ListError = exception.Message;
        }
        finally
        {
            IsCatalogLoading = false;
        }
    }

    private void SelectAccount(AccountingChartCatalogAccountResponse account)
    {
        Selected = account;
        ActionError = null;
        ActionMessage = null;
        Creation = new CatalogCreationModel
        {
            NameSv = account.RequiresNameSelection ? string.Empty : account.NameSv,
            AccountClass = account.SuggestedAccountClass ?? string.Empty,
            NormalBalance = account.SuggestedNormalBalance ?? string.Empty,
            EffectiveFrom = DateOnly.FromDateTime(DateTime.Today)
        };
    }

    private void HandleRowKey(KeyboardEventArgs args, AccountingChartCatalogAccountResponse account)
    {
        if (args.Key is "Enter" or " ") SelectAccount(account);
    }

    private async Task ApplyFiltersAsync()
    {
        Skip = 0;
        await ReloadAsync();
    }

    private async Task ClearFiltersAsync()
    {
        Search = string.Empty;
        GroupCode = string.Empty;
        K2Only = false;
        ExcludeExisting = true;
        Skip = 0;
        await ReloadAsync();
    }

    private async Task PreviousPageAsync()
    {
        Skip = Math.Max(0, Skip - PageSize);
        await ReloadAsync();
    }

    private async Task NextPageAsync()
    {
        Skip += PageSize;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is Guid companyId) await LoadCatalogAsync(companyId);
    }

    private async Task CreateAccountAsync()
    {
        if (!CanCreateSelected || Selected is null || AccessState.CompanyId is not Guid companyId) return;

        IsSaving = true;
        ActionError = null;
        ActionMessage = null;
        try
        {
            await FinanceApiClient.CreateAccountingAccountFromChartCatalogAsync(companyId, new CreateAccountingAccountFromChartCatalogApiRequest
            {
                Code = Selected.Code,
                NameSv = Creation.NameSv,
                AccountClass = Creation.AccountClass,
                NormalBalance = Creation.NormalBalance,
                AccountingSemanticsConfirmed = Creation.AccountingSemanticsConfirmed,
                CompanySuitabilityConfirmed = Creation.CompanySuitabilityConfirmed,
                EffectiveFrom = Creation.EffectiveFrom
            });
            ActionMessage = FinanceText["BasAccountAdded", Selected.Code, Creation.NameSv];
            Selected = null;
            Creation = CatalogCreationModel.Empty();
            await ReloadAsync();
        }
        catch (FinanceApiException exception)
        {
            ActionError = exception.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static string ShortHash(string? hash) => string.IsNullOrWhiteSpace(hash) ? "—" : $"{hash[..Math.Min(12, hash.Length)]}…";

    private sealed class CatalogCreationModel
    {
        public string NameSv { get; set; } = string.Empty;
        public string AccountClass { get; set; } = string.Empty;
        public string NormalBalance { get; set; } = string.Empty;
        public bool AccountingSemanticsConfirmed { get; set; }
        public bool CompanySuitabilityConfirmed { get; set; }
        public DateOnly EffectiveFrom { get; set; }

        public static CatalogCreationModel Empty() => new() { EffectiveFrom = DateOnly.FromDateTime(DateTime.Today) };
    }
}
