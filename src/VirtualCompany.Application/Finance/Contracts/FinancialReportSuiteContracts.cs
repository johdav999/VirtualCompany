namespace VirtualCompany.Application.Finance;

public static class FinancialReportKinds
{
    public const string CashFlow = "cash_flow";
    public const string EquityChanges = "equity_changes";
    public const string AgedReceivables = "aged_receivables";
    public const string AgedPayables = "aged_payables";
    public const string JournalRegister = "journal_register";
    public const string FixedAssetRegister = "fixed_asset_register";
    public const string TaxDetail = "tax_detail";
    public const string Currency = "currency";
    public const string Dimension = "dimension";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        CashFlow, EquityChanges, AgedReceivables, AgedPayables, JournalRegister,
        FixedAssetRegister, TaxDetail, Currency, Dimension
    };
}

public static class CashFlowMethods
{
    public const string Indirect = "indirect";
    public const string Direct = "direct";
}

public sealed record GetFinancialReportSuiteQuery(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string ReportKind,
    string CashFlowMethod = CashFlowMethods.Indirect,
    Guid? ComparisonFiscalPeriodId = null,
    int RollingPeriodCount = 12,
    DateOnly? AsOfDate = null,
    Guid? DimensionTypeId = null,
    Guid? DimensionMemberId = null,
    int Page = 1,
    int PageSize = 200,
    Guid? DefinitionVersionId = null,
    bool PreviewDefinition = false);

public sealed record GetFinancialReportDrilldownQuery(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string ReportKind,
    string LineKey,
    Guid? SnapshotId = null,
    int Page = 1,
    int PageSize = 200);

public sealed record CaptureFinancialReportSnapshotCommand(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string ReportKind,
    string CashFlowMethod,
    Guid ActorUserId,
    string IdempotencyKey,
    Guid? ComparisonFiscalPeriodId = null,
    int RollingPeriodCount = 12,
    DateOnly? AsOfDate = null,
    Guid? DimensionTypeId = null,
    Guid? DimensionMemberId = null);

public sealed record FinancialReportBlockerDto(string Code, string Explanation, Guid? SubjectId = null);

public sealed record FinancialReportProvenanceDto(
    IReadOnlyList<Guid> LedgerEntryIds,
    IReadOnlyList<Guid> LedgerEntryLineIds,
    IReadOnlyList<string> SourceReferences,
    IReadOnlyList<Guid> DocumentIds,
    IReadOnlyList<Guid> SubledgerItemIds,
    IReadOnlyList<string> DimensionPaths,
    IReadOnlyList<string> ExchangeRateIdentities);

public sealed record FinancialReportLineDto(
    string LineKey,
    string Section,
    string Label,
    decimal Amount,
    decimal? ComparativeAmount,
    decimal? RollingAmount,
    string Currency,
    int ItemCount,
    FinancialReportProvenanceDto Provenance,
    string? AccountCode = null,
    string? Classification = null,
    DateOnly? DueDate = null,
    int? DaysPastDue = null,
    decimal? DocumentCurrencyAmount = null,
    string? DocumentCurrency = null,
    decimal? FunctionalCurrencyAmount = null,
    string? FunctionalCurrency = null);

public sealed record FinancialReportControlTotalsDto(
    decimal TotalDebit,
    decimal TotalCredit,
    decimal NetAmount,
    decimal SourceControlAmount,
    decimal Difference,
    bool IsReconciled);

public sealed record CompleteFinancialReportDto(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string FiscalPeriodName,
    string ReportKind,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    DateOnly AsOfDate,
    string Currency,
    string CalculationVersion,
    string MappingVersion,
    string ParametersHash,
    string Checksum,
    bool IsClosed,
    bool IsReportingLocked,
    bool UsedSnapshot,
    Guid? SnapshotId,
    DateTime GeneratedUtc,
    IReadOnlyList<FinancialReportBlockerDto> Blockers,
    FinancialReportControlTotalsDto ControlTotals,
    IReadOnlyList<FinancialReportLineDto> Lines,
    int Page,
    int PageSize,
    long TotalLineCount,
    bool HasMore,
    long ReproducibilityBudgetMilliseconds,
    long ObservedDurationMilliseconds,
    Guid? ReportDefinitionVersionId = null,
    int? ReportDefinitionVersionNumber = null,
    string? ReportDefinitionHash = null);

public sealed record FinancialReportSnapshotDto(
    Guid Id,
    Guid CompanyId,
    Guid FiscalPeriodId,
    string ReportKind,
    string CalculationVersion,
    string MappingVersion,
    string ParametersHash,
    string Checksum,
    Guid CreatedByUserId,
    DateTime CreatedUtc,
    CompleteFinancialReportDto Report,
    bool IsIdempotentReplay);

public sealed record FinancialReportExportDto(
    string FileName,
    string ContentType,
    byte[] Content,
    string Checksum,
    Guid? ReportDefinitionVersionId,
    int? ReportDefinitionVersionNumber,
    string? ReportDefinitionHash);

public sealed record FinancialReportDrilldownItemDto(
    Guid LedgerEntryLineId,
    Guid LedgerEntryId,
    string VoucherNumber,
    DateOnly PostingDate,
    string AccountCode,
    string AccountName,
    decimal Debit,
    decimal Credit,
    string Currency,
    decimal? DocumentDebit,
    decimal? DocumentCredit,
    string? DocumentCurrency,
    string? ExchangeRateIdentity,
    string? SourceType,
    string? SourceId,
    string? SourceVersion,
    Guid? OriginalLedgerEntryId,
    IReadOnlyList<Guid> DocumentIds,
    IReadOnlyList<string> DimensionPaths);

public sealed record FinancialReportDrilldownDto(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string ReportKind,
    string LineKey,
    string ReportChecksum,
    IReadOnlyList<FinancialReportDrilldownItemDto> Items,
    int Page,
    int PageSize,
    long TotalCount,
    bool HasMore);

public interface IFinancialReportSuiteService
{
    Task<CompleteFinancialReportDto> GetAsync(GetFinancialReportSuiteQuery query, CancellationToken cancellationToken);
    Task<FinancialReportSnapshotDto> CaptureSnapshotAsync(CaptureFinancialReportSnapshotCommand command, CancellationToken cancellationToken);
    Task<FinancialReportSnapshotDto> GetSnapshotAsync(Guid companyId, Guid snapshotId, CancellationToken cancellationToken);
    Task<FinancialReportExportDto> ExportSnapshotAsync(Guid companyId, Guid snapshotId, string format, CancellationToken cancellationToken);
    Task<FinancialReportDrilldownDto> GetDrilldownAsync(GetFinancialReportDrilldownQuery query, CancellationToken cancellationToken);
}

public sealed class FinancialReportException(string reasonCode, string message, bool isConflict = false) : Exception(message)
{
    public string ReasonCode { get; } = reasonCode;
    public bool IsConflict { get; } = isConflict;
}
