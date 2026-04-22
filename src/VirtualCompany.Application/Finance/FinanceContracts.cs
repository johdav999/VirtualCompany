using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;

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
public sealed record UnlockReportingPeriodCommand(Guid CompanyId, Guid FiscalPeriodId);
public sealed record RegenerateStoredReportingStatementsCommand(
    Guid CompanyId,
    Guid FiscalPeriodId,
    bool RunInBackground = false);

public sealed record ReportingPeriodBlockingIssueDto(
    string Code,
    string Message,
    int Count,
    IReadOnlyList<string> SampleReferences);

public sealed record GetFinanceCashPositionQuery(
    Guid CompanyId,
    DateTime? AsOfUtc = null,
    decimal? AverageMonthlyBurn = null,
    int BurnLookbackDays = 90);

public sealed record GetFinanceExpenseBreakdownQuery(
    Guid CompanyId,
    DateTime StartUtc,
    DateTime EndUtc);

public sealed record GetFinanceTransactionsQuery(
    Guid CompanyId,
    DateTime? StartUtc = null,
    DateTime? EndUtc = null,
    int Limit = 100,
    string? Category = null,
    string? FlaggedState = null);

public sealed record GetFinanceTransactionDetailQuery(
    Guid CompanyId,
    Guid TransactionId);

public sealed record GetFinanceInvoicesQuery(
    Guid CompanyId,
    DateTime? StartUtc = null,
    DateTime? EndUtc = null,
    int Limit = 100);

public sealed record GetFinanceCounterpartiesQuery(
    Guid CompanyId,
    string CounterpartyType,
    DateTime? EndUtc = null,
    int Limit = 100);

public sealed record GetFinanceInvoiceDetailQuery(
    Guid CompanyId,
    Guid InvoiceId);

public sealed record GetFinanceCounterpartyQuery(
    Guid CompanyId,
    Guid CounterpartyId,
    string CounterpartyType);

public sealed record CreateFinanceCounterpartyCommand(
    Guid CompanyId,
    string CounterpartyType,
    FinanceCounterpartyUpsertDto Counterparty);

public sealed record UpdateFinanceCounterpartyCommand(
    Guid CompanyId,
    Guid CounterpartyId,
    string CounterpartyType,
    FinanceCounterpartyUpsertDto Counterparty);

public sealed record GetFinanceSeedAnomaliesQuery(
    Guid CompanyId,
    string? AnomalyType = null,
    Guid? AffectedRecordId = null,
    int Limit = 100);

public sealed record GetFinanceSeedAnomalyByIdQuery(
    Guid CompanyId,
    Guid AnomalyId);

public sealed record GetFinanceAnomalyWorkbenchQuery(
    Guid CompanyId,
    string? AnomalyType = null,
    string? Status = null,
    decimal? ConfidenceMin = null,
    decimal? ConfidenceMax = null,
    string? Supplier = null,
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null,
    int Page = 1,
    int PageSize = 50);

public sealed record GetFinanceAnomalyDetailQuery(
    Guid CompanyId,
    Guid AnomalyId);

public sealed record GetFinanceBillsQuery(
    Guid CompanyId,
    DateTime? StartUtc = null,
    DateTime? EndUtc = null,
    int Limit = 100);

public sealed record GetFinanceBalancesQuery(
    Guid CompanyId,
    DateTime? AsOfUtc = null);

public sealed record GetFinanceSummaryQuery(
    Guid CompanyId,
    DateTime? AsOfUtc = null,
    int RecentAssetPurchaseLimit = 5,
    bool IncludeConsistencyCheck = false);
public sealed record GetFinanceInsightsQuery(
    Guid CompanyId,
    DateTime? AsOfUtc = null,
    int ExpenseWindowDays = 90,
    int TrendWindowDays = 30,
    int PayableWindowDays = 14,
    bool PreferSnapshot = true);

public sealed record RefreshFinanceInsightsSnapshotCommand(
    Guid CompanyId,
    DateTime? AsOfUtc = null,
    int ExpenseWindowDays = 90,
    int TrendWindowDays = 30,
    int PayableWindowDays = 14,
    string SnapshotKey = FinanceInsightSnapshotKeys.Default,
    TimeSpan? Retention = null);

public sealed record QueueFinanceInsightsSnapshotRefreshCommand(
    Guid CompanyId,
    DateTime? AsOfUtc = null,
    int ExpenseWindowDays = 90,
    int TrendWindowDays = 30,
    int PayableWindowDays = 14,
    string SnapshotKey = FinanceInsightSnapshotKeys.Default,
    int RetentionMinutes = 360,
    bool ResetAttempts = false,
    string? CorrelationId = null);

public static class FinanceInsightSnapshotKeys
{
    public const string Default = "default";

    public static string Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? Default : value.Trim().ToLowerInvariant();
}

public sealed record GetFinanceAgentQueryQuery(
    Guid CompanyId,
    string QueryText,
    DateTime? AsOfUtc = null);

public sealed record UpdateFinanceInvoiceApprovalStatusCommand(
    Guid CompanyId,
    Guid InvoiceId,
    string Status);

public sealed record UpdateFinanceTransactionCategoryCommand(
    Guid CompanyId,
    Guid TransactionId,
    string Category);

public sealed record GetFinancePolicyConfigurationQuery(
    Guid CompanyId);

public sealed record UpsertFinancePolicyConfigurationCommand(
    Guid CompanyId,
    FinancePolicyConfigurationDto Configuration);

public sealed record EnsureFinanceApprovalTaskCommand(
    Guid CompanyId,
    ApprovalTargetType TargetType,
    Guid TargetId,
    decimal Amount,
    string Currency,
    DateTime? DueDateUtc = null);

public sealed record GetPendingFinanceApprovalTasksQuery(Guid CompanyId);

public sealed record BackfillFinanceApprovalTasksCommand(
    Guid CompanyId,
    int BatchSize = 250,
    string? CorrelationId = null,
    bool IncludePayments = true);

public sealed record RerunFinanceBootstrapCommand(
    Guid CompanyId,
    bool RerunPlanningBackfill = true,
    bool RerunApprovalBackfill = true,
    int BatchSize = 250,
    string? CorrelationId = null);

public sealed record ActOnFinanceApprovalTaskCommand(
    Guid CompanyId,
    Guid ApprovalTaskId,
    ApprovalTaskStatus Action,
    string? Comment = null);

public sealed record FinanceApprovalTaskAssigneeDto(Guid? UserId, string? DisplayName);
public sealed record FinancePendingApprovalTaskDto(Guid Id, string TargetType, Guid TargetId, FinanceApprovalTaskAssigneeDto? Assignee, DateTime? DueDateUtc, string Status);
public sealed record FinanceApprovalTaskBackfillResultDto(
    Guid CompanyId,
    string CorrelationId,
    int ScannedCount,
    int MatchedCount,
    int CreatedCount,
    int SkippedExistingCount,
    int BillScannedCount,
    int PaymentScannedCount,
    int BillCreatedCount,
    int PaymentCreatedCount);
public sealed record FinanceBootstrapRerunResultDto(
    Guid CompanyId,
    string CorrelationId,
    FinanceSeedingState SeedState,
    bool PlanningBackfillRan,
    bool ApprovalBackfillRan,
    int PlanningRowsInserted,
    FinanceApprovalTaskBackfillResultDto ApprovalBackfill,
    DateTime CompletedAtUtc,
    string Summary);

public sealed record FinanceInsightsDto(
    Guid CompanyId,
    DateTime GeneratedAt,
    string Currency,
    string Headline,
    string Summary,
    string? DataCoverageNote,
    bool FromSnapshot,
    DateTime? SnapshotExpiresAtUtc,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<FinanceNarrativeHintDto> NarrativeHints,
    FinanceTopExpensesInsightDto TopExpenses,
    FinanceRevenueTrendInsightDto RevenueTrend,
    FinanceBurnRateInsightDto BurnRate,
    FinanceOverdueCustomerRiskInsightDto OverdueCustomerRisk,
    FinancePayablePressureInsightDto PayablePressure);

public sealed record FinanceNarrativeHintDto(
    string Section,
    string Tone,
    string Summary,
    string SuggestedPromptFragment);

public sealed record FinanceInsightsSnapshotRefreshResultDto(
    Guid CompanyId,
    string SnapshotKey,
    string CacheKey,
    DateTime RequestedAtUtc,
    string CorrelationId,
    bool Queued,
    bool Refreshed,
    DateTime? ExpiresAtUtc,
    FinanceInsightsDto? Insights);

public sealed record FinanceTopExpensesInsightDto(
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    decimal TotalExpenses,
    string Currency,
    string TrendLabel,
    string Summary,
    IReadOnlyList<FinanceTopExpenseItemDto> Items);

public sealed record FinanceTopExpenseItemDto(
    string Label,
    decimal Amount,
    string Currency,
    int EntryCount,
    decimal ShareOfExpenses,
    string Narrative);

public sealed record FinanceRevenueTrendInsightDto(
    DateTime CurrentPeriodStartUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime PreviousPeriodStartUtc,
    DateTime PreviousPeriodEndUtc,
    decimal CurrentRevenue,
    decimal PreviousRevenue,
    decimal DeltaAmount,
    decimal? DeltaPercent,
    string DirectionLabel,
    string Summary);

public sealed record FinanceBurnRateInsightDto(
    int LookbackDays,
    decimal AverageDailyBurn,
    decimal AverageMonthlyBurn,
    decimal NetMonthlyBurn,
    decimal AvailableCash,
    int? EstimatedRunwayDays,
    string RiskLabel,
    string Summary);

public sealed record FinanceOverdueCustomerRiskInsightDto(
    int OverdueInvoiceCount,
    int OverdueCustomerCount,
    decimal TotalOverdueAmount,
    int MaxDaysOverdue,
    decimal LargestCustomerConcentrationRatio,
    string RiskLabel,
    string Summary,
    IReadOnlyList<FinanceOverdueCustomerRiskItemDto> Customers);

public sealed record FinanceOverdueCustomerRiskItemDto(
    Guid CounterpartyId,
    string CustomerName,
    decimal OverdueAmount,
    int OverdueInvoiceCount,
    int MaxDaysOverdue,
    decimal ConcentrationRatio,
    string RiskLabel,
    string Summary);

public sealed record FinancePayablePressureInsightDto(
    decimal OverdueAmount,
    decimal DueSoonAmount,
    int OverdueBillCount,
    int DueSoonBillCount,
    decimal? UpcomingBurdenRatioOfCash,
    string RiskLabel,
    string Summary,
    IReadOnlyList<FinancePayablePressureItemDto> Suppliers);

public sealed record FinancePayablePressureItemDto(
    Guid CounterpartyId,
    string SupplierName,
    decimal DueAmount,
    int DueBillCount,
    bool HasOverdueBalance,
    int MaxUrgencyDays,
    string RiskLabel,
    string Summary);

public sealed record FinanceBootstrapRerunRequestDto(
    bool RerunPlanningBackfill = true,
    bool RerunApprovalBackfill = true,
    int BatchSize = 250,
    string? CorrelationId = null);
public sealed record ReviewFinanceInvoiceWorkflowCommand(
    Guid CompanyId,
    Guid InvoiceId,
    Guid? WorkflowInstanceId,
    Guid? AgentId,
    Dictionary<string, JsonNode?>? Payload);

public sealed record EvaluateFinanceTransactionAnomalyCommand(
    Guid CompanyId,
    Guid TransactionId,
    Guid? WorkflowInstanceId = null,
    Guid? AgentId = null);

public sealed record EvaluateFinanceCashPositionWorkflowCommand(
    Guid CompanyId,
    Guid? WorkflowInstanceId = null,
    Guid? AgentId = null);

public sealed record FinanceSeedBootstrapCommand(
    Guid CompanyId,
    int SeedValue,
    DateTime? SeedAnchorUtc = null,
    bool ReplaceExisting = true,
    bool InjectAnomalies = false,
    string? AnomalyScenarioProfile = null);

public sealed record GetFinanceEntryStateQuery(
    Guid CompanyId,
    bool RetryOnFailure = false,
    bool ForceSeed = false,
    string Source = FinanceEntrySources.FinanceEntry,
    string SeedMode = FinanceSeedRequestModes.Replace,
    bool ConfirmReplace = false);

public sealed record GetCompanySimulationClockQuery(
    Guid CompanyId);

public sealed record AdvanceCompanySimulationTimeCommand(
    Guid CompanyId,
    int TotalHours,
    int? ExecutionStepHours = null,
    bool Accelerated = false);

public sealed record RunScheduledCompanySimulationCommand(
    Guid CompanyId);

public sealed record CompanySimulationClockDto(
    Guid CompanyId,
    DateTime CurrentUtc,
    bool Enabled,
    bool AutoAdvanceEnabled,
    int DefaultStepHours,
    int AutoAdvanceIntervalSeconds,
    DateTime? LastAdvancedUtc);

public sealed record GenerateCompanySimulationFinanceCommand(
    Guid CompanyId,
    Guid ActiveSessionId,
    DateTime StartSimulatedUtc,
    DateTime PreviousSimulatedUtc,
    DateTime CurrentSimulatedUtc,
    int Seed,
    string? DeterministicConfigurationJson);

public sealed record FinancePlanningEntryUpsertDto(
    Guid FinanceAccountId,
    DateTime PeriodStartUtc,
    string Version,
    decimal Amount,
    string? Currency = null,
    Guid? CostCenterId = null);

public sealed record CreateFinanceBudgetCommand(
    Guid CompanyId,
    FinancePlanningEntryUpsertDto Budget);

public sealed record UpdateFinanceBudgetCommand(
    Guid CompanyId,
    Guid BudgetId,
    FinancePlanningEntryUpsertDto Budget);

public sealed record GetFinanceBudgetsQuery(
    Guid CompanyId,
    DateTime PeriodStartUtc,
    DateTime? PeriodEndUtc = null,
    string? Version = null,
    Guid? FinanceAccountId = null,
    Guid? CostCenterId = null);

public sealed record CompanySimulationFinanceGenerationDayLogDto(
    DateTime SimulatedDateUtc,
    int TransactionsCreated,
    int InvoicesCreated,
    int BillsCreated,
    int AssetPurchasesCreated,
    int RecurringExpenseInstancesCreated,
    int AlertsCreated,
    IReadOnlyList<string> InjectedAnomalies,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    public int GeneratedRecordCount =>
        TransactionsCreated + InvoicesCreated + BillsCreated + AssetPurchasesCreated + RecurringExpenseInstancesCreated;
}

public sealed record CompanySimulationFinanceGenerationResultDto(
    Guid CompanyId,
    Guid ActiveSessionId,
    int DaysProcessed,
    int InvoicesCreated,
    int BillsCreated,
    int TransactionsCreated,
    int BalancesCreated,
    int AssetPurchasesCreated,
    int RecurringExpenseInstancesCreated,
    int WorkflowTasksCreated,
    int ApprovalRequestsCreated,
    int AuditEventsCreated,
    int ActivityEventsCreated,
    int AlertsCreated,
    IReadOnlyList<CompanySimulationFinanceGenerationDayLogDto>? DailyLogs = null);

public sealed record FinanceDeterministicGenerationContext(
    Guid CompanyId,
    int Seed,
    DateTime StartSimulatedUtc,
    DateTime SimulatedDateUtc,
    int DayIndex,
    string? DeterministicConfigurationJson);

public sealed record FinanceScenarioSelection(
    int InvoiceScenarioIndex,
    int ThresholdCaseIndex,
    int CustomerIndex,
    int SupplierIndex);

public sealed record FinanceAnomalySchedule(
    bool IsAnomalyDay,
    int? AnomalyIndex,
    int TargetTransactionIndex);

public sealed record GetFinanceForecastsQuery(
    Guid CompanyId,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    Guid? FinanceAccountId = null,
    string? Version = null);

public sealed record GetFinanceVarianceQuery(
    Guid CompanyId,
    DateTime PeriodStartUtc,
    string ComparisonType,
    DateTime? PeriodEndUtc = null,
    string? Version = null,
    Guid? FinanceAccountId = null,
    Guid? CostCenterId = null);
public interface IFinanceGenerationPolicy
{
    Task<CompanySimulationFinanceGenerationResultDto> GenerateAsync(
        GenerateCompanySimulationFinanceCommand command,
        CancellationToken cancellationToken);
}

public interface IFinanceDeterministicValueSource
{
    int GetCycleOffset(
        Guid companyId,
        int seed,
        DateTime startSimulatedUtc,
        string? deterministicConfigurationJson,
        string scope,
        int modulo);

    int GetDayValue(
        Guid companyId,
        int seed,
        DateTime startSimulatedUtc,
        DateTime simulatedDateUtc,
        int dayIndex,
        string? deterministicConfigurationJson,
        string scope,
        int modulo);
}

public interface IFinanceScenarioFactory
{
    FinanceScenarioSelection Create(FinanceDeterministicGenerationContext context, int invoiceScenarioCount, int thresholdCaseCount, int customerCount, int supplierCount);
}

public interface IFinanceAnomalyScheduleFactory
{
    FinanceAnomalySchedule Create(FinanceDeterministicGenerationContext context, int anomalyCount, int transactionCount, int anomalyCadenceDays, int anomalyOffsetDays);
}

public sealed record StartCompanySimulationStateCommand(
    Guid CompanyId,
    DateTime StartSimulatedUtc,
    bool GenerationEnabled,
    int Seed,
    string? DeterministicConfigurationJson = null,
    Guid? SessionId = null,
    DateTime? TransitionedUtc = null);

public sealed record SaveCompanySimulationStoppedDraftCommand(
    Guid CompanyId,
    DateTime ReferenceSimulatedUtc,
    bool GenerationEnabled,
    int Seed,
    string? DeterministicConfigurationJson = null,
    DateTime? UpdatedUtc = null);

public sealed record UpdateCompanySimulationStateCommand(
    Guid CompanyId,
    DateTime CurrentSimulatedUtc,
    DateTime? LastProgressedUtc = null,
    bool? GenerationEnabled = null,
    string? DeterministicConfigurationJson = null,
    DateTime? UpdatedUtc = null,
    DateTime? ExpectedCurrentSimulatedUtc = null,
    DateTime? ExpectedLastProgressedUtc = null);

public sealed record ProgressCompanySimulationStateResult(
    CompanySimulationState? State,
    bool Applied);

public sealed record PauseCompanySimulationStateCommand(
    Guid CompanyId,
    DateTime? PausedUtc = null);

public sealed record ResumeCompanySimulationStateCommand(
    Guid CompanyId,
    DateTime? ResumedUtc = null);

public sealed record StopCompanySimulationStateCommand(
    Guid CompanyId,
    DateTime? StoppedUtc = null);

public interface ICompanySimulationStateRepository
{
    Task<CompanySimulationState?> GetCurrentAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CompanySimulationState?> GetByActiveSessionAsync(Guid companyId, Guid activeSessionId, CancellationToken cancellationToken);
    Task<CompanySimulationState> StartAsync(StartCompanySimulationStateCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationState> SaveStoppedDraftAsync(SaveCompanySimulationStoppedDraftCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationState> UpdateAsync(UpdateCompanySimulationStateCommand command, CancellationToken cancellationToken);
    Task<ProgressCompanySimulationStateResult> TryProgressAsync(UpdateCompanySimulationStateCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationState> PauseAsync(PauseCompanySimulationStateCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationState> ResumeAsync(ResumeCompanySimulationStateCommand command, CancellationToken cancellationToken);
    Task<CompanySimulationState> StopAsync(StopCompanySimulationStateCommand command, CancellationToken cancellationToken);
}

public sealed record SimulationExecutionLogDto(
    Guid CompanyId,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    int TransactionsGenerated,
    int InvoicesGenerated,
    int BillsGenerated,
    int RecurringExpenseInstancesGenerated,
    int EventsEmitted);

public sealed record AdvanceCompanySimulationTimeResultDto(
    Guid CompanyId,
    DateTime PreviousUtc,
    DateTime CurrentUtc,
    int TotalHoursProcessed,
    int ExecutionStepHours,
    int TransactionsGenerated,
    int InvoicesGenerated,
    int BillsGenerated,
    int RecurringExpenseInstancesGenerated,
    int EventsEmitted,
    IReadOnlyList<SimulationExecutionLogDto> Logs);

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
    FinanceSummaryConsistencyResultDto? ConsistencyCheck = null);

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

public sealed record FinanceStatementLineDto(
    Guid? AccountId,
    string AccountCode,
    string AccountName,
    string ReportSection,
    string LineClassification,
    decimal Amount,
    string Currency);

public sealed record ProfitAndLossReportDto(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string FiscalPeriodName,
    DateTime StartUtc,
    DateTime EndUtc,
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
    DateTime StartUtc,
    DateTime EndUtc,
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

public sealed record FinancialStatementSnapshotMetadataDto(
    Guid SnapshotId,
    int VersionNumber,
    string BalancesChecksum,
    DateTime GeneratedAtUtc,
    DateTime SourcePeriodStartUtc,
    DateTime SourcePeriodEndUtc,
    string Currency);

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

public sealed record FinancialStatementDrilldownLineDto(
    string LineCode,
    string LineName,
    string ReportSection,
    string LineClassification,
    decimal Amount,
    string Currency);

public sealed record FinancialStatementDrilldownJournalEntryDto(
    Guid EntryId,
    string EntryNumber,
    DateTime EntryUtc,
    string? Description,
    decimal TotalContributionAmount,
    IReadOnlyList<FinancialStatementDrilldownJournalLineDto> Lines);

public sealed record FinancialStatementDrilldownJournalLineDto(
    Guid JournalLineId,
    Guid FinanceAccountId,
    string AccountCode,
    string AccountName,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal ContributionAmount,
    string Currency,
    string? Description);

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

public sealed record ReportingPeriodCloseValidationResultDto(
    Guid CompanyId,
    Guid FiscalPeriodId,
    string FiscalPeriodName,
    DateTime ExecutedAtUtc,
    string ExecutedByActorType,
    Guid? ExecutedByActorId,
    Guid ExecutedByMembershipId,
    string ExecutedByMembershipRole,
    bool IsReadyToClose,
    bool IsClosed,
    bool IsReportingLocked,
    IReadOnlyList<ReportingPeriodBlockingIssueDto> BlockingIssues);

public sealed record ReportingPeriodRegenerationRequestResultDto(
    Guid CompanyId,
    Guid FiscalPeriodId,
    bool Queued,
    Guid? BackgroundExecutionId,
    int SnapshotCount,
    string Status,
    DateTime RequestedAtUtc,
    DateTime? CompletedAtUtc,
    string RequestedByActorType,
    Guid? RequestedByActorId,
    ReportingPeriodLockStateDto LockState);

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

public sealed record FinanceWorkflowOutputSchemaDto(
    string Classification,
    string RiskLevel,
    string RecommendedAction,
    string Rationale,
    decimal Confidence,
    string SourceWorkflow);

public static class FinanceWorkflowOutputSchemas
{
    public static FinanceWorkflowOutputSchemaDto Create(
        string classification,
        string riskLevel,
        string recommendedAction,
        string rationale,
        decimal confidence,
        string sourceWorkflow) =>
        new(
            Required(classification, nameof(classification)),
            Required(riskLevel, nameof(riskLevel)).ToLowerInvariant(),
            Required(recommendedAction, nameof(recommendedAction)),
            Required(rationale, nameof(rationale)),
            Math.Clamp(confidence, 0m, 1m),
            Required(sourceWorkflow, nameof(sourceWorkflow)));

    public static JsonObject ToJsonObject(FinanceWorkflowOutputSchemaDto output) =>
        new()
        {
            ["classification"] = JsonValue.Create(output.Classification),
            ["riskLevel"] = JsonValue.Create(output.RiskLevel),
            ["recommendedAction"] = JsonValue.Create(output.RecommendedAction),
            ["rationale"] = JsonValue.Create(output.Rationale),
            ["confidence"] = JsonValue.Create(output.Confidence),
            ["sourceWorkflow"] = JsonValue.Create(output.SourceWorkflow)
        };

    public static void CopyToPayload(
        IDictionary<string, JsonNode?> payload,
        FinanceWorkflowOutputSchemaDto output,
        bool includeWorkflowOutputNode = true)
    {
        payload["classification"] = JsonValue.Create(output.Classification);
        payload["riskLevel"] = JsonValue.Create(output.RiskLevel);
        payload["recommendedAction"] = JsonValue.Create(output.RecommendedAction);
        payload["rationale"] = JsonValue.Create(output.Rationale);
        payload["confidence"] = JsonValue.Create(output.Confidence);
        payload["sourceWorkflow"] = JsonValue.Create(output.SourceWorkflow);

        if (includeWorkflowOutputNode)
        {
            payload["workflowOutput"] = ToJsonObject(output);
        }
    }

    private static string Required(string value, string parameterName)
    {
        var normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? throw new ArgumentException("Finance workflow output schema fields are required.", parameterName)
            : normalized;
    }
}

public sealed record FinanceCashPositionAlertStateDto(
    bool IsLowCash,
    string RiskLevel,
    bool AlertCreated,
    bool AlertDeduplicated,
    Guid? AlertId,
    string? AlertStatus,
    string Rationale);

public sealed record FinanceCashPositionThresholdsDto(
    int WarningRunwayDays,
    int CriticalRunwayDays,
    decimal? WarningCashAmount,
    decimal? CriticalCashAmount,
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

public static class FinanceAgentQueryIntents
{
    public const string WhatShouldIPayThisWeek = "what_should_i_pay_this_week";
    public const string WhichCustomersAreOverdue = "which_customers_are_overdue";
    public const string WhyIsCashDownThisMonth = "why_is_cash_down_this_month";
}

public static class FinanceAgentQueryRouting
{
    public const string WhatShouldIPayThisWeekPhrase = "what should i pay this week";
    public const string WhichCustomersAreOverduePhrase = "which customers are overdue";
    public const string WhyIsCashDownThisMonthPhrase = "why is cash down this month";

    public static IReadOnlyList<string> SupportedPhrases { get; } =
    [
        WhatShouldIPayThisWeekPhrase,
        WhichCustomersAreOverduePhrase,
        WhyIsCashDownThisMonthPhrase
    ];

    public static string NormalizeQueryText(string? value) =>
        string.Join(
            ' ',
            (value ?? string.Empty)
                .Trim()
                .Replace("?", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant()
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    public static bool TryResolveIntent(string? queryText, out string intent)
    {
        intent = NormalizeQueryText(queryText) switch
        {
            WhatShouldIPayThisWeekPhrase => FinanceAgentQueryIntents.WhatShouldIPayThisWeek,
            WhichCustomersAreOverduePhrase => FinanceAgentQueryIntents.WhichCustomersAreOverdue,
            WhyIsCashDownThisMonthPhrase => FinanceAgentQueryIntents.WhyIsCashDownThisMonth,
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(intent);
    }
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

public sealed record FinanceAgentQueryItemDto(
    Guid? RecordId,
    string RecordType,
    Guid? CounterpartyId,
    string? CounterpartyName,
    string? Reference,
    DateTime? DueUtc,
    decimal Amount,
    string Currency,
    string Reason,
    int SortOrder,
    int? DaysOverdue,
    string? AgingBucket,
    IReadOnlyList<Guid> SourceRecordIds,
    IReadOnlyList<FinanceAgentMetricComponentDto> MetricComponents);

public sealed record FinanceAgentQueryResultDto(
    Guid CompanyId,
    string Intent,
    string QueryText,
    string Summary,
    string Currency,
    DateTime AsOfUtc,
    FinanceAgentQueryPeriodDto Period,
    IReadOnlyList<FinanceAgentQueryItemDto> Items,
    IReadOnlyList<FinanceAgentMetricComponentDto> MetricComponents,
    IReadOnlyList<Guid> SourceRecordIds);

public sealed record FinanceTransactionDto(
    Guid Id,
    Guid AccountId,
    string AccountName,
    Guid? CounterpartyId,
    string? CounterpartyName,
    Guid? InvoiceId,
    Guid? BillId,
    DateTime TransactionUtc,
    string TransactionType,
    decimal Amount,
    string Currency,
    string Description,
    string ExternalReference,
    FinanceLinkedDocumentDto? LinkedDocument,
    bool IsFlagged = false,
    string AnomalyState = "clear");

public sealed record FinanceLinkedDocumentDto(
    Guid Id,
    string Title,
    string? OriginalFileName,
    string ContentType);

public sealed record FinanceInvoiceDto(
    Guid Id,
    Guid CounterpartyId,
    string CounterpartyName,
    string InvoiceNumber,
    DateTime IssuedUtc,
    DateTime DueUtc,
    decimal Amount,
    string Currency,
    string Status,
    FinanceLinkedDocumentDto? LinkedDocument);

public sealed record FinanceBillDto(
    Guid Id,
    Guid CounterpartyId,
    string CounterpartyName,
    string BillNumber,
    DateTime ReceivedUtc,
    DateTime DueUtc,
    decimal Amount,
    string Currency,
    string Status,
    FinanceLinkedDocumentDto? LinkedDocument);

public sealed record FinanceCounterpartyDto(
    Guid Id,
    Guid CompanyId,
    string CounterpartyType,
    string Name,
    string? Email,
    string? PaymentTerms,
    string? TaxId,
    decimal? CreditLimit,
    string? PreferredPaymentMethod,
    string? DefaultAccountMapping,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record FinanceCounterpartyUpsertDto(
    string Name,
    string? Email,
    string? PaymentTerms,
    string? TaxId,
    decimal? CreditLimit,
    string? PreferredPaymentMethod,
    string? DefaultAccountMapping);

public sealed record FinanceTransactionDetailDto(
    Guid Id,
    Guid AccountId,
    string AccountName,
    Guid? CounterpartyId,
    string? CounterpartyName,
    Guid? InvoiceId,
    Guid? BillId,
    DateTime TransactionUtc,
    string Category,
    decimal Amount,
    string Currency,
    string Description,
    string ExternalReference,
    bool IsFlagged,
    string AnomalyState,
    IReadOnlyList<string> Flags,
    FinanceActionPermissionsDto Permissions,
    FinanceLinkedDocumentAccessDto LinkedDocument);

public sealed record FinanceActionPermissionsDto(
    bool CanChangeTransactionCategory,
    bool CanChangeInvoiceApprovalStatus,
    bool CanManagePolicies);

public sealed record FinanceLinkedDocumentAccessDto(
    string AccessState,
    string Message,
    bool CanOpen,
    FinanceLinkedDocumentDto? Document);

public sealed record FinanceInvoiceWorkflowContextDto(
    Guid? WorkflowInstanceId,
    Guid TaskId,
    string WorkflowName,
    string ReviewTaskStatus,
    Guid? ApprovalRequestId,
    string Classification,
    string RiskLevel,
    string RecommendedAction,
    string Rationale,
    decimal Confidence,
    bool RequiresHumanApproval,
    string? ApprovalStatus = null,
    string? ApprovalAssigneeSummary = null,
    bool CanNavigateToWorkflow = false,
    bool CanNavigateToApproval = false);

public sealed record FinanceInvoiceDetailDto(
    Guid Id,
    Guid CounterpartyId,
    string CounterpartyName,
    string InvoiceNumber,
    DateTime IssuedUtc,
    DateTime DueUtc,
    decimal Amount,
    string Currency,
    string Status,
    FinanceInvoiceWorkflowContextDto? WorkflowContext,
    FinanceActionPermissionsDto Permissions,
    FinanceLinkedDocumentAccessDto LinkedDocument);

public sealed record FinancePolicyConfigurationDto(
    Guid CompanyId,
    string ApprovalCurrency,
    decimal InvoiceApprovalThreshold,
    decimal BillApprovalThreshold,
    bool RequireCounterpartyForTransactions,
    decimal AnomalyDetectionLowerBound,
    decimal AnomalyDetectionUpperBound,
    int CashRunwayWarningThresholdDays,
    int CashRunwayCriticalThresholdDays);

public sealed record FinanceTransactionCategoryRecommendationDto(
    Guid TransactionId,
    string RecommendedCategory,
    decimal Confidence);

public sealed record FinanceInvoiceApprovalRecommendationDto(
    Guid InvoiceId,
    string RecommendedStatus,
    decimal Confidence);

public sealed record FinanceInvoiceReviewWorkflowResultDto(
    Guid CompanyId,
    Guid InvoiceId,
    Guid? WorkflowInstanceId,
    Guid TaskId,
    Guid? ApprovalRequestId,
    string InvoiceClassification,
    string RiskLevel,
    string RecommendedAction,
    string Rationale,
    decimal ConfidenceScore,
    bool RequiresHumanApproval,
    FinanceInvoiceDto Invoice,
    FinancePolicyConfigurationDto Policy,
    Dictionary<string, JsonNode?> OutputPayload)
{
    public string ReviewTaskStatus { get; init; } = "new";
    public DateTime LastUpdatedUtc { get; init; } = DateTime.UtcNow;
    public FinanceWorkflowOutputSchemaDto WorkflowOutput { get; init; } =
        FinanceWorkflowOutputSchemas.Create("invoice_review", "low", "no_action", "No workflow output was recorded.", 0m, "invoice_review");
}

public sealed record FinanceTransactionAnomalyEvaluationDto(
    Guid CompanyId,
    Guid TransactionId,
    DateTime EvaluatedUtc,
    bool IsAnomalous,
    IReadOnlyList<FinanceTransactionAnomalyDto> Anomalies);

public sealed record FinanceTransactionAnomalyDto(
    string AnomalyType,
    string Explanation,
    decimal Confidence,
    string RecommendedAction,
    Guid AlertId,
    Guid FollowUpTaskId,
    bool AlertCreated,
    bool AlertDeduplicated)
{
    public FinanceWorkflowOutputSchemaDto WorkflowOutput { get; init; } =
        FinanceWorkflowOutputSchemas.Create(AnomalyType, "medium", RecommendedAction, Explanation, Confidence, "transaction_anomaly_detection");
}

public sealed record FinanceTransactionHistoricalBaselineDto(
    int SampleSize,
    decimal AverageAbsoluteAmount,
    decimal MaximumAbsoluteAmount,
    DateTime? EarliestTransactionUtc,
    DateTime? LatestTransactionUtc);

public sealed record FinanceSeedBootstrapResultDto(
    Guid CompanyId,
    int SeedValue,
    DateTime WindowStartUtc,
    DateTime WindowEndUtc,
    int AccountCount,
    int CounterpartyCount,
    int SupplierCount,
    int CategoryCount,
    int InvoiceCount,
    int BillCount,
    int RecurringExpenseCount,
    int TransactionCount,
    int BalanceCount,
    int PaymentCount,
    int DocumentCount,
    Guid PolicyConfigurationId,
    IReadOnlyList<FinanceSeedRecurringExpenseDto> RecurringExpenses,
    IReadOnlyList<FinanceSeedValidationErrorDto> ValidationErrors,
    IReadOnlyList<FinanceSeedAnomalyDto> Anomalies);

public sealed record FinanceBudgetDto(
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

public sealed record FinanceVarianceResultDto(
    Guid CompanyId,
    string ComparisonType,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    string? Version,
    bool IncludesCostCenters,
    IReadOnlyList<FinanceVarianceRowDto> Rows);

public sealed record FinanceVarianceRowDto(
    DateTime PeriodStartUtc,
    Guid FinanceAccountId,
    string AccountCode,
    string AccountName,
    string CategoryKey,
    string CategoryName,
    Guid? CostCenterId,
    string? CostCenterCode,
    string? CostCenterName,
    decimal ActualAmount,
    decimal ComparisonAmount,
    decimal VarianceAmount,
    decimal? VariancePercentage,
    string Currency);

public static class FinanceVarianceComparisonTypes
{
    public const string Budget = "budget";
    public const string Forecast = "forecast";
    public const string ActualVsBudget = "actual_vs_budget";
    public const string ActualVsForecast = "actual_vs_forecast";

    public static string Normalize(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Comparison type is required.", nameof(value))
            : value.Trim().Replace("-", "_", StringComparison.Ordinal).ToLowerInvariant() switch
            {
                Budget or ActualVsBudget => Budget,
                Forecast or ActualVsForecast => Forecast,
                _ => throw new ArgumentOutOfRangeException(nameof(value), "Comparison type must be budget or forecast.")
            };
}

public sealed record FinanceSeedAnomalyDto(
    Guid Id,
    string AnomalyType,
    string ScenarioProfile,
    IReadOnlyList<Guid> AffectedRecordIds,
    string ExpectedDetectionMetadataJson);

public sealed record FinanceAnomalyDeduplicationDto(
    string? Key,
    DateTime? WindowStartUtc,
    DateTime? WindowEndUtc);

public sealed record FinanceAnomalyFollowUpTaskDto(
    Guid Id,
    string Title,
    string Status,
    DateTime CreatedUtc,
    DateTime? DueUtc,
    DateTime UpdatedUtc);

public sealed record FinanceAnomalyRelatedRecordDto(
    Guid Id,
    string Reference,
    DateTime OccurredAtUtc,
    decimal Amount,
    string Currency,
    string? SupplierName);

public sealed record FinanceAnomalyRecordLinkDto(
    Guid? RecordId,
    string RecordType,
    string Reference,
    DateTime? OccurredAtUtc,
    decimal? Amount,
    string? Currency);

public sealed record FinanceAnomalyWorkbenchItemDto(
    Guid Id,
    string AnomalyType,
    string Status,
    decimal Confidence,
    string? SupplierName,
    Guid? AffectedRecordId,
    string AffectedRecordReference,
    string ExplanationSummary,
    string RecommendedAction,
    DateTime DetectedAtUtc,
    FinanceAnomalyDeduplicationDto? Deduplication,
    Guid? FollowUpTaskId,
    string? FollowUpTaskStatus,
    Guid? RelatedInvoiceId,
    Guid? RelatedBillId);

public sealed record FinanceAnomalyWorkbenchResultDto(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<FinanceAnomalyWorkbenchItemDto> Items);

public sealed record FinanceAnomalyDetailDto(
    Guid Id,
    string AnomalyType,
    string Status,
    decimal Confidence,
    string? SupplierName,
    string Explanation,
    string RecommendedAction,
    DateTime DetectedAtUtc,
    FinanceAnomalyDeduplicationDto? Deduplication,
    FinanceAnomalyRelatedRecordDto? AffectedRecord,
    Guid? RelatedInvoiceId,
    string? RelatedInvoiceReference,
    Guid? RelatedBillId,
    string? RelatedBillReference,
    IReadOnlyList<FinanceAnomalyRecordLinkDto> RelatedRecordLinks,
    IReadOnlyList<FinanceAnomalyFollowUpTaskDto> FollowUpTasks);

public sealed record FinanceSeedRecurringExpenseDto(
    Guid Id,
    Guid SupplierId,
    string CategoryId,
    string Name,
    decimal Amount,
    string Currency,
    string Cadence,
    int DayOfPeriod);

public sealed record FinanceSeedValidationErrorDto(
    string Code,
    string Message);

public sealed record FinanceSeedingStateDiagnosticsDto(
    bool MetadataPresent,
    FinanceSeedingState? PersistedState,
    FinanceSeedingState? MetadataState,
    bool MetadataIndicatesComplete,
    bool UsedFastPath,
    string Reason,
    bool HasAccounts,
    bool HasCounterparties,
    bool HasTransactions,
    bool HasBalances,
    bool HasPolicyConfiguration,
    bool HasInvoices,
    bool HasBills);

public static class FinanceSeedingStateDerivedFromValues
{
    public const string Metadata = "metadata";
    public const string RecordChecks = "record_checks";
}

public sealed record FinanceSeedingStateResultDto(
    Guid CompanyId,
    FinanceSeedingState State,
    string DerivedFrom,
    DateTime CheckedAtUtc,
    FinanceSeedingStateDiagnosticsDto Diagnostics);

public sealed record GetFinanceSeedBackfillRunsQuery(
    int Limit = 20);

public sealed record FinanceSeedBackfillRunDto(
    Guid RunId,
    FinanceSeedBackfillRunStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    int ScannedCount,
    int QueuedCount,
    int SucceededCount,
    int SkippedCount,
    int FailedCount,
    string ConfigurationSnapshotJson,
    string? ErrorDetails);

public sealed record FinanceSeedBackfillAttemptDto(
    Guid AttemptId,
    Guid RunId,
    Guid CompanyId,
    FinanceSeedBackfillAttemptStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? SkipReason,
    string? ErrorCode,
    string? ErrorDetails,
    int RetryCount,
    Guid? BackgroundExecutionId,
    string? IdempotencyKey,
    FinanceSeedingState SeedStateBefore,
    FinanceSeedingState? SeedStateAfter);


public sealed record FinanceEntryStateDto(
    Guid CompanyId,
    string InitializationStatus,
    string ProgressState,
    FinanceSeedingState SeedingState,
    bool SeedJobEnqueued,
    bool SeedJobActive,
    bool CanRetry,
    bool CanRefresh,
    string Message,
    DateTime CheckedAtUtc,
    DateTime? SeededAtUtc,
    DateTime? LastAttemptedUtc,
    DateTime? LastCompletedUtc,
    string? LastErrorCode,
    string? LastErrorMessage,
    string SeedMode,
    string SeedOperation,
    bool DataAlreadyExists,
    bool ConfirmationRequired,
    bool FallbackTriggered,
    string? StatusEndpoint,
    string? SeedEndpoint,
    string? JobStatus,
    string? IdempotencyKey,
    string? ConfirmationMessage,
    bool CanGenerate,
    string RecommendedAction,
    IReadOnlyList<string> SupportedModes,
    string? CorrelationId);

public interface IFinanceSeedBootstrapService
{
    Task<FinanceSeedBootstrapResultDto> GenerateAsync(FinanceSeedBootstrapCommand command, CancellationToken cancellationToken);
}

public interface IFinanceTransactionAnomalyDetectionService
{
    Task<FinanceTransactionAnomalyEvaluationDto> EvaluateAsync(EvaluateFinanceTransactionAnomalyCommand command, CancellationToken cancellationToken);
}

public interface IFinanceSeedingStateService
{
    Task<FinanceSeedingStateResultDto> GetCompanyFinanceSeedingStateAsync(Guid companyId, CancellationToken cancellationToken = default);
}

public interface IFinanceSeedingStateResolver : IFinanceSeedingStateService
{
    Task<FinanceSeedingStateResultDto> ResolveAsync(Guid companyId, CancellationToken cancellationToken = default);
}

public interface IFinanceSeedBackfillOrchestrator
{
    Task<FinanceSeedBackfillRunDto> RunAsync(CancellationToken cancellationToken);
}

public interface IFinanceSeedBackfillQueryService
{
    Task<IReadOnlyList<FinanceSeedBackfillRunDto>> GetRecentRunsAsync(
        GetFinanceSeedBackfillRunsQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceSeedBackfillAttemptDto>> GetAttemptsAsync(Guid runId, CancellationToken cancellationToken);
}

public sealed class FinanceNotInitializedException : InvalidOperationException
{
    public FinanceNotInitializedException(Guid companyId, string message, string domain = "finance", bool canTriggerSeed = true)
        : base(message)
    {
        CompanyId = companyId;
        Domain = domain;
        CanTriggerSeed = canTriggerSeed;
    }

    public Guid CompanyId { get; }
    public string Domain { get; }
    public bool CanTriggerSeed { get; }
}

public sealed class FinanceValidationException : Exception
{
    public FinanceValidationException(IDictionary<string, string[]> errors, string? message = null)
        : base(string.IsNullOrWhiteSpace(message) ? "Finance validation failed." : message)
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public class ReportingPeriodOperationException : InvalidOperationException
{
    public ReportingPeriodOperationException(string code, string title, string message)
        : base(message)
    {
        Code = code;
        Title = title;
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
            $"Fiscal period '{fiscalPeriodName}' is locked for stored reporting regeneration.")
    {
        FiscalPeriodId = fiscalPeriodId;
        FiscalPeriodName = fiscalPeriodName;
    }

    public Guid FiscalPeriodId { get; }
    public string FiscalPeriodName { get; }
}

public static class ReportingPeriodErrorCodes
{
    public const string ReportingPeriodLocked = "reporting_period_locked";
    public const string ReportingPeriodNotClosed = "reporting_period_not_closed";
    public const string ReportingPeriodInvalidState = "reporting_period_invalid_state";
}

public static class ReportingPeriodBlockingIssueCodes
{
    public const string UnpostedSourceDocuments = "unposted_source_documents";
    public const string UnbalancedJournalEntries = "unbalanced_journal_entries";
    public const string MissingStatementMappings = "missing_statement_mappings";
}

public interface IReportingPeriodCloseService
{
    Task<ReportingPeriodCloseValidationResultDto> ValidateAsync(ValidateReportingPeriodCloseQuery query, CancellationToken cancellationToken);
    Task<ReportingPeriodLockStateDto> LockAsync(LockReportingPeriodCommand command, CancellationToken cancellationToken);
    Task<ReportingPeriodLockStateDto> UnlockAsync(UnlockReportingPeriodCommand command, CancellationToken cancellationToken);
    Task<ReportingPeriodRegenerationRequestResultDto> RegenerateStoredStatementsAsync(RegenerateStoredReportingStatementsCommand command, CancellationToken cancellationToken);
    Task<int> RunBackgroundRegenerationAsync(Guid companyId, Guid fiscalPeriodId, string? correlationId, CancellationToken cancellationToken);
}

public interface IReportingPeriodRegenerationJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public interface IFinanceApprovalTaskBackfillJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public interface IFinanceEntryService
{
    Task<FinanceEntryStateDto> GetEntryStateAsync(GetFinanceEntryStateQuery query, CancellationToken cancellationToken);
    Task<FinanceEntryStateDto> RequestEntryStateAsync(GetFinanceEntryStateQuery query, CancellationToken cancellationToken);
}

public static class FinanceSeedTelemetryEventNames
{
    public const string Requested = "finance_auto_seed_requested";
    public const string Started = "finance_auto_seed_started";
    public const string Completed = "finance_auto_seed_completed";
    public const string Failed = "finance_auto_seed_failed";
}

public sealed record FinanceSeedTelemetryContext(
    Guid CompanyId,
    Guid JobId,
    string CorrelationId,
    string IdempotencyKey,
    string TriggerSource,
    FinanceSeedingState SeedStateBefore,
    FinanceSeedingState SeedStateAfter,
    Guid? UserId = null,
    bool JobAlreadyRunning = false,
    int? Attempt = null,
    int? MaxAttempts = null,
    long? DurationMs = null,
    string? ErrorType = null,
    string? ErrorMessageSafe = null,
    string SeedMode = FinanceSeedRequestModes.Replace,
    string ActorType = AuditActorTypes.System,
    Guid? ActorId = null);

public interface IFinanceCashPositionWorkflowService
{
    Task<FinanceCashPositionDto> EvaluateAsync(EvaluateFinanceCashPositionWorkflowCommand command, CancellationToken cancellationToken);
}

public interface IInvoiceReviewWorkflowService
{
    Task<FinanceInvoiceReviewWorkflowResultDto> ExecuteAsync(ReviewFinanceInvoiceWorkflowCommand command, CancellationToken cancellationToken);
    Task<FinanceInvoiceReviewWorkflowResultDto?> GetLatestByInvoiceAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken);
}

public interface IFinanceSeedTelemetry
{
    Task TrackAsync(string eventName, FinanceSeedTelemetryContext context, CancellationToken cancellationToken = default);
}

public interface IFinanceToolProvider
{
    Task<FinanceCashBalanceDto> GetCashBalanceAsync(GetFinanceCashBalanceQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceTransactionDto>> GetTransactionsAsync(GetFinanceTransactionsQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceInvoiceDto>> GetInvoicesAsync(GetFinanceInvoicesQuery query, CancellationToken cancellationToken);

    Task<FinanceMonthlyProfitAndLossDto> GetMonthlyProfitAndLossAsync(GetFinanceMonthlyProfitAndLossQuery query, CancellationToken cancellationToken);

    Task<FinanceExpenseBreakdownDto> GetExpenseBreakdownAsync(GetFinanceExpenseBreakdownQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceBillDto>> GetBillsAsync(GetFinanceBillsQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceAccountBalanceDto>> GetBalancesAsync(GetFinanceBalancesQuery query, CancellationToken cancellationToken);

    Task<FinanceAgentQueryResultDto> ResolveAgentQueryAsync(GetFinanceAgentQueryQuery query, CancellationToken cancellationToken);

    Task<FinanceTransactionCategoryRecommendationDto> RecommendTransactionCategoryAsync(InternalToolExecutionRequest request, CancellationToken cancellationToken);

    Task<FinanceInvoiceApprovalRecommendationDto> RecommendInvoiceApprovalDecisionAsync(InternalToolExecutionRequest request, CancellationToken cancellationToken);

    Task<FinanceTransactionDto> UpdateTransactionCategoryAsync(UpdateFinanceTransactionCategoryCommand command, CancellationToken cancellationToken);

    Task<FinanceInvoiceDto> UpdateInvoiceApprovalStatusAsync(UpdateFinanceInvoiceApprovalStatusCommand command, CancellationToken cancellationToken);
}

public interface IFinanceCommandService
{
    Task<FinanceInvoiceDto> UpdateInvoiceApprovalStatusAsync(
        UpdateFinanceInvoiceApprovalStatusCommand command,
        CancellationToken cancellationToken);

    Task<FinanceTransactionDto> UpdateTransactionCategoryAsync(
        UpdateFinanceTransactionCategoryCommand command,
        CancellationToken cancellationToken);

    Task<FinanceCounterpartyDto> CreateCounterpartyAsync(
        CreateFinanceCounterpartyCommand command,
        CancellationToken cancellationToken);

    Task<FinanceCounterpartyDto> UpdateCounterpartyAsync(
        UpdateFinanceCounterpartyCommand command,
        CancellationToken cancellationToken);

    Task<FinanceBudgetDto> CreateBudgetAsync(
        CreateFinanceBudgetCommand command,
        CancellationToken cancellationToken);

    Task<FinanceBudgetDto> UpdateBudgetAsync(
        UpdateFinanceBudgetCommand command,
        CancellationToken cancellationToken);
}

public interface IFinanceReadService
{
    Task<FinanceCashBalanceDto> GetCashBalanceAsync(
        GetFinanceCashBalanceQuery query,
        CancellationToken cancellationToken);

    Task<FinanceCashPositionDto> GetCashPositionAsync(
        GetFinanceCashPositionQuery query,
        CancellationToken cancellationToken);

    Task<ProfitAndLossReportDto> GetProfitAndLossReportAsync(
        GetFinanceProfitAndLossReportQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinancialStatementSnapshotSummaryDto>> ListFinancialStatementSnapshotsAsync(
        ListFinancialStatementSnapshotsQuery query,
        CancellationToken cancellationToken);

    Task<FinancialStatementSnapshotDetailDto?> GetFinancialStatementSnapshotAsync(
        GetFinancialStatementSnapshotQuery query,
        CancellationToken cancellationToken);

    Task<FinancialStatementDrilldownDto> GetFinancialStatementDrilldownAsync(
        GetFinancialStatementDrilldownQuery query,
        CancellationToken cancellationToken);

    Task<BalanceSheetReportDto> GetBalanceSheetReportAsync(
        GetFinanceBalanceSheetReportQuery query,
        CancellationToken cancellationToken);

    Task<FinanceMonthlyProfitAndLossDto> GetMonthlyProfitAndLossAsync(
        GetFinanceMonthlyProfitAndLossQuery query,
        CancellationToken cancellationToken);

    Task<FinanceExpenseBreakdownDto> GetExpenseBreakdownAsync(
        GetFinanceExpenseBreakdownQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceTransactionDto>> GetTransactionsAsync(
        GetFinanceTransactionsQuery query,
        CancellationToken cancellationToken);

    Task<FinanceTransactionDetailDto?> GetTransactionDetailAsync(
        GetFinanceTransactionDetailQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceInvoiceDto>> GetInvoicesAsync(
        GetFinanceInvoicesQuery query,
        CancellationToken cancellationToken);

    Task<FinanceInvoiceDetailDto?> GetInvoiceDetailAsync(
        GetFinanceInvoiceDetailQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceCounterpartyDto>> GetCounterpartiesAsync(
        GetFinanceCounterpartiesQuery query,
        CancellationToken cancellationToken);

    Task<FinanceCounterpartyDto?> GetCounterpartyAsync(
        GetFinanceCounterpartyQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceSeedAnomalyDto>> GetSeedAnomaliesAsync(
        GetFinanceSeedAnomaliesQuery query,
        CancellationToken cancellationToken);

    Task<FinanceSeedAnomalyDto?> GetSeedAnomalyByIdAsync(
        GetFinanceSeedAnomalyByIdQuery query,
        CancellationToken cancellationToken);

    Task<FinanceAnomalyWorkbenchResultDto> GetAnomalyWorkbenchAsync(
        GetFinanceAnomalyWorkbenchQuery query,
        CancellationToken cancellationToken);

    Task<FinanceAnomalyDetailDto?> GetAnomalyDetailAsync(
        GetFinanceAnomalyDetailQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceBillDto>> GetBillsAsync(
        GetFinanceBillsQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceAccountBalanceDto>> GetBalancesAsync(
        GetFinanceBalancesQuery query,
        CancellationToken cancellationToken);

    Task<FinanceInsightsDto> GetInsightsAsync(
        GetFinanceInsightsQuery query,
        CancellationToken cancellationToken);

    Task<FinanceInsightsSnapshotRefreshResultDto> RefreshInsightsSnapshotAsync(
        RefreshFinanceInsightsSnapshotCommand command,
        CancellationToken cancellationToken);

    Task<FinanceInsightsSnapshotRefreshResultDto> QueueInsightsSnapshotRefreshAsync(
        QueueFinanceInsightsSnapshotRefreshCommand command,
        CancellationToken cancellationToken);

    Task<FinanceAgentQueryResultDto> ResolveAgentQueryAsync(
        GetFinanceAgentQueryQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceBudgetDto>> GetBudgetsAsync(
        GetFinanceBudgetsQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceForecastDto>> GetForecastsAsync(
        GetFinanceForecastsQuery query,
        CancellationToken cancellationToken);

    Task<FinanceVarianceResultDto> GetVarianceAsync(
        GetFinanceVarianceQuery query,
        CancellationToken cancellationToken);
}

public interface IFinanceSummaryQueryService
{
    Task<FinanceSummaryDto> GetAsync(
        GetFinanceSummaryQuery query,
        CancellationToken cancellationToken);
}

public interface IFinancePolicyConfigurationService
{
    Task<FinancePolicyConfigurationDto> GetPolicyConfigurationAsync(
        GetFinancePolicyConfigurationQuery query,
        CancellationToken cancellationToken);

    Task<FinancePolicyConfigurationDto> UpsertPolicyConfigurationAsync(
        UpsertFinancePolicyConfigurationCommand command,
        CancellationToken cancellationToken);
}

public interface IFinanceApprovalTaskService
{
    Task<bool> EnsureTaskAsync(EnsureFinanceApprovalTaskCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinancePendingApprovalTaskDto>> GetPendingTasksAsync(GetPendingFinanceApprovalTasksQuery query, CancellationToken cancellationToken);
    Task<FinanceApprovalTaskBackfillResultDto> BackfillApprovalTasksAsync(BackfillFinanceApprovalTasksCommand command, CancellationToken cancellationToken);
    Task<FinancePendingApprovalTaskDto> ActOnTaskAsync(ActOnFinanceApprovalTaskCommand command, CancellationToken cancellationToken);
}

public interface IFinanceBootstrapRerunService
{
    Task<FinanceBootstrapRerunResultDto> RerunAsync(
        RerunFinanceBootstrapCommand command,
        CancellationToken cancellationToken);
}

public interface IFinanceInsightsSnapshotJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public interface IPlanningBaselineService
{
    Task<int> EnsureBaselineAsync(Guid companyId, CancellationToken cancellationToken);
    Task<int> BackfillAllCompaniesAsync(CancellationToken cancellationToken);
}

public interface ICompanySimulationService
{
    Task<CompanySimulationClockDto> GetClockAsync(
        GetCompanySimulationClockQuery query,
        CancellationToken cancellationToken);

    Task<AdvanceCompanySimulationTimeResultDto> AdvanceAsync(
        AdvanceCompanySimulationTimeCommand command,
        CancellationToken cancellationToken);

    Task<AdvanceCompanySimulationTimeResultDto?> RunScheduledAdvanceAsync(
        RunScheduledCompanySimulationCommand command,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}

public static class FinanceEntryInitializationStates
{
    public const string Ready = "ready";
    public const string Initializing = "initializing";
    public const string Failed = "failed";
}

public static class FinanceEntryProgressStates
{
    public const string NotSeeded = "not_seeded";
    public const string SeedingRequested = "seeding_requested";
    public const string InProgress = "in_progress";
    public const string Seeded = "seeded";
    public const string Failed = "failed";
}

public static class FinanceSeedRequestModes
{
    public const string Replace = "replace";
    public const string Append = "append";

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();

    public static bool IsSupported(string? value)
    {
        var normalized = Normalize(value);
        return string.Equals(normalized, Replace, StringComparison.Ordinal) ||
               string.Equals(normalized, Append, StringComparison.Ordinal);
    }
}

public static class FinanceEntrySources
{
    public const string FinanceEntry = "finance_entry";
    public const string FinanceEntryRetry = "finance_entry_retry";
    public const string ManualSeed = "finance_manual_seed";
    public const string FallbackRead = "finance_fallback_seed";
    public const string Backfill = "finance_seed_backfill";
}
