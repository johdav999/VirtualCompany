namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<GeneralLedgerReportResponse?> GetAccountingGeneralLedgerAsync(Guid companyId, Guid periodId, Guid? accountId = null, CancellationToken cancellationToken = default) =>
        GetAccountingGeneralLedgerPageAsync(companyId, periodId, accountId, 1, 200, cancellationToken);

    public Task<GeneralLedgerReportResponse?> GetAccountingGeneralLedgerPageAsync(Guid companyId, Guid periodId,
        Guid? accountId, int page, int pageSize, CancellationToken cancellationToken = default) =>
        GetAsync<GeneralLedgerReportResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/reports/general-ledger?fiscalPeriodId={periodId:D}{(accountId.HasValue ? $"&accountId={accountId:D}" : string.Empty)}&page={Math.Max(1, page)}&pageSize={Math.Clamp(pageSize, 25, 1000)}",
            false, cancellationToken);

    public Task<TrialBalanceReportResponse?> GetAccountingTrialBalanceAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken = default) =>
        GetAsync<TrialBalanceReportResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/reports/trial-balance?fiscalPeriodId={periodId:D}", false, cancellationToken);

    public Task<ProfitAndLossReportResponse?> GetAccountingProfitAndLossAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken = default) =>
        GetAsync<ProfitAndLossReportResponse>(companyId, $"internal/companies/{companyId}/finance/reports/profit-loss?fiscalPeriodId={periodId:D}", false, cancellationToken);

    public Task<BalanceSheetReportResponse?> GetAccountingBalanceSheetAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken = default) =>
        GetAsync<BalanceSheetReportResponse>(companyId, $"internal/companies/{companyId}/finance/reports/balance-sheet?fiscalPeriodId={periodId:D}", false, cancellationToken);

    public Task<AccountingTaxSummaryResponse?> GetAccountingTaxSummaryAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingTaxSummaryResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/reports/tax-summary?fiscalPeriodId={periodId:D}", false, cancellationToken);

    public Task<AccountingTaxSummaryResponse> ReviewAccountingTaxSummaryAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AccountingTaxSummaryResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/reports/tax-summary/review", new { fiscalPeriodId = periodId }, cancellationToken);
    }

    public Task<ControlAccountReconciliationResponse?> GetAccountingControlReconciliationAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken = default) =>
        GetAsync<ControlAccountReconciliationResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/reports/control-reconciliation?fiscalPeriodId={periodId:D}", false, cancellationToken);

    public async Task<IReadOnlyList<AccountingPeriodHistoryResponse>> GetAccountingPeriodHistoryAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<AccountingPeriodHistoryResponse>>(companyId, $"internal/companies/{companyId}/finance/accounting/periods/{periodId:D}/history", false, cancellationToken) ?? [];

    public Task<ReportingPeriodCloseValidationResponse> ValidateAccountingPeriodCloseAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<object, ReportingPeriodCloseValidationResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/fiscal-periods/{periodId:D}/reporting/validation", new { }, cancellationToken);

    public Task<ReportingPeriodLockStateResponse> CloseAndLockAccountingPeriodAsync(Guid companyId, Guid periodId, string reason, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, ReportingPeriodLockStateResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/fiscal-periods/{periodId:D}/reporting/close-and-lock", new { reason }, cancellationToken);
    }

    public Task<ReportingPeriodLockStateResponse> ReopenAccountingPeriodAsync(Guid companyId, Guid periodId, string reason, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, ReportingPeriodLockStateResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/fiscal-periods/{periodId:D}/reporting/reopen", new { reason }, cancellationToken);
    }

    public Task<AccountingExportJobResponse> RequestAccountingExportAsync(Guid companyId, Guid periodId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        RequestAccountingExportAsync(companyId, periodId, idempotencyKey, AccountingExportApiValues.GenericJson, null, cancellationToken);

    public Task<AccountingExportJobResponse> RequestAccountingExportAsync(Guid companyId, Guid periodId, string idempotencyKey,
        string exportType, string? correlationId = null, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AccountingExportJobResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/exports", new { fiscalPeriodId = periodId, idempotencyKey, exportType, correlationId }, cancellationToken);
    }

    public async Task<IReadOnlyList<AccountingExportJobResponse>> GetAccountingExportsAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<AccountingExportJobResponse>>(companyId, $"internal/companies/{companyId}/finance/accounting/exports?fiscalPeriodId={periodId:D}", false, cancellationToken) ?? [];

    public static string GetAccountingExportDownloadUrl(Guid companyId, Guid exportId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(companyId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(exportId, Guid.Empty);
        return $"internal/companies/{companyId}/finance/accounting/exports/{exportId:D}/download";
    }
}

public static class AccountingExportApiValues
{
    public const string GenericJson = "generic_json";
    public const string Sie4B = "sie_4b";
    public const string SwedishStatutoryArchive = "swedish_statutory_archive";
    public const string FinancialReportSuiteJson = "financial_report_suite_json";
}
