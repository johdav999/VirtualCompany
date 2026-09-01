using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Finance;

public static class FinanceLedgerAgentReadToolIds
{
    public const string LookupAccounts = "finance.ledger.lookup_accounts";
    public const string ReadFiscalPeriods = "finance.ledger.read_fiscal_periods";
    public const string SearchJournals = "finance.ledger.search_journals";
    public const string ReadGeneralLedger = "finance.ledger.read_general_ledger";
    public const string ReadTrialBalance = "finance.ledger.read_trial_balance";
    public const string ReadStatement = "finance.ledger.read_statement";
    public const string ReadReportDefinitions = "finance.ledger.read_report_definitions";
    public const string ReadReportSnapshot = "finance.ledger.read_report_snapshot";
    public const string ReadSourceDrilldown = "finance.ledger.read_source_drilldown";

    public static IReadOnlyList<string> All { get; } =
    [
        LookupAccounts,
        ReadFiscalPeriods,
        SearchJournals,
        ReadGeneralLedger,
        ReadTrialBalance,
        ReadStatement,
        ReadReportDefinitions,
        ReadReportSnapshot,
        ReadSourceDrilldown
    ];

    public static bool Contains(string? toolName) =>
        !string.IsNullOrWhiteSpace(toolName) && All.Contains(toolName.Trim(), StringComparer.OrdinalIgnoreCase);
}

public static class FinanceLedgerAgentReadContract
{
    public const string Version = "finance-ledger-agent-read-v1";
    public const int MaximumPageSize = 200;
    public const int MaximumJournalPageSize = 100;
    public const int MaximumLookupPageSize = 100;
    public const int MaximumSourceIds = 2_000;
}

public interface IFinanceLedgerAgentReadService
{
    Task<InternalToolExecutionResponse> ExecuteAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken);
}
