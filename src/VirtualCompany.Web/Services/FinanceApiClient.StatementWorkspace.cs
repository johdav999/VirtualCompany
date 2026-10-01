using VirtualCompany.Shared;

namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<StatementEvidenceResponse?> GetStatementEvidenceAsync(Guid companyId, Guid periodId, string reportKind,
        string lineCode, Guid? snapshotId, CancellationToken cancellationToken = default)
    {
        var type = reportKind == "profit-loss" ? "profit_and_loss" : "balance_sheet";
        var uri = snapshotId is Guid id
            ? $"api/companies/{companyId:D}/financial-statements/snapshots/{id:D}/lines/{Uri.EscapeDataString(lineCode)}/drilldown"
            : $"api/companies/{companyId:D}/financial-statements/drilldown" + BuildQuery(("fiscalPeriodId", periodId.ToString("D")),
                ("statementType", type), ("lineCode", lineCode));
        return GetAsync<StatementEvidenceResponse>(companyId, uri, false, cancellationToken);
    }
    public Task<StatementWorkspaceReport?> GetStatementWorkspaceAsync(Guid companyId, Guid fiscalPeriodId,
        string reportKind, Guid? comparisonFiscalPeriodId = null, Guid? snapshotId = null,
        Guid? comparisonSnapshotId = null, CancellationToken cancellationToken = default)
    {
        if (reportKind is not ("profit-loss" or "balance-sheet")) throw new ArgumentException("Unsupported statement.", nameof(reportKind));
        return GetAsync<StatementWorkspaceReport>(companyId,
            $"internal/companies/{companyId:D}/finance/accounting/statement-workspace/{reportKind}" +
            BuildQuery(("fiscalPeriodId", fiscalPeriodId.ToString("D")), ("comparisonFiscalPeriodId", comparisonFiscalPeriodId?.ToString("D")),
                ("snapshotId", snapshotId?.ToString("D")), ("comparisonSnapshotId", comparisonSnapshotId?.ToString("D"))), false, cancellationToken);
    }
}

public sealed class StatementEvidenceResponse
{
    public StatementEvidenceLine? Line { get; set; }
    public decimal OpeningBalanceAdjustment { get; set; }
    public decimal JournalLineTotal { get; set; }
    public decimal ReconciliationTotal { get; set; }
    public decimal ReconciliationDelta { get; set; }
    [System.Text.Json.Serialization.JsonPropertyName("journalEntries")]
    public List<StatementEvidenceEntry> Entries { get; set; } = [];
}
public sealed class StatementEvidenceLine
{
    public string ReportSection { get; set; } = "";
    public string LineClassification { get; set; } = "";
    public decimal Amount { get; set; }
}
public sealed class StatementEvidenceEntry
{
    public Guid LedgerEntryId { get; set; }
    public string EntryNumber { get; set; } = "";
    public DateTime EntryUtc { get; set; }
    public decimal TotalContributionAmount { get; set; }
}
