namespace VirtualCompany.Application.Orchestration;
public interface IQuarterlyPlanningService
{
    Task<QuarterlyPlanningOptions> OptionsAsync(Guid companyId, int fiscalYear, int quarter, CancellationToken ct);
    Task<QuarterReviewPreview> PreviewAsync(Guid companyId, PreviewQuarterReview proposal, CancellationToken ct);
    Task<QuarterReviewDocument> SaveAsync(Guid companyId, SaveQuarterReview command, CancellationToken ct);
    Task<QuarterReviewDocument> OpenAsync(Guid companyId, Guid id, CancellationToken ct);
    Task<IReadOnlyList<QuarterReviewSummary>> HistoryAsync(Guid companyId, int fiscalYear, int quarter, CancellationToken ct);
}

public static class QuarterlyPlanningCalculation
{
    public static PlanningPeriod Period(int year, int quarter, int month, int day, string timezone, string currency, long version)
    {
        if (year is < 2000 or > 2099 || quarter is < 1 or > 4) throw new ArgumentException("Choose a fiscal year from 2000 to 2099 and quarter from 1 to 4.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        // AddMonths is always relative to the fiscal anchor, including non-month-start fiscal calendars.
        var anchor = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified);
        return new(year, quarter, TimeZoneInfo.ConvertTimeToUtc(anchor.AddMonths((quarter - 1) * 3), zone),
            TimeZoneInfo.ConvertTimeToUtc(anchor.AddMonths(quarter * 3), zone), timezone, currency, version, month, day);
    }
    public static decimal? Aggregate(string key, IReadOnlyList<(DateTime Start, DateTime End, decimal? Value)> measures, PlanningPeriod period)
    {
        // Only additive native actuals can be summed. Ratios, stock balances and forecasts cannot be inferred as quarterly actuals.
        if (key is not ("finance.revenue" or "finance.net_result" or "sales.stage_movement" or "support.volume")) return null;
        var rows = measures.OrderBy(x => x.Start).ToArray();
        if (rows.Length != 3 || rows.Any(x => x.Value is null) || rows[0].Start != period.StartUtc || rows[^1].End != period.EndUtc ||
            rows.Zip(rows.Skip(1)).Any(x => x.First.End != x.Second.Start)) return null;
        return rows.Sum(x => x.Value!.Value);
    }
    public static decimal? Progress(decimal? actual, decimal baseline, decimal target) =>
        actual is null || target == baseline ? null : (actual - baseline) * 100m / (target - baseline);
    public static IReadOnlyList<QuarterResourceConflict> Conflicts(IReadOnlyList<QuarterResourceInput> rows) =>
        rows.GroupBy(x => x.Pool.Trim(), StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var available = g.Min(x => x.AvailableHours); var total = g.Sum(x => x.ProposedHours);
            return new QuarterResourceConflict(g.Key, available, total, Math.Max(0, total - available),
                g.Select(x => x.AvailableHours).Distinct().Count() > 1
                    ? "Pool availability assumptions disagree; the smallest declared capacity is used."
                    : total > available ? "Proposed commitments exceed this pool's declared capacity." : "Commitments fit the declared pool capacity.");
        }).ToArray();
}
