namespace VirtualCompany.Application.Finance;
public sealed record CompanyStatutoryProfileDto(Guid Id, Guid CompanyId, string? LegalName, string? SwedishOrganisationNumber, string? VatRegistrationNumber, string VatRegistrationStatus, StatutoryAddressDto RegisteredAddress, StatutoryAddressDto CorrespondenceAddress, string CountryCode, string AccountingCurrency, string FiscalYearBasis, string BookkeepingMethod, DateOnly? OrganisationRegistrationEffectiveFrom, DateOnly? VatRegistrationEffectiveFrom, DateOnly? VatRegistrationEffectiveTo, bool IsFormatComplete, bool IsUserAttested, Guid? AttestedByUserId, DateTime? AttestedUtc, string VerificationStatus, string SourceKind, string? SourceReference, DateTime SourceCapturedUtc, string? ExternalVerifier, DateTime? ExternallyVerifiedUtc, long Version, Guid CreatedByUserId, Guid UpdatedByUserId, DateTime CreatedUtc, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public Guid CompanyId { get; set; } = CompanyId;
    public string? LegalName { get; set; } = LegalName;
    public string? SwedishOrganisationNumber { get; set; } = SwedishOrganisationNumber;
    public string? VatRegistrationNumber { get; set; } = VatRegistrationNumber;
    public string VatRegistrationStatus { get; set; } = VatRegistrationStatus;
    public StatutoryAddressDto RegisteredAddress { get; set; } = RegisteredAddress;
    public StatutoryAddressDto CorrespondenceAddress { get; set; } = CorrespondenceAddress;
    public string CountryCode { get; set; } = CountryCode;
    public string AccountingCurrency { get; set; } = AccountingCurrency;
    public string FiscalYearBasis { get; set; } = FiscalYearBasis;
    public string BookkeepingMethod { get; set; } = BookkeepingMethod;
    public DateOnly? OrganisationRegistrationEffectiveFrom { get; set; } = OrganisationRegistrationEffectiveFrom;
    public DateOnly? VatRegistrationEffectiveFrom { get; set; } = VatRegistrationEffectiveFrom;
    public DateOnly? VatRegistrationEffectiveTo { get; set; } = VatRegistrationEffectiveTo;
    public bool IsFormatComplete { get; set; } = IsFormatComplete;
    public bool IsUserAttested { get; set; } = IsUserAttested;
    public Guid? AttestedByUserId { get; set; } = AttestedByUserId;
    public DateTime? AttestedUtc { get; set; } = AttestedUtc;
    public string VerificationStatus { get; set; } = VerificationStatus;
    public string SourceKind { get; set; } = SourceKind;
    public string? SourceReference { get; set; } = SourceReference;
    public DateTime SourceCapturedUtc { get; set; } = SourceCapturedUtc;
    public string? ExternalVerifier { get; set; } = ExternalVerifier;
    public DateTime? ExternallyVerifiedUtc { get; set; } = ExternallyVerifiedUtc;
    public long Version { get; set; } = Version;
    public Guid CreatedByUserId { get; set; } = CreatedByUserId;
    public Guid UpdatedByUserId { get; set; } = UpdatedByUserId;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public CompanyStatutoryProfileDto() : this(default !, default !, default !, default !, default !, string.Empty, new(), new(), string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record CompanyStatutoryProfileStatusDto(Guid CompanyId, bool Exists, bool IsFormatComplete, bool IsUserAttested, bool IsExternallyVerified, bool IsCompleteForSelectedPolicyPack, string VerificationExplanation, IReadOnlyList<string> MissingFacts, IReadOnlyList<string> NextActions, CompanyStatutoryProfileDto? Profile)
{
    public Guid CompanyId { get; set; } = CompanyId;
    public bool Exists { get; set; } = Exists;
    public bool IsFormatComplete { get; set; } = IsFormatComplete;
    public bool IsUserAttested { get; set; } = IsUserAttested;
    public bool IsExternallyVerified { get; set; } = IsExternallyVerified;
    public bool IsCompleteForSelectedPolicyPack { get; set; } = IsCompleteForSelectedPolicyPack;
    public string VerificationExplanation { get; set; } = VerificationExplanation;
    public IReadOnlyList<string> MissingFacts { get; set; } = MissingFacts;
    public IReadOnlyList<string> NextActions { get; set; } = NextActions;
    public CompanyStatutoryProfileDto? Profile { get; set; } = Profile;

    public CompanyStatutoryProfileStatusDto() : this(default !, default !, default !, default !, default !, default !, string.Empty, [], [], default !)
    {
    }
}

public sealed record StatutoryAddressDto(string? AddressLine1, string? AddressLine2, string? PostalCode, string? City, string? CountryCode)
{
    public string? AddressLine1 { get; set; } = AddressLine1;
    public string? AddressLine2 { get; set; } = AddressLine2;
    public string? PostalCode { get; set; } = PostalCode;
    public string? City { get; set; } = City;
    public string? CountryCode { get; set; } = CountryCode;

    public StatutoryAddressDto() : this(default !, default !, default !, default !, default !)
    {
    }
}
