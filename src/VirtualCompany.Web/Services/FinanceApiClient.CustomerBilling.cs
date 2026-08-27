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

public sealed record CustomerBillingAddressResponse(string Line1, string? Line2, string PostalCode, string City,
    string? Region, string CountryCode);
public sealed record CustomerBillingProfileInputResponse(string LegalName, string? DisplayName, string PartyKind,
    string? TaxIdentifier, string? VatIdentifier, string IdentityValidationState, CustomerBillingAddressResponse BillingAddress,
    CustomerBillingAddressResponse? DeliveryAddress, string LanguageCode, string CurrencyCode, string PaymentTermKind,
    int PaymentTermDays, string PaymentMethod, string InvoiceDeliveryChannel, string? InvoiceDeliveryEmail,
    string? BuyerReference, string? EInvoiceIdentifier, string? EInvoiceIdentifierType, decimal CreditLimit,
    string CreditStatus, string? DefaultAccountMapping, string? DefaultDimensionCode, DateOnly EffectiveFrom,
    DateOnly? EffectiveTo, string SourceKind, string? SourceReference, DateTime? UserAttestedUtc,
    DateTime? ExternallyVerifiedUtc, string? VerificationSource);
public sealed record CustomerBillingSourceConflictResponse(Guid Id, long BaseVersion, string ExistingSourceKind,
    string IncomingSourceKind, string? IncomingSourceReference, IReadOnlyList<string> ChangedFields, string Status,
    bool? UsedIncomingValues, string? DecisionReason, DateTime DetectedUtc, DateTime? DecidedUtc, long Version);
public sealed record CustomerBillingProfileResponse(Guid Id, Guid CompanyId, Guid CounterpartyId,
    CustomerBillingProfileInputResponse Profile, string ConflictState, Guid? MergedIntoCounterpartyId, long Version,
    Guid CreatedByUserId, Guid UpdatedByUserId, DateTime CreatedUtc, DateTime UpdatedUtc,
    IReadOnlyList<CustomerBillingSourceConflictResponse> Conflicts);
public sealed record CustomerBillingProfileVersionResponse(Guid Id, Guid CounterpartyId, long ProfileVersion,
    string SourceKind, string? SourceReference, IReadOnlyList<string> ChangedFields, string SnapshotHash,
    Guid ActorUserId, DateTime CreatedUtc);
public sealed record CustomerDuplicateEvidenceResponse(string Fact, string Explanation, int Weight);
public sealed record CustomerDuplicateCandidateResponse(Guid Id, Guid CompanyId, Guid FirstCounterpartyId,
    string FirstCustomerName, Guid SecondCounterpartyId, string SecondCustomerName, int Score,
    IReadOnlyList<CustomerDuplicateEvidenceResponse> Evidence, string Status, Guid? MergeSourceCounterpartyId,
    Guid? MergeTargetCounterpartyId, string? DecisionReason, DateTime DetectedUtc, DateTime UpdatedUtc, long Version);

public sealed record UpsertCustomerBillingProfileApiRequest(CustomerBillingProfileInputResponse Profile, long? ExpectedVersion);
public sealed record ResolveCustomerBillingSourceConflictApiRequest(long ExpectedConflictVersion, long ExpectedProfileVersion,
    bool UseIncomingValues, string Reason);
public sealed record DecideCustomerDuplicateApiRequest(long ExpectedVersion, string Decision,
    Guid? MergeSourceCounterpartyId, Guid? MergeTargetCounterpartyId, string Reason);
