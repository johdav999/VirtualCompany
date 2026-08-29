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

public sealed record StartBankConnectionApiRequest(string ProviderKey, string InstitutionId, string? ReturnUri, IReadOnlyCollection<string> RequestedCapabilities);
public sealed record RenewBankConnectionApiRequest(string ProviderKey, long ExpectedVersion, string? ReturnUri);
public sealed record MapBankAccountApiRequest(Guid CompanyBankAccountId, long ExpectedConnectionVersion, string Reason);
public sealed record BankConnectionVersionApiRequest(long ExpectedVersion);
public sealed record ChangeBankConnectionStateApiRequest(long ExpectedVersion, string? Reason);
public sealed record BankConsentSessionResponse(Guid SessionId, string AuthorizationUri, DateTime ExpiresUtc);
public sealed record BankAccountMappingResponse(Guid MappingId, int MappingVersion, long ConnectionVersion);
public sealed record BankSynchronizationAccessResponse(bool Allowed, string? ReasonCode, string Explanation, bool RenewalRequired);
public sealed record BankProviderResponse(string ProviderKey, string DisplayName, IReadOnlyList<string> Capabilities, bool IsConfigured);
public sealed record BankInstitutionResponse(string InstitutionId, string DisplayName, string? CountryCode, IReadOnlyList<string> Capabilities);
public sealed record BankInternalAccountResponse(Guid Id, string DisplayName, string MaskedAccountNumber, string Currency, bool IsActive);
public sealed record BankDiscoveredAccountResponse(Guid Id, string ProviderAccountId, string DisplayName, string MaskedAccountNumber,
    string Currency, string OwnershipStatus, string? OwnershipSummary, int Version, Guid? MappedCompanyBankAccountId,
    string? MappedCompanyBankAccountName, int? MappingVersion);
public sealed record BankConnectionResponse(Guid Id, string ProviderKey, string InstitutionId, string InstitutionName,
    string Status, string HealthStatus, string? ReasonCode, string? ReasonSummary, DateTime? ConsentExpiresUtc,
    DateTime? LastHealthCheckedUtc, long Version, IReadOnlyList<string> Capabilities, IReadOnlyList<BankDiscoveredAccountResponse> Accounts);
public sealed record BankConnectionStatusResponse(IReadOnlyList<BankProviderResponse> Providers,
    IReadOnlyList<BankConnectionResponse> Connections, IReadOnlyList<BankInternalAccountResponse> InternalAccounts);
public sealed record RequestBankFeedSynchronizationApiRequest(Guid? CheckpointId);
public sealed record RequestBankFeedBackfillApiRequest(DateOnly DateFrom, DateOnly DateTo, long ExpectedCheckpointVersion, string Reason);
public sealed record BankFeedRequestResponse(int QueuedAccountCount, string Status, string Explanation);
public sealed record BankFeedGapResponse(Guid Id, string Kind, DateOnly DateFrom, DateOnly DateTo,
    string Status, string ReasonCode, string Summary, DateTime DetectedUtc, DateTime? ResolvedUtc);
public sealed record BankFeedAccountHealthResponse(Guid CheckpointId, Guid ConnectionId, Guid DiscoveredAccountId,
    Guid CompanyBankAccountId, string InstitutionName, string AccountName, string MaskedAccountNumber, string Currency,
    string Status, string? ReasonCode, string? FailureSummary, DateOnly? CoverageFrom, DateOnly? CoverageThrough,
    DateTime? LastSuccessfulSyncUtc, DateTime? LastAttemptUtc, DateTime? NextAttemptUtc, int LagMinutes,
    long Version, IReadOnlyList<BankFeedGapResponse> Gaps);
public sealed record BankFeedHealthResponse(int HealthyCount, int AttentionCount, DateTime? LatestSuccessfulCoverageUtc,
    int MaximumLagMinutes, IReadOnlyList<BankFeedAccountHealthResponse> Accounts);
