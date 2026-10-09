using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesManagementService(VirtualCompanyDbContext db, ITodayWorkspaceLensResolver access,
    ISalesOperationalReportService operational, TimeProvider clock) : ISalesManagementService
{
    public const string Version="sales-management.v1";
    private sealed record Stored(SalesManagementReport Report,SalesCapacityAssumptions Assumptions,SalesCapacityResult Result);
    private async Task<TodayWorkspaceLensResolution> Authorize(Guid company,CancellationToken ct)
    {
        var scope=await access.ResolveAsync(company,TodayWorkspaceLenses.Sales,ct);
        if(!scope.AvailableLenses.Any(x=>x.Lens==TodayWorkspaceLenses.Sales))throw new UnauthorizedAccessException();
        return scope;
    }
    public async Task<SalesManagementReport> ReportAsync(Guid company,SalesManagementQuery query,CancellationToken ct)
    {
        await Authorize(company,ct);
        var profile=await db.Companies.IgnoreQueryFilters().AsNoTracking().SingleAsync(x=>x.Id==company,ct);
        var now=clock.GetUtcNow().UtcDateTime;
        var timezone=ResolveTimezone(profile.Timezone);
        var p=MonthlyWorkspacePeriod.Resolve(now,timezone,query.Year,query.Month);
        var currency=NormalizeCurrency(query.Currency);
        var cutoff=now<p.EndUtc?now:p.EndUtc;
        var priorCutoff=now<p.EndUtc?new[]{p.ComparisonEndUtc,p.ComparisonStartUtc.Add(cutoff>p.StartUtc?cutoff-p.StartUtc:TimeSpan.Zero)}.Min():p.ComparisonEndUtc;
        var deals=await db.Deals.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==company && !x.IsDeleted &&
            x.CreatedUtc>=p.ComparisonStartUtc && x.CreatedUtc<p.EndUtc && x.CreatedUtc<=now && (currency==null || x.Currency==currency))
            .OrderBy(x=>x.CreatedUtc).ThenBy(x=>x.Id).Take(2001).ToListAsync(ct);
        if(deals.Count>2000)throw new ArgumentException("This cohort exceeds 2,000 opportunities. Select one currency or a narrower month.");
        var ids=deals.Select(x=>x.Id).ToArray();
        var activities=await db.SalesActivities.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==company && !x.IsDeleted &&
            x.DealId.HasValue && ids.Contains(x.DealId.Value) && x.OccurredUtc<=now && x.OccurredUtc<p.EndUtc &&
            (x.ActivityType=="won" || x.ActivityType=="lost" || x.ActivityType=="stage change" || x.ActivityType=="conversion" || x.ActivityType=="reopened"))
            .OrderBy(x=>x.OccurredUtc).ThenBy(x=>x.Id).Take(20001).ToListAsync(ct);
        if(activities.Count>20000)throw new ArgumentException("Stage history exceeds the bounded report. Select another cohort.");
        var rows=deals.Select(d=>{
            var selected=d.CreatedUtc>=p.StartUtc;var end=selected?cutoff:priorCutoff;
            var history=activities.Where(a=>a.DealId==d.Id && a.OccurredUtc>=d.CreatedUtc && a.OccurredUtc<end)
                .Select(a=>new SalesOutcomeEvidence(a.Id,a.ActivityType,DateTime.SpecifyKind(a.OccurredUtc,DateTimeKind.Utc),a.RecordedReason,a.ActorUserId,a.PreviousStageId,a.NewStageId)).ToArray();
            var outcome=history.LastOrDefault(x=>x.Outcome is "won" or "lost" or "reopened");
            // A stage move after an outcome is evidence of reopening, never a second conversion.
            if(outcome!=null && history.Any(x=>x.Outcome=="stage change" && x.OccurredUtc>outcome.OccurredUtc))outcome=null;
            var decided=outcome?.Outcome is "won" or "lost";
            return new SalesManagementOpportunity(d.Id,d.Title,d.Currency,DateTime.SpecifyKind(d.CreatedUtc,DateTimeKind.Utc),selected?"selected":"prior",
                decided?outcome!.Outcome:"undecided",decided?outcome!.OccurredUtc:null,decided?outcome!.RecordedReason:null,
                decided?(decimal)(outcome!.OccurredUtc-d.CreatedUtc).TotalDays:null,
                history.Any(x=>x.NewStageId.HasValue),history,$"/app/sales/deals/{d.Id:D}?companyId={company:D}");
        }).ToArray();
        var selectedResult=Cohort("selected",rows);var priorResult=Cohort("prior",rows);
        var current=await operational.GetOpportunitiesAsync(company,true,90,currency,null,ct);
        var openIds=await db.Deals.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==company && !x.IsDeleted &&
            x.Status==SalesStatuses.Open && (currency==null || x.Currency==currency)).OrderBy(x=>x.Id).Select(x=>x.Id).Take(2001).ToArrayAsync(ct);
        if(openIds.Length>2000 || current.Rows.Count>2000)throw new ArgumentException("Current workload exceeds 2,000 opportunities. Select a currency.");
        var historySnapshots=await db.RevenueForecastSnapshots.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==company &&
            x.AsOfUtc<p.EndUtc && x.AsOfUtc<=now && (currency==null || x.Currency==currency)).OrderByDescending(x=>x.AsOfUtc).Take(2).ToListAsync(ct);
        var forecasts=historySnapshots.Select(SnapshotEvidence).ToArray();
        decimal? movement=forecasts.Length==2 && forecasts[0].Currency==forecasts[1].Currency &&
            forecasts[0].AsOfUtc>=p.StartUtc && forecasts[1].AsOfUtc<p.StartUtc ? forecasts[0].Expected30-forecasts[1].Expected30 : null;
        // Comparison is selected month's latest against latest preceding month, not two adjacent daily snapshots.
        if(historySnapshots.Count>0 && historySnapshots[0].AsOfUtc>=p.StartUtc){
            var previous=await db.RevenueForecastSnapshots.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==company && x.AsOfUtc<p.StartUtc &&
                x.Currency==historySnapshots[0].Currency).OrderByDescending(x=>x.AsOfUtc).FirstOrDefaultAsync(ct);
            forecasts=previous==null?[SnapshotEvidence(historySnapshots[0])]:[SnapshotEvidence(historySnapshots[0]),SnapshotEvidence(previous)];
            movement=previous==null?null:historySnapshots[0].ExpectedRevenue30Days-previous.ExpectedRevenue30Days;
        }
        return new(company,p.Year,p.Month,timezone.Id,p.StartUtc,p.EndUtc,now,currency,Version,selectedResult,priorResult,
            selectedResult.WonCreatedPercent.HasValue && priorResult.WonCreatedPercent.HasValue?selectedResult.WonCreatedPercent-priorResult.WonCreatedPercent:null,
            rows,current.Rows,current.Windows??[],openIds,forecasts,movement,
            "Created-date cohort [company-local start,end), observed through month end or current as-of. Prior cohort uses the same elapsed duration. Conversion = last recorded won / created; win rate = won / (won+lost); each opportunity counted once. Undecided includes absent outcomes and reopened records. Median cycle = days from created to last recorded won/lost. Stage evidence uses recorded IDs, not mutable current stage. Forecast = amount × authoritative stage probability × (1 − risk × 0.5), risk defaults to 0.5 when unavailable; horizons 30/60/90 inclusive.",
            "Actual opportunity owner and territory are not recorded by the current Sales model. Activity actors are event authors, not account owners. Territory allocations below are planning assumptions only.",
            "Nondeleted native opportunities and activities; deleted/missing history is outside this cohort. Zero denominators are unavailable. Missing recorded reasons are not inferred from activity summaries. Current forecast/workload is as-of now, separate from the selected period. Legacy forecast aggregates may lack retained inputs.");
    }
    public static SalesCohortResult Cohort(string key,IReadOnlyList<SalesManagementOpportunity> all)
    {
        var rows=all.Where(x=>x.Cohort==key).ToArray();var won=rows.Count(x=>x.Outcome=="won");var lost=rows.Count(x=>x.Outcome=="lost");
        var cycles=rows.Where(x=>x.CycleDays.HasValue).Select(x=>x.CycleDays!.Value).Order().ToArray();
        decimal? median=cycles.Length==0?null:cycles.Length%2==1?cycles[cycles.Length/2]:(cycles[cycles.Length/2-1]+cycles[cycles.Length/2])/2;
        return new(key,rows.Length,won,lost,rows.Length-won-lost,rows.Length==0?null:100m*won/rows.Length,
            won+lost==0?null:100m*won/(won+lost),median,rows.Count(x=>x.Outcome is "won" or "lost" && x.RecordedReason==null),rows.Count(x=>!x.StageHistoryAvailable));
    }
    private static SalesForecastEvidence SnapshotEvidence(RevenueForecastSnapshot value)
    {
        SalesForecastCapture? capture=null;string coverage="Aggregate only; included opportunity inputs unavailable (older capture or bounded storage limit).";
        if(value.InputsJson!=null){try{
            capture=JsonSerializer.Deserialize<SalesForecastCapture>(value.InputsJson);
            if(capture==null || capture.CalculationVersion!=RevenueForecastCalculation.Version || capture.Inputs==null ||
                capture.Inputs.Any(x=>x==null || x.Currency!=value.Currency || x.Risk is <0 or >1 || x.ExpectedAmount!=RevenueForecastCalculation.ExpectedAmount(x.Amount,x.StageId,x.Risk)) ||
                new[]{30,60,90}.Select((days,i)=>Math.Round(capture.Inputs.Where(x=>x.ExpectedCloseUtc>=capture.CalculationAsOfUtc && x.ExpectedCloseUtc<=capture.CalculationAsOfUtc.AddDays(days)).Sum(x=>x.ExpectedAmount),2)!=new[]{value.ExpectedRevenue30Days,value.ExpectedRevenue60Days,value.ExpectedRevenue90Days}[i]).Any(x=>x))capture=null;
            coverage=capture==null?"Retained inputs failed reconciliation; totals are aggregates only.":"Retained opportunity inputs reconcile to all three forecast windows.";
        }catch(Exception ex) when(ex is JsonException or ArgumentException or OverflowException){capture=null;coverage="Retained inputs unreadable; totals are aggregates only.";}}
        return new(value.Id,DateTime.SpecifyKind(value.AsOfUtc,DateTimeKind.Utc),value.Currency,value.ExpectedRevenue30Days,value.ExpectedRevenue60Days,value.ExpectedRevenue90Days,capture!=null,coverage,capture);
    }
    private static TimeZoneInfo ResolveTimezone(string? value)
    {try{return TimeZoneInfo.FindSystemTimeZoneById(value??"UTC");}catch(TimeZoneNotFoundException){return TimeZoneInfo.Utc;}catch(InvalidTimeZoneException){return TimeZoneInfo.Utc;}}
    private static string? NormalizeCurrency(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;value=value.Trim().ToUpperInvariant();
        if(value.Length!=3 || value.Any(x=>x is <'A' or >'Z'))throw new ArgumentException("Currency requires three letters.");return value;
    }
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static SalesCapacityProposalSummary Summary(SalesCapacityProposalRevision r)=>new(r.Id,r.SeriesId,r.Revision,r.PreviousId,r.AccountableUserId,DateTime.SpecifyKind(r.SavedAtUtc,DateTimeKind.Utc),r.Year,r.Month,r.Currency);
    private static SalesCapacityProposal Reproduce(SalesCapacityProposalRevision r)
    {
        if(Hash(r.Payload)!=r.Checksum)throw new InvalidDataException("Proposal integrity check failed.");
        Stored? s;try{s=JsonSerializer.Deserialize<Stored>(r.Payload);}catch(JsonException){throw new InvalidDataException("Proposal payload is unreadable.");}
        if(s==null || s.Report==null || s.Assumptions==null || s.Result==null || s.Report.CurrentOpenOpportunityIds==null ||
            s.Report.CompanyId!=r.CompanyId || s.Report.CalculationVersion!=Version || s.Report.Year!=r.Year || s.Report.Month!=r.Month ||
            s.Report.Currency!=r.Currency)throw new InvalidDataException("Proposal cannot be reproduced.");
        SalesCapacityResult result;
        try { result=SalesCapacityCalculation.Calculate(s.Assumptions,s.Report.CurrentOpenOpportunityIds.Count); }
        catch(ArgumentException ex) { throw new InvalidDataException("Retained proposal assumptions are invalid.",ex); }
        if(JsonSerializer.Serialize(s.Result)!=JsonSerializer.Serialize(result))throw new InvalidDataException("Proposal cannot be reproduced.");
        return new(Summary(r),s.Report,s.Assumptions,s.Result,r.Checksum,"Immutable revisions retained until company deletion. Private to the accountable reviewer; current Sales access required. Source links open current records.");
    }
    public async Task<SalesCapacityProposal> OpenAsync(Guid company,Guid id,CancellationToken ct)
    {
        var scope=await Authorize(company,ct);
        var row=await db.SalesCapacityProposalRevisions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company && x.Id==id && x.AccountableUserId==scope.UserId,ct);
        return row==null?throw new KeyNotFoundException():Reproduce(row);
    }
    public async Task<IReadOnlyList<SalesCapacityProposalSummary>> ListAsync(Guid company,int skip,CancellationToken ct)
    {
        if(skip is <0 or >10000)throw new ArgumentException("History page is outside the supported range.");
        var scope=await Authorize(company,ct);
        return (await db.SalesCapacityProposalRevisions.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==company && x.AccountableUserId==scope.UserId)
            .OrderByDescending(x=>x.SavedAtUtc).ThenByDescending(x=>x.Id).Skip(skip).Take(20).ToListAsync(ct)).Select(Summary).ToArray();
    }
    public async Task<SalesCapacityProposal> SaveAsync(Guid company,SaveSalesCapacityProposal command,CancellationToken ct)
    {
        var scope=await Authorize(company,ct);if(command.RequestId==Guid.Empty || command.Query==null || command.Assumptions==null)throw new ArgumentException("A request, cohort and assumptions are required.");
        SalesCapacityCalculation.Calculate(command.Assumptions,0);var currency=NormalizeCurrency(command.Query.Currency);
        var retry=await db.SalesCapacityProposalRevisions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company && x.AccountableUserId==scope.UserId && x.RequestId==command.RequestId,ct);
        if(retry!=null){var old=Reproduce(retry);if(old.Summary.PreviousId!=command.PreviousId || old.Report.Year!=command.Query.Year || old.Report.Month!=command.Query.Month ||
            old.Report.Currency!=currency || JsonSerializer.Serialize(old.Assumptions)!=JsonSerializer.Serialize(command.Assumptions))throw new InvalidOperationException("Request identity already contains different assumptions.");return old;}
        SalesCapacityProposal? previous=null;
        if(command.PreviousId.HasValue){previous=await OpenAsync(company,command.PreviousId.Value,ct);
            if(command.ExpectedRevision!=previous.Summary.Revision || command.Query.Year!=previous.Report.Year || command.Query.Month!=previous.Report.Month || currency!=previous.Report.Currency ||
                await db.SalesCapacityProposalRevisions.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==company && x.PreviousId==command.PreviousId,ct))throw new InvalidOperationException("Proposal changed. Open latest revision.");}
        else if(command.ExpectedRevision.HasValue)throw new ArgumentException("Expected revision requires a predecessor.");
        var report=await ReportAsync(company,command.Query,ct);var result=SalesCapacityCalculation.Calculate(command.Assumptions,report.CurrentOpenOpportunityIds.Count);
        await Authorize(company,ct);
        var payload=JsonSerializer.Serialize(new Stored(report,command.Assumptions,result));
        var row=new SalesCapacityProposalRevision(company,scope.UserId,command.RequestId,previous?.Summary.SeriesId??Guid.NewGuid(),
            (previous?.Summary.Revision??0)+1,command.PreviousId,report.Year,report.Month,report.Currency,clock.GetUtcNow().UtcDateTime,payload,Hash(payload));
        db.SalesCapacityProposalRevisions.Add(row);
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(),company,"user",scope.UserId,"sales.capacity.proposal_saved","sales_capacity_proposal",row.Id.ToString("D"),"succeeded",$"Saved capacity assumptions revision {row.Revision}; no customer assignments or commitments changed."));
        try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){db.ChangeTracker.Clear();var winner=await db.SalesCapacityProposalRevisions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company && x.AccountableUserId==scope.UserId && x.RequestId==command.RequestId,ct);
            if(winner!=null)return await SaveAsync(company,command,ct);if(command.PreviousId.HasValue && await db.SalesCapacityProposalRevisions.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==company && x.PreviousId==command.PreviousId,ct))throw new InvalidOperationException("Proposal changed while saving. Reload before retrying.");throw;}
        return Reproduce(row);
    }
}



