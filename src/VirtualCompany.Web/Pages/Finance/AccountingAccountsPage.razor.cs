using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AccountingAccountsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    private IReadOnlyList<AccountingAccountListItemResponse> Accounts { get; set; } = [];
    private AccountingAccountDetailResponse? Selected { get; set; }
    private string Search { get; set; } = string.Empty;
    private string AccountClassFilter { get; set; } = string.Empty;
    private string StatusFilter { get; set; } = string.Empty;
    private string? ListError { get; set; }
    private string? ActionError { get; set; }
    private string? ActionMessage { get; set; }
    private bool IsAccountsLoading { get; set; }
    private bool IsDetailLoading { get; set; }
    private bool IsSaving { get; set; }
    private bool IsCreating { get; set; }
    private bool ConfirmDeactivate { get; set; }
    private string RenameValue { get; set; } = string.Empty;
    private NewAccountModel NewAccount { get; set; } = NewAccountModel.CreateDefault();
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (AccessState.IsAllowed && AccessState.CompanyId is Guid companyId)
        {
            await LoadAccountsAsync(companyId, selectFirst: true);
        }
    }

    private async Task LoadAccountsAsync(Guid companyId, bool selectFirst)
    {
        IsAccountsLoading = true;
        ListError = null;
        try
        {
            Accounts = await FinanceApiClient.GetAccountingAccountsAsync(companyId, Search, AccountClassFilter, StatusFilter);
            if (selectFirst && Selected is null && Accounts.FirstOrDefault() is { } first)
            {
                await SelectAccountAsync(first.Id);
            }
            else if (Selected is not null && Accounts.All(account => account.Id != Selected.Id))
            {
                Selected = null;
            }
        }
        catch (FinanceApiException exception)
        {
            Accounts = [];
            ListError = exception.Message;
        }
        finally
        {
            IsAccountsLoading = false;
        }
    }

    private async Task SelectAccountAsync(Guid accountId)
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsCreating = false;
        ConfirmDeactivate = false;
        IsDetailLoading = true;
        ActionError = null;
        try
        {
            Selected = await FinanceApiClient.GetAccountingAccountAsync(companyId, accountId);
            RenameValue = Selected?.Name ?? string.Empty;
        }
        catch (FinanceApiException exception)
        {
            ActionError = exception.Message;
        }
        finally
        {
            IsDetailLoading = false;
        }
    }

    private Task HandleRowKeyAsync(KeyboardEventArgs args, Guid accountId) =>
        args.Key is "Enter" or " " ? SelectAccountAsync(accountId) : Task.CompletedTask;

    private Task ApplyFiltersAsync() => ReloadAsync();

    private async Task ClearFiltersAsync()
    {
        Search = string.Empty;
        AccountClassFilter = string.Empty;
        StatusFilter = string.Empty;
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is Guid companyId)
        {
            ActionError = null;
            await LoadAccountsAsync(companyId, selectFirst: Selected is null);
        }
    }

    private void BeginCreate()
    {
        IsCreating = true;
        ActionError = null;
        ActionMessage = null;
        NewAccount = NewAccountModel.CreateDefault();
    }

    private void CancelCreate() => IsCreating = false;

    private void UpdateDefaultBalance() => NewAccount.NormalBalance = AccountingPresentation.DefaultNormalBalance(NewAccount.AccountClass);

    private async Task CreateAccountAsync()
    {
        if (!CanManageAccounting || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await RunMutationAsync(async () =>
        {
            Selected = await FinanceApiClient.CreateAccountingAccountAsync(companyId, new CreateAccountingAccountApiRequest
            {
                Code = NewAccount.Code,
                Name = NewAccount.Name,
                AccountClass = NewAccount.AccountClass,
                NormalBalance = NewAccount.NormalBalance,
                EffectiveFrom = NewAccount.EffectiveFrom
            });
            RenameValue = Selected.Name;
            IsCreating = false;
            ActionMessage = FinanceText["AccountCreated"];
        });
    }

    private async Task RenameAccountAsync()
    {
        if (!CanManageAccounting || AccessState.CompanyId is not Guid companyId || Selected is null)
        {
            return;
        }

        await RunMutationAsync(async () =>
        {
            Selected = await FinanceApiClient.RenameAccountingAccountAsync(companyId, Selected.Id, new RenameAccountingAccountApiRequest { Name = RenameValue, ExpectedUpdatedUtc = Selected.UpdatedUtc });
            RenameValue = Selected.Name;
            ActionMessage = FinanceText["AccountRenamed"];
        });
    }

    private void RequestDeactivate() => ConfirmDeactivate = true;
    private void CancelDeactivate() => ConfirmDeactivate = false;

    private async Task DeactivateAccountAsync()
    {
        if (!CanManageAccounting || AccessState.CompanyId is not Guid companyId || Selected is null)
        {
            return;
        }

        await RunMutationAsync(async () =>
        {
            Selected = await FinanceApiClient.DeactivateAccountingAccountAsync(companyId, Selected.Id, new DeactivateAccountingAccountApiRequest { EffectiveTo = DateOnly.FromDateTime(DateTime.Today), ExpectedUpdatedUtc = Selected.UpdatedUtc });
            RenameValue = Selected.Name;
            ConfirmDeactivate = false;
            ActionMessage = FinanceText["AccountDeactivated"];
        });
    }

    private async Task RunMutationAsync(Func<Task> mutation)
    {
        IsSaving = true;
        ActionError = null;
        ActionMessage = null;
        try
        {
            await mutation();
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

    private string FormatActiveDates(AccountingAccountDetailResponse account) =>
        account.EffectiveFrom.HasValue
            ? account.EffectiveTo.HasValue
                ? $"{account.EffectiveFrom.Value:d} – {account.EffectiveTo.Value:d}"
                : $"{account.EffectiveFrom.Value:d} – {FinanceText["NoEndDate"]}"
            : FinanceText["NoDateRestriction"];

    private sealed class NewAccountModel
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AccountClass { get; set; } = "asset";
        public string NormalBalance { get; set; } = "debit";
        public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public static NewAccountModel CreateDefault() => new();
    }
}
