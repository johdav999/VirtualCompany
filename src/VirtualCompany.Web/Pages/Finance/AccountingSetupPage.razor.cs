using System.Globalization;
using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class AccountingSetupPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    private static readonly IReadOnlyList<SetupStep> Steps =
    [
        new(1, "AccountingBasicsStep"),
        new(2, "AccountingAccountsStep"),
        new(3, "AccountingPeriodsStep"),
        new(4, "AccountingReviewStep")
    ];

    private IReadOnlyList<AccountingPolicyPackOptionResponse> PolicyPacks { get; set; } = [];
    private AccountingSetupStatusResponse? SetupStatus { get; set; }
    private AccountingSetupPreviewResponse? Preview { get; set; }
    private SetupDraft Draft { get; set; } = SetupDraft.CreateDefault();
    private int CurrentStep { get; set; } = 1;
    private int HighestAvailableStep { get; set; } = 1;
    private bool IsPreviewLoading { get; set; }
    private bool IsSaving { get; set; }
    private string? PreviewError { get; set; }
    private string? ActionError { get; set; }
    private string? SuccessMessage { get; set; }
    private bool CanManageAccounting => FinanceAccess.CanManageAccounting(AccessState.MembershipRole);
    private string DisplayPolicyName => PolicyPacks.FirstOrDefault(pack =>
        string.Equals(pack.PackKey, SetupStatus?.Configuration?.PolicyPackKey, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(pack.PackVersion, SetupStatus?.Configuration?.PolicyPackVersion, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? FinanceText["AccountingPolicyUnavailable"];

    private IReadOnlyList<AccountingChartTemplateOptionResponse> SelectedPackTemplates =>
        SelectedPack?.ChartTemplates ?? [];

    private AccountingPolicyPackOptionResponse? SelectedPack
    {
        get
        {
            ParsePackIdentity(Draft.PolicyPackIdentity, out var key, out var version);
            return PolicyPacks.FirstOrDefault(pack =>
                string.Equals(pack.PackKey, key, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pack.PackVersion, version, StringComparison.OrdinalIgnoreCase));
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadAsync(companyId);
    }

    private async Task LoadAsync(Guid companyId)
    {
        ActionError = null;
        try
        {
            var packsTask = FinanceApiClient.GetAccountingPolicyPacksAsync(companyId);
            var statusTask = FinanceApiClient.GetAccountingSetupStatusAsync(companyId);
            await Task.WhenAll(packsTask, statusTask);
            PolicyPacks = packsTask.Result;
            SetupStatus = statusTask.Result;
            if (SetupStatus?.IsConfigured == true || PolicyPacks.Count == 0)
            {
                return;
            }

            var selected = PolicyPacks.FirstOrDefault(pack => pack.IsCountryNeutral) ?? PolicyPacks[0];
            Draft.PolicyPackIdentity = PackIdentity(selected);
            Draft.ChartTemplateKey = selected.ChartTemplates.FirstOrDefault()?.TemplateKey ?? string.Empty;
            await ReloadPreviewAsync();
        }
        catch (FinanceApiException exception)
        {
            ActionError = exception.Message;
        }
    }

    private async Task ReloadAsync()
    {
        if (AccessState.CompanyId is Guid companyId)
        {
            await LoadAsync(companyId);
        }
    }

    private async Task ReloadPreviewAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsPreviewLoading = true;
        PreviewError = null;
        ActionError = null;
        try
        {
            var selectedPack = SelectedPack ?? PolicyPacks.FirstOrDefault();
            if (selectedPack is null)
            {
                throw new InvalidOperationException(FinanceText["NoAccountingPolicyAvailable"]);
            }

            if (selectedPack.ChartTemplates.All(template => !string.Equals(template.TemplateKey, Draft.ChartTemplateKey, StringComparison.OrdinalIgnoreCase)))
            {
                Draft.ChartTemplateKey = selectedPack.ChartTemplates.FirstOrDefault()?.TemplateKey ?? string.Empty;
            }

            Preview = await FinanceApiClient.PreviewAccountingSetupAsync(companyId, BuildPreviewRequest(selectedPack));
        }
        catch (Exception exception) when (exception is FinanceApiException or InvalidOperationException)
        {
            Preview = null;
            PreviewError = exception.Message;
            ActionError = exception.Message;
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    private async Task NextStepAsync()
    {
        await ReloadPreviewAsync();
        if (Preview is null)
        {
            return;
        }

        CurrentStep = Math.Min(Steps.Count, CurrentStep + 1);
        HighestAvailableStep = Math.Max(HighestAvailableStep, CurrentStep);
    }

    private void PreviousStep() => CurrentStep = Math.Max(1, CurrentStep - 1);

    private void GoToStep(int step)
    {
        if (step <= HighestAvailableStep)
        {
            CurrentStep = step;
        }
    }

    private async Task CompleteSetupAsync()
    {
        if (!CanManageAccounting || AccessState.CompanyId is not Guid companyId || SelectedPack is not { } pack)
        {
            return;
        }

        IsSaving = true;
        ActionError = null;
        SuccessMessage = null;
        try
        {
            var completion = await FinanceApiClient.CompleteAccountingSetupAsync(
                companyId,
                new CompleteAccountingSetupApiRequest
                {
                    BaseCurrency = Draft.BaseCurrency,
                    FiscalYearStart = Draft.FiscalYearStart,
                    PolicyPackKey = pack.PackKey,
                    PolicyPackVersion = pack.PackVersion,
                    ChartTemplateKey = Draft.ChartTemplateKey,
                    IdempotencyKey = Draft.IdempotencyKey
                });
            SetupStatus = completion.SetupStatus;
            SuccessMessage = completion.WasAlreadyApplied
                ? FinanceText["AccountingSetupAlreadyCompleted"]
                : FinanceText["AccountingSetupCompleted"];
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

    private PreviewAccountingSetupApiRequest BuildPreviewRequest(AccountingPolicyPackOptionResponse pack) =>
        new()
        {
            BaseCurrency = Draft.BaseCurrency,
            FiscalYearStart = Draft.FiscalYearStart,
            PolicyPackKey = pack.PackKey,
            PolicyPackVersion = pack.PackVersion,
            ChartTemplateKey = Draft.ChartTemplateKey
        };

    private string GetStepClass(int step) =>
        step == CurrentStep ? "accounting-stepper__step active" : step < CurrentStep ? "accounting-stepper__step complete" : "accounting-stepper__step";

    private static string PackIdentity(AccountingPolicyPackOptionResponse pack) => $"{pack.PackKey}|{pack.PackVersion}";

    private static void ParsePackIdentity(string value, out string key, out string version)
    {
        var parts = value.Split('|', 2, StringSplitOptions.TrimEntries);
        key = parts.ElementAtOrDefault(0) ?? string.Empty;
        version = parts.ElementAtOrDefault(1) ?? string.Empty;
    }

    private static string FormatFiscalStart(AccountingConfigurationResponse configuration) =>
        new DateOnly(2000, configuration.FiscalYearStartMonth, Math.Min(configuration.FiscalYearStartDay, DateTime.DaysInMonth(2000, configuration.FiscalYearStartMonth))).ToString("MMMM d", CultureInfo.CurrentCulture);

    private sealed record SetupStep(int Number, string ResourceKey);

    private sealed class SetupDraft
    {
        public string BaseCurrency { get; set; } = string.Empty;
        public DateOnly FiscalYearStart { get; set; }
        public string PolicyPackIdentity { get; set; } = string.Empty;
        public string ChartTemplateKey { get; set; } = string.Empty;
        public string IdempotencyKey { get; } = $"accounting-setup:{Guid.NewGuid():N}";

        public static SetupDraft CreateDefault()
        {
            var currency = "USD";
            try
            {
                currency = new RegionInfo(CultureInfo.CurrentCulture.Name).ISOCurrencySymbol;
            }
            catch (ArgumentException)
            {
            }

            return new SetupDraft
            {
                BaseCurrency = currency,
                FiscalYearStart = new DateOnly(DateTime.Today.Year, 1, 1)
            };
        }
    }
}
