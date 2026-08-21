namespace VirtualCompany.Domain.Entities;

public sealed class BankStatementImport : ICompanyOwnedEntity
{
    private BankStatementImport() { }

    public BankStatementImport(Guid id, Guid companyId, Guid bankAccountId, string sourceKey, string statementIdentity,
        string contentHash, Guid importedByUserId, DateTime importedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        BankAccountId = bankAccountId == Guid.Empty ? throw new ArgumentException("BankAccountId is required.", nameof(bankAccountId)) : bankAccountId;
        SourceKey = Required(sourceKey, nameof(sourceKey), 64).ToLowerInvariant();
        StatementIdentity = Required(statementIdentity, nameof(statementIdentity), 128);
        ContentHash = Required(contentHash, nameof(contentHash), 64).ToLowerInvariant();
        ImportedByUserId = importedByUserId == Guid.Empty ? throw new ArgumentException("ImportedByUserId is required.", nameof(importedByUserId)) : importedByUserId;
        ImportedUtc = EntityTimestampNormalizer.NormalizeUtc(importedUtc, nameof(importedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BankAccountId { get; private set; }
    public string SourceKey { get; private set; } = null!;
    public string StatementIdentity { get; private set; } = null!;
    public string ContentHash { get; private set; } = null!;
    public Guid ImportedByUserId { get; private set; }
    public DateTime ImportedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public CompanyBankAccount BankAccount { get; private set; } = null!;
    public ICollection<BankStatementImportRow> Rows { get; } = new List<BankStatementImportRow>();

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var result = value.Trim();
        return result.Length <= maxLength ? result : throw new ArgumentOutOfRangeException(name);
    }
}

public sealed class BankStatementImportRow : ICompanyOwnedEntity
{
    private BankStatementImportRow() { }

    public BankStatementImportRow(Guid id, Guid companyId, Guid bankStatementImportId, Guid bankTransactionId,
        string rowIdentity, string rowContentHash, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        BankStatementImportId = bankStatementImportId == Guid.Empty ? throw new ArgumentException("BankStatementImportId is required.", nameof(bankStatementImportId)) : bankStatementImportId;
        BankTransactionId = bankTransactionId == Guid.Empty ? throw new ArgumentException("BankTransactionId is required.", nameof(bankTransactionId)) : bankTransactionId;
        RowIdentity = string.IsNullOrWhiteSpace(rowIdentity) ? throw new ArgumentException("RowIdentity is required.", nameof(rowIdentity)) : rowIdentity.Trim();
        RowContentHash = string.IsNullOrWhiteSpace(rowContentHash) ? throw new ArgumentException("RowContentHash is required.", nameof(rowContentHash)) : rowContentHash.Trim().ToLowerInvariant();
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BankStatementImportId { get; private set; }
    public Guid BankTransactionId { get; private set; }
    public string RowIdentity { get; private set; } = null!;
    public string RowContentHash { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public BankStatementImport BankStatementImport { get; private set; } = null!;
    public BankTransaction BankTransaction { get; private set; } = null!;
}
