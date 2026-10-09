using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class TaskTypePolicyService(VirtualCompanyDbContext db, CompanyWorkVisibility visibility,
    ICompanyMembershipContextResolver memberships, IAgentToolPolicyPreviewService tools,
    IFinanceAutonomyGrantService grants, IFinanceAutonomyWorkflowTemplateService templates,
    ICompanyTaskService tasks, IAuditEventWriter audit, TimeProvider clock,
    IOperatingPlanValidationService planValidation, ICompanyOperatingSnapshotService snapshots) : ITaskTypePolicyService
{
    private async Task<Guid> Require(Guid company,Guid agent,bool write,CancellationToken ct)
    {
        var member=await memberships.ResolveAsync(company,ct)??throw new UnauthorizedAccessException();
        if(write && member.MembershipRole is not(CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager)) throw new UnauthorizedAccessException();
        var scope=await visibility.ResolveAsync(company,ct);
        if(!await scope.Agents(db.Agents.IgnoreQueryFilters()).AnyAsync(x=>x.CompanyId==company && x.Id==agent,ct)) throw new KeyNotFoundException("Agent is unavailable in this access scope.");
        return member.UserId;
    }
    public async Task<TaskPolicyView> GetAsync(Guid company,Guid agent,string type,CancellationToken ct)
    {
        await Require(company,agent,false,ct);var entry=TaskTypePolicyCatalogue.Find(type);
        var names=await db.Users.Where(x=>db.CompanyMemberships.IgnoreQueryFilters().Any(m=>m.CompanyId==company&&m.UserId==x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.DisplayName,ct);
        if(entry.Finance) {
            var grant=(await grants.ListAsync(company,agent,ct)).SingleOrDefault(x=>x.CapabilityId==entry.CapabilityId);
            return new(company,agent,type,grant?.Version??0,grant?.Versions.OrderByDescending(x=>x.VersionNumber).Select(x=>new TaskPolicyHistory(x.VersionNumber,x.Status=="revoked"?"disabled":x.Level,x.MaximumRunsPerWindow,x.ActivatedUtc??x.CreatedUtc,x.ExpiresUtc??DateTime.MaxValue,x.CreatedByUserId,x.ReviewReason??"Prospective grant",x.AuthorityHash,names.GetValueOrDefault(x.CreatedByUserId,"Authorized policy manager"))).ToArray()??[],grant);
        }
        var policy=await db.TaskTypePolicies.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company && x.AgentId==agent && x.TaskType==type,ct);
        var rows=policy is null?[]:await db.TaskTypePolicyRevisions.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==company && x.PolicyId==policy.Id).OrderByDescending(x=>x.Version).ToListAsync(ct);
        return new(company,agent,type,policy?.Version??0,rows.Select(x=>new TaskPolicyHistory(x.Version,x.Mode,x.MaximumActionsPerDay,x.ActivatedUtc,x.ExpiresUtc,x.ActorId,x.Rationale,x.PreviewHash,names.GetValueOrDefault(x.ActorId,"Authorized policy manager"))).ToArray());
    }
    public async Task<TaskPolicyPreview> PreviewAsync(Guid company,TaskPolicyChange change,CancellationToken ct)
    {
        await Require(company,change.AgentId,true,ct);var entry=TaskTypePolicyCatalogue.Find(change.TaskType);var now=clock.GetUtcNow().UtcDateTime;
        if(!new[]{"disabled","review","automatic"}.Contains(change.Mode) || change.MaximumActionsPerDay is <1 or >100 || change.ExpiresUtc<=now || change.ExpiresUtc>now.AddDays(30) || string.IsNullOrWhiteSpace(change.Rationale) || change.Rationale.Length>2000) throw new ArgumentException("Choose a supported mode, 1–100 actions per day, an expiry within 30 days and a rationale.");
        var before=await GetAsync(company,change.AgentId,change.TaskType,ct);
        if(before.Version!=change.ExpectedVersion) throw new TaskPolicyConflictException("The policy changed. Refresh and preview the current version.");
        var config=await db.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company,ct);
        var preview=await tools.PreviewAsync(company,change.AgentId,ct);
        var check=preview.Checks.GetValueOrDefault(entry.ToolName)??new("Capability catalogue","not_implemented","unsupported_task_tool","No supported tool is available for this task type.",false);
        FinanceAutonomyGrantDefinition? definition=null;
        if(entry.Finance) {
            var finance=await templates.PreviewAsync(company,new(FinanceAutonomyWorkflowTemplateCodes.StaleCashEvidence,change.AgentId,config?.Timezone??"UTC"),ct);
            if(change.MaximumActionsPerDay>finance.Template.Limits.MaximumRunsPerWindow)throw new ArgumentException($"The Finance template permits at most {finance.Template.Limits.MaximumRunsPerWindow} run(s) per window.");
            definition=finance.ProspectiveGrant with {ExpiresUtc=change.ExpiresUtc,MaximumRunsPerWindow=Math.Min(change.MaximumActionsPerDay,finance.Template.Limits.MaximumRunsPerWindow)};
            if(change.Mode=="review") check=new("Finance template","not_implemented","finance_template_review_unavailable","This reviewed template supports bounded monitoring and internal review tasks. Its generic review mode is unavailable; use the owning Finance workflow approvals.",false);
            else if(!finance.IsReady) check=new("Finance template","configuration_required","finance_template_restricted",string.Join(" ",finance.BlockingReasons),false);
        }
        if(change.SimulationRecordId.HasValue && !entry.Finance) await RequireRecord(company,entry,change.SimulationRecordId.Value,ct);
        var department=await db.Agents.IgnoreQueryFilters().Where(x=>x.CompanyId==company&&x.Id==change.AgentId).Select(x=>x.Department).SingleAsync(ct);
        if(!department.Equals(entry.Department,StringComparison.OrdinalIgnoreCase))check=new("Agent responsibility","permission_denied","task_department_mismatch","Choose an agent configured for this responsibility.",false);
        var ready=change.Mode=="disabled" || check.State is "available" or "approval_required";
        if(change.Mode!="disabled" && config is null){ready=false;check=new("Company policy","configuration_required","company_policy_missing","Configure company operating limits first.",false);}
        var simulation=change.SimulationRecordId.HasValue?"The referenced company record exists. Capability checks run without generating, delivering, queuing or reserving budgets. Actual grounding, target version and business payload are checked during execution.":"No business record was supplied. This is a no-effect capability preview; task evidence and payload checks remain for execution.";
        var hash=Hash(JsonSerializer.Serialize(new{company,change,before,authority=preview.Authority.AuthorityHash,companyVersion=config?.Version,check,definition}));
        var afterVersion=entry.Finance?change.Mode=="disabled"?before.Version+(before.FinanceGrant is null?0:1):before.Version+(before.FinanceGrant is null?3:2):before.Version+1;
        IReadOnlyList<AuthorityLimitDto> limits=definition is null?[new("Records per task","1"),new("Actions per task","1"),new("Actions per UTC day",change.MaximumActionsPerDay.ToString())]:
            [new("Records per run",definition.MaximumRecordsPerRun.ToString()),new("Actions per run",definition.MaximumActionsPerRun.ToString()),new("Runs per window",definition.MaximumRunsPerWindow.ToString()),new("Local time window",$"{definition.WindowStartLocal}–{definition.WindowEndLocal} ({definition.Timezone})"),new("Evidence freshness (minutes)",definition.EvidenceFreshnessMinutes.ToString())];
        return new(change,entry,before,afterVersion,check,ready,hash,preview.Authority.AuthorityHash,config?.Version??0,simulation,definition,limits);
    }
    public async Task<TaskPolicyView> ApplyAsync(Guid company,TaskPolicyApply apply,CancellationToken ct)
    {
        var actor=await Require(company,apply.Change.AgentId,true,ct);var preview=await PreviewAsync(company,apply.Change,ct);
        if(preview.PreviewHash!=apply.PreviewHash) throw new TaskPolicyConflictException("The preview inputs, authority or current policy changed. Preview again before applying.");
        if(!preview.CanApply) throw new ArgumentException(preview.Check.Explanation);
        if(preview.Type.Finance) {
            var grant=preview.Before.FinanceGrant;
            if(apply.Change.Mode=="disabled") {if(grant is not null) await grants.RevokeAsync(company,grant.Id,new(grant.Version,apply.Change.Rationale),ct);}
            else {
                grant=grant is null?await grants.CreateAsync(company,new(preview.FinanceDefinition!,apply.Change.Rationale),ct):await grants.CreateVersionAsync(company,grant.Id,new(preview.FinanceDefinition!,grant.Version,apply.Change.Rationale),ct);
                var latest=grant.Versions.MaxBy(x=>x.VersionNumber)!;
                await grants.ActivateAsync(company,grant.Id,latest.Id,new(grant.Version,apply.Change.Rationale),ct);
            }
        } else {
            var policy=await db.TaskTypePolicies.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.CompanyId==company && x.AgentId==apply.Change.AgentId && x.TaskType==apply.Change.TaskType,ct);
            if((policy?.Version??0)!=apply.Change.ExpectedVersion) throw new TaskPolicyConflictException("A newer policy is already active.");
            policy??=new(company,apply.Change.AgentId,apply.Change.TaskType);
            if(db.Entry(policy).State==EntityState.Detached) db.Add(policy);
            var row=new TaskTypePolicyRevision(policy,apply.Change.Mode,apply.Change.MaximumActionsPerDay,apply.Change.ExpiresUtc,actor,apply.Change.Rationale,apply.PreviewHash,JsonSerializer.Serialize(preview.Change),clock.GetUtcNow().UtcDateTime);
            db.Add(row);policy.Activate(row.Id);
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateException ex){throw new TaskPolicyConflictException("Another policy or budget update won. Refresh and preview again.",ex);}
            await audit.WriteAsync(new(company,"user",actor,"task_policy.applied","task_type_policy",policy.Id.ToString("N"),"succeeded",RationaleSummary:apply.Change.Rationale,Metadata:new Dictionary<string,string?>{{"version",policy.Version.ToString()},{"previewHash",apply.PreviewHash},{"taskType",policy.TaskType},{"mode",row.Mode}}),ct);
        }
        if(preview.Type.Finance)await audit.WriteAsync(new(company,"user",actor,"task_policy.finance_applied","finance_autonomy_grant",preview.Type.CapabilityId,"succeeded",RationaleSummary:apply.Change.Rationale,
            Metadata:new Dictionary<string,string?>{{"previewHash",preview.PreviewHash},{"previewInputs",JsonSerializer.Serialize(apply.Change)},{"beforeVersion",preview.Before.Version.ToString()},{"afterVersion",preview.AfterVersion.ToString()},{"authorityHash",preview.AuthorityHash}}),ct);
        return await GetAsync(company,apply.Change.AgentId,apply.Change.TaskType,ct);
    }
    public async Task<Guid> QueueAsync(Guid company,QueueTaskPolicyWork command,CancellationToken ct)
    {
        var actor=await Require(company,command.AgentId,true,ct);var entry=TaskTypePolicyCatalogue.Find(command.TaskType);
        if(entry.Finance) throw new ArgumentException("Use the existing Finance template trigger and run path for monitoring.");
        var view=await GetAsync(company,command.AgentId,command.TaskType,ct);var current=view.History.FirstOrDefault();
        if(view.Version!=command.ExpectedVersion || current is null || current.Mode=="disabled" || current.ExpiresUtc<=clock.GetUtcNow().UtcDateTime) throw new TaskPolicyConflictException("A current enabled policy is required before queuing work.");
        await RequireRecord(company,entry,command.RecordId,ct);
        var config=await db.CompanyOperatingConfigurations.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.CompanyId==company,ct)
            ??throw new ArgumentException("Configure company operating limits first.");
        var goal=await db.CompanyGoals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x=>x.CompanyId==company&&x.Status==CompanyGoalStatus.Active&&
            (command.GoalId.HasValue?x.Id==command.GoalId:x.OwnerAgentId==command.AgentId),ct)??throw new ArgumentException("Choose an active company goal for this agent before queuing work.");
        await using var transaction=await db.Database.BeginTransactionAsync(ct);
        var payload=new Dictionary<string,JsonNode?> { ["requestedDomain"]=JsonValue.Create(entry.Department.ToLowerInvariant()),[entry.RecordKey]=JsonValue.Create(command.RecordId),["taskPolicyVersion"]=JsonValue.Create(view.Version),["taskPolicyAgentId"]=JsonValue.Create(command.AgentId),["toolInvocations"]=new JsonArray(new JsonObject{["toolName"]=entry.ToolName,["actionType"]=entry.ActionType,["scope"]=entry.Department.ToLowerInvariant(),["requestPayload"]=new JsonObject{[entry.RecordKey]=command.RecordId.ToString("D")}})};
        var task=await tasks.CreateTaskAsync(company,new(entry.Code,entry.Name,"Produce a retained internal draft or research result. Customer delivery remains separately approved.","normal",null,command.AgentId,payload,CorrelationId:$"task-policy:{Guid.NewGuid():N}"),ct);
        var correlation=$"task-policy:{task.Id:N}";
        var cycle=new OperatingCycle(Guid.NewGuid(),company,"task_policy",task.Id.ToString("N"),config.CoordinatorAgentId??command.AgentId,correlation,correlation,config.Version);
        var plan=new OperatingPlan(Guid.NewGuid(),company,cycle.Id,1,entry.Name,"Bounded work queued under an explicitly reviewed task policy.");plan.SubmitForReview();
        var initiative=new OperatingInitiative(Guid.NewGuid(),company,plan.Id,goal.Id,entry.Name,"Retain a grounded internal draft or research result for review.",CompanyGoalPriority.Normal,
            "Owning module output and task result retained; no customer delivery.",command.AgentId,clock.GetUtcNow().UtcDateTime.AddDays(1),null);initiative.Approve();initiative.LinkWork(task.Id,null);
        var decision=new OperatingDecision(Guid.NewGuid(),company,plan.Id,initiative.Id,OperatingActionClass.Recommend,"initiative","task",task.Id.ToString("N"),command.AgentId,
            "Only the explicitly catalogued internal action is requested.",1,"low",false,correlation);
        db.AddRange(cycle,plan,initiative,decision);await db.SaveChangesAsync(ct);
        cycle.MarkObserving();await db.SaveChangesAsync(ct);
        var snapshot=await snapshots.CaptureAsync(company,cycle.Id,ct);cycle.MarkPlanning(snapshot.Id);cycle.MarkValidating();
        var validation=await planValidation.ValidateAsync(company,plan.Id,ct);
        if(validation.Any(x=>x.Outcome=="denied"))throw new ArgumentException(string.Join(" ",validation.Where(x=>x.Outcome=="denied").Select(x=>x.Explanation)));
        plan.Approve();plan.BeginCommit();plan.MarkCommitted();cycle.MarkAwaitingReview();cycle.RecordUsage(0,0,1,0);cycle.Complete();
        db.Add(new OperatingDispatch(Guid.NewGuid(),company,initiative.Id,task.Id,OperatingDispatchKind.SingleAgent,correlation));
        await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
        return task.Id;
    }
    private async Task RequireRecord(Guid company,TaskTypePolicyCatalogueEntry entry,Guid record,CancellationToken ct)
    {
        var found=entry.RecordKey switch {
            "prospectId"=>await db.ProspectAccounts.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==company&&x.Id==record,ct),
            "dealId"=>await db.Deals.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==company&&x.Id==record,ct),
            "briefId"=>await db.MarketingContentBriefs.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==company&&x.Id==record,ct),
            "caseId"=>await db.SupportCases.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==company&&x.Id==record,ct),_=>false};
        if(!found)throw new KeyNotFoundException("The simulation or work record is unavailable in this company.");
    }
    public async Task<TaskPolicyQueueContext> WorkChoicesAsync(Guid company,Guid agent,string type,CancellationToken ct)
    {
        await Require(company,agent,true,ct);var entry=TaskTypePolicyCatalogue.Find(type);
        var department=await db.Agents.IgnoreQueryFilters().Where(x=>x.CompanyId==company&&x.Id==agent).Select(x=>x.Department).SingleAsync(ct);
        if(!department.Equals(entry.Department,StringComparison.OrdinalIgnoreCase))return new([],[]);
        var records=entry.RecordKey switch {
            "prospectId"=>await db.ProspectAccounts.IgnoreQueryFilters().Where(x=>x.CompanyId==company).OrderBy(x=>x.Name).Take(100).Select(x=>new TaskPolicyWorkChoice(x.Id,x.Name)).ToListAsync(ct),
            "dealId"=>await db.Deals.IgnoreQueryFilters().Where(x=>x.CompanyId==company).OrderBy(x=>x.Title).Take(100).Select(x=>new TaskPolicyWorkChoice(x.Id,x.Title)).ToListAsync(ct),
            "briefId"=>await db.MarketingContentBriefs.IgnoreQueryFilters().Where(x=>x.CompanyId==company).OrderBy(x=>x.Title).Take(100).Select(x=>new TaskPolicyWorkChoice(x.Id,x.Title)).ToListAsync(ct),
            "caseId"=>await db.SupportCases.IgnoreQueryFilters().Where(x=>x.CompanyId==company).OrderBy(x=>x.Subject).Take(100).Select(x=>new TaskPolicyWorkChoice(x.Id,x.Subject)).ToListAsync(ct),_=>new List<TaskPolicyWorkChoice>()};
        var goals=await db.CompanyGoals.IgnoreQueryFilters().Where(x=>x.CompanyId==company&&x.Status==CompanyGoalStatus.Active&&x.OwnerAgentId==agent).OrderBy(x=>x.Name).Take(100).Select(x=>new TaskPolicyWorkChoice(x.Id,x.Name)).ToListAsync(ct);
        return new(records,goals);
    }
    internal static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
public sealed class TaskPolicyConflictException(string message,Exception? inner=null):Exception(message,inner);
