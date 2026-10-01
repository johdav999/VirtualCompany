using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinancialStatementWorkspaceService(
    IFinanceReadService statements, IFinancialStatementMappingService mappings,
    ICompanyMembershipContextResolver membershipResolver, VirtualCompanyDbContext db,
    IAccountingChartCatalogResolver catalogs) : IFinancialStatementWorkspaceService
{
    public async Task<StatementWorkspaceReport> GetAsync(GetStatementWorkspaceQuery query, CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty || query.FiscalPeriodId == Guid.Empty)
            throw new ArgumentException("Company and fiscal period are required.");
        var member = await membershipResolver.ResolveAsync(query.CompanyId, cancellationToken)
            ?? throw new UnauthorizedAccessException();
        if (!FinanceAccess.CanViewAccounting(member.MembershipRole.ToStorageValue())) throw new UnauthorizedAccessException();
        var type = query.ReportKind switch
        {
            "profit-loss" => FinancialStatementType.ProfitAndLoss,
            "balance-sheet" => FinancialStatementType.BalanceSheet,
            _ => throw new ArgumentException("Select profit-loss or balance-sheet.")
        };
        var configuration = await db.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);
        IAccountingChartCatalog? bas = configuration?.PolicyPackKey.StartsWith("sweden", StringComparison.Ordinal) == true
            ? catalogs.Resolve(AccountingChartCatalogDefaults.Bas2026CatalogKey, AccountingChartCatalogDefaults.Bas2026CatalogVersion) : null;
        // Sequential: the readers share the scoped DbContext.
        var current = await LoadAsync(query.CompanyId, query.FiscalPeriodId, type, query.SnapshotId, cancellationToken);
        var comparison = query.ComparisonFiscalPeriodId is Guid previous
            ? await LoadAsync(query.CompanyId, previous, type, query.ComparisonSnapshotId, cancellationToken) : null;
        foreach (var source in new[] { current, comparison }.OfType<Source>())
            if (source.Lines.Select(x => x.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                throw new InvalidOperationException("Statement lines contain multiple currencies. Resolve currency reporting before displaying aggregated totals.");
        if (current.Lines.Count == 0 && current.Snapshot is null && configuration is not null)
            current = current with { Currency = configuration.BaseCurrency };
        if (comparison is { Lines.Count: 0, Snapshot: null } && configuration is not null)
            comparison = comparison with { Currency = configuration.BaseCurrency };
        if (query.ComparisonSnapshotId.HasValue && comparison is null)
            throw new ArgumentException("A comparison period is required for its snapshot.");
        if (comparison is not null && comparison.Currency != current.Currency)
            throw new InvalidOperationException("Reports in different currencies cannot be compared without conversion.");
        var period = await db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == query.CompanyId && x.Id == query.FiscalPeriodId, cancellationToken);
        var saved = await statements.ListFinancialStatementSnapshotsAsync(new(query.CompanyId, query.FiscalPeriodId, type), cancellationToken);
        var warnings = new List<string>();
        if (!current.IsClosed)
        {
            var validation = await mappings.ValidateAsync(new(query.CompanyId), cancellationToken);
            warnings.AddRange(validation.Issues.Where(x => x.StatementType is null || x.StatementType == type.ToStorageValue())
                .Select(x => $"{x.AccountCode} · {x.Message}"));
        }
        if (current.IsClosed && current.Snapshot is null) warnings.Add("Legacy closed-period data has no retained statement version; exact version drill-down is unavailable.");
        if (bas is null) warnings.Add("BAS subdivisions require a Swedish accounting policy. Accounts use their retained statement classifications.");
        var rows = FinancialStatementWorkspaceLayout.Build(query.ReportKind, current.Lines, comparison?.Lines, bas);
        if (rows.Any(x => x.Key == "unclassified")) warnings.Add("Unclassified statement lines require review.");
        var keys = type == FinancialStatementType.ProfitAndLoss ? new[] { "sales", "operating", "net" } : ["assets", "equity", "debt"];
        decimal? difference = type == FinancialStatementType.BalanceSheet
            ? rows.Single(x => x.Key == "assets").Amount - rows.Single(x => x.Key == "equity_debt").Amount : null;
        var report = new StatementWorkspaceReport(query.CompanyId, query.FiscalPeriodId, query.ReportKind,
            current.Name, current.StartUtc, current.EndUtc, current.Currency, current.IsClosed, period.IsReportingLocked,
            current.Snapshot is not null ? "snapshot" : "posted_journals", current.Snapshot,
            query.ComparisonFiscalPeriodId, comparison?.Name, comparison?.StartUtc, comparison?.EndUtc, comparison?.Snapshot,
            FinancialStatementWorkspaceLayout.Version, rows, keys.Select(k => rows.Single(x => x.Key == k)).ToArray(),
            difference, warnings, saved.Select(s => new StatementWorkspaceSnapshot(s.SnapshotId, s.FiscalPeriodId,
                s.VersionNumber, s.BalancesChecksum, s.GeneratedAtUtc, s.StatementType)).ToArray(), "");
        return report with { CsvContent = ToCsv(report) };
    }

    private async Task<Source> LoadAsync(Guid company, Guid period, FinancialStatementType type, Guid? snapshotId, CancellationToken ct)
    {
        if (snapshotId is Guid id)
        {
            var snapshot = await statements.GetFinancialStatementSnapshotAsync(new(company, id), ct)
                ?? throw new KeyNotFoundException("Saved report was not found in this company.");
            var s = snapshot.Summary;
            if (s.FiscalPeriodId != period || s.StatementType != type.ToStorageValue())
                throw new ArgumentException("Saved report does not belong to the selected period and report.");
            return new(s.FiscalPeriodName, s.SourcePeriodStartUtc, s.SourcePeriodEndUtc, s.Currency, true,
                snapshot.Lines, new(s.SnapshotId, period, s.VersionNumber, s.BalancesChecksum, s.GeneratedAtUtc, s.StatementType));
        }
        if (type == FinancialStatementType.ProfitAndLoss)
        {
            var s = await statements.GetProfitAndLossReportAsync(new(company, period), ct);
            return new(s.FiscalPeriodName, s.PeriodStartUtc, s.PeriodEndUtc, s.Currency, s.IsClosed,
                s.RevenueLines.Concat(s.ExpenseLines).ToArray(), Snapshot(s.Snapshot, period, type));
        }
        var b = await statements.GetBalanceSheetReportAsync(new(company, period), ct);
        return new(b.FiscalPeriodName, b.PeriodStartUtc, b.PeriodEndUtc, b.Currency, b.IsClosed,
            b.AssetLines.Concat(b.EquityLines).Concat(b.LiabilityLines).ToArray(), Snapshot(b.Snapshot, period, type));
    }
    private static StatementWorkspaceSnapshot? Snapshot(FinancialStatementSnapshotMetadataDto? s, Guid period, FinancialStatementType type) =>
        s is null ? null : new(s.SnapshotId, period, s.VersionNumber, s.BalancesChecksum, s.GeneratedAtUtc, type.ToStorageValue());
    private sealed record Source(string Name, DateTime StartUtc, DateTime EndUtc, string Currency, bool IsClosed,
        IReadOnlyList<FinanceStatementLineDto> Lines, StatementWorkspaceSnapshot? Snapshot);

    public static string ToCsv(StatementWorkspaceReport r)
    {
        // Exact read payload, including both retained identities. Small statement export; no object-storage job required.
        var csv = new StringBuilder();
        string Text(string s) => "\"" + ((s.StartsWith('=') || s.StartsWith('+') || s.StartsWith('-') || s.StartsWith('@')) ? "'" : "") + s.Replace("\"", "\"\"") + "\"";
        string Number(decimal? n) => n?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";
        void Meta(string key, string? value) => csv.AppendLine($"{Text(key)},{Text(value ?? "")}");
        Meta("Company", r.CompanyId.ToString()); Meta("Report", r.ReportKind); Meta("Period", r.PeriodName);
        Meta("StartUtc", r.StartUtc.ToString("O")); Meta("EndUtcExclusive", r.EndUtc.ToString("O"));
        Meta("Currency", r.Currency); Meta("Scale", "1"); Meta("LayoutVersion", r.LayoutVersion);
        Meta("Source", r.SourceMode); Meta("Snapshot", r.Snapshot?.Id.ToString()); Meta("Checksum", r.Snapshot?.Checksum);
        Meta("Comparison", r.ComparisonName); Meta("ComparisonStartUtc", r.ComparisonStartUtc?.ToString("O"));
        Meta("ComparisonEndUtcExclusive", r.ComparisonEndUtc?.ToString("O"));
        Meta("ComparisonSnapshot", r.ComparisonSnapshot?.Id.ToString()); Meta("ComparisonChecksum", r.ComparisonSnapshot?.Checksum);
        foreach (var warning in r.Warnings) Meta("Warning", warning);
        csv.AppendLine("Post,Amount,Comparison,ChangePercent,AccountId");
        foreach (var row in r.Rows.Where(x => x.Kind != "heading"))
        {
            csv.AppendLine($"{Text(row.LabelSv)},{Number(row.Amount)},{Number(row.ComparisonAmount)},{Number(row.ChangePercent)},");
            if (row.Kind == "detail") foreach (var a in row.Accounts)
                csv.AppendLine($"{Text(a.Code + " " + a.Name)},{Number(a.Amount)},{Number(a.ComparisonAmount)},,{a.AccountId}");
        }
        return csv.ToString();
    }
}
