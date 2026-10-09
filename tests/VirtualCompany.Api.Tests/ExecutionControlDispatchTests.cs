using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
namespace VirtualCompany.Api.Tests;

public sealed class ExecutionControlDispatchTests
{
    [Theory][InlineData("allowed")][InlineData("revoked")][InlineData("expired")]
    public async Task Pause_prevents_real_dispatch_and_resume_rechecks_the_retained_task_policy(string currentPolicy)
    {
        using var f=new ExecutionControlNativeFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();await BusinessWorkEvidenceIntegrationTests.SeedOwner(f,company,owner);var p=await ExecutionControlFixture.SeedAsync(f,company,owner);
        using var client=BusinessWorkEvidenceIntegrationTests.Client(f);await Change(client,company,p.AgentId,true);
        await f.ExecuteScopeAsync(async scope=>{var result=await scope.ServiceProvider.GetRequiredService<IOperatingWorkDispatcher>().RunOnceAsync(10,default);Assert.Equal(0,result.Claimed);});
        await f.SeedAsync(async db=>{Assert.False(await db.ToolExecutionAttempts.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==company));Assert.Equal(OperatingDispatchStatus.Paused,(await db.OperatingDispatches.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company)).Status);
            if(currentPolicy=="revoked"){var policy=await db.TaskTypePolicies.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company&&x.AgentId==p.AgentId);var revision=new TaskTypePolicyRevision(policy,"disabled",1,DateTime.UtcNow.AddDays(1),owner,"Revoked while paused",new string('b',64),"{}",DateTime.UtcNow);policy.Activate(revision.Id);db.Add(revision);}
            if(currentPolicy=="expired"){var policy=await db.TaskTypePolicies.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company&&x.AgentId==p.AgentId);var revision=await db.TaskTypePolicyRevisions.IgnoreQueryFilters().SingleAsync(x=>x.Id==policy.ActiveRevisionId);db.Entry(revision).Property(x=>x.ExpiresUtc).CurrentValue=DateTime.UtcNow.AddSeconds(-1);}
        });
        await Change(client,company,p.AgentId,false);
        await f.ExecuteScopeAsync(async scope=>await scope.ServiceProvider.GetRequiredService<IOperatingWorkDispatcher>().RunOnceAsync(10,default));
        await f.SeedAsync(async db=>{
            var dispatch=await db.OperatingDispatches.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company);var task=await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==p.TaskId);var attempts=await db.ToolExecutionAttempts.IgnoreQueryFilters().Where(x=>x.CompanyId==company).ToListAsync();
            Assert.True(dispatch.Status==(currentPolicy!="allowed"?OperatingDispatchStatus.Blocked:OperatingDispatchStatus.Completed),$"{dispatch.Status}: {dispatch.FailureCode} {dispatch.FailureSummary}");
            Assert.Single(attempts);Assert.Equal(currentPolicy!="allowed"?ToolExecutionStatus.Denied:ToolExecutionStatus.Executed,attempts.Single().Status);
            if(currentPolicy=="allowed"){Assert.Equal(WorkTaskStatus.Completed,task.Status);Assert.NotEmpty(task.OutputPayload);Assert.Equal(1,(await db.TaskTypePolicies.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company&&x.AgentId==p.AgentId)).ActionsUsed);}
        });
    }
    private static async Task Change(HttpClient client,Guid company,Guid agent,bool pause)
    {
        var route=$"/api/companies/{company}/execution-controls";var before=(await client.GetFromJsonAsync<ExecutionControlView>(route+$"?agentId={agent}"))!;
        var command=new ExecutionControlChange(Guid.NewGuid(),agent,pause,before.Version,before.CompanyVersion,"Reviewed current work");using var response=await client.PostAsJsonAsync(route+"/preview",command);Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        var preview=(await response.Content.ReadFromJsonAsync<ExecutionControlPreview>())!;using var applied=await client.PostAsJsonAsync(route+"/apply",new ExecutionControlApply(command,preview.PreviewHash));Assert.True(applied.IsSuccessStatusCode,await applied.Content.ReadAsStringAsync());
    }
}
