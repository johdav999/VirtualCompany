

namespace VirtualCompany.Application.Finance;


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


public sealed record CustomerBillingAddressDto(
    string Line1,
    string? Line2,
    string PostalCode,
    string City,
    string? Region,
    string CountryCode);


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


public sealed record CustomerDuplicateEvidenceDto(string Fact, string Explanation, int Weight);
