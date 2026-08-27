using System.Globalization;

namespace VirtualCompany.Domain.Entities;

public static class StatutoryVatRegistrationStatusValues
{
    public const string NotRegistered = "not_registered";
    public const string Pending = "pending";
    public const string Registered = "registered";

    public static string Normalize(string value) => NormalizeToken(value, nameof(value)) switch
    {
        NotRegistered => NotRegistered,
        Pending => Pending,
        Registered => Registered,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "VAT registration status is not supported.")
    };

    private static string NormalizeToken(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("VAT registration status is required.", name)
            : value.Trim().Replace('-', '_').ToLowerInvariant();
}

public static class StatutoryFiscalYearBasisValues
{
    public const string CalendarYear = "calendar_year";
    public const string NonCalendarYear = "non_calendar_year";

    public static string Normalize(string value) => NormalizeToken(value, nameof(value)) switch
    {
        CalendarYear => CalendarYear,
        NonCalendarYear => NonCalendarYear,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Fiscal-year basis is not supported.")
    };

    private static string NormalizeToken(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Fiscal-year basis is required.", name)
            : value.Trim().Replace('-', '_').ToLowerInvariant();
}

public static class StatutoryBookkeepingMethodValues
{
    public const string NotSpecified = "not_specified";
    public const string Accrual = "accrual";
    public const string Cash = "cash";

    public static string Normalize(string value) => NormalizeToken(value, nameof(value)) switch
    {
        NotSpecified => NotSpecified,
        Accrual => Accrual,
        Cash => Cash,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Bookkeeping method is not supported.")
    };

    private static string NormalizeToken(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Bookkeeping method is required.", name)
            : value.Trim().Replace('-', '_').ToLowerInvariant();
}

public static class StatutoryVerificationStatusValues
{
    public const string Unverified = "unverified";
    public const string ExternallyVerified = "externally_verified";
    public const string VerificationFailed = "verification_failed";

    public static string Normalize(string value) => NormalizeToken(value, nameof(value)) switch
    {
        Unverified => Unverified,
        ExternallyVerified => ExternallyVerified,
        VerificationFailed => VerificationFailed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Verification status is not supported.")
    };

    private static string NormalizeToken(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Verification status is required.", name)
            : value.Trim().Replace('-', '_').ToLowerInvariant();
}

public static class StatutoryProfileSourceKindValues
{
    public const string UserEntry = "user_entry";
    public const string ImportedDocument = "imported_document";
    public const string ExternalRegistry = "external_registry";

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Statutory profile source kind is required.", nameof(value))
            : value.Trim().Replace('-', '_').ToLowerInvariant() switch
            {
                UserEntry => UserEntry,
                ImportedDocument => ImportedDocument,
                ExternalRegistry => ExternalRegistry,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Statutory profile source kind is not supported.")
            };
}

public sealed record CompanyStatutoryProfileValues(
    string? LegalName,
    string? SwedishOrganisationNumber,
    string? VatRegistrationNumber,
    string VatRegistrationStatus,
    string? RegisteredAddressLine1,
    string? RegisteredAddressLine2,
    string? RegisteredPostalCode,
    string? RegisteredCity,
    string? RegisteredCountryCode,
    string? CorrespondenceAddressLine1,
    string? CorrespondenceAddressLine2,
    string? CorrespondencePostalCode,
    string? CorrespondenceCity,
    string? CorrespondenceCountryCode,
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

public sealed class CompanyStatutoryProfile : ICompanyOwnedEntity
{
    private CompanyStatutoryProfile()
    {
    }

    public CompanyStatutoryProfile(
        Guid id,
        Guid companyId,
        CompanyStatutoryProfileValues values,
        Guid actorUserId,
        DateTime createdUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CreatedByUserId = actorUserId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        Version = 1;
        Apply(values, actorUserId, CreatedUtc, incrementVersion: false);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string? LegalName { get; private set; }
    public string? SwedishOrganisationNumber { get; private set; }
    public string? VatRegistrationNumber { get; private set; }
    public string VatRegistrationStatus { get; private set; } = null!;
    public string? RegisteredAddressLine1 { get; private set; }
    public string? RegisteredAddressLine2 { get; private set; }
    public string? RegisteredPostalCode { get; private set; }
    public string? RegisteredCity { get; private set; }
    public string? RegisteredCountryCode { get; private set; }
    public string? CorrespondenceAddressLine1 { get; private set; }
    public string? CorrespondenceAddressLine2 { get; private set; }
    public string? CorrespondencePostalCode { get; private set; }
    public string? CorrespondenceCity { get; private set; }
    public string? CorrespondenceCountryCode { get; private set; }
    public string CountryCode { get; private set; } = null!;
    public string AccountingCurrency { get; private set; } = null!;
    public string FiscalYearBasis { get; private set; } = null!;
    public string BookkeepingMethod { get; private set; } = null!;
    public DateOnly? OrganisationRegistrationEffectiveFrom { get; private set; }
    public DateOnly? VatRegistrationEffectiveFrom { get; private set; }
    public DateOnly? VatRegistrationEffectiveTo { get; private set; }
    public bool IsUserAttested { get; private set; }
    public Guid? AttestedByUserId { get; private set; }
    public DateTime? AttestedUtc { get; private set; }
    public string VerificationStatus { get; private set; } = null!;
    public string SourceKind { get; private set; } = null!;
    public string? SourceReference { get; private set; }
    public DateTime SourceCapturedUtc { get; private set; }
    public string? ExternalVerifier { get; private set; }
    public DateTime? ExternallyVerifiedUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;

    public bool IsFormatComplete =>
        !string.IsNullOrWhiteSpace(LegalName) &&
        !string.IsNullOrWhiteSpace(SwedishOrganisationNumber) &&
        HasCompleteRegisteredAddress &&
        CountryCode == "SE" &&
        AccountingCurrency == "SEK" &&
        BookkeepingMethod != StatutoryBookkeepingMethodValues.NotSpecified &&
        OrganisationRegistrationEffectiveFrom is not null &&
        (VatRegistrationStatus != StatutoryVatRegistrationStatusValues.Registered ||
         (!string.IsNullOrWhiteSpace(VatRegistrationNumber) && VatRegistrationEffectiveFrom is not null));

    public bool HasCompleteRegisteredAddress =>
        !string.IsNullOrWhiteSpace(RegisteredAddressLine1) &&
        !string.IsNullOrWhiteSpace(RegisteredPostalCode) &&
        !string.IsNullOrWhiteSpace(RegisteredCity) &&
        !string.IsNullOrWhiteSpace(RegisteredCountryCode);

    public void Update(CompanyStatutoryProfileValues values, Guid actorUserId, DateTime updatedUtc) =>
        Apply(values, actorUserId, EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc)), incrementVersion: true);

    private void Apply(CompanyStatutoryProfileValues values, Guid actorUserId, DateTime updatedUtc, bool incrementVersion)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));

        var vatStatus = StatutoryVatRegistrationStatusValues.Normalize(values.VatRegistrationStatus);
        var verificationStatus = StatutoryVerificationStatusValues.Normalize(values.VerificationStatus);
        var sourceKind = StatutoryProfileSourceKindValues.Normalize(values.SourceKind);
        var organisationNumber = NormalizeOrganisationNumber(values.SwedishOrganisationNumber);
        var vatNumber = NormalizeVatNumber(values.VatRegistrationNumber);
        if (vatNumber is not null && organisationNumber is not null &&
            !string.Equals(vatNumber[2..12], organisationNumber, StringComparison.Ordinal))
        {
            throw new ArgumentException("The VAT registration number must contain the supplied organisation number.", nameof(values));
        }

        if (values.VatRegistrationEffectiveTo < values.VatRegistrationEffectiveFrom)
        {
            throw new ArgumentException("VAT registration end date cannot precede its effective date.", nameof(values));
        }

        if (verificationStatus == StatutoryVerificationStatusValues.ExternallyVerified &&
            (string.IsNullOrWhiteSpace(values.ExternalVerifier) || values.ExternallyVerifiedUtc is null ||
             string.IsNullOrWhiteSpace(values.SourceReference) || sourceKind != StatutoryProfileSourceKindValues.ExternalRegistry))
        {
            throw new ArgumentException("External verification requires an external-registry source, verifier, date, and source reference evidence.", nameof(values));
        }

        LegalName = NormalizeOptional(values.LegalName, 200);
        SwedishOrganisationNumber = organisationNumber;
        VatRegistrationNumber = vatNumber;
        VatRegistrationStatus = vatStatus;
        RegisteredAddressLine1 = NormalizeOptional(values.RegisteredAddressLine1, 200);
        RegisteredAddressLine2 = NormalizeOptional(values.RegisteredAddressLine2, 200);
        RegisteredPostalCode = NormalizePostalCode(values.RegisteredPostalCode);
        RegisteredCity = NormalizeOptional(values.RegisteredCity, 100);
        RegisteredCountryCode = NormalizeOptionalCountryCode(values.RegisteredCountryCode);
        CorrespondenceAddressLine1 = NormalizeOptional(values.CorrespondenceAddressLine1, 200);
        CorrespondenceAddressLine2 = NormalizeOptional(values.CorrespondenceAddressLine2, 200);
        CorrespondencePostalCode = NormalizePostalCode(values.CorrespondencePostalCode);
        CorrespondenceCity = NormalizeOptional(values.CorrespondenceCity, 100);
        CorrespondenceCountryCode = NormalizeOptionalCountryCode(values.CorrespondenceCountryCode);
        CountryCode = NormalizeCountryCode(values.CountryCode);
        AccountingCurrency = NormalizeCurrency(values.AccountingCurrency);
        FiscalYearBasis = StatutoryFiscalYearBasisValues.Normalize(values.FiscalYearBasis);
        BookkeepingMethod = StatutoryBookkeepingMethodValues.Normalize(values.BookkeepingMethod);
        OrganisationRegistrationEffectiveFrom = values.OrganisationRegistrationEffectiveFrom;
        VatRegistrationEffectiveFrom = values.VatRegistrationEffectiveFrom;
        VatRegistrationEffectiveTo = values.VatRegistrationEffectiveTo;
        IsUserAttested = values.IsUserAttested;
        AttestedByUserId = values.IsUserAttested ? actorUserId : null;
        AttestedUtc = values.IsUserAttested ? updatedUtc : null;
        VerificationStatus = verificationStatus;
        SourceKind = sourceKind;
        SourceReference = NormalizeOptional(values.SourceReference, 256);
        SourceCapturedUtc = EntityTimestampNormalizer.NormalizeUtc(values.SourceCapturedUtc, nameof(values.SourceCapturedUtc));
        ExternalVerifier = verificationStatus == StatutoryVerificationStatusValues.ExternallyVerified
            ? NormalizeOptional(values.ExternalVerifier, 200)
            : null;
        ExternallyVerifiedUtc = verificationStatus == StatutoryVerificationStatusValues.ExternallyVerified
            ? EntityTimestampNormalizer.NormalizeUtc(values.ExternallyVerifiedUtc!.Value, nameof(values.ExternallyVerifiedUtc))
            : null;
        UpdatedByUserId = actorUserId;
        UpdatedUtc = updatedUtc;
        if (incrementVersion) Version++;
    }

    private static string? NormalizeOrganisationNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(char.IsDigit).ToArray());
        if (normalized.Length != 10 || !HasValidLuhnChecksum(normalized))
            throw new ArgumentException("Swedish organisation number must contain 10 digits with a valid format checksum.", nameof(value));
        return normalized;
    }

    private static string? NormalizeVatNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(character => !char.IsWhiteSpace(character) && character != '-').ToArray()).ToUpperInvariant();
        if (normalized.Length != 14 || !normalized.StartsWith("SE", StringComparison.Ordinal) ||
            normalized[2..].Any(character => character is < '0' or > '9') || !normalized.EndsWith("01", StringComparison.Ordinal) ||
            !HasValidLuhnChecksum(normalized[2..12]))
            throw new ArgumentException("Swedish VAT registration number must use the format SE plus 12 digits ending in 01.", nameof(value));
        return normalized;
    }

    private static bool HasValidLuhnChecksum(string value)
    {
        var sum = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var digit = value[index] - '0';
            if (index % 2 == 0)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
        }
        return sum % 10 == 0;
    }

    private static string NormalizeCurrency(string value)
    {
        var normalized = NormalizeRequired(value, 3).ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException("Accounting currency must be a three-letter alphabetic code.", nameof(value));
        return normalized;
    }

    private static string NormalizeCountryCode(string value)
    {
        var normalized = NormalizeRequired(value, 2).ToUpperInvariant();
        if (normalized.Length != 2 || normalized.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException("Country code must be a two-letter alphabetic code.", nameof(value));
        return normalized;
    }

    private static string? NormalizeOptionalCountryCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeCountryCode(value);

    private static string? NormalizePostalCode(string? value)
    {
        var normalized = NormalizeOptional(value, 16);
        return normalized?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpper(CultureInfo.InvariantCulture);
    }

    private static string NormalizeRequired(string value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A required statutory profile value is missing.", nameof(value))
            : value.Trim().Length <= maxLength
                ? value.Trim()
                : throw new ArgumentOutOfRangeException(nameof(value), $"Statutory profile value must be {maxLength} characters or fewer.");

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), $"Statutory profile value must be {maxLength} characters or fewer.");
    }
}
