using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
namespace VirtualCompany.Infrastructure.Companies;

public sealed class TaskTypePolicyEvaluator(VirtualCompanyDbContext db,TimeProvider clock):ITaskTypePolicyEvaluator
{
    public async Task<AuthorityCheckDto?> CheckAsync(Guid company,Guid agent,string tool,Guid? taskId,bool reserve,CancellationToken ct,Guid? executionId=null)
    {
        var entry=TaskTypePolicyCatalogue.All.SingleOrDefault(x=>!x.Finance && x.ToolName==tool);
        var task=taskId.HasValue?await db.WorkTasks.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company&&x.Id==taskId,ct):null;
        var taskEntry=task is null?null:TaskTypePolicyCatalogue.All.SingleOrDefault(x=>x.Code==task.Type&&!x.Finance);
        if(taskEntry is not null && taskEntry.ToolName!=tool) return Deny("task_policy_tool_mismatch","This task policy does not authorize the requested tool or customer delivery.");
        if(entry is null)return null;
        var policy=await db.TaskTypePolicies.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company&&x.AgentId==agent&&x.TaskType==entry.Code,ct);
        // Preserve the existing separately governed research path until a task policy is configured.
        if(policy is null) return tool=="sales.research_prospect"?null:Deny("task_policy_missing","Configure and review this task policy before execution.");
        var row=await db.TaskTypePolicyRevisions.IgnoreQueryFilters().AsNoTracking().SingleAsync(x=>x.CompanyId==company&&x.Id==policy.ActiveRevisionId,ct);
        var now=clock.GetUtcNow().UtcDateTime;
        if(row.Mode=="disabled")return Deny("task_policy_revoked","The task policy is disabled or revoked.");
        if(row.ExpiresUtc<=now)return Deny("task_policy_expired","The task policy has expired.");
        if(taskEntry is not null) {
            if(task!.AssignedAgentId!=agent || !task.InputPayload.TryGetValue("taskPolicyAgentId",out var assigned) || assigned?.ToString()!=agent.ToString("D"))return Deny("task_policy_delegation_denied","Delegation requires a separately reviewed policy and newly queued task for the receiving agent.");
            if(!task.InputPayload.TryGetValue("taskPolicyVersion",out var version)||version?.ToString()!=policy.Version.ToString()) return Deny("task_policy_changed","The policy changed after this task was queued. Review and queue new work.");
        } else if(tool!="sales.research_prospect")return Deny("task_policy_task_required","Use a retained task queued under the current policy.");
        var config=await db.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company,ct);
        if(config is null || config.EmergencyStopped || config.IsPaused) return Deny("company_operation_restricted","Company operation is unavailable, paused or stopped.");
        if(config.AutonomyLevel is CompanyAutonomyLevel.Recommend or CompanyAutonomyLevel.Organize)return new("Company policy","approval_required","internal_operation_not_enabled","Company operating intent requires human review before internal work.",true);
        if(config.MaximumToolCallsPerCycle<1 || config.MaximumModelCallsPerCycle<1)return Deny("company_budget_restricted","Company model or tool bounds do not allow this work.");
        var actorAgent=await db.Agents.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company&&x.Id==agent,ct);
        if(actorAgent is null || actorAgent.Status!=AgentStatus.Active)return Deny("task_agent_unavailable","The assigned agent is unavailable.");
        if(actorAgent.AutonomyLevel is AgentAutonomyLevel.Level0 or AgentAutonomyLevel.Level1)return new("Agent profile","approval_required","agent_autonomy_insufficient","The agent profile requires human review for internal task execution.",true);
        if(policy.UsageDayUtc==now.Date&&policy.ActionsUsed>=row.MaximumActionsPerDay)return Deny("task_policy_budget_exhausted","The task policy's daily action budget is exhausted.");
        if(row.Mode=="review") {
            var approved=reserve && executionId.HasValue && await db.ApprovalRequests.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==company&&x.ToolExecutionAttemptId==executionId&&x.Status==ApprovalRequestStatus.Approved,ct);
            if(!approved)return new("Task-type policy","approval_required","task_policy_review_required","An authorized human must review this internal action.",true);
        }
        if(reserve) {
            var tracked=db.TaskTypePolicies.Local.SingleOrDefault(x=>x.Id==policy.Id);
            if(tracked is not null) db.Entry(tracked).State=EntityState.Detached;
            db.Attach(policy);
            if(!policy.Reserve(now,row.MaximumActionsPerDay))return Deny("task_policy_budget_exhausted","The daily action budget is exhausted.");
            try{await db.SaveChangesAsync(ct);}catch(DbUpdateConcurrencyException){db.Entry(policy).State=EntityState.Detached;return Deny("task_policy_concurrent_change","Policy or budget changed before execution. Refresh and retry through the current workflow.");}
        }
        return new("Task-type policy","available","task_policy_allowed","This internal action fits the current task policy. Business grounding and owning workflow checks still apply.",false);
    }
    private static AuthorityCheckDto Deny(string reason,string explanation)=>new("Task-type policy","permission_denied",reason,explanation,false);
}
