

namespace VirtualCompany.Application.Finance;

public sealed record FinanceOperationalCurrencyDto(string Currency, decimal Receivables, decimal OverdueReceivables,
    decimal Payables, decimal OverduePayables, decimal? StartingCash, decimal ExpectedInflows,
    decimal ExpectedOutflows, decimal? ProjectedCash);

public sealed record FinanceObligationRowDto(Guid Id, string Kind, string Number, string Counterparty,
    DateTime DueUtc, int DaysOverdue, string Bucket, decimal RemainingAmount, string Currency,
    string Status, DateTime SourceUpdatedUtc, string DeepLink);

public sealed record FinanceOperationalReportDto(Guid CompanyId, DateOnly AsOfDate, DateTime ObservedAtUtc,
    DateOnly ThroughDate, string TimeZone, string CalculationVersion, string Meaning, string ForecastAssumptions,
    string? CurrencyFilter, string? BucketFilter, IReadOnlyList<FinanceObligationRowDto> Receivables,
    IReadOnlyList<FinanceObligationRowDto> Payables, IReadOnlyList<FinanceOperationalCurrencyDto> Totals,
    IReadOnlyList<FinanceCashEvidenceDto> CashEvidence, IReadOnlyList<string> CoverageGaps, int ReconciliationExceptions = 0);

public sealed record FinanceCashEvidenceDto(Guid AccountId, string AccountName, decimal Amount, string Currency,
    DateTime? SourceObservedUtc = null, string? Basis = null);
