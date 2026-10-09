using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TimeZoneConverter;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyWeeklyWorkspaceQueryService(VirtualCompanyDbContext db,
    ITodayWorkspaceLensResolver lenses, IEnumerable<IWeeklyWorkspaceContributor> contributors,
    TimeProvider time, ILogger<CompanyWeeklyWorkspaceQueryService> logger) : IWeeklyWorkspaceQueryService
{
    public async Task<WeeklyWorkspaceDto> GetAsync(GetWeeklyWorkspaceQuery query, CancellationToken token)
    {
        var resolution = await lenses.ResolveAsync(query.CompanyId, query.Lens, token);
        var company = await db.Companies.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == query.CompanyId, token);
        var diagnostics = new List<TodayWorkspaceDiagnosticDto>();
        TimeZoneInfo zone;
        try { zone = TZConvert.GetTimeZoneInfo(company.Timezone ?? "UTC"); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            diagnostics.Add(new("calendar", "weekly_timezone_fallback", "Company timezone is invalid; UTC is used. Review company settings."));
        }
        var weekStartsOn = DayOfWeek.Monday;
        if (company.Settings.Extensions.TryGetValue("weekStartsOn", out var configured))
        {
            if (!Enum.TryParse<DayOfWeek>(configured?.ToString(), true, out weekStartsOn) || !Enum.IsDefined(weekStartsOn))
                throw new ArgumentException("Company weekStartsOn must be a day of the week.");
        }
        var now = time.GetUtcNow().UtcDateTime;
        var period = WeeklyWorkspacePeriod.Resolve(now, zone, query.Week, weekStartsOn);
        var selected = resolution.ActiveLens == TodayWorkspaceLenses.Company
            ? resolution.AvailableLenses : resolution.AvailableLenses.Where(x => x.Lens == resolution.ActiveLens).ToList();
        var owners = contributors.ToDictionary(x => x.Lens, StringComparer.OrdinalIgnoreCase);
        var results = new List<WeeklyWorkspaceContribution>();
        foreach (var access in selected)
        {
            try
            {
                if (!owners.TryGetValue(access.Lens, out var owner)) throw new InvalidOperationException("Weekly contributor is unavailable.");
                var result = await owner.ContributeAsync(new(query.CompanyId, resolution.UserId, now, period, access), token);
                if (result.Lens != access.Lens) throw new InvalidOperationException("Weekly contributor returned an incompatible responsibility.");
                results.Add(result with { ResponsiblePerson = access.ResponsiblePerson, WorkingAgent = access.WorkingAgent });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Weekly projection {Lens} failed for company {CompanyId}.", access.Lens, query.CompanyId);
                diagnostics.Add(new(access.Lens, "weekly_projection_failed", $"{access.Label} weekly sources are temporarily unavailable. Retry the review."));
                results.Add(new(access.Lens, access.Label, [], [], ["Source unavailable; no measured zero is inferred."]));
            }
        }
        return new(query.CompanyId, resolution.CompanyName, resolution.ActiveLens,
            resolution.AvailableLenses.Select(x => new TodayWorkspaceLensDto(x.Lens, x.Label, x.Lens == resolution.DefaultLens, x.AvailabilityReason)).ToList(),
            period, results, now, diagnostics.Count > 0 || results.Any(x => x.CoverageGaps.Count > 0 || x.Metrics.Any(m => !m.IsComplete)),
            diagnostics, $"{weekStartsOn}–{(DayOfWeek)(((int)weekStartsOn + 6) % 7)} calendar weeks in {zone.Id}; independent of fiscal-year boundaries. " +
            "Activity uses [start, cutoff); balances use their disclosed observation/cutoff. Current risks are observed now, not reconstructed past states.");
    }
}
