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

public sealed record FinancialReportExportDto(
    string FileName,
    string ContentType,
    byte[] Content,
    string Checksum,
    Guid? ReportDefinitionVersionId,
    int? ReportDefinitionVersionNumber,
    string? ReportDefinitionHash);

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
