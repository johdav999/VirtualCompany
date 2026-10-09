using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Support;

public sealed partial class SupportQualityService
{
    public async Task<SupportIssueGroupingSummary> CorrectAsync(Guid company,CorrectSupportIssueGrouping command,CancellationToken ct)
    {
        var scope=await Require(company,ct);
        if(command.RequestId==Guid.Empty || command.CaseId==Guid.Empty || string.IsNullOrWhiteSpace(command.Group) || command.Group.Length>80 || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length>1000) throw new ArgumentException("Choose a case and explain a bounded reviewed group.");
        var requestHash=Hash(Json(command));
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>{
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            await Lock(company,ct);
            var duplicate=await db.SupportIssueGroupingRevisions.AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company&&x.ActorId==scope.UserId&&x.RequestId==command.RequestId,ct);
            if(duplicate is not null){if(duplicate.RequestHash!=requestHash)throw new InvalidOperationException("This grouping request was already used for different content.");return GroupSummary(duplicate);}
            var r=await ReportAsync(company,command.Query,ct);
            if(!r.CompleteSourceCoverage) throw new InvalidOperationException("Grouping correction needs complete bounded case sources.");
            if(r.Fingerprint!=command.ExpectedFingerprint)throw new InvalidOperationException("Case evidence changed. Reload the quality report before correcting the group.");
            var c=r.Cases.SingleOrDefault(x=>x.Id==command.CaseId && (x.Arrival||x.ResolutionCohort) && !x.Merged)??throw new KeyNotFoundException("Cohort case not found.");
            if(c.GroupRevisionId!=command.PreviousId)throw new InvalidOperationException("The case grouping has a newer revision. Reload before saving.");
            var row=new SupportIssueGroupingRevision(Guid.NewGuid(),company,c.Id,scope.UserId,command.RequestId,command.PreviousId,command.Group,command.Reason,r.Fingerprint,requestHash,Now);
            db.SupportIssueGroupingRevisions.Add(row);
            await Audit(company,scope.UserId,"support.issue_grouping.corrected",row.Id,"Reviewed category grouping saved; source case permissions and classification remain unchanged.",ct);
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return GroupSummary(row);
        });
    }
    public async Task<IReadOnlyList<SupportIssueGroupingSummary>> GroupHistoryAsync(Guid company,Guid caseId,CancellationToken ct)
    {
        await Require(company,ct); if(!await db.SupportCases.AsNoTracking().AnyAsync(x=>x.CompanyId==company&&x.Id==caseId,ct))throw new KeyNotFoundException();
        return (await db.SupportIssueGroupingRevisions.AsNoTracking().Where(x=>x.CompanyId==company&&x.SupportCaseId==caseId).OrderByDescending(x=>x.SavedUtc).ThenByDescending(x=>x.Id).Take(100).ToListAsync(ct)).Select(GroupSummary).ToArray();
    }
    public async Task<SupportCapacityPreview> PreviewAsync(Guid company,PreviewSupportCapacity input,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(input.Assumptions);
        var report=await ReportAsync(company,input.Query,ct);
        var result=SupportCapacityCalculation.Calculate(report,input.Assumptions,report.AsOfUtc);
        return new(report,input.Assumptions,result,PreviewHash(report,input.Assumptions,result));
    }
    public async Task<SupportCapacityProposal> SaveAsync(Guid company,SaveSupportCapacityProposal command,CancellationToken ct)
    {
        var scope=await Require(company,ct);
        if(command.RequestId==Guid.Empty || string.IsNullOrWhiteSpace(command.Name) || command.Name.Length>120)throw new ArgumentException("Give the capacity proposal a name in at most 120 characters.");
        var requestHash=Hash(Json(command));
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async()=>{
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);await Lock(company,ct);
            var duplicate=await Proposals(company,scope.UserId).SingleOrDefaultAsync(x=>x.RequestId==command.RequestId,ct);
            if(duplicate is not null){if(duplicate.RequestHash!=requestHash)throw new InvalidOperationException("The save request already identifies different assumptions.");return Reproduce(duplicate);}
            var preview=await PreviewAsync(company,command.Input,ct);
            if(preview.Fingerprint!=command.ExpectedFingerprint)throw new InvalidOperationException("Support sources or assumptions changed. Preview again before saving.");
            if(command.PreviousId is Guid previous){
                var parent=await Proposals(company,scope.UserId).SingleOrDefaultAsync(x=>x.Id==previous,ct)??throw new KeyNotFoundException();
                if(parent.Year!=preview.Report.Query.Year||parent.Month!=preview.Report.Query.Month)throw new ArgumentException("A revision must retain its source period.");
                if(await db.SupportCapacityProposalRevisions.AnyAsync(x=>x.CompanyId==company&&x.PreviousId==previous,ct))throw new InvalidOperationException("This proposal already has a successor. Open the latest revision.");
            }
            var a=preview.Assumptions;var payload=Json(preview);
            var row=new SupportCapacityProposalRevision(Guid.NewGuid(),company,scope.UserId,command.RequestId,command.PreviousId,command.Name,
                preview.Report.Query.Year,preview.Report.Query.Month,a.TargetYear,a.TargetMonth,a.ExpectedArrivals,a.BacklogToClear,a.HandlingMinutes,a.AvailablePeople,a.HoursPerBusinessDay,a.UtilizationPercent,a.ResponseTargetMinutes,
                payload,Hash(payload),preview.Report.Fingerprint,requestHash,Now);
            db.SupportCapacityProposalRevisions.Add(row);
            await Audit(company,scope.UserId,"support.capacity_proposal.saved",row.Id,"Explicit workload and staffing assumptions retained; no schedule, case permission or customer promise changed.",ct);
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Reproduce(row);
        });
    }
    public async Task<SupportCapacityProposal> OpenAsync(Guid company,Guid id,CancellationToken ct)
    {
        var scope=await Require(company,ct);
        return Reproduce(await Proposals(company,scope.UserId).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new KeyNotFoundException());
    }
    public async Task<IReadOnlyList<SupportCapacityProposalSummary>> HistoryAsync(Guid company,int skip,CancellationToken ct)
    {
        var scope=await Require(company,ct); if(skip<0||skip>10000)throw new ArgumentException("Choose a bounded history page.");
        return (await Proposals(company,scope.UserId).OrderByDescending(x=>x.SavedUtc).ThenByDescending(x=>x.Id).Skip(skip).Take(20).ToListAsync(ct)).Select(x=>Reproduce(x).Summary).ToArray();
    }
    private IQueryable<SupportCapacityProposalRevision> Proposals(Guid company,Guid owner)=>db.SupportCapacityProposalRevisions.AsNoTracking().Where(x=>x.CompanyId==company&&x.OwnerId==owner);
    private static SupportCapacityProposal Reproduce(SupportCapacityProposalRevision row)
    {
        try{
            if(row.Payload.Length>1024*1024 || Hash(row.Payload)!=row.Checksum)throw new JsonException();
            var p=JsonSerializer.Deserialize<SupportCapacityPreview>(row.Payload,JsonOptions)??throw new JsonException();
            var r=p.Report;var a=p.Assumptions;
            if(r.CompanyId!=row.CompanyId||r.Query.Year!=row.Year||r.Query.Month!=row.Month||r.CalculationVersion!=SupportCapacityCalculation.Version||r.Fingerprint!=row.SourceFingerprint||a.TargetYear!=row.TargetYear||a.TargetMonth!=row.TargetMonth||
                a.ExpectedArrivals!=row.ExpectedArrivals||a.BacklogToClear!=row.BacklogToClear||a.HandlingMinutes!=row.HandlingMinutes||a.AvailablePeople!=row.AvailablePeople||a.HoursPerBusinessDay!=row.HoursPerBusinessDay||a.UtilizationPercent!=row.UtilizationPercent||a.ResponseTargetMinutes!=row.ResponseTargetMinutes||
                Json(p.Result)!=Json(SupportCapacityCalculation.Calculate(r,a,r.AsOfUtc))||p.Fingerprint!=PreviewHash(r,a,p.Result))throw new JsonException();
            return new(new(row.Id,row.Name,row.OwnerId,r.AccountableOwner,row.Year,row.Month,row.TargetYear,row.TargetMonth,row.SavedUtc,row.PreviousId),p,row.Checksum);
        }catch(Exception e)when(e is JsonException or ArgumentException or NullReferenceException or InvalidOperationException or TimeZoneNotFoundException or InvalidTimeZoneException){throw new InvalidDataException("The original capacity evidence cannot be reproduced.",e);}
    }
    private static string PreviewHash(SupportQualityReport r,SupportCapacityAssumptions a,SupportCapacityResult result)=>Hash(r.Fingerprint+Json(a)+Json(result));
    private static SupportIssueGroupingSummary GroupSummary(SupportIssueGroupingRevision r)=>new(r.Id,r.SupportCaseId,r.Group,r.Reason,r.ActorId,r.SavedUtc,r.PreviousId,r.SourceFingerprint);
    private Task Audit(Guid company,Guid actor,string action,Guid target,string message,CancellationToken ct)=>audit.WriteAsync(new AuditEventWriteRequest(company,AuditActorTypes.Human,actor,action,"support_planning_revision",target.ToString("D"),AuditEventOutcomes.Succeeded,message,["support","planning"]),ct);
    private async Task Lock(Guid company,CancellationToken ct)
    {
        if(!db.Database.IsSqlServer())return;
        var resource=$"support-planning:{company:N}";
        await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource={resource}, @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=10000; IF @r < 0 THROW 50001, 'Support planning is busy; reload and retry.', 1;",ct);
    }
    public async Task<SupportQualityExport> ProposalExportAsync(Guid company,Guid id,CancellationToken ct)
    {
        var p=await OpenAsync(company,id,ct);var a=p.Preview.Assumptions;var r=p.Preview.Result;
        var csv=new StringBuilder("Proposal,Owner,Source period,Target period,Observed arrivals,Assumed arrivals,Backlog assumption,Handling minutes,People,Hours per business day,Utilization percent,Response target minutes,Business days,Required hours,Available hours,Case capacity,Shortfall hours,Required people,Explanation,Limits\r\n");
        csv.AppendLine(string.Join(",",new object?[]{p.Summary.Name,p.Summary.Owner,$"{p.Summary.Year}-{p.Summary.Month:00}",$"{a.TargetYear}-{a.TargetMonth:00}",r.ObservedArrivals,a.ExpectedArrivals,a.BacklogToClear,a.HandlingMinutes,a.AvailablePeople,a.HoursPerBusinessDay,a.UtilizationPercent,a.ResponseTargetMinutes,r.BusinessDays,r.RequiredHours,r.AvailableHours,r.CaseCapacity,r.ShortfallHours,r.RequiredPeople,a.Explanation,r.Limits}.Select(Cell)));
        return new("support-capacity.csv","text/csv",csv.ToString(),p.Checksum);
    }
}
