using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

public sealed record GetFinanceTransactionsQuery(
    Guid CompanyId,
    DateTime? StartUtc = null,
    DateTime? EndUtc = null,
    int Limit = 100,
    string? Category = null,
    string? FlaggedState = null,
    string SourceFilter = FinanceDataSources.All);

public static class FinanceDataSources
{
    public const string All = "all";
    public const string Fortnox = "fortnox";
    public const string Simulation = "simulation";
    public const string Manual = "manual";

    public static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? All : value.Trim().ToLowerInvariant();
}

public sealed record GetFinanceTransactionDetailQuery(
    Guid CompanyId,
    Guid TransactionId);

public sealed record GetFinanceCounterpartiesQuery(
    Guid CompanyId,
    string CounterpartyType,
    DateTime? EndUtc = null,
    int Limit = 100);

public sealed record GetFinanceAnalyticsQuery(
    Guid CompanyId,
    DateTime? AsOfUtc = null,
    int ExpenseWindowDays = 90,
    int TrendWindowDays = 30,
    int PayableWindowDays = 14,
    int RecentAssetPurchaseLimit = 5,
    bool IncludeConsistencyCheck = true,
    bool RefreshInsightsSnapshot = false);

public sealed record GetFinanceAgentQueryQuery(
    Guid CompanyId,
    string QueryText,
    DateTime? AsOfUtc = null);

public sealed record UpdateFinanceTransactionCategoryCommand(
    Guid CompanyId,
    Guid TransactionId,
    string Category);

public sealed record EnsureFinanceApprovalTaskCommand(
    Guid CompanyId,
    ApprovalTargetType TargetType,
    Guid TargetId,
    decimal Amount,
    string Currency,
    DateTime? DueDateUtc = null,
    string? CorrelationId = null,
    string? TriggerEventId = null,
    string? SourceEntityVersion = null);

public sealed record GetPendingFinanceApprovalTasksQuery(Guid CompanyId);

public sealed record BackfillFinanceApprovalTasksCommand(
    Guid CompanyId,
    int BatchSize = 250,
    string? CorrelationId = null,
    bool IncludePayments = true);

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

public sealed record FinanceNarrativeHintDto(
    string Section,
    string Tone,
    string Summary,
    string SuggestedPromptFragment);

public sealed record FinanceAnalyticsNarrativeDto(
    string Headline,
    string Summary,
    string? CoverageNote,
    IReadOnlyList<string> Highlights,
    IReadOnlyList<FinanceNarrativeHintDto> NarrativeHints,
    FinanceTopExpensesInsightDto TopExpenses,
    FinanceRevenueTrendInsightDto RevenueTrend,
    FinanceBurnRateInsightDto BurnRate,
    FinanceOverdueCustomerRiskInsightDto OverdueCustomerRisk,
    FinancePayablePressureInsightDto PayablePressure);

public sealed record FinancePlanningAnalyticsDto(
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc,
    bool HasBudgets,
    int BudgetCount,
    IReadOnlyList<string> BudgetVersions,
    bool HasForecasts,
    int ForecastCount,
    IReadOnlyList<string> ForecastVersions,
    FinanceVarianceResultDto? ActualVsBudgetVariance,
    FinanceVarianceResultDto? ActualVsForecastVariance);

public sealed record FinanceAnalyticsDto(
    Guid CompanyId,
    DateTime AsOfUtc,
    FinanceInsightsDto OperationalInsights,
    FinanceCashPositionDto CashPosition,
    FinanceSummaryDto Summary,
    FinanceAnalyticsNarrativeDto Narrative,
    FinancePlanningAnalyticsDto Planning,
    FinanceStatementAnalyticsDto Statements);

public sealed record GetFinanceEntryStateQuery(
    Guid CompanyId,
    bool RetryOnFailure = false,
    bool ForceSeed = false,
    string Source = FinanceEntrySources.FinanceEntry,
    string SeedMode = FinanceSeedRequestModes.Replace,
    bool ConfirmReplace = false);

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

public sealed record FinanceScenarioSelection(
    int InvoiceScenarioIndex,
    int ThresholdCaseIndex,
    int CustomerIndex,
    int SupplierIndex);

public sealed record GetFinanceVarianceQuery(
    Guid CompanyId,
    DateTime PeriodStartUtc,
    string ComparisonType,
    DateTime? PeriodEndUtc = null,
    string? Version = null,
    Guid? FinanceAccountId = null,
    Guid? CostCenterId = null);

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

public sealed record FinanceIntelligenceSnapshotDto(
    DateTime AsOfUtc,
    FinanceCashProjectionDto SevenDayProjection,
    FinanceCashProjectionDto ThirtyDayProjection,
    FinanceObligationCoverageDto ObligationCoverage,
    IReadOnlyList<FinanceOverdueInvoiceRecommendationDto> OverdueInvoices,
    IReadOnlyList<FinanceDueSoonBillRecommendationDto> DueSoonBills);

public sealed record FinanceObligationCoverageDto(
    int HorizonDays,
    decimal AvailableCash,
    decimal NearTermObligations,
    decimal CoverageRatio,
    string Severity,
    string RecommendationCode,
    string RecommendationText);

public sealed record FinanceIntelligenceInputDto(
    Guid CompanyId,
    DateTime AsOfUtc,
    decimal CurrentCash,
    string Currency,
    IReadOnlyList<FinanceOpenReceivableItemDto> OpenInvoices,
    IReadOnlyList<FinanceOpenPayableItemDto> OpenBills,
    IReadOnlyList<FinanceRecurringOutflowItemDto> RecurringOutflows,
    IReadOnlyList<FinanceHistoricalReceivablePaymentDto>? HistoricalReceivablePayments = null);

public sealed record FinanceRecurringOutflowItemDto(
    string Name,
    DateTime DueUtc,
    decimal Amount,
    string Currency,
    string Cadence = "scheduled",
    string? Reference = null);

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
    string AnomalyState = "clear",
    string Source = FinanceDataSources.Simulation);

public sealed record FinanceLinkedDocumentDto(
    Guid Id,
    string Title,
    string? OriginalFileName,
    string ContentType);

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
    FinanceLinkedDocumentAccessDto LinkedDocument,
    FinanceTransactionPaymentContextDto? PaymentContext = null);

public sealed record FinanceActionPermissionsDto(
    bool CanChangeTransactionCategory,
    bool CanChangeInvoiceApprovalStatus,
    bool CanManagePolicies);

public sealed record FinanceLinkedDocumentAccessDto(
    string AccessState,
    string Message,
    bool CanOpen,
    FinanceLinkedDocumentDto? Document);

public sealed record FinanceTransactionCategoryRecommendationDto(
    Guid TransactionId,
    string RecommendedCategory,
    decimal Confidence);

public sealed record FinanceTransactionHistoricalBaselineDto(
    int SampleSize,
    decimal AverageAbsoluteAmount,
    decimal MaximumAbsoluteAmount,
    DateTime? EarliestTransactionUtc,
    DateTime? LatestTransactionUtc);

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

public sealed class FinanceValidationException : Exception
{
    public FinanceValidationException(IReadOnlyDictionary<string, string[]> errors, string? message = null)
        : base(string.IsNullOrWhiteSpace(message) ? "Finance validation failed." : message)
    {
        var normalizedErrors = errors is null
            ? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            : errors.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase);

        Errors = new ReadOnlyDictionary<string, string[]>(
            normalizedErrors);
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class FinanceNotInitializedException : Exception
{
    public FinanceNotInitializedException(
        Guid companyId,
        string message,
        string domain = FinanceInitializationDomainValues.Finance,
        bool canTriggerSeed = true)
        : base(message)
    {
        CompanyId = companyId;
        Domain = string.IsNullOrWhiteSpace(domain) ? FinanceInitializationDomainValues.Finance : domain;
        CanTriggerSeed = canTriggerSeed;
    }

    public Guid CompanyId { get; }

    public string Domain { get; }

    public bool CanTriggerSeed { get; }
}

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

public interface IFinanceEntryService
{
    Task<FinanceEntryStateDto> GetEntryStateAsync(GetFinanceEntryStateQuery query, CancellationToken cancellationToken);
    Task<FinanceEntryStateDto> RequestEntryStateAsync(GetFinanceEntryStateQuery query, CancellationToken cancellationToken);
}

public interface IFinanceApprovalTaskBackfillJobRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
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

    Task<FinanceBillDetailDto?> GetBillDetailAsync(
        GetFinanceBillDetailQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceBillDto>> GetBillsAsync(
        GetFinanceBillsQuery query,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceAccountBalanceDto>> GetBalancesAsync(
        GetFinanceBalancesQuery query,
        CancellationToken cancellationToken);

    Task<NormalizedFinanceInsightsDto> GetNormalizedInsightsAsync(
        GetNormalizedFinanceInsightsQuery query,
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

    Task<FinanceAnalyticsDto> GetAnalyticsAsync(
        GetFinanceAnalyticsQuery query,
        CancellationToken cancellationToken);
}

public interface IFinanceApprovalTaskService
{
    Task<bool> EnsureTaskAsync(EnsureFinanceApprovalTaskCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinancePendingApprovalTaskDto>> GetPendingTasksAsync(GetPendingFinanceApprovalTasksQuery query, CancellationToken cancellationToken);
    Task<FinanceApprovalTaskBackfillResultDto> BackfillApprovalTasksAsync(BackfillFinanceApprovalTasksCommand command, CancellationToken cancellationToken);
    Task<FinancePendingApprovalTaskDto> ActOnTaskAsync(ActOnFinanceApprovalTaskCommand command, CancellationToken cancellationToken);
}

public sealed record FinanceDataResetResultDto(
    Guid CompanyId,
    int TotalDeleted,
    IReadOnlyDictionary<string, int> DeletedCounts);

public interface IFinanceMaintenanceService
{
    Task<FinanceDataResetResultDto> ResetFinancialDataAsync(Guid companyId, CancellationToken cancellationToken);
}

public interface IPlanningBaselineService
{
    Task<int> EnsureBaselineAsync(Guid companyId, CancellationToken cancellationToken);
    Task<int> BackfillAllCompaniesAsync(CancellationToken cancellationToken);
}

public static class FinanceEntryProgressStates
{
    public const string NotSeeded = "not_seeded";
    public const string SeedingRequested = "seeding_requested";
    public const string InProgress = "in_progress";
    public const string Seeded = "seeded";
    public const string Failed = "failed";
}

public static class FinanceEntrySources
{
    public const string FinanceEntry = "finance_entry";
    public const string FinanceEntryRetry = "finance_entry_retry";
    public const string ManualSeed = "finance_manual_seed";
    public const string FallbackRead = "finance_fallback_seed";
    public const string Backfill = "finance_seed_backfill";
}

