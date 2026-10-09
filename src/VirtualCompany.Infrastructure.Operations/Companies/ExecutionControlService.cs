using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
namespace VirtualCompany.Infrastructure.Companies;

public sealed class ExecutionControlService(VirtualCompanyDbContext db, CompanyWorkVisibility visibility,
    ICompanyMembershipContextResolver memberships, IAgentExecutionControlGate gate, IAuditEventWriter audit, TimeProvider clock) : IExecutionControlService
{
    private async Task<(ResolvedCompanyMembershipContext Member, CompanyWorkScope Scope, List<Agent> Agents)> Access(Guid company, Guid? agent, bool write, CancellationToken ct)
    {
        if (agent==Guid.Empty) throw new ArgumentException("Choose an agent or company scope.");
        var member=await memberships.ResolveAsync(company,ct)??throw new UnauthorizedAccessException();
        var scope=await visibility.ResolveAsync(company,ct);
        var agents=await scope.Agents(db.Agents.IgnoreQueryFilters().AsNoTracking()).ToListAsync(ct);
        if (agent.HasValue&&!agents.Any(x=>x.Id==agent)) throw new KeyNotFoundException();
        if (write && (member.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager) || !agent.HasValue&&!scope.Executive)) throw new UnauthorizedAccessException();
        return (member,scope,agents);
    }
    public async Task<ExecutionControlView> GetAsync(Guid company, Guid? agent, CancellationToken ct)
    {
        var access=await Access(company,agent,false,ct); var scopeId=agent??Guid.Empty;
        var config=await db.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company,ct);
        var control=await db.AgentExecutionControls.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company&&x.ScopeId==scopeId,ct);
        var ids=access.Agents.Select(x=>x.Id).ToArray(); var names=access.Agents.ToDictionary(x=>x.Id,x=>x.DisplayName);
        var tasks=access.Scope.Tasks(db.WorkTasks.IgnoreQueryFilters().AsNoTracking()).Where(x=>x.CompanyId==company && (!agent.HasValue||x.AssignedAgentId==agent));
        var taskIds=tasks.Select(x=>x.Id);
        var rows=await db.OperatingDispatches.IgnoreQueryFilters().AsNoTracking().Include(x=>x.Task)
            .Where(x=>x.CompanyId==company&&taskIds.Contains(x.TaskId)).OrderByDescending(x=>x.UpdatedUtc).Take(100).ToListAsync(ct);
        var work=new List<ExecutionControlWork>();
        foreach(var x in rows)
        {
            var paused=await gate.IsPausedAsync(company,x.Task.AssignedAgentId,ct,x.TaskId);
            var state=x.Status switch {
                OperatingDispatchStatus.Pending or OperatingDispatchStatus.RetryScheduled or OperatingDispatchStatus.Claimed => paused?"Paused before start":"Queued",
                OperatingDispatchStatus.Paused=>"Paused before start",
                OperatingDispatchStatus.Running=>x.LeaseExpiresUtc<=clock.GetUtcNow().UtcDateTime?"Uncertain":paused?"Stopping between steps":"In flight",
                OperatingDispatchStatus.Uncertain=>"Uncertain",
                OperatingDispatchStatus.Completed=>"Completed internal work",
                OperatingDispatchStatus.AwaitingApproval=>"Waiting for approval", _=>"Blocked" };
            work.Add(new(x.TaskId,"task",x.Task.AssignedAgentId,x.Task.AssignedAgentId is { } a?names.GetValueOrDefault(a,"Assigned agent"):"Unassigned",x.Task.Title,state,x.UpdatedUtc,
                state=="Uncertain"?"Review recorded attempts and owning provider reconciliation. Automatic retry is unavailable.":x.FailureSummary??"Resume rechecks current policy, approval, assignment and budgets."));
        }
        var runs=await db.FinanceAutonomyRuns.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==company && ids.Contains(x.AgentId) && (!agent.HasValue||x.AgentId==agent)).OrderByDescending(x=>x.UpdatedUtc).Take(100).ToListAsync(ct);
        foreach(var x in runs)
        {
            var paused=await gate.IsPausedAsync(company,x.AgentId,ct);
            var state=x.Status switch { FinanceAutonomyRunStatus.Reconciling=>"Uncertain",FinanceAutonomyRunStatus.Completed=>"Completed internal work",FinanceAutonomyRunStatus.Running=>paused?"Stopping between steps":"In flight",FinanceAutonomyRunStatus.Queued=>paused?"Paused before start":"Queued",FinanceAutonomyRunStatus.Paused=>"Paused before start",FinanceAutonomyRunStatus.AwaitingApproval=>"Waiting for approval",_=>"Blocked" };
            work.Add(new(x.Id,"finance_run",x.AgentId,names.GetValueOrDefault(x.AgentId,"Finance agent"),"Finance review run",state,x.UpdatedUtc,"Use the owning Finance run and reconciliation controls. Resume does not renew its grant or approval."));
        }
        var admissions=await db.AgentExecutionAdmissions.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==company && (x.AgentId.HasValue?ids.Contains(x.AgentId.Value):access.Scope.Executive) && (!agent.HasValue||x.AgentId==agent)).OrderByDescending(x=>x.AdmittedUtc).Take(100).ToListAsync(ct);
        foreach(var x in admissions.Where(x=>!x.AcknowledgedUtc.HasValue||x.Confirmed==false))
        {
            var recoveryId=x.Id;var kind="admission";
            if(Guid.TryParse(x.BusinessKey.Split(':')[0],out var ownerId))
            {
                Guid? linkedTask=x.Boundary=="tool"?await db.ToolExecutionAttempts.IgnoreQueryFilters().AsNoTracking()
                    .Where(t=>t.CompanyId==company&&t.Id==ownerId).Select(t=>t.TaskId).SingleOrDefaultAsync(ct)
                    :x.Boundary=="dispatch"?await db.OperatingDispatches.IgnoreQueryFilters().AsNoTracking()
                    .Where(t=>t.CompanyId==company&&t.Id==ownerId).Select(t=>(Guid?)t.TaskId).SingleOrDefaultAsync(ct):null;
                if(linkedTask.HasValue&&await tasks.AnyAsync(t=>t.Id==linkedTask,ct)){recoveryId=linkedTask.Value;kind="task";}
                if(x.Boundary=="payment_submission"&&access.Scope.Allows("finance"))
                {
                    var batch=await db.PaymentBatchExecutions.IgnoreQueryFilters().AsNoTracking().Where(t=>t.CompanyId==company&&t.Id==ownerId).Select(t=>(Guid?)t.BatchId).SingleOrDefaultAsync(ct);
                    if(batch.HasValue){recoveryId=batch.Value;kind="payment_batch";}
                }
            }
            work.Add(new(recoveryId,kind,x.AgentId,x.AgentId is { } a?names.GetValueOrDefault(a,"Assigned agent"):"Company agents",x.Boundary=="tool"?"Controlled tool step":"Controlled delivery step",x.AcknowledgedUtc.HasValue||x.AdmittedUtc<clock.GetUtcNow().UtcDateTime.AddMinutes(-5)?"Uncertain":"In flight",x.AcknowledgedUtc??x.AdmittedUtc,"This step was admitted before pause. Check its owning work/provider record; this screen cannot resend it."));
        }
        if(access.Scope.Allows("support"))
        {
            var deliveries=await db.CompanyOutboxMessages.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==company&&x.Topic==VirtualCompany.Application.Companies.CompanyOutboxTopics.SupportReplyDeliveryRequested).OrderByDescending(x=>x.CreatedUtc).Take(100).ToListAsync(ct);
            foreach(var message in deliveries)
            {
                VirtualCompany.Application.Companies.SupportReplyDeliveryRequestedMessage? delivery;
                try{delivery=JsonSerializer.Deserialize<VirtualCompany.Application.Companies.SupportReplyDeliveryRequestedMessage>(message.PayloadJson,new JsonSerializerOptions(JsonSerializerDefaults.Web));}catch(JsonException){continue;}
                if(delivery is null||!delivery.Autonomous||delivery.CompanyId!=company)continue;
                var draft=await db.SupportReplyDrafts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company&&x.Id==delivery.DraftId,ct);
                if(draft is null||agent.HasValue&&draft.CreatedByAgentId!=agent||draft.CreatedByAgentId.HasValue&&!ids.Contains(draft.CreatedByAgentId.Value))continue;
                var title=await db.SupportCases.IgnoreQueryFilters().Where(x=>x.CompanyId==company&&x.Id==draft.SupportCaseId).Select(x=>x.Subject).SingleOrDefaultAsync(ct);
                var admission=admissions.FirstOrDefault(x=>x.Boundary=="support_delivery"&&x.BusinessKey==delivery.IdempotencyKey);
                var state=draft.DeliveryStatus==SupportReplyDeliveryStatuses.ReconciliationRequired?"Uncertain":draft.SentUtc.HasValue?"Sent recorded":admission is not null?(admission.Confirmed==false?"Uncertain":"In flight"):await gate.IsPausedAsync(company,draft.CreatedByAgentId,ct)?"Paused before start":"Queued";
                if(admission is not null)work.RemoveAll(x=>x.Id==admission.Id);
                work.Add(new(draft.SupportCaseId,"case",draft.CreatedByAgentId,draft.CreatedByAgentId is { } a?names.GetValueOrDefault(a,"Support agent"):"Support agent",title??"Support reply delivery",state,message.LastAttemptUtc??message.CreatedUtc,"Delivery, safety and provider reconciliation remain owned by the Support case. Pause cannot undo a sent reply."));
            }
        }
        if(!agent.HasValue&&access.Scope.Allows("finance"))
        {
            var payments=await db.PaymentBatchExecutions.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==company).OrderByDescending(x=>x.UpdatedUtc).Take(100).ToListAsync(ct);
            foreach(var payment in payments)
            {
                var state=payment.Status switch{PaymentExecutionStatuses.Queued=>config?.IsPaused==true||config?.EmergencyStopped==true?"Paused before start":"Queued",PaymentExecutionStatuses.Submitting=>"In flight",PaymentExecutionStatuses.ReconciliationRequired=>"Uncertain",PaymentExecutionStatuses.ProviderCompleted=>"Provider confirmed",PaymentExecutionStatuses.Settled=>"Settled",_=>"Review provider state"};
                // Owning provider confirmation resolves an earlier missing boundary acknowledgement.
                if(work.Any(x=>x.Kind=="payment_batch"&&x.Id==payment.BatchId)&&payment.Status is not (PaymentExecutionStatuses.ProviderCompleted or PaymentExecutionStatuses.Settled))continue;
                work.RemoveAll(x=>x.Kind=="payment_batch"&&x.Id==payment.BatchId);
                work.Add(new(payment.BatchId,"payment_batch",null,"Finance provider instruction","Payment batch submission",state,payment.UpdatedUtc,"Company pause holds queued submissions, including reviewed human requests. Provider polls and reconciliation continue. Agent attribution is not recorded for these instructions."));
            }
        }
        var history=await db.AgentExecutionControlCommands.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==company && x.ScopeId==scopeId && (agent.HasValue||access.Scope.Executive)).OrderByDescending(x=>x.ChangedUtc).Take(100).ToListAsync(ct);
        var actors=await db.Users.Where(x=>db.CompanyMemberships.IgnoreQueryFilters().Any(m=>m.CompanyId==company&&m.UserId==x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.DisplayName,ct);
        return new(company,agent,control?.Version??0,config?.Version??0,agent.HasValue?control?.Paused==true:config?.IsPaused==true,config?.IsPaused==true,config?.EmergencyStopped==true,
            (access.Member.MembershipRole is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager) && (agent.HasValue||access.Scope.Executive),clock.GetUtcNow().UtcDateTime,
            access.Agents.Select(x=>new ExecutionControlAgent(x.Id,x.DisplayName)).ToArray(),work.OrderByDescending(x=>x.UpdatedUtc).Take(100).ToArray(),
            history.Select(x=>new ExecutionControlHistory(x.Id,x.Version,x.ScopeId==Guid.Empty?null:x.ScopeId,x.Paused,actors.GetValueOrDefault(x.ActorId,"Authorized manager"),x.ChangedUtc,x.Reason)).ToArray());
    }
    public async Task<ExecutionControlPreview> PreviewAsync(Guid company, ExecutionControlChange change, CancellationToken ct)
    {
        await Access(company,change.AgentId,true,ct);
        if(change.CommandId==Guid.Empty||string.IsNullOrWhiteSpace(change.Reason)||change.Reason.Length>500)throw new ArgumentException("Provide a command identity and a reason of at most 500 characters.");
        var before=await GetAsync(company,change.AgentId,ct);
        if(before.Version!=change.ExpectedVersion||before.CompanyVersion!=change.ExpectedCompanyVersion)throw new ExecutionControlConflictException("Controls changed. Refresh and review a new preview.");
        if(!change.Pause&&before.EmergencyStopped)throw new ArgumentException("Clear the emergency stop through company operation settings before resuming.");
        return new(change,before,Hash(new{company,change}),change.Pause?"Pause prevents admission of new controlled steps. Already admitted steps may complete or need reconciliation; completed delivery cannot be undone.":"Resume releases paused work for current eligibility checks. Revoked policies, expired approvals and uncertain outcomes remain blocked. Company-wide pause overrides an agent resume.");
    }
    public async Task<ExecutionControlView> ApplyAsync(Guid company, ExecutionControlApply apply, CancellationToken ct)
    {
        var access=await Access(company,apply.Change.AgentId,true,ct); var requestHash=Hash(new{company,change=apply.Change});
        await using var fence=await gate.FenceAsync(company,ct);
        var prior=await db.AgentExecutionControlCommands.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company&&x.Id==apply.Change.CommandId,ct);
        if(prior is not null) { if(prior.RequestHash!=requestHash||apply.PreviewHash!=requestHash)throw new ExecutionControlConflictException("This command identity belongs to a different reviewed request."); await fence.CommitAsync(ct); return await GetAsync(company,apply.Change.AgentId,ct); }
        var preview=await PreviewAsync(company,apply.Change,ct);
        if(preview.PreviewHash!=apply.PreviewHash)throw new ExecutionControlConflictException("Preview changed. Refresh and review again.");
        var scopeId=apply.Change.AgentId??Guid.Empty; var now=clock.GetUtcNow().UtcDateTime;
        var control=await db.AgentExecutionControls.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.CompanyId==company&&x.ScopeId==scopeId,ct);
        if(control is null){control=new(company,scopeId);db.AgentExecutionControls.Add(control);}control.Change(apply.Change.Pause,now);
        if(!apply.Change.AgentId.HasValue) {
            var config=await db.CompanyOperatingConfigurations.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.CompanyId==company,ct);
            if(config is null){config=new(Guid.NewGuid(),company);db.CompanyOperatingConfigurations.Add(config);}
            if(apply.Change.Pause)config.Pause(apply.Change.Reason);else config.Resume();
        }
        var scopedTasks=db.WorkTasks.IgnoreQueryFilters().Where(x=>x.CompanyId==company && (!apply.Change.AgentId.HasValue||x.AssignedAgentId==apply.Change.AgentId)).Select(x=>x.Id);
        var dispatches=await db.OperatingDispatches.IgnoreQueryFilters().Where(x=>x.CompanyId==company&&scopedTasks.Contains(x.TaskId)).ToListAsync(ct);
        foreach(var dispatch in dispatches) { if(apply.Change.Pause)dispatch.PauseBeforeStart(now);else dispatch.ResumePaused(now); }
        db.AgentExecutionControlCommands.Add(new(company,apply.Change.CommandId,scopeId,control.Version,apply.Change.Pause,access.Member.UserId,apply.Change.Reason.Trim(),requestHash,now));
        await audit.WriteAsync(new(company,AuditActorTypes.User,access.Member.UserId,apply.Change.Pause?"agent.execution.paused":"agent.execution.resumed","agent_execution_control",control.Id.ToString("D"),AuditEventOutcomes.Succeeded,RationaleSummary:apply.Change.Reason,Metadata:new Dictionary<string,string?>{["scope"]=apply.Change.AgentId.HasValue?"agent":"company",["revision"]=control.Version.ToString(),["commandId"]=apply.Change.CommandId.ToString("D")}),ct);
        try {await db.SaveChangesAsync(ct);await fence.CommitAsync(ct);}catch(DbUpdateException){throw new ExecutionControlConflictException("Execution controls or work changed concurrently. Refresh before retrying.");}
        return await GetAsync(company,apply.Change.AgentId,ct);
    }
    private static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
}
