using System.Net.Http.Json;

namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<CompleteFinancialReportResponse?> GetFinancialReportSuiteAsync(Guid companyId, Guid periodId,
        string reportKind, string cashFlowMethod = "indirect", Guid? comparisonPeriodId = null,
        int rollingPeriodCount = 12, DateOnly? asOfDate = null, Guid? dimensionTypeId = null,
        Guid? dimensionMemberId = null, int page = 1, int pageSize = 200,
        CancellationToken cancellationToken = default)
    {
        var path = $"internal/companies/{companyId}/finance/accounting/report-suite/{Uri.EscapeDataString(reportKind)}" +
            $"?fiscalPeriodId={periodId:D}&cashFlowMethod={Uri.EscapeDataString(cashFlowMethod)}" +
            $"&rollingPeriodCount={rollingPeriodCount}&page={page}&pageSize={pageSize}" +
            (comparisonPeriodId.HasValue ? $"&comparisonFiscalPeriodId={comparisonPeriodId:D}" : string.Empty) +
            (asOfDate.HasValue ? $"&asOfDate={asOfDate:yyyy-MM-dd}" : string.Empty) +
            (dimensionTypeId.HasValue ? $"&dimensionTypeId={dimensionTypeId:D}" : string.Empty) +
            (dimensionMemberId.HasValue ? $"&dimensionMemberId={dimensionMemberId:D}" : string.Empty);
        return GetAsync<CompleteFinancialReportResponse>(companyId, path, false, cancellationToken);
    }

    public Task<FinancialReportSnapshotResponse> CaptureFinancialReportSnapshotAsync(Guid companyId,
        CaptureFinancialReportSnapshotApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, FinancialReportSnapshotResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/report-suite/snapshots", request, cancellationToken);
    }

    public Task<FinancialReportSnapshotResponse?> GetFinancialReportSnapshotAsync(Guid companyId, Guid snapshotId,
        CancellationToken cancellationToken = default) => GetAsync<FinancialReportSnapshotResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/report-suite/snapshots/{snapshotId:D}", false, cancellationToken);

    public string GetFinancialReportSnapshotExportUrl(Guid companyId, Guid snapshotId, string format = "csv") =>
        $"internal/companies/{companyId}/finance/accounting/report-suite/snapshots/{snapshotId:D}/export?format={Uri.EscapeDataString(format)}";

    public Task<FinancialReportDrilldownResponse?> GetFinancialReportDrilldownAsync(Guid companyId, Guid periodId,
        string reportKind, string lineKey, Guid? snapshotId = null, int page = 1, int pageSize = 200,
        CancellationToken cancellationToken = default) => GetAsync<FinancialReportDrilldownResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/report-suite/{Uri.EscapeDataString(reportKind)}/lines/{Uri.EscapeDataString(lineKey)}/drilldown" +
            $"?fiscalPeriodId={periodId:D}&page={page}&pageSize={pageSize}" +
            (snapshotId.HasValue ? $"&snapshotId={snapshotId:D}" : string.Empty), false, cancellationToken);
}

public sealed class CaptureFinancialReportSnapshotApiRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string ReportKind { get; set; } = string.Empty;
    public string CashFlowMethod { get; set; } = "indirect";
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? ComparisonFiscalPeriodId { get; set; }
    public int RollingPeriodCount { get; set; } = 12;
    public DateOnly? AsOfDate { get; set; }
    public Guid? DimensionTypeId { get; set; }
    public Guid? DimensionMemberId { get; set; }
}
public sealed class CompleteFinancialReportResponse
{
    public Guid CompanyId { get; set; } public Guid FiscalPeriodId { get; set; } public string FiscalPeriodName { get; set; } = "";
    public string ReportKind { get; set; } = ""; public DateTime PeriodStartUtc { get; set; } public DateTime PeriodEndUtc { get; set; }
    public DateOnly AsOfDate { get; set; } public string Currency { get; set; } = ""; public string CalculationVersion { get; set; } = "";
    public string MappingVersion { get; set; } = ""; public string ParametersHash { get; set; } = ""; public string Checksum { get; set; } = "";
    public bool IsClosed { get; set; } public bool IsReportingLocked { get; set; } public bool UsedSnapshot { get; set; } public Guid? SnapshotId { get; set; }
    public DateTime GeneratedUtc { get; set; } public List<FinancialReportBlockerResponse> Blockers { get; set; } = [];
    public Guid? DefinitionVersionId { get; set; } public int? DefinitionVersionNumber { get; set; } public string? DefinitionHash { get; set; }
    public FinancialReportControlTotalsResponse ControlTotals { get; set; } = new(); public List<FinancialReportLineResponse> Lines { get; set; } = [];
    public int Page { get; set; } public int PageSize { get; set; } public long TotalLineCount { get; set; } public bool HasMore { get; set; }
    public long ReproducibilityBudgetMilliseconds { get; set; } public long ObservedDurationMilliseconds { get; set; }
}
public sealed class FinancialReportBlockerResponse { public string Code { get; set; } = ""; public string Explanation { get; set; } = ""; public Guid? SubjectId { get; set; } }
public sealed class FinancialReportControlTotalsResponse { public decimal TotalDebit { get; set; } public decimal TotalCredit { get; set; } public decimal NetAmount { get; set; } public decimal SourceControlAmount { get; set; } public decimal Difference { get; set; } public bool IsReconciled { get; set; } }
public sealed class FinancialReportLineResponse
{
    public string LineKey { get; set; } = ""; public string Section { get; set; } = ""; public string Label { get; set; } = "";
    public decimal Amount { get; set; } public decimal? ComparativeAmount { get; set; } public decimal? RollingAmount { get; set; }
    public string Currency { get; set; } = ""; public int ItemCount { get; set; } public FinancialReportProvenanceResponse Provenance { get; set; } = new();
}
public sealed class FinancialReportProvenanceResponse { public List<Guid> LedgerEntryIds { get; set; } = []; public List<Guid> LedgerEntryLineIds { get; set; } = []; public List<string> SourceReferences { get; set; } = []; public List<Guid> DocumentIds { get; set; } = []; public List<Guid> SubledgerItemIds { get; set; } = []; public List<string> DimensionPaths { get; set; } = []; public List<string> ExchangeRateIdentities { get; set; } = []; }
public sealed class FinancialReportSnapshotResponse { public Guid Id { get; set; } public string Checksum { get; set; } = ""; public string MappingVersion { get; set; } = ""; public DateTime CreatedUtc { get; set; } public bool IsIdempotentReplay { get; set; } public CompleteFinancialReportResponse Report { get; set; } = new(); }
public sealed class FinancialReportDrilldownResponse { public string ReportKind { get; set; } = ""; public string LineKey { get; set; } = ""; public string ReportChecksum { get; set; } = ""; public List<FinancialReportDrilldownItemResponse> Items { get; set; } = []; public int Page { get; set; } public int PageSize { get; set; } public long TotalCount { get; set; } public bool HasMore { get; set; } }
public sealed class FinancialReportDrilldownItemResponse { public Guid LedgerEntryLineId { get; set; } public Guid LedgerEntryId { get; set; } public string VoucherNumber { get; set; } = ""; public DateOnly PostingDate { get; set; } public string AccountCode { get; set; } = ""; public string AccountName { get; set; } = ""; public decimal Debit { get; set; } public decimal Credit { get; set; } public string Currency { get; set; } = ""; public string? SourceType { get; set; } public string? SourceId { get; set; } public List<Guid> DocumentIds { get; set; } = []; public List<string> DimensionPaths { get; set; } = []; }
