using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Finance;

public static class FinanceAgentAnalysisTypes
{
    public const string CashLiquidity = "cash_liquidity";
    public const string Payables = "payables";
    public const string Receivables = "receivables";
    public const string AccountingTreatment = "accounting_treatment";
    public const string CloseAnalysis = "close_analysis";
    public const string OperatingCadence = "operating_cadence";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { CashLiquidity, Payables, Receivables, AccountingTreatment, CloseAnalysis, OperatingCadence };
}

public interface IFinanceAgentAnalysisService
{
    Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        RoleAgentAnalysisRequest request, CancellationToken cancellationToken);
}
