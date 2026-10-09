using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class MultiAgentHandoffRetryTests
{
    [Fact]
    public void Durable_execution_claim_only_recovers_after_expiry()
    {
        var now=DateTime.UtcNow;var lease=new CollaborationExecutionLease(Guid.NewGuid(),"retry",new string('a',64));
        var first=Guid.NewGuid();var second=Guid.NewGuid();Assert.True(lease.TryClaim(first,now,TimeSpan.FromSeconds(45)));
        Assert.False(lease.TryClaim(second,now.AddSeconds(44),TimeSpan.FromSeconds(45)));
        Assert.Equal(first,lease.Token);Assert.True(lease.TryClaim(second,now.AddSeconds(46),TimeSpan.FromSeconds(45)));Assert.Equal(second,lease.Token);
    }
    [Fact]
    public async Task Failed_sequential_input_blocks_receiver_and_retry_versions_same_tasks_without_duplicate_success()
    {
        using var factory = new ControlledFactory(); var command = await Seed(factory); factory.State.Failures = 1;
        using var client = CollaborationEvidenceIntegrationTests.Client(factory); var route = $"/api/companies/{command.CompanyId}/tasks/manager-worker-collaborations";
        var firstResponse = await client.PostAsJsonAsync(route,command); firstResponse.EnsureSuccessStatusCode();
        var first = (await firstResponse.Content.ReadFromJsonAsync<MultiAgentCollaborationResultDto>())!;
        Assert.Equal(1,factory.State.Calls); Assert.Contains(first.Steps,x=>x.Status=="blocked");
        Assert.DoesNotContain("sensitive-provider-payload",await firstResponse.Content.ReadAsStringAsync());
        var nextResponse = await client.PostAsJsonAsync(route,command); nextResponse.EnsureSuccessStatusCode();
        var next=(await nextResponse.Content.ReadFromJsonAsync<MultiAgentCollaborationResultDto>())!;
        Assert.Equal(first.ParentTaskId,next.ParentTaskId); Assert.Equal("completed",next.Status); Assert.Equal(3,factory.State.Calls);
        (await client.PostAsJsonAsync(route,command)).EnsureSuccessStatusCode(); Assert.Equal(3,factory.State.Calls);
        await factory.SeedAsync(async db =>
        {
            var rows=await db.CollaborationContributions.IgnoreQueryFilters().Where(x=>x.ParentTaskId==first.ParentTaskId).OrderBy(x=>x.Version).ToListAsync();
            Assert.Equal(4,rows.Count); Assert.Equal(2,rows.Max(x=>x.Version));
            Assert.Equal(2,rows.Count(x=>x.Status=="completed"));
            var blockedId=rows.Single(x=>x.Status=="blocked").Id;
            Assert.True(await db.CollaborationArtifactHandoffs.IgnoreQueryFilters().AnyAsync(x=>x.ReceivingContributionId==blockedId && !x.Passed));
            Assert.Equal(3,await db.WorkTasks.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==command.CompanyId));
        });
    }
    [Fact]
    public async Task Overlapping_retry_is_conflict_and_changed_plan_cannot_reuse_execution_identity()
    {
        using var factory = new ControlledFactory(); var command=await Seed(factory); factory.State.Block=true;
        using var client=CollaborationEvidenceIntegrationTests.Client(factory); var route=$"/api/companies/{command.CompanyId}/tasks/manager-worker-collaborations";
        var running=client.PostAsJsonAsync(route,command);
        await factory.State.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        try { Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(route,command)).StatusCode); }
        finally { factory.State.Release.TrySetResult(); }
        (await running).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(route,command with {Objective="A materially different proposal"})).StatusCode);
        Assert.Equal(2,factory.State.Calls);
    }
    private static async Task<StartMultiAgentCollaborationCommand> Seed(ControlledFactory factory)
    {
        var company=Guid.NewGuid();var human=Guid.NewGuid();await CollaborationEvidenceIntegrationTests.SeedOwner(factory,company,human);
        var manager=Guid.NewGuid();var finance=Guid.NewGuid();var support=Guid.NewGuid();
        await factory.SeedAsync(db=>{Agent Agent(Guid id,string area)=>new(id,company,area,area,area,area,null,AgentSeniority.Senior,AgentStatus.Active);
            db.AddRange(Agent(manager,"Sales"),Agent(finance,"Finance"),Agent(support,"Support"));return Task.CompletedTask;});
        return new(company,"Renewal contributions",manager,[new(finance,"Margin evidence"),new(support,"Review terms",Pattern:"sequential_handoff",Role:"reviewer")],human,"user",CorrelationId:"p11-controlled");
    }
    private sealed class Control
    { public int Failures,Calls;public bool Block,NeedsReview;public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously); }
    private sealed class ControlledFactory:TestWebApplicationFactory
    {
        public Control State { get; }=new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        { base.ConfigureWebHost(builder);builder.ConfigureTestServices(services=>{services.RemoveAll<ISingleAgentOrchestrationService>();services.AddScoped<ISingleAgentOrchestrationService>(sp=>new ControlledSingle(sp.GetRequiredService<ICompanyTaskCommandService>(),State));}); }
    }
    private sealed class ControlledSingle(ICompanyTaskCommandService tasks,Control state):ISingleAgentOrchestrationService
    {
        public async Task<OrchestrationResult> ExecuteAsync(SingleAgentOrchestrationRequest request,CancellationToken ct)
        {
            var call=Interlocked.Increment(ref state.Calls);
            if(state.Block && call==1) {state.Entered.TrySetResult();await state.Release.Task.WaitAsync(ct);}
            if(Interlocked.Exchange(ref state.Failures,0)>0)throw new InvalidOperationException("sensitive-provider-payload");
            var status=state.NeedsReview && call==1 ? "awaiting_approval" : "completed";
            await tasks.UpdateStatusAsync(request.CompanyId,request.TaskId!.Value,new(status,new(),"Recorded business conclusion",1m),ct);
            var run=Guid.NewGuid();var now=DateTime.UtcNow;
            return new(run,request.CompanyId,request.TaskId.Value,request.AgentId!.Value,status,"Recorded business output",new(),"Recorded business conclusion",1m,[],[],
                new(run,Guid.NewGuid(),request.CompanyId,request.TaskId.Value,request.AgentId.Value,request.CorrelationId!,"test",now,now,new Dictionary<string,string?>()),request.CorrelationId!);
        }
        public Task<OrchestrationResult> ExecuteAsync(OrchestrationRequest request,CancellationToken ct)=>throw new NotSupportedException();
    }
    [Fact]
    public async Task Human_review_blocks_handoff_and_never_becomes_completed_or_reexecuted_on_retry()
    {
        using var factory=new ControlledFactory();var command=await Seed(factory);factory.State.NeedsReview=true;
        using var client=CollaborationEvidenceIntegrationTests.Client(factory);var route=$"/api/companies/{command.CompanyId}/tasks/manager-worker-collaborations";
        var response=await client.PostAsJsonAsync(route,command);response.EnsureSuccessStatusCode();
        var result=(await response.Content.ReadFromJsonAsync<MultiAgentCollaborationResultDto>())!;
        Assert.NotEqual("completed",result.Status);Assert.All(result.Steps,x=>Assert.NotEqual("completed",x.Status));
        var task=(await client.GetFromJsonAsync<TaskDetailDto>($"/api/companies/{command.CompanyId}/tasks/{result.ParentTaskId}"))!;
        Assert.Equal("awaiting_approval",task.Status);Assert.Equal(1,factory.State.Calls);
        (await client.PostAsJsonAsync(route,command)).EnsureSuccessStatusCode();Assert.Equal(1,factory.State.Calls);
    }
}
