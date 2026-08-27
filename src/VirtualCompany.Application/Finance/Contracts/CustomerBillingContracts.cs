namespace VirtualCompany.Application.Finance;

public sealed record CustomerBillingAddressDto(
    string Line1,
    string? Line2,
    string PostalCode,
    string City,
    string? Region,
    string CountryCode);

public sealed record CustomerBillingProfileInputDto(
    string LegalName,
    string? DisplayName,
    string PartyKind,
    string? TaxIdentifier,
    string? VatIdentifier,
    string IdentityValidationState,
    CustomerBillingAddressDto BillingAddress,
    CustomerBillingAddressDto? DeliveryAddress,
    string LanguageCode,
    string CurrencyCode,
    string PaymentTermKind,
    int PaymentTermDays,
    string PaymentMethod,
    string InvoiceDeliveryChannel,
    string? InvoiceDeliveryEmail,
    string? BuyerReference,
    string? EInvoiceIdentifier,
    string? EInvoiceIdentifierType,
    decimal CreditLimit,
    string CreditStatus,
    string? DefaultAccountMapping,
    string? DefaultDimensionCode,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string SourceKind,
    string? SourceReference,
    DateTime? UserAttestedUtc,
    DateTime? ExternallyVerifiedUtc,
    string? VerificationSource);

public sealed record CustomerBillingSourceConflictDto(
    Guid Id,
    long BaseVersion,
    string ExistingSourceKind,
    string IncomingSourceKind,
    string? IncomingSourceReference,
    IReadOnlyList<string> ChangedFields,
    string Status,
    bool? UsedIncomingValues,
    string? DecisionReason,
    DateTime DetectedUtc,
    DateTime? DecidedUtc,
    long Version);

public sealed record CustomerBillingProfileDto(
    Guid Id,
    Guid CompanyId,
    Guid CounterpartyId,
    CustomerBillingProfileInputDto Profile,
    string ConflictState,
    Guid? MergedIntoCounterpartyId,
    long Version,
    Guid CreatedByUserId,
    Guid UpdatedByUserId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyList<CustomerBillingSourceConflictDto> Conflicts);

public sealed record CustomerBillingProfileVersionDto(
    Guid Id,
    Guid CounterpartyId,
    long ProfileVersion,
    string SourceKind,
    string? SourceReference,
    IReadOnlyList<string> ChangedFields,
    string SnapshotHash,
    Guid ActorUserId,
    DateTime CreatedUtc);

public sealed record CustomerDuplicateEvidenceDto(string Fact, string Explanation, int Weight);

public sealed record CustomerDuplicateCandidateDto(
    Guid Id,
    Guid CompanyId,
    Guid FirstCounterpartyId,
    string FirstCustomerName,
    Guid SecondCounterpartyId,
    string SecondCustomerName,
    int Score,
    IReadOnlyList<CustomerDuplicateEvidenceDto> Evidence,
    string Status,
    Guid? MergeSourceCounterpartyId,
    Guid? MergeTargetCounterpartyId,
    string? DecisionReason,
    DateTime DetectedUtc,
    DateTime UpdatedUtc,
    long Version);

public sealed record GetCustomerBillingProfileQuery(Guid CompanyId, Guid CounterpartyId);
public sealed record GetCustomerBillingProfileHistoryQuery(Guid CompanyId, Guid CounterpartyId, int Limit = 100);
public sealed record GetCustomerDuplicateCandidatesQuery(Guid CompanyId, string? Status = null, int Limit = 100);

public sealed record UpsertCustomerBillingProfileCommand(
    Guid CompanyId,
    Guid CounterpartyId,
    CustomerBillingProfileInputDto Profile,
    long? ExpectedVersion,
    Guid ActorUserId,
    string? CorrelationId);

public sealed record ResolveCustomerBillingSourceConflictCommand(
    Guid CompanyId,
    Guid ConflictId,
    long ExpectedConflictVersion,
    long ExpectedProfileVersion,
    bool UseIncomingValues,
    string Reason,
    Guid ActorUserId,
    string? CorrelationId);

public sealed record DecideCustomerDuplicateCommand(
    Guid CompanyId,
    Guid CandidateId,
    long ExpectedVersion,
    string Decision,
    Guid? MergeSourceCounterpartyId,
    Guid? MergeTargetCounterpartyId,
    string Reason,
    Guid ActorUserId,
    string? CorrelationId);

public static class CustomerDuplicateDecisions
{
    public const string Merge = "merge";
    public const string KeepSeparate = "keep_separate";
}

public static class CustomerBillingReasonCodes
{
    public const string ProfileNotFound = "customer_billing_profile_not_found";
    public const string CustomerNotFound = "customer_not_found";
    public const string CandidateNotFound = "customer_duplicate_candidate_not_found";
    public const string ConflictNotFound = "customer_billing_conflict_not_found";
    public const string ConcurrencyConflict = "customer_billing_concurrency_conflict";
    public const string SourceConflict = "customer_billing_source_conflict";
    public const string InvalidDecision = "customer_duplicate_invalid_decision";
    public const string UnsafeMerge = "customer_duplicate_unsafe_merge";
    public const string AlreadyDecided = "customer_duplicate_already_decided";
}

public sealed class CustomerBillingException : Exception
{
    public CustomerBillingException(string reasonCode, string message, bool isConflict = false, bool isNotFound = false)
        : base(message) { ReasonCode = reasonCode; IsConflict = isConflict; IsNotFound = isNotFound; }
    public string ReasonCode { get; }
    public bool IsConflict { get; }
    public bool IsNotFound { get; }
}

public interface ICustomerBillingProfileService
{
    Task<CustomerBillingProfileDto?> GetAsync(GetCustomerBillingProfileQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerBillingProfileVersionDto>> GetHistoryAsync(GetCustomerBillingProfileHistoryQuery query, CancellationToken cancellationToken);
    Task<CustomerBillingProfileDto> UpsertAsync(UpsertCustomerBillingProfileCommand command, CancellationToken cancellationToken);
    Task<CustomerBillingProfileDto> ResolveConflictAsync(ResolveCustomerBillingSourceConflictCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<CustomerDuplicateCandidateDto>> GetDuplicateCandidatesAsync(GetCustomerDuplicateCandidatesQuery query, CancellationToken cancellationToken);
    Task<CustomerDuplicateCandidateDto> DecideDuplicateAsync(DecideCustomerDuplicateCommand command, CancellationToken cancellationToken);
}
