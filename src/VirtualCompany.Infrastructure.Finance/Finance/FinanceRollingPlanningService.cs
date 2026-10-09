using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using Revision = VirtualCompany.Application.Finance.FinanceForecastRevision;
using RevisionEntity = VirtualCompany.Domain.Entities.FinanceForecastRevision;
namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class FinanceRollingPlanningService(VirtualCompanyDbContext db, ITodayWorkspaceLensResolver access,
    TimeProvider clock) : IFinanceRollingPlanningService
{
    private const int Limit = 2000;
    private static DateTime Utc(DateTime x) => DateTime.SpecifyKind(x, DateTimeKind.Utc);
    private static DateTime Month(DateTime x) => new(x.Year, x.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    private static string Hash(string x) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x)));
    private static string Json<T>(T x) => JsonSerializer.Serialize(x);
    private static string? Currency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var c = value.Trim().ToUpperInvariant();
        if (c.Length != 3 || !c.All(char.IsAsciiLetterUpper)) throw new ArgumentException("Choose a three-letter currency.");
        return c;
    }
    private async Task<TodayWorkspaceLensResolution> Authorize(Guid company, CancellationToken ct)
    {
        if (company == Guid.Empty) throw new ArgumentException("Choose a company.");
        var scope = await access.ResolveAsync(company, TodayWorkspaceLenses.Finance, ct);
        if (!scope.AvailableLenses.Any(x => x.Lens == TodayWorkspaceLenses.Finance)) throw new UnauthorizedAccessException();
        return scope;
    }
    private static List<T> Bounded<T>(List<T> rows, string source) => rows.Count > Limit
        ? throw new ArgumentException($"{source} exceeds {Limit} records. Narrow the account, dimension or period filters.") : rows;
    public async Task<FinancePlanningReport> ReportAsync(Guid company, FinancePlanningQuery query, CancellationToken ct)
    {
        await Authorize(company, ct);
        if (query is null || query.Year is < 2000 or > 2100 || query.Month is < 1 or > 12 || query.Months is < 1 or > 12 ||
            query.BudgetVersion?.Length > 64 || query.ForecastVersion?.Length > 64 || query.CostCenterId == Guid.Empty || query.FinanceAccountId == Guid.Empty)
            throw new ArgumentException("Choose a valid monthly range, account, dimension and explicit planning versions.");
        query = query with { Currency = Currency(query.Currency), BudgetVersion = string.IsNullOrWhiteSpace(query.BudgetVersion) ? null : query.BudgetVersion.Trim(),
            ForecastVersion = string.IsNullOrWhiteSpace(query.ForecastVersion) ? null : query.ForecastVersion.Trim() };
        var periods = Bounded(await db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company)
            .OrderBy(x => x.StartUtc).Take(Limit + 1).ToListAsync(ct), "Fiscal periods");
        var start = new DateTime(query.Year, query.Month, 1, 0, 0, 0, DateTimeKind.Utc); var end = start.AddMonths(query.Months);
        if (query.FiscalPeriodId is { } fiscalId)
        {
            var p = periods.SingleOrDefault(x => x.Id == fiscalId) ?? throw new KeyNotFoundException("Fiscal period is unavailable.");
            if (Utc(p.StartUtc) != start || Utc(p.EndUtc) != end)
                throw new ArgumentException("The fiscal period must exactly match these native UTC planning months. Adjust the range; non-monthly fiscal boundaries cannot be silently prorated.");
        }
        var accounts = Bounded(await db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company)
            .OrderBy(x => x.Code).Take(Limit + 1).ToListAsync(ct), "Accounts");
        if (query.FinanceAccountId.HasValue && !accounts.Any(x => x.Id == query.FinanceAccountId)) throw new KeyNotFoundException("Account is unavailable.");
        var dimensions = Bounded(await db.AccountingDimensionMembers.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company &&
            db.AccountingDimensionTypes.IgnoreQueryFilters().Any(t => t.CompanyId == company && t.Id == x.DimensionTypeId && t.Code == AccountingDimensionCodes.CostCenter))
            .OrderBy(x => x.Code).Take(Limit + 1).ToListAsync(ct), "Cost centers");
        if (query.CostCenterId.HasValue && !dimensions.Any(x => x.Id == query.CostCenterId)) throw new KeyNotFoundException("Cost center is unavailable.");
        var budgets = Bounded(await db.Budgets.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company && x.PeriodStartUtc >= start && x.PeriodStartUtc < end &&
            (!query.FinanceAccountId.HasValue || x.FinanceAccountId == query.FinanceAccountId) && (!query.CostCenterId.HasValue || x.CostCenterId == query.CostCenterId) &&
            (query.Currency == null || x.Currency == query.Currency)).OrderBy(x => x.Id).Take(Limit + 1).ToListAsync(ct), "Budgets");
        var forecasts = Bounded(await db.Forecasts.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company && x.PeriodStartUtc >= start && x.PeriodStartUtc < end &&
            (!query.FinanceAccountId.HasValue || x.FinanceAccountId == query.FinanceAccountId) && (!query.CostCenterId.HasValue || x.CostCenterId == query.CostCenterId) &&
            (query.Currency == null || x.Currency == query.Currency)).OrderBy(x => x.Id).Take(Limit + 1).ToListAsync(ct), "Forecasts");
        var forecastKeys = forecasts.Select(x => x.Version).Distinct().ToArray();
        var labels = (await db.FinanceForecastRevisions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company && forecastKeys.Contains(x.NativeVersion))
            .Select(x => new { x.NativeVersion, x.Name, x.SavedUtc }).ToListAsync(ct)).ToDictionary(x => x.NativeVersion, x => $"{x.Name} · {x.SavedUtc:yyyy-MM-dd HH:mm} UTC");
        var now = clock.GetUtcNow().UtcDateTime; var cutoff = now < end ? now : end; var historyStart = start.AddYears(-1); var historyEnd = end.AddYears(-1);
        var actuals = Bounded(await db.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company &&
            x.LedgerEntry.CompanyId == company && x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
            ((x.LedgerEntry.PostedAtUtc ?? x.LedgerEntry.EntryUtc) >= start && (x.LedgerEntry.PostedAtUtc ?? x.LedgerEntry.EntryUtc) < cutoff ||
             (x.LedgerEntry.PostedAtUtc ?? x.LedgerEntry.EntryUtc) >= historyStart && (x.LedgerEntry.PostedAtUtc ?? x.LedgerEntry.EntryUtc) < historyEnd &&
             (x.LedgerEntry.PostedAtUtc ?? x.LedgerEntry.EntryUtc) <= now) &&
            (!query.FinanceAccountId.HasValue || x.FinanceAccountId == query.FinanceAccountId) && (!query.CostCenterId.HasValue || x.CostCenterId == query.CostCenterId) &&
            (query.Currency == null || x.Currency == query.Currency)).OrderBy(x => x.Id).Take(Limit + 1).Select(x => new {
                x.Id, x.FinanceAccountId, x.CostCenterId, x.Currency, Amount = x.DebitAmount - x.CreditAmount,
                Date = x.LedgerEntry.PostedAtUtc ?? x.LedgerEntry.EntryUtc, x.LedgerEntry.UpdatedUtc, x.Description, x.LedgerEntryId }).ToListAsync(ct), "Posted ledger lines");
        var sources = new List<FinancePlanningSource>();
        foreach (var a in actuals)
        {
            var account = accounts.Single(x => x.Id == a.FinanceAccountId);
            sources.Add(new(a.Id, a.Date < start ? "history" : "actual", account.Id, account.Code, account.Name,
                Month(a.Date), a.CostCenterId, a.Amount, a.Currency, null, Utc(a.UpdatedUtc), a.Description ?? "Posted journal line",
                $"/finance/accounting/journals?companyId={company:D}&journalId={a.LedgerEntryId:D}"));
        }
        foreach (var b in budgets.Where(x => x.Version == query.BudgetVersion)) Add(b.Id, "budget", b.FinanceAccountId, b.PeriodStartUtc, b.CostCenterId, b.Amount, b.Currency, b.Version, b.UpdatedUtc);
        foreach (var f in forecasts.Where(x => x.Version == query.ForecastVersion)) Add(f.Id, "forecast", f.FinanceAccountId, f.PeriodStartUtc, f.CostCenterId, f.Amount, f.Currency, f.Version, f.UpdatedUtc);
        void Add(Guid id, string kind, Guid accountId, DateTime month, Guid? dim, decimal amount, string currency, string version, DateTime updated)
        {
            var a = accounts.Single(x => x.Id == accountId);
            sources.Add(new(id, kind, a.Id, a.Code, a.Name, Utc(month), dim, amount, currency, version, Utc(updated), $"{kind} · {labels.GetValueOrDefault(version) ?? version}",
                $"/finance/reports/variance?companyId={company:D}&year={query.Year}&month={query.Month}&months={query.Months}&{kind}Version={Uri.EscapeDataString(version)}"));
        }
        var rows = sources.GroupBy(x => (Month: x.Kind == "history" ? x.MonthUtc.AddYears(1) : x.MonthUtc, x.AccountId, x.CostCenterId, x.Currency))
            .Select(g => { decimal? Sum(string kind) => g.Any(x => x.Kind == kind) ? decimal.Round(g.Where(x => x.Kind == kind).Sum(x => x.Amount), 2, MidpointRounding.AwayFromZero) : null;
                var actual = Sum("actual"); var budget = Sum("budget"); var difference = FinanceRollingPlanningCalculation.Difference(actual, budget);
                return new FinancePlanningRow(g.Key.Month, g.Key.AccountId, g.First().AccountCode, g.First().AccountName, g.Key.CostCenterId,
                    g.Key.Currency, actual, budget, Sum("forecast"), Sum("history"), difference, FinanceRollingPlanningCalculation.Percentage(difference, budget)); })
            .OrderBy(x => x.MonthUtc).ThenBy(x => x.AccountCode).ThenBy(x => x.CostCenterId).ThenBy(x => x.Currency).ToArray();
        var coverage = new List<string> { "Native UTC calendar months; actuals are posted ledger debit minus credit, using posting date. Revenue credit balances are negative. This is account variance, not a restyled financial statement.",
            "Currencies and cost centers stay separate. No conversion, opening-balance, revenue-to-cash timing or scenario calculation is included.",
            "No posted lines means unavailable coverage, not a proven zero. Missing plan and history values remain unavailable." };
        if (query.BudgetVersion is null) coverage.Add("Choose one budget version to calculate variance. Versions are never added together.");
        if (query.ForecastVersion is null) coverage.Add("No forecast version selected.");
        if (sources.Count == 0) coverage.Add("No matching posted or selected planning sources exist.");
        if (cutoff < end) coverage.Add("Actuals are partial through the as-of time; future assumptions remain separate.");
        var explanations = Bounded(await db.FinanceVarianceExplanations.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company && x.MonthUtc >= start && x.MonthUtc < end &&
            x.BudgetVersion == query.BudgetVersion && (!query.FinanceAccountId.HasValue || x.AccountId == query.FinanceAccountId) &&
            (!query.CostCenterId.HasValue || x.CostCenterId == query.CostCenterId) && (query.Currency == null || x.Currency == query.Currency))
            .OrderBy(x => x.SavedUtc).Take(Limit + 1).ToListAsync(ct), "Explanations").Select(Note).ToArray();
        var sourceHash = Hash(Json(new { company, query, Sources = sources, Periods = periods.Select(x => new { x.Id, x.StartUtc, x.EndUtc, x.IsClosed, x.UpdatedUtc }) }));
        return new(company, query, now, FinanceRollingPlanningCalculation.Version, sourceHash, rows, sources,
            budgets.Select(x => x.Version).Distinct().Order().ToArray(), forecasts.Select(x => x.Version).Distinct().Order().ToArray(),
            accounts.Select(x => new FinancePlanningChoice(x.Id, $"{x.Code} · {x.Name}")).ToArray(),
            dimensions.Select(x => new FinancePlanningChoice(x.Id, $"{x.Code} · {x.Name}")).ToArray(),
            periods.Select(x => new FinancePlanningPeriod(x.Id, x.Name, Utc(x.StartUtc), Utc(x.EndUtc), x.IsClosed)).ToArray(), coverage, explanations, labels);
    }
    private static FinanceVarianceExplanationDto Note(FinanceVarianceExplanation x) => new(x.Id, Utc(x.MonthUtc), x.AccountId, x.CostCenterId, x.Currency, x.BudgetVersion, x.Text, Utc(x.SavedUtc), x.SourceFingerprint);
    public async Task<FinanceVarianceExplanationDto> ExplainAsync(Guid company, ExplainFinanceVariance command, CancellationToken ct)
    {
        var scope = await Authorize(company, ct);
        if (command.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(command.Text) || command.Text.Length > 2000) throw new ArgumentException("Provide an explanation up to 2000 characters.");
        var retry = await db.FinanceVarianceExplanations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == company && x.AuthorId == scope.UserId && x.RequestId == command.RequestId, ct);
        if (retry != null)
        {
            if (retry.Text != command.Text.Trim() || retry.SourceFingerprint != command.SourceFingerprint || retry.AccountId != command.AccountId ||
                retry.MonthUtc != command.MonthUtc || retry.CostCenterId != command.CostCenterId || retry.Currency != command.Currency || retry.BudgetVersion != command.Query.BudgetVersion)
                throw new InvalidOperationException("This request already contains a different explanation.");
            return Note(retry);
        }
        var report = await ReportAsync(company, command.Query, ct);
        if (report.Fingerprint != command.SourceFingerprint) throw new InvalidOperationException("Source evidence changed. Reload before explaining this variance.");
        if (!report.Rows.Any(x => x.MonthUtc == command.MonthUtc && x.AccountId == command.AccountId && x.CostCenterId == command.CostCenterId && x.Currency == command.Currency && x.Variance.HasValue))
            throw new ArgumentException("Choose a comparable variance row with posted actuals and a selected budget baseline.");
        var row = new FinanceVarianceExplanation(company, scope.UserId, command.RequestId, command.MonthUtc, command.AccountId, command.CostCenterId,
            command.Currency, report.Query.BudgetVersion, command.Text.Trim(), report.Fingerprint, clock.GetUtcNow().UtcDateTime);
        db.FinanceVarianceExplanations.Add(row);
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), company, "user", scope.UserId, "finance.variance.explained", "finance_variance_explanation",
            row.Id.ToString("D"), "succeeded", $"Retained explanation for {command.MonthUtc:yyyy-MM}; source fingerprint {report.Fingerprint}."));
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException ex) { throw new InvalidOperationException("Explanation changed concurrently. Reload or retry the same request.", ex); }
        return Note(row);
    }
    public async Task<FinancePlanningExport> ExportAsync(Guid company, FinancePlanningQuery query, CancellationToken ct)
    {
        var r = await ReportAsync(company, query, ct); static string C(object? x) { var s = Convert.ToString(x, CultureInfo.InvariantCulture) ?? "";
            if (s.Length > 0 && (s[0] is '=' or '+' or '@' or '\t' or '\r' || s[0] == '-' && !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _))) s = "'" + s; return "\"" + s.Replace("\"", "\"\"") + "\""; }
        var csv = new StringBuilder("Month,Account,Account name,Cost center,Currency,Actual,Budget,Forecast,Prior year,Variance,Variance percent,Budget version,Forecast version,Source fingerprint\r\n");
        static string? Number(decimal? x) => x?.ToString("0.00", CultureInfo.InvariantCulture);
        foreach (var x in r.Rows) csv.AppendLine(string.Join(",", new object?[] { x.MonthUtc.ToString("yyyy-MM"), x.AccountCode, x.AccountName,
            r.CostCenters.FirstOrDefault(d => d.Id == x.CostCenterId)?.Name, x.Currency, Number(x.Actual), Number(x.Budget), Number(x.Forecast), Number(x.PriorYear), Number(x.Variance), Number(x.VariancePercent),
            r.Query.BudgetVersion, r.Query.ForecastVersion, r.Fingerprint }.Select(C)));
        return new($"finance-variance-{query.Year}-{query.Month:00}.csv", csv.ToString(), r.Fingerprint);
    }
}
