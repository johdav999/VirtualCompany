using Microsoft.AspNetCore.Components;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class SettingsPage : FinancePageBase
{
    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;

    private FinanceEmailSettingsResponse? Settings { get; set; }
    private bool IsSettingsLoading { get; set; }
    private bool IsSaving { get; set; }
    private string? SettingsErrorMessage { get; set; }
    private string? SaveErrorMessage { get; set; }
    private string? SuccessMessage { get; set; }
    private string GmailClientId { get; set; } = string.Empty;
    private string GmailClientSecret { get; set; } = string.Empty;
    private string Microsoft365ClientId { get; set; } = string.Empty;
    private string Microsoft365ClientSecret { get; set; } = string.Empty;

    private bool IsBusy => IsSettingsLoading || IsSaving;
    private bool IsFormDisabled => IsBusy || Settings?.IsWritable != true;
    private string EmailSettingsHref => FinanceRoutes.WithCompanyContext(FinanceRoutes.EmailSettings, AccessState.CompanyId);
    private string MailboxHref => FinanceRoutes.WithCompanyContext(FinanceRoutes.Mailbox, AccessState.CompanyId);

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        Settings = null;
        SettingsErrorMessage = null;
        SaveErrorMessage = null;
        SuccessMessage = null;
        GmailClientSecret = string.Empty;
        Microsoft365ClientSecret = string.Empty;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadSettingsAsync(companyId);
    }

    private async Task ReloadAsync()
    {
        SaveErrorMessage = null;
        SuccessMessage = null;

        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadSettingsAsync(companyId);
    }

    private async Task LoadSettingsAsync(Guid companyId)
    {
        IsSettingsLoading = true;
        SettingsErrorMessage = null;

        try
        {
            Settings = await FinanceApiClient.GetEmailSettingsAsync(companyId);
            GmailClientId = Settings.Gmail.ClientId;
            Microsoft365ClientId = Settings.Microsoft365.ClientId;
            GmailClientSecret = string.Empty;
            Microsoft365ClientSecret = string.Empty;
        }
        catch (FinanceApiException ex)
        {
            Settings = null;
            SettingsErrorMessage = ex.Message;
        }
        finally
        {
            IsSettingsLoading = false;
        }
    }

    private async Task SaveAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsSaving = true;
        SaveErrorMessage = null;
        SuccessMessage = null;

        try
        {
            Settings = await FinanceApiClient.UpdateEmailSettingsAsync(
                companyId,
                new UpdateFinanceEmailSettingsRequest
                {
                    Gmail = new UpdateFinanceEmailProviderSettingsRequest
                    {
                        ClientId = GmailClientId,
                        ClientSecret = GmailClientSecret
                    },
                    Microsoft365 = new UpdateFinanceEmailProviderSettingsRequest
                    {
                        ClientId = Microsoft365ClientId,
                        ClientSecret = Microsoft365ClientSecret
                    }
                });

            GmailClientId = Settings.Gmail.ClientId;
            Microsoft365ClientId = Settings.Microsoft365.ClientId;
            GmailClientSecret = string.Empty;
            Microsoft365ClientSecret = string.Empty;
            SuccessMessage = Settings.RequiresRestart
                ? "Email settings saved. Restart the API before connecting a mailbox."
                : "Email settings saved. You can connect a mailbox now.";
        }
        catch (FinanceApiException ex)
        {
            SaveErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static string GetProviderStatus(FinanceEmailProviderSettingsResponse? provider) =>
        provider?.IsClientIdConfigured == true && provider.IsClientSecretConfigured
            ? "Configured"
            : "Missing settings";

    private static string GetProviderBadgeClass(FinanceEmailProviderSettingsResponse? provider) =>
        provider?.IsClientIdConfigured == true && provider.IsClientSecretConfigured
            ? "badge text-bg-success"
            : "badge text-bg-warning";

    private static string GetSecretPlaceholder(FinanceEmailProviderSettingsResponse? provider) =>
        provider?.IsClientSecretConfigured == true
            ? "Already configured. Leave blank to keep existing secret."
            : "Required";
}
