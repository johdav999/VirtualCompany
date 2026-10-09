using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class AuthorityExplanationIntegrationTests
{
    [Fact]
    public async Task Scoped_settings_do_not_reveal_hidden_agents_or_authorize_reader_execution()
    {
        using var factory=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(factory,company,owner);var f=await AuthorityExplanationFixture.SeedAsync(factory,company,owner);
        await factory.SeedAsync(db=> {
            foreach(var (subject,role,area) in new[]{("p14-sales",CompanyMembershipRole.Manager,ResponsibilityArea.Sales),("p14-finance-reader",CompanyMembershipRole.FinanceApprover,ResponsibilityArea.CashAndAccounting)}) {
                var user=Guid.NewGuid();var membership=Guid.NewGuid();db.AddRange(new User(user,subject+"@example.com",subject,"dev-header",subject),
                    new CompanyMembership(membership,company,user,role,CompanyMembershipStatus.Active),
                    new CompanyResponsibilityAssignment(Guid.NewGuid(),company,area,ResponsibilityAssignmentKind.Primary,membership,null,AgentAutonomyLevel.Level1,null,null)); }
            return Task.CompletedTask;
        });
        using var sales=factory.CreateClient();sales.DefaultRequestHeaders.Add("X-Dev-Auth-Subject","p14-sales");
        Assert.Equal(HttpStatusCode.NotFound,(await sales.GetAsync(Route(f,false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await sales.GetAsync(Route(f,true))).StatusCode);
        var list=(await sales.GetFromJsonAsync<AuthorityExplanationDto>($"/api/companies/{company}/authority-explanation"))!;
        Assert.DoesNotContain(list.AvailableAgents,x=>x.Id==f.AgentId);Assert.Empty(list.Agents);
        using var reader=factory.CreateClient();reader.DefaultRequestHeaders.Add("X-Dev-Auth-Subject","p14-finance-reader");
        var result=(await reader.GetFromJsonAsync<AuthorityExplanationDto>(Route(f,false)))!;
        Assert.Contains(result.Agents.Single().Actions,x=>x.ToolName=="get_cash_balance"&&x.Check.State=="available");
        Assert.Contains(result.Agents.Single().Actions,x=>x.ToolName=="categorize_transaction"&&x.Check.ReasonCode==FinanceAgentAuthorizationReasonCodes.PermissionMissing);
    }
    [Fact]
    public async Task Evaluation_failure_is_diagnostic_and_establishes_no_permission()
    {
        using var factory=new FailureFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(factory,company,owner);var f=await AuthorityExplanationFixture.SeedAsync(factory,company,owner);
        using var client=Client(factory);var result=(await client.GetFromJsonAsync<AuthorityExplanationDto>(Route(f,false)))!;
        Assert.Single(result.Diagnostics);var action=Assert.Single(result.Agents.Single().Actions);
        Assert.Equal("evaluation_failed",action.Check.State);Assert.Contains("no permission",action.Check.Explanation);
    }
    [Theory]
    [InlineData(CompanyAutonomyLevel.Recommend,AgentAutonomyLevel.Level2,false,"internal_operation_not_enabled")]
    [InlineData(CompanyAutonomyLevel.Organize,AgentAutonomyLevel.Level2,false,"internal_operation_not_enabled")]
    [InlineData(CompanyAutonomyLevel.OperateInternally,AgentAutonomyLevel.Level1,false,"agent_autonomy_insufficient")]
    [InlineData(CompanyAutonomyLevel.OperateInternally,AgentAutonomyLevel.Level2,false,"within_autonomy")]
    [InlineData(CompanyAutonomyLevel.ControlledExecution,AgentAutonomyLevel.Level2,false,"within_autonomy")]
    [InlineData(CompanyAutonomyLevel.ControlledExecution,AgentAutonomyLevel.Level2,true,"action_outside_autonomy")]
    public async Task Company_agent_task_matrix_uses_owning_dispatch_policy(CompanyAutonomyLevel companyLevel, AgentAutonomyLevel agentLevel,bool external,string reason)
    {
        using var factory=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(factory,company,owner);
        var f=await AuthorityExplanationFixture.SeedAsync(factory,company,owner,companyLevel,agentLevel,external);
        using var client=Client(factory);using var response=await client.GetAsync(Route(f,true));
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        var result=(await response.Content.ReadFromJsonAsync<AuthorityExplanationDto>())!;
        Assert.Equal(companyLevel.ToStorageValue(),result.CompanyIntent);Assert.Equal(reason,result.TaskPolicy.ReasonCode);
        Assert.Equal("finance_review",result.TaskType);Assert.Contains(result.CompanyLimits,x=>x.Label=="Mandatory review"&&x.Value.Contains("external"));
        var agent=Assert.Single(result.Agents);Assert.Equal(agentLevel.ToStorageValue(),agent.ProfileLevel);
        Assert.Contains(agent.Actions,x=>x.ToolName=="list_transactions"&&x.Check.State==AgentCapabilityStates.IntegrationUnavailable);
        Assert.Contains(agent.Actions,x=>x.ToolName=="finance.removed_tool"&&x.Check.State==AgentCapabilityStates.NotImplemented);
        Assert.True(response.Headers.CacheControl!.NoStore);Assert.Empty(result.Diagnostics);
        await factory.SeedAsync(async db=> { Assert.False(await db.ToolExecutionAttempts.IgnoreQueryFilters().AnyAsync()); Assert.False(await db.ApprovalRequests.IgnoreQueryFilters().AnyAsync()); });
    }
    [Fact]
    public async Task Settings_and_work_show_same_checks_and_refresh_observes_configuration_and_expiry()
    {
        var clock=new Clock();using var factory=new TestWebApplicationFactory(clock);var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(factory,company,owner);var f=await AuthorityExplanationFixture.SeedAsync(factory,company,owner);
        using var client=Client(factory);
        var definition=new FinanceAutonomyGrantDefinition(f.AgentId,FinanceAgentCoverageCapabilityIds.DailyCash,FinanceAutonomyLevels.ReadMonitor,
            [FinanceAutonomyTriggers.ManualReview],["read"],["get_cash_balance"],10,100,1,null,"UTC","00:00","23:59",60,
            FinanceAutonomyConfirmationBehaviors.NoConfirmation,"company_owner",clock.Now.AddMinutes(-1),clock.Now.AddMinutes(5));
        using var created=await client.PostAsJsonAsync($"/api/companies/{company}/finance/autonomy/grants",new CreateFinanceAutonomyGrantCommand(definition));
        Assert.True(created.IsSuccessStatusCode,await created.Content.ReadAsStringAsync());var grant=(await created.Content.ReadFromJsonAsync<FinanceAutonomyGrantDto>())!;
        using var activated=await client.PostAsJsonAsync($"/api/companies/{company}/finance/autonomy/grants/{grant.Id}/versions/{grant.Versions.Single().Id}/activate",new ActivateFinanceAutonomyGrantVersionCommand(grant.Version,"Reviewed fixture"));
        Assert.True(activated.IsSuccessStatusCode,await activated.Content.ReadAsStringAsync());
        var before=(await client.GetFromJsonAsync<AuthorityExplanationDto>(Route(f,true)))!;
        var settings=(await client.GetFromJsonAsync<AuthorityExplanationDto>(Route(f,false)))!;
        Assert.Equal(before.Agents.Single().Actions,settings.Agents.Single().Actions);
        Assert.Equal(FinanceAutonomyDecisionReasonCodes.EvidenceStale,before.Agents.Single().Grants.Single().Check.ReasonCode);
        var repeated=(await client.GetFromJsonAsync<AuthorityExplanationDto>(Route(f,true)))!;Assert.Equal(before.ProjectionHash,repeated.ProjectionHash);
        clock.Now=clock.Now.AddMinutes(6);
        var expired=(await client.GetFromJsonAsync<AuthorityExplanationDto>(Route(f,true)))!;
        Assert.Equal(FinanceAutonomyDecisionReasonCodes.GrantExpired,expired.Agents.Single().Grants.Single().Check.ReasonCode);
        Assert.NotEqual(before.ProjectionHash,expired.ProjectionHash);
        await factory.SeedAsync(async db=> (await db.CompanyOperatingConfigurations.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company)).Pause("Owner paused work"));
        var paused=(await client.GetFromJsonAsync<AuthorityExplanationDto>(Route(f,true)))!;
        Assert.Equal("operation_paused",paused.TaskPolicy.ReasonCode);Assert.NotEqual(expired.ProjectionHash,paused.ProjectionHash);
    }
    [Fact]
    public async Task Hidden_foreign_agents_and_work_are_unavailable_and_read_endpoint_has_no_mutation()
    {
        using var factory=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(factory,company,owner);var f=await AuthorityExplanationFixture.SeedAsync(factory,company,owner);
        using var client=Client(factory);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/companies/{company}/authority-explanation?agentId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(Route(f with { CompanyId=Guid.NewGuid() },false))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(Route(f with {TaskId=Guid.NewGuid()},true))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync($"/api/companies/{company}/authority-explanation?workId={f.TaskId}")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await client.PostAsync(Route(f,false),null)).StatusCode);
    }
    private static HttpClient Client(TestWebApplicationFactory factory) {var client=factory.CreateClient();client.DefaultRequestHeaders.Add("X-Dev-Auth-Subject","p13-owner");client.DefaultRequestHeaders.Add("X-Dev-Auth-Email","p13-owner@example.com");return client;}
    private static string Route(AuthorityFixtureProfile f,bool work)=>$"/api/companies/{f.CompanyId}/authority-explanation?agentId={f.AgentId}"+(work?$"&workKind=task&workId={f.TaskId}":"");
    private sealed class Clock:TimeProvider {public DateTime Now {get;set;}=DateTime.UtcNow;public override DateTimeOffset GetUtcNow()=>new(Now);}
    private sealed class FailureFactory:TestWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) {
            base.ConfigureWebHost(builder);builder.ConfigureServices(services=>services.Replace(ServiceDescriptor.Scoped<IAgentToolPolicyPreviewService,FailingPreview>())); }
    }
    private sealed class FailingPreview:IAgentToolPolicyPreviewService
    { public Task<AgentToolPolicyPreviewDto> PreviewAsync(Guid companyId,Guid agentId,CancellationToken token)=>throw new InvalidOperationException("Controlled policy-evaluation failure"); }
}
