using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

// Real queue, policies, dispatcher, adapter and Sales/Marketing/Support owners.
// Only the AI analysis response is deterministic; no customer provider is called.
public sealed class Release2NativeDraftFactory : ExecutionControlNativeFactory
{
    public bool MissingEvidence { get; set; }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISalesAgentAnalysisService>();
            services.RemoveAll<IMarketingAgentAnalysisService>();
            services.RemoveAll<IFinanceAgentAnalysisService>();
            var analysis = new Analysis(this);
            services.AddSingleton<ISalesAgentAnalysisService>(analysis);
            services.AddSingleton<IMarketingAgentAnalysisService>(analysis);
            services.AddSingleton<IFinanceAgentAnalysisService>(analysis);
            services.RemoveAll<ISupportOutboundEmailSender>();
            services.AddSingleton<ISupportOutboundEmailSender>(Delivery);
        });
    }
    public RecordingDelivery Delivery {get;}=new();
    public sealed class RecordingDelivery:ISupportOutboundEmailSender
    {
        public List<SupportOutboundEmailSendRequest> Calls {get;}=[];
        public Task<SupportOutboundEmailSendResult> SendReplyAsync(SupportOutboundEmailSendRequest request,CancellationToken ct)
        {if(request.ToEmail!="renewal-controlled@example.invalid")throw new InvalidOperationException("Controlled recipient guard.");Calls.Add(request);return Task.FromResult(new SupportOutboundEmailSendResult("controlled-test",Guid.NewGuid(),"p18-confirmed-reply",null,"sent"));}
    }
    private sealed class Analysis(Release2NativeDraftFactory factory) : ISalesAgentAnalysisService, IMarketingAgentAnalysisService, IFinanceAgentAnalysisService
    {
        public Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid company, Guid agent, Guid? actor,
            RoleAgentAnalysisRequest request, CancellationToken ct) => Task.FromResult(new RoleAgentAnalysisResult(
                Guid.NewGuid(), request.AnalysisType, factory.MissingEvidence ? "blocked" : "completed",
                factory.MissingEvidence ? "" : "Controlled analysis: retain the renewal terms for human review.", .8m,
                DateTime.UtcNow, [], [], factory.MissingEvidence ? [] : [new("The recorded renewal is under review.", "fact", .8m, ["controlled-source"])],
                factory.MissingEvidence ? [] : [new("controlled-source", "test", "Isolated test evidence", "The recorded renewal is under review.")],
                factory.MissingEvidence ? ["Approved evidence unavailable."] : [], [], true));
    }
}

public sealed class Release2PolicyIntegrationTests
{
    [Theory][InlineData("material")][InlineData("actor")][InlineData("uncertain")][InlineData("delegated")]
    public async Task Reviewed_outbox_rechecks_material_actor_and_never_replays_an_uncertain_admission(string condition)
    {
        using var f=new Release2NativeDraftFactory();var s=await Setup(f,"sales.proposal_drafting","deal");using var c=BusinessWorkEvidenceIntegrationTests.Client(f);
        await Apply(c,s.Company,await Preview(c,s.Company,new(s.Agent,"sales.proposal_drafting","review",2,DateTime.UtcNow.AddDays(1),"Mandatory review")));
        var task=await Queue(c,s.Company,s.Agent,"sales.proposal_drafting",s.Record,1);await Dispatch(f,s.Company);
        var approval=Assert.Single((await c.GetFromJsonAsync<List<ApprovalRequestDto>>($"/api/companies/{s.Company}/approvals"))!,x=>x.TargetEntityType=="action");
        var route=$"/api/companies/{s.Company}/approvals/{approval.Id}";approval=(await c.GetFromJsonAsync<ApprovalRequestDto>(route))!;
        var command=new ApprovalDecisionCommand(approval.Id,"approve",approval.CurrentStep!.Id,"Internal draft only",Guid.NewGuid(),approval.Review!.Token);
        (await c.PostAsJsonAsync(route+"/decisions",command)).EnsureSuccessStatusCode();
        Guid attemptId=Guid.Empty;
        await f.SeedAsync(async db=>{var attempt=await db.ToolExecutionAttempts.IgnoreQueryFilters().SingleAsync(x=>x.TaskId==task);attemptId=attempt.Id;if(condition=="material")attempt.RequestPayload["dealId"]=JsonValue.Create(Guid.NewGuid());if(condition=="actor")db.Entry(await db.CompanyMemberships.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==s.Company&&x.UserId==s.Owner)).Property(x=>x.Role).CurrentValue=CompanyMembershipRole.Employee;if(condition=="delegated"){var receiving=await db.Agents.IgnoreQueryFilters().Where(x=>x.CompanyId==s.Company&&x.Department=="Sales"&&x.Id!=s.Agent).Select(x=>x.Id).FirstAsync();(await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==task)).AssignTo(receiving);}});
        if(condition=="uncertain")await f.ExecuteScopeAsync(async scope=>{var gate=scope.ServiceProvider.GetRequiredService<IAgentExecutionControlGate>();var admitted=await gate.AdmitAsync(s.Company,s.Agent,"tool",attemptId.ToString("D"),default,task);await gate.AcknowledgeAsync(s.Company,admitted,false,default);});
        await f.ExecuteScopeAsync(async scope=>await scope.ServiceProvider.GetRequiredService<VirtualCompany.Infrastructure.Companies.ICompanyOutboxProcessor>().DispatchPendingAsync(default));
        await f.ExecuteScopeAsync(async scope=>await scope.ServiceProvider.GetRequiredService<VirtualCompany.Infrastructure.Companies.ICompanyOutboxProcessor>().DispatchPendingAsync(default));await Dispatch(f,s.Company);
        await f.SeedAsync(async db=>{Assert.Equal(condition=="uncertain"?ToolExecutionStatus.ReconciliationRequired:ToolExecutionStatus.Denied,(await db.ToolExecutionAttempts.IgnoreQueryFilters().SingleAsync(x=>x.TaskId==task)).Status);Assert.Equal(WorkTaskStatus.Blocked,(await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==task)).Status);Assert.Equal(condition=="uncertain"?OperatingDispatchStatus.Uncertain:OperatingDispatchStatus.Blocked,(await db.OperatingDispatches.IgnoreQueryFilters().SingleAsync(x=>x.TaskId==task)).Status);Assert.Equal(0,(await db.TaskTypePolicies.IgnoreQueryFilters().SingleAsync(x=>x.AgentId==s.Agent)).ActionsUsed);});
    }

    [Theory]
    [InlineData("sales.proposal_drafting","deal",true)][InlineData("marketing.content_drafting","brief",true)][InlineData("support.grounded_reply_drafting","case",true)]
    [InlineData("sales.proposal_drafting","deal",false)][InlineData("marketing.content_drafting","brief",false)][InlineData("support.grounded_reply_drafting","case",false)]
    public async Task Native_draft_review_requires_a_human_and_current_policy_before_committed_outbox_execution(string type,string kind,bool revoke)
    {
        using var f=new Release2NativeDraftFactory();var s=await Setup(f,type,kind);using var c=BusinessWorkEvidenceIntegrationTests.Client(f);
        var change=new TaskPolicyChange(s.Agent,type,"review",2,DateTime.UtcNow.AddDays(1),"Human review before internal drafting");
        await Apply(c,s.Company,await Preview(c,s.Company,change));var task=await Queue(c,s.Company,s.Agent,type,s.Record,1);await Dispatch(f,s.Company);
        var approvals=(await c.GetFromJsonAsync<List<ApprovalRequestDto>>($"/api/companies/{s.Company}/approvals"))!;
        var approval=Assert.Single(approvals.Where(x=>x.TargetEntityType=="action"));
        Assert.Equal("pending",approval.Status);
        if(revoke)await Apply(c,s.Company,await Preview(c,s.Company,change with {Mode="disabled",ExpectedVersion=1}));
        var route=$"/api/companies/{s.Company}/approvals/{approval.Id}";approval=(await c.GetFromJsonAsync<ApprovalRequestDto>(route))!;
        using var response=await c.PostAsJsonAsync(route+"/decisions",new ApprovalDecisionCommand(approval.Id,"approve",approval.CurrentStep!.Id,"Review does not override revocation",Guid.NewGuid(),approval.Review!.Token));
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        await f.ExecuteScopeAsync(async scope=>await scope.ServiceProvider.GetRequiredService<VirtualCompany.Infrastructure.Companies.ICompanyOutboxProcessor>().DispatchPendingAsync(default));
        await f.SeedAsync(async db=>{Assert.Equal(revoke?ToolExecutionStatus.Denied:ToolExecutionStatus.Executed,(await db.ToolExecutionAttempts.IgnoreQueryFilters().SingleAsync(x=>x.TaskId==task)).Status);Assert.Equal(revoke?WorkTaskStatus.Blocked:WorkTaskStatus.Completed,(await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==task)).Status);Assert.Equal(revoke?OperatingDispatchStatus.Blocked:OperatingDispatchStatus.Completed,(await db.OperatingDispatches.IgnoreQueryFilters().SingleAsync(x=>x.TaskId==task)).Status);});
    }

    [Fact]
    public async Task Finance_catalogue_retains_template_limits_review_exclusion_and_separately_governed_trigger()
    {
        using var f=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();await BusinessWorkEvidenceIntegrationTests.SeedOwner(f,company,owner);
        var basis=await AuthorityExplanationFixture.SeedAsync(f,company,owner);using var c=BusinessWorkEvidenceIntegrationTests.Client(f);
        var entry=TaskTypePolicyCatalogue.Find("finance.stale_cash_monitoring");Assert.True(entry.Finance);
        var change=new TaskPolicyChange(basis.AgentId,entry.Code,"review",1,DateTime.UtcNow.AddDays(1),"Finance remains bounded");
        var review=await Preview(c,company,change);Assert.False(review.CanApply);Assert.Equal("finance_template_review_unavailable",review.Check.ReasonCode);
        var disabled=await Preview(c,company,change with {Mode="disabled"});Assert.True(disabled.CanApply);await Apply(c,company,disabled);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync($"/api/companies/{company}/task-policies/queue",new QueueTaskPolicyWork(basis.AgentId,entry.Code,Guid.NewGuid(),0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync($"/api/companies/{company}/task-policies/preview",change with {Mode="automatic",MaximumActionsPerDay=2})).StatusCode);
        var automatic=await Preview(c,company,change with{Mode="automatic"});Assert.True(automatic.CanApply,automatic.Check.Explanation);
        var active=await Apply(c,company,automatic);Assert.NotNull(active.FinanceGrant!.ActiveVersionId);Assert.Equal("read_monitor",Assert.Single(active.History).Mode);Assert.Equal(1,active.History.Single().MaximumActionsPerDay);
        var revoked=await Apply(c,company,await Preview(c,company,change with{Mode="disabled",ExpectedVersion=active.Version}));Assert.Equal("disabled",Assert.Single(revoked.History).Mode);
    }

    [Theory]
    [InlineData("sales.proposal_drafting", "deal")]
    [InlineData("marketing.content_drafting", "brief")]
    [InlineData("support.grounded_reply_drafting", "case")]
    public async Task Each_draft_catalogue_type_previews_applies_queues_and_dispatches_to_its_real_owner(string type, string kind)
    {
        using var f = new Release2NativeDraftFactory();
        var s = await Setup(f, type, kind);
        using var client = BusinessWorkEvidenceIntegrationTests.Client(f);
        var change = new TaskPolicyChange(s.Agent, type, "automatic", 2, DateTime.UtcNow.AddDays(2), "Bounded internal renewal evidence", 0, s.Record);
        var preview = await Preview(client, s.Company, change);
        Assert.True(preview.CanApply, preview.Check.Explanation);
        await f.SeedAsync(async db =>
        {
            Assert.False(await db.TaskTypePolicies.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == s.Company && x.AgentId == s.Agent));
            Assert.False(await db.ToolExecutionAttempts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == s.Company));
        });
        var applied = await Apply(client, s.Company, preview);
        Assert.Equal(1, applied.Version); Assert.Equal("automatic", Assert.Single(applied.History).Mode);
        var id = await Queue(client, s.Company, s.Agent, type, s.Record, 1);
        await Dispatch(f, s.Company);
        await f.SeedAsync(async db =>
        {
            var attempt = await db.ToolExecutionAttempts.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == s.Company && x.TaskId == id);
            Assert.True(attempt.Status == ToolExecutionStatus.Executed, $"{attempt.Status}: {System.Text.Json.JsonSerializer.Serialize(attempt.ResultPayload)}; {f.LastError}");
            var task = await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
            Assert.Equal(WorkTaskStatus.Completed, task.Status); Assert.NotEmpty(task.OutputPayload);
            Assert.Equal(1, (await db.TaskTypePolicies.IgnoreQueryFilters().SingleAsync(x => x.AgentId == s.Agent)).ActionsUsed);
            if(kind == "brief") Assert.Contains(await db.MarketingContentVariants.IgnoreQueryFilters().Where(x => x.CompanyId == s.Company).ToListAsync(), x => x.GeneratedByAi && x.ContentFormat == "sales_enablement");
            if(kind == "case") Assert.True(await db.SupportReplyDrafts.IgnoreQueryFilters().CountAsync(x => x.SupportCaseId == s.Record) > 1);
        });
        var revoked = await Apply(client, s.Company, await Preview(client, s.Company, change with { Mode = "disabled", ExpectedVersion = 1 }));
        Assert.Equal(2, revoked.Version); Assert.Equal(2, revoked.History.Count);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/companies/{s.Company}/task-policies/queue", new QueueTaskPolicyWork(s.Agent, type, s.Record, 2))).StatusCode);
    }

    [Theory]
    [InlineData("sales.proposal_drafting", "deal")]
    [InlineData("marketing.content_drafting", "brief")]
    public async Task Missing_owner_output_is_failed_and_never_counted_as_prepared(string type, string kind)
    {
        using var f = new Release2NativeDraftFactory { MissingEvidence = true }; var s = await Setup(f, type, kind);
        using var c = BusinessWorkEvidenceIntegrationTests.Client(f);
        await Apply(c, s.Company, await Preview(c, s.Company, new(s.Agent, type, "automatic", 2, DateTime.UtcNow.AddDays(1), "Evidence is required")));
        var id = await Queue(c, s.Company, s.Agent, type, s.Record, 1); await Dispatch(f, s.Company);
        await f.SeedAsync(async db => Assert.Equal(ToolExecutionStatus.Failed, (await db.ToolExecutionAttempts.IgnoreQueryFilters().SingleAsync(x => x.TaskId == id)).Status));
        var report = (await c.GetFromJsonAsync<AgentSupervisionReport>($"/api/companies/{s.Company}/agent-supervision?agentId={s.Agent}"))!;
        Assert.Equal(0, report.Measures.Single(x => x.Code == "prepared_output").Count);
    }

    [Theory]
    [InlineData("review")][InlineData("changed")][InlineData("delegated")][InlineData("budget")]
    public async Task Research_dispatch_preserves_review_current_version_delegation_and_budget_controls(string scenario)
    {
        using var f = new ExecutionControlNativeFactory(); var company = Guid.NewGuid(); var owner = Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(f, company, owner); var p = await ExecutionControlFixture.SeedAsync(f, company, owner);
        using var c = BusinessWorkEvidenceIntegrationTests.Client(f);
        if(scenario is "review" or "changed" or "budget")
        {
            var preview = await Preview(c, company, new(p.AgentId, "sales.account_research", scenario == "review" ? "review" : "automatic", 1, DateTime.UtcNow.AddDays(1), "Recheck after queueing", 1));
            await Apply(c, company, preview);
            if(scenario != "changed")
            {
                // The first task retains version 1. Newly queued work binds version 2.
                await f.SeedAsync(async db => db.RemoveRange(await db.OperatingDispatches.IgnoreQueryFilters().Where(x => x.CompanyId == company).ToListAsync()));
                var goalId=Guid.NewGuid();
                await f.SeedAsync(db=>{var goal=new CompanyGoal(goalId,company,"Separately reviewed work","New bounded policy work.",CompanyGoalPriority.Normal,DateTime.UtcNow.AddDays(-1),DateTime.UtcNow.AddDays(2),ownerAgentId:p.AgentId);goal.Activate();db.Add(goal);return Task.CompletedTask;});
                await Queue(c, company, p.AgentId, "sales.account_research", p.RecordId, 2, goalId);
                if(scenario == "budget") await f.SeedAsync(async db => { var policy = await db.TaskTypePolicies.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == company && x.AgentId == p.AgentId); Assert.True(policy.Reserve(DateTime.UtcNow, 1)); });
            }
        }
        if(scenario == "delegated") await f.SeedAsync(async db => (await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == p.TaskId)).AssignTo(p.SecondAgentId));
        await Dispatch(f, company);
        await f.SeedAsync(async db =>
        {
            var attempts = await db.ToolExecutionAttempts.IgnoreQueryFilters().Where(x => x.CompanyId == company).ToListAsync();
            var dispatch = await db.OperatingDispatches.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == company);
            if(scenario=="delegated"){Assert.Equal(OperatingDispatchStatus.Blocked,dispatch.Status);Assert.Empty(attempts);return;}
            Assert.True(attempts.Count == 1, $"{dispatch.Status}: {dispatch.FailureCode} {dispatch.FailureSummary}; {f.LastError}");
            var attempt = Assert.Single(attempts);
            Assert.Equal(scenario == "review" ? ToolExecutionStatus.AwaitingApproval : ToolExecutionStatus.Denied, attempt.Status);
            Assert.False(attempt.Status == ToolExecutionStatus.Executed);
            if(scenario == "review") Assert.Single(await db.ApprovalRequests.IgnoreQueryFilters().Where(x => x.CompanyId == company).ToListAsync());
        });
        if(scenario=="review")
        {
            var approval=Assert.Single((await c.GetFromJsonAsync<List<ApprovalRequestDto>>($"/api/companies/{company}/approvals"))!);
            var route=$"/api/companies/{company}/approvals/{approval.Id}";
            approval=(await c.GetFromJsonAsync<ApprovalRequestDto>(route))!;
            var command=new ApprovalDecisionCommand(approval.Id,"approve",approval.CurrentStep!.Id,"Reviewed internal research only.",Guid.NewGuid(),approval.Review!.Token);
            using var decision=await c.PostAsJsonAsync(route+"/decisions",command);Assert.True(decision.IsSuccessStatusCode,await decision.Content.ReadAsStringAsync());
            (await c.PostAsJsonAsync(route+"/decisions",command)).EnsureSuccessStatusCode();
            var queued=(await c.GetFromJsonAsync<ApprovalRequestDto>(route))!;Assert.Equal("Queued for internal execution",queued.Review!.ExecutionStatus);
            Guid queuedTask=Guid.Empty;
            await f.SeedAsync(async db=>queuedTask=(await db.ToolExecutionAttempts.IgnoreQueryFilters().SingleAsync(x=>x.Id==queued.TargetEntityId)).TaskId!.Value);
            var queuedWork=(await c.GetFromJsonAsync<AgentWorkItemDto>($"/api/companies/{company}/agent-work/task/{queuedTask}"))!;
            Assert.Equal(AgentWorkStates.Planned,queuedWork.State);Assert.Equal("Queued for internal execution",queuedWork.CurrentStep);Assert.Contains(queuedWork.RelatedRecords,x=>x.Route.Contains(approval.Id.ToString()));
            await SetPause(c,company,p.AgentId,true);
            await f.ExecuteScopeAsync(async scope=>await scope.ServiceProvider.GetRequiredService<VirtualCompany.Infrastructure.Companies.ICompanyOutboxProcessor>().DispatchPendingAsync(default));
            await f.SeedAsync(async db=>{var outbox=await db.CompanyOutboxMessages.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company&&x.Topic=="task_policy.reviewed_execution_requested");Assert.Equal(0,outbox.AttemptCount);Assert.Equal(ToolExecutionStatus.AwaitingApproval,(await db.ToolExecutionAttempts.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company)).Status);});
            await SetPause(c,company,p.AgentId,false);
            await f.SeedAsync(async db=>db.Entry(await db.CompanyOutboxMessages.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company&&x.Topic=="task_policy.reviewed_execution_requested")).Property(x=>x.AvailableUtc).CurrentValue=DateTime.UtcNow.AddSeconds(-1));
            await f.ExecuteScopeAsync(async scope=>await scope.ServiceProvider.GetRequiredService<VirtualCompany.Infrastructure.Companies.ICompanyOutboxProcessor>().DispatchPendingAsync(default));
            await Dispatch(f,company);
            await f.SeedAsync(async db=>{var attempt=await db.ToolExecutionAttempts.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company);Assert.Equal(ToolExecutionStatus.Executed,attempt.Status);Assert.Equal(WorkTaskStatus.Completed,(await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==attempt.TaskId)).Status);Assert.Equal(OperatingDispatchStatus.Completed,(await db.OperatingDispatches.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company)).Status);Assert.Equal(1,(await db.TaskTypePolicies.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company&&x.AgentId==p.AgentId)).ActionsUsed);});
        }
    }

    [Fact]
    public async Task Preview_is_bound_to_current_version_actor_and_company_and_cannot_overwrite_an_activation()
    {
        using var f = new ExecutionControlNativeFactory(); var company = Guid.NewGuid(); var owner = Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(f, company, owner); var p = await ExecutionControlFixture.SeedAsync(f, company, owner);
        using var c = BusinessWorkEvidenceIntegrationTests.Client(f); var change = new TaskPolicyChange(p.AgentId, "sales.account_research", "automatic", 2, DateTime.UtcNow.AddDays(1), "Reviewed settings", 1);
        var first = await Preview(c, company, change); var second = await Preview(c, company, change with { MaximumActionsPerDay = 3 });
        await Apply(c, company, first);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync($"/api/companies/{company}/task-policies/apply", new TaskPolicyApply(second.Change, second.PreviewHash))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync($"/api/companies/{company}/task-policies/preview", change with { AgentId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync($"/api/companies/{Guid.NewGuid()}/task-policies/preview", change)).StatusCode);
        var employee = Guid.NewGuid(); await f.SeedAsync(db => { db.AddRange(new User(employee, "p18-employee@example.test", "Employee", "dev-header", "p18-employee"), new CompanyMembership(Guid.NewGuid(), company, employee, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active)); return Task.CompletedTask; });
        using var denied = BusinessWorkEvidenceIntegrationTests.Client(f, "p18-employee");
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsJsonAsync($"/api/companies/{company}/task-policies/preview", change)).StatusCode);
    }

    internal static async Task<(Guid Company, Guid Owner, Guid Agent, Guid Record)> Setup(Release2NativeDraftFactory f, string type, string kind)
    {
        var company = Guid.NewGuid(); var owner = Guid.NewGuid(); await BusinessWorkEvidenceIntegrationTests.SeedOwner(f, company, owner);
        await ExecutionControlFixture.SeedAsync(f, company, owner);
        var business = await BusinessEvidenceFixture.SeedAsync(f, company, owner); var agent = Guid.NewGuid(); var entry = TaskTypePolicyCatalogue.Find(type);
        await f.SeedAsync(async db =>
        {
            var membership=await db.CompanyMemberships.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company&&x.UserId==owner);
            db.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(),company,ResponsibilityArea.CompanyPerformance,ResponsibilityAssignmentKind.ExecutiveOversight,membership.Id,null,AgentAutonomyLevel.Level1,null,null));
            // Isolate this type's actual dispatch from the already tested research fixture.
            db.RemoveRange(await db.OperatingDispatches.IgnoreQueryFilters().Where(x => x.CompanyId == company).ToListAsync());
            db.Add(new Agent(agent, company, "p18-" + agent.ToString("N"), "P18 " + entry.Department, entry.Department + " specialist", entry.Department, null, AgentSeniority.Senior, AgentStatus.Active, AgentAutonomyLevel.Level2,
                tools: new Dictionary<string, JsonNode?> { ["allowed"] = new JsonArray(entry.ToolName), ["actions"] = new JsonArray("recommend") },
                scopes: new Dictionary<string, JsonNode?> { ["recommend"] = new JsonArray(entry.Department.ToLowerInvariant()), ["responsibilityPolicy"] = new JsonObject { ["allowedDomains"] = new JsonArray(entry.Department.ToLowerInvariant()), ["deniedDomains"] = new JsonArray() } },
                escalationRules: new Dictionary<string, JsonNode?> { ["escalateTo"] = JsonValue.Create("company_owner") }));
            var goal = new CompanyGoal(Guid.NewGuid(), company, "P18 internal renewal work", "Prepare retained evidence only.", CompanyGoalPriority.Normal, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(5), ownerAgentId: agent); goal.Activate(); db.Add(goal);
        });
        return (company, owner, agent, business.Records[kind]);
    }
    internal static async Task<TaskPolicyPreview> Preview(HttpClient c, Guid company, TaskPolicyChange change)
    { using var r = await c.PostAsJsonAsync($"/api/companies/{company}/task-policies/preview", change); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<TaskPolicyPreview>())!; }
    internal static async Task<TaskPolicyView> Apply(HttpClient c, Guid company, TaskPolicyPreview preview)
    { using var r = await c.PostAsJsonAsync($"/api/companies/{company}/task-policies/apply", new TaskPolicyApply(preview.Change, preview.PreviewHash)); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<TaskPolicyView>())!; }
    internal static async Task<Guid> Queue(HttpClient c, Guid company, Guid agent, string type, Guid record, int version, Guid? goal=null)
    { using var r = await c.PostAsJsonAsync($"/api/companies/{company}/task-policies/queue", new QueueTaskPolicyWork(agent, type, record, version, goal)); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Content.ReadFromJsonAsync<Guid>(); }
    internal static Task Dispatch(TestWebApplicationFactory f, Guid company) => f.ExecuteScopeAsync(async scope => await scope.ServiceProvider.GetRequiredService<IOperatingWorkDispatcher>().RunOnceAsync(25, default));
    internal static async Task SetPause(HttpClient c,Guid company,Guid agent,bool paused)
    {var route=$"/api/companies/{company}/execution-controls";var view=(await c.GetFromJsonAsync<ExecutionControlView>(route+$"?agentId={agent}"))!;var change=new ExecutionControlChange(Guid.NewGuid(),agent,paused,view.Version,view.CompanyVersion,"Review current internal work");using var p=await c.PostAsJsonAsync(route+"/preview",change);p.EnsureSuccessStatusCode();var preview=(await p.Content.ReadFromJsonAsync<ExecutionControlPreview>())!;(await c.PostAsJsonAsync(route+"/apply",new ExecutionControlApply(change,preview.PreviewHash))).EnsureSuccessStatusCode();}
}
