using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using Revision = VirtualCompany.Application.Finance.FinanceForecastRevision;
using RevisionEntity = VirtualCompany.Domain.Entities.FinanceForecastRevision;
using AccountingDimensionRequirementValues = VirtualCompany.Domain.Entities.AccountingDimensionRequirementValues;
using AccountingDimensionStatusValues = VirtualCompany.Domain.Entities.AccountingDimensionStatusValues;
namespace VirtualCompany.Infrastructure.Finance;

public sealed partial class FinanceRollingPlanningService
{
    public async Task<FinanceForecastPreview> PreviewAsync(Guid company, PreviewFinanceForecast command, CancellationToken ct)
    {
        if (command?.Query is null || command.Assumptions is null || command.Assumptions.Any(a=>a is null)) throw new ArgumentException("Choose the forecast range and assumptions.");
        var report = await ReportAsync(company, command.Query, ct);
        if (report.Query.Currency is null) throw new ArgumentException("Choose one native account currency before forecasting; no implicit conversion is permitted.");
        var nowMonth = Month(clock.GetUtcNow().UtcDateTime);
        if (command.ActualThroughUtc > nowMonth || command.ActualThroughUtc.Kind == DateTimeKind.Local)
            throw new ArgumentException("The actual cutoff cannot include future months.");
        var input = command with { Query = report.Query, Notes = command.Notes?.Trim() ?? "",
            Assumptions = command.Assumptions.Select(a => a with { Currency = Currency(a.Currency) ?? "", Rationale = a.Rationale?.Trim() ?? "" }).ToArray() };
        var values = FinanceRollingPlanningCalculation.Calculate(report, input);
        foreach (var a in input.Assumptions)
        {
            if (a.MonthUtc <= nowMonth) throw new ArgumentException("Forecast assumptions must be in future UTC months; all existing actual months remain read-only.");
            if (report.Query.FinanceAccountId is { } selected && a.AccountId != selected || report.Query.CostCenterId is { } dim && a.CostCenterId != dim || a.Currency != report.Query.Currency)
                throw new ArgumentException("Every assumption must match the selected account, cost center and currency scope.");
            var account = await db.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == a.AccountId, ct)
                ?? throw new ArgumentException("Assumption account is unavailable in this company.");
            if (account.Currency != a.Currency) throw new ArgumentException("Assumption currency must match the native account currency. Approved conversion is not configured in this editor.");
            if (report.FiscalPeriods.Any(p => p.Closed && p.StartUtc < a.MonthUtc.AddMonths(1) && p.EndUtc > a.MonthUtc))
                throw new ArgumentException("A closed fiscal period overlaps this assumption month. Closed actuals cannot be changed.");
            await FinancePlanningDimensionValidator.ValidateAsync(db, company, account.Id, a.CostCenterId, DateOnly.FromDateTime(a.MonthUtc), ct);
        }
        var fingerprint = Hash(Json(new { report.Fingerprint, Input = input, Values = values }));
        return new(report, input, values, fingerprint);
    }
    private static FinanceForecastRevisionSummary Summary(RevisionEntity r) => new(r.Id, r.Name, r.NativeVersion, r.PreviousId, r.AuthorId, Utc(r.SavedUtc), Utc(r.SourceAsOfUtc));
    private static Revision Reproduce(RevisionEntity r)
    {
        if (Encoding.UTF8.GetByteCount(r.Payload) > 1024 * 1024 || Hash(r.Payload) != r.Checksum) throw new InvalidDataException("Forecast evidence failed its integrity check.");
        FinanceForecastPreview? preview;
        try { preview = JsonSerializer.Deserialize<FinanceForecastPreview>(r.Payload); }
        catch (JsonException ex) { throw new InvalidDataException("Forecast evidence cannot be read.", ex); }
        if (preview?.Report is not { } report || preview.Input is null || preview.Values is null || report.CompanyId != r.CompanyId ||
            report.CalculationVersion != FinanceRollingPlanningCalculation.Version || report.AsOfUtc != Utc(r.SourceAsOfUtc) || report.Query != preview.Input.Query ||
            report.Sources is null || report.Rows is null || report.Coverage is null || report.Explanations is null)
            throw new InvalidDataException("Forecast context is inconsistent.");
        IReadOnlyList<FinanceForecastValue> values;
        try { values = FinanceRollingPlanningCalculation.Calculate(report, preview.Input); }
        catch (ArgumentException ex) { throw new InvalidDataException("Retained forecast assumptions are invalid.", ex); }
        if (Json(values) != Json(preview.Values) || Hash(Json(new { report.Fingerprint, Input = preview.Input, Values = values })) != preview.Fingerprint)
            throw new InvalidDataException("Retained values do not reproduce from their assumptions.");
        return new(Summary(r), preview, r.Checksum);
    }
    public async Task<Revision> OpenAsync(Guid company, Guid id, CancellationToken ct)
    {
        await Authorize(company, ct);
        var row = await db.FinanceForecastRevisions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == company && x.Id == id, ct);
        return row is null ? throw new KeyNotFoundException() : Reproduce(row);
    }
    public async Task<IReadOnlyList<FinanceForecastRevisionSummary>> HistoryAsync(Guid company, int skip, CancellationToken ct)
    {
        await Authorize(company, ct);
        if (skip is < 0 or > 10000) throw new ArgumentException("History page is outside the supported range.");
        return (await db.FinanceForecastRevisions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == company)
            .OrderByDescending(x => x.SavedUtc).ThenByDescending(x => x.Id).Skip(skip).Take(20).ToListAsync(ct)).Select(Summary).ToArray();
    }
    public Task<Revision> SaveAsync(Guid company, SaveFinanceForecast command, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(() => SaveCoreAsync(company, command, ct));
    private async Task<Revision> SaveCoreAsync(Guid company, SaveFinanceForecast command, CancellationToken ct)
    {
        var scope = await Authorize(company, ct);
        if (command is null || command.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > 64 || command.Input is null)
            throw new ArgumentException("Provide a request identity, named version and previewed assumptions.");
        var commandHash = Hash(Json(command));
        var retry = await db.FinanceForecastRevisions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == company && x.AuthorId == scope.UserId && x.RequestId == command.RequestId, ct);
        if (retry != null) return retry.CommandHash == commandHash ? Reproduce(retry) : throw new InvalidOperationException("Request identity already contains different assumptions.");
        if (command.PreviousId.HasValue)
        {
            await OpenAsync(company, command.PreviousId.Value, ct);
            if (await db.FinanceForecastRevisions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == company && x.PreviousId == command.PreviousId, ct))
                throw new InvalidOperationException("This version already has a later revision. Open the latest version before saving.");
        }
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (db.Database.IsSqlServer())
        {
            var resource = "finance-rolling-planning:" + company.ToString("N");
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @lockResult int; EXEC @lockResult = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; IF @lockResult < 0 THROW 51023, 'Finance planning is busy. Retry the same request.', 1;", ct);
        }
        var preview = await PreviewAsync(company, command.Input, ct);
        if (preview.Fingerprint != command.ExpectedFingerprint) throw new InvalidOperationException("Sources or assumptions changed after preview. Preview again before saving.");
        var id = Guid.NewGuid(); var name = command.Name.Trim(); var nativeVersion = name[..Math.Min(name.Length, 31)] + "-" + id.ToString("N");
        var payload = Json(preview); var now = clock.GetUtcNow().UtcDateTime;
        var row = new RevisionEntity(id, company, scope.UserId, command.RequestId, name, nativeVersion, command.PreviousId, now, preview.Report.AsOfUtc, payload, Hash(payload), commandHash);
        db.FinanceForecastRevisions.Add(row);
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), company, "user", scope.UserId, "finance.forecast.version_saved", "finance_forecast_revision",
            row.Id.ToString("D"), "succeeded", $"Saved {name}; {preview.Input.Assumptions.Count} future native Forecast rows; immutable actual/source snapshot {preview.Report.AsOfUtc:O}."));
        foreach (var a in preview.Input.Assumptions)
        {
            var forecast = new Forecast(Guid.NewGuid(), company, a.AccountId, a.MonthUtc, nativeVersion, a.Amount, a.Currency, a.CostCenterId, now, now);
            forecast.LinkRevision(id); db.Forecasts.Add(forecast);
        }
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(ct);
            await transaction.DisposeAsync();
            db.ChangeTracker.Clear();
            var concurrent = await db.FinanceForecastRevisions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == company && x.AuthorId == scope.UserId && x.RequestId == command.RequestId, ct);
            if (concurrent?.CommandHash == commandHash) return Reproduce(concurrent);
            throw new InvalidOperationException("A concurrent revision changed this version. Reload history before retrying.", ex);
        }
        return Reproduce(row);
    }
    public async Task<FinanceForecastComparison> CompareAsync(Guid company, Guid earlier, Guid later, string? currency, Guid? costCenter, CancellationToken ct)
    {
        var a = await OpenAsync(company, earlier, ct); var b = await OpenAsync(company, later, ct); currency = Currency(currency);
        if (costCenter == Guid.Empty) throw new ArgumentException("Choose a valid cost center.");
        if (costCenter.HasValue && !await db.AccountingDimensionMembers.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == company && x.Id == costCenter, ct)) throw new KeyNotFoundException();
        var all = a.Preview.Values.Concat(b.Preview.Values).Where(x => (currency == null || x.Currency == currency) && (costCenter == null || x.CostCenterId == costCenter))
            .Select(x => (x.MonthUtc, x.AccountId, x.CostCenterId, x.Currency)).Distinct().OrderBy(x => x.MonthUtc).ThenBy(x => x.AccountId).ThenBy(x => x.CostCenterId).ThenBy(x => x.Currency);
        var rows = all.Select(key => { FinanceForecastValue? Find(Revision r) => r.Preview.Values.SingleOrDefault(x => (x.MonthUtc, x.AccountId, x.CostCenterId, x.Currency) == key);
            var left = Find(a); var right = Find(b); return new FinanceForecastComparisonRow(key.MonthUtc, key.AccountId, key.CostCenterId, key.Currency,
                left?.Actual, right?.Actual, left?.Forecast, right?.Forecast, FinanceRollingPlanningCalculation.Difference(right?.Forecast, left?.Forecast), right?.Explanation ?? "Absent from later version"); }).ToArray();
        return new(a, b, rows);
    }
    public async Task<FinancePlanningExport> ComparisonExportAsync(Guid company, Guid earlier, Guid later, string? currency, Guid? costCenter, CancellationToken ct)
    {
        var r = await CompareAsync(company, earlier, later, currency, costCenter, ct);
        static string Cell(object? x) { var s = Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (s.Length > 0 && (s[0] is '=' or '+' or '@' or '\t' or '\r' || s[0] == '-' && !decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\""; }
        var csv = new StringBuilder("Month,Account,Cost center,Currency,Earlier actual,Later actual,Earlier forecast,Later forecast,Change,Explanation,Earlier version,Later version\r\n");
        static string? Number(decimal? x) => x?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        foreach(var x in r.Rows) csv.AppendLine(string.Join(",", new object?[] {x.MonthUtc.ToString("yyyy-MM"),
            r.Later.Preview.Report.Accounts.Concat(r.Earlier.Preview.Report.Accounts).FirstOrDefault(a=>a.Id==x.AccountId)?.Name,
            r.Later.Preview.Report.CostCenters.Concat(r.Earlier.Preview.Report.CostCenters).FirstOrDefault(a=>a.Id==x.CostCenterId)?.Name,
            x.Currency,Number(x.EarlierActual),Number(x.LaterActual),Number(x.EarlierForecast),Number(x.LaterForecast),Number(x.Change),x.Explanation,r.Earlier.Summary.Name,r.Later.Summary.Name}.Select(Cell)));
        return new("finance-forecast-comparison.csv",csv.ToString(),Hash(Json(r)));
    }
}

