namespace VirtualCompany.Shared;

public sealed record StatementWorkspaceRow(string Key, string LabelSv, string LabelEn, string Kind,
    decimal Amount, decimal? ComparisonAmount, decimal? ChangePercent,
    IReadOnlyList<StatementWorkspaceAccount> Accounts);
public sealed record StatementWorkspaceAccount(Guid? AccountId, string Code, string Name, decimal Amount,
    decimal? ComparisonAmount, string Currency);
public sealed record StatementWorkspaceSnapshot(Guid Id, Guid PeriodId, int Version, string Checksum,
    DateTime GeneratedUtc, string StatementType);
public sealed record StatementWorkspaceReport(Guid CompanyId, Guid PeriodId, string ReportKind,
    string PeriodName, DateTime StartUtc, DateTime EndUtc, string Currency, bool IsClosed,
    bool IsReportingLocked, string SourceMode, StatementWorkspaceSnapshot? Snapshot,
    Guid? ComparisonPeriodId, string? ComparisonName, DateTime? ComparisonStartUtc, DateTime? ComparisonEndUtc,
    StatementWorkspaceSnapshot? ComparisonSnapshot, string LayoutVersion,
    IReadOnlyList<StatementWorkspaceRow> Rows, IReadOnlyList<StatementWorkspaceRow> Kpis,
    decimal? BalanceDifference, IReadOnlyList<string> Warnings,
    IReadOnlyList<StatementWorkspaceSnapshot> SavedReports, string CsvContent);
