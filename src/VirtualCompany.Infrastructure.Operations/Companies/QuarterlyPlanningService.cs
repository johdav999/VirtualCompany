using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Infrastructure.Companies;

public sealed class QuarterlyPlanningService(VirtualCompanyDbContext db, ICompanyMembershipContextResolver memberships,
    ICompanyGoalQueryService goals, IMonthlyReviewSnapshotService monthly, ITodayWorkspaceLensResolver lenses,
    IAuditEventWriter audit, TimeProvider clock) : IQuarterlyPlanningService
{
    public async Task<QuarterlyPlanningOptions> OptionsAsync(Guid company, int year, int quarter, CancellationToken ct)
    {
        await RequireAsync(company, ct); var period = await PeriodAsync(company, year, quarter, ct);
        var ownerIds = await db.CompanyMemberships.Where(x => x.CompanyId == company && x.Status == CompanyMembershipStatus.Active).Select(x => x.UserId).ToListAsync(ct);
        var owners = await db.Users.Where(x => ownerIds.Contains(x.Id)).OrderBy(x => x.DisplayName).Select(x => new PlanningOwner(x.Id, x.DisplayName)).ToListAsync(ct);
        var evidence = new List<PlanningEvidenceOption>();
        var start = TimeZoneInfo.ConvertTimeFromUtc(period.StartUtc, TimeZoneInfo.FindSystemTimeZoneById(period.Timezone));
        foreach (var lens in TodayWorkspaceLenses.Ordered)
        {
            for (var i = 0; i < (period.StartDay == 1 ? 3 : 4); i++)
            {
                var month = new DateTime(start.Year, start.Month, 1).AddMonths(i);
                try
                {
                    var list = await monthly.ListAsync(company, lens, month.Year, month.Month, 0, 10, ct);
                    evidence.AddRange(list.Items.Select(x => new PlanningEvidenceOption(x.Id, x.Lens, x.Year, x.Month, x.Revision)));
                }
                catch (UnauthorizedAccessException) { /* A planning role never grants departmental detail. */ }
            }
        }
        return new(company, period, await goals.ListAsync(company, null, ct), owners, evidence.DistinctBy(x => x.Id).ToArray(), await InitiativesAsync(company, ct));
    }
    public async Task<QuarterReviewPreview> PreviewAsync(Guid company, PreviewQuarterReview proposal, CancellationToken ct)
    {
        await RequireAsync(company, ct); var period = await PeriodAsync(company, proposal.FiscalYear, proposal.Quarter, ct);
        ValidateShape(proposal); var nativeGoals = (await goals.ListAsync(company, null, ct)).ToDictionary(x => x.Id);
        var initiatives = await InitiativesAsync(company, ct); var ids = initiatives.ToDictionary(x => x.Id);
        var owners = await db.CompanyMemberships.Where(x => x.CompanyId == company && x.Status == CompanyMembershipStatus.Active)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.DisplayName }).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var sources = new Dictionary<Guid, MonthlyReviewSnapshotDto>();
        async Task<MonthlyReviewSnapshotDto> Source(Guid id)
        { if (!sources.TryGetValue(id, out var s)) sources[id] = s = await monthly.OpenAsync(company, id, ct); return s; }
        var progress = new List<QuarterObjectiveProgress>();
        foreach (var row in proposal.Objectives)
        {
            if (!nativeGoals.TryGetValue(row.GoalId, out var goal)) throw new KeyNotFoundException("Company objective not found.");
            if (goal.Version != row.GoalVersion) throw new CompanyOperatingConcurrencyException("The native objective changed. Reload it before reviewing.");
            if (!owners.TryGetValue(row.OwnerUserId, out var owner)) throw new ArgumentException("Each quarter objective needs an active company member as accountable owner.");
            if (goal.MetricKey is null || !string.Equals(goal.MetricUnit, row.Unit, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The target unit must match the native objective's measure unit.");
            if (goal.StartUtc >= period.EndUtc || goal.TargetUtc <= period.StartUtc) throw new ArgumentException("The objective does not overlap this fiscal quarter.");
            ValidateMilestones(row.Milestones, period);
            var values = new List<(DateTime Start, DateTime End, decimal? Value)>(); var links = new List<string>();
            foreach (var link in row.Measures)
            {
                var s = await Source(link.SnapshotId); var m = s.Workspace.Review?.Measures.SingleOrDefault(x => x.Key == link.MeasureKey);
                if (m is null || m.Key != goal.MetricKey || !string.Equals(m.Unit, row.Unit, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Snapshot measure, native objective and target unit must agree.");
                if (s.Workspace.Period.StartUtc < period.StartUtc || s.Workspace.Period.EndUtc > period.EndUtc) throw new ArgumentException("Monthly evidence must be wholly inside the selected quarter; partial months cannot be inferred.");
                values.Add((s.Workspace.Period.StartUtc, s.Workspace.Period.EndUtc, m.Kind == "actual" ? m.Actual : null)); links.Add(EvidenceLink(company, s));
            }
            foreach (var id in row.InitiativeIds) if (!ids.TryGetValue(id, out var initiative) || initiative.GoalId != row.GoalId) throw new ArgumentException("Select native initiatives for this objective in this company.");
            var risks = Risks(row.InitiativeIds, ids, period);
            var actual = QuarterlyPlanningCalculation.Aggregate(goal.MetricKey, values, period);
            progress.Add(new(row, goal.Name, owner, actual, QuarterlyPlanningCalculation.Progress(actual, row.Baseline, row.Target),
                Outcome(actual, row.Target, row.Direction, period.EndUtc), actual.HasValue ? "Three reconciled, contiguous monthly actuals" : "Evidence unavailable: incomplete months or non-additive measure; ratios and forecasts are not summed.", links, risks));
        }
        foreach (var r in proposal.Resources)
        {
            if (!proposal.Objectives.Any(x => x.GoalId == r.GoalId && x.OwnerUserId == r.OwnerUserId)) throw new ArgumentException("Resource commitments must name a reviewed objective and its accountable owner.");
            var s = await Source(r.SnapshotId);
            if (s.Workspace.Period.EndUtc <= period.StartUtc || s.Workspace.Period.StartUtc >= period.EndUtc) throw new ArgumentException("Resource evidence must overlap the fiscal quarter.");
        }
        var conflicts = QuarterlyPlanningCalculation.Conflicts(proposal.Resources);
        var fingerprint = Hash(new { company, period, proposal, progress, conflicts, sources = sources.OrderBy(x => x.Key).Select(x => new { x.Key, x.Value.Checksum }) });
        return new(company, period, proposal, progress, conflicts, fingerprint);
    }
    public async Task<QuarterReviewDocument> SaveAsync(Guid company, SaveQuarterReview command, CancellationToken ct)
    {
        var member = await RequireAsync(company, ct);
        if (command.RequestId == Guid.Empty || command.ExpectedRevision < 0) throw new ArgumentException("A stable request identity and expected revision are required.");
        var commandHash = Hash(new { command.Proposal, command.PreviousId, command.ExpectedRevision, command.ExpectedFingerprint });
        var repeated = await Reviews(company).SingleOrDefaultAsync(x => x.RequestId == command.RequestId, ct);
        if (repeated is not null)
        { if (repeated.CommandHash != commandHash || repeated.AuthorUserId != member.UserId) throw Conflict(); return await OpenAsync(company, repeated.Id, ct); }
        var preview = await PreviewAsync(company, command.Proposal, ct);
        if (preview.Fingerprint != command.ExpectedFingerprint) throw new CompanyOperatingConcurrencyException("Sources or commitments changed. Preview the proposal again.");
        var latest = await Reviews(company).Where(x => x.FiscalYear == command.Proposal.FiscalYear && x.Quarter == command.Proposal.Quarter).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
        if (latest?.Id != command.PreviousId || (latest?.Revision ?? 0) != command.ExpectedRevision) throw Conflict();
        var id = Guid.NewGuid();
        var review = new QuarterlyReview { Id = id, CompanyId = company, FiscalYear = preview.Period.FiscalYear, Quarter = preview.Period.Quarter,
            Revision = (latest?.Revision ?? 0) + 1, PreviousId = latest?.Id, RequestId = command.RequestId, AuthorUserId = member.UserId,
            SavedUtc = clock.GetUtcNow().UtcDateTime, StartUtc = preview.Period.StartUtc, EndUtc = preview.Period.EndUtc, Timezone = preview.Period.Timezone,
            Currency = preview.Period.Currency, CalendarVersion = preview.Period.CalendarVersion, StartMonth = preview.Period.StartMonth, StartDay = preview.Period.StartDay,
            Notes = command.Proposal.Notes, Fingerprint = preview.Fingerprint, CommandHash = commandHash };
        foreach (var p in preview.Objectives)
        {
            var row = p.Objective; var oid = Guid.NewGuid();
            var objective = new QuarterlyObjective { Id = oid, CompanyId = company, ReviewId = id, GoalId = row.GoalId, GoalVersion = row.GoalVersion,
                Name = p.Name, OwnerUserId = row.OwnerUserId, OwnerName = p.Owner, Baseline = row.Baseline, Target = row.Target, Unit = row.Unit, Direction = row.Direction };
            foreach (var m in row.Measures) objective.Measures.Add(new() { Id = Guid.NewGuid(), CompanyId = company, ObjectiveId = oid, SnapshotId = m.SnapshotId,
                MeasureKey = m.MeasureKey, SnapshotChecksum = await SnapshotChecksum(company, m.SnapshotId, ct) });
            objective.Milestones.AddRange(row.Milestones.Select(m => new QuarterlyMilestone { Id = Guid.NewGuid(), CompanyId = company, ObjectiveId = oid, Title = m.Title, DueUtc = m.DueUtc, Status = m.Status }));
            objective.Initiatives.AddRange(row.InitiativeIds.Select(i => new QuarterlyInitiativeLink { Id = Guid.NewGuid(), CompanyId = company, ObjectiveId = oid, InitiativeId = i }));
            review.Objectives.Add(objective);
        }
        foreach (var r in command.Proposal.Resources) review.Resources.Add(new() { Id = Guid.NewGuid(), CompanyId = company, ReviewId = id, GoalId = r.GoalId,
            OwnerUserId = r.OwnerUserId, Pool = r.Pool.Trim(), AvailableHours = r.AvailableHours, ProposedHours = r.ProposedHours, SnapshotId = r.SnapshotId,
            SnapshotChecksum = await SnapshotChecksum(company, r.SnapshotId, ct), Rationale = r.Rationale });
        db.Set<QuarterlyReview>().Add(review);
        await audit.WriteAsync(new AuditEventWriteRequest(company, "user", member.UserId, "company.quarter.reviewed", "quarterly_review", id.ToString("N"), "succeeded",
            "Quarter objectives and explicit resource proposal reviewed; no execution authorized.", Metadata: new Dictionary<string, string?> {
                ["previousId"] = latest?.Id.ToString(), ["revision"] = review.Revision.ToString(), ["fingerprint"] = preview.Fingerprint,
                ["conflictingPools"] = preview.Conflicts.Count(x => x.Shortfall > 0 || x.Explanation.Contains("disagree")).ToString() }), ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await Reviews(company).SingleOrDefaultAsync(x => x.RequestId == command.RequestId, ct);
            if (winner is not null && winner.CommandHash == commandHash && winner.AuthorUserId == member.UserId) return await OpenAsync(company, winner.Id, ct);
            if (winner is not null || await Reviews(company).AnyAsync(x => x.FiscalYear == review.FiscalYear && x.Quarter == review.Quarter && x.Revision == review.Revision, ct)) throw Conflict();
            throw;
        }
        return new(Summary(review), preview, Changes(latest, review));
    }
    public async Task<QuarterReviewDocument> OpenAsync(Guid company, Guid id, CancellationToken ct)
    {
        await RequireAsync(company, ct); var r = await Reviews(company).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Quarter review not found.");
        var period = Period(r); var input = Input(r); var progress = new List<QuarterObjectiveProgress>();
        var initiatives = (await InitiativesAsync(company, ct)).ToDictionary(x => x.Id);
        foreach (var o in r.Objectives)
        {
            var values = new List<(DateTime Start, DateTime End, decimal? Value)>(); var links = new List<string>(); var available = true;
            foreach (var l in o.Measures)
            {
                try
                {
                    var s = await monthly.OpenAsync(company, l.SnapshotId, ct);
                    if (s.Checksum != l.SnapshotChecksum) throw new InvalidDataException("Quarter source checksum differs from the reviewed version.");
                    var m = s.Workspace.Review?.Measures.SingleOrDefault(x => x.Key == l.MeasureKey);
                    if (m is null || !string.Equals(m.Unit, o.Unit, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Reviewed measure cannot be reconciled.");
                    values.Add((s.Workspace.Period.StartUtc, s.Workspace.Period.EndUtc, m.Kind == "actual" ? m.Actual : null)); links.Add(EvidenceLink(company, s));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException) { available = false; }
            }
            var actual = available && o.Measures.Count > 0 ? QuarterlyPlanningCalculation.Aggregate(o.Measures[0].MeasureKey, values, period) : null;
            progress.Add(new(input.Objectives.Single(x => x.GoalId == o.GoalId), o.Name, o.OwnerName, actual,
                QuarterlyPlanningCalculation.Progress(actual, o.Baseline, o.Target), Outcome(actual, o.Target, o.Direction, period.EndUtc),
                actual.HasValue ? "Retained monthly measures reconciled" : "Evidence unavailable or protected; no missed-target conclusion.", links, Risks(o.Initiatives.Select(x => x.InitiativeId).ToArray(), initiatives, period)));
        }
        // Resource source reproduction is also checked on every open. Do not turn plan membership into source access.
        foreach (var resource in r.Resources)
        { try { var s = await monthly.OpenAsync(company, resource.SnapshotId, ct); if (s.Checksum != resource.SnapshotChecksum) throw new InvalidDataException("Resource source changed."); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException) { progress = progress.Select(x => x.Objective.GoalId == resource.GoalId ? x with { EvidenceState = "Resource evidence unavailable or protected" } : x).ToList(); } }
        var prior = r.PreviousId.HasValue ? await Reviews(company).SingleAsync(x => x.Id == r.PreviousId, ct) : null;
        return new(Summary(r), new(company, period, input, progress, QuarterlyPlanningCalculation.Conflicts(input.Resources), r.Fingerprint), Changes(prior, r));
    }
    public async Task<IReadOnlyList<QuarterReviewSummary>> HistoryAsync(Guid company, int year, int quarter, CancellationToken ct)
    { await RequireAsync(company, ct); _ = await PeriodAsync(company, year, quarter, ct); return (await Reviews(company).Where(x => x.FiscalYear == year && x.Quarter == quarter).OrderByDescending(x => x.Revision).Take(50).ToListAsync(ct)).Select(Summary).ToArray(); }
    private IQueryable<QuarterlyReview> Reviews(Guid company) => db.Set<QuarterlyReview>().AsNoTracking().Where(x => x.CompanyId == company)
        .Include(x => x.Resources).Include(x => x.Objectives).ThenInclude(x => x.Measures)
        .Include(x => x.Objectives).ThenInclude(x => x.Milestones).Include(x => x.Objectives).ThenInclude(x => x.Initiatives).AsSplitQuery();
    private async Task<ResolvedCompanyMembershipContext> RequireAsync(Guid company, CancellationToken ct)
    {
        var m = await memberships.ResolveAsync(company, ct) ?? throw new UnauthorizedAccessException();
        if (m.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager)) throw new UnauthorizedAccessException("Company planning requires manager access.");
        var resolution = await lenses.ResolveAsync(company, "company", ct);
        if (resolution.ActiveLens != "company" || !resolution.AvailableLenses.Any(x => x.Lens == "company" && (x.IsPrimary || x.IsExecutiveOversight))) throw new UnauthorizedAccessException("Company performance responsibility is required for coordinated planning.");
        return m;
    }
    public async Task<PlanningPeriod> PeriodAsync(Guid company, int year, int quarter, CancellationToken ct)
    {
        var config = await db.AccountingConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == company, ct)
            ?? throw new ArgumentException("Configure the company fiscal calendar before planning.");
        var timezone = await db.Companies.AsNoTracking().Where(x => x.Id == company).Select(x => x.Timezone).SingleAsync(ct);
        if (string.IsNullOrWhiteSpace(timezone)) throw new ArgumentException("Configure the company timezone before planning.");
        return QuarterlyPlanningCalculation.Period(year, quarter, config.FiscalYearStartMonth, config.FiscalYearStartDay, timezone, config.BaseCurrency, config.Version);
    }
    private async Task<IReadOnlyList<PlanningInitiative>> InitiativesAsync(Guid company, CancellationToken ct)
    {
        var rows = await db.OperatingInitiatives.AsNoTracking().Where(x => x.CompanyId == company).OrderBy(x => x.Id).Take(1001).ToListAsync(ct);
        if (rows.Count > 1000) throw new ArgumentException("Narrow the native operating initiative portfolio before coordinated planning; more than 1,000 initiatives cannot be represented completely.");
        var deps = await db.OperatingPlanDependencies.AsNoTracking().Where(x => x.CompanyId == company).ToListAsync(ct);
        return rows.Select(x => new PlanningInitiative(x.Id, x.PlanId, x.GoalId, x.Title, x.Status.ToStorageValue(), x.OwnerAgentId, x.TargetUtc,
            deps.Where(d => d.InitiativeId == x.Id).Select(d => d.DependsOnInitiativeId).ToArray())).ToArray();
    }
    private Task<string> SnapshotChecksum(Guid company, Guid id, CancellationToken ct) => db.MonthlyReviewSnapshots.Where(x => x.CompanyId == company && x.Id == id).Select(x => x.Checksum).SingleAsync(ct);
    private string Outcome(decimal? actual, decimal target, string direction, DateTime end) => actual is null ? "Evidence unavailable" :
        (direction == "at_least" ? actual >= target : actual <= target) ? "Target met" : clock.GetUtcNow().UtcDateTime >= end ? "Missed target" : "Below target; quarter open";
    private static IReadOnlyList<string> Risks(IReadOnlyList<Guid> selected, IReadOnlyDictionary<Guid, PlanningInitiative> all, PlanningPeriod period)
    {
        var risks = new List<string>();
        foreach (var id in selected)
        {
            if (!all.TryGetValue(id, out var row)) { risks.Add("A linked native initiative is unavailable."); continue; }
            if (row.Status == "blocked") risks.Add(row.Title + ": blocked.");
            if (row.TargetUtc >= period.EndUtc) risks.Add(row.Title + ": commitment falls after this quarter.");
            foreach (var dep in row.DependsOn)
                if (!all.TryGetValue(dep, out var d)) risks.Add(row.Title + ": dependency unavailable.");
                else if (d.Status != "completed") risks.Add($"{row.Title} depends on {d.Title} ({d.Status}){(d.PlanId != row.PlanId ? " in another operating plan" : "")}.");
        }
        return risks;
    }
    private static void ValidateShape(PreviewQuarterReview p)
    {
        if (p.Objectives is null || p.Resources is null || p.Objectives.Count is < 1 or > 50 || p.Resources.Count > 100 || p.Notes is null || p.Notes.Length > 4000 || p.Objectives.Select(x => x.GoalId).Distinct().Count() != p.Objectives.Count) throw new ArgumentException("Review 1–50 distinct objectives, at most 100 resource commitments, and notes up to 4,000 characters.");
        foreach (var o in p.Objectives)
            if (o.Measures is null || o.Milestones is null || o.InitiativeIds is null || o.Measures.Count > 3 || o.Milestones.Count > 20 || o.InitiativeIds.Count > 50 || o.Measures.Select(x => x.SnapshotId).Distinct().Count() != o.Measures.Count || o.InitiativeIds.Distinct().Count() != o.InitiativeIds.Count ||
                Math.Abs(o.Baseline) >= 1000000000000000m || Math.Abs(o.Target) >= 1000000000000000m || decimal.Round(o.Baseline, 4) != o.Baseline || decimal.Round(o.Target, 4) != o.Target ||
                string.IsNullOrWhiteSpace(o.Unit) || o.Unit.Length > 64 || o.Direction is not ("at_least" or "at_most") || o.Measures.Any(x => string.IsNullOrWhiteSpace(x.MeasureKey) || x.MeasureKey.Length > 128)) throw new ArgumentException("Check objective units, direction, evidence and milestone bounds; values support four decimal places.");
        foreach (var r in p.Resources)
            if (string.IsNullOrWhiteSpace(r.Pool) || r.Pool.Length > 128 || r.AvailableHours < 0 || r.ProposedHours < 0 || r.AvailableHours > 1000000 || r.ProposedHours > 1000000 || decimal.Round(r.AvailableHours, 4) != r.AvailableHours || decimal.Round(r.ProposedHours, 4) != r.ProposedHours || string.IsNullOrWhiteSpace(r.Rationale) || r.Rationale.Length > 2000) throw new ArgumentException("Each resource pool needs bounded nonnegative hours with up to four decimal places and an explicit rationale.");
    }
    private static void ValidateMilestones(IReadOnlyList<QuarterMilestone> rows, PlanningPeriod p)
    { if (rows.Any(x => string.IsNullOrWhiteSpace(x.Title) || x.Title.Length > 200 || x.DueUtc.Kind != DateTimeKind.Utc || x.DueUtc < p.StartUtc || x.DueUtc >= p.EndUtc || x.Status is not ("planned" or "in_progress" or "completed" or "blocked"))) throw new ArgumentException("Milestones need a title, UTC date inside the quarter and a supported status."); }
    private static string EvidenceLink(Guid company, MonthlyReviewSnapshotDto s) => $"/dashboard?companyId={company:D}&period=month&lens={s.Summary.Lens}&year={s.Summary.Year}&month={s.Summary.Month}&snapshot={s.Summary.Id:D}";
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    private static CompanyOperatingConcurrencyException Conflict() => new("This quarter already has a newer reviewed revision, or the request identity was reused. Reload the latest review.");
    private static PlanningPeriod Period(QuarterlyReview r) => new(r.FiscalYear, r.Quarter, r.StartUtc, r.EndUtc, r.Timezone, r.Currency, r.CalendarVersion, r.StartMonth, r.StartDay);
    private static QuarterReviewSummary Summary(QuarterlyReview r) => new(r.Id, r.CompanyId, r.FiscalYear, r.Quarter, r.Revision, r.PreviousId, r.AuthorUserId, r.SavedUtc, r.Notes);
    private static PreviewQuarterReview Input(QuarterlyReview r) => new(r.FiscalYear, r.Quarter,
        r.Objectives.Select(o => new QuarterObjectiveInput(o.GoalId, o.GoalVersion, o.OwnerUserId, o.Baseline, o.Target, o.Unit, o.Direction,
            o.Measures.OrderBy(x => x.SnapshotId).Select(m => new QuarterMeasureLink(m.SnapshotId, m.MeasureKey)).ToArray(),
            o.Milestones.OrderBy(x => x.DueUtc).Select(m => new QuarterMilestone(m.Title, m.DueUtc, m.Status)).ToArray(), o.Initiatives.Select(x => x.InitiativeId).Order().ToArray())).OrderBy(x => x.GoalId).ToArray(),
        r.Resources.Select(x => new QuarterResourceInput(x.GoalId, x.OwnerUserId, x.Pool, x.AvailableHours, x.ProposedHours, x.SnapshotId, x.Rationale)).OrderBy(x => x.Pool).ThenBy(x => x.GoalId).ToArray(), r.Notes);
    private static IReadOnlyList<string> Changes(QuarterlyReview? prior, QuarterlyReview current)
    {
        if (prior is null) return ["Initial reviewed objectives, milestones and explicit resource assumptions."];
        var changes = new List<string>();
        if (prior.StartUtc != current.StartUtc || prior.EndUtc != current.EndUtc || prior.CalendarVersion != current.CalendarVersion) changes.Add("Fiscal calendar binding changed; prior reviewed dates are preserved.");
        foreach (var o in current.Objectives)
        {
            var before = prior.Objectives.SingleOrDefault(x => x.GoalId == o.GoalId);
            if (before is null) changes.Add(o.Name + ": objective added.");
            else { if (before.Target != o.Target || before.Baseline != o.Baseline) changes.Add($"{o.Name}: baseline {before.Baseline} → {o.Baseline}; target {before.Target} → {o.Target} {o.Unit}.");
                if (before.OwnerUserId != o.OwnerUserId) changes.Add($"{o.Name}: owner {before.OwnerName} → {o.OwnerName}.");
                if (JsonSerializer.Serialize(Input(prior).Objectives.Single(x => x.GoalId == o.GoalId).Milestones) != JsonSerializer.Serialize(Input(current).Objectives.Single(x => x.GoalId == o.GoalId).Milestones)) changes.Add(o.Name + ": quarterly milestones changed."); }
        }
        foreach (var o in prior.Objectives.Where(x => current.Objectives.All(c => c.GoalId != x.GoalId))) changes.Add(o.Name + ": objective removed from this quarter.");
        if (JsonSerializer.Serialize(Input(prior).Resources) != JsonSerializer.Serialize(Input(current).Resources)) changes.Add("Explicit resource allocations or evidence changed.");
        if (prior.Notes != current.Notes) changes.Add("Reviewed outlook and decision notes changed.");
        return changes.Count > 0 ? changes : ["Reviewed evidence links or initiative commitments updated; no target/owner/resource/outlook change."];
    }
}
