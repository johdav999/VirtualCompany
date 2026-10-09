using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class MonthlyReviewSnapshotService(VirtualCompanyDbContext db, IMonthlyWorkspaceQueryService monthly,
    ITodayWorkspaceLensResolver lenses, CompanyWorkVisibility visibility, IAuditEventWriter audit,
    ICorrelationContextAccessor correlation, TimeProvider clock) : IMonthlyReviewSnapshotService, IMonthlyReviewAccessService
{
    public async Task<bool> CanReadAsync(Guid company,Guid snapshot,CancellationToken ct)
    {
        try { var row=await FindAsync(company,snapshot,ct);var access=await RequireAsync(company,row.Lens,ct);
            return row.CreatedByUserId==access.UserId && row.AccessStamp==Stamp(access) && await CanReadSourcesAsync(row,ct); }
        catch (UnauthorizedAccessException) { return false; } catch (KeyNotFoundException) { return false; }
    }
    private sealed record RetainedReview(MonthlyWorkspaceDto Workspace, string ReviewNotes, IReadOnlyList<string> Changes);
    public async Task<MonthlyReviewHistoryDto> ListAsync(Guid companyId,string lens,int year,int month,int skip,int take,CancellationToken ct)
    {
        ValidatePeriod(year,month); if(skip is <0 or >10000 || take is <1 or >50) throw new ArgumentException("Invalid history page.");
        var access=await RequireAsync(companyId,lens,ct); var stamp=Stamp(access);
        var rows=await Rows(companyId,access.UserId).Where(x=>x.Lens==access.ActiveLens && x.Year==year && x.Month==month && x.AccessStamp==stamp)
            .OrderByDescending(x=>x.SavedAtUtc).ThenByDescending(x=>x.Id).Skip(skip).Take(take+1).ToListAsync(ct);
        // Source-level changes can remove access without changing the responsibility assignment.
        var permitted=new List<MonthlyReviewSnapshotSummaryDto>();
        foreach(var row in rows.Take(take))
            if(await CanReadSourcesAsync(row,ct)) permitted.Add(Summary(row));
        return new(permitted,skip,take,rows.Count>take);
    }
    public async Task<MonthlyReviewSnapshotDto> SaveAsync(Guid companyId,SaveMonthlyReviewCommand command,CancellationToken ct)
    {
        Validate(command.RequestId,command.ReviewNotes); ValidatePeriod(command.Year,command.Month);
        var access=await RequireAsync(companyId,command.Lens,ct);
        var repeated=await Rows(companyId,access.UserId).SingleOrDefaultAsync(x=>x.RequestId==command.RequestId,ct);
        if(repeated!=null)
        {
            var payload=await ReproduceAsync(repeated,access,ct);
            if(repeated.Lens!=access.ActiveLens || repeated.Year!=command.Year || repeated.Month!=command.Month || repeated.PreviousId!=null ||
                payload.ReviewNotes!=(command.ReviewNotes??"").Trim()) throw Conflict();
            return payload;
        }
        var current=await monthly.GetAsync(new(companyId,command.Lens,command.Year,command.Month,BypassCache:true),ct);
        return await PersistAsync(access,current,command.RequestId,command.ReviewNotes,null,ct);
    }
    public async Task<MonthlyReviewSnapshotDto> OpenAsync(Guid companyId,Guid id,CancellationToken ct)
    {
        var row=await FindAsync(companyId,id,ct);
        return await ReproduceAsync(row,await RequireAsync(companyId,row.Lens,ct),ct);
    }
    public async Task<MonthlyReviewSnapshotDto> RefreshAsync(Guid companyId,Guid id,RefreshMonthlyReviewCommand command,CancellationToken ct)
    {
        Validate(command.RequestId,command.ReviewNotes);
        var previous=await FindAsync(companyId,id,ct); var access=await RequireAsync(companyId,previous.Lens,ct);
        var old=await ReproduceAsync(previous,access,ct);
        if(command.ExpectedRevision!=previous.Revision) throw Conflict();
        var repeated=await Rows(companyId,access.UserId).SingleOrDefaultAsync(x=>x.RequestId==command.RequestId,ct);
        if(repeated!=null)
        {
            var retained=await ReproduceAsync(repeated,access,ct);
            if(repeated.PreviousId!=id || retained.ReviewNotes!=(command.ReviewNotes??old.ReviewNotes).Trim()) throw Conflict();
            return retained;
        }
        if(await Rows(companyId,access.UserId).AnyAsync(x=>x.PreviousId==id,ct)) throw Conflict();
        var current=await monthly.GetAsync(new(companyId,previous.Lens,previous.Year,previous.Month,BypassCache:true),ct);
        return await PersistAsync(access,current,command.RequestId,command.ReviewNotes??old.ReviewNotes,old,ct);
    }
    public async Task<MonthlyReviewExportDto> ExportAsync(Guid companyId,Guid id,CancellationToken ct)
    {
        var saved=await OpenAsync(companyId,id,ct); var s=saved.Summary;
        var csv=new StringBuilder("snapshot_id,company_id,responsibility,year,month,revision,as_of_utc,calculation_version,checksum,measure,kind,actual,prior,target,change,target_variance,unit,definition,target_availability,source,coverage\r\n");
        foreach(var m in saved.Workspace.Review!.Measures)
            csv.AppendLine(string.Join(",",new object?[]{s.Id,s.CompanyId,s.Lens,s.Year,s.Month,s.Revision,s.AsOfUtc.ToString("O"),s.CalculationVersion,
                saved.Checksum,m.Key,m.Kind,m.Actual,m.Prior,m.Target,m.Change,m.TargetVariance,m.Unit,m.Definition,m.TargetAvailability,m.SourceLink,s.Coverage}.Select(Cell)));
        return new($"monthly-{s.Lens}-{s.Year:D4}-{s.Month:D2}-revision-{s.Revision}-{s.Id:N}.csv",csv.ToString());
    }
    private async Task<MonthlyReviewSnapshotDto> PersistAsync(TodayWorkspaceLensResolution access,MonthlyWorkspaceDto workspace,
        Guid requestId,string? notes,MonthlyReviewSnapshotDto? previous,CancellationToken ct)
    {
        // Recheck after source composition. Permissions are current, never retained as authority.
        var confirmed=await RequireAsync(access.CompanyId,workspace.ActiveLens,ct);
        if(Stamp(confirmed)!=Stamp(access)) throw new UnauthorizedAccessException("Review access changed during capture.");
        var changes=previous==null ? new List<string>() : Differences(previous.Workspace,workspace);
        var payload=JsonSerializer.Serialize(new RetainedReview(workspace with {CacheTimestampUtc=null},(notes??"").Trim(),changes));
        var row=new MonthlyReviewSnapshot(Guid.NewGuid(),access.CompanyId,access.UserId,previous?.Summary.SeriesId??Guid.NewGuid(),
            (previous?.Summary.Revision??0)+1,previous?.Summary.Id,requestId,workspace.ActiveLens,workspace.Period.Year,workspace.Period.Month,
            Stamp(access),payload,Hash(payload),workspace.GeneratedAtUtc,clock.GetUtcNow().UtcDateTime,workspace.Review!.CalculationVersion);
        db.MonthlyReviewSnapshots.Add(row);
        await WriteAuditAsync(row,previous==null ? "monthly.review.saved" : "monthly.review.refreshed","succeeded",
            previous==null ? "Management review retained with immutable input values and definitions." : "Refreshed sources retained as a new revision; previous review unchanged.",ct);
        try { await db.SaveChangesAsync(ct); }
        catch(DbUpdateException)
        {
            // Unique request and predecessor indexes are the durable cross-process concurrency boundary.
            db.ChangeTracker.Clear();
            var winner=await Rows(access.CompanyId,access.UserId).SingleOrDefaultAsync(x=>x.RequestId==requestId,ct);
            if(winner!=null && winner.PreviousId==row.PreviousId && winner.Lens==row.Lens && winner.Year==row.Year && winner.Month==row.Month)
            {
                var result=await ReproduceAsync(winner,access,ct);
                if(result.ReviewNotes==(notes??"").Trim()) return result;
            }
            if(winner!=null || row.PreviousId!=null && await Rows(access.CompanyId,access.UserId).AnyAsync(x=>x.PreviousId==row.PreviousId,ct)) throw Conflict();
            throw;
        }
        return Map(row,new(workspace,(notes??"").Trim(),changes));
    }
    private async Task<MonthlyReviewSnapshotDto> ReproduceAsync(MonthlyReviewSnapshot row,TodayWorkspaceLensResolution access,CancellationToken ct)
    {
        if(row.CreatedByUserId!=access.UserId || row.AccessStamp!=Stamp(access)) throw new UnauthorizedAccessException("The saved review requires its original current responsibility scope.");
        RetainedReview retained;
        try
        {
            if(Encoding.UTF8.GetByteCount(row.PayloadJson)>MonthlyReviewSnapshot.MaximumPayloadBytes || Hash(row.PayloadJson)!=row.Checksum)
                throw new JsonException();
            retained=JsonSerializer.Deserialize<RetainedReview>(row.PayloadJson)??throw new JsonException();
            var w=retained.Workspace;
            if(w.CompanyId!=row.CompanyId || w.ActiveLens!=row.Lens || w.Period.Year!=row.Year || w.Period.Month!=row.Month ||
                w.GeneratedAtUtc!=row.AsOfUtc || w.Review?.CalculationVersion!=row.CalculationVersion || row.CalculationVersion!=MonthlyManagementReviewProjector.CalculationVersion)
                throw new JsonException();
            foreach(var m in w.Review.Measures)
                if(m.Change!=(m.Actual.HasValue && m.Prior.HasValue ? m.Actual-m.Prior : null) ||
                    m.TargetVariance!=(m.Actual.HasValue && m.Target.HasValue ? m.Actual-m.Target : null)) throw new JsonException();
            if(w.SalesManagement is { } sales && (sales.CompanyId!=row.CompanyId || sales.Year!=row.Year || sales.Month!=row.Month ||
                sales.CalculationVersion!="sales-management.v1")) throw new JsonException();
            if(w.MarketingManagement is { } marketing && (marketing.CompanyId!=row.CompanyId || marketing.Query is null ||
                marketing.Query.Year!=row.Year || marketing.Query.Month!=row.Month ||
                marketing.CalculationVersion!="marketing-management.v1")) throw new JsonException();
            if(w.FinancePlanning is { } planning && (planning.CompanyId!=row.CompanyId || planning.Query is null ||
                planning.Query.Year!=row.Year || planning.Query.Month!=row.Month || planning.Query.Months!=1 ||
                planning.CalculationVersion!="finance-rolling-planning.v1" || planning.Rows is null || planning.Sources is null)) throw new JsonException();
            if(w.SupportQuality is { } support && (support.CompanyId!=row.CompanyId || support.Query is null || support.Query.Year!=row.Year || support.Query.Month!=row.Month ||
                support.CalculationVersion!="support-quality.v1" || support.Cases is null || support.Groups is null || support.Backlog is null)) throw new JsonException();
        }
        catch(Exception ex) when(ex is JsonException or NotSupportedException)
        {
            await WriteAuditAsync(row,"monthly.review.reproduction_failed","failed","Saved review integrity or calculation version could not be reproduced. No retained results returned.",ct);
            await db.SaveChangesAsync(ct);
            throw new MonthlyReviewReproductionException("This saved review could not be reproduced. Open another revision or contact your company administrator.");
        }
        if(!await CanReadSourcesAsync(row,ct)) throw new UnauthorizedAccessException("A retained work source is now restricted.");
        return Map(row,retained);
    }
    private async Task<bool> CanReadSourcesAsync(MonthlyReviewSnapshot row,CancellationToken ct)
    {
        RetainedReview? retained;
        if(Encoding.UTF8.GetByteCount(row.PayloadJson)>MonthlyReviewSnapshot.MaximumPayloadBytes) return true;
        try { retained=JsonSerializer.Deserialize<RetainedReview>(row.PayloadJson); }
        catch(JsonException) { return true; } // Open performs the audited integrity check; history reveals metadata only.
        if(retained==null) return true;
        if (retained.Workspace.FinancePlanning is not null)
        {
            var financeAccess = await lenses.ResolveAsync(row.CompanyId, TodayWorkspaceLenses.Finance, ct);
            if (!financeAccess.AvailableLenses.Any(x => x.Lens == TodayWorkspaceLenses.Finance)) return false;
        }
        if (retained.Workspace.SupportQuality is not null)
        {
            var supportAccess=await lenses.ResolveAsync(row.CompanyId,TodayWorkspaceLenses.Customers,ct);
            if(!supportAccess.AvailableLenses.Any(x=>x.Lens==TodayWorkspaceLenses.Customers))return false;
        }
        var ids=(retained.Workspace.Review?.WorkSourceIds??[]).Concat(retained.Workspace.AgentOutcomes.Where(x=>x.RelatedTaskId.HasValue).Select(x=>x.RelatedTaskId!.Value))
            .Concat(retained.Workspace.Priorities.Where(x=>x.EvidenceSourceType=="work_task" && Guid.TryParse(x.EvidenceSourceId,out _))
                .Select(x=>Guid.Parse(x.EvidenceSourceId!))).Distinct().ToArray();
        var scope=await visibility.ResolveAsync(row.CompanyId,ct);
        foreach(var id in ids)
        {
            // Deleted sources do not erase captured inputs; existing sources must still pass native access.
            if(!await db.WorkTasks.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==row.CompanyId && x.Id==id,ct)) continue;
            if(!await scope.Tasks(db.WorkTasks.IgnoreQueryFilters().Where(x=>x.CompanyId==row.CompanyId)).AnyAsync(x=>x.Id==id,ct) ||
                !await CollaborationContentVisibility.AllowsAsync(db,scope,id,ct)) return false;
        }
        return true;
    }
    private async Task<TodayWorkspaceLensResolution> RequireAsync(Guid companyId,string lens,CancellationToken ct)
    {
        if(!TodayWorkspaceLenses.All.Contains(lens)) throw new ArgumentException("Unsupported review responsibility.");
        var access=await lenses.ResolveAsync(companyId,lens,ct);
        if(access.ActiveLens!=lens || !access.AvailableLenses.Any(x=>x.Lens==lens)) throw new UnauthorizedAccessException("Current responsibility access is required.");
        return access;
    }
    private IQueryable<MonthlyReviewSnapshot> Rows(Guid companyId,Guid userId)=>db.MonthlyReviewSnapshots.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==companyId && x.CreatedByUserId==userId);
    private async Task<MonthlyReviewSnapshot> FindAsync(Guid companyId,Guid id,CancellationToken ct)
    {
        var access=await lenses.ResolveAsync(companyId,null,ct);
        return await Rows(companyId,access.UserId).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new KeyNotFoundException("Saved review not found.");
    }
    private static string Stamp(TodayWorkspaceLensResolution r)=>Hash($"{r.CompanyId:D}|{r.UserId:D}|{r.MembershipId:D}|{r.MembershipRole}|{r.ResponsibilityRevision}|{string.Join(',',r.AvailableLenses.Select(x=>x.Lens).Order())}");
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static MonthlyReviewSnapshotSummaryDto Summary(MonthlyReviewSnapshot x)=>new(x.Id,x.CompanyId,x.SeriesId,x.Revision,x.PreviousId,x.Lens,x.Year,x.Month,x.AsOfUtc,x.SavedAtUtc,Coverage(x),x.CalculationVersion);
    private static string Coverage(MonthlyReviewSnapshot x)
    {
        if(Encoding.UTF8.GetByteCount(x.PayloadJson)>MonthlyReviewSnapshot.MaximumPayloadBytes || Hash(x.PayloadJson)!=x.Checksum) return "Integrity check required";
        try { return JsonSerializer.Deserialize<RetainedReview>(x.PayloadJson)?.Workspace.ManagementSummary.CoverageSummary??"Coverage unavailable"; }
        catch(JsonException) { return "Coverage unavailable"; }
    }
    private static MonthlyReviewSnapshotDto Map(MonthlyReviewSnapshot x,RetainedReview p)=>new(Summary(x) with {Coverage=p.Workspace.ManagementSummary.CoverageSummary},p.Workspace,p.ReviewNotes,p.Changes,x.Checksum,"Retained until company deletion; no automatic purge. Current access is required on every open and export.");
    private Task WriteAuditAsync(MonthlyReviewSnapshot row,string action,string outcome,string rationale,CancellationToken ct)=>audit.WriteAsync(new(row.CompanyId,"user",row.CreatedByUserId,action,"monthly_review",row.Id.ToString("D"),outcome,rationale,
        DataSources:["monthly_workspace",row.CalculationVersion],Metadata:new Dictionary<string,string?>{["revision"]=row.Revision.ToString(),["checksum"]=row.Checksum,["previousId"]=row.PreviousId?.ToString("D")},CorrelationId:correlation.CorrelationId,OccurredUtc:clock.GetUtcNow().UtcDateTime),ct);
    private static List<string> Differences(MonthlyWorkspaceDto old,MonthlyWorkspaceDto current)
    {
        var changes=new List<string>();
        foreach(var key in old.Review!.Measures.Select(x=>x.Key).Union(current.Review!.Measures.Select(x=>x.Key)))
        {
            var a=old.Review.Measures.FirstOrDefault(x=>x.Key==key); var b=current.Review.Measures.FirstOrDefault(x=>x.Key==key);
            if(a!=b) changes.Add($"{b?.Label??a!.Label}: actual {a?.Actual?.ToString(CultureInfo.InvariantCulture)??"unavailable"} → {b?.Actual?.ToString(CultureInfo.InvariantCulture)??"unavailable"}; prior {a?.Prior} → {b?.Prior}; target {a?.Target} → {b?.Target}. Definitions, target versions and source coverage are retained in each revision.");
        }
        if(JsonSerializer.Serialize(old.SourceCoverage)!=JsonSerializer.Serialize(current.SourceCoverage)) changes.Add("Source coverage or observation times changed; compare each revision's retained coverage.");
        if(JsonSerializer.Serialize(old.Sections)!=JsonSerializer.Serialize(current.Sections)) changes.Add("Feature facts, current risks or source-linked items changed.");
        if(JsonSerializer.Serialize(old.Decisions)!=JsonSerializer.Serialize(current.Decisions) || JsonSerializer.Serialize(old.Priorities)!=JsonSerializer.Serialize(current.Priorities)) changes.Add("Review decisions or next-period priorities changed.");
        if(JsonSerializer.Serialize(old.AgentOutcomes)!=JsonSerializer.Serialize(current.AgentOutcomes)) changes.Add("Recorded agent outcomes changed.");
        if(changes.Count==0) changes.Add("No retained result, target, coverage, decision or outcome change; a new as-of observation is retained.");
        return changes;
    }
    private static void ValidatePeriod(int year,int month) { if(year is <2000 or >2100 || month is <1 or >12) throw new ArgumentException("Invalid review month."); }
    private static void Validate(Guid id,string? notes) { if(id==Guid.Empty || notes?.Length>2000) throw new ArgumentException("A request identity and review notes of at most 2,000 characters are required."); }
    private static MonthlyReviewConflictException Conflict()=>new("This review already has a newer revision, or the request identity was reused. Open the latest saved review and retry.");
    private static string Cell(object? value)
    {
        var text=Convert.ToString(value,CultureInfo.InvariantCulture)??"";
        if(text.Length>0 && "=+@-\t\r".Contains(text[0]) && value is not decimal) text="'"+text;
        return "\""+text.Replace("\"","\"\"")+"\"";
    }
}
