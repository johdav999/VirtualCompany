using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using Microsoft.Extensions.Logging;
using VirtualCompany.Shared;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<List<FinanceIntegrationProviderResponse>> GetFinanceIntegrationProvidersAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<List<FinanceIntegrationProviderResponse>>(companyId, $"api/companies/{companyId}/finance/integrations", allowNotFound: false, cancellationToken)!;

    public Task<FinanceIntegrationConnectionStatusResponse> GetFinanceIntegrationStatusAsync(Guid companyId, string providerKey, CancellationToken cancellationToken = default) =>
        GetAsync<FinanceIntegrationConnectionStatusResponse>(companyId, $"api/companies/{companyId}/finance/integrations/{Uri.EscapeDataString(providerKey)}/status", allowNotFound: false, cancellationToken)!;

    public Task<StartFinanceIntegrationConnectionResponse> StartFinanceIntegrationConnectionAsync(
        Guid companyId,
        string providerKey,
        string returnUri,
        bool reconnect,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        var action = reconnect ? "reconnect" : "connect";
        return SendCompanyScopedAsync<StartFinanceIntegrationConnectionRequest, StartFinanceIntegrationConnectionResponse>(
            companyId,
            HttpMethod.Post,
            $"api/companies/{companyId}/finance/integrations/{Uri.EscapeDataString(providerKey)}/{action}",
            new StartFinanceIntegrationConnectionRequest(returnUri, reconnect),
            cancellationToken);
    }

    public Task<FinanceIntegrationSyncResultResponse> SyncFinanceIntegrationNowAsync(
        Guid companyId,
        string providerKey,
        Guid? connectionId,
        CancellationToken cancellationToken = default) =>
        SyncFinanceIntegrationNowCoreAsync(companyId, providerKey, connectionId, cancellationToken);

    private Task<FinanceIntegrationSyncResultResponse> SyncFinanceIntegrationNowCoreAsync(
        Guid companyId,
        string providerKey,
        Guid? connectionId,
        CancellationToken cancellationToken)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SyncFinanceIntegrationNowRequest, FinanceIntegrationSyncResultResponse>(
            companyId,
            HttpMethod.Post,
            $"api/companies/{companyId}/finance/integrations/{Uri.EscapeDataString(providerKey)}/sync",
            new SyncFinanceIntegrationNowRequest(connectionId, FullSync: true),
            cancellationToken);
    }

    public Task<FinanceIntegrationSyncHistoryResponse> GetFinanceIntegrationSyncHistoryAsync(
        Guid companyId,
        string providerKey,
        CancellationToken cancellationToken = default) =>
        GetAsync<FinanceIntegrationSyncHistoryResponse>(companyId, $"api/companies/{companyId}/finance/integrations/{Uri.EscapeDataString(providerKey)}/sync-history?limit=25", allowNotFound: false, cancellationToken)!;

    public Task<FinanceIntegrationConnectionDisconnectResponse> DisconnectFinanceIntegrationAsync(
        Guid companyId,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, FinanceIntegrationConnectionDisconnectResponse>(companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/integrations/{Uri.EscapeDataString(providerKey)}/disconnect", new { }, cancellationToken);
    }

}

