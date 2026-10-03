using System.Globalization;

namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public async Task<FinanceOperationalReportResponse?> GetOperationalReportAsync(Guid companyId, DateOnly asOfDate,
        int horizonDays = 14, string? currency = null, string? bucket = null, CancellationToken token = default, string? sourceFilter = null)
    {
        if (_useOfflineMode) throw new FinanceApiException("Finance reports are unavailable in offline mode.");
        var uri = $"api/companies/{companyId:D}/finance/operational-report?asOfDate={asOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&horizonDays={horizonDays}" +
            $"&currency={Uri.EscapeDataString(currency ?? "")}&bucket={Uri.EscapeDataString(bucket ?? "")}&source={Uri.EscapeDataString(sourceFilter ?? _financeDataSourceFilter ?? "operational")}";
        try { return await GetAsync<FinanceOperationalReportResponse>(companyId, uri, false, token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new FinanceApiException("The report request timed out. Retry the same filters."); }
        catch (System.Text.Json.JsonException) { throw new FinanceApiException("The report response could not be read. Retry the report."); }
    }
}
public sealed record FinanceObligationRowResponse(Guid Id, string Kind, string Number, string Counterparty, DateTime DueUtc,
    int DaysOverdue, string Bucket, decimal RemainingAmount, string Currency, string Status, DateTime SourceUpdatedUtc, string DeepLink);
public sealed record FinanceOperationalCurrencyResponse(string Currency, decimal Receivables, decimal OverdueReceivables,
    decimal Payables, decimal OverduePayables, decimal? StartingCash, decimal ExpectedInflows, decimal ExpectedOutflows, decimal? ProjectedCash);
public sealed record FinanceCashEvidenceResponse(Guid AccountId, string AccountName, decimal Amount, string Currency,
    DateTime? SourceObservedUtc = null, string? Basis = null);
public sealed record FinanceOperationalReportResponse(Guid CompanyId, DateOnly AsOfDate, DateTime ObservedAtUtc,
    DateOnly ThroughDate, string TimeZone, string CalculationVersion, string Meaning, string ForecastAssumptions,
    string? CurrencyFilter, string? BucketFilter, IReadOnlyList<FinanceObligationRowResponse> Receivables,
    IReadOnlyList<FinanceObligationRowResponse> Payables, IReadOnlyList<FinanceOperationalCurrencyResponse> Totals,
    IReadOnlyList<FinanceCashEvidenceResponse> CashEvidence, IReadOnlyList<string> CoverageGaps, int ReconciliationExceptions = 0);
