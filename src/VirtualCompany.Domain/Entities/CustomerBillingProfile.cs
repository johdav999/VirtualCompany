using System.Globalization;
using System.Net.Mail;
using System.Text;

namespace VirtualCompany.Domain.Entities;

public static class CustomerBillingPartyKinds
{
    public const string Organization = "organization";
    public const string Person = "person";

    public static string Normalize(string value) => NormalizeChoice(value, nameof(value), Organization, Person);

    private static string NormalizeChoice(string value, string name, params string[] allowed)
    {
        var normalized = CustomerBillingNormalization.Required(value, name, 32).ToLowerInvariant();
        return allowed.Contains(normalized, StringComparer.Ordinal) ? normalized :
            throw new ArgumentOutOfRangeException(name, $"Unsupported value '{value}'.");
    }
}

public static class CustomerBillingSourceKinds
{
    public const string User = "user";
    public const string Provider = "provider";
    public const string Migration = "migration";
    public const string ApprovedMerge = "approved_merge";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { User, Provider, Migration, ApprovedMerge };
}

public static class CustomerBillingValidationStates
{
    public const string FormatValid = "format_valid";
    public const string UserAttested = "user_attested";
    public const string ProviderSourced = "provider_sourced";
    public const string ExternallyVerified = "externally_verified";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { FormatValid, UserAttested, ProviderSourced, ExternallyVerified };
}

public static class CustomerBillingCreditStatuses
{
    public const string Active = "active";
    public const string OnHold = "on_hold";
    public const string Blocked = "blocked";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Active, OnHold, Blocked };
}

public static class CustomerBillingDeliveryChannels
{
    public const string Email = "email";
    public const string EInvoice = "e_invoice";
    public const string Postal = "postal";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { Email, EInvoice, Postal };
}

public static class CustomerBillingPaymentTermKinds
{
    public const string FixedDays = "fixed_days";
    public const string DueOnReceipt = "due_on_receipt";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
        { FixedDays, DueOnReceipt };
}

public sealed record CustomerBillingProfileValues(
    string LegalName,
    string? DisplayName,
    string PartyKind,
    string? TaxIdentifier,
    string? VatIdentifier,
    string IdentityValidationState,
    string BillingAddressLine1,
    string? BillingAddressLine2,
    string BillingPostalCode,
    string BillingCity,
    string? BillingRegion,
    string BillingCountryCode,
    string? DeliveryAddressLine1,
    string? DeliveryAddressLine2,
    string? DeliveryPostalCode,
    string? DeliveryCity,
    string? DeliveryRegion,
    string? DeliveryCountryCode,
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

public sealed class CustomerBillingProfile : ICompanyOwnedEntity
{
    private CustomerBillingProfile() { }

    public CustomerBillingProfile(Guid id, Guid companyId, Guid counterpartyId, CustomerBillingProfileValues values,
        Guid actorUserId, DateTime nowUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        CounterpartyId = counterpartyId == Guid.Empty ? throw new ArgumentException("CounterpartyId is required.", nameof(counterpartyId)) : counterpartyId;
        CreatedByUserId = actorUserId == Guid.Empty ? throw new ArgumentException("Actor user id is required.", nameof(actorUserId)) : actorUserId;
        CreatedUtc = CustomerBillingNormalization.Utc(nowUtc, nameof(nowUtc));
        Version = 1;
        Apply(values, actorUserId, CreatedUtc, incrementVersion: false);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CounterpartyId { get; private set; }
    public string LegalName { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string PartyKind { get; private set; } = null!;
    public string? TaxIdentifier { get; private set; }
    public string? NormalizedTaxIdentifier { get; private set; }
    public string? VatIdentifier { get; private set; }
    public string? NormalizedVatIdentifier { get; private set; }
    public string IdentityValidationState { get; private set; } = null!;
    public string BillingAddressLine1 { get; private set; } = null!;
    public string? BillingAddressLine2 { get; private set; }
    public string BillingPostalCode { get; private set; } = null!;
    public string BillingCity { get; private set; } = null!;
    public string? BillingRegion { get; private set; }
    public string BillingCountryCode { get; private set; } = null!;
    public string? DeliveryAddressLine1 { get; private set; }
    public string? DeliveryAddressLine2 { get; private set; }
    public string? DeliveryPostalCode { get; private set; }
    public string? DeliveryCity { get; private set; }
    public string? DeliveryRegion { get; private set; }
    public string? DeliveryCountryCode { get; private set; }
    public string LanguageCode { get; private set; } = null!;
    public string CurrencyCode { get; private set; } = null!;
    public string PaymentTermKind { get; private set; } = null!;
    public int PaymentTermDays { get; private set; }
    public string PaymentMethod { get; private set; } = null!;
    public string InvoiceDeliveryChannel { get; private set; } = null!;
    public string? InvoiceDeliveryEmail { get; private set; }
    public string? NormalizedInvoiceDeliveryEmail { get; private set; }
    public string? BuyerReference { get; private set; }
    public string? EInvoiceIdentifier { get; private set; }
    public string? NormalizedEInvoiceIdentifier { get; private set; }
    public string? EInvoiceIdentifierType { get; private set; }
    public decimal CreditLimit { get; private set; }
    public string CreditStatus { get; private set; } = null!;
    public string? DefaultAccountMapping { get; private set; }
    public string? DefaultDimensionCode { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public string SourceKind { get; private set; } = null!;
    public string? SourceReference { get; private set; }
    public DateTime? UserAttestedUtc { get; private set; }
    public DateTime? ExternallyVerifiedUtc { get; private set; }
    public string? VerificationSource { get; private set; }
    public string ConflictState { get; private set; } = "clear";
    public Guid? MergedIntoCounterpartyId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public FinanceCounterparty Counterparty { get; private set; } = null!;

    public void Update(CustomerBillingProfileValues values, Guid actorUserId, DateTime nowUtc) =>
        Apply(values, actorUserId, CustomerBillingNormalization.Utc(nowUtc, nameof(nowUtc)), incrementVersion: true);

    public void MarkConflict(Guid actorUserId, DateTime nowUtc)
    {
        ConflictState = "needs_review";
        Touch(actorUserId, nowUtc);
    }

    public void ClearConflict(Guid actorUserId, DateTime nowUtc)
    {
        ConflictState = "clear";
        Touch(actorUserId, nowUtc);
    }

    public void MarkMerged(Guid targetCounterpartyId, Guid actorUserId, DateTime nowUtc)
    {
        if (targetCounterpartyId == Guid.Empty || targetCounterpartyId == CounterpartyId)
            throw new ArgumentException("A different merge target is required.", nameof(targetCounterpartyId));
        MergedIntoCounterpartyId = targetCounterpartyId;
        CreditStatus = CustomerBillingCreditStatuses.Blocked;
        ConflictState = "clear";
        Touch(actorUserId, nowUtc);
    }

    public CustomerBillingProfileValues ToValues() => new(LegalName, DisplayName, PartyKind, TaxIdentifier,
        VatIdentifier, IdentityValidationState, BillingAddressLine1, BillingAddressLine2, BillingPostalCode,
        BillingCity, BillingRegion, BillingCountryCode, DeliveryAddressLine1, DeliveryAddressLine2,
        DeliveryPostalCode, DeliveryCity, DeliveryRegion, DeliveryCountryCode, LanguageCode, CurrencyCode,
        PaymentTermKind, PaymentTermDays, PaymentMethod, InvoiceDeliveryChannel, InvoiceDeliveryEmail,
        BuyerReference, EInvoiceIdentifier, EInvoiceIdentifierType, CreditLimit, CreditStatus,
        DefaultAccountMapping, DefaultDimensionCode, EffectiveFrom, EffectiveTo, SourceKind, SourceReference,
        UserAttestedUtc, ExternallyVerifiedUtc, VerificationSource);

    public string NormalizedLegalName => CustomerBillingNormalization.Search(LegalName);
    public string NormalizedBillingAddress => CustomerBillingNormalization.Search(
        $"{BillingAddressLine1}|{BillingAddressLine2}|{BillingPostalCode}|{BillingCity}|{BillingCountryCode}");

    private void Apply(CustomerBillingProfileValues values, Guid actorUserId, DateTime nowUtc, bool incrementVersion)
    {
        if (actorUserId == Guid.Empty) throw new ArgumentException("Actor user id is required.", nameof(actorUserId));
        LegalName = CustomerBillingNormalization.Required(values.LegalName, nameof(values.LegalName), 200);
        DisplayName = CustomerBillingNormalization.Optional(values.DisplayName, nameof(values.DisplayName), 200) ?? LegalName;
        PartyKind = CustomerBillingPartyKinds.Normalize(values.PartyKind);
        TaxIdentifier = CustomerBillingNormalization.Identifier(values.TaxIdentifier, nameof(values.TaxIdentifier));
        NormalizedTaxIdentifier = CustomerBillingNormalization.IdentityKey(TaxIdentifier);
        VatIdentifier = CustomerBillingNormalization.Identifier(values.VatIdentifier, nameof(values.VatIdentifier));
        NormalizedVatIdentifier = CustomerBillingNormalization.IdentityKey(VatIdentifier);
        IdentityValidationState = CustomerBillingNormalization.Choice(values.IdentityValidationState, nameof(values.IdentityValidationState), CustomerBillingValidationStates.All);
        BillingAddressLine1 = CustomerBillingNormalization.Required(values.BillingAddressLine1, nameof(values.BillingAddressLine1), 200);
        BillingAddressLine2 = CustomerBillingNormalization.Optional(values.BillingAddressLine2, nameof(values.BillingAddressLine2), 200);
        BillingPostalCode = CustomerBillingNormalization.Required(values.BillingPostalCode, nameof(values.BillingPostalCode), 32).ToUpperInvariant();
        BillingCity = CustomerBillingNormalization.Required(values.BillingCity, nameof(values.BillingCity), 100);
        BillingRegion = CustomerBillingNormalization.Optional(values.BillingRegion, nameof(values.BillingRegion), 100);
        BillingCountryCode = CustomerBillingNormalization.Country(values.BillingCountryCode, nameof(values.BillingCountryCode));
        DeliveryAddressLine1 = CustomerBillingNormalization.Optional(values.DeliveryAddressLine1, nameof(values.DeliveryAddressLine1), 200);
        DeliveryAddressLine2 = CustomerBillingNormalization.Optional(values.DeliveryAddressLine2, nameof(values.DeliveryAddressLine2), 200);
        DeliveryPostalCode = CustomerBillingNormalization.Optional(values.DeliveryPostalCode, nameof(values.DeliveryPostalCode), 32)?.ToUpperInvariant();
        DeliveryCity = CustomerBillingNormalization.Optional(values.DeliveryCity, nameof(values.DeliveryCity), 100);
        DeliveryRegion = CustomerBillingNormalization.Optional(values.DeliveryRegion, nameof(values.DeliveryRegion), 100);
        DeliveryCountryCode = string.IsNullOrWhiteSpace(values.DeliveryCountryCode) ? null : CustomerBillingNormalization.Country(values.DeliveryCountryCode, nameof(values.DeliveryCountryCode));
        ValidateDeliveryAddress();
        LanguageCode = CustomerBillingNormalization.Language(values.LanguageCode);
        CurrencyCode = CustomerBillingNormalization.Currency(values.CurrencyCode);
        PaymentTermKind = CustomerBillingNormalization.Choice(values.PaymentTermKind, nameof(values.PaymentTermKind), CustomerBillingPaymentTermKinds.All);
        PaymentTermDays = values.PaymentTermKind == CustomerBillingPaymentTermKinds.DueOnReceipt ? 0 :
            values.PaymentTermDays is >= 0 and <= 365 ? values.PaymentTermDays : throw new ArgumentOutOfRangeException(nameof(values.PaymentTermDays));
        PaymentMethod = CustomerBillingNormalization.Required(values.PaymentMethod, nameof(values.PaymentMethod), 64).ToLowerInvariant();
        InvoiceDeliveryChannel = CustomerBillingNormalization.Choice(values.InvoiceDeliveryChannel, nameof(values.InvoiceDeliveryChannel), CustomerBillingDeliveryChannels.All);
        InvoiceDeliveryEmail = CustomerBillingNormalization.Email(values.InvoiceDeliveryEmail, nameof(values.InvoiceDeliveryEmail));
        NormalizedInvoiceDeliveryEmail = InvoiceDeliveryEmail?.ToLowerInvariant();
        BuyerReference = CustomerBillingNormalization.Optional(values.BuyerReference, nameof(values.BuyerReference), 100);
        EInvoiceIdentifier = CustomerBillingNormalization.Optional(values.EInvoiceIdentifier, nameof(values.EInvoiceIdentifier), 128);
        NormalizedEInvoiceIdentifier = CustomerBillingNormalization.IdentityKey(EInvoiceIdentifier);
        EInvoiceIdentifierType = CustomerBillingNormalization.Optional(values.EInvoiceIdentifierType, nameof(values.EInvoiceIdentifierType), 64)?.ToLowerInvariant();
        ValidateDeliveryChannel();
        CreditLimit = values.CreditLimit >= 0 ? decimal.Round(values.CreditLimit, 2, MidpointRounding.AwayFromZero) : throw new ArgumentOutOfRangeException(nameof(values.CreditLimit));
        CreditStatus = CustomerBillingNormalization.Choice(values.CreditStatus, nameof(values.CreditStatus), CustomerBillingCreditStatuses.All);
        DefaultAccountMapping = CustomerBillingNormalization.Optional(values.DefaultAccountMapping, nameof(values.DefaultAccountMapping), 64);
        DefaultDimensionCode = CustomerBillingNormalization.Optional(values.DefaultDimensionCode, nameof(values.DefaultDimensionCode), 64);
        EffectiveFrom = values.EffectiveFrom;
        EffectiveTo = values.EffectiveTo;
        if (EffectiveTo.HasValue && EffectiveTo.Value < EffectiveFrom) throw new ArgumentOutOfRangeException(nameof(values.EffectiveTo));
        SourceKind = CustomerBillingNormalization.Choice(values.SourceKind, nameof(values.SourceKind), CustomerBillingSourceKinds.All);
        SourceReference = CustomerBillingNormalization.Optional(values.SourceReference, nameof(values.SourceReference), 200);
        UserAttestedUtc = values.UserAttestedUtc.HasValue ? CustomerBillingNormalization.Utc(values.UserAttestedUtc.Value, nameof(values.UserAttestedUtc)) : null;
        ExternallyVerifiedUtc = values.ExternallyVerifiedUtc.HasValue ? CustomerBillingNormalization.Utc(values.ExternallyVerifiedUtc.Value, nameof(values.ExternallyVerifiedUtc)) : null;
        VerificationSource = CustomerBillingNormalization.Optional(values.VerificationSource, nameof(values.VerificationSource), 200);

        if (IdentityValidationState == CustomerBillingValidationStates.UserAttested && UserAttestedUtc is null)
        {
            throw new ArgumentException("User-attested identity facts require an attestation timestamp.", nameof(values));
        }

        if (IdentityValidationState == CustomerBillingValidationStates.ExternallyVerified &&
            (ExternallyVerifiedUtc is null || string.IsNullOrWhiteSpace(VerificationSource)))
        {
            throw new ArgumentException("Externally verified identity facts require a verification timestamp and source.", nameof(values));
        }

        if (IdentityValidationState == CustomerBillingValidationStates.ProviderSourced &&
            (SourceKind != CustomerBillingSourceKinds.Provider || string.IsNullOrWhiteSpace(SourceReference)))
        {
            throw new ArgumentException("Provider-sourced identity facts require provider provenance.", nameof(values));
        }

        UpdatedByUserId = actorUserId;
        UpdatedUtc = nowUtc;
        if (incrementVersion) Version++;
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedByUserId = actorUserId == Guid.Empty ? throw new ArgumentException("Actor user id is required.", nameof(actorUserId)) : actorUserId;
        UpdatedUtc = CustomerBillingNormalization.Utc(nowUtc, nameof(nowUtc));
        Version++;
    }

    private void ValidateDeliveryAddress()
    {
        var any = DeliveryAddressLine1 is not null || DeliveryPostalCode is not null || DeliveryCity is not null || DeliveryCountryCode is not null;
        var complete = DeliveryAddressLine1 is not null && DeliveryPostalCode is not null && DeliveryCity is not null && DeliveryCountryCode is not null;
        if (any && !complete) throw new ArgumentException("Delivery address requires line 1, postal code, city, and country code.");
    }

    private void ValidateDeliveryChannel()
    {
        if (InvoiceDeliveryChannel == CustomerBillingDeliveryChannels.Email && InvoiceDeliveryEmail is null)
            throw new ArgumentException("Invoice delivery email is required for email delivery.");
        if (InvoiceDeliveryChannel == CustomerBillingDeliveryChannels.EInvoice && (EInvoiceIdentifier is null || EInvoiceIdentifierType is null))
            throw new ArgumentException("E-invoice identifier and type are required for e-invoice delivery.");
    }
}

internal static class CustomerBillingNormalization
{
    public static string Required(string? value, string name, int maxLength) =>
        Optional(value, name, maxLength) ?? throw new ArgumentException($"{name} is required.", name);

    public static string? Optional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().Normalize(NormalizationForm.FormKC);
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(name);
    }

    public static string Choice(string value, string name, IReadOnlySet<string> allowed)
    {
        var normalized = Required(value, name, 64).ToLowerInvariant();
        return allowed.Contains(normalized) ? normalized : throw new ArgumentOutOfRangeException(name, $"Unsupported value '{value}'.");
    }

    public static string Country(string value, string name)
    {
        var normalized = Required(value, name, 2).ToUpperInvariant();
        return normalized.Length == 2 && normalized.All(char.IsLetter) ? normalized : throw new ArgumentOutOfRangeException(name);
    }

    public static string Currency(string value)
    {
        var normalized = Required(value, nameof(value), 3).ToUpperInvariant();
        return normalized.Length == 3 && normalized.All(char.IsLetter) ? normalized : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public static string Language(string value)
    {
        var normalized = Required(value, nameof(value), 16);
        if (!normalized.All(ch => char.IsLetter(ch) || ch == '-')) throw new ArgumentOutOfRangeException(nameof(value));
        return CultureInfo.GetCultureInfo(normalized).Name;
    }

    public static string? Email(string? value, string name)
    {
        var normalized = Optional(value, name, 256);
        if (normalized is null) return null;
        try { return new MailAddress(normalized).Address; }
        catch (FormatException) { throw new ArgumentException("Email address format is invalid.", name); }
    }

    public static string? Identifier(string? value, string name)
    {
        var normalized = Optional(value, name, 64);
        if (normalized is null) return null;
        if (!normalized.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or ' ' or '.' or '/'))
            throw new ArgumentException("Identifier contains unsupported characters.", name);
        return normalized;
    }

    public static string? IdentityKey(string? value) => string.IsNullOrWhiteSpace(value) ? null :
        new(value.Normalize(NormalizationForm.FormKC).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static string Search(string value) =>
        new(value.Normalize(NormalizationForm.FormKD).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static DateTime Utc(DateTime value, string name) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
