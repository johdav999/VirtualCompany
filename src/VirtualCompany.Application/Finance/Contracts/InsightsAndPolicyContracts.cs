using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

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

public sealed record GetNormalizedFinanceInsightsQuery(
    Guid CompanyId,
    string? EntityType = null,
    string? EntityId = null,
    string? Status = null,
    string? Severity = null,
    string? CheckCode = null,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null,
    DateTime? UpdatedFromUtc = null,
    DateTime? UpdatedToUtc = null,
    string SortBy = FinanceInsightSortFields.UpdatedAt,
    string SortDirection = FinanceInsightSortDirections.Desc);

public sealed record GetFinanceInsightsQuery(
    Guid CompanyId,
    DateTime? AsOfUtc = null,
    int ExpenseWindowDays = 90,
    int TrendWindowDays = 30,
    int PayableWindowDays = 14,
    string? EntityType = null,
    string? EntityId = null,
    bool IncludeResolved = true,
    bool PreferSnapshot = true);

public sealed record RefreshFinanceInsightsSnapshotCommand(
    Guid CompanyId,
    DateTime? AsOfUtc = null,
    int ExpenseWindowDays = 90,
    int TrendWindowDays = 30,
    int PayableWindowDays = 14,
    string SnapshotKey = FinanceInsightSnapshotKeys.Default,
    TimeSpan? Retention = null,
    string? CorrelationId = null);

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

public static class FinanceInsightSortFields
{
    public const string CreatedAt = "createdAt";
    public const string UpdatedAt = "updatedAt";

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? UpdatedAt
            : value.Trim() switch
            {
                CreatedAt => CreatedAt,
                UpdatedAt => UpdatedAt,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, "SortBy must be createdAt or updatedAt.")
            };
}

public static class FinanceInsightSortDirections
{
    public const string Asc = "asc";
    public const string Desc = "desc";
}

public sealed record GetFinancePolicyConfigurationQuery(
    Guid CompanyId);

public sealed record UpsertFinancePolicyConfigurationCommand(
    Guid CompanyId,
    FinancePolicyConfigurationDto Configuration);

public sealed record FinanceInsightsDto(
    Guid CompanyId,
    DateTime GeneratedAt,
    bool FromSnapshot,
    DateTime? SnapshotExpiresAtUtc,
    IReadOnlyList<FinanceInsightDto> Items);

public sealed record FinanceInsightDto(
    Guid Id,
    string CheckCode,
    string CheckName,
    string ConditionKey,
    string Severity,
    string Message,
    string Recommendation,
    string Status,
    decimal Confidence,
    FinanceInsightEntityReferenceDto? PrimaryEntity,
    IReadOnlyList<FinanceInsightEntityReferenceDto> AffectedEntities,
    string EntityType,
    string EntityId,
    DateTime ObservedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ResolvedAt,
    string? MetadataJson);

public sealed record NormalizedFinanceInsightsDto(
    Guid CompanyId,
    IReadOnlyList<NormalizedFinanceInsightDto> Items);

public sealed record NormalizedFinanceInsightDto(
    Guid Id,
    string Severity,
    string Message,
    string Recommendation,
    FinanceInsightEntityReferenceDto EntityReference,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string CheckCode,
    string CheckName,
    string ConditionKey,
    IReadOnlyList<FinanceInsightEntityReferenceDto> AffectedEntities,
    DateTime ObservedAt,
    DateTime? ResolvedAt);

public sealed record FinanceInsightEntityReferenceDto(
    string EntityType,
    string EntityId,
    string? DisplayName = null,
    bool IsPrimary = false);

public sealed record FinancialCheckContext(
    Guid CompanyId,
    DateTime AsOfUtc,
    int ExpenseWindowDays,
    int TrendWindowDays,
    int PayableWindowDays);

public sealed record FinancialCheckDefinition(
    string Code,
    string Name,
    string EntityScope);

public static class FinancialCheckDefinitions
{
    public static readonly FinancialCheckDefinition CashRisk = new("cash_risk", "Cash risk", "company");
    public static readonly FinancialCheckDefinition TransactionAnomaly = new("transaction_anomaly", "Transaction anomaly", "finance_transaction");
    public static readonly FinancialCheckDefinition OverdueReceivables = new("overdue_receivables", "Overdue receivables", "counterparty");
    public static readonly FinancialCheckDefinition PayablesPressure = new("payables_pressure", "Payables pressure", "counterparty");
    public static readonly FinancialCheckDefinition SupplierBillDueMonitoring = new("supplier_bill_due_monitoring", "Supplier bill due monitoring", "bill");
    public static readonly FinancialCheckDefinition TopExpenseConcentration = new("top_expense_concentration", "Top expense concentration", "company");
    public static readonly FinancialCheckDefinition RevenueTrend = new("revenue_trend", "Revenue trend", "company");
    public static readonly FinancialCheckDefinition BurnRunwayRisk = new("burn_runway_risk", "Burn runway risk", "company");
    public static readonly FinancialCheckDefinition OverdueCustomerConcentration = new("overdue_customer_concentration", "Overdue customer concentration", "counterparty");
    public static readonly FinancialCheckDefinition NearTermLiquidityPressure = new("near_term_liquidity_pressure", "Near-term liquidity pressure", "company");
    public static readonly FinancialCheckDefinition ApprovalNeededFinanceEvents = new("approval_needed_finance_events", "Approval-needed finance events", "approval_task");
    public static readonly FinancialCheckDefinition ThresholdBreachFinanceEvents = new("threshold_breach_finance_events", "Threshold-breach finance events", "company");
    public static readonly FinancialCheckDefinition SummaryConsistencyAnomaly = new("summary_consistency_anomaly", "Summary consistency anomaly", "company");
    public static readonly FinancialCheckDefinition SparseDataCoverage = new("sparse_data_coverage", "Sparse data coverage", "company");
    public static readonly FinancialCheckDefinition ForecastGap = new("forecast_gap", "Forecast gap", "company");
    public static readonly FinancialCheckDefinition BudgetGap = new("budget_gap", "Budget gap", "company");

    private static readonly IReadOnlyDictionary<string, FinancialCheckDefinition> ByCode =
        new ReadOnlyDictionary<string, FinancialCheckDefinition>(
            new Dictionary<string, FinancialCheckDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                [CashRisk.Code] = CashRisk,
                [TransactionAnomaly.Code] = TransactionAnomaly,
                [OverdueReceivables.Code] = OverdueReceivables,
                [PayablesPressure.Code] = PayablesPressure,
                [SupplierBillDueMonitoring.Code] = SupplierBillDueMonitoring,
                [TopExpenseConcentration.Code] = TopExpenseConcentration,
                [RevenueTrend.Code] = RevenueTrend,
                [BurnRunwayRisk.Code] = BurnRunwayRisk,
                [OverdueCustomerConcentration.Code] = OverdueCustomerConcentration,
                [NearTermLiquidityPressure.Code] = NearTermLiquidityPressure,
                [ApprovalNeededFinanceEvents.Code] = ApprovalNeededFinanceEvents,
                [ThresholdBreachFinanceEvents.Code] = ThresholdBreachFinanceEvents,
                [SummaryConsistencyAnomaly.Code] = SummaryConsistencyAnomaly,
                [SparseDataCoverage.Code] = SparseDataCoverage,
                [ForecastGap.Code] = ForecastGap,
                [BudgetGap.Code] = BudgetGap
            });

    public static FinancialCheckDefinition Resolve(string checkCode)
    {
        if (string.IsNullOrWhiteSpace(checkCode))
        {
            throw new ArgumentException("Check code is required.", nameof(checkCode));
        }

        var normalizedCode = checkCode.Trim().ToLowerInvariant();
        return ByCode.TryGetValue(normalizedCode, out var definition)
            ? definition
            : new FinancialCheckDefinition(
                normalizedCode,
                normalizedCode.Replace("_", " ", StringComparison.Ordinal),
                "mixed");
    }
}

public sealed record FinancialCheckResult(
    FinancialCheckDefinition Definition,
    string ConditionKey,
    string EntityType,
    string EntityId,
    FinancialCheckSeverity Severity,
    string Message,
    string Recommendation,
    decimal Confidence,
    FinanceInsightEntityReferenceDto? PrimaryEntity,
    IReadOnlyList<FinanceInsightEntityReferenceDto> AffectedEntities,
    bool IsActive = true,
    DateTime? ObservedAtUtc = null,
    string? MetadataJson = null)
{
    public string CheckCode => Definition.Code;
    public string CheckName => Definition.Name;
    public string InsightKey => ConditionKey;
}

public interface IFinancialCheck
{
    FinancialCheckDefinition Definition { get; }
    string CheckCode { get; }
    Task<IReadOnlyList<FinancialCheckResult>> ExecuteAsync(FinancialCheckContext context, CancellationToken cancellationToken);
}

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

public sealed record EvaluateFinanceTransactionAnomalyCommand(
    Guid CompanyId,
    Guid TransactionId,
    Guid? WorkflowInstanceId = null,
    Guid? AgentId = null);

public sealed record FinanceAnomalySchedule(
    bool IsAnomalyDay,
    int? AnomalyIndex,
    int TargetTransactionIndex);

public interface IFinanceAnomalyScheduleFactory
{
    FinanceAnomalySchedule Create(FinanceDeterministicGenerationContext context, int anomalyCount, int transactionCount, int anomalyCadenceDays, int anomalyOffsetDays);
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

public interface IFinanceTransactionAnomalyDetectionService
{
    Task<FinanceTransactionAnomalyEvaluationDto> EvaluateAsync(EvaluateFinanceTransactionAnomalyCommand command, CancellationToken cancellationToken);
}

public interface IFinanceAgentInsightRepository
{
    Task<FinanceAgentInsight?> GetByIdentityAsync(
        Guid companyId,
        string checkCode,
        string conditionKey,
        string entityType,
        string entityId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceAgentInsight>> ListByCheckCodesAsync(
        Guid companyId,
        IReadOnlyList<string> checkCodes,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceAgentInsight>> ListAsync(
        Guid companyId,
        bool includeResolved,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceAgentInsight>> QueryAsync(
        GetNormalizedFinanceInsightsQuery query,
        CancellationToken cancellationToken);

    Task AddAsync(FinanceAgentInsight insight, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IFinanceInsightPersistenceService
{
    Task ReconcileAsync(
        FinancialCheckContext context,
        IReadOnlyList<string> executedCheckCodes,
        IReadOnlyList<FinancialCheckResult> currentResults,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceInsightDto>> ListAsync(
        Guid companyId,
        string? entityType,
        string? entityId,
        bool includeResolved,
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

public interface IFinanceInsightsSnapshotJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

