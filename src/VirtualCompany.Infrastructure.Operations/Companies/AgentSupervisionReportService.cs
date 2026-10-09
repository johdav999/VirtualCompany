using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

// One definition owner: cards, permitted drill-down and CSV are derived from these same retained rows.
public sealed class AgentSupervisionReportService(VirtualCompanyDbContext db, CompanyWorkVisibility visibility,
    ICompanyMembershipContextResolver memberships, TimeProvider clock) : IAgentSupervisionReportService
{
    private const int Limit = 2000;
    private sealed record Definition(string Code,string View,string Name,string Meaning,string Rule);
    private static readonly Definition[] Definitions = [
        new("work_in_progress","work","Work in progress","Included permitted work created before period end","Current observed work state; linked tasks and collaborators do not create extra company work. This is not a historical status reconstruction."),
        new("contributions","work","Agent contributions","Retained contribution streams in this period","Latest retained version in the period per parent/source-task/agent/sequence. Separate from company outcomes."),
        new("prepared_output","outcomes","Completed retained outputs","Permitted company work completed in the period","Completed root task with retained output; linked initiative counts once. This is prepared output, not delivered or achieved benefit."),
        new("reviewed_outcome","outcomes","Reviewed company outcomes","Permitted company initiatives with review events in the period","CloseSuccessful review with nonempty actual evidence; one initiative once. Recorded review does not independently prove economic impact."),
        new("provider_confirmed","outcomes","Provider-confirmed payments","Permitted payment batches with confirmation in the period","Owning ProviderCompletedUtc; one batch once across instructions/retries. Agent attribution is unrecorded."),
        new("business_outcome","outcomes","Recorded payment settlements","Permitted payment batches with settlement in the period","Owning SettledUtc; one batch once. Settlement is a recorded business outcome, not proof an agent caused it."),
        new("delivery_recorded","outcomes","Recorded Support sends","Permitted attributed Support drafts with send time in the period","SentUtc on the owning draft, one draft once; a sent record is distinct from independently measured delivery or customer benefit."),
        new("execution_failure","outcomes","Work with execution failures","Permitted company work with tool attempts in the period","Failed completed tool attempts grouped by their actual root work identity; technical retries do not multiply affected work."),
        new("blocked_work","bottlenecks","Blocked work","Included permitted work created before period end","Current blocked, failed or uncertain work snapshot. Historical blocked duration is unavailable without complete transition history."),
        new("approval_wait","bottlenecks","Approval waiting intervals","Permitted approval intervals intersecting this period","Unique approval request; seconds intersect [period start, min(period end, observed time)). Resolved and ongoing intervals are retained."),
        new("approval_turnaround","bottlenecks","Resolved approvals","Included permitted approval intervals intersecting this period","Decisions inside this period; DecidedUtc minus CreatedUtc. Average uses only nonnegative known resolved intervals. Period waiting seconds are reported separately."),
        new("corrections","bottlenecks","Work with recorded corrections","Included permitted work created before period end","Immutable contribution revisions, Revise reviews and request-changes review audits in this period; one affected work once, all evidence retained."),
        new("policy_exceptions","authority","Recorded policy denials","Included permitted tool attempts with period activity or linked audit events","Immutable agent.tool_execution.denied audit events, deduplicated by audit identity. Events are not additional business outcomes or a percentage of attempts."),
        new("interventions","authority","Recorded supervisor interventions","Included permitted control commands in the period","Immutable pause/resume command receipts, once per command identity; company commands require executive scope."),
        new("escalations","authority","Work with recorded escalations","Included initiatives with review events in the period","Escalate operating reviews, once per initiative with all retained review evidence; not a count of technical retries."),
        new("budget_history","authority","Historical combined budget usage","Work with comparable historical budget units","Unavailable: retained sources do not provide one comparable company/task/agent budget history. Current limits are not historical usage."),
        new("blocked_handoffs","bottlenecks","Recorded blocked handoffs","Included permitted handoff receipts in the period","Failed collaboration handoff receipts, once per handoff identity. Receipt latency is receipt CreatedUtc minus input contribution CreatedUtc, not historical blocked duration."),
        new("blocked_duration","bottlenecks","Historical blocked duration","Work with complete typed blocked transitions","Unavailable: current status alone cannot reconstruct historical blocked intervals.")
    ];
    public async Task<AgentSupervisionReport> GetAsync(AgentSupervisionQuery query,CancellationToken ct)
    {
        var member=await memberships.ResolveAsync(query.CompanyId,ct)??throw new UnauthorizedAccessException();
        if(member.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager))throw new UnauthorizedAccessException();
        if(!new[]{"work","outcomes","bottlenecks","authority"}.Contains(query.View)||query.TaskType?.Length>100||
            query.Metric is not null&&!Definitions.Any(x=>x.Code==query.Metric&&x.View==query.View))throw new ArgumentException("Choose valid report filters.");
        var scope=await visibility.ResolveAsync(query.CompanyId,ct);
        if(query.Responsibility is not null&&!scope.Allows(query.Responsibility))throw new UnauthorizedAccessException();
        var company=await db.Companies.AsNoTracking().SingleAsync(x=>x.Id==query.CompanyId,ct);
        var zone=TimeZoneInfo.FindSystemTimeZoneById(company.Timezone??"UTC");
        var observed=clock.GetUtcNow().UtcDateTime;
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(observed,zone));
        var from=query.From??today.AddDays(-29);var to=query.To??today;
        if(to<from||to.DayNumber-from.DayNumber>365||to==DateOnly.MaxValue)throw new ArgumentException("Choose an ordered period of at most 366 days.");
        var start=TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue),zone);
        var end=TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue),zone);
        var cutoff=end<observed?end:observed;
        if(start>=observed)throw new ArgumentException("Choose a reporting period that has begun.");
        query=query with{From=from,To=to};
        var companyId=query.CompanyId;
        var agentQuery=scope.Agents(db.Agents.AsNoTracking());
        var agents=await agentQuery.OrderBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);
        var partial=agents.Count>Limit;agents=agents.Take(Limit).ToList();
        if(query.AgentId.HasValue&&!agents.Any(x=>x.Id==query.AgentId))throw new KeyNotFoundException();
        var agentIds=agentQuery.Select(x=>x.Id);
        var allowedTasks=scope.Tasks(db.WorkTasks.AsNoTracking().Where(x=>x.CompanyId==companyId));
        var allowedTaskIds=allowedTasks.Select(x=>x.Id);
        // Apply complete shared-work visibility before limits, counts or evidence reads.
        var allowedInitiatives=db.OperatingInitiatives.AsNoTracking().Include(x=>x.Goal).Where(x=>x.CompanyId==companyId&&
            (x.OwnerAgentId.HasValue?agentIds.Contains(x.OwnerAgentId.Value):scope.Executive)&&
            (!x.TaskId.HasValue||allowedTaskIds.Contains(x.TaskId.Value))&&
            !db.OperatingInitiativeCollaborators.Any(c=>c.CompanyId==companyId&&c.InitiativeId==x.Id&&!agentIds.Contains(c.AgentId)));
        var visibleInitiativeIds=allowedInitiatives.Select(x=>x.Id);
        var visibleTaskRoots=allowedTasks.Where(t=>!db.OperatingInitiatives.Any(i=>i.CompanyId==companyId&&i.TaskId==t.Id&&!visibleInitiativeIds.Contains(i.Id)));
        var permittedTaskTree=visibleTaskRoots;
        // Suppress restricted shared roots and their descendants before source bounds and filter options.
        for(var depth=0;depth<8;depth++){
            var precedingIds=permittedTaskTree.Select(x=>x.Id);
            permittedTaskTree=visibleTaskRoots.Where(x=>!x.ParentTaskId.HasValue||precedingIds.Contains(x.ParentTaskId.Value));
        }
        var permittedTaskIds=permittedTaskTree.Select(x=>x.Id);
        // Older completed work can have a new correction, decision or failure. Never select it solely by completion time.
        var taskSource=await permittedTaskTree.Where(x=>x.CreatedUtc<cutoff)
            .OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);
        partial|=taskSource.Count>Limit;taskSource=taskSource.Take(Limit).ToList();
        var allTypes=taskSource.Select(x=>x.Type).Concat(["company.initiative","company.execution_control"]).Concat(scope.Allows("finance")?["finance.payment_submission"]:[])
            .Concat(scope.Allows("support")?["support.delivery"]:[]).Distinct().OrderBy(x=>x).ToArray();
        var initiatives=await allowedInitiatives.Where(x=>x.CreatedUtc<cutoff).OrderByDescending(x=>x.UpdatedUtc).ThenBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);
        partial|=initiatives.Count>Limit;initiatives=initiatives.Take(Limit).ToList();
        var initiativeIds=initiatives.Select(x=>x.Id).ToArray();
        var linkedTasks=initiatives.Where(x=>x.TaskId.HasValue).Select(x=>x.TaskId!.Value).ToArray();
        var taskIds=taskSource.Select(x=>x.Id).Concat(linkedTasks).Distinct().ToArray();
        var loadedTaskIds=taskSource.Select(x=>x.Id).ToArray();
        var missingParents=await permittedTaskTree.Where(x=>linkedTasks.Contains(x.Id)&&!loadedTaskIds.Contains(x.Id)).ToListAsync(ct);
        taskSource.AddRange(missingParents);
        // Resolve permitted ancestry with a bounded read. A missing/restricted ancestor is not a new company outcome.
        for(var depth=0;depth<8;depth++)
        {
            var loaded=taskSource.Select(x=>x.Id).ToArray();
            var wanted=taskSource.Where(x=>x.ParentTaskId.HasValue&&!loaded.Contains(x.ParentTaskId.Value)).Select(x=>x.ParentTaskId!.Value).Distinct().ToArray();
            if(wanted.Length==0)break;
            var parents=await permittedTaskTree.Where(x=>wanted.Contains(x.Id)).Take(Limit+1).ToListAsync(ct);
            partial|=parents.Count>Limit;
            if(parents.Count==0)break;
            taskSource.AddRange(parents.Take(Limit));
        }
        taskIds=taskSource.Select(x=>x.Id).Distinct().ToArray();
        var sharedTaskIds=await db.OperatingInitiatives.AsNoTracking().Where(x=>x.CompanyId==companyId&&x.TaskId.HasValue&&taskIds.Contains(x.TaskId.Value)).Select(x=>x.TaskId!.Value).ToListAsync(ct);
        var parentIds=taskIds;
        var taskMap=taskSource.ToDictionary(x=>x.Id);
        var agentMap=agents.ToDictionary(x=>x.Id);
        var dispatches=await db.OperatingDispatches.AsNoTracking().Where(x=>x.CompanyId==companyId&&initiativeIds.Contains(x.InitiativeId))
            .OrderByDescending(x=>x.UpdatedUtc).ThenBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);
        partial|=dispatches.Count>Limit;dispatches=dispatches.Take(Limit).ToList();
        var reviews=await db.OperatingReviews.AsNoTracking().Where(x=>x.CompanyId==companyId&&initiativeIds.Contains(x.InitiativeId)&&x.CreatedUtc<observed)
            .OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);
        partial|=reviews.Count>Limit;reviews=reviews.Take(Limit).ToList();
        var approvals=await db.ApprovalRequests.AsNoTracking().Where(x=>x.CompanyId==companyId&&x.TargetEntityType=="task"&&taskIds.Contains(x.TargetEntityId)&&
            x.CreatedUtc<cutoff&&(!x.DecidedUtc.HasValue||x.DecidedUtc>=start||db.AuditEvents.Any(a=>a.CompanyId==companyId&&a.RelatedApprovalRequestId==x.Id&&a.OccurredUtc>=start&&a.OccurredUtc<cutoff)))
            .OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);
        partial|=approvals.Count>Limit;approvals=approvals.Take(Limit).ToList();
        var attempts=await db.ToolExecutionAttempts.AsNoTracking().Where(x=>x.CompanyId==companyId&&x.TaskId.HasValue&&taskIds.Contains(x.TaskId.Value)&&
            agentIds.Contains(x.AgentId)&&x.CreatedUtc<cutoff&&(x.CreatedUtc>=start||x.CompletedUtc>=start||
                db.AuditEvents.Any(a=>a.CompanyId==companyId&&a.RelatedToolExecutionAttemptId==x.Id&&a.OccurredUtc>=start&&a.OccurredUtc<cutoff)))
            .OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);
        partial|=attempts.Count>Limit;attempts=attempts.Take(Limit).ToList();
        var attemptIds=attempts.Select(x=>x.Id).ToArray();var approvalIds=approvals.Select(x=>x.Id).ToArray();
        var events=await db.AuditEvents.AsNoTracking().Where(x=>x.CompanyId==companyId&&x.OccurredUtc>=start&&x.OccurredUtc<cutoff&&
            (x.RelatedToolExecutionAttemptId.HasValue&&attemptIds.Contains(x.RelatedToolExecutionAttemptId.Value)||
             x.RelatedApprovalRequestId.HasValue&&approvalIds.Contains(x.RelatedApprovalRequestId.Value)))
            .OrderByDescending(x=>x.OccurredUtc).ThenBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);
        partial|=events.Count>Limit;events=events.Take(Limit).ToList();
        var allowedContributions=db.CollaborationContributions.AsNoTracking().Where(x=>x.CompanyId==companyId&&parentIds.Contains(x.ParentTaskId)&&
            permittedTaskIds.Contains(x.SourceTaskId)&&agentIds.Contains(x.AgentId)&&x.CreatedUtc<cutoff);
        var contributions=await allowedContributions.Where(x=>x.CreatedUtc>=start)
            .OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);
        partial|=contributions.Count>Limit;contributions=contributions.Take(Limit).ToList();
        var rows=new List<SupervisionRow>();
        bool InPeriod(DateTime? time)=>time.HasValue&&time>=start&&time<cutoff;
        (string Kind,Guid Id,WorkTask Task) Root(WorkTask task)
        {
            var visited=new HashSet<Guid>();
            while(task.ParentTaskId is { } parent&&taskMap.TryGetValue(parent,out var previous)&&visited.Add(parent))task=previous;
            var initiative=initiatives.FirstOrDefault(x=>x.TaskId==task.Id);
            return (initiative is null?"task":"initiative",initiative?.Id??task.Id,task);
        }
        void Add(string metric,string kind,Guid id,WorkTask? task,string title,string area,Guid? agent,string type,
            string state,DateTime time,string stage,IEnumerable<SupervisionEvidence> evidence,Guid? approval=null,
            decimal? seconds=null,decimal? total=null,bool ongoing=false,string? unit=null)
        {
            if(query.Responsibility is not null&&query.Responsibility!=area||query.TaskType is not null&&query.TaskType!=type||query.AgentId.HasValue&&query.AgentId!=agent)return;
            var name=agent.HasValue?agentMap.GetValueOrDefault(agent.Value)?.DisplayName??"Assigned agent":"Attribution not recorded";
            var route=kind switch{"task" or "initiative"=>$"/agents/work/{kind}/{id:D}?companyId={companyId:D}","case"=>$"/support/cases/{id:D}?companyId={companyId:D}","payment_batch"=>$"/finance/payments/batches/{id:D}?companyId={companyId:D}","control"=>$"/settings/agents/execution?companyId={companyId:D}"+(agent.HasValue?$"&agentId={agent:D}":""),_=>$"/agents/staff?companyId={companyId:D}"};
            rows.Add(new(unit??$"{metric}:{kind}:{id:N}",metric,kind,id,title,area,type,agent,name,state,time,stage,route,approval,seconds,total,ongoing,evidence.ToArray()));
        }
        void TaskRow(string metric,WorkTask task,string state,DateTime time,string stage,IEnumerable<SupervisionEvidence> evidence,
            Guid? approval=null,decimal? seconds=null,decimal? total=null,bool ongoing=false)
        {
            var root=Root(task);
            if(root.Task.ParentTaskId.HasValue){partial=true;return;}
            if(sharedTaskIds.Contains(root.Task.Id)&&!linkedTasks.Contains(root.Task.Id))return;
            var owner=root.Task.AssignedAgentId;var area=CompanyWorkScope.Area(owner.HasValue?agentMap.GetValueOrDefault(owner.Value)?.Department:null,root.Task.Type);
            Add(metric,root.Kind,root.Id,root.Task,root.Task.Title,area,owner,root.Task.Type,state,time,stage,evidence,approval,seconds,total,ongoing);
        }
        void InitiativeRow(string metric,OperatingInitiative initiative,string state,DateTime time,string stage,IEnumerable<SupervisionEvidence> evidence)
        {
            if(initiative.TaskId.HasValue&&taskMap.TryGetValue(initiative.TaskId.Value,out var task))TaskRow(metric,task,state,time,stage,evidence);
            else Add(metric,"initiative",initiative.Id,null,initiative.Title,CompanyWorkScope.Area(initiative.OwnerAgentId.HasValue?agentMap.GetValueOrDefault(initiative.OwnerAgentId.Value)?.Department:null),
                initiative.OwnerAgentId,"company.initiative",state,time,stage,evidence);
        }
        var standalone=taskSource.Where(x=>x.ParentTaskId is null&&!linkedTasks.Contains(x.Id)).ToList();
        // Never turn a restricted/bounded-out shared initiative into a standalone outcome.
        standalone=standalone.Where(x=>!sharedTaskIds.Contains(x.Id)).ToList();
        var companyWork=standalone.Concat(initiatives.Where(x=>x.TaskId.HasValue&&taskMap.ContainsKey(x.TaskId.Value)).Select(x=>taskMap[x.TaskId!.Value])).DistinctBy(x=>x.Id).ToList();
        foreach(var task in companyWork)
        {
            var initiative=initiatives.FirstOrDefault(x=>x.TaskId==task.Id);
            var state=initiative is null?task.Status.ToStorageValue():CompanyAgentWorkQueryService.InitiativeState(initiative,dispatches.FirstOrDefault(x=>x.InitiativeId==initiative.Id),reviews.FirstOrDefault(x=>x.InitiativeId==initiative.Id),approvals.FirstOrDefault(x=>x.TargetEntityId==task.Id));
            var evidence=new[]{new SupervisionEvidence(task.Id,"work_task",task.UpdatedUtc,"Current retained work state; not historical status reconstruction.")};
            if(initiative is not null?state is not ("completed" or "failed"):task.Status is not (WorkTaskStatus.Completed or WorkTaskStatus.Failed))TaskRow("work_in_progress",task,state,observed,"current_work",evidence);
            if(state is "blocked" or "failed" or "Blocked"||dispatches.Any(x=>x.TaskId==task.Id&&x.Status==OperatingDispatchStatus.Uncertain))TaskRow("blocked_work",task,state,observed,"current_work",evidence);
            if(task.Status==WorkTaskStatus.Completed&&InPeriod(task.CompletedUtc)&&task.OutputPayload.Count>0)
                TaskRow("prepared_output",task,"Retained output completed",task.CompletedUtc!.Value,"prepared_output",[new(task.Id,"work_task",task.CompletedUtc.Value,"CompletedUtc and nonempty retained output.")],total:Duration(task.CreatedUtc,task.CompletedUtc.Value));
        }
        foreach(var initiative in initiatives.Where(x=>!x.TaskId.HasValue))
        {
            var state=CompanyAgentWorkQueryService.InitiativeState(initiative,dispatches.FirstOrDefault(x=>x.InitiativeId==initiative.Id),reviews.FirstOrDefault(x=>x.InitiativeId==initiative.Id),null);
            var evidence=new[]{new SupervisionEvidence(initiative.Id,"operating_initiative",initiative.UpdatedUtc,"Current initiative state; no task is invented.")};
            if(state is not ("completed" or "cancelled"))InitiativeRow("work_in_progress",initiative,state,observed,"current_work",evidence);
            if(state is "blocked" or "failed"||dispatches.Any(x=>x.InitiativeId==initiative.Id&&x.Status==OperatingDispatchStatus.Uncertain))InitiativeRow("blocked_work",initiative,state,observed,"current_work",evidence);
        }
        foreach(var review in reviews.Where(x=>InPeriod(x.CreatedUtc)))
        {
            var initiative=initiatives.Single(x=>x.Id==review.InitiativeId);
            var evidence=new[]{new SupervisionEvidence(review.Id,"operating_review",review.CreatedUtc,$"{review.Outcome.ToStorageValue()} · evidence version {review.EvidenceVersion}")};
            if(review.Outcome==OperatingReviewOutcome.CloseSuccessful&&!string.IsNullOrWhiteSpace(review.ActualEvidence))InitiativeRow("reviewed_outcome",initiative,"Reviewed with recorded evidence",review.CreatedUtc,"reviewed_outcome",evidence);
            if(review.Outcome==OperatingReviewOutcome.Revise)InitiativeRow("corrections",initiative,"Revision requested",review.CreatedUtc,"correction",evidence);
            if(review.Outcome==OperatingReviewOutcome.Escalate)InitiativeRow("escalations",initiative,"Review escalated",review.CreatedUtc,"review_escalation",evidence);
        }
        foreach(var contribution in contributions.GroupBy(x=>new{x.ParentTaskId,x.SourceTaskId,x.AgentId,x.Sequence}).Select(x=>x.OrderByDescending(y=>y.Version).First()))
        {
            if(!taskMap.TryGetValue(contribution.ParentTaskId,out var parent))continue;
            if(Root(parent).Task.ParentTaskId.HasValue){partial=true;continue;}
            if(sharedTaskIds.Contains(Root(parent).Task.Id)&&!linkedTasks.Contains(Root(parent).Task.Id))continue;
            var area=CompanyWorkScope.Area(agentMap.GetValueOrDefault(contribution.AgentId)?.Department,parent.Type);
            Add("contributions","task",contribution.SourceTaskId,parent,contribution.Objective,area,contribution.AgentId,parent.Type,contribution.Status,contribution.CreatedUtc,"agent_contribution",
                contributions.Where(x=>x.ParentTaskId==contribution.ParentTaskId&&x.SourceTaskId==contribution.SourceTaskId&&x.AgentId==contribution.AgentId&&x.Sequence==contribution.Sequence)
                    .Select(x=>new SupervisionEvidence(x.Id,"collaboration_contribution",x.CreatedUtc,$"Version {x.Version}; {x.Role.ToStorageValue()}")),
                unit:$"contributions:{contribution.ParentTaskId:N}:{contribution.SourceTaskId:N}:{contribution.AgentId:N}:{contribution.Sequence}");
            foreach(var revision in contributions.Where(x=>x.ParentTaskId==contribution.ParentTaskId&&x.SourceTaskId==contribution.SourceTaskId&&x.AgentId==contribution.AgentId&&x.Sequence==contribution.Sequence&&x.Version>1))
                TaskRow("corrections",parent,"Retained contribution revised",revision.CreatedUtc,"correction",[new(revision.Id,"collaboration_contribution",revision.CreatedUtc,$"Version {revision.Version}")]);
        }
        foreach(var approval in approvals)
        {
            if(!taskMap.TryGetValue(approval.TargetEntityId,out var task))continue;
            var terminal=approval.IsTerminal||approval.DecidedUtc.HasValue;
            var stop=approval.DecidedUtc.HasValue&&approval.DecidedUtc<cutoff?approval.DecidedUtc.Value:cutoff;
            var known=approval.DecidedUtc.HasValue||!terminal;
            var seconds=known?Intersection(approval.CreatedUtc,stop,start,cutoff):null;
            var evidence=new[]{new SupervisionEvidence(approval.Id,"approval_request",approval.DecidedUtc??approval.CreatedUtc,"CreatedUtc, DecidedUtc and current decision state.")};
            if(!approval.DecidedUtc.HasValue||approval.DecidedUtc>=start)
                TaskRow("approval_wait",task,approval.Status.ToStorageValue(),approval.DecidedUtc??approval.CreatedUtc,"approval_interval",evidence,approval.Id,seconds,
                    known?Duration(approval.CreatedUtc,stop):null,!terminal||approval.DecidedUtc>=cutoff);
            if(InPeriod(approval.DecidedUtc))TaskRow("approval_turnaround",task,approval.Status.ToStorageValue(),approval.DecidedUtc!.Value,"resolved_approval",evidence,approval.Id,
                seconds,Duration(approval.CreatedUtc,approval.DecidedUtc.Value));
        }
        var contributionIds=allowedContributions.Select(x=>x.Id);
        var handoffs=await db.CollaborationArtifactHandoffs.AsNoTracking().Where(x=>x.CompanyId==companyId&&x.CreatedUtc>=start&&x.CreatedUtc<cutoff&&
            contributionIds.Contains(x.InputContributionId)&&contributionIds.Contains(x.ReceivingContributionId)).OrderByDescending(x=>x.CreatedUtc).ThenBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);
        partial|=handoffs.Count>Limit;handoffs=handoffs.Take(Limit).ToList();
        var receiptIds=handoffs.SelectMany(x=>new[]{x.InputContributionId,x.ReceivingContributionId}).Distinct().ToArray();
        var receipts=await allowedContributions.Where(x=>receiptIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,ct);
        var handoffDenominator=0;
        foreach(var handoff in handoffs)
        {
            var input=receipts[handoff.InputContributionId];var receiver=receipts[handoff.ReceivingContributionId];
            if(!taskMap.TryGetValue(receiver.ParentTaskId,out var parent)||!taskMap.TryGetValue(input.ParentTaskId,out var inputParent))continue;var root=Root(parent);var inputRoot=Root(inputParent);
            if(root.Task.ParentTaskId.HasValue||sharedTaskIds.Contains(root.Task.Id)&&!linkedTasks.Contains(root.Task.Id))continue;
            if(inputRoot.Task.ParentTaskId.HasValue||sharedTaskIds.Contains(inputRoot.Task.Id)&&!linkedTasks.Contains(inputRoot.Task.Id))continue;
            var area=CompanyWorkScope.Area(agentMap.GetValueOrDefault(receiver.AgentId)?.Department,parent.Type);
            if(query.Responsibility is not null&&query.Responsibility!=area||query.TaskType is not null&&query.TaskType!=parent.Type||query.AgentId.HasValue&&query.AgentId!=receiver.AgentId)continue;
            handoffDenominator++;
            if(!handoff.Passed)Add("blocked_handoffs","task",receiver.SourceTaskId,null,receiver.Objective,area,receiver.AgentId,parent.Type,"Recorded handoff blocked",handoff.CreatedUtc,"handoff_receipt",
                [new(handoff.Id,"collaboration_handoff",handoff.CreatedUtc,handoff.Reason??"Recorded handoff did not pass."),new(input.Id,"collaboration_contribution",input.CreatedUtc,"Input receipt creation; not the beginning of a measured blocked interval.")],
                total:Duration(input.CreatedUtc,handoff.CreatedUtc),unit:$"blocked_handoffs:{handoff.Id:N}");
        }
        var commands=await db.AgentExecutionControlCommands.AsNoTracking().Where(x=>x.CompanyId==companyId&&x.ChangedUtc>=start&&x.ChangedUtc<cutoff&&
            (x.ScopeId==Guid.Empty?scope.Executive:agentIds.Contains(x.ScopeId))).OrderByDescending(x=>x.ChangedUtc).ThenBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);
        partial|=commands.Count>Limit;
        foreach(var command in commands.Take(Limit)){
            Guid? agent=command.ScopeId==Guid.Empty?null:command.ScopeId;
            Add("interventions","control",command.Id,null,command.Paused?"Supervisor paused new steps":"Supervisor resumed new steps",CompanyWorkScope.Area(agent.HasValue?agentMap.GetValueOrDefault(agent.Value)?.Department:null),agent,"company.execution_control",
                command.Paused?"Pause recorded":"Resume recorded",command.ChangedUtc,"supervisor_command",[new(command.Id,"execution_control_command",command.ChangedUtc,$"Version {command.Version}: {command.Reason}")]);
        }
        foreach(var attempt in attempts.Where(x=>x.Status==ToolExecutionStatus.Failed&&InPeriod(x.CompletedUtc)))
            if(taskMap.TryGetValue(attempt.TaskId!.Value,out var task))TaskRow("execution_failure",task,"Recorded tool failure",attempt.CompletedUtc!.Value,"execution_failure",
                [new(attempt.Id,"tool_execution_attempt",attempt.CompletedUtc.Value,$"{attempt.ToolName}; failed")]);
        foreach(var audit in events)
        {
            if(audit.Action=="agent.tool_execution.denied"&&audit.RelatedToolExecutionAttemptId.HasValue)
            {
                var attempt=attempts.SingleOrDefault(x=>x.Id==audit.RelatedToolExecutionAttemptId);if(attempt is null||!taskMap.TryGetValue(attempt.TaskId!.Value,out var task))continue;
                TaskRow("policy_exceptions",task,"Recorded policy denial",audit.OccurredUtc,"policy_decision",[new(audit.Id,"audit_event",audit.OccurredUtc,$"{attempt.ToolName}; {audit.Outcome}")]);
            }
            if((audit.Action is "approval.review.request_changes" or "approval.review.changes_requested")&&audit.RelatedApprovalRequestId.HasValue)
            {
                var approval=approvals.SingleOrDefault(x=>x.Id==audit.RelatedApprovalRequestId);if(approval is not null&&taskMap.TryGetValue(approval.TargetEntityId,out var task))
                    TaskRow("corrections",task,"Changes requested",audit.OccurredUtc,"correction",[new(audit.Id,"audit_event",audit.OccurredUtc,"Retained request-changes decision")],approval.Id);
            }
        }
        if(scope.Allows("finance")&&!query.AgentId.HasValue)
        {
            var payments=await db.PaymentBatchExecutions.AsNoTracking().Where(x=>x.CompanyId==companyId&&
                (x.ProviderCompletedUtc>=start&&x.ProviderCompletedUtc<cutoff||x.SettledUtc>=start&&x.SettledUtc<cutoff))
                .OrderByDescending(x=>x.UpdatedUtc).ThenBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);
            partial|=payments.Count>Limit;
            foreach(var payment in payments.Take(Limit))
            {
                if(InPeriod(payment.ProviderCompletedUtc))Add("provider_confirmed","payment_batch",payment.BatchId,null,"Payment batch provider confirmation","finance",null,"finance.payment_submission","Provider confirmed",payment.ProviderCompletedUtc!.Value,"provider_confirmed",[new(payment.Id,"payment_batch_execution",payment.ProviderCompletedUtc.Value,"Owning provider completion timestamp; agent attribution not recorded.")]);
                if(InPeriod(payment.SettledUtc))Add("business_outcome","payment_batch",payment.BatchId,null,"Payment batch settlement","finance",null,"finance.payment_submission","Settled recorded",payment.SettledUtc!.Value,"business_outcome",[new(payment.Id,"payment_batch_execution",payment.SettledUtc.Value,"Owning settlement timestamp; no attribution or causality inference.")]);
            }
        }
        if(scope.Allows("support"))
        {
            var sends=await db.SupportReplyDrafts.AsNoTracking().Where(x=>x.CompanyId==companyId&&db.SupportCases.Any(c=>c.CompanyId==companyId&&c.Id==x.SupportCaseId)&&x.CreatedByAgentId.HasValue&&agentIds.Contains(x.CreatedByAgentId.Value)&&x.SentUtc>=start&&x.SentUtc<cutoff)
                .OrderByDescending(x=>x.SentUtc).ThenBy(x=>x.Id).Take(Limit+1).ToListAsync(ct);partial|=sends.Count>Limit;
            foreach(var send in sends.Take(Limit))Add("delivery_recorded","case",send.SupportCaseId,null,"Retained Support send","support",send.CreatedByAgentId,"support.delivery","Sent recorded",send.SentUtc!.Value,"delivery_recorded",[new(send.Id,"support_reply_draft",send.SentUtc.Value,"SentUtc on the owning Support draft; customer benefit unmeasured.")]);
        }
        // Approval requests and policy events are event units; business measures use root work/batch identity.
        var normalized=rows.GroupBy(x=>x.Metric is "approval_wait" or "approval_turnaround"?$"{x.Metric}:approval:{x.ApprovalId}":
            x.Metric=="policy_exceptions"?$"{x.Metric}:event:{x.Evidence[0].Id}":x.Metric=="delivery_recorded"?$"{x.Metric}:draft:{x.Evidence[0].Id}":x.Key)
            .Select(g=>g.OrderByDescending(x=>x.RecordedUtc).First() with{Key=g.Key,Evidence=g.SelectMany(x=>x.Evidence).DistinctBy(x=>x.Id).OrderBy(x=>x.OccurredUtc).ThenBy(x=>x.Id).ToArray()}).ToArray();
        var workDenominator=companyWork.Select(Root).Where(x=>Matches(x.Task,query,agentMap)).Select(x=>$"{x.Kind}:{x.Id}").Distinct().Count()+
            initiatives.Count(x=>!x.TaskId.HasValue&&(query.AgentId is null||query.AgentId==x.OwnerAgentId)&&(query.TaskType is null||query.TaskType=="company.initiative")&&
                (query.Responsibility is null||query.Responsibility==CompanyWorkScope.Area(x.OwnerAgentId.HasValue?agentMap.GetValueOrDefault(x.OwnerAgentId.Value)?.Department:null)));
        var completedDenominator=companyWork.Where(x=>x.Status==WorkTaskStatus.Completed&&InPeriod(x.CompletedUtc)&&Matches(x,query,agentMap)).Select(x=>Root(x).Id).Distinct().Count();
        var reviewDenominator=reviews.Where(x=>InPeriod(x.CreatedUtc)).Select(x=>initiatives.Single(i=>i.Id==x.InitiativeId))
            .Where(x=>x.TaskId.HasValue&&taskMap.TryGetValue(x.TaskId.Value,out var task)?Matches(task,query,agentMap):
                (query.AgentId is null||query.AgentId==x.OwnerAgentId)&&(query.TaskType is null||query.TaskType=="company.initiative")&&
                (query.Responsibility is null||query.Responsibility==CompanyWorkScope.Area(x.OwnerAgentId.HasValue?agentMap.GetValueOrDefault(x.OwnerAgentId.Value)?.Department:null))).Select(x=>x.Id).Distinct().Count();
        var attemptWorkDenominator=attempts.Where(x=>taskMap.TryGetValue(x.TaskId!.Value,out var task)&&Matches(Root(task).Task,query,agentMap))
            .Where(x=>InPeriod(x.CreatedUtc)||InPeriod(x.CompletedUtc)).Select(x=>Root(taskMap[x.TaskId!.Value]).Id).Distinct().Count();
        var measures=Definitions.Select(def=>{
            var matching=normalized.Where(x=>x.Metric==def.Code).ToArray();
            var denominator=def.Code is "blocked_duration" or "budget_history"?0:def.Code=="prepared_output"?completedDenominator:def.Code is "reviewed_outcome" or "escalations"?reviewDenominator:def.Code=="execution_failure"?attemptWorkDenominator:
                def.Code is "approval_wait" or "approval_turnaround"?normalized.Count(x=>x.Metric=="approval_wait"):
                def.Code=="blocked_handoffs"?handoffDenominator:
                def.Code is "contributions" or "provider_confirmed" or "business_outcome" or "delivery_recorded" or "interventions"?matching.Length:
                def.Code=="policy_exceptions"?attempts.Count(x=>x.TaskId.HasValue&&taskMap.TryGetValue(x.TaskId.Value,out var t)&&Matches(Root(t).Task,query,agentMap)):workDenominator;
            var durations=matching.Where(x=>x.TotalSeconds.HasValue).Select(x=>x.TotalSeconds!.Value).ToArray();
            var restricted=(def.Code is "provider_confirmed" or "business_outcome")&&(!scope.Allows("finance")||query.AgentId.HasValue)||def.Code=="delivery_recorded"&&!scope.Allows("support");
            return new SupervisionMeasure(def.Code,def.View,def.Name,def.Code is "blocked_duration" or "budget_history"||restricted?null:matching.Length,denominator,def.Meaning,def.Rule,
                restricted?"Unavailable in this responsibility/agent scope; payment attribution is not recorded.":def.Code is "blocked_duration" or "budget_history"?def.Rule:partial?"Partial retained source window; counts reconcile with included permitted rows.":"Recorded source rows; complete historical instrumentation is not assumed.",
                matching.Any(x=>x.SecondsInPeriod.HasValue)?matching.Sum(x=>x.SecondsInPeriod??0):null,durations.Length>0?durations.Average():null);
        }).ToArray();
        var coverage=new[]{"Dates are inclusive company-local dates; event windows are half-open UTC. Current work status is observed now, not reconstructed at a past period end.",
            "Only retained events are measured. Pre-instrumentation transitions, missing terminal decision times and historical blocked duration are unavailable, not zero.",
            "Company outcomes deduplicate root work/initiative or payment batch; contributions and policy events are separate units. Technical retries never create extra company outcomes.",
            "Prepared output, recorded review, sent record, provider confirmation and recorded settlement are distinct. Productivity savings and agent-caused business benefits are unmeasured.",
            "Finance provider instructions lack durable agent attribution and appear only without an agent filter. Support sent records do not independently measure customer delivery or benefit.",
            "Approval time measures cover requests linked to included task work. Other native approval histories and unlinked policy events are outside this measured cohort. Company-work attribution uses the accountable root owner; contributions/handoffs use the recorded contributor/receiver.",
            partial?"A permitted source reached the 2000-row bound or an ancestry link was unavailable. All displayed totals and CSV cover only included rows; filters cannot recover a bounded-out source.":"Each included source is below the 2000-row bound. Absence of retained historical events is not proof of complete coverage."};
        var selected=normalized.Where(x=>Definitions.Any(d=>d.Code==x.Metric&&d.View==query.View)&&(query.Metric is null||x.Metric==query.Metric))
            .OrderByDescending(x=>x.RecordedUtc).ThenBy(x=>x.Key,StringComparer.Ordinal).ToArray();
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{query,observed,selected,measures}))));
        return new(query,observed,start,end,zone.Id,partial,coverage,scope.Areas.OrderBy(x=>x).ToArray(),allTypes,
            agents.Select(x=>new SupervisionAgent(x.Id,x.DisplayName,CompanyWorkScope.Area(x.Department))).ToArray(),measures,selected,hash);
    }
    private static bool Matches(WorkTask task,AgentSupervisionQuery q,IReadOnlyDictionary<Guid,Agent> agents)=>
        (q.AgentId is null||q.AgentId==task.AssignedAgentId)&&(q.TaskType is null||q.TaskType==task.Type)&&
        (q.Responsibility is null||q.Responsibility==CompanyWorkScope.Area(task.AssignedAgentId.HasValue?agents.GetValueOrDefault(task.AssignedAgentId.Value)?.Department:null,task.Type));
    private static decimal? Duration(DateTime from,DateTime to)=>to>=from?(decimal)(to-from).TotalSeconds:null;
    private static decimal? Intersection(DateTime from,DateTime to,DateTime start,DateTime until)=>Duration(from>start?from:start,to<until?to:until);
    public async Task<SupervisionCsv> ExportAsync(AgentSupervisionQuery query,CancellationToken ct)
    {
        var report=await GetAsync(query,ct);
        var csv=new StringBuilder();
        void Line(params object?[] values)=>csv.AppendLine(string.Join(",",values.Select(value=>{
            var text=Convert.ToString(value,CultureInfo.InvariantCulture)??"";
            var first=text.TrimStart();
            if(first.Length>0&&"=+-@".Contains(first[0])||text.Length>0&&"\t\r\n".Contains(text[0]))text="'"+text;
            return '"'+text.Replace("\"","\"\"")+'"';})));
        Line("observed_utc",report.ObservedUtc.ToString("O"),"timezone",report.Timezone,"from_utc",report.FromUtc.ToString("O"),"until_utc",report.UntilUtc.ToString("O"),"partial",report.Partial,"snapshot",report.SnapshotHash);
        Line("company_id",report.Query.CompanyId,"responsibility",report.Query.Responsibility,"task_type",report.Query.TaskType,"agent_id",report.Query.AgentId,"view",report.Query.View,"metric",report.Query.Metric);
        foreach(var measure in report.Measures.Where(x=>x.View==report.Query.View))Line("definition",measure.Code,"count",measure.Count,"denominator",measure.Denominator,measure.DenominatorMeaning,measure.Definition,measure.Coverage);
        Line("metric","work_kind","work_id","title","responsibility","task_type","agent","state","recorded_utc","evidence_stage","seconds_in_period","total_seconds","ongoing","work_route","approval_id","evidence");
        foreach(var row in report.Rows)Line(row.Metric,row.WorkKind,row.WorkId,row.Title,row.Responsibility,row.TaskType,row.AgentName,row.State,row.RecordedUtc.ToString("O"),row.EvidenceStage,row.SecondsInPeriod,row.TotalSeconds,row.Ongoing,row.WorkRoute,row.ApprovalId,string.Join(" | ",row.Evidence.Select(x=>$"{x.Source}:{x.Id:D}@{x.OccurredUtc:O}: {x.Description}")));
        return new($"agent-supervision-{report.Query.From:yyyyMMdd}-{report.Query.To:yyyyMMdd}.csv",csv.ToString(),report);
    }
}
