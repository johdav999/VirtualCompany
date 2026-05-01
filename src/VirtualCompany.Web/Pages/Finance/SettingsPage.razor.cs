using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.WebUtilities;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class SettingsPage : FinancePageBase, IDisposable
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
    private FortnoxConnectionStatusResponse? FortnoxStatus { get; set; }
    private FortnoxSyncHistoryResponse? FortnoxHistory { get; set; }
    private bool IsFortnoxLoading { get; set; }
    private bool IsFortnoxConnecting { get; set; }
    private bool IsFortnoxSyncing { get; set; }
    private bool IsFortnoxDisconnecting { get; set; }
    private bool IsFortnoxHistoryLoading { get; set; }
    private bool ShowFortnoxHistory { get; set; }
    private string? FortnoxErrorMessage { get; set; }
    private string? FortnoxActionErrorMessage { get; set; }
    private string? FortnoxSuccessMessage { get; set; }
    private string? LoadedSettingsRoute { get; set; }

    private bool IsBusy => IsSettingsLoading || IsSaving;
    private bool IsFortnoxBusy => IsFortnoxLoading || IsFortnoxConnecting || IsFortnoxSyncing || IsFortnoxDisconnecting;
    private bool IsFormDisabled => IsBusy || Settings?.IsWritable != true;
    private string EmailSettingsHref => FinanceRoutes.WithCompanyContext(FinanceRoutes.EmailSettings, AccessState.CompanyId);
    private string FortnoxSettingsHref => FinanceRoutes.WithCompanyContext(FinanceRoutes.FortnoxIntegrationSettings, AccessState.CompanyId);
    private string MailboxHref => FinanceRoutes.WithCompanyContext(FinanceRoutes.Mailbox, AccessState.CompanyId);
    private bool IsFortnoxSettingsRoute => Navigation.Uri.Contains("/finance/settings/integrations/fortnox", StringComparison.OrdinalIgnoreCase);

    protected override void OnInitialized()
    {
        Navigation.LocationChanged += OnLocationChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        await LoadCurrentSettingsSectionAsync(forceReload: true);
    }

    private async void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        await InvokeAsync(async () =>
        {
            var route = GetCurrentSettingsRoute();
            if (string.Equals(route, LoadedSettingsRoute, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await LoadCurrentSettingsSectionAsync(forceReload: true);
            StateHasChanged();
        });
    }

    private async Task LoadCurrentSettingsSectionAsync(bool forceReload)
    {
        var route = GetCurrentSettingsRoute();
        if (!forceReload && string.Equals(route, LoadedSettingsRoute, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        LoadedSettingsRoute = route;
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

        if (IsFortnoxSettingsRoute)
        {
            await LoadFortnoxStatusAsync(companyId);
        }
        else
        {
            await LoadSettingsAsync(companyId);
        }
    }

    private string GetCurrentSettingsRoute()
    {
        var relativePath = Navigation.ToBaseRelativePath(Navigation.Uri);
        var queryIndex = relativePath.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? relativePath[..queryIndex] : relativePath;
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

    private async Task ReloadFortnoxAsync()
    {
        FortnoxActionErrorMessage = null;
        FortnoxSuccessMessage = null;

        if (AccessState.CompanyId is Guid companyId)
        {
            await LoadFortnoxStatusAsync(companyId);
        }
    }

    private async Task LoadFortnoxStatusAsync(Guid companyId)
    {
        IsFortnoxLoading = true;
        FortnoxErrorMessage = null;

        try
        {
            FortnoxStatus = await FinanceApiClient.GetFortnoxConnectionStatusAsync(companyId);
            ApplyFortnoxCallbackMessage();
        }
        catch (FinanceApiException ex)
        {
            FortnoxStatus = null;
            FortnoxErrorMessage = ex.Message;
        }
        finally
        {
            IsFortnoxLoading = false;
        }
    }

    private async Task ConnectFortnoxAsync() => await StartFortnoxOAuthAsync(reconnect: false);

    private async Task ReconnectFortnoxAsync() => await StartFortnoxOAuthAsync(reconnect: true);

    private async Task StartFortnoxOAuthAsync(bool reconnect)
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsFortnoxConnecting = true;
        FortnoxActionErrorMessage = null;
        FortnoxSuccessMessage = null;

        try
        {
            var returnUri = Navigation.ToAbsoluteUri(FortnoxSettingsHref).ToString();
            var response = await FinanceApiClient.StartFortnoxConnectionAsync(companyId, returnUri, reconnect);
            Navigation.NavigateTo(response.AuthorizationUrl, forceLoad: true);
        }
        catch (FinanceApiException ex)
        {
            FortnoxActionErrorMessage = ex.Message;
        }
        finally
        {
            IsFortnoxConnecting = false;
        }
    }

    private async Task SyncFortnoxAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsFortnoxSyncing = true;
        FortnoxActionErrorMessage = null;
        FortnoxSuccessMessage = null;

        try
        {
            var result = await FinanceApiClient.SyncFortnoxNowAsync(companyId, FortnoxStatus?.ConnectionId);
            FortnoxSuccessMessage = $"Fortnox sync finished: {result.Created} created, {result.Updated} updated, {result.Skipped} skipped.";
            await LoadFortnoxStatusAsync(companyId);
            if (ShowFortnoxHistory)
            {
                await LoadFortnoxHistoryAsync();
            }
        }
        catch (FinanceApiException ex)
        {
            FortnoxActionErrorMessage = ex.Message;
        }
        finally
        {
            IsFortnoxSyncing = false;
        }
    }

    private async Task DisconnectFortnoxAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsFortnoxDisconnecting = true;
        FortnoxActionErrorMessage = null;
        FortnoxSuccessMessage = null;

        try
        {
            var result = await FinanceApiClient.DisconnectFortnoxAsync(companyId);
            FortnoxSuccessMessage = result.Message;
            await LoadFortnoxStatusAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            FortnoxActionErrorMessage = ex.Message;
        }
        finally
        {
            IsFortnoxDisconnecting = false;
        }
    }

    private async Task ToggleFortnoxHistoryAsync()
    {
        ShowFortnoxHistory = !ShowFortnoxHistory;
        if (ShowFortnoxHistory && FortnoxHistory is null)
        {
            await LoadFortnoxHistoryAsync();
        }
    }

    private async Task LoadFortnoxHistoryAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsFortnoxHistoryLoading = true;
        try
        {
            FortnoxHistory = await FinanceApiClient.GetFortnoxSyncHistoryAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            FortnoxActionErrorMessage = ex.Message;
        }
        finally
        {
            IsFortnoxHistoryLoading = false;
        }
    }

    private void ApplyFortnoxCallbackMessage()
    {
        var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
        var query = QueryHelpers.ParseQuery(uri.Query);
        if (query.TryGetValue("fortnoxConnection", out var state) && string.Equals(state, "connected", StringComparison.OrdinalIgnoreCase))
        {
            FortnoxSuccessMessage = "Fortnox is connected.";
        }
        else if (query.TryGetValue("fortnoxMessage", out var message))
        {
            FortnoxActionErrorMessage = message.ToString();
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

    private string FortnoxState => FortnoxStatus?.ConnectionStatus switch
    {
        null or "" => "not_connected",
        "pending" => "connecting",
        "connected" when IsFortnoxSyncing => "syncing",
        "connected" => "connected",
        "needs_reconnect" => "needs_reconnect",
        "error" => "error",
        "revoked" or "disconnected" => "not_connected",
        _ => "error"
    };

    private string FortnoxStateLabel => FortnoxState switch
    {
        "not_connected" => "Not connected",
        "connecting" => "Connecting",
        "connected" => "Connected",
        "syncing" => "Syncing",
        "needs_reconnect" => "Needs reconnect",
        "error" => "Error",
        _ => "Error"
    };

    private string FortnoxStateDescription => FortnoxState switch
    {
        "not_connected" => "Connect Fortnox to sync customers, suppliers, invoices, bills, accounts, and payments.",
        "connecting" => "Fortnox authorization has started and is waiting for completion.",
        "connected" => "Fortnox is connected and ready to sync finance records.",
        "syncing" => "Fortnox sync is running. You can review history when it completes.",
        "needs_reconnect" => "Fortnox needs a fresh authorization before syncing can continue.",
        "error" => FortnoxStatus?.LastErrorSummary ?? "Fortnox reported an error. Reconnect to restore the integration.",
        _ => "Fortnox state is unavailable."
    };

    private string FortnoxConnectionSummary => FortnoxStatus?.ConnectionId is Guid connectionId
        ? $"{FortnoxStateLabel} ({connectionId:N})"
        : "No Fortnox connection is stored.";

    private string FortnoxBadgeClass => FortnoxState switch
    {
        "connected" => "badge text-bg-success",
        "syncing" => "badge text-bg-primary",
        "needs_reconnect" => "badge text-bg-warning",
        "error" => "badge text-bg-danger",
        "connecting" => "badge text-bg-info",
        _ => "badge text-bg-secondary"
    };

    private bool ShowConnectAction => FortnoxState is "not_connected";
    private bool ShowReconnectAction => FortnoxState is "needs_reconnect" or "error";
    private bool ShowSyncAction => FortnoxState is "connected" or "syncing";
    private bool ShowDisconnectAction => FortnoxStatus?.ConnectionId is not null && FortnoxState is not "not_connected";

    private static string FormatFortnoxStatus(string status) =>
        string.IsNullOrWhiteSpace(status)
            ? "Unknown"
            : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(status.Replace("_", " "));

    private static string GetHistoryBadgeClass(string status) => status switch
    {
        "succeeded" => "badge text-bg-success",
        "failed" => "badge text-bg-danger",
        _ => "badge text-bg-secondary"
    };

    public void Dispose()
    {
        Navigation.LocationChanged -= OnLocationChanged;
    }
}
