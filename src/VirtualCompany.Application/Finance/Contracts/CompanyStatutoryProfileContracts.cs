namespace VirtualCompany.Application.Finance;

public static class CompanyStatutoryProfileReasonCodes
{
    public const string NotFound = "company_statutory_profile_not_found";
    public const string AlreadyExists = "company_statutory_profile_already_exists";
    public const string ConcurrencyConflict = "company_statutory_profile_concurrency_conflict";
    public const string InvalidProfile = "company_statutory_profile_invalid";
}

public static class StatutoryProfileFactKeys
{
    public const string LegalName = "legal_name";
    public const string OrganisationNumber = "swedish_organisation_number";
    public const string RegisteredAddress = "registered_address";
    public const string CountryCode = "country_code";
    public const string AccountingCurrency = "accounting_currency";
    public const string FiscalYearBasis = "fiscal_year_basis";
    public const string BookkeepingMethod = "bookkeeping_method";
    public const string OrganisationRegistrationDate = "organisation_registration_effective_from";
    public const string VatRegistrationNumber = "vat_registration_number";
    public const string VatRegistrationDate = "vat_registration_effective_from";
    public const string UserAttestation = "user_attestation";
}

public sealed record StatutoryAddressDto(
    string? AddressLine1,
    string? AddressLine2,
    string? PostalCode,
    string? City,
    string? CountryCode);

public sealed record CompanyStatutoryProfileDto(
    Guid Id,
    Guid CompanyId,
    string? LegalName,
    string? SwedishOrganisationNumber,
    string? VatRegistrationNumber,
    string VatRegistrationStatus,
    StatutoryAddressDto RegisteredAddress,
    StatutoryAddressDto CorrespondenceAddress,
    string CountryCode,
    string AccountingCurrency,
    string FiscalYearBasis,
    string BookkeepingMethod,
    DateOnly? OrganisationRegistrationEffectiveFrom,
    DateOnly? VatRegistrationEffectiveFrom,
    DateOnly? VatRegistrationEffectiveTo,
    bool IsFormatComplete,
    bool IsUserAttested,
    Guid? AttestedByUserId,
    DateTime? AttestedUtc,
    string VerificationStatus,
    string SourceKind,
    string? SourceReference,
    DateTime SourceCapturedUtc,
    string? ExternalVerifier,
    DateTime? ExternallyVerifiedUtc,
    long Version,
    Guid CreatedByUserId,
    Guid UpdatedByUserId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record CompanyStatutoryProfileStatusDto(
    Guid CompanyId,
    bool Exists,
    bool IsFormatComplete,
    bool IsUserAttested,
    bool IsExternallyVerified,
    bool IsCompleteForSelectedPolicyPack,
    string VerificationExplanation,
    IReadOnlyList<string> MissingFacts,
    IReadOnlyList<string> NextActions,
    CompanyStatutoryProfileDto? Profile);

public sealed record CompanyStatutoryProfileInput(
    string? LegalName,
    string? SwedishOrganisationNumber,
    string? VatRegistrationNumber,
    string VatRegistrationStatus,
    StatutoryAddressDto RegisteredAddress,
    StatutoryAddressDto? CorrespondenceAddress,
    string CountryCode,
    string AccountingCurrency,
    string FiscalYearBasis,
    string BookkeepingMethod,
    DateOnly? OrganisationRegistrationEffectiveFrom,
    DateOnly? VatRegistrationEffectiveFrom,
    DateOnly? VatRegistrationEffectiveTo,
    bool IsUserAttested,
    string VerificationStatus,
    string SourceKind,
    string? SourceReference,
    DateTime SourceCapturedUtc,
    string? ExternalVerifier,
    DateTime? ExternallyVerifiedUtc);

public sealed record GetCompanyStatutoryProfileQuery(Guid CompanyId);
public sealed record CreateCompanyStatutoryProfileCommand(
    Guid CompanyId,
    CompanyStatutoryProfileInput Profile,
    Guid ActorUserId,
    string? CorrelationId = null);
public sealed record UpdateCompanyStatutoryProfileCommand(
    Guid CompanyId,
    long ExpectedVersion,
    CompanyStatutoryProfileInput Profile,
    Guid ActorUserId,
    string? CorrelationId = null);

public interface ICompanyStatutoryProfileService
{
    Task<CompanyStatutoryProfileStatusDto> GetAsync(GetCompanyStatutoryProfileQuery query, CancellationToken cancellationToken);
    Task<CompanyStatutoryProfileStatusDto> CreateAsync(CreateCompanyStatutoryProfileCommand command, CancellationToken cancellationToken);
    Task<CompanyStatutoryProfileStatusDto> UpdateAsync(UpdateCompanyStatutoryProfileCommand command, CancellationToken cancellationToken);
}

public sealed class CompanyStatutoryProfileException : Exception
{
    public CompanyStatutoryProfileException(string reasonCode, string message, bool isConflict = false)
        : base(message)
    {
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode)
            ? throw new ArgumentException("ReasonCode is required.", nameof(reasonCode))
            : reasonCode.Trim();
        IsConflict = isConflict;
    }

    public string ReasonCode { get; }
    public bool IsConflict { get; }
}
