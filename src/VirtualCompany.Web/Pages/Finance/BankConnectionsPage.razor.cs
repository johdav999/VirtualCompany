using Microsoft.AspNetCore.Components;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Pages.Finance;

public partial class BankConnectionsPage
{
    [Inject] private FinanceApiClient FinanceClient { get; set; } = default!;
    private BankConnectionStatusResponse? Status { get; set; }
    private BankFeedHealthResponse? FeedHealth { get; set; }
    private IReadOnlyList<BankInstitutionResponse> Institutions { get; set; } = [];
    private Dictionary<Guid, Guid> MappingTargets { get; } = [];
    private string? SelectedProviderKey { get; set; }
    private string? SelectedInstitutionId { get; set; }
    private string? ActionMessage { get; set; }
    private string? ActionError { get; set; }
    private bool IsBusy { get; set; }
    private Guid? _disconnectConfirmationId;
    private bool CanManage => FinanceAccess.CanManageFinanceIntegrations(AccessState.MembershipRole);
    private int ActiveCount => Status?.Connections.Count(x => x.Status == "active") ?? 0;
    private int RenewalCount => Status?.Connections.Count(x => x.ReasonCode is "expired_consent" or "scope_loss" or "missing_consent") ?? 0;
    private int SuspendedCount => Status?.Connections.Count(x => x.Status == "suspended") ?? 0;
    private int OutageCount => Status?.Connections.Count(x => x.HealthStatus == "outage") ?? 0;
    private int OwnershipMismatchCount => Status?.Connections.Count(x => x.ReasonCode == "account_ownership_mismatch") ?? 0;
    private IEnumerable<(BankConnectionResponse Connection, BankDiscoveredAccountResponse Account)> DiscoveredAccounts =>
        Status?.Connections.SelectMany(connection => connection.Accounts.Select(account => (connection, account))) ?? [];

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        if (AccessState.IsAllowed) await LoadAsync();
    }
    private async Task LoadAsync()
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        IsBusy = true; ActionError = null;
        try
        {
            var statusTask = FinanceClient.GetBankConnectionsAsync(companyId);
            var feedTask = FinanceClient.GetBankFeedHealthAsync(companyId);
            await Task.WhenAll(statusTask, feedTask);
            Status = await statusTask;
            FeedHealth = await feedTask;
            foreach (var account in Status.Connections.SelectMany(x => x.Accounts)) MappingTargets.TryAdd(account.Id, Guid.Empty);
            var configured = Status.Providers.FirstOrDefault(x => x.IsConfigured);
            if (string.IsNullOrWhiteSpace(SelectedProviderKey) && configured is not null) SelectedProviderKey = configured.ProviderKey;
            await LoadInstitutionsAsync();
            ApplyCallbackMessage();
        }
        catch (Exception exception) { ActionError = exception.Message; }
        finally { IsBusy = false; }
    }
    private async Task LoadInstitutionsAsync()
    {
        if (AccessState.CompanyId is not Guid companyId || string.IsNullOrWhiteSpace(SelectedProviderKey)) { Institutions = []; SelectedInstitutionId = null; return; }
        try { Institutions = await FinanceClient.GetBankInstitutionsAsync(companyId, SelectedProviderKey); SelectedInstitutionId = Institutions.FirstOrDefault()?.InstitutionId; }
        catch (Exception exception) { Institutions = []; ActionError = exception.Message; }
    }
    private async Task ConnectAsync()
    {
        if (!CanManage || AccessState.CompanyId is not Guid companyId || string.IsNullOrWhiteSpace(SelectedProviderKey) || string.IsNullOrWhiteSpace(SelectedInstitutionId)) return;
        await MutateAsync(async () =>
        {
            var result = await FinanceClient.StartBankConnectionAsync(companyId, new(SelectedProviderKey, SelectedInstitutionId,
                Navigation.Uri, ["accounts", "account_ownership", "transactions"]));
            Navigation.NavigateTo(result.AuthorizationUri, forceLoad: true);
        });
    }
    private async Task RenewAsync(BankConnectionResponse connection) => await MutateAsync(async () =>
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        var result = await FinanceClient.RenewBankConnectionAsync(companyId, connection.Id, new(connection.ProviderKey, connection.Version, Navigation.Uri));
        Navigation.NavigateTo(result.AuthorizationUri, forceLoad: true);
    });
    private async Task RefreshAsync(BankConnectionResponse connection) => await MutateAsync(async () =>
    { if (AccessState.CompanyId is not Guid companyId) return; Status = await FinanceClient.RefreshBankConnectionAsync(companyId, connection.Id, connection.Version); ActionMessage = FinanceText["BankConnectionRefreshed"]; });
    private async Task SuspendAsync(BankConnectionResponse connection) => await MutateAsync(async () =>
    { if (AccessState.CompanyId is not Guid companyId) return; Status = await FinanceClient.SuspendBankConnectionAsync(companyId, connection.Id, new(connection.Version, FinanceText["AdministratorSuspensionReason"])); ActionMessage = FinanceText["BankConnectionSuspended"]; });
    private async Task DisconnectAsync(BankConnectionResponse connection)
    {
        if (_disconnectConfirmationId != connection.Id) { _disconnectConfirmationId = connection.Id; ActionMessage = FinanceText["DisconnectConfirmationHelp"]; return; }
        await MutateAsync(async () =>
        { if (AccessState.CompanyId is not Guid companyId) return; Status = await FinanceClient.DisconnectBankConnectionAsync(companyId, connection.Id, new(connection.Version, FinanceText["AdministratorDisconnectReason"])); _disconnectConfirmationId = null; ActionMessage = FinanceText["BankConnectionDisconnected"]; });
    }
    private async Task MapAsync(BankConnectionResponse connection, BankDiscoveredAccountResponse account) => await MutateAsync(async () =>
    {
        if (AccessState.CompanyId is not Guid companyId || !MappingTargets.TryGetValue(account.Id, out var target) || target == Guid.Empty) return;
        await FinanceClient.MapBankAccountAsync(companyId, connection.Id, account.Id, new(target, connection.Version, FinanceText["ExplicitAccountMappingReason"]));
        Status = await FinanceClient.GetBankConnectionsAsync(companyId); ActionMessage = FinanceText["BankAccountMapped"];
    });
    private async Task SynchronizeFeedsAsync(Guid? checkpointId = null) => await MutateAsync(async () =>
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        var result = await FinanceClient.RequestBankFeedSynchronizationAsync(companyId, checkpointId);
        FeedHealth = await FinanceClient.GetBankFeedHealthAsync(companyId);
        ActionMessage = result.Explanation;
    });
    private async Task RecoverGapAsync(BankFeedAccountHealthResponse account, BankFeedGapResponse gap) => await MutateAsync(async () =>
    {
        if (AccessState.CompanyId is not Guid companyId) return;
        var result = await FinanceClient.RequestBankFeedBackfillAsync(companyId, account.CheckpointId, gap.Id,
            new(gap.DateFrom, gap.DateTo, account.Version, FinanceText["BankFeedRecoveryReason"]));
        FeedHealth = await FinanceClient.GetBankFeedHealthAsync(companyId);
        ActionMessage = result.Explanation;
    });
    private async Task MutateAsync(Func<Task> action)
    {
        if (!CanManage || IsBusy) return; IsBusy = true; ActionError = null; ActionMessage = null;
        try { await action(); } catch (Exception exception) { ActionError = exception.Message; } finally { IsBusy = false; }
    }
    private bool CanMap(BankDiscoveredAccountResponse account) => CanManage && !IsBusy && account.OwnershipStatus == "verified";
    private IEnumerable<BankInternalAccountResponse> CompatibleAccounts(BankDiscoveredAccountResponse account) => Status?.InternalAccounts.Where(x => x.IsActive && x.Currency == account.Currency) ?? [];
    private string ProviderName(string key) => Status?.Providers.FirstOrDefault(x => x.ProviderKey == key)?.DisplayName ?? key;
    private string FormatDate(DateTime? value) => value.HasValue ? LocalDateTime.DateTime(value.Value) : FinanceText["NotAvailable"];
    private string StatusLabel(BankConnectionResponse connection) => FinanceText[connection.Status switch { "active" => "Connected", "attention_required" => "NeedsAttention", "suspended" => "Suspended", "revoked" => "Revoked", "disconnected" => "Disconnected", _ => "Pending" }];
    private static string StatusClass(BankConnectionResponse connection) => $"bank-status bank-status--{connection.Status.Replace('_', '-')}";
    private string HealthLabel(string health) => FinanceText[health switch { "healthy" => "Healthy", "degraded" => "Degraded", "outage" => "ProviderOutage", _ => "Unknown" }];
    private string OwnershipLabel(string ownership) => FinanceText[ownership switch { "verified" => "Verified", "mismatch" => "OwnershipMismatch", _ => "Unverified" }];
    private string ReasonLabel(string? reason) => FinanceText[reason switch { "expired_consent" => "RenewalRequired", "scope_loss" => "ScopeLoss", "account_ownership_mismatch" => "OwnershipMismatch", "provider_outage" => "ProviderOutage", "reconciliation_required_setup" => "ReconciliationRequired", _ => "NeedsAttention" }];
    private string FeedStatusLabel(string status) => FinanceText[status switch
    {
        "ready" => "Healthy", "queued" => "BankFeedQueued", "running" => "BankFeedSyncing",
        "failed" => "BankFeedRetrying", "attention_required" => "NeedsAttention",
        "paused" => "BankFeedPaused", _ => "Unknown"
    }];
    private static string FeedStatusClass(string status) => $"feed-status feed-status--{status.Replace('_', '-')}";
    private string FormatCoverage(BankFeedAccountHealthResponse account) => account.CoverageThrough.HasValue
        ? account.CoverageFrom == account.CoverageThrough ? account.CoverageThrough.Value.ToString("yyyy-MM-dd")
            : $"{account.CoverageFrom:yyyy-MM-dd} – {account.CoverageThrough:yyyy-MM-dd}"
        : FinanceText["NotAvailable"];
    private string FormatLag(int minutes) => minutes < 60 ? FinanceText["MinutesShort", minutes]
        : minutes < 1440 ? FinanceText["HoursShort", Math.Ceiling(minutes / 60d)]
        : FinanceText["DaysShort", Math.Ceiling(minutes / 1440d)];
    private void ApplyCallbackMessage()
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(Navigation.ToAbsoluteUri(Navigation.Uri).Query);
        if (query.TryGetValue("bankConnection", out var state) && state == "connected") ActionMessage = FinanceText["BankConnectionCompleted"];
        else if (query.TryGetValue("reason", out var reason)) ActionError = ReasonLabel(reason.ToString());
    }
}
