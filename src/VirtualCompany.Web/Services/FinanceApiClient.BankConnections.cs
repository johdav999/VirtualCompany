namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<BankConnectionStatusResponse> GetBankConnectionsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<BankConnectionStatusResponse>(companyId, $"api/companies/{companyId}/finance/bank-connections", false, cancellationToken)!;
    public Task<List<BankInstitutionResponse>> GetBankInstitutionsAsync(Guid companyId, string providerKey, CancellationToken cancellationToken = default) =>
        GetAsync<List<BankInstitutionResponse>>(companyId, $"api/companies/{companyId}/finance/bank-connections/providers/{Uri.EscapeDataString(providerKey)}/institutions", false, cancellationToken)!;
    public Task<BankConsentSessionResponse> StartBankConnectionAsync(Guid companyId, StartBankConnectionApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<StartBankConnectionApiRequest, BankConsentSessionResponse>(companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-connections/connect", request, cancellationToken); }
    public Task<BankConsentSessionResponse> RenewBankConnectionAsync(Guid companyId, Guid connectionId, RenewBankConnectionApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<RenewBankConnectionApiRequest, BankConsentSessionResponse>(companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-connections/{connectionId:D}/renew", request, cancellationToken); }
    public Task<BankAccountMappingResponse> MapBankAccountAsync(Guid companyId, Guid connectionId, Guid discoveredAccountId, MapBankAccountApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<MapBankAccountApiRequest, BankAccountMappingResponse>(companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-connections/{connectionId:D}/accounts/{discoveredAccountId:D}/mapping", request, cancellationToken); }
    public Task<BankConnectionStatusResponse> RefreshBankConnectionAsync(Guid companyId, Guid connectionId, long expectedVersion, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<BankConnectionVersionApiRequest, BankConnectionStatusResponse>(companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-connections/{connectionId:D}/refresh", new(expectedVersion), cancellationToken); }
    public Task<BankConnectionStatusResponse> SuspendBankConnectionAsync(Guid companyId, Guid connectionId, ChangeBankConnectionStateApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<ChangeBankConnectionStateApiRequest, BankConnectionStatusResponse>(companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-connections/{connectionId:D}/suspend", request, cancellationToken); }
    public Task<BankConnectionStatusResponse> DisconnectBankConnectionAsync(Guid companyId, Guid connectionId, ChangeBankConnectionStateApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<ChangeBankConnectionStateApiRequest, BankConnectionStatusResponse>(companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-connections/{connectionId:D}/disconnect", request, cancellationToken); }
    public Task<BankSynchronizationAccessResponse> GetBankSynchronizationAccessAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken = default) =>
        GetAsync<BankSynchronizationAccessResponse>(companyId, $"api/companies/{companyId}/finance/bank-connections/{connectionId:D}/synchronization-access", false, cancellationToken)!;
    public Task<BankFeedHealthResponse> GetBankFeedHealthAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        GetAsync<BankFeedHealthResponse>(companyId, $"api/companies/{companyId}/finance/bank-feeds", false, cancellationToken)!;
    public Task<BankFeedRequestResponse> RequestBankFeedSynchronizationAsync(Guid companyId, Guid? checkpointId, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<RequestBankFeedSynchronizationApiRequest, BankFeedRequestResponse>(companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-feeds/synchronize", new(checkpointId), cancellationToken); }
    public Task<BankFeedRequestResponse> RequestBankFeedBackfillAsync(Guid companyId, Guid checkpointId, Guid gapId, RequestBankFeedBackfillApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<RequestBankFeedBackfillApiRequest, BankFeedRequestResponse>(companyId, HttpMethod.Post, $"api/companies/{companyId}/finance/bank-feeds/{checkpointId:D}/gaps/{gapId:D}/backfill", request, cancellationToken); }
}
