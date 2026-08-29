using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class BankStatementImportJob : ICompanyOwnedEntity
{
    private BankStatementImportJob() { }

    public BankStatementImportJob(Guid id, Guid companyId, Guid bankAccountId, string originalFileName,
        string? contentType, long contentLength, string storageKey, string checksum, Guid createdByUserId,
        DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = RequiredGuid(companyId, nameof(companyId));
        BankAccountId = RequiredGuid(bankAccountId, nameof(bankAccountId));
        OriginalFileName = Required(originalFileName, nameof(originalFileName), 255);
        ContentType = Optional(contentType, 128);
        ContentLength = contentLength is > 0 and <= 20 * 1024 * 1024
            ? contentLength : throw new ArgumentOutOfRangeException(nameof(contentLength));
        StorageKey = Required(storageKey, nameof(storageKey), 512);
        Checksum = Hash(checksum, nameof(checksum));
        Status = BankStatementImportJobStatuses.PendingScan;
        CreatedByUserId = RequiredGuid(createdByUserId, nameof(createdByUserId));
        CreatedUtc = Utc(createdUtc);
        UpdatedUtc = CreatedUtc;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BankAccountId { get; private set; }
    public Guid? CsvMappingProfileId { get; private set; }
    public int? CsvMappingProfileVersion { get; private set; }
    public string OriginalFileName { get; private set; } = null!;
    public string? ContentType { get; private set; }
    public long ContentLength { get; private set; }
    public string StorageKey { get; private set; } = null!;
    public string Checksum { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? Format { get; private set; }
    public string? MessageVersion { get; private set; }
    public string? ParserVersion { get; private set; }
    public string? StatementIdentity { get; private set; }
    public string? SourceAccountIdentifier { get; private set; }
    public string? Currency { get; private set; }
    public decimal? OpeningBalance { get; private set; }
    public decimal? ClosingBalance { get; private set; }
    public decimal DebitTotal { get; private set; }
    public decimal CreditTotal { get; private set; }
    public decimal? CalculatedClosingBalance { get; private set; }
    public int TotalRowCount { get; private set; }
    public int AcceptedRowCount { get; private set; }
    public int DuplicateRowCount { get; private set; }
    public int ErrorRowCount { get; private set; }
    public int ImportedRowCount { get; private set; }
    public int LastCommittedRowNumber { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public CompanyBankAccount BankAccount { get; private set; } = null!;
    public BankStatementCsvMappingProfile? CsvMappingProfile { get; private set; }
    public ICollection<BankStatementImportJobRow> Rows { get; } = new List<BankStatementImportJobRow>();
    public ICollection<BankStatementImportJobIssue> Issues { get; } = new List<BankStatementImportJobIssue>();

    public void CompletePreview(string format, string messageVersion, string parserVersion, string statementIdentity,
        string? sourceAccountIdentifier, string? currency, decimal? openingBalance, decimal? closingBalance,
        decimal debitTotal, decimal creditTotal, int totalRows, int acceptedRows, int duplicateRows, int errorRows,
        Guid? mappingProfileId, int? mappingProfileVersion, bool statusOnly, DateTime nowUtc)
    {
        Format = Required(format, nameof(format), 32);
        MessageVersion = Required(messageVersion, nameof(messageVersion), 64);
        ParserVersion = Required(parserVersion, nameof(parserVersion), 32);
        StatementIdentity = Required(statementIdentity, nameof(statementIdentity), 128);
        SourceAccountIdentifier = Optional(sourceAccountIdentifier, 128);
        Currency = Optional(currency, 3)?.ToUpperInvariant();
        OpeningBalance = openingBalance;
        ClosingBalance = closingBalance;
        DebitTotal = Money(debitTotal);
        CreditTotal = Money(creditTotal);
        CalculatedClosingBalance = openingBalance.HasValue
            ? Money(openingBalance.Value + CreditTotal - DebitTotal) : null;
        TotalRowCount = totalRows;
        AcceptedRowCount = acceptedRows;
        DuplicateRowCount = duplicateRows;
        ErrorRowCount = errorRows;
        CsvMappingProfileId = mappingProfileId;
        CsvMappingProfileVersion = mappingProfileVersion;
        Status = statusOnly ? BankStatementImportJobStatuses.StatusOnly :
            errorRows > 0 ? BankStatementImportJobStatuses.AttentionRequired :
            acceptedRows > 0 ? BankStatementImportJobStatuses.ReadyToImport :
            BankStatementImportJobStatuses.PreviewReady;
        Touch(nowUtc);
    }

    public void BeginCommit(long expectedVersion, DateTime nowUtc)
    {
        EnsureVersion(expectedVersion);
        if (Status is BankStatementImportJobStatuses.Completed or BankStatementImportJobStatuses.StatusOnly or BankStatementImportJobStatuses.Failed)
            throw new InvalidOperationException("This import job cannot be committed in its current state.");
        if (AcceptedRowCount == 0)
            throw new InvalidOperationException("This import job has no accepted rows to import.");
        Status = BankStatementImportJobStatuses.Importing;
        Touch(nowUtc);
    }

    public void RecordCommittedChunk(int lastRowNumber, int newlyImported, bool complete, Guid actorUserId, DateTime nowUtc)
    {
        LastCommittedRowNumber = Math.Max(LastCommittedRowNumber, lastRowNumber);
        ImportedRowCount += Math.Max(0, newlyImported);
        if (complete)
        {
            Status = BankStatementImportJobStatuses.Completed;
            CompletedByUserId = RequiredGuid(actorUserId, nameof(actorUserId));
            CompletedUtc = Utc(nowUtc);
        }
        else
        {
            Status = BankStatementImportJobStatuses.PartiallyImported;
        }
        Touch(nowUtc);
    }

    public void RecordCommitConflicts(int conflictCount, DateTime nowUtc)
    {
        ErrorRowCount += Math.Max(0, conflictCount);
        Status = BankStatementImportJobStatuses.AttentionRequired;
        Touch(nowUtc);
    }

    public void ResolveIssueRow(bool allCommitCandidatesProcessed, Guid actorUserId, DateTime nowUtc)
    {
        if (ErrorRowCount > 0) ErrorRowCount--;
        if (ErrorRowCount == 0)
        {
            if (allCommitCandidatesProcessed && ImportedRowCount > 0)
            {
                Status = BankStatementImportJobStatuses.Completed;
                CompletedByUserId = RequiredGuid(actorUserId, nameof(actorUserId));
                CompletedUtc = Utc(nowUtc);
            }
            else Status = BankStatementImportJobStatuses.ReadyToImport;
        }
        Touch(nowUtc);
    }

    public void Fail(string code, string summary, DateTime nowUtc)
    {
        FailureCode = Required(code, nameof(code), 64);
        FailureSummary = Required(summary, nameof(summary), 500);
        Status = BankStatementImportJobStatuses.Failed;
        Touch(nowUtc);
    }

    public void EnsureVersion(long expectedVersion)
    {
        if (expectedVersion != Version)
            throw new InvalidOperationException("The import job changed after it was loaded. Refresh and try again.");
    }

    private void Touch(DateTime nowUtc) { UpdatedUtc = Utc(nowUtc); Version++; }
    private static Guid RequiredGuid(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    private static DateTime Utc(DateTime value) => EntityTimestampNormalizer.NormalizeUtc(value, nameof(value));
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Hash(string value, string name)
    {
        var normalized = Required(value, name, 64).ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit) ? normalized : throw new ArgumentOutOfRangeException(name);
    }
    private static string Required(string value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var result = value.Trim();
        return result.Length <= max ? result : throw new ArgumentOutOfRangeException(name);
    }
    private static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null :
        value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value));
}
