using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

// Provider-free retained facts. Times are explicit so clipping and missing instrumentation are testable.
public sealed record AgentSupervisionFixture(Guid Company, Guid Owner, Guid Agent, Guid Task, Guid Initiative,
    Guid UnstartedInitiative, Guid OldTask, Guid ResolvedApproval, Guid PendingApproval, Guid UnknownApproval)
{
    public static async Task<AgentSupervisionFixture> Seed(TestWebApplicationFactory factory)
    {
        var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await factory.SeedAsync(db=>{
            var tenant=new Company(company,"Supervision fixture");db.Add(tenant);
            db.Entry(tenant).Property(x=>x.Timezone).CurrentValue="Europe/Stockholm";
            var membership=new CompanyMembership(Guid.NewGuid(),company,owner,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active);
            db.AddRange(new User(owner,"p17-owner@example.com","Supervision owner","dev-header","p17-owner"),membership,
                new CompanyResponsibilityAssignment(Guid.NewGuid(),company,ResponsibilityArea.CompanyPerformance,ResponsibilityAssignmentKind.ExecutiveOversight,membership.Id,null,AgentAutonomyLevel.Level1,null,null));
            return System.Threading.Tasks.Task.CompletedTask;
        });
        var basis=await AuthorityExplanationFixture.SeedAsync(factory,company,owner);
        AgentSupervisionFixture result=null!;
        await factory.SeedAsync(async db=>{
            var parent=await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==basis.TaskId);
            parent.UpdateStatus(WorkTaskStatus.Completed);parent.OutputPayload["summary"]=JsonValue.Create("Retained prepared review");
            db.Entry(parent).Property(x=>x.CreatedUtc).CurrentValue=Utc(28,10);
            db.Entry(parent).Property(x=>x.CompletedUtc).CurrentValue=Utc(31,10);
            var initiative=await db.OperatingInitiatives.IgnoreQueryFilters().SingleAsync(x=>x.Id==basis.InitiativeId);
            initiative.Complete();
            db.Entry(initiative).Property(x=>x.CreatedUtc).CurrentValue=Utc(28,10);
            var goal=await db.CompanyGoals.IgnoreQueryFilters().SingleAsync(x=>x.Id==initiative.GoalId);
            var unstarted=new OperatingInitiative(Guid.NewGuid(),company,basis.PlanId,goal.Id,"Unstarted evidence review","Retained initiative without a task.",CompanyGoalPriority.Normal,"Review receipt",basis.AgentId,null,null);
            db.Add(unstarted);db.Entry(unstarted).Property(x=>x.CreatedUtc).CurrentValue=Utc(28,10);
            for(var version=1;version<=2;version++){
                var review=new OperatingReview(Guid.NewGuid(),company,basis.PlanId,1,initiative.Id,OperatingReviewOutcome.CloseSuccessful,"Reviewed", "Source review", "Recorded evidence", "Close", $"v{version}",.9m);
                db.Add(review);db.Entry(review).Property(x=>x.CreatedUtc).CurrentValue=Utc(31,11+version);
            }
            var child=new WorkTask(Guid.NewGuid(),company,"finance_review","Contributor review","Contribution",WorkTaskPriority.Normal,basis.AgentId,parent.Id,"user",owner);
            child.UpdateStatus(WorkTaskStatus.Completed);db.Add(child);db.Entry(child).Property(x=>x.CreatedUtc).CurrentValue=Utc(28,12);
            for(var sequence=1;sequence<=2;sequence++)for(var version=1;version<=2;version++){
                var contribution=new CollaborationContribution(company,parent.Id,child.Id,basis.PlanId,basis.AgentId,sequence,version,
                    OperatingCollaborationRole.Contributor,OperatingCollaborationPattern.Parallel,$"Stream {sequence}","completed","Output", "Retained evidence");
                db.Add(contribution);db.Entry(contribution).Property(x=>x.CreatedUtc).CurrentValue=Utc(31,version+sequence);
            }
            var old=new WorkTask(Guid.NewGuid(),company,"finance_review","=Old completed review","New failure on older work",WorkTaskPriority.Normal,basis.AgentId,null,"user",owner);
            old.UpdateStatus(WorkTaskStatus.Completed);db.Add(old);db.Entry(old).Property(x=>x.CreatedUtc).CurrentValue=Utc(28,10);db.Entry(old).Property(x=>x.CompletedUtc).CurrentValue=Utc(29,10);
            for(var retry=0;retry<2;retry++){
                var attempt=new ToolExecutionAttempt(Guid.NewGuid(),company,basis.AgentId,"get_cash_balance",ToolActionType.Read,"finance",taskId:old.Id,startedAtUtc:Utc(31,8));
                attempt.MarkFailed(null,null,Utc(31,9));db.Add(attempt);
                db.Add(new AuditEvent(Guid.NewGuid(),company,"agent",basis.AgentId,"agent.tool_execution.denied","agent_tool_execution",attempt.Id.ToString("D"),"denied",occurredUtc:Utc(31,9)));
            }
            ApprovalRequest Request()=>ApprovalRequest.CreateForTarget(Guid.NewGuid(),company,ApprovalTargetEntityType.Task,parent.Id,"user",owner,"review",new Dictionary<string,JsonNode?>(){["reason"]=JsonValue.Create("Review retained facts")},null,owner,[]);
            var resolved=Request();resolved.ApproveCurrentStep(resolved.CurrentActionableStep!.Id,owner,"Reviewed");
            var pending=Request();var unknown=Request();unknown.ApproveCurrentStep(unknown.CurrentActionableStep!.Id,owner,"Legacy missing time");
            db.AddRange(resolved,pending,unknown);
            foreach(var approval in new[]{resolved,pending,unknown})db.Entry(approval).Property(x=>x.CreatedUtc).CurrentValue=Utc(30,22);
            db.Entry(resolved).Property(x=>x.DecidedUtc).CurrentValue=Utc(31,1);
            db.Entry(unknown).Property(x=>x.DecidedUtc).CurrentValue=null;
            result=new(company,owner,basis.AgentId,parent.Id,initiative.Id,unstarted.Id,old.Id,resolved.Id,pending.Id,unknown.Id);
        });
        return result;
    }
    public static DateTime Utc(int day,int hour)=>new(2030,3,day,hour,0,0,DateTimeKind.Utc);
    public static HttpClient Client(TestWebApplicationFactory factory,string subject="p17-owner"){
        var client=factory.CreateClient();client.DefaultRequestHeaders.Add("X-Dev-Auth-Subject",subject);client.DefaultRequestHeaders.Add("X-Dev-Auth-Email",subject+"@example.com");return client;
    }
    public sealed class Clock:TimeProvider{
        public DateTime Now{get;set;}=new(2030,4,1,12,0,0,DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow()=>new(Now);
    }
}
