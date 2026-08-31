using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

public sealed record GetFinanceCashBalanceQuery(
    Guid CompanyId,
    DateTime? AsOfUtc = null);

public sealed record GetFinanceMonthlyProfitAndLossQuery(
    Guid CompanyId,
    int Year,
    int Month);

public sealed record GetFinanceProfitAndLossReportQuery(
    Guid CompanyId,
    Guid FiscalPeriodId);

public sealed record GetFinancialStatementDrilldownQuery(
    Guid CompanyId,
    Guid? FiscalPeriodId,
    FinancialStatementType? StatementType,
    string LineCode,
    int? SnapshotVersionNumber = null,
    Guid? SnapshotId = null);

public sealed record ListFinancialStatementSnapshotsQuery(
    Guid CompanyId,
    Guid? FiscalPeriodId = null,
    FinancialStatementType? StatementType = null);

public sealed record GetFinancialStatementSnapshotQuery(
    Guid CompanyId,
    Guid SnapshotId);

public sealed record GetFinanceBalanceSheetReportQuery(Guid CompanyId, Guid FiscalPeriodId);

public sealed record ValidateReportingPeriodCloseQuery(Guid CompanyId, Guid FiscalPeriodId);

public sealed record LockReportingPeriodCommand(Guid CompanyId, Guid FiscalPeriodId);

public sealed record CloseAndLockReportingPeriodCommand(Guid CompanyId, Guid FiscalPeriodId, string Reason);

public sealed record ReopenReportingPeriodCommand(Guid CompanyId, Guid FiscalPeriodId, string Reason);

public sealed record UnlockReportingPeriodCommand(Guid CompanyId, Guid FiscalPeriodId);

public sealed record RegenerateStoredReportingStatementsCommand(
    Guid CompanyId,
    Guid FiscalPeriodId,
    bool RunInBackground = false);

public sealed record ReportingPeriodBlockingIssueDto(
    string Code,
    string Message,
    int Count,
    IReadOnlyList<string> SampleReferences,
    decimal? Amount = null,
    string? Currency = null,
    IReadOnlyList<string>? RecordLinks = null,
    string? Remediation = null,
    IReadOnlyDictionary<string, string>? Evidence = null);

public sealed record ReportingPeriodCloseValidationResultDto(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string FiscalPeriodName,
    DateTime ExecutedAtUtc,
    string ActorType,
    Guid? ActorId,
    Guid MembershipId,
    string MembershipRole,
    bool IsReadyToClose,
    bool IsClosed,
    bool IsReportingLocked,
    [property: System.Text.Json.Serialization.JsonPropertyName("blockingIssues")]
    IReadOnlyList<ReportingPeriodBlockingIssueDto> Issues);

public sealed record ReportingPeriodLockStateDto(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string FiscalPeriodName,
    bool IsClosed,
    bool IsReportingLocked,
    DateTime? ReportingLockedAtUtc,
    Guid? ReportingLockedByUserId,
    DateTime? ReportingUnlockedAtUtc,
    Guid? ReportingUnlockedByUserId,
    DateTime? LastCloseValidatedAtUtc,
    Guid? LastCloseValidatedByUserId,
    DateTime UpdatedAtUtc);

public static class ReportingPeriodErrorCodes
{
    public const string ReportingPeriodNotClosed = "reporting_period_not_closed";
    public const string ReportingPeriodLocked = "reporting_period_locked";
    public const string ReportingPeriodStateChanged = "reporting_period_state_changed";
}

public static class ReportingPeriodBlockingIssueCodes
{
    public const string UnpostedSourceDocuments = "unposted_source_documents";
    public const string UnbalancedJournalEntries = "unbalanced_journal_entries";
    public const string MissingStatementMappings = "missing_statement_mappings";
    public const string UnresolvedSuspense = "unresolved_suspense";
    public const string ReconciliationConflicts = "reconciliation_conflicts";
    public const string ControlAccountDifference = "control_account_difference";
    public const string TaxReviewIncomplete = "tax_review_incomplete";
    public const string VatReturnMissing = "vat_return_missing";
    public const string VatReturnStale = "vat_return_stale";
    public const string VatReturnBlocking = "vat_return_blocking";
    public const string VatReturnUnreviewed = "vat_return_unreviewed";
    public const string CurrencyRevaluationMissing = "currency_revaluation_missing";
    public const string CurrencyRevaluationStale = "currency_revaluation_stale";
    public const string CurrencyRevaluationFailed = "currency_revaluation_failed";
    public const string CurrencyRevaluationUnposted = "currency_revaluation_unposted";
    public const string CurrencyRevaluationUnreconciled = "currency_revaluation_unreconciled";
    public const string CurrencyRevaluationSuperseded = "currency_revaluation_superseded";
    public const string AccountingSchedulesIncomplete = "accounting_schedules_incomplete";
    public const string FixedAssetDepreciationIncomplete = "fixed_asset_depreciation_incomplete";
    public const string FixedAssetSubledgerUnreconciled = "fixed_asset_subledger_unreconciled";
    public const string FixedAssetMigrationConflicts = "fixed_asset_migration_conflicts";
    public const string AccountingSchedulesUnreconciled = "accounting_schedules_unreconciled";
    public const string StoredReportsStale = "stored_reports_stale";
}

public class ReportingPeriodOperationException : Exception
{
    public ReportingPeriodOperationException(string code, string title, string message)
        : base(message)
    {
        Code = string.IsNullOrWhiteSpace(code) ? "reporting_period_operation_failed" : code.Trim();
        Title = string.IsNullOrWhiteSpace(title) ? "Reporting period operation failed" : title.Trim();
    }

    public string Code { get; }
    public string Title { get; }
}

public sealed class ReportingPeriodLockedException : ReportingPeriodOperationException
{
    public ReportingPeriodLockedException(Guid fiscalPeriodId, string fiscalPeriodName)
        : base(
            ReportingPeriodErrorCodes.ReportingPeriodLocked,
            "Reporting period is locked.",
            $"Fiscal period '{fiscalPeriodName}' ({fiscalPeriodId:D}) is locked for reporting changes.")
    {
        FiscalPeriodId = fiscalPeriodId;
        FiscalPeriodName = fiscalPeriodName;
    }

    public Guid FiscalPeriodId { get; }
    public string FiscalPeriodName { get; }
}

public sealed record GetFinanceCashPositionQuery(
    Guid CompanyId,
    DateTime? AsOfUtc = null,
    decimal? AverageMonthlyBurn = null,
    int BurnLookbackDays = 90);

public sealed record GetFinanceExpenseBreakdownQuery(
    Guid CompanyId,
    DateTime StartUtc,
    DateTime EndUtc);

public sealed record GetFinanceBalancesQuery(
    Guid CompanyId,
    DateTime? AsOfUtc = null);

public sealed record GetFinanceSummaryQuery(
    Guid CompanyId,
    DateTime? AsOfUtc = null,
    int RecentAssetPurchaseLimit = 5,
    bool IncludeConsistencyCheck = false,
    string SourceFilter = FinanceDataSources.Operational);

public sealed record FinanceTopExpenseItemDto(
    string Label,
    decimal Amount,
    string Currency,
    int EntryCount,
    decimal ShareOfExpenses,
    string Narrative);

public sealed record FinanceStatementAnalyticsDto(
    IReadOnlyList<FinancialStatementSnapshotSummaryDto> Snapshots,
    FinancialStatementSnapshotSummaryDto? LatestBalanceSheetSnapshot,
    FinancialStatementSnapshotSummaryDto? LatestProfitAndLossSnapshot);

public sealed record EvaluateFinanceCashPositionWorkflowCommand(
    Guid CompanyId,
    Guid? WorkflowInstanceId = null,
    Guid? AgentId = null,
    string? CorrelationId = null,
    string? TriggerEventId = null,
    string? SourceEntityId = null,
    string? SourceEntityVersion = null);

public sealed record GetFinanceForecastsQuery(
    Guid CompanyId,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    Guid? FinanceAccountId = null,
    string? Version = null);

public sealed record FinanceCashBalanceDto(
    Guid CompanyId,
    DateTime AsOfUtc,
    decimal Amount,
    string Currency,
    IReadOnlyList<FinanceAccountBalanceDto> Accounts);

public sealed record FinanceAccountBalanceDto(
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string AccountType,
    decimal Amount,
    string Currency,
    DateTime AsOfUtc);

public sealed record FinanceSummaryAssetPurchaseDto(
    Guid AssetId,
    Guid CompanyId,
    string ReferenceNumber,
    string Name,
    string Category,
    DateTime PurchasedUtc,
    decimal Amount,
    string Currency,
    string FundingBehavior,
    string FundingSettlementStatus);

public sealed record FinanceSummaryConsistencyMetricDto(
    string MetricKey,
    decimal ExpectedValue,
    decimal ActualValue,
    bool IsMatch);

public sealed record FinanceSummaryConsistencyResultDto(
    Guid CompanyId,
    DateTime AsOfUtc,
    bool IsConsistent,
    int SourceRecordCount,
    IReadOnlyList<FinanceSummaryConsistencyMetricDto> Metrics);

public sealed record FinanceSummaryDto(
    Guid CompanyId,
    DateTime AsOfUtc,
    decimal CurrentCash,
    decimal AccountsReceivable,
    decimal OverdueReceivables,
    decimal AccountsPayable,
    decimal OverduePayables,
    decimal MonthlyRevenue,
    decimal MonthlyCosts,
    string Currency,
    bool HasFinanceData,
    int RecentAssetPurchaseCount,
    decimal RecentAssetPurchaseTotalAmount,
    IReadOnlyList<FinanceSummaryAssetPurchaseDto> RecentAssetPurchases,
    FinanceIntelligenceSnapshotDto? Intelligence = null,
    FinanceSummaryConsistencyResultDto? ConsistencyCheck = null,
    string Source = FinanceDataSources.Operational);

public sealed record FinanceCashProjectionDto(
    int HorizonDays,
    decimal StartingCash,
    decimal ProjectedInflows,
    decimal ProjectedOutflows,
    decimal EndingCash,
    decimal InvoiceInflows,
    decimal BillOutflows,
    decimal RecurringOutflows,
    string ProjectionRule = "Includes open invoices, due bills, and recurring outflows with due dates inside the projection horizon.");

public sealed record FinanceMonthlyProfitAndLossDto(
    Guid CompanyId,
    int Year,
    int Month,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal Revenue,
    decimal Expenses,
    decimal NetResult,
    string Currency);

public sealed record FinanceExpenseBreakdownDto(
    Guid CompanyId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal TotalExpenses,
    string Currency,
    IReadOnlyList<FinanceExpenseCategoryDto> Categories);

public sealed record FinanceExpenseCategoryDto(
    string Category,
    decimal Amount,
    string Currency);

public sealed record FinanceCashPositionDto(
    Guid CompanyId,
    DateTime AsOfUtc,
    decimal AvailableBalance,
    string Currency,
    decimal AverageMonthlyBurn,
    int? EstimatedRunwayDays,
    FinanceCashPositionThresholdsDto Thresholds,
    FinanceCashPositionAlertStateDto AlertState,
    FinanceWorkflowOutputSchemaDto WorkflowOutput)
{
    public string Classification => WorkflowOutput.Classification;
    public string RiskLevel => WorkflowOutput.RiskLevel;
    public string RecommendedAction => WorkflowOutput.RecommendedAction;
    public string Rationale => WorkflowOutput.Rationale;
    public decimal Confidence => WorkflowOutput.Confidence;
    public string SourceWorkflow => WorkflowOutput.SourceWorkflow;
}

public sealed record FinanceAgentQueryPeriodDto(
    DateTime AsOfUtc,
    DateTime? WindowStartUtc,
    DateTime? WindowEndUtc,
    DateTime? ComparisonStartUtc,
    DateTime? ComparisonEndUtc,
    string TimeZoneId);

public sealed record FinanceAgentMetricComponentDto(
    string ComponentKey,
    string Label,
    decimal CurrentValue,
    decimal? PreviousValue,
    decimal Delta,
    string Currency,
    IReadOnlyList<Guid> SourceRecordIds);

public sealed record FinanceStatementLineDto(
    Guid? FinanceAccountId,
    string AccountCode,
    string AccountName,
    string ReportSection,
    string LineClassification,
    decimal Amount,
    string Currency);

public sealed record FinancialStatementSnapshotMetadataDto(
    Guid SnapshotId,
    int VersionNumber,
    string BalancesChecksum,
    DateTime GeneratedAtUtc,
    DateTime SourcePeriodStartUtc,
    DateTime SourcePeriodEndUtc,
    string Currency);

public sealed record ProfitAndLossReportDto(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string FiscalPeriodName,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    bool IsClosed,
    bool UsedSnapshot,
    string Currency,
    IReadOnlyList<FinanceStatementLineDto> RevenueLines,
    IReadOnlyList<FinanceStatementLineDto> ExpenseLines,
    decimal TotalRevenue,
    decimal TotalExpenses,
    decimal NetIncome,
    FinancialStatementSnapshotMetadataDto? Snapshot);

public sealed record BalanceSheetReportDto(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string FiscalPeriodName,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    bool IsClosed,
    bool UsedSnapshot,
    string Currency,
    IReadOnlyList<FinanceStatementLineDto> AssetLines,
    IReadOnlyList<FinanceStatementLineDto> LiabilityLines,
    IReadOnlyList<FinanceStatementLineDto> EquityLines,
    decimal TotalAssets,
    decimal TotalLiabilities,
    decimal TotalEquity,
    bool IsBalanced,
    FinancialStatementSnapshotMetadataDto? Snapshot);

public sealed record FinancialStatementSnapshotSummaryDto(
    Guid SnapshotId,
    Guid CompanyId,
    Guid FiscalPeriodId,
    string FiscalPeriodName,
    string StatementType,
    int VersionNumber,
    string BalancesChecksum,
    DateTime GeneratedAtUtc,
    DateTime SourcePeriodStartUtc,
    DateTime SourcePeriodEndUtc,
    string Currency,
    int LineCount);

public sealed record FinancialStatementSnapshotDetailDto(
    Guid SnapshotId,
    FinancialStatementSnapshotSummaryDto Summary,
    IReadOnlyList<FinanceStatementLineDto> Lines);

public sealed record FinancialStatementDrilldownLineDto(
    string LineCode,
    string LineName,
    string ReportSection,
    string LineClassification,
    decimal Amount,
    string Currency);

public sealed record FinancialStatementDrilldownJournalLineDto(
    Guid LedgerEntryLineId,
    Guid FinanceAccountId,
    string AccountCode,
    string AccountName,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal ContributionAmount,
    string Currency,
    string? Description);

public sealed record FinancialStatementDrilldownJournalEntryDto(
    Guid LedgerEntryId,
    string EntryNumber,
    DateTime EntryUtc,
    string? Description,
    decimal TotalContributionAmount,
    IReadOnlyList<FinancialStatementDrilldownJournalLineDto> Lines);

public sealed record FinancialStatementDrilldownDto(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string FiscalPeriodName,
    string StatementType,
    string SourceMode,
    FinancialStatementSnapshotMetadataDto? Snapshot,
    FinancialStatementDrilldownLineDto SelectedLine,
    decimal OpeningBalanceAdjustment,
    decimal JournalLineTotal,
    decimal ReconciliationTotal,
    decimal ReconciliationDelta,
    IReadOnlyList<FinancialStatementDrilldownJournalEntryDto> JournalEntries);

public sealed record FinanceForecastDto(
    Guid Id,
    Guid CompanyId,
    Guid FinanceAccountId,
    string AccountCode,
    string AccountName,
    DateTime PeriodStartUtc,
    string Version,
    Guid? CostCenterId,
    decimal Amount,
    string Currency,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public interface IReportingPeriodCloseService
{
    Task<ReportingPeriodCloseValidationResultDto> ValidateAsync(
        ValidateReportingPeriodCloseQuery query,
        CancellationToken cancellationToken);

    Task<ReportingPeriodLockStateDto> LockAsync(
        LockReportingPeriodCommand command,
        CancellationToken cancellationToken);

    Task<ReportingPeriodLockStateDto> UnlockAsync(
        UnlockReportingPeriodCommand command,
        CancellationToken cancellationToken);

    Task<ReportingPeriodLockStateDto> CloseAndLockAsync(
        CloseAndLockReportingPeriodCommand command,
        CancellationToken cancellationToken);

    Task<ReportingPeriodLockStateDto> ReopenAsync(
        ReopenReportingPeriodCommand command,
        CancellationToken cancellationToken);

    Task<ReportingPeriodRegenerationRequestResultDto> RegenerateStoredStatementsAsync(
        RegenerateStoredReportingStatementsCommand command,
        CancellationToken cancellationToken);

    Task<int> RunBackgroundRegenerationAsync(
        Guid companyId,
        Guid fiscalPeriodId,
        string? correlationId,
        CancellationToken cancellationToken);
}

public interface IFinanceCashPositionWorkflowService
{
    Task<FinanceCashPositionDto> EvaluateAsync(EvaluateFinanceCashPositionWorkflowCommand command, CancellationToken cancellationToken);
}

public interface IFinanceSummaryQueryService
{
    Task<FinanceSummaryDto> GetAsync(
        GetFinanceSummaryQuery query,
        CancellationToken cancellationToken);
}
