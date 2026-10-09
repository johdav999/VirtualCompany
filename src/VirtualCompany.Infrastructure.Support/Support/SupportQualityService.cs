using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Support;

public sealed partial class SupportQualityService(VirtualCompanyDbContext db, ITodayWorkspaceLensResolver access,
    ISupportSlaPolicyService sla, IAuditEventWriter audit, TimeProvider? clock=null, ILogger<SupportQualityService>? logger=null) : ISupportQualityService
{
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
    private DateTime Now=>(clock??TimeProvider.System).GetUtcNow().UtcDateTime;
    private const int CaseLimit=2000, EventLimit=20000;
    private async Task<TodayWorkspaceLensResolution> Require(Guid company,CancellationToken ct)
    {
        if(company==Guid.Empty) throw new ArgumentException("Choose a company.");
        var scope=await access.ResolveAsync(company,TodayWorkspaceLenses.Customers,ct);
        if(scope.ActiveLens!=TodayWorkspaceLenses.Customers || !scope.AvailableLenses.Any(x=>x.Lens==TodayWorkspaceLenses.Customers)) throw new UnauthorizedAccessException("Current Support responsibility is required.");
        return scope;
    }
    public async Task<SupportQualityReport> ReportAsync(Guid company, SupportQualityQuery query,CancellationToken ct)
    {
        var scope=await Require(company,ct); query=Normalize(query); var now=Now;
        var owner=await db.Users.AsNoTracking().Where(x=>x.Id==scope.UserId).Select(x=>x.DisplayName).SingleAsync(ct);
        var nativeCompany=await db.Companies.AsNoTracking().SingleAsync(x=>x.Id==company,ct);
        var zone=TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(nativeCompany.Timezone)?"UTC":nativeCompany.Timezone);
        var month=MonthlyWorkspacePeriod.Resolve(now,zone,query.Year,query.Month);
        if(month.StartUtc>now) throw new ArgumentException("Quality reports need an observed month.");
        var calendar=await sla.GetCalendarAsync(company,ct);
        var followup=now.AddTicks(1); // Observation is inclusive; period/cohort intervals remain half-open.
        var period=new SupportQualityPeriod(month.StartUtc,month.EndUtc,now<month.EndUtc?followup:month.EndUtc,zone.Id);
        var all=await db.SupportCases.AsNoTracking().Where(x=>x.CompanyId==company && x.CreatedUtc<=now).OrderBy(x=>x.CreatedUtc).ThenBy(x=>x.Id).Take(CaseLimit+1).ToListAsync(ct);
        var events=await db.SupportCaseEvents.AsNoTracking().Where(x=>x.CompanyId==company && x.SupportCase.CompanyId==company && x.OccurredUtc<=now)
            .OrderBy(x=>x.OccurredUtc).ThenBy(x=>x.StateSequence).ThenBy(x=>x.Id).Take(EventLimit+1).ToListAsync(ct);
        var corrections=await db.SupportIssueGroupingRevisions.AsNoTracking().Where(x=>x.CompanyId==company).OrderByDescending(x=>x.SavedUtc).ThenByDescending(x=>x.Id).Take(EventLimit+1).ToListAsync(ct);
        var complete=all.Count<=CaseLimit && events.Count<=EventLimit && corrections.Count<=EventLimit;
        all=all.Take(CaseLimit).ToList(); events=events.Take(EventLimit).ToList(); corrections=corrections.Take(EventLimit).ToList();
        var cases=all.Where(x=>query.Category==null || x.Category==query.Category).ToList();
        var rows=BuildRows(company,cases,events,corrections,period,followup,calendar,logger);
        var priorPeriod=new SupportQualityPeriod(month.ComparisonStartUtc,month.ComparisonEndUtc,month.ComparisonEndUtc,zone.Id);
        var prior=BuildRows(company,cases,events,corrections,priorPeriod,followup,calendar,logger);
        var backlog=new[]{month.ComparisonStartUtc,month.StartUtc,period.ActivityEndUtc}.Distinct().Select(cut=>BacklogAt(cases,events,cut,complete)).ToArray();
        const string definition="Arrivals: cases created in [month start, activity cutoff). Resolution cohort: distinct cases with recorded resolution events in that interval. Reopen numerator: cohort cases with any later recorded reopening before report as-of, including cross-period reopenings; repeated resolutions/reopenings count once per case. Follow-up exposure differs between months. Response sample: arrival-cohort cases with a recorded first sent response before activity cutoff; unanswered cases are excluded from mean durations, never counted as zero. Resolution duration: creation to first recorded resolution in the interval, including waiting. Recorded merged aliases before activity cutoff are excluded, and their evidence remains visible. Category is the current native category, not reconstructed historical classification.";
        var coverage=$"Up to {CaseLimit} cases and {EventLimit} lifecycle/grouping records per company. {(complete?"All bounded sources loaded.":"Source limit reached: totals are recorded sample values; exports and capacity are withheld.")} State coverage starts at each recorded state observation; old prose status notes never reconstruct missing history. Waiting and paused owners do not pause native SLA targets or elapsed durations. Business response minutes replay the current selected calendar, not historic calendar changes; replay is bounded to ten years and unavailable durations are excluded. Business sample: {rows.Count(x=>x.Arrival&&!x.Merged&&x.ResponseBusinessMinutes.HasValue)} cases. No employee-performance inference.";
        var groups=rows.Where(x=>(x.Arrival||x.ResolutionCohort)&&!x.Merged).GroupBy(x=>new{x.Group,x.ReviewedGroup}).Select(g=>new SupportIssueGroup(g.Key.Group,g.Key.ReviewedGroup,g.Select(x=>x.Id).ToArray())).OrderByDescending(g=>g.CaseIds.Count).ThenBy(g=>g.Key).ToArray();
        var fingerprint=Hash(Json(new{company,query,calendar,complete,period.StartUtc,period.EndUtc,period.TimeZoneId,caseSources=cases.Select(x=>new{x.Id,x.CaseNumber,x.Subject,x.CreatedUtc,x.Category,x.Status,x.StateRevision,x.FirstResponseSentUtc,x.FirstResponseDueUtc,x.ResolutionDueUtc,x.ResolvedUtc}),
            events=events.Where(x=>cases.Any(c=>c.Id==x.SupportCaseId)).Select(x=>new{x.Id,x.SupportCaseId,x.EventType,x.Summary,x.OccurredUtc,x.FromStatus,x.ToStatus,x.IsStateBaseline,x.MergeTargetCaseId,x.StateSequence}),
            corrections=corrections.Select(x=>new{x.Id,x.SupportCaseId,x.Group,x.Reason,x.SavedUtc})}));
        if(!complete) logger?.LogWarning("Support quality source bound exceeded for {Company} {Year}-{Month}",company,query.Year,query.Month);
        return new(company,query,SupportCapacityCalculation.Version,now,followup,period,calendar,Metrics(rows),Metrics(prior),backlog,rows,groups,complete,coverage,definition,fingerprint,owner);
    }
    private static SupportQualityQuery Normalize(SupportQualityQuery q)
    {
        if(q.Year is <2000 or >2100 || q.Month is <1 or >12) throw new ArgumentException("Choose a valid quality report month.");
        return q with{Category=string.IsNullOrWhiteSpace(q.Category)?null:SupportCaseCategories.Normalize(q.Category)};
    }
    private static IReadOnlyList<SupportQualityCase> BuildRows(Guid company,List<SupportCase> cases,List<SupportCaseEvent> events,List<SupportIssueGroupingRevision> corrections,
        SupportQualityPeriod p,DateTime followup,SupportBusinessCalendarDto calendar,ILogger<SupportQualityService>? logger)
    {
        return cases.Select(c=>{
            var e=events.Where(x=>x.SupportCaseId==c.Id && x.OccurredUtc>=c.CreatedUtc).ToArray();
            bool Resolution(SupportCaseEvent x)=>!x.IsStateBaseline&&(x.EventType==SupportCaseEventTypes.Resolved || x.ToStatus==SupportCaseStatuses.Resolved);
            bool Reopen(SupportCaseEvent x)=>!x.IsStateBaseline&&(x.EventType==SupportCaseEventTypes.Reopened || x.ToStatus==SupportCaseStatuses.Reopened);
            var resolution=e.FirstOrDefault(x=>Resolution(x)&&x.OccurredUtc>=p.StartUtc&&x.OccurredUtc<p.ActivityEndUtc);
            var resolved=(DateTime?)resolution?.OccurredUtc;
            var reopened=resolution is not null?e.Where(x=>Reopen(x)&&(x.OccurredUtc>resolution.OccurredUtc||x.OccurredUtc==resolution.OccurredUtc&&x.StateSequence>0&&x.StateSequence>resolution.StateSequence)&&x.OccurredUtc<followup).Select(x=>(DateTime?)x.OccurredUtc).FirstOrDefault():null;
            var response=c.FirstResponseSentUtc is DateTime sent && sent>=c.CreatedUtc && sent<p.ActivityEndUtc ? (decimal?)decimal.Round((decimal)(sent-c.CreatedUtc).TotalMinutes,2):null;
            decimal? business=null;
            if(response.HasValue) { try { business=SupportBusinessTime.Minutes(c.CreatedUtc,c.FirstResponseSentUtc!.Value,calendar); } catch(ArgumentException ex) { logger?.LogWarning(ex,"Support business-time aggregation unavailable for company {Company}, case {Case}",company,c.Id); } }
            var first=e.FirstOrDefault(x=>x.EventType==SupportCaseEventTypes.StateRecorded);
            var full=first is not null && !first.IsStateBaseline && first.OccurredUtc==c.CreatedUtc && first.FromStatus is null && ValidState(e.Where(x=>x.OccurredUtc<p.ActivityEndUtc).ToArray()).known;
            var correction=corrections.FirstOrDefault(x=>x.SupportCaseId==c.Id&&!corrections.Any(next=>next.PreviousId==x.Id));
            var merged=e.Any(x=>x.EventType==SupportCaseEventTypes.Merged && x.MergeTargetCaseId.HasValue && x.OccurredUtc<p.ActivityEndUtc);
            return new SupportQualityCase(c.Id,c.CaseNumber,c.Subject,c.Category,correction?.Group??SupportLabels.Category(c.Category),correction is not null,correction?.Id,c.CreatedUtc,
                c.CreatedUtc>=p.StartUtc&&c.CreatedUtc<p.ActivityEndUtc,!merged&&resolved.HasValue,resolved,reopened,response,business,
                resolved.HasValue?decimal.Round((decimal)(resolved.Value-c.CreatedUtc).TotalMinutes,2):null,first?.OccurredUtc,full,merged,
                e.Select(x=>new SupportQualitySource(x.Id,x.EventType,x.OccurredUtc,x.Summary)).ToArray(),$"/support/cases/{c.Id:D}?companyId={company:D}");
        }).ToArray();
    }
    private static (bool known,string? state) ValidState(SupportCaseEvent[] events)
    {
        string? state=null; bool broken=false;int sequence=0;
        foreach(var e in events.Where(x=>x.EventType==SupportCaseEventTypes.StateRecorded || x.EventType==SupportCaseEventTypes.Merged))
        {
            if(e.EventType==SupportCaseEventTypes.Merged) { state=SupportCaseStatuses.Closed; continue; }
            if(e.ToStatus is null) { broken=true; continue; }
            if(sequence>0 && e.StateSequence>sequence+1)broken=true;
            if(e.StateSequence>0)sequence=e.StateSequence;
            if(state is not null && e.FromStatus is not null && state!=e.FromStatus) broken=true;
            state=e.ToStatus;
        }
        return (!broken&&state is not null,state);
    }
    private static SupportQualityBacklog BacklogAt(List<SupportCase> cases,List<SupportCaseEvent> events,DateTime cut,bool complete)
    {
        int open=0,waiting=0,unknown=0;
        foreach(var c in cases.Where(x=>x.CreatedUtc<cut))
        {
            var state=ValidState(events.Where(x=>x.SupportCaseId==c.Id && x.OccurredUtc>=c.CreatedUtc && x.OccurredUtc<cut).ToArray());
            if(!state.known){unknown++;continue;}
            if(state.state is not (SupportCaseStatuses.Resolved or SupportCaseStatuses.Closed)) open++;
            if(state.state is SupportCaseStatuses.WaitingForCustomer or SupportCaseStatuses.WaitingInternal) waiting++;
        }
        return new(cut,complete&&unknown==0?open:null,complete&&unknown==0?waiting:null,open,unknown);
    }
    private static SupportQualityMetrics Metrics(IReadOnlyList<SupportQualityCase> rows)
    {
        var active=rows.Where(x=>!x.Merged).ToArray(); var arrivals=active.Where(x=>x.Arrival).ToArray(); var resolved=active.Where(x=>x.ResolutionCohort).ToArray(); var reopened=resolved.Count(x=>x.ReopenedUtc.HasValue);
        static decimal? Mean(IEnumerable<decimal?> v){var a=v.Where(x=>x.HasValue).Select(x=>x!.Value).ToArray();return a.Length==0?null:decimal.Round(a.Average(),2,MidpointRounding.AwayFromZero);}
        return new(arrivals.Length,resolved.Length,reopened,resolved.Length==0?null:decimal.Round(100m*reopened/resolved.Length,2),arrivals.Count(x=>x.ResponseMinutes.HasValue),Mean(arrivals.Select(x=>x.ResponseMinutes)),Mean(arrivals.Select(x=>x.ResponseBusinessMinutes)),resolved.Length,Mean(resolved.Select(x=>x.ResolutionMinutes)));
    }
    private static string Json<T>(T value)=>JsonSerializer.Serialize(value,JsonOptions);
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Cell(object? value)
    {
        var s=Convert.ToString(value,CultureInfo.InvariantCulture)??"Unavailable";
        if(s.Length>0&&(s[0] is '=' or '+' or '@' or '\t' or '\r' || s[0]=='-'&&!decimal.TryParse(s,NumberStyles.Number,CultureInfo.InvariantCulture,out _))) s="'"+s;
        return "\""+s.Replace("\"","\"\"")+"\"";
    }
    public async Task<SupportQualityExport> ExportAsync(Guid company,SupportQualityQuery query,CancellationToken ct)
    {
        var r=await ReportAsync(company,query,ct); if(!r.CompleteSourceCoverage) throw new InvalidOperationException("A bounded partial report cannot be exported as a complete cohort.");
        var csv=new StringBuilder("Case,Subject,Group,Created UTC,Arrival,Resolved cohort,Resolution UTC,Reopened UTC,Response elapsed minutes,Response business minutes,Resolution elapsed minutes,Merged alias,Reopen numerator,Resolution denominator,Reopen percent,As of UTC,Follow-up UTC,Definition\r\n");
        foreach(var c in r.Cases.Where(x=>x.Arrival||x.ResolutionCohort)) csv.AppendLine(string.Join(",",new object?[]{c.Number,c.Subject,c.Group,c.CreatedUtc.ToString("O"),c.Arrival,c.ResolutionCohort,c.ResolutionUtc?.ToString("O"),c.ReopenedUtc?.ToString("O"),c.ResponseMinutes,c.ResponseBusinessMinutes,c.ResolutionMinutes,c.Merged,r.Metrics.ReopenedCases,r.Metrics.ResolvedCases,r.Metrics.ReopenRate,r.AsOfUtc.ToString("O"),r.FollowupEndUtc.ToString("O"),r.Definition}.Select(Cell)));
        return new("support-quality.csv","text/csv",csv.ToString(),r.Fingerprint);
    }
}
