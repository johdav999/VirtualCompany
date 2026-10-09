using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
namespace VirtualCompany.Api.Tests;

public sealed class ExecutionControlIntegrationTests
{
    [Fact]
    public async Task Uncertain_tool_step_recovers_through_its_actual_visible_task()
    {
        using var f=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(f,company,owner);var p=await AuthorityExplanationFixture.SeedAsync(f,company,owner);var attempt=Guid.NewGuid();
        await f.SeedAsync(db=>{db.Add(new ToolExecutionAttempt(attempt,company,p.AgentId,"tasks.list",ToolActionType.Read,"tasks",taskId:p.TaskId));return Task.CompletedTask;});
        await f.ExecuteScopeAsync(async scope=>{var gate=scope.ServiceProvider.GetRequiredService<IAgentExecutionControlGate>();var admission=await gate.AdmitAsync(company,p.AgentId,"tool",attempt.ToString("D"),default);await gate.AcknowledgeAsync(company,admission,false,default);});
        using var c=BusinessWorkEvidenceIntegrationTests.Client(f);var view=(await c.GetFromJsonAsync<ExecutionControlView>(Route(company)))!;
        Assert.Contains(view.Work,x=>x.Id==p.TaskId&&x.Kind=="task"&&x.State=="Uncertain");
    }
    [Fact]
    public async Task Scoped_pause_follows_persisted_delegation_and_missing_ancestry_fails_closed()
    {
        using var f=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(f,company,owner);var p=await AuthorityExplanationFixture.SeedAsync(f,company,owner);
        var childId=Guid.NewGuid();
        await f.SeedAsync(db=>{
            db.Add(new WorkTask(childId,company,"delegated","Delegated retained work",null,WorkTaskPriority.Normal,null,p.TaskId,"user",owner));
            var control=new AgentExecutionControl(company,p.AgentId);control.Change(true,DateTime.UtcNow);db.Add(control);return Task.CompletedTask;
        });
        await f.ExecuteScopeAsync(async scope=>{
            var gate=scope.ServiceProvider.GetRequiredService<IAgentExecutionControlGate>();
            Assert.True(await gate.IsPausedAsync(company,null,default,childId));
            await Assert.ThrowsAsync<ExecutionPausedException>(()=>gate.AdmitAsync(company,null,"tool","delegated",default,childId));
            Assert.True(await gate.IsPausedAsync(company,null,default,Guid.NewGuid()));
            Assert.False(await gate.IsPausedAsync(company,null,default));
        });
        await f.SeedAsync(async db=>Assert.Empty(await db.AgentExecutionAdmissions.IgnoreQueryFilters().Where(x=>x.CompanyId==company).ToListAsync()));
    }
    [Fact]
    public async Task Preview_has_no_effect_apply_is_idempotent_and_resume_preserves_uncertainty()
    {
        using var f=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(f,company,owner);var p=await AuthorityExplanationFixture.SeedAsync(f,company,owner);
        var dispatchId=Guid.NewGuid(); await f.SeedAsync(db=>{db.Add(new OperatingDispatch(dispatchId,company,p.InitiativeId,p.TaskId,OperatingDispatchKind.SingleAgent,"p16"));return Task.CompletedTask;});
        using var c=BusinessWorkEvidenceIntegrationTests.Client(f);var before=(await c.GetFromJsonAsync<ExecutionControlView>(Route(company)))!;
        Assert.True(before.CanManage);Assert.Single(before.Work);Assert.Equal("Queued",before.Work.Single().State);
        var change=new ExecutionControlChange(Guid.NewGuid(),null,true,before.Version,before.CompanyVersion,"Review current work");
        var preview=await Preview(c,company,change);
        await f.SeedAsync(async db=>{Assert.Empty(await db.AgentExecutionControlCommands.IgnoreQueryFilters().ToListAsync());Assert.Equal(OperatingDispatchStatus.Pending,(await db.OperatingDispatches.IgnoreQueryFilters().SingleAsync()).Status);});
        var apply=new ExecutionControlApply(change,preview.PreviewHash);var paused=await Apply(c,company,apply);
        Assert.True(paused.CompanyPaused);Assert.Equal("Paused before start",paused.Work.Single().State);Assert.Single(paused.History);
        var duplicate=await Apply(c,company,apply);Assert.Equal(paused.Version,duplicate.Version);Assert.Single(duplicate.History);
        using var stale=await c.PostAsJsonAsync(Route(company)+"/apply",apply with{Change=change with{CommandId=Guid.NewGuid()}});Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        var resume=new ExecutionControlChange(Guid.NewGuid(),null,false,paused.Version,paused.CompanyVersion,"Reviewed current constraints");
        var resumed=await Apply(c,company,new(resume,(await Preview(c,company,resume)).PreviewHash));Assert.False(resumed.CompanyPaused);Assert.Equal("Queued",resumed.Work.Single().State);
        await f.SeedAsync(async db=>{var row=await db.OperatingDispatches.IgnoreQueryFilters().SingleAsync();row.TryClaim("interrupted",DateTime.UtcNow,TimeSpan.FromSeconds(1));row.Start("interrupted",DateTime.UtcNow);row.MarkUncertain("Provider acknowledgement missing",DateTime.UtcNow);});
        var final=(await c.GetFromJsonAsync<ExecutionControlView>(Route(company)))!;Assert.Equal("Uncertain",final.Work.Single().State);Assert.Equal(2,final.History.Count);
    }
    [Fact]
    public async Task Scoped_pause_blocks_exact_agent_and_global_pause_overrides_scoped_resume()
    {
        using var f=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();await BusinessWorkEvidenceIntegrationTests.SeedOwner(f,company,owner);var p=await AuthorityExplanationFixture.SeedAsync(f,company,owner);
        using var c=BusinessWorkEvidenceIntegrationTests.Client(f);var before=(await c.GetFromJsonAsync<ExecutionControlView>(Route(company)+$"?agentId={p.AgentId}"))!;
        var change=new ExecutionControlChange(Guid.NewGuid(),p.AgentId,true,before.Version,before.CompanyVersion,"Stop one agent");var paused=await Apply(c,company,new(change,(await Preview(c,company,change)).PreviewHash));
        await f.ExecuteScopeAsync(async scope=>{var gate=scope.ServiceProvider.GetRequiredService<IAgentExecutionControlGate>();Assert.True(await gate.IsPausedAsync(company,p.AgentId,default));Assert.False(await gate.IsPausedAsync(company,Guid.NewGuid(),default));await Assert.ThrowsAsync<ExecutionPausedException>(()=>gate.AdmitAsync(company,p.AgentId,"tool","paused-step",default));});
        var global=(await c.GetFromJsonAsync<ExecutionControlView>(Route(company)))!;var all=new ExecutionControlChange(Guid.NewGuid(),null,true,global.Version,global.CompanyVersion,"Company review");await Apply(c,company,new(all,(await Preview(c,company,all)).PreviewHash));
        var current=(await c.GetFromJsonAsync<ExecutionControlView>(Route(company)+$"?agentId={p.AgentId}"))!;var resume=change with{CommandId=Guid.NewGuid(),Pause=false,ExpectedVersion=current.Version,ExpectedCompanyVersion=current.CompanyVersion};
        var resumed=await Apply(c,company,new(resume,(await Preview(c,company,resume)).PreviewHash));Assert.False(resumed.Paused);Assert.True(resumed.CompanyPaused);
        await f.ExecuteScopeAsync(async scope=>Assert.True(await scope.ServiceProvider.GetRequiredService<IAgentExecutionControlGate>().IsPausedAsync(company,p.AgentId,default)));
    }
    [Fact]
    public async Task Admitted_step_remains_in_flight_pause_does_not_erase_it_or_allow_duplicate_retry()
    {
        using var f=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();await BusinessWorkEvidenceIntegrationTests.SeedOwner(f,company,owner);var p=await AuthorityExplanationFixture.SeedAsync(f,company,owner);Guid admission=Guid.Empty;
        await f.ExecuteScopeAsync(async scope=>{var gate=scope.ServiceProvider.GetRequiredService<IAgentExecutionControlGate>();admission=await gate.AdmitAsync(company,p.AgentId,"tool","controlled-original",default);await Assert.ThrowsAsync<ExecutionControlConflictException>(()=>gate.AdmitAsync(company,p.AgentId,"tool","controlled-original",default));});
        using var c=BusinessWorkEvidenceIntegrationTests.Client(f);var before=(await c.GetFromJsonAsync<ExecutionControlView>(Route(company)))!;var change=new ExecutionControlChange(Guid.NewGuid(),null,true,before.Version,before.CompanyVersion,"Pause between steps");var after=await Apply(c,company,new(change,(await Preview(c,company,change)).PreviewHash));Assert.Contains(after.Work,x=>x.Id==admission&&x.State=="In flight");
        await f.ExecuteScopeAsync(async scope=>await scope.ServiceProvider.GetRequiredService<IAgentExecutionControlGate>().AcknowledgeAsync(company,admission,false,default));
        var current=(await c.GetFromJsonAsync<ExecutionControlView>(Route(company)))!;Assert.Contains(current.Work,x=>x.Id==admission&&x.State=="Uncertain");
        var resume=change with{CommandId=Guid.NewGuid(),Pause=false,ExpectedVersion=current.Version,ExpectedCompanyVersion=current.CompanyVersion};await Apply(c,company,new(resume,(await Preview(c,company,resume)).PreviewHash));
        await f.ExecuteScopeAsync(async scope=>await Assert.ThrowsAsync<ExecutionControlConflictException>(()=>scope.ServiceProvider.GetRequiredService<IAgentExecutionControlGate>().AdmitAsync(company,p.AgentId,"tool","controlled-original",default)));
    }
    [Fact]
    public async Task Member_cannot_manage_foreign_agent_and_company_reads_writes_are_isolated()
    {
        using var f=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();await BusinessWorkEvidenceIntegrationTests.SeedOwner(f,company,owner);await AuthorityExplanationFixture.SeedAsync(f,company,owner);
        using var c=BusinessWorkEvidenceIntegrationTests.Client(f);var before=(await c.GetFromJsonAsync<ExecutionControlView>(Route(company)))!;
        using var foreign=await c.GetAsync(Route(Guid.NewGuid()));Assert.False(foreign.IsSuccessStatusCode);
        var bad=new ExecutionControlChange(Guid.NewGuid(),Guid.NewGuid(),true,0,before.CompanyVersion,"Foreign agent");using var cross=await c.PostAsJsonAsync(Route(company)+"/preview",bad);Assert.Equal(HttpStatusCode.NotFound,cross.StatusCode);
        await f.SeedAsync(async db=>{var membership=await db.CompanyMemberships.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company&&x.UserId==owner);db.Entry(membership).Property(x=>x.Role).CurrentValue=CompanyMembershipRole.Employee;});
        using var denied=await c.PostAsJsonAsync(Route(company)+"/preview",bad with{AgentId=null});Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        await f.SeedAsync(async db=>Assert.Empty(await db.AgentExecutionControls.IgnoreQueryFilters().ToListAsync()));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void Expired_running_leases_and_uncertain_work_cannot_be_reclaimed_or_resumed(bool running)
    {
        var row=new OperatingDispatch(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),OperatingDispatchKind.SingleAgent,"p16");var now=DateTime.UtcNow;
        Assert.True(row.TryClaim("one",now,TimeSpan.FromSeconds(1)));row.Start("one",now);
        if(!running)row.MarkUncertain("Ambiguous provider outcome",now);
        Assert.False(row.TryClaim("two",now.AddSeconds(2),TimeSpan.FromSeconds(10)));Assert.Equal(OperatingDispatchStatus.Uncertain,row.Status);row.ResumePaused(now);Assert.Equal(OperatingDispatchStatus.Uncertain,row.Status);
    }
    private static string Route(Guid company)=>$"/api/companies/{company}/execution-controls";
    private static async Task<ExecutionControlPreview> Preview(HttpClient c,Guid company,ExecutionControlChange change){using var response=await c.PostAsJsonAsync(Route(company)+"/preview",change);Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());return (await response.Content.ReadFromJsonAsync<ExecutionControlPreview>())!;}
    private static async Task<ExecutionControlView> Apply(HttpClient c,Guid company,ExecutionControlApply apply){using var response=await c.PostAsJsonAsync(Route(company)+"/apply",apply);Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());return (await response.Content.ReadFromJsonAsync<ExecutionControlView>())!;}
}
