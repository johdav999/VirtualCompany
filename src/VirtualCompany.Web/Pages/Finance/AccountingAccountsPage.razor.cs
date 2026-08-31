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
    private string ActiveTab { get; set; } = "accounts";
    private IReadOnlyList<AccountingSeriesPolicyResponse> SeriesPolicies { get; set; } = [];
    private AccountingSeriesPolicyResponse? SelectedSeriesPolicy { get; set; }
    private IReadOnlyList<SeriesLocationOption> SeriesLocationOptions { get; set; } = [];
    private int GapFiscalYear { get; set; } = DateTime.Today.Year;
    private long? GapMissingNumber { get; set; }
    private string GapReason { get; set; } = string.Empty;
    private CommerceAccountingCapabilityResponse? CommerceCapability { get; set; }
    private AccountingAccountLifecyclePreviewResponse? LifecyclePreview { get; set; }
    private AccountLifecycleModel LifecycleDraft { get; set; } = new();
    private string RenameValue { get; set; } = string.Empty;
    private NewAccountModel NewAccount { get; set; } = NewAccountModel.CreateDefault();
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (AccessState.IsAllowed && AccessState.CompanyId is Guid companyId)
        {
            await LoadAccountsAsync(companyId, selectFirst: true);
            await LoadGovernanceAsync(companyId);
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
            if (Selected is not null) LifecycleDraft = AccountLifecycleModel.From(Selected);
            LifecyclePreview = null;
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

    private async Task LoadGovernanceAsync(Guid companyId)
    {
        try
        {
            SeriesPolicies = await FinanceApiClient.GetAccountingSeriesPoliciesAsync(companyId);
            CommerceCapability = await FinanceApiClient.GetCommerceAccountingCapabilityAsync(companyId);
            var dimensions = await FinanceApiClient.GetAccountingDimensionWorkspaceAsync(companyId);
            SeriesLocationOptions = dimensions?.DimensionTypes
                .SelectMany(type => type.Members
                    .Where(member => string.Equals(member.Status, "active", StringComparison.OrdinalIgnoreCase))
                    .Select(member => new SeriesLocationOption(
                        member.Id,
                        $"{type.Name} — {member.HierarchyPath}")))
                .OrderBy(option => option.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray() ?? [];
        }
        catch (FinanceApiException exception) { ActionError = exception.Message; }
    }

    private void SetTab(string tab)
    {
        ActiveTab = tab;
        ActionError = null;
        ActionMessage = null;
    }

    private async Task PreviewLifecycleAsync()
    {
        if (Selected is null || AccessState.CompanyId is not Guid companyId) return;
        IsSaving = true; ActionError = null;
        try
        {
            LifecyclePreview = await FinanceApiClient.PreviewAccountingAccountLifecycleAsync(companyId, Selected.Id,
                LifecycleDraft.ToPreviewRequest());
        }
        catch (FinanceApiException exception) { ActionError = exception.Message; }
        finally { IsSaving = false; }
    }

    private async Task ApplyLifecycleAsync()
    {
        if (!CanManageAccounting || Selected is null || AccessState.CompanyId is not Guid companyId) return;
        await RunMutationAsync(async () =>
        {
            Selected = await FinanceApiClient.ApplyAccountingAccountLifecycleAsync(companyId, Selected.Id,
                LifecycleDraft.ToApplyRequest(Selected));
            LifecycleDraft = AccountLifecycleModel.From(Selected);
            LifecyclePreview = null;
            ActionMessage = FinanceText["AccountLifecycleUpdated"];
        });
    }

    private void SelectSeriesPolicy(AccountingSeriesPolicyResponse policy)
    {
        SelectedSeriesPolicy = new AccountingSeriesPolicyResponse
        {
            Id = policy.Id, SeriesKind = policy.SeriesKind, SeriesId = policy.SeriesId, SeriesCode = policy.SeriesCode,
            SeriesName = policy.SeriesName, SourceType = policy.SourceType, TransactionType = policy.TransactionType,
            FiscalYear = policy.FiscalYear, LocationDimensionMemberId = policy.LocationDimensionMemberId,
            Jurisdiction = policy.Jurisdiction, PolicyPackKey = policy.PolicyPackKey, PolicyPackVersion = policy.PolicyPackVersion,
            ProviderKey = policy.ProviderKey, ProviderSeriesCode = policy.ProviderSeriesCode, IsActive = policy.IsActive,
            Version = policy.Version, UnexplainedGapCount = policy.UnexplainedGapCount
        };
        GapFiscalYear = policy.FiscalYear ?? DateTime.Today.Year;
        GapMissingNumber = null;
        GapReason = string.Empty;
    }

    private async Task SaveSeriesPolicyAsync()
    {
        if (!CanManageAccounting || SelectedSeriesPolicy is null || AccessState.CompanyId is not Guid companyId) return;
        await RunMutationAsync(async () =>
        {
            await FinanceApiClient.SaveAccountingSeriesPolicyAsync(companyId, new SaveAccountingSeriesPolicyApiRequest
            {
                PolicyId = SelectedSeriesPolicy.Id == Guid.Empty ? null : SelectedSeriesPolicy.Id,
                SeriesKind = SelectedSeriesPolicy.SeriesKind, SeriesId = SelectedSeriesPolicy.SeriesId,
                SourceType = SelectedSeriesPolicy.SourceType, TransactionType = SelectedSeriesPolicy.TransactionType,
                FiscalYear = SelectedSeriesPolicy.FiscalYear, LocationDimensionMemberId = SelectedSeriesPolicy.LocationDimensionMemberId,
                Jurisdiction = SelectedSeriesPolicy.Jurisdiction, ProviderKey = SelectedSeriesPolicy.ProviderKey,
                ProviderSeriesCode = SelectedSeriesPolicy.ProviderSeriesCode, IsActive = SelectedSeriesPolicy.IsActive,
                ExpectedVersion = SelectedSeriesPolicy.Id == Guid.Empty ? null : SelectedSeriesPolicy.Version
            });
            await LoadGovernanceAsync(companyId);
            SelectedSeriesPolicy = null;
            ActionMessage = FinanceText["SeriesPolicySaved"];
        });
    }

    private async Task RecordVoucherGapAsync()
    {
        if (!CanManageAccounting || SelectedSeriesPolicy is null ||
            SelectedSeriesPolicy.SeriesKind != "voucher" || GapMissingNumber is not > 0 ||
            string.IsNullOrWhiteSpace(GapReason) || AccessState.CompanyId is not Guid companyId) return;
        await RunMutationAsync(async () =>
        {
            SelectedSeriesPolicy = await FinanceApiClient.RecordAccountingVoucherGapAsync(companyId,
                SelectedSeriesPolicy.SeriesId, new RecordAccountingVoucherGapApiRequest
                {
                    FiscalYear = GapFiscalYear,
                    MissingNumber = GapMissingNumber.Value,
                    Reason = GapReason
                });
            GapMissingNumber = null;
            GapReason = string.Empty;
            await LoadGovernanceAsync(companyId);
            ActionMessage = FinanceText["VoucherGapRecorded"];
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

    private string FormatSeriesLocation(Guid? memberId) => memberId.HasValue
        ? SeriesLocationOptions.FirstOrDefault(option => option.Id == memberId.Value)?.DisplayName
            ?? FinanceText["UnavailableLocation"]
        : FinanceText["AllLocations"];

    private sealed record SeriesLocationOption(Guid Id, string DisplayName);

    private sealed class NewAccountModel
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AccountClass { get; set; } = "asset";
        public string NormalBalance { get; set; } = "debit";
        public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public static NewAccountModel CreateDefault() => new();
    }

    private sealed class AccountLifecycleModel
    {
        public string Name { get; set; } = string.Empty; public string AccountClass { get; set; } = "asset";
        public string NormalBalance { get; set; } = "debit"; public bool IsReportable { get; set; } = true;
        public string PostingRestriction { get; set; } = "none"; public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.Today);
        public DateOnly? EffectiveTo { get; set; } public Guid? ReplacementAccountId { get; set; } public string Reason { get; set; } = string.Empty;
        public static AccountLifecycleModel From(AccountingAccountDetailResponse account) => new()
        {
            Name = account.Name, AccountClass = account.AccountClass.ToLowerInvariant(), NormalBalance = account.NormalBalance.ToLowerInvariant(),
            IsReportable = account.IsReportable, PostingRestriction = account.PostingRestriction,
            EffectiveFrom = account.EffectiveFrom ?? DateOnly.FromDateTime(DateTime.Today), EffectiveTo = account.EffectiveTo,
            ReplacementAccountId = account.ReplacementAccountId
        };
        public PreviewAccountingAccountLifecycleApiRequest ToPreviewRequest() => new()
        {
            AccountClass = AccountClass, NormalBalance = NormalBalance, IsReportable = IsReportable,
            PostingRestriction = PostingRestriction, EffectiveFrom = EffectiveFrom, EffectiveTo = EffectiveTo,
            ReplacementAccountId = ReplacementAccountId
        };
        public ApplyAccountingAccountLifecycleApiRequest ToApplyRequest(AccountingAccountDetailResponse account) => new()
        {
            Name = Name, AccountClass = AccountClass, NormalBalance = NormalBalance, IsReportable = IsReportable,
            PostingRestriction = PostingRestriction, EffectiveFrom = EffectiveFrom, EffectiveTo = EffectiveTo,
            ReplacementAccountId = ReplacementAccountId, Reason = Reason, ExpectedLifecycleVersion = account.LifecycleVersion
        };
    }
}
