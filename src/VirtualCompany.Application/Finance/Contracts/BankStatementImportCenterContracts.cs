namespace VirtualCompany.Application.Finance;

public static class BankStatementImportReasonCodes
{
    public const string UnsupportedFormat = "statement_import_unsupported_format";
    public const string UnsupportedVersion = "statement_import_unsupported_version";
    public const string FileTooLarge = "statement_import_file_too_large";
    public const string MalformedFile = "statement_import_malformed_file";
    public const string MalwareBlocked = "statement_import_malware_blocked";
    public const string ScanUnavailable = "statement_import_scan_unavailable";
    public const string DuplicateFile = "statement_import_duplicate_file";
    public const string AccountMismatch = "statement_import_account_mismatch";
    public const string CurrencyMismatch = "statement_import_currency_mismatch";
    public const string ControlTotalMismatch = "statement_import_control_total_mismatch";
    public const string MissingMappingProfile = "statement_import_mapping_profile_required";
    public const string InvalidMapping = "statement_import_mapping_invalid";
    public const string RowInvalid = "statement_import_row_invalid";
    public const string VersionConflict = "statement_import_version_conflict";
}

public sealed record PreviewBankStatementImportCommand(Guid CompanyId, Guid BankAccountId,
    string OriginalFileName, string? ContentType, long ContentLength, Stream Content,
    Guid? CsvMappingProfileId, int? CsvMappingProfileVersion, Guid ActorUserId, string? CorrelationId = null);

public sealed record CommitBankStatementImportCommand(Guid CompanyId, Guid JobId, long ExpectedVersion,
    Guid ActorUserId, string? CorrelationId = null);

public sealed record DecideBankStatementImportConflictCommand(Guid CompanyId, Guid JobId, Guid RowId,
    long ExpectedVersion, string Decision, string Reason, Guid ActorUserId, string? CorrelationId = null);

public sealed record CreateBankStatementCsvMappingProfileCommand(Guid CompanyId, string Name, char Delimiter,
    string CultureName, string DateFormat, bool HasHeader, string BookingDateColumn, string? ValueDateColumn,
    string? AmountColumn, string? DebitColumn, string? CreditColumn, string? CurrencyColumn,
    string ReferenceColumn, string? CounterpartyColumn, string? ExternalReferenceColumn,
    string? AccountIdentifierColumn, string? DefaultCurrency, Guid ActorUserId, string? CorrelationId = null);

public sealed record CreateBankStatementCsvMappingProfileVersionCommand(Guid CompanyId, Guid ProfileId,
    int ExpectedCurrentVersion, char Delimiter, string CultureName, string DateFormat, bool HasHeader,
    string BookingDateColumn, string? ValueDateColumn, string? AmountColumn, string? DebitColumn,
    string? CreditColumn, string? CurrencyColumn, string ReferenceColumn, string? CounterpartyColumn,
    string? ExternalReferenceColumn, string? AccountIdentifierColumn, string? DefaultCurrency,
    Guid ActorUserId, string? CorrelationId = null);

public sealed record BankStatementImportAccountDto(Guid Id, string DisplayName, string BankName,
    string MaskedAccountNumber, string Currency);

public sealed record BankStatementCsvMappingProfileDto(Guid Id, string Name, int Version, char Delimiter,
    string CultureName, string DateFormat, bool HasHeader, string BookingDateColumn, string? ValueDateColumn,
    string? AmountColumn, string? DebitColumn, string? CreditColumn, string? CurrencyColumn,
    string ReferenceColumn, string? CounterpartyColumn, string? ExternalReferenceColumn,
    string? AccountIdentifierColumn, string? DefaultCurrency, DateTime CreatedUtc);

public sealed record BankStatementImportIssueDto(string Code, string Severity, string Message, int? RowNumber = null);

public sealed record BankStatementImportRowDto(Guid Id, int RowNumber, string RowIdentity, string Outcome,
    DateTime? BookingDateUtc, DateTime? ValueDateUtc, decimal? Amount, string? Currency,
    string? ReferenceText, string? Counterparty, string? ExternalReference, string? IssueCode,
    string? IssueSeverity, string? IssueMessage, string? PaymentStatus, string? ConflictDecision,
    Guid? ImportedBankTransactionId);

public sealed record BankStatementImportJobDto(Guid Id, Guid BankAccountId, string BankAccountName,
    string OriginalFileName, long ContentLength, string Checksum, string Status, string? Format,
    string? MessageVersion, string? ParserVersion, string? StatementIdentity, string? SourceAccountIdentifier,
    string? Currency, decimal? OpeningBalance, decimal? ClosingBalance, decimal DebitTotal,
    decimal CreditTotal, decimal? CalculatedClosingBalance, int TotalRowCount, int AcceptedRowCount,
    int DuplicateRowCount, int ErrorRowCount, int ImportedRowCount, int LastCommittedRowNumber,
    string? FailureCode, string? FailureSummary, long Version, DateTime CreatedUtc, DateTime UpdatedUtc,
    DateTime? CompletedUtc, IReadOnlyList<BankStatementImportIssueDto> Issues,
    IReadOnlyList<BankStatementImportRowDto> Rows);

public sealed record BankStatementImportWorkspaceDto(IReadOnlyList<BankStatementImportAccountDto> Accounts,
    IReadOnlyList<BankStatementCsvMappingProfileDto> CsvProfiles,
    IReadOnlyList<BankStatementImportJobDto> Jobs);

public interface IBankStatementImportCenterService
{
    Task<BankStatementImportWorkspaceDto> GetWorkspaceAsync(Guid companyId, CancellationToken cancellationToken);
    Task<BankStatementImportJobDto?> GetJobAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken);
    Task<BankStatementImportJobDto> PreviewAsync(PreviewBankStatementImportCommand command, CancellationToken cancellationToken);
    Task<BankStatementImportJobDto> CommitAsync(CommitBankStatementImportCommand command, CancellationToken cancellationToken);
    Task<BankStatementImportJobDto> DecideConflictAsync(DecideBankStatementImportConflictCommand command, CancellationToken cancellationToken);
    Task<BankStatementCsvMappingProfileDto> CreateCsvProfileAsync(CreateBankStatementCsvMappingProfileCommand command,
        CancellationToken cancellationToken);
    Task<BankStatementCsvMappingProfileDto> CreateCsvProfileVersionAsync(
        CreateBankStatementCsvMappingProfileVersionCommand command, CancellationToken cancellationToken);
}

public sealed record BankStatementParseRequest(string FileName, string? ContentType,
    BankStatementCsvMappingProfileDto? CsvProfile, string AccountCurrency, string MaskedAccountNumber,
    string? ExternalAccountCode);

public sealed record ParsedBankStatementRow(int RowNumber, string RowIdentity, DateTime? BookingDateUtc,
    DateTime? ValueDateUtc, decimal? Amount, string? Currency, string? ReferenceText,
    string? Counterparty, string? ExternalReference, string? PaymentStatus,
    IReadOnlyList<BankStatementImportIssueDto> Issues);

public sealed record ParsedBankStatement(string Format, string MessageVersion, string ParserVersion,
    string StatementIdentity, string? SourceAccountIdentifier, string? Currency, decimal? OpeningBalance,
    decimal? ClosingBalance, IReadOnlyList<ParsedBankStatementRow> Rows,
    IReadOnlyList<BankStatementImportIssueDto> FileIssues, bool IsPaymentStatusMessage);

public interface IBankStatementFileParser
{
    bool Supports(string fileName, string? contentType);
    Task<ParsedBankStatement> ParseAsync(BankStatementParseRequest request, Stream content,
        CancellationToken cancellationToken);
}

public sealed class BankStatementImportOperationException : Exception
{
    public BankStatementImportOperationException(string reasonCode, string safeMessage, bool isConflict = false,
        Exception? innerException = null) : base(safeMessage, innerException)
    { ReasonCode = reasonCode; SafeMessage = safeMessage; IsConflict = isConflict; }
    public string ReasonCode { get; }
    public string SafeMessage { get; }
    public bool IsConflict { get; }
}
