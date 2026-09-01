using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Finance;

public static class FinanceAdvancedAccountingAgentToolIds
{
    public const string ReadStatementImports = "finance.advanced.read_statement_imports";
    public const string ReadReconciliation = "finance.advanced.read_reconciliation";
    public const string ReadSubledgerSettlement = "finance.advanced.read_subledger_settlement";
    public const string ReadPaymentBatches = "finance.advanced.read_payment_batches";
    public const string ReadExchangeRates = "finance.advanced.read_exchange_rates";
    public const string ReadRevaluation = "finance.advanced.read_revaluation";
    public const string ReadDimensions = "finance.advanced.read_dimensions";
    public const string ReadSchedules = "finance.advanced.read_schedules";
    public const string ReadFixedAssets = "finance.advanced.read_fixed_assets";
    public const string ReadInventoryBoundary = "finance.advanced.read_inventory_boundary";

    public const string RecommendReconciliationReview = "finance.advanced.recommend_reconciliation_review";
    public const string RecommendRateEvidenceRemediation = "finance.advanced.recommend_rate_evidence_remediation";
    public const string RecommendScheduleAssetReview = "finance.advanced.recommend_schedule_asset_review";
    public const string PrioritizeSubledgerExceptions = "finance.advanced.recommend_subledger_exceptions";

    public static IReadOnlyList<string> ReadTools { get; } =
    [
        ReadStatementImports, ReadReconciliation, ReadSubledgerSettlement, ReadPaymentBatches,
        ReadExchangeRates, ReadRevaluation, ReadDimensions, ReadSchedules, ReadFixedAssets,
        ReadInventoryBoundary
    ];

    public static IReadOnlyList<string> RecommendationTools { get; } =
    [
        RecommendReconciliationReview, RecommendRateEvidenceRemediation,
        RecommendScheduleAssetReview, PrioritizeSubledgerExceptions
    ];

    public static IReadOnlyList<string> All { get; } = [.. ReadTools, .. RecommendationTools];

    public static bool Contains(string? toolName) =>
        !string.IsNullOrWhiteSpace(toolName) && All.Contains(toolName.Trim(), StringComparer.OrdinalIgnoreCase);

    public static ToolActionType ActionFor(string toolName) =>
        RecommendationTools.Contains(toolName, StringComparer.OrdinalIgnoreCase)
            ? ToolActionType.Recommend
            : ToolActionType.Read;
}

public static class FinanceAdvancedAccountingAgentContract
{
    public const string Version = "finance-advanced-accounting-agent-v1";
    public const int MaximumPageSize = 100;
    public const int MaximumSourceIds = 2_000;
    public const int MaximumCalculationRangeDays = 366;
    public const string AuthorityNotice =
        "Agent reads and recommendations do not import statements, apply matches or allocations, approve rates, post revaluations, release payments, generate schedules, depreciate or dispose assets, or determine tax treatment.";
    public const string InventoryBoundary =
        "Inventory quantity, inventory valuation, and cost-of-goods-sold accounting are not supported Finance accounting capabilities. Commerce records must not be presented as an inventory subledger or accounting valuation.";
}

public interface IFinanceAdvancedAccountingAgentService
{
    Task<InternalToolExecutionResponse> ExecuteAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken);
}
