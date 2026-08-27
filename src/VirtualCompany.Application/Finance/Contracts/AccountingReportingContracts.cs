using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Finance;

public sealed record GetGeneralLedgerQuery(
    Guid CompanyId,
    Guid FiscalPeriodId,
    Guid? FinanceAccountId = null,
    int Page = 1,
    int PageSize = 200);
public sealed record GetTrialBalanceQuery(Guid CompanyId, Guid FiscalPeriodId);
public sealed record GetAccountingTaxSummaryQuery(Guid CompanyId, Guid FiscalPeriodId);
public sealed record GetControlAccountReconciliationQuery(Guid CompanyId, Guid FiscalPeriodId);
public sealed record ReviewAccountingTaxSummaryCommand(Guid CompanyId, Guid FiscalPeriodId, Guid ActorUserId);
public sealed record RequestAccountingExportCommand(
    Guid CompanyId,
    Guid FiscalPeriodId,
    Guid ActorUserId,
    string IdempotencyKey,
    string ExportType = AccountingExportTypeValues.GenericJson,
    string? CorrelationId = null);
public sealed record GetAccountingExportQuery(Guid CompanyId, Guid ExportId);
public sealed record ListAccountingExportsQuery(
    Guid CompanyId,
    Guid? FiscalPeriodId = null,
    int Page = 1,
    int PageSize = 100);

public sealed record GeneralLedgerLineDto(
    Guid LedgerEntryLineId, Guid LedgerEntryId, string VoucherNumber, DateOnly PostingDate,
    string? Description, decimal Debit, decimal Credit, decimal RunningBalance, string Currency,
    string? SourceType, string? SourceId, string? SourceVersion, Guid? OriginalLedgerEntryId,
    IReadOnlyList<AccountingEvidenceReferenceDto> Evidence);

public sealed record GeneralLedgerAccountDto(
    Guid AccountId, string AccountCode, string AccountName, string AccountClass, string Currency,
    decimal OpeningBalance, decimal Debit, decimal Credit, decimal ClosingBalance,
    int TotalLineCount, IReadOnlyList<GeneralLedgerLineDto> Lines);

public sealed record GeneralLedgerReportDto(
    Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, DateTime PeriodStartUtc,
    DateTime PeriodEndUtc, bool IsClosed, bool IsReportingLocked, string SourceMode,
    IReadOnlyList<GeneralLedgerAccountDto> Accounts,
    int Page,
    int PageSize,
    long TotalLineCount,
    bool HasMore);

public sealed record TrialBalanceAccountDto(
    Guid AccountId, string AccountCode, string AccountName, string AccountClass, string Currency,
    decimal OpeningBalance, decimal Debit, decimal Credit, decimal ClosingBalance, int JournalLineCount);

public sealed record TrialBalanceReportDto(
    Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, DateTime PeriodStartUtc,
    DateTime PeriodEndUtc, bool IsClosed, bool IsReportingLocked, string SourceMode,
    string Checksum, decimal TotalOpeningDebits, decimal TotalOpeningCredits,
    decimal TotalDebits, decimal TotalCredits, decimal TotalClosingDebits,
    decimal TotalClosingCredits, bool IsBalanced, IReadOnlyList<TrialBalanceAccountDto> Accounts);

public sealed record AccountingEvidenceReferenceDto(Guid DocumentId, string Title, string ContentHash);

public sealed record AccountingTaxSummaryLineDto(
    string PolicyPackKey, string PolicyPackVersion, string TaxRuleKey, string TaxTreatment,
    decimal TaxableAmount, decimal TaxAmount, string Currency, int JournalLineCount,
    IReadOnlyList<Guid> LedgerEntryIds);

public sealed record AccountingTaxSummaryDto(
    Guid CompanyId, Guid FiscalPeriodId, string FiscalPeriodName, bool IsCountryNeutral,
    bool IsStatutoryComplianceValidated, string Label, string ComplianceNotice,
    string Checksum, bool IsReviewed, Guid? ReviewedByUserId, DateTime? ReviewedUtc,
    IReadOnlyList<AccountingTaxSummaryLineDto> Lines);

public sealed record ControlAccountReconciliationLineDto(
    string RoleKey, Guid AccountId, string AccountCode, string AccountName, string Currency,
    decimal LedgerBalance, decimal SourcePostingBalance, decimal Difference, bool IsReconciled,
    IReadOnlyList<Guid> DifferenceJournalEntryIds);

public sealed record ControlAccountReconciliationDto(
    Guid CompanyId, Guid FiscalPeriodId, bool IsReconciled,
    IReadOnlyList<ControlAccountReconciliationLineDto> Accounts);

public sealed record AccountingPeriodHistoryDto(
    Guid Id, string Action, Guid ActorUserId, string Reason, string? SnapshotChecksum, DateTime OccurredUtc);

public sealed record AccountingExportJobDto(
    Guid Id, Guid CompanyId, Guid FiscalPeriodId, string Status, int AttemptCount,
    DateTime RequestedUtc, DateTime? StartedUtc, DateTime? CompletedUtc, DateTime ExpiresUtc,
    string? Checksum, string? FileName, string? MediaType, long? ContentLength,
    string? FailureCode, string? FailureSummary, bool CanDownload,
    string ExportType = AccountingExportTypeValues.GenericJson,
    string? SpecificationVersion = null,
    string? InputChecksum = null,
    string? EncodingName = null,
    int? SourceAccountCount = null,
    int? SourceJournalCount = null,
    int? SourceLineCount = null,
    decimal? SourceDebitTotal = null,
    decimal? SourceCreditTotal = null,
    string? CorrelationId = null);

public sealed record AccountingExportDownloadDto(string FileName, string MediaType, byte[] Content, string Checksum);

public class AccountingExportException(string reasonCode, string message, bool isConflict = false) : Exception(message)
{
    public string ReasonCode { get; } = string.IsNullOrWhiteSpace(reasonCode)
        ? throw new ArgumentException("An export reason code is required.", nameof(reasonCode))
        : reasonCode.Trim().ToLowerInvariant();
    public bool IsConflict { get; } = isConflict;
}

public interface IAccountingReportingService
{
    Task<GeneralLedgerReportDto> GetGeneralLedgerAsync(GetGeneralLedgerQuery query, CancellationToken cancellationToken);
    Task<TrialBalanceReportDto> GetTrialBalanceAsync(GetTrialBalanceQuery query, CancellationToken cancellationToken);
    Task<AccountingTaxSummaryDto> GetTaxSummaryAsync(GetAccountingTaxSummaryQuery query, CancellationToken cancellationToken);
    Task<AccountingTaxSummaryDto> ReviewTaxSummaryAsync(ReviewAccountingTaxSummaryCommand command, CancellationToken cancellationToken);
    Task<ControlAccountReconciliationDto> GetControlAccountReconciliationAsync(GetControlAccountReconciliationQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingPeriodHistoryDto>> GetPeriodHistoryAsync(Guid companyId, Guid fiscalPeriodId, CancellationToken cancellationToken);
    Task<AccountingExportJobDto> RequestExportAsync(RequestAccountingExportCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingExportJobDto>> ListExportsAsync(ListAccountingExportsQuery query, CancellationToken cancellationToken);
    Task<AccountingExportDownloadDto> DownloadExportAsync(GetAccountingExportQuery query, CancellationToken cancellationToken);
    Task<int> RunDueExportsAsync(CancellationToken cancellationToken);
}
