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
