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

    public Task<AccountingExportJobResponse> RequestAccountingExportAsync(Guid companyId, Guid periodId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, AccountingExportJobResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/exports", new { fiscalPeriodId = periodId, idempotencyKey }, cancellationToken);
    }

    public async Task<IReadOnlyList<AccountingExportJobResponse>> GetAccountingExportsAsync(Guid companyId, Guid periodId, CancellationToken cancellationToken = default) =>
        await GetAsync<List<AccountingExportJobResponse>>(companyId, $"internal/companies/{companyId}/finance/accounting/exports?fiscalPeriodId={periodId:D}", false, cancellationToken) ?? [];
}

public sealed class GeneralLedgerReportResponse { public Guid CompanyId { get; set; } public Guid FiscalPeriodId { get; set; } public string FiscalPeriodName { get; set; } = ""; public bool IsClosed { get; set; } public bool IsReportingLocked { get; set; } public int Page { get; set; } public int PageSize { get; set; } public long TotalLineCount { get; set; } public bool HasMore { get; set; } public List<GeneralLedgerAccountResponse> Accounts { get; set; } = []; }
public sealed class GeneralLedgerAccountResponse { public Guid AccountId { get; set; } public string AccountCode { get; set; } = ""; public string AccountName { get; set; } = ""; public string AccountClass { get; set; } = ""; public string Currency { get; set; } = ""; public decimal OpeningBalance { get; set; } public decimal Debit { get; set; } public decimal Credit { get; set; } public decimal ClosingBalance { get; set; } public int TotalLineCount { get; set; } public List<GeneralLedgerLineResponse> Lines { get; set; } = []; }
public sealed class GeneralLedgerLineResponse { public Guid LedgerEntryLineId { get; set; } public Guid LedgerEntryId { get; set; } public string VoucherNumber { get; set; } = ""; public DateOnly PostingDate { get; set; } public string? Description { get; set; } public decimal Debit { get; set; } public decimal Credit { get; set; } public decimal RunningBalance { get; set; } public string Currency { get; set; } = ""; public string? SourceType { get; set; } public string? SourceId { get; set; } public List<AccountingEvidenceReferenceResponse> Evidence { get; set; } = []; }
public sealed class AccountingEvidenceReferenceResponse { public Guid DocumentId { get; set; } public string Title { get; set; } = ""; public string ContentHash { get; set; } = ""; }
public sealed class TrialBalanceReportResponse { public Guid CompanyId { get; set; } public Guid FiscalPeriodId { get; set; } public string FiscalPeriodName { get; set; } = ""; public bool IsClosed { get; set; } public bool IsReportingLocked { get; set; } public string SourceMode { get; set; } = ""; public string Checksum { get; set; } = ""; public decimal TotalDebits { get; set; } public decimal TotalCredits { get; set; } public decimal TotalClosingDebits { get; set; } public decimal TotalClosingCredits { get; set; } public bool IsBalanced { get; set; } public List<TrialBalanceAccountResponse> Accounts { get; set; } = []; }
public sealed class TrialBalanceAccountResponse { public Guid AccountId { get; set; } public string AccountCode { get; set; } = ""; public string AccountName { get; set; } = ""; public string AccountClass { get; set; } = ""; public string Currency { get; set; } = ""; public decimal OpeningBalance { get; set; } public decimal Debit { get; set; } public decimal Credit { get; set; } public decimal ClosingBalance { get; set; } public int JournalLineCount { get; set; } }
public sealed class FinanceStatementLineResponse { public Guid? FinanceAccountId { get; set; } public string AccountCode { get; set; } = ""; public string AccountName { get; set; } = ""; public string ReportSection { get; set; } = ""; public string LineClassification { get; set; } = ""; public decimal Amount { get; set; } public string Currency { get; set; } = ""; }
public sealed class ProfitAndLossReportResponse { public string FiscalPeriodName { get; set; } = ""; public bool IsClosed { get; set; } public bool UsedSnapshot { get; set; } public string Currency { get; set; } = ""; public List<FinanceStatementLineResponse> RevenueLines { get; set; } = []; public List<FinanceStatementLineResponse> ExpenseLines { get; set; } = []; public decimal TotalRevenue { get; set; } public decimal TotalExpenses { get; set; } public decimal NetIncome { get; set; } }
public sealed class BalanceSheetReportResponse { public string FiscalPeriodName { get; set; } = ""; public bool IsClosed { get; set; } public bool UsedSnapshot { get; set; } public string Currency { get; set; } = ""; public List<FinanceStatementLineResponse> AssetLines { get; set; } = []; public List<FinanceStatementLineResponse> LiabilityLines { get; set; } = []; public List<FinanceStatementLineResponse> EquityLines { get; set; } = []; public decimal TotalAssets { get; set; } public decimal TotalLiabilities { get; set; } public decimal TotalEquity { get; set; } public bool IsBalanced { get; set; } }
public sealed class AccountingTaxSummaryResponse { public bool IsCountryNeutral { get; set; } public bool IsStatutoryComplianceValidated { get; set; } public string Label { get; set; } = ""; public string ComplianceNotice { get; set; } = ""; public string Checksum { get; set; } = ""; public bool IsReviewed { get; set; } public Guid? ReviewedByUserId { get; set; } public DateTime? ReviewedUtc { get; set; } public List<AccountingTaxSummaryLineResponse> Lines { get; set; } = []; }
public sealed class AccountingTaxSummaryLineResponse { public string PolicyPackKey { get; set; } = ""; public string PolicyPackVersion { get; set; } = ""; public string TaxRuleKey { get; set; } = ""; public string TaxTreatment { get; set; } = ""; public decimal TaxableAmount { get; set; } public decimal TaxAmount { get; set; } public string Currency { get; set; } = ""; public int JournalLineCount { get; set; } }
public sealed class ControlAccountReconciliationResponse { public bool IsReconciled { get; set; } public List<ControlAccountReconciliationLineResponse> Accounts { get; set; } = []; }
public sealed class ControlAccountReconciliationLineResponse { public string RoleKey { get; set; } = ""; public Guid AccountId { get; set; } public string AccountCode { get; set; } = ""; public string AccountName { get; set; } = ""; public string Currency { get; set; } = ""; public decimal LedgerBalance { get; set; } public decimal SourcePostingBalance { get; set; } public decimal Difference { get; set; } public bool IsReconciled { get; set; } }
public sealed class ReportingPeriodCloseValidationResponse { public bool IsReadyToClose { get; set; } public bool IsClosed { get; set; } public bool IsReportingLocked { get; set; } public DateTime ExecutedAtUtc { get; set; } public List<ReportingPeriodBlockingIssueResponse> BlockingIssues { get; set; } = []; }
public sealed class ReportingPeriodBlockingIssueResponse { public string Code { get; set; } = ""; public string Message { get; set; } = ""; public int Count { get; set; } public List<string> SampleReferences { get; set; } = []; public decimal? Amount { get; set; } public string? Currency { get; set; } public List<string> RecordLinks { get; set; } = []; public string? Remediation { get; set; } }
public sealed class ReportingPeriodLockStateResponse { public Guid FiscalPeriodId { get; set; } public bool IsClosed { get; set; } public bool IsReportingLocked { get; set; } public DateTime? ReportingLockedAtUtc { get; set; } public DateTime? ReportingUnlockedAtUtc { get; set; } }
public sealed class AccountingPeriodHistoryResponse { public Guid Id { get; set; } public string Action { get; set; } = ""; public Guid ActorUserId { get; set; } public string Reason { get; set; } = ""; public string? SnapshotChecksum { get; set; } public DateTime OccurredUtc { get; set; } }
public sealed class AccountingExportJobResponse { public Guid Id { get; set; } public Guid FiscalPeriodId { get; set; } public string Status { get; set; } = ""; public int AttemptCount { get; set; } public DateTime RequestedUtc { get; set; } public DateTime? StartedUtc { get; set; } public DateTime? CompletedUtc { get; set; } public DateTime ExpiresUtc { get; set; } public string? Checksum { get; set; } public string? FileName { get; set; } public long? ContentLength { get; set; } public string? FailureCode { get; set; } public string? FailureSummary { get; set; } public bool CanDownload { get; set; } }
