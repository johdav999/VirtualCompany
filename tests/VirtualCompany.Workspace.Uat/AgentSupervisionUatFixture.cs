using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Api.Tests;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

public sealed record AgentSupervisionUatFixture(Guid Company,Guid Agent,Guid Task,Guid Initiative,Guid Approval,Guid Batch)
{
    public static async Task<AgentSupervisionUatFixture> Seed(TestWebApplicationFactory factory,Guid owner)
    {
        var company=Guid.Parse("17171717-1717-1717-1717-171717171717");
        await factory.SeedAsync(db=>{var tenant=new Company(company,"P17 supervision company");tenant.UpdateWorkspaceProfile(tenant.Name,null,null,"Europe/Stockholm","SEK","en-GB","SE");tenant.CompleteOnboarding(1,null,"{}");db.AddRange(tenant,new CompanyMembership(Guid.NewGuid(),company,owner,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));return System.Threading.Tasks.Task.CompletedTask;});
        var basis=await AuthorityExplanationFixture.SeedAsync(factory,company,owner);var batchId=Guid.NewGuid();Guid approvalId=default;
        await factory.SeedAsync(async db=>{
            var now=DateTime.UtcNow;var task=await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==basis.TaskId);task.UpdateStatus(WorkTaskStatus.AwaitingApproval);db.Entry(task).Property(x=>x.CreatedUtc).CurrentValue=now.AddDays(-2);
            var approval=ApprovalRequest.CreateForTarget(Guid.NewGuid(),company,ApprovalTargetEntityType.Task,task.Id,"user",owner,"p17_review",new Dictionary<string,JsonNode?>{["reason"]=JsonValue.Create("Review the retained work evidence.")},null,owner,[]);approvalId=approval.Id;db.Add(approval);db.Entry(approval).Property(x=>x.CreatedUtc).CurrentValue=now.AddDays(-1);
            var failed=new ToolExecutionAttempt(Guid.NewGuid(),company,basis.AgentId,"get_cash_balance",ToolActionType.Read,"finance",taskId:task.Id,startedAtUtc:now.AddHours(-2));failed.MarkFailed(null,null,now.AddHours(-1));db.Add(failed);
            db.Add(new AuditEvent(Guid.NewGuid(),company,"agent",basis.AgentId,"agent.tool_execution.denied","agent_tool_execution",failed.Id.ToString("D"),"denied",occurredUtc:now.AddHours(-1)));
            var child=new WorkTask(Guid.NewGuid(),company,"finance_review","P17 retained contributor review","Retained review",WorkTaskPriority.Normal,basis.AgentId,task.Id,"user",owner);db.Add(child);db.Entry(child).Property(x=>x.CreatedUtc).CurrentValue=now.AddDays(-1);
            for(var version=1;version<=2;version++){var contribution=new CollaborationContribution(company,task.Id,child.Id,basis.PlanId,basis.AgentId,1,version,OperatingCollaborationRole.Contributor,OperatingCollaborationPattern.Parallel,"P17 review contribution","completed","Retained evidence","Recorded review");db.Add(contribution);db.Entry(contribution).Property(x=>x.CreatedUtc).CurrentValue=now.AddHours(-3+version);}
            var hash=new string('a',64);var account=new FinanceAccount(Guid.NewGuid(),company,"1930","P17 synthetic cash","asset","SEK",0,now);var bank=new CompanyBankAccount(Guid.NewGuid(),company,account.Id,"P17 synthetic bank account","Fixture bank","•••• 1717","SEK");var connection=new BankConnection(Guid.NewGuid(),company,"fixture","p17-bank","Provider-free bank",owner,now);
            var batch=new PaymentBatch(batchId,company,"P17-RECORDED","P17 synthetic completed batch",DateOnly.FromDateTime(now),"p17-create",hash,owner,now);
            var batchApproval=ApprovalRequest.CreateForTarget(Guid.NewGuid(),company,ApprovalTargetEntityType.PaymentBatch,batchId,"user",owner,"payment_batch",new Dictionary<string,JsonNode?>{["reason"]=JsonValue.Create("Synthetic retained records; no provider request.")},null,owner,[]);
            var binding=new PaymentBatchApprovalBinding(Guid.NewGuid(),company,batchId,batchApproval.Id,1,hash,owner,now);
            var execution=new PaymentBatchExecution(Guid.NewGuid(),company,batchId,1,binding.Id,connection.Id,bank.Id,"fixture",hash,"p17-execution",owner,null,now.AddMinutes(-30));
            execution.RecordSubmission("p17-synthetic-payment",null,"COMPLETED",true,false,false,now.AddMinutes(-15));execution.MarkSettled(now.AddMinutes(-10));db.AddRange(account,bank,connection,batch,batchApproval,binding,execution);
        });
        return new(company,basis.AgentId,basis.TaskId,basis.InitiativeId,approvalId,batchId);
    }
}
