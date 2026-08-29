namespace VirtualCompany.Domain.Entities;

public sealed class BankStatementCsvMappingProfile : ICompanyOwnedEntity
{
    private BankStatementCsvMappingProfile() { }
    public BankStatementCsvMappingProfile(Guid id, Guid companyId, string name, Guid createdByUserId, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        Name = Required(name, nameof(name), 120);
        CreatedByUserId = createdByUserId == Guid.Empty ? throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId)) : createdByUserId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        CurrentVersion = 1;
        IsActive = true;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public int CurrentVersion { get; private set; }
    public bool IsActive { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<BankStatementCsvMappingProfileVersion> Versions { get; } = new List<BankStatementCsvMappingProfileVersion>();
    public void AdvanceVersion(DateTime nowUtc) { CurrentVersion++; UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(nowUtc, nameof(nowUtc)); }
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value) ?
        throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
}

public sealed class BankStatementCsvMappingProfileVersion : ICompanyOwnedEntity
{
    private BankStatementCsvMappingProfileVersion() { }
    public BankStatementCsvMappingProfileVersion(Guid id, Guid companyId, Guid profileId, int version, char delimiter,
        string cultureName, string dateFormat, bool hasHeader, string bookingDateColumn, string? valueDateColumn,
        string? amountColumn, string? debitColumn, string? creditColumn, string? currencyColumn,
        string referenceColumn, string? counterpartyColumn, string? externalReferenceColumn,
        string? accountIdentifierColumn, string? defaultCurrency, Guid createdByUserId, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        ProfileId = profileId == Guid.Empty ? throw new ArgumentException("ProfileId is required.", nameof(profileId)) : profileId;
        Version = version > 0 ? version : throw new ArgumentOutOfRangeException(nameof(version));
        Delimiter = delimiter is ',' or ';' or '\t' ? delimiter : throw new ArgumentOutOfRangeException(nameof(delimiter));
        CultureName = Required(cultureName, nameof(cultureName), 32);
        DateFormat = Required(dateFormat, nameof(dateFormat), 64);
        HasHeader = hasHeader;
        BookingDateColumn = Required(bookingDateColumn, nameof(bookingDateColumn), 64);
        ValueDateColumn = Optional(valueDateColumn, 64);
        AmountColumn = Optional(amountColumn, 64);
        DebitColumn = Optional(debitColumn, 64);
        CreditColumn = Optional(creditColumn, 64);
        if (AmountColumn is null && (DebitColumn is null || CreditColumn is null)) throw new ArgumentException("Map either an amount column or both debit and credit columns.");
        CurrencyColumn = Optional(currencyColumn, 64);
        ReferenceColumn = Required(referenceColumn, nameof(referenceColumn), 64);
        CounterpartyColumn = Optional(counterpartyColumn, 64);
        ExternalReferenceColumn = Optional(externalReferenceColumn, 64);
        AccountIdentifierColumn = Optional(accountIdentifierColumn, 64);
        DefaultCurrency = Optional(defaultCurrency, 3)?.ToUpperInvariant();
        CreatedByUserId = createdByUserId == Guid.Empty ? throw new ArgumentException("CreatedByUserId is required.", nameof(createdByUserId)) : createdByUserId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ProfileId { get; private set; }
    public int Version { get; private set; }
    public char Delimiter { get; private set; }
    public string CultureName { get; private set; } = null!;
    public string DateFormat { get; private set; } = null!;
    public bool HasHeader { get; private set; }
    public string BookingDateColumn { get; private set; } = null!;
    public string? ValueDateColumn { get; private set; }
    public string? AmountColumn { get; private set; }
    public string? DebitColumn { get; private set; }
    public string? CreditColumn { get; private set; }
    public string? CurrencyColumn { get; private set; }
    public string ReferenceColumn { get; private set; } = null!;
    public string? CounterpartyColumn { get; private set; }
    public string? ExternalReferenceColumn { get; private set; }
    public string? AccountIdentifierColumn { get; private set; }
    public string? DefaultCurrency { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public BankStatementCsvMappingProfile Profile { get; private set; } = null!;
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value) ?
        throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null :
        value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value));
}
