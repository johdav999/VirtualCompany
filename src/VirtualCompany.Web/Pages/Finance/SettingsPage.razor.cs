using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.WebUtilities;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class SettingsPage : FinancePageBase, IDisposable
{
    private const string DefaultIntegrationProviderKey = "fortnox";

    [Inject] private FinanceApiClient FinanceApiClient { get; set; } = default!;
    [Parameter] public string? ProviderKey { get; set; }

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
    private List<FinanceIntegrationProviderResponse> FinanceIntegrationProviders { get; set; } = [];
    private FinanceIntegrationProviderResponse? SelectedFinanceIntegrationProvider { get; set; }
    private FinanceIntegrationConnectionStatusResponse? FinanceIntegrationStatus { get; set; }
    private FinanceIntegrationSyncHistoryResponse? FinanceIntegrationHistory { get; set; }
    private bool IsFinanceIntegrationLoading { get; set; }
    private bool IsFinanceIntegrationConnecting { get; set; }
    private bool IsFinanceIntegrationSyncing { get; set; }
    private bool IsFinanceIntegrationDisconnecting { get; set; }
    private bool IsFinanceIntegrationHistoryLoading { get; set; }
    private bool ShowFinanceIntegrationHistory { get; set; } = true;
    private string? FinanceIntegrationErrorMessage { get; set; }
    private string? FinanceIntegrationActionErrorMessage { get; set; }
    private string? FinanceIntegrationSuccessMessage { get; set; }
    private string? FinanceIntegrationHistoryErrorMessage { get; set; }
    private string? LoadedSettingsRoute { get; set; }

    private bool IsBusy => IsSettingsLoading || IsSaving;
    private bool IsFinanceIntegrationBusy => IsFinanceIntegrationLoading || IsFinanceIntegrationConnecting || IsFinanceIntegrationSyncing || IsFinanceIntegrationDisconnecting;
    private bool IsFormDisabled => IsBusy || Settings?.IsWritable != true;
    private bool CanManageFinanceIntegrations => FinanceAccess.CanManageFinanceIntegrations(AccessState.MembershipRole);
    private string EmailSettingsHref => FinanceRoutes.WithCompanyContext(FinanceRoutes.EmailProviderSettings, AccessState.CompanyId);
    private string IntegrationSettingsHref => FinanceRoutes.BuildFinanceIntegrationSettingsPath(SelectedProviderKey, AccessState.CompanyId);
    private string MailboxHref => FinanceRoutes.WithCompanyContext(FinanceRoutes.Mailbox, AccessState.CompanyId);
    private bool IsIntegrationSettingsRoute => Navigation.ToBaseRelativePath(Navigation.Uri).StartsWith("finance/settings/integrations/", StringComparison.OrdinalIgnoreCase);
    private bool IsSystemEmailProviderRoute => Navigation.ToBaseRelativePath(Navigation.Uri).StartsWith("system/admin/integrations/email-providers", StringComparison.OrdinalIgnoreCase);
    private string SettingsArea => IsSystemEmailProviderRoute ? "SystemAdmin" : "Finance";
    private string SettingsPageTitle => IsSystemEmailProviderRoute ? FinanceText["EmailIntegrationSettings"] : FinanceText["FinanceSettings"];
    private string SettingsPageDescription => IsSystemEmailProviderRoute
        ? FinanceText["EmailIntegrationSettingsDescription"]
        : FinanceText["FinanceSettingsDescription"];
    private string SelectedProviderKey => string.IsNullOrWhiteSpace(ProviderKey) ? DefaultIntegrationProviderKey : ProviderKey.Trim();
    private string SelectedProviderDisplayName => SelectedFinanceIntegrationProvider?.DisplayName ?? FormatProviderName(SelectedProviderKey);
    private string LastFailureSummary => FortnoxIntegrationDisplayMapper.FormatSafeText(FinanceIntegrationStatus?.LastErrorSummary, "No recent sync issues.");

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
            var route = GetSettingsRoute(args.Location);
            if (!IsOwnedSettingsRoute(route))
            {
                return;
            }

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
        FinanceIntegrationStatus = null;
        FinanceIntegrationHistory = null;
        ShowFinanceIntegrationHistory = true;
        FinanceIntegrationHistoryErrorMessage = null;

        if (!AccessState.IsAllowed || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        if (IsIntegrationSettingsRoute)
        {
            await LoadFinanceIntegrationStatusAsync(companyId);
        }
        else if (IsSystemEmailProviderRoute)
        {
            await LoadSettingsAsync(companyId);
        }
        else
        {
            Navigation.NavigateTo(
                FinanceRoutes.WithCompanyContext(FinanceRoutes.EmailProviderSettings, companyId),
                replace: true);
        }
    }

    private string BuildAgentMailboxSettingsHref() =>
        AccessState.CompanyId is Guid companyId
            ? $"/agents/manage?companyId={companyId}#agent-access-configuration"
            : "/agents/manage#agent-access-configuration";

    private string GetCurrentSettingsRoute()
        => GetSettingsRoute(Navigation.Uri);

    private string GetSettingsRoute(string uri)
    {
        var relativePath = Navigation.ToBaseRelativePath(uri);
        var queryIndex = relativePath.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? relativePath[..queryIndex] : relativePath;
    }

    internal static bool IsOwnedSettingsRoute(string route) =>
        string.Equals(route, "finance/settings", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(route, "finance/settings/email-settings", StringComparison.OrdinalIgnoreCase) ||
        route.StartsWith("finance/settings/integrations/", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(route, "system/admin/integrations/email-providers", StringComparison.OrdinalIgnoreCase);

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

    private async Task ReloadFinanceIntegrationsAsync()
    {
        FinanceIntegrationActionErrorMessage = null;
        FinanceIntegrationSuccessMessage = null;

        if (AccessState.CompanyId is Guid companyId)
        {
            await LoadFinanceIntegrationStatusAsync(companyId);
        }
    }

    private async Task LoadFinanceIntegrationStatusAsync(Guid companyId)
    {
        IsFinanceIntegrationLoading = true;
        FinanceIntegrationErrorMessage = null;

        try
        {
            FinanceIntegrationProviders = await FinanceApiClient.GetFinanceIntegrationProvidersAsync(companyId);
            SelectedFinanceIntegrationProvider = FinanceIntegrationProviders.FirstOrDefault(provider =>
                string.Equals(provider.ProviderKey, SelectedProviderKey, StringComparison.OrdinalIgnoreCase));

            if (SelectedFinanceIntegrationProvider is null)
            {
                FinanceIntegrationStatus = await FinanceApiClient.GetFinanceIntegrationStatusAsync(companyId, SelectedProviderKey);
                SelectedFinanceIntegrationProvider = new FinanceIntegrationProviderResponse
                {
                    ProviderKey = SelectedProviderKey,
                    DisplayName = FormatProviderName(SelectedProviderKey),
                    Status = FinanceIntegrationStatus
                };
            }
            else
            {
                FinanceIntegrationStatus = SelectedFinanceIntegrationProvider.Status;
            }

            ApplyFinanceIntegrationCallbackMessage();

            if (ShowFinanceIntegrationHistory)
            {
                await LoadFinanceIntegrationHistoryAsync(companyId, surfaceErrors: false);
            }
        }
        catch (FinanceApiException ex)
        {
            FinanceIntegrationStatus = null;
            FinanceIntegrationErrorMessage = FortnoxIntegrationDisplayMapper.FormatSafeText(ex.Message, "Financial integrations are unavailable. Please try again.");
        }
        finally
        {
            IsFinanceIntegrationLoading = false;
        }
    }

    private async Task ConnectFinanceIntegrationAsync() => await StartFinanceIntegrationOAuthAsync(reconnect: false);

    private async Task ReconnectFinanceIntegrationAsync() => await StartFinanceIntegrationOAuthAsync(reconnect: true);

    private async Task StartFinanceIntegrationOAuthAsync(bool reconnect)
    {
        if (!CanManageFinanceIntegrations || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsFinanceIntegrationConnecting = true;
        FinanceIntegrationActionErrorMessage = null;
        FinanceIntegrationSuccessMessage = null;
        FinanceIntegrationStatus = CreateOptimisticStatus("pending");

        try
        {
            var returnUri = Navigation.ToAbsoluteUri(IntegrationSettingsHref).ToString();
            var response = await FinanceApiClient.StartFinanceIntegrationConnectionAsync(companyId, SelectedProviderKey, returnUri, reconnect);
            Navigation.NavigateTo(response.AuthorizationUrl, forceLoad: true);
        }
        catch (FinanceApiException ex)
        {
            FinanceIntegrationActionErrorMessage = FortnoxIntegrationDisplayMapper.FormatSafeText(ex.Message, reconnect ? "Couldn't start Fortnox reconnection. Please try again." : "Couldn't start Fortnox connection. Please try again.");
            await RefreshFinanceIntegrationStateAsync(companyId);
        }
        finally
        {
            IsFinanceIntegrationConnecting = false;
        }
    }

    private async Task SyncFinanceIntegrationAsync()
    {
        if (!CanManageFinanceIntegrations || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        if (FinanceIntegrationState is not "connected" and not "syncing" ||
            FinanceIntegrationStatus?.ConnectionId is null)
        {
            FinanceIntegrationActionErrorMessage = "Fortnox must be connected before syncing.";
            await RefreshFinanceIntegrationStateAsync(companyId);
            return;
        }

        IsFinanceIntegrationSyncing = true;
        FinanceIntegrationActionErrorMessage = null;
        FinanceIntegrationSuccessMessage = null;

        try
        {
            var result = await FinanceApiClient.SyncFinanceIntegrationNowAsync(companyId, SelectedProviderKey, FinanceIntegrationStatus?.ConnectionId);
            FinanceIntegrationSuccessMessage = $"Sync finished: {result.Created} created, {result.Updated} updated, {result.Skipped} skipped.";
            await RefreshFinanceIntegrationStateAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            FinanceIntegrationActionErrorMessage = FortnoxIntegrationDisplayMapper.FormatSafeText(ex.Message, "Sync failed. Review the latest sync history entry for a safe summary.");
            await RefreshFinanceIntegrationStateAsync(companyId);
        }
        finally
        {
            IsFinanceIntegrationSyncing = false;
        }
    }

    private async Task DisconnectFinanceIntegrationAsync()
    {
        if (!CanManageFinanceIntegrations || AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        IsFinanceIntegrationDisconnecting = true;
        FinanceIntegrationActionErrorMessage = null;
        FinanceIntegrationSuccessMessage = null;
        FinanceIntegrationStatus = CreateOptimisticStatus("disconnected");

        try
        {
            var result = await FinanceApiClient.DisconnectFinanceIntegrationAsync(companyId, SelectedProviderKey);
            FinanceIntegrationSuccessMessage = FortnoxIntegrationDisplayMapper.FormatSafeText(result.Message, $"{SelectedProviderDisplayName} has been disconnected.");
            await RefreshFinanceIntegrationStateAsync(companyId);
        }
        catch (FinanceApiException ex)
        {
            FinanceIntegrationActionErrorMessage = FortnoxIntegrationDisplayMapper.FormatSafeText(ex.Message, "Couldn't disconnect Fortnox. Please try again.");
            await RefreshFinanceIntegrationStateAsync(companyId);
        }
        finally
        {
            IsFinanceIntegrationDisconnecting = false;
        }
    }

    private async Task ToggleFinanceIntegrationHistoryAsync()
    {
        ShowFinanceIntegrationHistory = !ShowFinanceIntegrationHistory;
        if (ShowFinanceIntegrationHistory && FinanceIntegrationHistory is null)
        {
            await LoadFinanceIntegrationHistoryAsync();
        }
    }

    private async Task LoadFinanceIntegrationHistoryAsync()
    {
        if (AccessState.CompanyId is not Guid companyId)
        {
            return;
        }

        await LoadFinanceIntegrationHistoryAsync(companyId, surfaceErrors: true);
    }

    private Task RefreshFinanceIntegrationStateAsync(Guid companyId) =>
        LoadFinanceIntegrationStatusAsync(companyId);

    private async Task LoadFinanceIntegrationHistoryAsync(Guid companyId, bool surfaceErrors)
    {
        IsFinanceIntegrationHistoryLoading = true;
        FinanceIntegrationHistoryErrorMessage = null;
        try
        {
            FinanceIntegrationHistory = await FinanceApiClient.GetFinanceIntegrationSyncHistoryAsync(companyId, SelectedProviderKey);
        }
        catch (FinanceApiException ex)
        {
            FinanceIntegrationHistory = null;
            FinanceIntegrationHistoryErrorMessage = FortnoxIntegrationDisplayMapper.FormatSafeText(ex.Message, "Sync history is unavailable. Please try again.");
            if (surfaceErrors)
            {
                FinanceIntegrationErrorMessage = FortnoxIntegrationDisplayMapper.FormatSafeText(ex.Message, "Financial integrations are unavailable. Please try again.");
            }
        }
        finally
        {
            IsFinanceIntegrationHistoryLoading = false;
        }
    }

    private FinanceIntegrationConnectionStatusResponse CreateOptimisticStatus(string connectionStatus) =>
        new()
        {
            ProviderKey = SelectedProviderKey,
            IsConnected = string.Equals(connectionStatus, "connected", StringComparison.OrdinalIgnoreCase),
            ConnectionId = FinanceIntegrationStatus?.ConnectionId,
            ConnectionStatus = connectionStatus,
            ConnectedAtUtc = FinanceIntegrationStatus?.ConnectedAtUtc,
            LastSuccessfulSyncUtc = FinanceIntegrationStatus?.LastSuccessfulSyncUtc,
            LastErrorSummary = FinanceIntegrationStatus?.LastErrorSummary
        };

    private void ApplyFinanceIntegrationCallbackMessage()
    {
        var uri = Navigation.ToAbsoluteUri(Navigation.Uri);
        var query = QueryHelpers.ParseQuery(uri.Query);
        if ((query.TryGetValue("integrationConnection", out var state) ||
             query.TryGetValue("fortnoxConnection", out state)) &&
            string.Equals(state, "connected", StringComparison.OrdinalIgnoreCase))
        {
            FinanceIntegrationSuccessMessage = $"{SelectedProviderDisplayName} is connected.";
        }
        else if (query.TryGetValue("integrationMessage", out var message) ||
                 query.TryGetValue("fortnoxMessage", out message))
        {
            FinanceIntegrationActionErrorMessage = FortnoxIntegrationDisplayMapper.FormatSafeText(message.ToString(), "Finance integration authorization could not be completed.");
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

    private string FinanceIntegrationState =>
        FortnoxIntegrationDisplayMapper.NormalizeConnectionState(FinanceIntegrationStatus?.ConnectionStatus, IsFinanceIntegrationSyncing);

    private string FinanceIntegrationStateLabel =>
        FortnoxIntegrationDisplayMapper.FormatConnectionLabel(FinanceIntegrationState);

    private string FinanceIntegrationStateDescription =>
        FortnoxIntegrationDisplayMapper.FormatConnectionDescription(
            SelectedProviderDisplayName,
            FinanceIntegrationState,
            FinanceIntegrationStatus?.LastErrorSummary);

    private string FinanceIntegrationConnectionSummary => FinanceIntegrationStatus?.ConnectionId is not null
        ? FinanceIntegrationStateLabel
        : "No accounting connection is stored.";

    private string FinanceIntegrationBadgeClass =>
        FortnoxIntegrationDisplayMapper.GetConnectionBadgeClass(FinanceIntegrationState);

    private bool ShowConnectAction => FinanceIntegrationState is "not_connected";
    private bool ShowReconnectAction => FinanceIntegrationState is "needs_reconnect" or "error";
    private bool ShowSyncAction => FinanceIntegrationState is "connected" or "syncing";
    private bool ShowDisconnectAction => FinanceIntegrationStatus?.ConnectionId is not null && FinanceIntegrationState is not "not_connected";
    private bool IsFinanceIntegrationSyncDisabled =>
        IsFinanceIntegrationConnecting ||
        IsFinanceIntegrationDisconnecting ||
        IsFinanceIntegrationSyncing ||
        FinanceIntegrationState is not "connected" and not "syncing" ||
        FinanceIntegrationStatus?.ConnectionId is null;

    private string FinanceIntegrationSyncDisabledReason
    {
        get
        {
            if (IsFinanceIntegrationSyncing)
            {
                return "Sync is already running.";
            }

            if (IsFinanceIntegrationConnecting)
            {
                return "Finish connecting Fortnox before syncing.";
            }

            if (IsFinanceIntegrationDisconnecting)
            {
                return "Finish disconnecting Fortnox before syncing.";
            }

            if (FinanceIntegrationState is not "connected" and not "syncing" ||
                FinanceIntegrationStatus?.ConnectionId is null)
            {
                return "Connect Fortnox before syncing.";
            }

            return "Sync data from Fortnox.";
        }
    }

    private static string FormatProviderName(string providerKey) =>
        string.IsNullOrWhiteSpace(providerKey)
            ? "Accounting system"
            : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(providerKey.Replace("-", " ").Replace("_", " "));

    private string FormatDateTime(DateTime? value) =>
        value.HasValue
            ? LocalDateTime.DateTime(value.Value)
            : "Not synced yet";

    private static string FormatDuration(DateTime startedUtc, DateTime? completedUtc)
    {
        if (!completedUtc.HasValue)
        {
            return "Still running";
        }

        var duration = completedUtc.Value - startedUtc;
        if (duration.TotalSeconds < 1)
        {
            return "Under 1 second";
        }

        if (duration.TotalMinutes < 1)
        {
            return $"{Math.Round(duration.TotalSeconds):0} seconds";
        }

        return duration.TotalHours < 1
            ? $"{Math.Round(duration.TotalMinutes):0} minutes"
            : $"{duration.TotalHours:0.#} hours";
    }

    private static string FormatSafeErrorSummary(FinanceIntegrationSyncHistoryItemResponse item)
        => FortnoxIntegrationDisplayMapper.FormatSafeHistoryError(item);

    public void Dispose()
    {
        Navigation.LocationChanged -= OnLocationChanged;
    }
}
