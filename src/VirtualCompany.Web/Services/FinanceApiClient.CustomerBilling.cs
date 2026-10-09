namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<CustomerBillingProfileResponse?> GetCustomerBillingProfileAsync(Guid companyId, Guid counterpartyId,
        CancellationToken cancellationToken = default) => _useOfflineMode
        ? Task.FromResult<CustomerBillingProfileResponse?>(null)
        : GetAsync<CustomerBillingProfileResponse>(companyId,
            $"internal/companies/{companyId}/finance/customers/{counterpartyId}/billing-profile", allowNotFound: true, cancellationToken);

    public Task<IReadOnlyList<CustomerBillingProfileVersionResponse>> GetCustomerBillingProfileHistoryAsync(
        Guid companyId, Guid counterpartyId, int limit = 100, CancellationToken cancellationToken = default) =>
        _useOfflineMode ? Task.FromResult<IReadOnlyList<CustomerBillingProfileVersionResponse>>([]) :
        GetListAsync<CustomerBillingProfileVersionResponse>(companyId,
            $"internal/companies/{companyId}/finance/customers/{counterpartyId}/billing-profile/history?limit={Math.Clamp(limit, 1, 500)}", cancellationToken);

    public Task<CustomerBillingProfileResponse> UpsertCustomerBillingProfileAsync(Guid companyId, Guid counterpartyId,
        UpsertCustomerBillingProfileApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<UpsertCustomerBillingProfileApiRequest, CustomerBillingProfileResponse>(companyId,
            HttpMethod.Put, $"internal/companies/{companyId}/finance/customers/{counterpartyId}/billing-profile", request, cancellationToken);
    }

    public Task<CustomerBillingProfileResponse> ResolveCustomerBillingSourceConflictAsync(Guid companyId, Guid conflictId,
        ResolveCustomerBillingSourceConflictApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ResolveCustomerBillingSourceConflictApiRequest, CustomerBillingProfileResponse>(companyId,
            HttpMethod.Put, $"internal/companies/{companyId}/finance/customer-billing/source-conflicts/{conflictId}", request, cancellationToken);
    }

    public Task<IReadOnlyList<CustomerDuplicateCandidateResponse>> GetCustomerDuplicateCandidatesAsync(Guid companyId,
        string? status = null, int limit = 100, CancellationToken cancellationToken = default) => _useOfflineMode
        ? Task.FromResult<IReadOnlyList<CustomerDuplicateCandidateResponse>>([])
        : GetListAsync<CustomerDuplicateCandidateResponse>(companyId,
            $"internal/companies/{companyId}/finance/customer-duplicates{BuildQuery(("status", status), ("limit", Math.Clamp(limit, 1, 500).ToString()))}", cancellationToken);

    public Task<CustomerDuplicateCandidateResponse> DecideCustomerDuplicateAsync(Guid companyId, Guid candidateId,
        DecideCustomerDuplicateApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<DecideCustomerDuplicateApiRequest, CustomerDuplicateCandidateResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/customer-duplicates/{candidateId}/decision", request, cancellationToken);
    }
}
