
namespace VirtualCompany.Application.Finance;

public sealed record BankStatementImportWorkspaceDto(IReadOnlyList<BankStatementImportAccountDto> Accounts,
    IReadOnlyList<BankStatementCsvMappingProfileDto> CsvProfiles,
    IReadOnlyList<BankStatementImportJobDto> Jobs);


public sealed record BankStatementCsvMappingProfileDto(Guid Id, string Name, int Version, char Delimiter,
    string CultureName, string DateFormat, bool HasHeader, string BookingDateColumn, string? ValueDateColumn,
    string? AmountColumn, string? DebitColumn, string? CreditColumn, string? CurrencyColumn,
    string ReferenceColumn, string? CounterpartyColumn, string? ExternalReferenceColumn,
    string? AccountIdentifierColumn, string? DefaultCurrency, DateTime CreatedUtc);


public sealed record BankStatementImportJobDto(Guid Id, Guid BankAccountId, string BankAccountName,
    string OriginalFileName, long ContentLength, string Checksum, string Status, string? Format,
    string? MessageVersion, string? ParserVersion, string? StatementIdentity, string? SourceAccountIdentifier,
    string? Currency, decimal? OpeningBalance, decimal? ClosingBalance, decimal DebitTotal,
    decimal CreditTotal, decimal? CalculatedClosingBalance, int TotalRowCount, int AcceptedRowCount,
    int DuplicateRowCount, int ErrorRowCount, int ImportedRowCount, int LastCommittedRowNumber,
    string? FailureCode, string? FailureSummary, long Version, DateTime CreatedUtc, DateTime UpdatedUtc,
    DateTime? CompletedUtc, IReadOnlyList<BankStatementImportIssueDto> Issues,
    IReadOnlyList<BankStatementImportRowDto> Rows);


public sealed record BankStatementImportIssueDto(string Code, string Severity, string Message, int? RowNumber = null);


public sealed record BankStatementImportRowDto(Guid Id, int RowNumber, string RowIdentity, string Outcome,
    DateTime? BookingDateUtc, DateTime? ValueDateUtc, decimal? Amount, string? Currency,
    string? ReferenceText, string? Counterparty, string? ExternalReference, string? IssueCode,
    string? IssueSeverity, string? IssueMessage, string? PaymentStatus, string? ConflictDecision,
    Guid? ImportedBankTransactionId);


public sealed record BankStatementImportAccountDto(Guid Id, string DisplayName, string BankName,
    string MaskedAccountNumber, string Currency);
